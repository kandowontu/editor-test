"""Diagnose dorabaebasic6 ball-mode gravity-flip divergence at MT sim=2318 death."""
import csv, os

PF = os.path.expandvars(r"%TEMP%\famidash_pf_trace_dorabaebasic6_20260521_002652_287.csv")
MT = os.path.expandvars(r"%TEMP%\famidash_mesen_trace_dorabaebasic6_20260521_003653_624.csv")

def load_pf():
    with open(PF, newline='') as f:
        return list(csv.DictReader(f))

def load_mt():
    rows=[]
    with open(MT, newline='') as f:
        f.readline()  # metadata
        hdr=f.readline().strip().split(',')
        r=csv.DictReader(f, fieldnames=hdr)
        for row in r:
            if row.get('sim_cursor') is None: continue
            rows.append(row)
    return rows

pf=load_pf(); mt=load_mt()
mt_by_sim={}
for row in mt:
    k=int(row['sim_cursor'])
    if k not in mt_by_sim: mt_by_sim[k]=row

start, end = 2280, 2325
print(f"frame |  PF X     Y  vy     mode mini gF onG | MT X    py  vel_y  mode mini cpg")
for r in pf:
    fr=int(r['frame'])
    if fr<start or fr>end: continue
    mtr = mt_by_sim.get(fr) or mt_by_sim.get(fr+1)
    vy=int(r['VelY_fixed'],16); 
    if vy>=0x80000000: vy-=0x100000000
    mt_x = mtr['px'] if mtr else '?'
    mt_y = mtr['py'] if mtr else '?'
    mt_vy = mtr['vel_y'] if mtr else '?'
    mt_mode = mtr['gamemode'] if mtr else '?'
    mt_mini = mtr['mini'] if mtr else '?'
    mt_cpg = mtr['cp_gravity'] if mtr else '?'
    print(f"{fr:5d} | {r['X_px']:5s} {r['Y_px']:3s} {vy:6d} {r['mode']:4s} {r['mini']:4s} {r['gravFlipped']:2s} {r['onGround']:3s} | {mt_x:5s} {mt_y:3s} {mt_vy:6s} {mt_mode:4s} {mt_mini:4s} {mt_cpg}")
