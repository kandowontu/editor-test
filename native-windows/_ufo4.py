import csv
sf2pf={}
with open(r"C:\Users\kando\AppData\Local\Temp\famidash_replay_20260514_001406.csv") as f:
    rd=csv.reader(f); next(rd); next(rd)
    for row in rd:
        if len(row)<4: continue
        sf2pf[int(row[0])]=row
nes={}
with open(r"C:\Users\kando\AppData\Local\Temp\famidash_mesen_trace_20260514_001406.csv",errors="ignore") as f:
    rd=csv.reader(f); next(rd); next(rd)
    for row in rd:
        if len(row)<15: continue
        sf=int(row[1])
        if sf not in nes: nes[sf]=row
def sgn16(v):
    return v-0x10000 if v>=0x8000 else v
print(f"sf | NES x,y,vy gm | PF x,y,a")
for sf in range(2900, 2980):
    if sf not in nes or sf not in sf2pf: continue
    n=nes[sf]; pp=sf2pf[sf]
    nx,ny = int(n[2]),int(n[3])
    nvy = sgn16(int(n[10]))
    px,py,a = int(pp[1]),int(pp[2]),int(pp[3])
    print(f"{sf:4d} | {nx:5d},{ny:3d},{nvy:+5d} gm={n[14]} a={n[5]} | {px:5d},{py:3d},a={a} | dx={nx-px:+3d} dy={ny-py:+3d}")
