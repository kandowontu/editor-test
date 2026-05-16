import csv, re
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

print(f"sim |  NES x,y,vy   |  PF x,y,vy   | dx dy dvy | gm")
prev_pfvy=None; prev_nesvy=None
for sf in range(380, 440):
    if sf not in nes or sf not in pf: continue
    n=nes[sf]; pp=pf[sf]
    nx,ny,nvy = int(n[2]),int(n[3]),int(n[4])
    px,py,pvy = int(pp[6]),int(pp[7]),int.from_bytes(int(pp[3],16).to_bytes(4,'little',signed=False),'little',signed=False)
    # signed: PF VelY_fixed printed as 0x... 32bit
    vy_int = int(pp[3],16)
    if vy_int >= 0x80000000: vy_int -= 0x100000000
    print(f"{sf:4d} | {nx:4d},{ny:3d},{nvy:+5d} | {px:4d},{py:3d},{vy_int:+5d} | {nx-px:+3d} {ny-py:+3d} {nvy-vy_int:+5d} | gm={n[14]}")
