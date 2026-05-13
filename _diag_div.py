import csv, os, sys
pf={}
nes={}
with open(os.path.expandvars(r'%TEMP%\famidash_pf_trace.csv')) as f:
    for row in csv.DictReader(f):
        pf[int(row['frame'])]=row
with open(os.path.expandvars(r'%TEMP%\famidash_mesen_trace.csv')) as f:
    next(f)  # skip nes_y_offset header
    for row in csv.DictReader(f):
        v=row.get('sim_cursor')
        if not v: continue
        try: sc=int(v)
        except: continue
        if sc not in nes: nes[sc]=row

cmd = sys.argv[1] if len(sys.argv)>1 else 'transitions'

if cmd=='transitions':
    prev=None
    for f in sorted(pf):
        p=pf[f]; key=(p['mode'],p['mini'],p['gravFlipped'])
        if key!=prev:
            print(f'PF f={f} mode={p["mode"]} mini={p["mini"]} grav={p["gravFlipped"]} X={p["X_px"]} Y={p["Y_px"]}')
            prev=key
    print('---')
    print('pf frames', len(pf), 'nes sims', len(nes), 'max nes sim', max(nes))

elif cmd=='div':
    # Cursor mapping: PF f=N <-> NES sim=N+1 (after intro freeze fix)
    # Compare X_px, Y_px
    lo = int(sys.argv[2]) if len(sys.argv)>2 else 0
    hi = int(sys.argv[3]) if len(sys.argv)>3 else 4500
    show_only_div = '--all' not in sys.argv
    diverges = []
    for f in range(lo, hi):
        p = pf.get(f); n = nes.get(f+1)
        if not p or not n: continue
        px=int(p['X_px']); py=int(p['Y_px'])
        nx=int(n['px']); ny=int(n['py'])
        dx=px-nx; dy=py-ny
        if dx!=0 or dy!=0:
            diverges.append((f, px, py, nx, ny, dx, dy, p['mode'], p['mini'], p['gravFlipped']))
    print(f'Found {len(diverges)} diverging frames in [{lo},{hi})')
    # First 20
    for d in diverges[:30]:
        print(f'  f={d[0]} PF=({d[1]},{d[2]}) NES=({d[3]},{d[4]}) dXY=({d[5]},{d[6]}) mode={d[7]} mini={d[8]} grav={d[9]}')
    if len(diverges)>30:
        print('...')
        for d in diverges[-5:]:
            print(f'  f={d[0]} PF=({d[1]},{d[2]}) NES=({d[3]},{d[4]}) dXY=({d[5]},{d[6]}) mode={d[7]} mini={d[8]} grav={d[9]}')
