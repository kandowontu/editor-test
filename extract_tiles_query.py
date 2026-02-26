import sys

tmx_path = r"C:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\jumper.tmx"
lines = open(tmx_path, "r").readlines()

data_start = None
for i, l in enumerate(lines):
    if "data encoding" in l and "csv" in l:
        data_start = i + 1
        break

rows = []
for i in range(data_start, data_start + 27):
    row = [int(x.strip()) for x in lines[i].strip().rstrip(",").split(",")]
    rows.append(row)

for label, sr in [("tileY=20 (storage row 23)", 23), ("tileY=21 (storage row 24)", 24)]:
    print(f"\n{label}:")
    print(f"{'tileX':>6} | {'TMX_ID':>6} | {'metatile(0-based)':>17}")
    print("-" * 35)
    for tx in range(710, 741):
        tid = rows[sr][tx]
        if tid != 0:
            mt = str(tid - 1)
        else:
            mt = "-"
        print(f"{tx:>6} | {tid:>6} | {mt:>17}")
