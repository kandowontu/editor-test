import csv,sys
pf={}
with open(r"C:\Users\kando\AppData\Local\Temp\famidash_pf_trace.csv") as f:
    rd=csv.reader(f); next(rd)
    for row in rd: pf[int(row[0])]=row
nes={}
with open(r"C:\Users\kando\AppData\Local\Temp\famidash_mesen_trace.csv",errors="ignore") as f:
    for L in f:
        p=L.strip().split(",")
        if len(p)<15 or not p[0].isdigit(): continue
        sf=int(p[1])
        if sf not in nes: nes[sf]=p
def sgn(h):
    v=int(h,16)
    return v-0x100000000 if v>=0x80000000 else v
ranges = [(330,360),(415,435),(790,805)]
for lo,hi in ranges:
    print(f"=== sf {lo}-{hi} ===")
    print("sf | NES x,y,vy gm | PF x,y,vy gm csl fsl ftl | dx dy")
    for sf in range(lo, hi):
        if sf not in nes or sf not in pf: continue
        n=nes[sf]; pp=pf[sf]
        nx,ny,nvy = int(n[2]),int(n[3]),int(n[4])
        px,py = int(pp[6]),int(pp[7])
        pvy = sgn(pp[3])
        csl = pp[13]; fsl = pp[16]; ftl = pp[17]
        print(f"{sf} | {nx},{ny},{nvy:+d} | {px},{py},{pvy:+d} csl={csl} fsl={fsl} ftl={ftl} | dx={nx-px} dy={ny-py:+d}")
