import csv
from pathlib import Path
pf=Path(r"C:\Users\kando\AppData\Local\Temp\famidash_pf_trace_untitled_20260517_131605_527.csv")
mes=Path(r"C:\Users\kando\AppData\Local\Temp\famidash_mesen_trace_silentcircles_20260517_143603_468.csv")
off=13
pf_rows={int(r['frame']):r for r in csv.DictReader(pf.open(newline=''))}
lines=mes.read_text(errors='replace').splitlines(); hdr=lines[1].split(',')
mes_rows={}
for ln in lines[2:]:
 p=ln.split(',')
 if len(p)<len(hdr) or not p[0].isdigit(): continue
 d={hdr[i]:p[i] for i in range(len(hdr))}
 mes_rows[int(d['rom_frame'])]=d
for f in range(1882,1892):
 pr=pf_rows[f]; mr=mes_rows[f+off]
 if int(pr['mode'])!=6 or int(mr['gamemode'])!=6: continue
 print('f',f,'rom',f+off,
       'pfY_fixed',pr['Y_fixed'],'pfCam',pr['CamY_fixed'],'pfYpx',pr['Y_px'],'pfYlow',pr['Y_lowB'],'pfScrollSub',pr['ScrollYSubpx'],
       'mes_raw_y',mr['raw_y'],'mes_scroll_y_raw',mr['scroll_y_raw'],'mes_py',mr['py'],'mes_scroll_subpx',mr['scroll_y_subpx'],
       'pfVy',int(pr['VelY_fixed'],16)&0xFFFF,'mesVy',int(mr['vel_y'])&0xFFFF)
