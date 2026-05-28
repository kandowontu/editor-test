import re, os
T=os.environ['TEMP']
nes=os.path.join(T,'famidash_mesen_physics_debug_dorabaebasic6_20260525_193155_739.log')
orb=os.path.join(T,'famidash_pf_orb_debug_dorabaebasic6_20260525_192426_022.log')

# Per sim: capture LAST cpvy (end-of-frame state) AND first ufo/ship_movement.in cpvy (start-of-frame state)
nesStart = {}; nesEnd = {}
curSim = None
re_sim = re.compile(r'^F=\d+ sim=(\d+)')
re_mv  = re.compile(r'(?:ufo|ship)_movement\.in.* cpx=([0-9A-Fa-f]{4}) cpy=([0-9A-Fa-f]{4}).*cpvx=(-?\d+) cpvy=(-?\d+)')
re_any = re.compile(r'\.(?:in|out).* cpx=([0-9A-Fa-f]{4}) cpy=([0-9A-Fa-f]{4}).*cpvx=(-?\d+) cpvy=(-?\d+)')

with open(nes,'r',encoding='utf-8',errors='ignore') as f:
    for ln in f:
        m = re_sim.match(ln)
        if m:
            curSim = int(m.group(1)); continue
        if curSim is None: continue
        m = re_mv.search(ln)
        if m and curSim not in nesStart:
            nesStart[curSim] = (int(m.group(1),16), int(m.group(2),16), int(m.group(3)), int(m.group(4)))
        m2 = re_any.search(ln)
        if m2:
            nesEnd[curSim] = (int(m2.group(1),16), int(m2.group(2),16), int(m2.group(3)), int(m2.group(4)))

# PF orb_debug ENTER is start-of-frame state
re_pf = re.compile(r'f=(\d+) ENTER mode=(\d+).*X_fixed=0x([0-9A-Fa-f]+) Y_fixed=0x([0-9A-Fa-f]+) VelY_fixed=0x([0-9A-Fa-f]+)')
pf = {}
with open(orb,'r',encoding='utf-8',errors='ignore') as f:
    for ln in f:
        m = re_pf.search(ln)
        if m:
            fr = int(m.group(1)); mode = int(m.group(2))
            x = int(m.group(3),16) & 0xFFFFFF
            y = int(m.group(4),16) & 0xFFFFFF
            vy_h = m.group(5); vy = int(vy_h,16)
            if vy >= 0x80000000: vy -= 0x100000000
            if fr not in pf:
                pf[fr] = (mode, x, y, vy)

# Try PF f=N ↔ NES sim=N+1
# Walk from f=2150 onward, compare Vy
print('f  | PFmode  PFVy  | NES sim  NES_cpvy  | match?')
mismatch_count = 0
first_mismatch = None
for fr in range(2150, min(max(pf), max(nesStart)-1)):
    sim = fr + 1
    if fr in pf and sim in nesStart:
        pf_vy = pf[fr][3]
        nes_vy = nesStart[sim][3]
        ok = (pf_vy == nes_vy)
        if not ok:
            if first_mismatch is None:
                first_mismatch = fr
                print(f'>>> first mismatch f={fr} sim={sim} PFvy={pf_vy} NESvy={nes_vy}')
            mismatch_count += 1
            if mismatch_count <= 10:
                print(f'f={fr} | mode={pf[fr][0]} Vy={pf_vy} | sim={sim} Vy={nes_vy}')

print(f'\nTotal mismatches: {mismatch_count}')

# Also try PF f=N ↔ NES sim=N (alt mapping)
print('\n\nAlternative mapping PF f=N ↔ NES sim=N:')
mm = 0
for fr in range(2150, min(max(pf), max(nesStart))):
    if fr in pf and fr in nesStart:
        if pf[fr][3] != nesStart[fr][3]:
            mm += 1
            if mm <= 5:
                print(f'f={fr} PFvy={pf[fr][3]} NESvy={nesStart[fr][3]}')
print(f'Total: {mm}')
