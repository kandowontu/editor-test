pf=open(r"C:\Users\kando\AppData\Local\Temp\famidash_pf_trace.csv").read().splitlines()
mes=open(r"C:\Users\kando\AppData\Local\Temp\famidash_mesen_trace.csv").read().splitlines()
def s32(h):
    v=int(h,16)
    if v>=0x80000000: v-=0x100000000
    return v
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

# Walk the whole trace. Show every frame where dvy CHANGES (transitions in offset).
prev_dvy=None
print("sim |  NES_y NES_vy | PF_y PF_vy onG mode mini | dy dvy  CHG")
for sf in sorted(set(nes)&set(pfs)):
    n=nes[sf]; pf_=pfs[sf]
    nesy=int(n[3]); nesvy=int(n[10])
    pfy=int(pf_[7]); pfvy=s32(pf_[3])
    dvy=nesvy-pfvy
    if prev_dvy is None or dvy != prev_dvy:
        print(f"{sf:4d} | {nesy:5d} {nesvy:6d} | {pfy:4d} {pfvy:6d} {pf_[8]} {pf_[11]} {pf_[23]} | {nesy-pfy:+3d} {dvy:+5d}  CHG (was {prev_dvy})")
    prev_dvy=dvy
