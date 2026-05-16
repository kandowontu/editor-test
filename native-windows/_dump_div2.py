import re, sys
f = r"C:\Users\kando\AppData\Local\Temp\famidash_trace_compare_20260513_182109.txt"
lines = open(f, encoding="utf-8", errors="ignore").read().splitlines()
data = []
in_full = False
for L in lines:
    if "Full per-frame" in L:
        in_full = True; continue
    if not in_full: continue
    m = re.match(r"^(\d+),(\d+),\((-?\d+),(-?\d+)\),\((-?\d+),(-?\d+)\),(-?\d+),(-?\d+)", L)
    if m:
        rom_f, sim_f, rx, ry, sx, sy, dx, dy = map(int, m.groups())
        data.append((rom_f, sim_f, rx, ry, sx, sy, dx, dy))

# Detect notable divergences: where |dy| jumps significantly
print("=== |dy|>=4 events (with surrounding context) ===")
prev_dy = 0
for i, d in enumerate(data):
    if d[1] > 3100: break
    if abs(d[7]) >= 4 and abs(d[7] - prev_dy) >= 3:
        # print +-3 frames
        lo = max(0, i-2); hi = min(len(data), i+4)
        print(f"-- around sim_f={d[1]}")
        for j in range(lo, hi):
            dd = data[j]
            print(f"  rom_f={dd[0]:4d} sim_f={dd[1]:4d} rom=({dd[2]:5d},{dd[3]:4d}) sim=({dd[4]:5d},{dd[5]:4d}) dx={dd[6]:3d} dy={dd[7]:4d}")
    prev_dy = d[7]
