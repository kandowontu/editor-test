import re, os
T=os.environ['TEMP']
nes=os.path.join(T,'famidash_mesen_physics_debug_dorabaebasic6_20260525_193155_739.log')
orb=os.path.join(T,'famidash_pf_orb_debug_dorabaebasic6_20260525_192426_022.log')

# NES: per sim, capture first *_movement.in event with cpx/cpy
nesPos = {}
curSim = None
re_sim = re.compile(r'^F=\d+ sim=(\d+)')
re_mv  = re.compile(r'(?:ufo|cube|ball|spider|ship|robot|wave|swing|ninja|pogo)_movement\.in.* cpx=([0-9A-Fa-f]{4}) cpy=([0-9A-Fa-f]{4}).*cpvx=(-?\d+) cpvy=(-?\d+)')
with open(nes,'r',encoding='utf-8',errors='ignore') as f:
    for ln in f:
        m = re_sim.match(ln)
        if m:
            curSim = int(m.group(1))
            continue
        if curSim is None: continue
        m = re_mv.search(ln)
        if m and curSim not in nesPos:
            cpx = int(m.group(1),16)
            cpy = int(m.group(2),16)
            vx = int(m.group(3))
            vy = int(m.group(4))
            nesPos[curSim] = (cpx, cpy, vx, vy)

# PF orb_debug: per f, capture X_fixed/Y_fixed/VelY/mode
pfPos = {}
re_pf = re.compile(r'f=(\d+) ENTER mode=(\d+).*X_fixed=0x([0-9A-Fa-f]+) Y_fixed=0x([0-9A-Fa-f]+) VelY_fixed=0x([0-9A-Fa-f]+).*playerY_px=(-?\d+) CamY_px=(-?\d+)')
with open(orb,'r',encoding='utf-8',errors='ignore') as f:
    for ln in f:
        m = re_pf.search(ln)
        if m:
            fr = int(m.group(1))
            mode = int(m.group(2))
            x = int(m.group(3),16) & 0xFFFFFF
            y = int(m.group(4),16) & 0xFFFFFF
            vy = int(m.group(5),16)
            if vy >= 0x80000000: vy -= 0x100000000
            elif vy >= 0x8000 and len(m.group(5))<=4: pass  # already short
            py = int(m.group(6))
            cy = int(m.group(7))
            if fr not in pfPos:
                pfPos[fr] = (mode, x, y, vy, py, cy)

print(f'NES sims: {len(nesPos)} min={min(nesPos)} max={max(nesPos)}')
print(f'PF frames: {len(pfPos)} min={min(pfPos)} max={max(pfPos)}')

# PF f maps roughly to NES sim with some offset. From prior analysis: PF f=N ↔ NES sim=N+1.
# Compare per-frame px positions to find first divergence.
# NES screen Y = cpy_hi. PF playerY_px = world Y. Diff = camY.
# Let's track (PF.playerY_px - NES.cpy_hi) sequence; if drift, divergence.
maxF = min(max(pfPos), max(nesPos)-1)
prevDiff = None
firstBigDiv = None
prevX = None
for fr in range(2160, maxF+1):
    sim = fr + 1
    if fr not in pfPos or sim not in nesPos: continue
    pf = pfPos[fr]; nes = nesPos[sim]
    pf_mode, pf_xf, pf_yf, pf_vy, pf_py, pf_cy = pf
    pf_px = pf_xf >> 8
    nes_cpx, nes_cpy, nes_vx, nes_vy = nes
    nes_pxhi = nes_cpx >> 8
    nes_pyhi = nes_cpy >> 8
    # diff PF_px - NES_px screen + assumed camera offset
    # X comparison: PF X fixed (pixels world). NES cpx is screen pos (fixed). World X = scrollX + cpx. Use only delta diff.
    xdiff = pf_px - nes_pxhi  # not meaningful absolute
    ydiff = pf_py - nes_pyhi
    if firstBigDiv is None and prevDiff is not None and abs(ydiff - prevDiff) > 2:
        firstBigDiv = fr
        print(f'>> divergence change at f={fr} prevDiff={prevDiff} now={ydiff}')
    prevDiff = ydiff

# Print snapshots
print('\nSnapshots:')
for fr in [2200,2300,2400,2500,2600,2640]:
    sim = fr+1
    if fr in pfPos and sim in nesPos:
        pf = pfPos[fr]; nes = nesPos[sim]
        print(f'f={fr} sim={sim} | PF mode={pf[0]} X_px={pf[1]>>8} Y_px={pf[4]} Vy={pf[3]} CamY={pf[5]} | NES cpx={nes[0]:04X} cpy={nes[1]:04X} vx={nes[2]} vy={nes[3]} Y_hi={nes[1]>>8}')
    else:
        print(f'f={fr} sim={sim} | missing pf={fr in pfPos} nes={sim in nesPos}')
