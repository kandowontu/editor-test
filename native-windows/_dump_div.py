import re, os
f = r'C:\Users\kando\AppData\Local\Temp\famidash_trace_compare_20260513_174402.txt'
lines = open(f, encoding='utf-8', errors='ignore').read().splitlines()
data = []
in_full = False
for L in lines:
    if 'Full per-frame' in L:
        in_full = True; continue
    if not in_full: continue
    m = re.match(r'^(\d+),(\d+),\((-?\d+),(-?\d+)\),\((-?\d+),(-?\d+)\),(-?\d+),(-?\d+)', L)
    if m:
        rom_f, sim_f, rx, ry, sx, sy, dx, dy = map(int, m.groups())
        data.append((rom_f, sim_f, rx, ry, sx, sy, dx, dy))

# Print max-divergence frames and surrounding context
print('=== Wave region (sim_cursor 3170-3210) ===')
for d in data:
    if 3170 <= d[1] <= 3210:
        print(f'rom_f={d[0]:4d} sim_f={d[1]:4d} rom=({d[2]:5d},{d[3]:4d}) sim=({d[4]:5d},{d[5]:4d}) dx={d[6]:4d} dy={d[7]:4d}')

print('\n=== Mode transitions: dx history every 100 sim frames ===')
prev = None
for d in data:
    if d[1] % 100 == 0 and d[1] != prev:
        prev = d[1]
        print(f'sim_f={d[1]:4d} dx={d[6]:4d} dy={d[7]:4d}  rom=({d[2]:5d},{d[3]:4d}) sim=({d[4]:5d},{d[5]:4d})')
