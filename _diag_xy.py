import csv, os, sys
pf={};nes={}
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
lo=int(sys.argv[1]); hi=int(sys.argv[2])
for f in range(lo, hi):
    if f not in pf or (f+1) not in nes: continue
    p=pf[f]; n=nes[f+1]
    px = int(p['X_px']); py = int(p['Y_px'])
    nx = int(n['px']); ny = int(n['py'])
    dx=px-nx; dy=py-ny
    print(f'f={f} PF=({px},{py}) NES=({nx},{ny}) d=({dx},{dy})')
