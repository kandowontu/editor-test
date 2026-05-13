import csv, os, sys
pf={}; nes={}
with open(os.path.expandvars(r'%TEMP%\famidash_pf_trace.csv')) as f:
    for row in csv.DictReader(f): pf[int(row['frame'])]=row
with open(os.path.expandvars(r'%TEMP%\famidash_mesen_trace.csv')) as f:
    next(f)
    for row in csv.DictReader(f):
        v=row.get('sim_cursor')
        if not v: continue
        try: sc=int(v)
        except: continue
        if sc not in nes: nes[sc]=row

lo = int(sys.argv[1]); hi = int(sys.argv[2])
for f in range(lo, hi):
    p=pf.get(f); n=nes.get(f+1)
    if not (p and n): continue
    pvy = int(p['VelY_fixed'],16) if p['VelY_fixed'].startswith('0x') else int(p['VelY_fixed'])
    if pvy >= 0x80000000: pvy -= 0x100000000
    print(f"f={f} PF X={p['X_px']:>5} Y={p['Y_px']:>4} vy={pvy:>6} onG={p['onGround']} inp={p['input']} grav={p['gravFlipped']} | NES X={n['px']:>5} Y={n['py']:>4} vy={n['vel_y']:>6} cd={n['cube_data']} a={n['a_cur']}")
