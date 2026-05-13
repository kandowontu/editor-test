import csv, os, io
T=os.environ['TEMP']
me=os.path.join(T,'famidash_mesen_trace.csv')
pf=os.path.join(T,'famidash_pf_trace.csv')

with open(me) as f:
    lines=f.readlines()
me_rows=list(csv.DictReader(io.StringIO(''.join(lines[1:]))))
with open(pf) as f:
    pf_rows=list(csv.DictReader(f))

# Find the rom_frame where ROM transitions to gamemode 2 (ball) near sim 3788
# Look at last 50 frames where sim_cursor advances (alive)
print("MES last 60 progressing frames:")
seen_sim=set()
last=[]
for r in me_rows:
    try: sc=int(r['sim_cursor'])
    except: continue
    if sc not in seen_sim:
        seen_sim.add(sc)
        last.append(r)
for r in last[-60:]:
    print(f"sim={r['sim_cursor']} rom={r['rom_frame']} px={r['px']} py={r['py']} gm={r['gamemode']} ac={r['a_cur']} an={r['a_next']} cd={r['cube_data']} vy={r['vel_y']} gravmod={r['gravity_mod']} mini={r['mini']}")
