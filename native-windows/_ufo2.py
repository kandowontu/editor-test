import csv
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
print(f"sf | NES x,y,vy | PF x,y,vy | dx dy dvy")
for sf in range(2880, 2965):
    if sf not in nes or sf not in pf: continue
    n=nes[sf]; pp=pf[sf]
    nx,ny,nvy = int(n[2]),int(n[3]),int(n[4])
    px,py = int(pp[6]),int(pp[7])
    pvy = sgn(pp[3])
    print(f"{sf:4d} | {nx:5d},{ny:3d},{nvy:+5d} | {px:5d},{py:3d},{pvy:+5d} | dx={nx-px:+3d} dy={ny-py:+3d} dvy={nvy-pvy:+5d}")
