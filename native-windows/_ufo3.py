import csv
pf={}
with open(r"C:\Users\kando\AppData\Local\Temp\famidash_pf_trace.csv") as f:
    rd=csv.reader(f); next(rd)
    for row in rd: pf[int(row[0])]=row
nes={}
with open(r"C:\Users\kando\AppData\Local\Temp\famidash_mesen_trace.csv",errors="ignore") as f:
    rd=csv.reader(f); next(rd); next(rd)
    for row in rd:
        if len(row)<15: continue
        sf=int(row[1])
        if sf not in nes: nes[sf]=row
def sgn16(v):
    return v-0x10000 if v>=0x8000 else v
def sgn32(h):
    v=int(h,16)
    return v-0x100000000 if v>=0x80000000 else v
print(f"sf | NES x,y,vy gm | PF x,y,vy gm")
for sf in range(2880, 2965):
    if sf not in nes or sf not in pf: continue
    n=nes[sf]; pp=pf[sf]
    nx,ny = int(n[2]),int(n[3])
    nvy = sgn16(int(n[10]))
    px,py = int(pp[6]),int(pp[7])
    pvy = sgn32(pp[3])
    print(f"{sf:4d} | {nx:5d},{ny:3d},{nvy:+5d} gm={n[14]} | {px:5d},{py:3d},{pvy:+5d} gm={pp[11]} | dy={ny-py:+3d}")
