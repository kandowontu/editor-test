import csv
from pathlib import Path
pf=Path(r"C:\Users\kando\AppData\Local\Temp\famidash_pf_trace_untitled_20260517_150151_715.csv")
mes=Path(r"C:\Users\kando\AppData\Local\Temp\famidash_mesen_trace_silentcircles_20260517_143603_468.csv")
off=13
pf_rows={int(r['frame']):r for r in csv.DictReader(pf.open(newline=''))}
lines=mes.read_text(errors='replace').splitlines(); hdr=lines[1].split(',')
mes_rows={}
for ln in lines[2:]:
 p=ln.split(',')
 if len(p)>=len(hdr) and p[0].isdigit():
  d={hdr[i]:p[i] for i in range(len(hdr))}
  mes_rows[int(d['rom_frame'])]=d
first_any=None; first_1px=None
for f in sorted(pf_rows):
 pr=pf_rows[f]
 if int(pr['mode'])!=6: continue
 mr=mes_rows.get(f+off)
 if not mr or int(mr['gamemode'])!=6: continue
 px=int(pr['X_px']); mx=int(mr['px'])
 py=int(pr['Y_px']); my=int(mr['py'])
 pv=int(pr['VelY_fixed'],16)&0xFFFF; mv=int(mr['vel_y'])&0xFFFF
 if px!=mx or py!=my or pv!=mv or int(pr['gravFlipped'])!=(int(mr['cp_gravity'])&0xFF):
  if first_any is None: first_any=(f,f+off,px,mx,py,my,pv,mv)
  if first_1px is None and px==mx and pv==mv and abs(py-my)==1:
   first_1px=(f,f+off,px,py,my,pv)
print('first_any_wave_mismatch',first_any)
print('first_1px_wave_mismatch',first_1px)
if first_any:
 f0=first_any[0]
 print('window: f rom x_px y_px vy')
 for f in range(f0-2,f0+6):
  pr=pf_rows.get(f); mr=mes_rows.get(f+off)
  if not pr or not mr: continue
  if int(pr['mode'])!=6 or int(mr['gamemode'])!=6: continue
  print(f,f+off,pr['X_px'],mr['px'],pr['Y_px'],mr['py'],int(pr['VelY_fixed'],16)&0xFFFF,int(mr['vel_y'])&0xFFFF)
