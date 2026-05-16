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
# Show frames around when divergence appears
for sf in range(2400, 2560):
    if sf not in nes: continue
    if sf-1 not in pf: continue
    n=nes[sf]; pp=pf[sf-1]
    ny=int(n[3]); py=int(pp[7])
    nvy=int(n[10])
    dy=ny-py
    nx=int(n[2])
    if abs(dy)>=2 or sf%5==0:
        print(f"sf={sf} NES X={nx} Y={ny} vy={nvy} gm={n[14]} mini={n[24]} cpg={n[25]} | PFy={py} dy={dy:+d}")
