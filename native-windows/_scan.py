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
print("Sustained divergences |dy|>=5 for >=3 frames")
run_start=-1; run_dy_max=0
for sf in sorted(set(nes.keys())&set(pf.keys())):
    n=nes[sf]; pp=pf[sf]
    ny=int(n[3]); py=int(pp[7])
    dy=ny-py
    if abs(dy) >= 5:
        if run_start<0: run_start=sf; run_dy_max=dy
        if abs(dy)>abs(run_dy_max): run_dy_max=dy
    else:
        if run_start>=0 and sf-run_start>=3:
            print(f"  run sf={run_start}-{sf-1} ({sf-run_start}f) max_dy={run_dy_max:+d} gm={nes[run_start][14]}")
        run_start=-1
