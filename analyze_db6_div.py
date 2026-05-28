import csv, os, sys
T=os.environ['TEMP']
pf=os.path.join(T,'famidash_pf_trace_dorabaebasic6_20260522_013436_781.csv')
mt=os.path.join(T,'famidash_mesen_trace_dorabaebasic6_20260522_014133_015.csv')

# Load PF: frame -> (y_px, vy_fixed)
pfr={}
with open(pf,'r',newline='') as f:
    r=csv.DictReader(f)
    for row in r:
        try:
            fr=int(row['frame'])
        except:
            continue
        vy=int(row['VelY_fixed'],16) if row['VelY_fixed'].startswith('0x') else int(row['VelY_fixed'])
        # sign-extend (PF stores as uint32)
        if vy>=0x80000000: vy-=0x100000000
        y=int(row['Y_fixed'],16) if row['Y_fixed'].startswith('0x') else int(row['Y_fixed'])
        yp=int(row['Y_px'])
        xp=int(row['X_px'])
        pfr[fr]=(yp,vy,xp,int(row.get('mode',0)),int(row.get('onGround',0)))

# Load MT: skip first metadata line, then header line, then data
with open(mt,'r',newline='') as f:
    lines=f.readlines()
# Find header line containing rom_frame,sim_cursor
hdr_idx=None
for i,ln in enumerate(lines):
    if ln.startswith('rom_frame,sim_cursor'):
        hdr_idx=i; break
# Read meta nes_y_offset
meta=lines[0].strip().split(',')
nes_yoff=int(meta[1]) if meta[0]=='nes_y_offset' else 0
# Use positional CSV - fields can contain commas
hdr=lines[hdr_idx].strip().split(',')
col={n:i for i,n in enumerate(hdr)}
mtr={}
import re
for ln in lines[hdr_idx+1:]:
    parts=ln.rstrip('\n').split(',')
    if len(parts)<len(hdr):
        continue
    try:
        sim=int(parts[col['sim_cursor']])
    except:
        continue
    vy=int(parts[col['vel_y']])
    if vy>=0x8000: vy-=0x10000
    py=int(parts[col['py']])
    px=int(parts[col['px']])
    if px>=0x80000000: px-=0x100000000
    gm=int(parts[col['gamemode']])
    mtr[sim]=(py,vy,px,gm,0)

# Alignment PF.f=N END ↔ MT.sim=N+1 (per memory)
print(f"PF frames: {min(pfr)}..{max(pfr)}  MT sims: {min(mtr)}..{max(mtr)}")
print("First divergences (vy mismatch):")
divs=[]
for fr in sorted(pfr):
    sim=fr+1
    if sim not in mtr: continue
    pyp,pvy,pxp,pmode,pog=pfr[fr]
    myp,mvy,mxp,mmode,mmini=mtr[sim]
    if pvy != mvy or pyp != myp or pxp != mxp:
        divs.append((fr,sim,pxp,pyp,pvy,mxp,myp,mvy,pmode,mmode,pog,mmini))
        if len(divs)<=30:
            print(f"  PFf={fr} MTs={sim}: PFx={pxp} PFy={pyp} PFvy={pvy} | MTx={mxp} MTy={myp} MTvy={mvy} | pmode={pmode} mmode={mmode}")

if not divs:
    print("NO DIVERGENCES")
else:
    print(f"\nTotal divergences: {len(divs)}")
    print(f"First: f={divs[0][0]}")
