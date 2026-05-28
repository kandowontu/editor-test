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
        pf_rows[fr]=row

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
    if sim not in mt_rows:
        mt_rows[sim]={k:p[col[k]] for k in hdr}

for fr in range(875, 895):
    pr=pf_rows.get(fr)
    mr=mt_rows.get(fr+1)
    if not pr or not mr: continue
    vs=pr['VelY_fixed']; pvy=int(vs,16) if vs.startswith('0x') else int(vs)
    if pvy>=0x80000000: pvy-=0x100000000
    mvy=int(mr['vel_y']);
    if mvy>=0x8000: mvy-=0x10000
    mark=' <<<' if pr['Y_px']!=mr['py'] or pvy!=mvy else ''
    print(f"  f={fr}: PF(X={pr['X_px']:>5} Y={pr['Y_px']:>4} vy={pvy:>6} oG={pr['onGround']} mode={pr['mode']}) MT(X={mr['px']:>5} Y={mr['py']:>4} vy={mvy:>6} tidx={mr['table_idx']} grav={mr['gravity_mod']}){mark}")
