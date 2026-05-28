import csv
from pathlib import Path
pf=Path(r"C:\Users\kando\AppData\Local\Temp\famidash_pf_trace_untitled_20260517_152604_263.csv")
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
for f in sorted(pf_rows):
 pr=pf_rows[f]
 if int(pr['mode'])!=6: continue
 mr=mes_rows.get(f+off)
 if not mr or int(mr['gamemode'])!=6: continue
 if int(pr['X_px'])!=int(mr['px']) or int(pr['Y_px'])!=int(mr['py']) or ((int(pr['VelY_fixed'],16)&0xFFFF)!=(int(mr['vel_y'])&0xFFFF)):
  print('first',f,f+off,pr['X_px'],mr['px'],pr['Y_px'],mr['py'],int(pr['VelY_fixed'],16)&0xFFFF,int(mr['vel_y'])&0xFFFF)
  break
