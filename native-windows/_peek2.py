pf=open(r"C:\Users\kando\AppData\Local\Temp\famidash_pf_trace.csv").read().splitlines()
mes=open(r"C:\Users\kando\AppData\Local\Temp\famidash_mesen_trace.csv").read().splitlines()
def s32(h):
    v=int(h,16); 
    if v>=0x80000000: v-=0x100000000
    return v

# build sim-indexed rows
nes={}
for L in mes:
    if not L or L.startswith('#') or L.startswith('rom_frame') or L.startswith('nes_y_off'): continue
    p=L.split(',')
    if len(p)<15: continue
    try: sf=int(p[1])
    except: continue
    if sf not in nes:
        nes[sf]=p
pfs={}
for L in pf[1:]:
    p=L.split(',')
    pfs[int(p[0])]=p

print("sim |  NES_y NES_vy a_next gm grav | PF_y PF_vy onG mode grv mini | dy dvy")
for sf in range(395,440):
    if sf not in nes or sf not in pfs: continue
    n=nes[sf]; pf_=pfs[sf]
    nesy=int(n[3]); nesvy=int(n[10])
    pfy=int(pf_[7]); pfvy=s32(pf_[3])
    print(f"{sf:4d} | {nesy:5d} {nesvy:6d} {n[4]:1s} {n[14]} {n[12]} | {pfy:4d} {pfvy:6d} {pf_[8]} {pf_[11]} {pf_[12]} {pf_[23]} | {nesy-pfy:+3d} {nesvy-pfvy:+5d}")
