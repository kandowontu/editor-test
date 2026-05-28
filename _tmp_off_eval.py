import csv
from pathlib import Path
pf=Path(r"C:\Users\kando\AppData\Local\Temp\famidash_pf_trace_silentcircles_20260517_124616_205.csv")
mes=Path(r"C:\Users\kando\AppData\Local\Temp\famidash_mesen_trace_silentcircles_20260517_124916_016.csv")
pf_rows=list(csv.DictReader(pf.open(newline='')))
lines=mes.read_text().splitlines(); hdr=lines[1].split(','); mes_rows=[]
for ln in lines[2:]:
  if not ln.strip(): continue
  p=ln.split(',')
  if len(p)<len(hdr): continue
  mes_rows.append({hdr[i]:p[i] for i in range(len(hdr))})
mb={int(r['rom_frame']):r for r in mes_rows if r['rom_frame'].isdigit()}
# focus around first ball transition detected in pf
ball_frames=[int(r['frame']) for r in pf_rows if int(r['mode'])==2]
start=min(ball_frames); end=min(start+180, max(ball_frames))
print('ball range',start,end)
for off in range(8,17):
  n=0;mode=0;grav=0;vy=0;x=0;y=0
  for f in range(start,end+1):
    pr=pf_rows[f]
    mr=mb.get(f+off)
    if not mr: continue
    n+=1
    if int(pr['mode'])==int(mr['gamemode']): mode+=1
    pg=1 if pr['gravFlipped']=='1' else 0
    mg=1 if mr['cp_gravity'] in ('255','1') else 0
    if pg==mg: grav+=1
    if (int(pr['VelY_fixed'],16)&0xFFFF)==(int(mr['vel_y'])&0xFFFF): vy+=1
    if int(pr['X_px'])==int(mr['px']): x+=1
    if int(pr['Y_px'])==int(mr['py']): y+=1
  if n:
    print(off, 'n',n, 'mode',mode/n, 'grav',grav/n, 'vy',vy/n, 'x',x/n, 'y',y/n)
