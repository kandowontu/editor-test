import csv
from pathlib import Path
pf=Path(r"C:\Users\kando\AppData\Local\Temp\famidash_pf_trace_untitled_20260517_131605_527.csv")
mes=Path(r"C:\Users\kando\AppData\Local\Temp\famidash_mesen_trace_silentcircles_20260517_143603_468.csv")
pf_rows=list(csv.DictReader(pf.open(newline='')))
lines=mes.read_text(errors='replace').splitlines(); hdr=lines[1].split(',')
mes_rows=[]
for ln in lines[2:]:
 p=ln.split(',')
 if len(p)>=len(hdr) and p[0].isdigit(): mes_rows.append({hdr[i]:p[i] for i in range(len(hdr))})
mes_by={int(r['rom_frame']):r for r in mes_rows}

def eval_off(off):
 n=0; bad=0; absdy=0; absdx=0; first=None
 for pr in pf_rows:
  if int(pr['mode'])!=6: continue
  f=int(pr['frame']); mr=mes_by.get(f+off)
  if not mr or int(mr['gamemode'])!=6: continue
  n+=1
  px=int(pr['X_px']); mx=int(mr['px'])
  py=int(pr['Y_px']); my=int(mr['py'])
  pv=int(pr['VelY_fixed'],16)&0xFFFF; mv=int(mr['vel_y'])&0xFFFF
  absdy += abs(py-my); absdx += abs(px-mx)
  mismatch = (px!=mx or py!=my or pv!=mv or (int(pr['gravFlipped'])!=(int(mr['cp_gravity'])&0xFF)))
  if mismatch:
   bad+=1
   if first is None: first=(f,f+off,px,mx,py,my,pv,mv,int(pr['gravFlipped']),int(mr['cp_gravity'])&0xFF)
 return n,bad,absdx,absdy,first

best=None
for off in range(8,18):
 n,bad,absdx,absdy,first=eval_off(off)
 if n==0: continue
 print('off',off,'n',n,'bad',bad,'absdx',absdx,'absdy',absdy,'first',first)
 score=(bad,absdy,absdx)
 if best is None or score<best[0]: best=(score,off,n,bad,absdx,absdy,first)
print('BEST',best)
if best:
 off=best[1]
 f0=best[6][0]
 print('window for best off',off,'around',f0)
 for f in range(f0-3,f0+6):
  pr=next((x for x in pf_rows if int(x['frame'])==f),None)
  mr=mes_by.get(f+off)
  if not pr or not mr: continue
  if int(pr['mode'])!=6 or int(mr['gamemode'])!=6: continue
  print(f,f+off,'x',pr['X_px'],mr['px'],'y',pr['Y_px'],mr['py'],'vy',int(pr['VelY_fixed'],16)&0xFFFF,int(mr['vel_y'])&0xFFFF,'g',pr['gravFlipped'],int(mr['cp_gravity'])&0xFF)
