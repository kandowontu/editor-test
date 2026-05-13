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

def hs(s):
    v=int(s,16); 
    if v>=0x80000000: v-=0x100000000
    return v

print(f"{'pf_f':>5} {'sim':>5}  pf_Yfx_hex  pf_camYpx pf_subpx  me_py me_scy me_subpx me_cpy mes_vy")
for f in range(3720, 3760):
    if f>=len(pf_rows): break
    r=pf_rows[f]
    sim=f+1
    m=me_by_sim.get(sim)
    if not m: continue
    pf_yfx=int(r['Y_fixed'],16)
    print(f"{f:>5} {sim:>5}   0x{pf_yfx:08X}    {r['CamY_px']:>5}    {r['ScrollYSubpx']:>3}    {m['py']:>4}  {m['scrolly']:>4}    {m['scroll_y_subpx']:>3}    {m['cp_y']:>5}  {m['vel_y']:>5}")
