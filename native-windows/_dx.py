import csv
pf={}
with open(r'C:\Users\kando\AppData\Local\Temp\famidash_pf_trace.csv') as f:
    rd=csv.reader(f); hdr=next(rd)
    for row in rd:
        pf[int(row[0])]=row
nes={}
with open(r'C:\Users\kando\AppData\Local\Temp\famidash_mesen_trace.csv',errors='ignore') as f:
    for L in f:
        if not L or L.startswith('#') or L.startswith('rom_frame') or L.startswith('nes_y_off'): continue
        p=L.strip().split(',')
        if len(p)<15: continue
        try: sf=int(p[1])
        except: continue
        if sf not in nes: nes[sf]=p

# find when dx first becomes non-zero, and track dx increments
prev_dx = 0
print("First 30 frames + transitions:")
last_print = -10
for sf in sorted(nes.keys())[:200]:
    if sf not in pf: continue
    nx=int(nes[sf][2]); px=int(pf[sf][6])
    dx = nx-px
    if dx != prev_dx or sf < 30:
        if sf - last_print > 0:
            print(f"sf={sf:4d} NES_x={nx:5d} PF_x={px:5d} dx={dx:+3d}  gm={nes[sf][14]}")
            last_print = sf
        prev_dx = dx
