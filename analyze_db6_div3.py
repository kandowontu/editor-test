import csv, os
T=os.environ['TEMP']
pf=os.path.join(T,'famidash_pf_trace_dorabaebasic6_20260522_015820_172.csv')
mt=os.path.join(T,'famidash_mesen_trace_dorabaebasic6_20260522_020620_795.csv')

pf_rows={}
with open(pf) as f:
    r=csv.DictReader(f)
    for row in r:
        fr=int(row['frame']); 
        vs=row['VelY_fixed']
        vy=int(vs,16) if vs.startswith('0x') else int(vs)
        if vy>=0x80000000: vy-=0x100000000
        pf_rows[fr]=(int(row['X_px']),int(row['Y_px']),vy,row['onGround'])

with open(mt) as f:
    lines=f.readlines()
hi=1
hdr=lines[hi].strip().split(',')
col={n:i for i,n in enumerate(hdr)}
mt_rows={}
for ln in lines[hi+1:]:
    p=ln.rstrip('\n').split(',')
    if len(p)<len(hdr): continue
    if len(p)>len(hdr):
        extra=len(p)-len(hdr)
        ci=col['cube_data']
        p=p[:ci]+[','.join(p[ci:ci+extra+1])]+p[ci+extra+1:]
    sim=int(p[col['sim_cursor']])
    vy=int(p[col['vel_y']])
    if vy>=0x8000: vy-=0x10000
    if sim not in mt_rows:
        mt_rows[sim]=(int(p[col['px']]),int(p[col['py']]),vy,p[col['table_idx']])

# Find ALL persistent divergences (group consecutive)
runs=[]
cur=None
for fr in sorted(pf_rows):
    sim=fr+1
    if sim not in mt_rows: continue
    px,py,pvy,onG=pf_rows[fr]
    mx,my,mvy,tidx=mt_rows[sim]
    if py!=my or px!=mx or pvy!=mvy:
        if cur and cur['end']==fr-1:
            cur['end']=fr; cur['n']+=1
        else:
            if cur: runs.append(cur)
            cur={'start':fr,'end':fr,'n':1,
                 'first':(px,py,pvy,mx,my,mvy)}
    else:
        if cur: runs.append(cur); cur=None
if cur: runs.append(cur)
print(f"Total divergence runs: {len(runs)}")
print("First 10:")
for r in runs[:10]:
    px,py,pvy,mx,my,mvy=r['first']
    print(f"  f={r['start']}..{r['end']} ({r['n']} frames) start: PF({px},{py},vy={pvy}) MT({mx},{my},vy={mvy})")

# Context around first divergence
fd=runs[0]['start']
print(f"\nContext around first divergence f={fd}:")
for fr in range(fd-5, fd+8):
    if fr in pf_rows:
        px,py,pvy,onG=pf_rows[fr]
        mx,my,mvy,tidx=mt_rows.get(fr+1,(0,0,0,'?'))
        mark=' <<<' if py!=my or px!=mx or pvy!=mvy else ''
        print(f"  f={fr}: PF({px:>5},{py:>4},vy={pvy:>6},oG={onG}) MT({mx:>5},{my:>4},vy={mvy:>6},tidx={tidx}){mark}")
