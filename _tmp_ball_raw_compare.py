import csv
from pathlib import Path
pf=Path(r"C:\Users\kando\AppData\Local\Temp\famidash_pf_trace_silentcircles_20260517_124616_205.csv")
mes=Path(r"C:\Users\kando\AppData\Local\Temp\famidash_mesen_trace_silentcircles_20260517_124916_016.csv")
off=13
pf_rows=list(csv.DictReader(pf.open(newline='')))
lines=mes.read_text().splitlines(); hdr=lines[1].split(',')
mes_rows=[]
for ln in lines[2:]:
  if not ln.strip(): continue
  p=ln.split(',')
  if len(p)<len(hdr): continue
  mes_rows.append({hdr[i]:p[i] for i in range(len(hdr))})
mb={int(r['rom_frame']):r for r in mes_rows if r['rom_frame'].isdigit()}
pf_by={int(r['frame']):r for r in pf_rows}
print('f,rom,pfY_fixed,mes_raw_y,pfCam_fixed,mes_scroll_y_raw,pfYpx,mes_py,pfMini,mesMini,pfVel,mesVel,pfOnGround')
for f in range(1940,1951):
  pr=pf_by[f]; mr=mb[f+off]
  print(','.join(map(str,[f,f+off,pr['Y_fixed'],mr['raw_y'],pr['CamY_fixed'],mr['scroll_y_raw'],pr['Y_px'],mr['py'],pr['mini'],mr['mini'],int(pr['VelY_fixed'],16)&0xFFFF,int(mr['vel_y'])&0xFFFF,pr['onGround']])))
