import csv, os
T=os.environ['TEMP']
pf=os.path.join(T,'famidash_pf_trace_dorabaebasic6_20260522_013436_781.csv')
mt=os.path.join(T,'famidash_mesen_trace_dorabaebasic6_20260522_014133_015.csv')

pfr={}
with open(pf) as f:
    r=csv.DictReader(f)
    for row in r:
        try: fr=int(row['frame'])
        except: continue
        vy=int(row['VelY_fixed'],16) if row['VelY_fixed'].startswith('0x') else int(row['VelY_fixed'])
        if vy>=0x80000000: vy-=0x100000000
        pfr[fr]=(int(row['Y_px']),vy,int(row['X_px']),int(row['mode']),int(row.get('onGround',0)))

with open(mt) as f:
    lines=f.readlines()
hi=0
for i,ln in enumerate(lines):
    if ln.startswith('rom_frame'): hi=i; break
hdr=lines[hi].strip().split(',')
col={n:i for i,n in enumerate(hdr)}
mtr={}
for ln in lines[hi+1:]:
    p=ln.rstrip('\n').split(',')
    if len(p)<len(hdr): continue
    # cube_data field may contain commas; collapse so positional indexing works
    if len(p)>len(hdr):
        extra=len(p)-len(hdr)
        ci=col['cube_data']
        p=p[:ci]+[','.join(p[ci:ci+extra+1])]+p[ci+extra+1:]
    try: sim=int(p[col['sim_cursor']])
    except: continue
    vy=int(p[col['vel_y']])
    if vy>=0x8000: vy-=0x10000
    mtr[sim]=(int(p[col['py']]),vy,int(p[col['px']]),int(p[col['gamemode']]),int(p[col['mini']]))

# Show ALL ball mode divergences with neighbors
print("Ball-mode (mode=2) divergences only:")
prev_div=None
for fr in sorted(pfr):
    sim=fr+1
    if sim not in mtr: continue
    pyp,pvy,pxp,pmode,_=pfr[fr]
    myp,mvy,mxp,mmode,_=mtr[sim]
    if pmode!=2 or mmode!=2: continue
    if pvy != mvy or pyp != myp or pxp != mxp:
        gap=""
        if prev_div and (fr-prev_div)>3: gap=f"  [GAP {fr-prev_div}]"
        print(f"  f={fr}: PF({pxp},{pyp},vy={pvy}) MT({mxp},{myp},vy={mvy}){gap}")
        prev_div=fr
