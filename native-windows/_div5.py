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
for sf in range(280, 335):
    if sf not in nes or sf not in pf: continue
    n=nes[sf]; pp=pf[sf]
    nx,ny,nvy,ng=int(n[2]),int(n[3]),int(n[10]),n[25]
    px,py=int(pp[6]),int(pp[7]); pvy=sgn(pp[3]); gF=pp[12]
    print(f"sf={sf} NES({nx},{ny}) vy={nvy:+d} cpg={ng} | PF({px},{py}) vy={pvy:+d} gF={gF} | dx={nx-px} dy={ny-py:+d}")
