import csv
from pathlib import Path
pf=Path(r"C:\Users\kando\AppData\Local\Temp\famidash_pf_trace_silentcircles_20260517_124616_205.csv")
mes=Path(r"C:\Users\kando\AppData\Local\Temp\famidash_mesen_trace_silentcircles_20260517_124916_016.csv")
off=13
pf_rows=list(csv.DictReader(pf.open(newline='')))
lines=mes.read_text().splitlines(); hdr=lines[1].split(','); mes_rows=[]
for ln in lines[2:]:
    if not ln.strip(): continue
    p=ln.split(',')
    if len(p)<len(hdr): continue
    mes_rows.append({hdr[i]:p[i] for i in range(len(hdr))})
mb={int(r['rom_frame']):r for r in mes_rows if r['rom_frame'].isdigit()}
first=None
for pr in pf_rows:
    f=int(pr['frame']); mr=mb.get(f+off)
    if not mr: continue
    if int(pr['mode'])!=2 or int(mr['gamemode'])!=2: continue
    pg=1 if pr['gravFlipped']=='1' else 0
    mg=1 if mr['cp_gravity'] in ('255','1') else 0
    pv=int(pr['VelY_fixed'],16)&0xFFFF
    mv=int(mr['vel_y'])&0xFFFF
    pog=int(pr['onGround'])
    # cube_data bit meanings unknown; low bit often onground/death flags; keep simple compare using cp_y delta maybe not.
    if pg!=mg or pv!=mv:
        first=(f,f+off,pg,mg,pv,mv,pog,int(mr['cp_y']))
        break
print('FIRST_MISMATCH(gravity/vel):',first)
if first:
    s=first[0]-1
    e=first[0]+20
    print('f,rom,modeP,modeM,gravP,gravM,vyP,vyM,xP,xM,yP,yM,onGroundP,input')
    pby={int(r['frame']):r for r in pf_rows}
    for f in range(s,e+1):
        pr=pby.get(f); mr=mb.get(f+off)
        if not pr or not mr: continue
        print(','.join(map(str,[f,f+off,pr['mode'],mr['gamemode'],pr['gravFlipped'],mr['cp_gravity'],int(pr['VelY_fixed'],16)&0xFFFF,int(mr['vel_y'])&0xFFFF,pr['X_px'],mr['px'],pr['Y_px'],mr['py'],pr['onGround'],pr['input']])))
