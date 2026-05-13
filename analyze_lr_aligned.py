import csv, os, io
T=os.environ['TEMP']
me=os.path.join(T,'famidash_mesen_trace.csv')
pf=os.path.join(T,'famidash_pf_trace.csv')

with open(me) as f: lines=f.readlines()
me_rows=list(csv.DictReader(io.StringIO(''.join(lines[1:]))))
with open(pf) as f: pf_rows=list(csv.DictReader(f))

# MES sim_cursor=K corresponds to PF f=K-1
me_by_sim={}
for r in me_rows:
    try: sc=int(r['sim_cursor'])
    except: continue
    me_by_sim[sc]=r

def hs(s):
    v=int(s,16); 
    if v>=0x80000000: v-=0x100000000
    return v

# Compare with offset: PF.f vs MES.sim_cursor=PF.f+1
# Window around death region
print(f"{'pf_f':>5} {'sim':>5} {'pf_X':>5} {'pf_Y':>4} {'pf_in':>3} {'pf_vy':>5} {'mode':>3} {'gr':>2} {'oG':>2}  {'me_px':>5} {'me_py':>4} {'me_ac':>3} {'me_an':>3} {'me_vy':>5} {'gm':>2} {'cd':>2}")
for f in range(3640, 3800):
    if f>=len(pf_rows): break
    r=pf_rows[f]
    sim=f+1
    m=me_by_sim.get(sim)
    if not m: continue
    print(f"{r['frame']:>5} {sim:>5} {r['X_px']:>5} {r['Y_px']:>4} {r['input']:>3} {hs(r['VelY_fixed']):>5} {r['mode']:>3} {r['gravFlipped']:>2} {r['onGround']:>2}  {m['px']:>5} {m['py']:>4} {m['a_cur']:>3} {m['a_next']:>3} {m['vel_y']:>5} {m['gamemode']:>2} {m['cube_data']:>2}")
