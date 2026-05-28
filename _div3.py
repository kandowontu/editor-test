import os, sys
T = os.environ['TEMP']
PF = T + r'\famidash_pf_trace_cataclysm_20260520_142348_350.csv'
MT = T + r'\famidash_mesen_trace_cataclysm_20260520_145022_953.csv'

def h(s):
    s=s.strip()
    return int(s,16) if s.startswith('0x') else int(s)

pf = {}
with open(PF) as f:
    f.readline()
    for line in f:
        p = line.rstrip().split(',')
        try: fr = int(p[0])
        except: continue
        pf[fr] = dict(
            X_fixed=h(p[1]), Y_fixed=h(p[2]), velY=h(p[3]), input=int(p[4]),
            X_px=int(p[6]), Y_px=int(p[7]), onG=int(p[8]),
            camY_px=int(p[9]), tgtCamY=int(p[10]), mode=int(p[11]),
            grav=int(p[12]), camY_fixed=h(p[19]), Ylow=h(p[20]),
            CamLow=h(p[21]), subpx=int(p[22]), mini=int(p[23]),
        )

mt = {}
with open(MT) as f:
    f.readline(); f.readline()
    for line in f:
        parts = line.rstrip('\n').split(',')
        extra = len(parts) - 30
        if extra > 0:
            parts = parts[:22] + [','.join(parts[22:22+extra+1])] + parts[22+extra+1:]
        try: s = int(parts[1])
        except: continue
        if s in mt: continue
        mt[s] = dict(
            rom=int(parts[0]), px=int(parts[2]), py=int(parts[3]),
            a_next=int(parts[4]), a_cur=int(parts[5]),
            raw_x=int(parts[6]), raw_y=int(parts[7]),
            scrollx=int(parts[8]), scrollY=int(parts[9]),
            velY=int(parts[10]), table_idx=int(parts[11]),
            grav_mod=int(parts[12]), dashing=int(parts[13]),
            mode=int(parts[14]), subpx=int(parts[15]),
            framerate=int(parts[16]), tgt_scroll_y=int(parts[17]),
            cp_y=int(parts[18]), cube_data=int(parts[20]),
            mini=int(parts[24]),
        )

print(f'PF frames: {len(pf)} (last={max(pf)})')
print(f'MT frames: {len(mt)} (last={max(mt)})')
print()

# Find first persistent divergence in cam/sub/y at shift=+1
SHIFT = int(sys.argv[1]) if len(sys.argv)>1 else 1
LO = int(sys.argv[2]) if len(sys.argv)>2 else 100
HI = int(sys.argv[3]) if len(sys.argv)>3 else 2932

divs = []
for i in sorted(pf):
    j = i + SHIFT
    if j not in mt: continue
    if i < LO or i > HI: continue
    p = pf[i]; m = mt[j]
    cam_pf = p['camY_fixed'] >> 8
    cam_mt = m['scrollY'] - 528
    dC = cam_mt - cam_pf
    dS = m['subpx'] - p['subpx']
    dY = m['py'] - p['Y_px']
    dX = m['px'] - p['X_px']
    dM = m['mode'] - p['mode']
    if dX or dY or dC or dS or dM:
        divs.append((i, j, dX, dY, dC, dS, dM, p, m))

if not divs:
    print('No divergence found!')
else:
    print(f'First divergence: PFsim={divs[0][0]} MTsim={divs[0][1]} dX={divs[0][2]} dY={divs[0][3]} dC={divs[0][4]} dS={divs[0][5]} dM={divs[0][6]}')
    # Find first persistent (no recover for 10 frames)
    first_persistent = None
    for k in range(len(divs)):
        i = divs[k][0]
        # check next 20 sim cursors all have div
        future_divs = [d for d in divs[k:k+50] if d[0] < i + 30]
        if len(future_divs) >= 25:
            first_persistent = divs[k]
            break
    print(f'First persistent: {first_persistent[0] if first_persistent else None}')
    
    # dump around persistent (or first if none)
    target = first_persistent[0] if first_persistent else divs[0][0]
    print(f'\nDump around sim={target}:')
    print(f'{"PF":>5} {"MT":>5} {"PFx":>5} {"MTx":>5} {"dX":>3} {"PFy":>4} {"MTy":>4} {"dY":>3} {"PFm":>3} {"MTm":>3} {"PFcam":>5} {"MTcam":>5} {"dC":>3} {"PFsub":>5} {"MTsub":>5} {"dS":>4} {"PFvy":>6} {"MTvy":>6} {"PFin":>3} {"MTac":>3} {"MTan":>3}')
    for i in sorted(pf):
        if i < target - 10 or i > target + 20: continue
        j = i + SHIFT
        if j not in mt: continue
        p = pf[i]; m = mt[j]
        cam_pf = p['camY_fixed'] >> 8
        cam_mt = m['scrollY'] - 528
        pvy = p['velY']
        if pvy >= 0x8000: pvy -= 0x10000
        print(f'{i:>5} {j:>5} {p["X_px"]:>5} {m["px"]:>5} {m["px"]-p["X_px"]:>+3} {p["Y_px"]:>4} {m["py"]:>4} {m["py"]-p["Y_px"]:>+3} {p["mode"]:>3} {m["mode"]:>3} {cam_pf:>5} {cam_mt:>5} {cam_mt-cam_pf:>+3} {p["subpx"]:>5} {m["subpx"]:>5} {m["subpx"]-p["subpx"]:>+4} {pvy:>6} {m["velY"]:>6} {p["input"]:>3} {m["a_cur"]:>3} {m["a_next"]:>3}')
