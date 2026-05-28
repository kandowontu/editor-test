import csv
from pathlib import Path

pf = Path(r"C:\Users\kando\AppData\Local\Temp\famidash_pf_trace_silentcircles_20260517_221457_007.csv")
mes = Path(r"C:\Users\kando\AppData\Local\Temp\famidash_mesen_trace_silentcircles_20260517_221939_010.csv")
pf_debug = Path(r"C:\Users\kando\AppData\Local\Temp\famidash_pf_debug_silentcircles_20260517_221457.txt")
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

start = 2260
end = 2270
print('frame pf/rom x_pf x_mes y_pf y_mes vy_pf vy_mes mode_pf mode_mes')
for f in range(start, end + 1):
    pr = pf_rows.get(f)
    mr = mes_rows.get(f + off)
    if not pr or not mr:
        continue
    print(f, f + off, int(pr['X_px']), int(mr['px']), int(pr['Y_px']), int(mr['py']), int(pr['VelY_fixed'],16)&0xFFFF, int(mr['vel_y'])&0xFFFF, int(pr['mode']), int(mr['gamemode']))

print('\nPF debug lines around 2264:')
for line in pf_debug.read_text(errors='replace').splitlines():
    if '[PF f=226' in line or '[PF f=227' in line:
        if '[WAVE_PHYS]' in line or '[WAVE_EJECT]' in line or '[REPLAY' in line or '[PHYSICS]' in line:
            print(line)
