import csv, os, sys
T=os.environ['TEMP']
pf={}
with open(os.path.join(T,'famidash_pf_trace.csv')) as f:
    r=csv.DictReader(f)
    for row in r:
        pf[int(row['frame'])] = (int(row['X_px']), int(row['Y_px']), row['mode'], row['gravFlipped'], row['mini'])

mes={}
nes_y_off=0
with open(os.path.join(T,'famidash_mesen_trace.csv')) as f:
    first=f.readline().strip()
    if first.startswith('nes_y_offset'):
        nes_y_off=int(first.split(',')[1])
        header=f.readline().strip().split(',')
    else:
        header=first.split(',')
    # mesen file has multiple attempts; use last attempt with max sim_cursor
    attempts=[]
    cur=[]
    for line in f:
        parts=line.rstrip().split(',')
        if len(parts)<len(header):
            continue
        try:
            sim=int(parts[1])
        except:
            continue
        if cur and sim < cur[-1][0]:
            attempts.append(cur)
            cur=[]
        cur.append((sim, parts))
    if cur: attempts.append(cur)
    # pick attempt with max final sim_cursor
    best=max(attempts, key=lambda a: a[-1][0])
    for sim, parts in best:
        # build mapping by sim_cursor
        d=dict(zip(header,parts))
        mes[sim]=(int(d['px']), int(d['py']), d.get('gamemode',''), d.get('mini',''), d.get('a_cur',''), d.get('death_pc',''))

print("nes_y_off=", nes_y_off)
print("PF frames:", max(pf.keys()) if pf else 0)
print("Mesen sim_cursor max:", max(mes.keys()) if mes else 0)

# find first sustained divergence (>=2 consecutive frames mismatch)
common=sorted(set(pf.keys()) & set(mes.keys()))
print("Common frames:", len(common))

streak=0
first_div=None
for f in common:
    px,py,*_ = pf[f]
    mx,my,*_ = mes[f]
    my_adj = my + nes_y_off  # if needed
    # try unadjusted first
    if px!=mx or py!=my:
        streak+=1
        if streak==1:
            cand=f
        if streak>=3:
            first_div=cand
            break
    else:
        streak=0

print("First sustained diverge:", first_div)
if first_div:
    for f in range(max(0,first_div-3), min(max(common)+1, first_div+10)):
        if f in pf and f in mes:
            print(f"f={f} PF=({pf[f][0]},{pf[f][1]}) mode={pf[f][2]} grav={pf[f][3]} mini={pf[f][4]}  MES=({mes[f][0]},{mes[f][1]}) gm={mes[f][2]} mini={mes[f][3]} a={mes[f][4]}")

# print region around halfway
half = max(common)//2
print(f"\n--- around halfway ({half}) ---")
for f in range(half-5, half+15):
    if f in pf and f in mes:
        print(f"f={f} PF=({pf[f][0]},{pf[f][1]}) mode={pf[f][2]}  MES=({mes[f][0]},{mes[f][1]}) gm={mes[f][2]} a={mes[f][4]}")
