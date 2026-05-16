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
# PF leads NES by 1 frame: align pf[sf-1] with nes[sf]
for sf in range(2555, 2575):
    if sf not in nes or sf-1 not in pf: continue
    n=nes[sf]; pp=pf[sf-1]
    print(f"sf={sf} NES X={n[2]} Y={n[3]} vy={n[10]} sy={n[9]} | PF[{sf-1}] X={pp[6]} Y={pp[7]} vy={int(pp[3],16):d} CamY={pp[9]}")
