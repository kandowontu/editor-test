import re, os
T=os.environ['TEMP']
nes=os.path.join(T,'famidash_mesen_physics_debug_dorabaebasic6_20260525_193155_739.log')
orb=os.path.join(T,'famidash_pf_orb_debug_dorabaebasic6_20260525_192426_022.log')

nesStart = {}
curSim = None
re_sim = re.compile(r'^F=\d+ sim=(\d+)')
re_mv  = re.compile(r'(?:ufo|ship)_movement\.in.* cpx=([0-9A-Fa-f]{4}) cpy=([0-9A-Fa-f]{4}).*cpvx=(-?\d+) cpvy=(-?\d+)')
with open(nes,'r',encoding='utf-8',errors='ignore') as f:
    for ln in f:
        m = re_sim.match(ln)
        if m:
            curSim = int(m.group(1)); continue
        if curSim is None: continue
        m = re_mv.search(ln)
        if m and curSim not in nesStart:
            nesStart[curSim] = (int(m.group(1),16), int(m.group(2),16), int(m.group(3)), int(m.group(4)))

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

# Show parallel: f=N PF | sim=N NES | sim=N+1 NES
print('f | PF(mode,Y_px,Vy) | NES sim=f (Y_hi,Vy) | NES sim=f+1 (Y_hi,Vy)')
for fr in range(2140, 2200):
    if fr not in pf: continue
    pm, px, py, pvy = pf[fr]
    py_px = py >> 8
    px_px = px >> 8
    nA = nesStart.get(fr)
    nB = nesStart.get(fr+1)
    nAs = f'cpx={nA[0]:04X} cpy={nA[1]:04X} Yh={nA[1]>>8} Xh={nA[0]>>8} Vy={nA[3]}' if nA else '---'
    nBs = f'cpx={nB[0]:04X} cpy={nB[1]:04X} Yh={nB[1]>>8} Xh={nB[0]>>8} Vy={nB[3]}' if nB else '---'
    print(f'f={fr} m={pm} Y={py_px} X={px_px} Vy={pvy} | NESsim={fr}: {nAs} | NESsim={fr+1}: {nBs}')
