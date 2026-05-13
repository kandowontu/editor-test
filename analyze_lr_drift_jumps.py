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

def hex_to_signed(s):
    v=int(s,16) if isinstance(s,str) else s
    if v>=0x80000000: v-=0x100000000
    return v

print(f"{'pf_f':>5} {'pf_X':>6} {'pf_Y':>5} {'mod':>3} {'gr':>2} {'oG':>2} {'in':>2} {'pf_vy':>6}  {'mes_px':>6} {'mes_py':>5} {'gm':>2} {'cd':>2} {'ac':>2} {'mes_vy':>6} {'dx':>3} {'dy':>4}")

# Scan continuously to find first big jump
prev_dy=0
for f in range(3460, 3760):
    if f>=len(pf_rows): break
    r=pf_rows[f]
    m=me_by_sim.get(f)
    if not m: continue
    pfy=int(r['Y_px']); pfx=int(r['X_px'])
    try:
        mpy=int(m['py']); mpx=int(m['px'])
    except: continue
    dy=pfy-mpy; dx=pfx-mpx
    print(f"{f:>5} {pfx:>6} {pfy:>5} {r['mode']:>3} {r['gravFlipped']:>2} {r['onGround']:>2} {r['input']:>2} {hex_to_signed(r['VelY_fixed']):>6}  {mpx:>6} {mpy:>5} {m['gamemode']:>2} {m['cube_data']:>2} {m['a_cur']:>2} {m['vel_y']:>6} {dx:>3} {dy:>4}")
