import csv
pf={}
with open(r"C:\Users\kando\AppData\Local\Temp\famidash_pf_trace.csv") as f:
    rd=csv.reader(f); next(rd)
    for row in rd: pf[int(row[0])]=row
nes={}
with open(r"C:\Users\kando\AppData\Local\Temp\famidash_mesen_trace.csv",errors="ignore") as f:
    for L in f:
        p=L.strip().split(",")
        if len(p)<26 or not p[0].isdigit(): continue
        sf=int(p[1])
        if sf not in nes: nes[sf]=p
def sgn(h):
    v=int(h,16); return v-0x100000000 if v>=0x80000000 else v
# check NES vs PF accounting for 1-frame offset (PF leads by 1)
prints=0
for sf in sorted(set(nes)&set(pf)):
    if sf-1 not in pf: continue
    n=nes[sf]; pp=pf[sf-1]
    nx,ny=int(n[2]),int(n[3])
    px,py=int(pp[6]),int(pp[7])
    dy=ny-py
    if abs(dy) > 3:
        gm=n[14]; gf=n[25]
        print(f"sf={sf} NES({nx},{ny}) gm={gm} cpg={gf} | PF[sf-1]({px},{py}) dy={dy:+d}")
        prints+=1
        if prints > 60: break
