import csv
pf={}
with open(r"C:\Users\kando\AppData\Local\Temp\famidash_pf_trace.csv") as f:
    rd=csv.reader(f); next(rd)
    for row in rd: pf[int(row[0])]=row
def sgn(h):
    v=int(h,16); return v-0x100000000 if v>=0x80000000 else v
prev_vy=0
for sf in sorted(pf.keys())[:400]:
    vy=sgn(pf[sf][3])
    if prev_vy==0 and vy!=0:
        print(f"sf={sf} PFvy 0 -> {vy} inp={pf[sf][4]}")
    prev_vy=vy
