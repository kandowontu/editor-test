import csv
from pathlib import Path
pf=Path(r"C:\Users\kando\AppData\Local\Temp\famidash_pf_trace_untitled_20260517_131605_527.csv")
mes=Path(r"C:\Users\kando\AppData\Local\Temp\famidash_mesen_trace_silentcircles_20260517_143603_468.csv")
pf_rows=list(csv.DictReader(pf.open(newline='')))
lines=mes.read_text(errors='replace').splitlines()
hdr=lines[1].split(',')
mes_rows=[]
for ln in lines[2:]:
    if not ln.strip():
        continue
    p=ln.split(',')
    if len(p) < len(hdr):
        continue
    row={hdr[i]:p[i] for i in range(len(hdr))}
    if row['rom_frame'].isdigit():
        mes_rows.append(row)
mes_by={int(r['rom_frame']):r for r in mes_rows}
pf_by={int(r['frame']):r for r in pf_rows}
# Identify likely wave mode value in PF by longest contiguous run among non-0/1/2/3 in midgame
modes={}
for r in pf_rows:
    m=int(r['mode'])
    modes[m]=modes.get(m,0)+1
print('pf_mode_counts',sorted(modes.items()))
# Evaluate offsets by x/y agreement on wave-candidate mode=4 and mes gamemode=4
for off in range(8,18):
    n=0; score=0
    for f,pr in pf_by.items():
        if int(pr['mode'])!=4:
            continue
        mr=mes_by.get(f+off)
        if not mr: continue
        if int(mr['gamemode'])!=4: continue
        n+=1
        dy=abs(int(pr['Y_px'])-int(mr['py']))
        dx=abs(int(pr['X_px'])-int(mr['px']))
        score += dx + dy*2
    if n:
        print('off',off,'samples',n,'score',score,'avg',round(score/n,3))

best_off=13
first=None
for f in sorted(pf_by):
    pr=pf_by[f]
    if int(pr['mode'])!=4:
        continue
    mr=mes_by.get(f+best_off)
    if not mr or int(mr['gamemode'])!=4:
        continue
    # compare key state
    py=int(pr['Y_px']); my=int(mr['py'])
    pv=int(pr['VelY_fixed'],16) & 0xFFFF
    mv=int(mr['vel_y']) & 0xFFFF
    px=int(pr['X_px']); mx=int(mr['px'])
    if py!=my or pv!=mv or px!=mx:
        first=(f,f+best_off,px,mx,py,my,pv,mv,int(pr['gravFlipped']),int(mr['cp_gravity']) & 0xFF)
        break
print('first_wave_mismatch',first)
if first:
    f0=first[0]
    print('window: f rom pf_mode mes_mode px mx py my pv mv gpf gmes')
    for f in range(f0-2,f0+6):
        pr=pf_by.get(f)
        mr=mes_by.get(f+best_off)
        if not pr or not mr: continue
        print(f,f+best_off,pr['mode'],mr['gamemode'],pr['X_px'],mr['px'],pr['Y_px'],mr['py'],int(pr['VelY_fixed'],16)&0xFFFF,int(mr['vel_y'])&0xFFFF,pr['gravFlipped'],int(mr['cp_gravity'])&0xFF)
