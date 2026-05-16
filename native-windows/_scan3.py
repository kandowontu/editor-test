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
runs=[]
cur=None
for sf in sorted(set(nes)&set(pf)):
    if sf-1 not in pf: continue
    n=nes[sf]; pp=pf[sf-1]
    ny=int(n[3]); py=int(pp[7])
    dy=ny-py
    gm=n[14]; mini=n[24]
    if abs(dy)>3:
        if cur and cur[1]==gm and cur[2]==mini and sf-cur[3]<=3:
            cur[3]=sf; cur[4]=max(cur[4], abs(dy))
        else:
            if cur: runs.append(cur)
            cur=[sf, gm, mini, sf, abs(dy)]
    else:
        if cur and sf-cur[3]>5:
            runs.append(cur); cur=None
if cur: runs.append(cur)
for r in runs:
    if r[3]-r[0]>=3:
        print(f"sf={r[0]}-{r[3]} ({r[3]-r[0]}f) gm={r[1]} mini={r[2]} max|dy|={r[4]}")
