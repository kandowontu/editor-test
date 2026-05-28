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
        pf_rows[fr]=row
        pf_rows[fr]['_vy']=vy

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
        vy=int(p[col['vel_y']])
        if vy>=0x8000: vy-=0x10000
        mt_rows[sim]={k:p[col[k]] for k in hdr}
        mt_rows[sim]['_vy']=vy

# get target range from args
lo=int(sys.argv[1]) if len(sys.argv)>1 else 2195
hi_=int(sys.argv[2]) if len(sys.argv)>2 else 2245
for fr in range(lo,hi_):
    pr=pf_rows.get(fr)
    mr=mt_rows.get(fr+1)
    if not pr or not mr: continue
    mark=''
    if pr['X_px']!=mr['px'] or pr['Y_px']!=mr['py'] or pr['_vy']!=mr['_vy']:
        mark=' <<<'
    print(f"f={fr:>4}: PF(X={pr['X_px']:>5} Y={pr['Y_px']:>4} vy={pr['_vy']:>6} m={pr['mode']} oG={pr['onGround']} gF={pr.get('gravFlipped','?')}) MT(X={mr['px']:>5} Y={mr['py']:>4} vy={mr['_vy']:>6} tidx={mr['table_idx']} grav={mr['gravity_mod']}){mark}")
