import csv, os, sys
T=os.environ['TEMP']
pf=os.path.join(T,'famidash_pf_trace_dorabaebasic6_20260522_024425_084.csv')
mt=os.path.join(T,'famidash_mesen_trace_dorabaebasic6_20260522_025551_880.csv')

pf_rows={}
with open(pf) as f:
    r=csv.DictReader(f)
    for row in r:
        fr=int(row['frame']); 
        vs=row['VelY_fixed']
        vy=int(vs,16) if vs.startswith('0x') else int(vs)
        if vy>=0x80000000: vy-=0x100000000
        pf_rows[fr]=(int(row['X_px']),int(row['Y_px']),vy,row['onGround'],row['mode'],row['mini'],row.get('gravFlipped','0'),int(row['alive']))

with open(mt) as f:
    lines=f.readlines()
hi=1
hdr=lines[hi].strip().split(',')
col={n:i for i,n in enumerate(hdr)}
mt_rows={}
last_sim=-1
for ln in lines[hi+1:]:
    p=ln.rstrip('\n').split(',')
    if len(p)<len(hdr): continue
    if len(p)>len(hdr):
        extra=len(p)-len(hdr)
        ci=col['cube_data']
        p=p[:ci]+[','.join(p[ci:ci+extra+1])]+p[ci+extra+1:]
    sim=int(p[col['sim_cursor']])
    last_sim=max(last_sim,sim)
    if sim not in mt_rows:
        vy=int(p[col['vel_y']])
        if vy>=0x8000: vy-=0x10000
        mt_rows[sim]=(int(p[col['px']]),int(p[col['py']]),vy,p[col['gamemode']],p[col['mini']],p[col['gravity_mod']],p[col.get('death_pc','death_pc')] if 'death_pc' in col else '',p[col.get('death_ctx','death_ctx')] if 'death_ctx' in col else '')

print(f"PF frames {min(pf_rows)}..{max(pf_rows)}, MT sims {min(mt_rows)}..{last_sim}")
pf_alive_last=max(fr for fr,r in pf_rows.items() if r[7]==1)
pf_dead=[fr for fr,r in pf_rows.items() if r[7]==0]
print(f"PF last alive: {pf_alive_last}, total dead frames: {len(pf_dead)}, first dead: {pf_dead[:3] if pf_dead else None}")

# Find divergence RUNS
runs=[]; cur=None
for fr in sorted(pf_rows):
    sim=fr+1
    if sim not in mt_rows: continue
    px,py,pvy,onG,mode,mini,grav,alive=pf_rows[fr]
    mx,my,mvy,mmode,mmini,mgrav,_,_=mt_rows[sim]
    diff = (px!=mx) or (py!=my) or (pvy!=mvy) or (mode!=mmode)
    if diff:
        if cur and cur['end']==fr-1:
            cur['end']=fr; cur['n']+=1
        else:
            if cur: runs.append(cur)
            cur={'start':fr,'end':fr,'n':1,
                 'first':(px,py,pvy,mx,my,mvy,mode,mmode,mini,mmini)}
    else:
        if cur: runs.append(cur); cur=None
if cur: runs.append(cur)
print(f"\n{len(runs)} divergence runs:")
for r in runs:
    px,py,pvy,mx,my,mvy,mode,mmode,mini,mmini=r['first']
    print(f"  f={r['start']:>5}..{r['end']:<5} ({r['n']:>4} fr) PF({px:>5},{py:>4},vy={pvy:>6},m={mode}/{mini}) MT({mx:>5},{my:>4},vy={mvy:>6},m={mmode}/{mmini})")
