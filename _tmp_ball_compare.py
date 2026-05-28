import csv
from pathlib import Path

pf = Path(r"C:\Users\kando\AppData\Local\Temp\famidash_pf_trace_silentcircles_20260517_124616_205.csv")
mes = Path(r"C:\Users\kando\AppData\Local\Temp\famidash_mesen_trace_silentcircles_20260517_124916_016.csv")

pf_rows = list(csv.DictReader(pf.open(newline='', encoding='utf-8')))
lines = mes.read_text(encoding='utf-8', errors='replace').splitlines()
header = lines[1].split(',')
mes_rows = []
for ln in lines[2:]:
    if not ln.strip():
        continue
    parts = ln.split(',')
    if len(parts) < len(header):
        continue
    mes_rows.append({header[i]: parts[i] for i in range(len(header))})
mes_by_rom = {int(r['rom_frame']): r for r in mes_rows if r.get('rom_frame','').isdigit()}

# Find best frame offset between PF frame and Mesen rom_frame
best = None
for off in range(-40, 41):
    tot = 0
    mode_match = 0
    grav_match = 0
    for pr in pf_rows[200:4000]:
        f = int(pr['frame'])
        mr = mes_by_rom.get(f + off)
        if not mr:
            continue
        tot += 1
        if int(pr['mode']) == int(mr['gamemode']):
            mode_match += 1
        pf_grav = 1 if pr['gravFlipped'] == '1' else 0
        mes_grav = 1 if mr['cp_gravity'] in ('255','1') else 0
        if pf_grav == mes_grav:
            grav_match += 1
    if tot == 0:
        continue
    score = (mode_match + grav_match) / (2*tot)
    cand = (score, tot, off, mode_match/tot, grav_match/tot)
    if best is None or cand > best:
        best = cand

print('BEST_OFFSET', best)
off = best[2]

# First divergence in ball mode (gamemode 2 both sides)
first = None
for pr in pf_rows:
    f = int(pr['frame'])
    mr = mes_by_rom.get(f + off)
    if not mr:
        continue
    if int(pr['mode']) != 2 or int(mr['gamemode']) != 2:
        continue
    # Compare key physics observables
    pf_x = int(pr['X_px'])
    pf_y = int(pr['Y_px'])
    pf_vy = int(pr['VelY_fixed'], 16)
    mes_x = int(mr['px'])
    mes_y = int(mr['py'])
    mes_vy = int(mr['vel_y']) & 0xFFFF
    if (pf_x, pf_y, pf_vy) != (mes_x, mes_y, mes_vy):
        first = (f, f+off, pf_x, mes_x, pf_y, mes_y, pf_vy, mes_vy)
        break

print('FIRST_BALL_DIVERGENCE', first)
if first:
    f0 = first[0]-1
    f1 = first[0]+15
    print('WINDOW: pf_frame,mes_frame,pf_mode,mes_mode,pf_grav,mes_grav,pf_x,mes_x,pf_y,mes_y,pf_vy,mes_vy,input')
    pf_by = {int(r['frame']):r for r in pf_rows}
    for f in range(f0, f1+1):
        pr = pf_by.get(f)
        mr = mes_by_rom.get(f+off)
        if not pr or not mr:
            continue
        print(','.join(map(str,[
            f, f+off, pr['mode'], mr['gamemode'], pr['gravFlipped'], mr['cp_gravity'],
            pr['X_px'], mr['px'], pr['Y_px'], mr['py'],
            int(pr['VelY_fixed'],16), int(mr['vel_y']) & 0xFFFF, pr['input']
        ])))
