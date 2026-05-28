import csv, os
T = os.environ['TEMP']
pf_path = T + r'\famidash_pf_trace_cataclysm_20260518_151738_650.csv'
mt_path = T + r'\famidash_mesen_trace_cataclysm_20260518_151724_006.csv'

def h(s):
    s = s.strip()
    return int(s, 16) if s.startswith('0x') else int(s)

pf = {}
with open(pf_path) as f:
    f.readline()
    for line in f:
        p = line.rstrip().split(',')
        try: fr = int(p[0])
        except: continue
        pf[fr] = dict(subpx=int(p[22]), camY=h(p[19]), Y=h(p[2]), velY=h(p[3]), mode=int(p[11]), y_px=int(p[7]), input=int(p[4]))

mt = {}
with open(mt_path) as f:
    f.readline(); f.readline()
    for line in f:
        parts = line.rstrip('\n').split(',')
        extra = len(parts) - 30
        if extra > 0:
            parts = parts[:22] + [','.join(parts[22:22+extra+1])] + parts[22+extra+1:]
        try: s = int(parts[1])
        except: continue
        if s in mt: continue
        mt[s] = dict(subpx=int(parts[15]), scrollY=int(parts[9]), rawY=int(parts[7]), velY=int(parts[10]), mode=int(parts[14]), py=int(parts[3]))

# SHIFT=+1: PF[N] aligned with MT[N+1]
SHIFT = 1
print(f'Shift=+{SHIFT} (PF[N] vs MT[N+{SHIFT}])')
print(f'{"PFsim":>5} {"MTsim":>5} {"PFcam":>5} {"MTcam":>5} {"dC":>3} {"PFsub":>5} {"MTsub":>5} {"dS":>5} {"PFy":>4} {"MTy":>4} {"dY":>3} {"PFvy":>6} {"MTvy":>6}')

divStart = None; divCount = 0; firstPersist = None
for i in sorted(pf):
    j = i + SHIFT
    if j not in mt: continue
    cam_pf = pf[i]['camY'] >> 8
    cam_mt = mt[j]['scrollY'] - 528
    if cam_pf != cam_mt:
        if divStart is None: divStart = i
        divCount += 1
    else:
        if divCount >= 20 and firstPersist is None:
            firstPersist = divStart
        divStart = None; divCount = 0

if firstPersist is None and divCount >= 20: firstPersist = divStart
print(f'\n>>> First persistent cam div: PFsim={firstPersist} <<<\n')

for i in sorted(pf):
    if firstPersist is None: break
    if i < firstPersist - 6 or i > firstPersist + 15: continue
    j = i + SHIFT
    if j not in mt: continue
    p = pf[i]; m = mt[j]
    cam_pf = p['camY'] >> 8
    cam_mt = m['scrollY'] - 528
    dC = cam_mt - cam_pf
    dS = m['subpx'] - p['subpx']
    pvy = p['velY']
    if pvy >= 0x8000: pvy -= 0x10000
    print(f'{i:>5} {j:>5} {cam_pf:>5} {cam_mt:>5} {dC:>+3} {p["subpx"]:>5} {m["subpx"]:>5} {dS:>+5} {p["y_px"]:>4} {m["py"]:>4} {m["py"]-p["y_px"]:>+3} {pvy:>6} {m["velY"]:>6}  PFY=0x{p["Y"]:X} mode={p["mode"]}')
