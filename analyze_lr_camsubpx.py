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

# Track when PF.ScrollYSubpx vs NES.scroll_y_subpx diverge
prev=None
print(f"{'pf_f':>5} {'sim':>5} {'pf_camy':>7} {'pf_subpx':>8}  {'me_scy':>6} {'me_subpx':>8}")
for f in range(0, len(pf_rows), 10):
    r=pf_rows[f]
    sim=f+1
    m=me_by_sim.get(sim)
    if not m: continue
    pair=(int(r['CamY_px']), int(r['ScrollYSubpx']), int(m['scrolly']), int(m['scroll_y_subpx']))
    if pair != prev:
        print(f"{f:>5} {sim:>5} {r['CamY_px']:>7} {r['ScrollYSubpx']:>8}  {m['scrolly']:>6} {m['scroll_y_subpx']:>8}")
        prev=pair
