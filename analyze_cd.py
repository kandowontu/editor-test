import re, os
T=os.environ['TEMP']
nes=os.path.join(T,'famidash_mesen_physics_debug_dorabaebasic6_20260525_193155_739.log')

curSim = None
re_sim = re.compile(r'^F=\d+ sim=(\d+)')
re_cd = re.compile(r'\bcd=([0-9A-Fa-f]{2})\b')
modePerSim = {}
with open(nes,'r',encoding='utf-8',errors='ignore') as f:
    for ln in f:
        m = re_sim.match(ln)
        if m:
            curSim = int(m.group(1)); continue
        if curSim is None: continue
        m = re_cd.search(ln)
        if m and curSim not in modePerSim:
            modePerSim[curSim] = int(m.group(1),16)

prev=-1
for s in sorted(modePerSim):
    v = modePerSim[s]
    if v != prev:
        print(f'sim={s} cd=0x{v:02X}')
        prev = v
