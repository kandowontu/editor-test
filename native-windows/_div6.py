import csv
nes={}
with open(r"C:\Users\kando\AppData\Local\Temp\famidash_mesen_trace.csv",errors="ignore") as f:
    for L in f:
        p=L.strip().split(",")
        if len(p)<26 or not p[0].isdigit(): continue
        sf=int(p[1])
        if sf not in nes: nes[sf]=p
prev_vy=0
for sf in sorted(nes.keys())[:400]:
    vy=int(nes[sf][10])
    if prev_vy==0 and vy!=0:
        print(f"sf={sf} vy 0 -> {vy} a_cur={nes[sf][5]} a_next={nes[sf][4]} prev a_next={nes[sf-1][4] if sf-1 in nes else '?'}")
    prev_vy=vy
