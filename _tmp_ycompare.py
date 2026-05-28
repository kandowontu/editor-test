import csv, os
from pathlib import Path

t=Path(os.environ['TEMP'])
pf=t/'famidash_pf_trace_silentcircles_20260516_140915_697.csv'
mes=t/'famidash_mesen_trace_silentcircles_20260516_141210_235.csv'

pf_rows=list(csv.DictReader(open(pf,newline='')))
lines=open(mes).read().splitlines()
hdr=lines[1].split(',')
mes_rows=[]
for ln in lines[2:]:
    if not ln or ln.startswith('#'): continue
    parts=ln.split(',')
    if len(parts)<len(hdr): continue
    mes_rows.append({hdr[i]:parts[i] for i in range(len(hdr))})
mb={r['rom_frame']:r for r in mes_rows}

def i32hex(s):
    v=int(s,16)
    if v>=0x80000000: v-=0x100000000
    return v

for f in range(1908,1933):
    pr=next((r for r in pf_rows if int(r['frame'])==f),None)
    mr=mb.get(str(f+13))
    if not pr or not mr: continue
    ycur=int(pr['Y_px'])
    yfixed=i32hex(pr['Y_fixed'][2:])
    y8=(yfixed>>8)+8
    y7=(yfixed>>8)+7
    my=int(mr['py'])
    print(f, ycur-my, y8-my, y7-my)
