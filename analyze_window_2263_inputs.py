import csv
from pathlib import Path

pf = Path(r"C:\Users\kando\AppData\Local\Temp\famidash_pf_trace_silentcircles_20260517_221457_007.csv")
mes = Path(r"C:\Users\kando\AppData\Local\Temp\famidash_mesen_trace_silentcircles_20260517_221939_010.csv")
off = 13

pf_rows = {int(r['frame']): r for r in csv.DictReader(pf.open(newline=''))}
lines = mes.read_text(errors='replace').splitlines()
hdr = lines[1].split(',')
mes_rows = {}
for ln in lines[2:]:
    p = ln.split(',')
    if len(p) >= len(hdr) and p[0].isdigit():
        d = {hdr[i]: p[i] for i in range(len(hdr))}
        mes_rows[int(d['rom_frame'])] = d

print('frame pf/rom y_pf y_mes vy_pf vy_mes input_pf a_cur a_next gamemode')
for f in range(2260, 2266):
    pr = pf_rows.get(f)
    mr = mes_rows.get(f + off)
    if not pr or not mr:
        continue
    print(f, f+off, int(pr['Y_px']), int(mr['py']), int(pr['VelY_fixed'],16)&0xFFFF, int(mr['vel_y'])&0xFFFF, int(pr['input']), mr['a_cur'], mr['a_next'], int(pr['mode']), int(mr['gamemode']))
