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

first_any = None
first_1px = None
for f in sorted(pf_rows):
    pr = pf_rows[f]
    if int(pr['mode']) != 6:
        continue
    mr = mes_rows.get(f + off)
    if not mr or int(mr['gamemode']) != 6:
        continue
    px = int(pr['X_px']); mx = int(mr['px'])
    py = int(pr['Y_px']); my = int(mr['py'])
    pv = int(pr['VelY_fixed'], 16) & 0xFFFF
    mv = int(mr['vel_y']) & 0xFFFF
    grav_pf = int(pr['gravFlipped'])
    grav_mes = int(mr['cp_gravity']) & 0xFF
    if px != mx or py != my or pv != mv or grav_pf != grav_mes:
        if first_any is None:
            first_any = (f, f + off, px, mx, py, my, pv, mv, grav_pf, grav_mes)
        if first_1px is None and px == mx and pv == mv and abs(py - my) == 1:
            first_1px = (f, f + off, px, py, my, pv)
            break

print('first_any', first_any)
print('first_1px', first_1px)

if first_any:
    f0 = first_any[0]
    print('window: pf_frame rom_frame x_pf x_mes y_pf y_mes vy_pf vy_mes')
    for f in range(f0 - 3, f0 + 8):
        pr = pf_rows.get(f)
        mr = mes_rows.get(f + off)
        if not pr or not mr:
            continue
        if int(pr['mode']) != 6 or int(mr['gamemode']) != 6:
            continue
        print(f, f + off, int(pr['X_px']), int(mr['px']), int(pr['Y_px']), int(mr['py']), int(pr['VelY_fixed'],16)&0xFFFF, int(mr['vel_y'])&0xFFFF)
