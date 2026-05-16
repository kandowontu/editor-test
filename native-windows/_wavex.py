pf=open(r"C:\Users\kando\AppData\Local\Temp\famidash_pf_trace.csv").read().splitlines()
mes=open(r"C:\Users\kando\AppData\Local\Temp\famidash_mesen_trace.csv").read().splitlines()
nes={}
for L in mes:
    if not L or L.startswith('#') or L.startswith('rom_frame') or L.startswith('nes_y_off'): continue
    p=L.split(',')
    if len(p)<15: continue
    try: sf=int(p[1])
    except: continue
    if sf not in nes: nes[sf]=p
pfs={}
for L in pf[1:]:
    p=L.split(',')
    pfs[int(p[0])]=p
prev_pfx=prev_nesx=None
print("sim |  NES_x NES_dx | PF_x PF_dx | dxdiff(NES-PF) gm")
for sf in range(3140, 3200):
    if sf not in nes or sf not in pfs: continue
    n=nes[sf]; pf_=pfs[sf]
    nesx=int(n[2]); pfx=int(pf_[6])
    nes_dx = nesx - prev_nesx if prev_nesx is not None else 0
    pf_dx = pfx - prev_pfx if prev_pfx is not None else 0
    print(f"{sf:4d} | {nesx:5d} {nes_dx:+3d} | {pfx:4d} {pf_dx:+3d} | {nes_dx - pf_dx:+3d} gm={n[14]}")
    prev_pfx=pfx; prev_nesx=nesx
