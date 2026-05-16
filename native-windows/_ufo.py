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

def signed(h):
    v=int(h,16)
    if v >= 0x80000000: v -= 0x100000000
    return v

# Find UFO mode entry
for sf in sorted(nes.keys()):
    if int(nes[sf][14]) == 3:
        print(f"UFO entry sim_f={sf}")
        break
print()
print(f"sim |  NES x,y,vy   |  PF x,y,vy   | dx dy dvy | gm")
for sf in range(2390, 2410):
    if sf not in nes or sf not in pf: continue
    n=nes[sf]; pp=pf[sf]
    nx,ny,nvy = int(n[2]),int(n[3]),int(n[4])
    px,py = int(pp[6]),int(pp[7])
    pvy = signed(pp[3])
    print(f"{sf:4d} | {nx:4d},{ny:3d},{nvy:+5d} | {px:4d},{py:3d},{pvy:+5d} | {nx-px:+3d} {ny-py:+3d} {nvy-pvy:+5d} | gm={n[14]}")

print("\n--- UFO middle ---")
for sf in range(2560, 2580):
    if sf not in nes or sf not in pf: continue
    n=nes[sf]; pp=pf[sf]
    nx,ny,nvy = int(n[2]),int(n[3]),int(n[4])
    px,py = int(pp[6]),int(pp[7])
    pvy = signed(pp[3])
    print(f"{sf:4d} | {nx:4d},{ny:3d},{nvy:+5d} | {px:4d},{py:3d},{pvy:+5d} | {nx-px:+3d} {ny-py:+3d} {nvy-pvy:+5d} | gm={n[14]}")
