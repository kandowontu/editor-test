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
# Try different offsets, find best alignment by minimizing |X_diff|
for off in [-1,0,1]:
    tot=0; n=0
    for sf in range(2400, 2560):
        if sf not in nes or sf+off not in pf: continue
        nx=int(nes[sf][2]); px=int(pf[sf+off][6])
        tot += abs(nx-px); n+=1
    if n: print(f"offset={off:+d} avg|dx|={tot/n:.2f} n={n}")
