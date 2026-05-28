import csv
from collections import Counter,defaultdict
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
off=13
joint=Counter(); pfm=Counter(); msm=Counter()
for pr in pf_rows:
 f=int(pr['frame']); mr=mes_by.get(f+off)
 if not mr: continue
 a=int(pr['mode']); b=int(mr['gamemode'])
 joint[(a,b)]+=1; pfm[a]+=1; msm[b]+=1
print('pf modes aligned',pfm)
print('mes modes aligned',msm)
print('top joint',joint.most_common(12))
# check mismatches for pf mode 6 aligned with mes mode maybe 6
for mesmode in sorted({b for a,b in joint if a==6}):
 n=0; bad=0; first=None
 for pr in pf_rows:
  if int(pr['mode'])!=6: continue
  f=int(pr['frame']); mr=mes_by.get(f+off)
  if not mr or int(mr['gamemode'])!=mesmode: continue
  n+=1
  py=int(pr['Y_px']); my=int(mr['py'])
  pv=int(pr['VelY_fixed'],16)&0xFFFF; mv=int(mr['vel_y'])&0xFFFF
  if py!=my or pv!=mv:
   bad+=1
   if first is None: first=(f,f+off,py,my,pv,mv,int(pr['X_px']),int(mr['px']))
 print('pf6 vs mes',mesmode,'n',n,'bad',bad,'first',first)
