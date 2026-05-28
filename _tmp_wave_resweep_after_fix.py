import csv
from pathlib import Path
pf=Path(r"C:\Users\kando\AppData\Local\Temp\famidash_pf_trace_untitled_20260517_144831_029.csv")
mes=Path(r"C:\Users\kando\AppData\Local\Temp\famidash_mesen_trace_silentcircles_20260517_143603_468.csv")
pf_rows=list(csv.DictReader(pf.open(newline='')))
lines=mes.read_text(errors='replace').splitlines(); hdr=lines[1].split(',')
mes_by={}
for ln in lines[2:]:
 p=ln.split(',')
 if len(p)>=len(hdr) and p[0].isdigit():
  d={hdr[i]:p[i] for i in range(len(hdr))}
  mes_by[int(d['rom_frame'])]=d
best=None
for off in range(8,18):
 n=bad=absdy=absdx=0
 first=None
 for pr in pf_rows:
  if int(pr['mode'])!=6: continue
  f=int(pr['frame']); mr=mes_by.get(f+off)
  if not mr or int(mr['gamemode'])!=6: continue
  n+=1
  dx=abs(int(pr['X_px'])-int(mr['px'])); dy=abs(int(pr['Y_px'])-int(mr['py']))
  absdx+=dx; absdy+=dy
  pv=int(pr['VelY_fixed'],16)&0xFFFF; mv=int(mr['vel_y'])&0xFFFF
  if dx or dy or pv!=mv or int(pr['gravFlipped'])!=(int(mr['cp_gravity'])&0xFF):
   bad+=1
   if first is None: first=(f,f+off,int(pr['X_px']),int(mr['px']),int(pr['Y_px']),int(mr['py']),pv,mv)
 if n:
  print('off',off,'n',n,'bad',bad,'absdy',absdy,'absdx',absdx,'first',first)
  score=(bad,absdy,absdx)
  if best is None or score<best[0]: best=(score,off)
print('best',best)
