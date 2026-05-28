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

# Same-frame alignment
print(f'{"sim":>4} {"PFcam":>5} {"MTcam":>5} {"dC":>3} {"PFsub":>5} {"MTsub":>5} {"dS":>4} {"PFy":>4} {"MTy":>4} {"dY":>3} {"PFvy":>6} {"MTvy":>6} {"PFmode":>4}')
firstCam = None
firstSub = None
# Find FIRST PERSISTENT cam divergence (lasts >= 30 frames straight)
divStart = None
divCount = 0
firstPersist = None
for i in sorted(set(pf)&set(mt)):
    if i > 500: break
    p = pf[i]; m = mt[i]
    cam_pf = p['camY'] >> 8
    cam_mt = m['scrollY'] - 528
    if cam_pf != cam_mt:
        if divStart is None: divStart = i
        divCount += 1
    else:
        if divCount >= 20 and firstPersist is None:
            firstPersist = divStart
        divStart = None
        divCount = 0
if divCount >= 20 and firstPersist is None:
    firstPersist = divStart

print(f'\n>>> FIRST PERSISTENT cam divergence: PFsim={firstPersist} <<<\n')

# Dump around it
for i in sorted(set(pf)&set(mt)):
    if firstPersist is None: break
    if i < firstPersist - 5 or i > firstPersist + 12: continue
    p = pf[i]; m = mt[i]
    cam_pf = p['camY'] >> 8
    cam_mt = m['scrollY'] - 528
    dC = cam_mt - cam_pf
    dS = m['subpx'] - p['subpx']
    pvy = p['velY']
    if pvy >= 0x8000: pvy -= 0x10000
    pY = p['Y']
    print(f'{i:>4} {cam_pf:>5} {cam_mt:>5} {dC:>+3} {p["subpx"]:>5} {m["subpx"]:>5} {dS:>+4} {p["y_px"]:>4} {m["py"]:>4} {m["py"]-p["y_px"]:>+3} {pvy:>6} {m["velY"]:>6} {p["mode"]:>4}  PFY=0x{pY:X} PFin={p["input"]}')
