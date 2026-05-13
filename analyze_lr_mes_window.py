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

# PF flipped at f=3661. Let's inspect MES around sim 3640..3800 with EVERY frame.
print(f"{'sim':>5} {'rom':>5} {'px':>5} {'py':>4} {'gm':>2} {'ac':>2} {'an':>2} {'cd':>2} {'vy':>5} {'sc_y':>4} {'cpg':>3} {'cd_y':>4}")
seen=set()
for r in me_rows:
    try: sc=int(r['sim_cursor'])
    except: continue
    if sc in seen: continue
    if sc < 3640 or sc > 3800: continue
    seen.add(sc)
    print(f"{sc:>5} {r['rom_frame']:>5} {r['px']:>5} {r['py']:>4} {r['gamemode']:>2} {r['a_cur']:>2} {r['a_next']:>2} {r['cube_data']:>2} {r['vel_y']:>5} {r['scrolly']:>4} {r['cp_gravity']:>3} {r['cp_y']:>4}")
