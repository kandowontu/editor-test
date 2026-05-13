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

# Compare by X position to determine real correspondence
# For each PF frame, find MES sim_cursor matching PF.X_px exactly
# Print PF.Y vs MES.py and key state
print(f"{'pf_f':>5} {'pf_X':>6} {'pf_Y':>5} {'pf_mode':>3} {'grav':>2} {'onG':>2} {'inp':>2} {'pf_vy':>6}  {'sim':>5} {'mes_px':>6} {'mes_py':>5} {'gm':>2} {'cd':>2} {'ac':>2} {'mes_vy':>6} {'dy':>5}")

# Approach: PF.frame == sim_cursor (already established)
# But position has drifted. So compare directly
def hex_to_signed(s):
    v=int(s,16) if isinstance(s,str) else s
    if v>=0x80000000: v-=0x100000000
    return v

# Look at ball-mode region 1214..2700 first
for f in range(1214, 2750, 5):
    if f>=len(pf_rows): break
    r=pf_rows[f]
    if r['mode']!='2': continue
    m=me_by_sim.get(f)
    if not m: continue
    pfy=int(r['Y_px']); pfx=int(r['X_px'])
    try:
        mpy=int(m['py']); mpx=int(m['px'])
    except: continue
    dx=pfx-mpx; dy=pfy-mpy
    if abs(dy)>3 or abs(dx)>3:
        print(f"{f:>5} {pfx:>6} {pfy:>5} {r['mode']:>3} {r['gravFlipped']:>2} {r['onGround']:>2} {r['input']:>2} {hex_to_signed(r['VelY_fixed']):>6}  {m['sim_cursor']:>5} {mpx:>6} {mpy:>5} {m['gamemode']:>2} {m['cube_data']:>2} {m['a_cur']:>2} {m['vel_y']:>6} {dy:>5}")
