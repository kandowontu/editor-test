import csv
from pathlib import Path

pf = Path(r"C:\Users\kando\AppData\Local\Temp\famidash_pf_trace_untitled_20260517_175207_652.csv")
mes = Path(r"C:\Users\kando\AppData\Local\Temp\famidash_mesen_trace_silentcircles_20260517_143603_468.csv")
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

for f in range(1883, 1891):
    pr = pf_rows.get(f)
    mr = mes_rows.get(f + off)
    if not pr or not mr:
        continue
    if int(pr['mode']) != 6 or int(mr['gamemode']) != 6:
        continue
    print(f, f + off, 'PF', pr['X_px'], pr['Y_px'], int(pr['VelY_fixed'], 16) & 0xFFFF,
          'MES', mr['px'], mr['py'], int(mr['vel_y']) & 0xFFFF)
