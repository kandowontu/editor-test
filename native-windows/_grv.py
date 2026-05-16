import csv
nes={}
with open(r"C:\Users\kando\AppData\Local\Temp\famidash_mesen_trace.csv",errors="ignore") as f:
    for L in f:
        p=L.strip().split(",")
        if len(p)<26 or not p[0].isdigit(): continue
        sf=int(p[1])
        if sf not in nes: nes[sf]=p
prev_g="?"
for sf in sorted(nes.keys()):
    g=nes[sf][25]  # cp_gravity
    if g!=prev_g:
        print(f"sf={sf} x={nes[sf][2]} y={nes[sf][3]} cp_grav={g} gm={nes[sf][14]}")
        prev_g=g
