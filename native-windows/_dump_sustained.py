import re
f = r"C:\Users\kando\AppData\Local\Temp\famidash_trace_compare_20260513_182109.txt"
lines = open(f, encoding="utf-8", errors="ignore").read().splitlines()
data = []
in_full = False
for L in lines:
    if "Full per-frame" in L: in_full = True; continue
    if not in_full: continue
    m = re.match(r"^(\d+),(\d+),\((-?\d+),(-?\d+)\),\((-?\d+),(-?\d+)\),(-?\d+),(-?\d+)", L)
    if m: data.append(tuple(map(int, m.groups())))

# Find sustained divergences: |dy|>=4 for >=5 consecutive frames
print("=== SUSTAINED |dy|>=4 (>=5 consecutive) ===")
i = 0
while i < len(data):
    if abs(data[i][7]) >= 4:
        j = i
        while j < len(data) and abs(data[j][7]) >= 4: j += 1
        if j - i >= 5:
            print(f"-- run {i}..{j-1} (len={j-i}) sim_f={data[i][1]}..{data[j-1][1]}")
            for k in range(i, min(j, i+8)):
                d = data[k]
                print(f"   rom_f={d[0]:4d} sim_f={d[1]:4d} rom=({d[2]:5d},{d[3]:4d}) sim=({d[4]:5d},{d[5]:4d}) dx={d[6]:3d} dy={d[7]:4d}")
            if j - i > 8:
                print(f"   ... {j-i-8} more ...")
                d = data[j-1]
                print(f"   rom_f={d[0]:4d} sim_f={d[1]:4d} rom=({d[2]:5d},{d[3]:4d}) sim=({d[4]:5d},{d[5]:4d}) dx={d[6]:3d} dy={d[7]:4d}")
        i = j
    else:
        i += 1
