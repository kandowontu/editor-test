import csv, os, io
T=os.environ['TEMP']
me=os.path.join(T,'famidash_mesen_trace.csv')
pf=os.path.join(T,'famidash_pf_trace.csv')

with open(me) as f: lines=f.readlines()
me_rows=list(csv.DictReader(io.StringIO(''.join(lines[1:]))))
with open(pf) as f: pf_rows=list(csv.DictReader(f))

me_by_sim={}
for r in me_rows:
    try: sc=int(r['sim_cursor'])
    except: continue
    me_by_sim[sc]=r

# Find first frame where dx > 1
for f in range(0, 1500):
    if f>=len(pf_rows): break
    r=pf_rows[f]
    m=me_by_sim.get(f)
    if not m: continue
    try:
        pfx=int(r['X_px']); mpx=int(m['px'])
        pfy=int(r['Y_px']); mpy=int(m['py'])
    except: continue
    dx=pfx-mpx; dy=pfy-mpy
    if abs(dx)>=2 or abs(dy)>=3:
        print(f"f={f} PF=({pfx},{pfy}) MES=({mpx},{mpy}) dx={dx} dy={dy} PFmode={r['mode']} MESmode={m['gamemode']} PFinp={r['input']} MESac={m['a_cur']} MESan={m['a_next']}")
        if f>50: break
