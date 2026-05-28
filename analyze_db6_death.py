import csv, os
T=os.environ['TEMP']
pf=os.path.join(T,'famidash_pf_trace_dorabaebasic6_20260522_015820_172.csv')
mt=os.path.join(T,'famidash_mesen_trace_dorabaebasic6_20260522_020620_795.csv')

# Read PF
pf_rows=[]
with open(pf) as f:
    r=csv.DictReader(f)
    for row in r:
        try: fr=int(row['frame'])
        except: continue
        pf_rows.append(row)

# Read MT, find last sim_cursor advance frame
with open(mt) as f:
    lines=f.readlines()
hi=1
hdr=lines[hi].strip().split(',')
col={n:i for i,n in enumerate(hdr)}
last_sim=-1
mt_at={}
for ln in lines[hi+1:]:
    p=ln.rstrip('\n').split(',')
    if len(p)<len(hdr): continue
    if len(p)>len(hdr):
        extra=len(p)-len(hdr)
        ci=col['cube_data']
        p=p[:ci]+[','.join(p[ci:ci+extra+1])]+p[ci+extra+1:]
    try: sim=int(p[col['sim_cursor']])
    except: continue
    if sim>last_sim:
        last_sim=sim
        mt_at[sim]={k:p[col[k]] for k in ['px','py','vel_y','gamemode','table_idx','gravity_mod','mini','death_pc','death_ctx','dual']}

print("MT last sim:", last_sim)
print("MT at last:", mt_at[last_sim])
print("MT at last-1:", mt_at.get(last_sim-1))
print("MT at last-2:", mt_at.get(last_sim-2))
print("MT at last-3:", mt_at.get(last_sim-3))
print()
# PF context around frame = last_sim-1
target=last_sim-1
print(f"PF context around frame {target} (sim alignment: PF.f=N <-> MT.sim=N+1)")
for row in pf_rows:
    fr=int(row['frame'])
    if target-6 <= fr <= target+3:
        vs=row['VelY_fixed']
        vy=int(vs,16) if vs.startswith('0x') else int(vs)
        if vy>=0x80000000: vy-=0x100000000
        print(f"  f={fr}: X={row['X_px']:>5} Y={row['Y_px']:>4} vy={vy:>6} mode={row['mode']} mini={row['mini']} onG={row['onGround']}")
