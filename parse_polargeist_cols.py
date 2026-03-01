import xml.etree.ElementTree as ET

tmx_path = r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\polargeist.tmx"
tree = ET.parse(tmx_path)
root = tree.getroot()

layers = {}
for layer in root.findall('layer'):
    name = layer.get('name', 'tiles')
    if name == '' or name is None:
        name = 'tiles'
    data = layer.find('data').text.strip()
    rows = []
    for line in data.split('\n'):
        line = line.strip().rstrip(',')
        if line:
            vals = [int(x) for x in line.split(',')]
            rows.append(vals)
    layers[name] = rows

COL_START = 485
COL_END = 600

print("=" * 80)
print(f"TILE LAYER - Columns {COL_START}-{COL_END}, ALL ROWS (0-26)")
print("=" * 80)

tile_rows = layers.get('tiles', layers.get(list(layers.keys())[0]))
print(f"Total rows: {len(tile_rows)}, Total cols: {len(tile_rows[0])}")

# Print rows 0-26 for columns 485-600
print(f"\n--- Rows 0-26, Columns {COL_START}-{COL_END} ---")
print(f"{'Row':<5}", end="")
# Print column headers every 5
for c in range(COL_START, COL_END + 1):
    if c % 10 == 0:
        print(f"{c:<5}", end="")
print()

for r in range(len(tile_rows)):
    row_data = tile_rows[r][COL_START:COL_END + 1]
    # Only print rows that have non-zero values in this range
    if any(v != 0 for v in row_data):
        print(f"R{r:<4}", end=" ")
        for i, v in enumerate(row_data):
            col = COL_START + i
            if v != 0:
                print(f"[{col}]={v}", end=" ")
        print()

print("\n" + "=" * 80)
print(f"COLUMN-BY-COLUMN SUMMARY - Rows 22-26, Columns {COL_START}-{COL_END}")
print("=" * 80)

for c in range(COL_START, COL_END + 1):
    vals = {}
    for r in range(22, min(27, len(tile_rows))):
        v = tile_rows[r][c]
        if v != 0:
            vals[r] = v
    if vals:
        desc = ", ".join([f"R{r}={v}" for r, v in sorted(vals.items())])
        print(f"Col {c}: {desc}")

print("\n" + "=" * 80)
print(f"COLUMN-BY-COLUMN SUMMARY - Rows 0-21, Columns {COL_START}-{COL_END}")
print("=" * 80)

for c in range(COL_START, COL_END + 1):
    vals = {}
    for r in range(0, 22):
        v = tile_rows[r][c]
        if v != 0:
            vals[r] = v
    if vals:
        desc = ", ".join([f"R{r}={v}" for r, v in sorted(vals.items())])
        print(f"Col {c}: {desc}")

print("\n" + "=" * 80)
print(f"SP (SPRITE) LAYER - Columns {COL_START}-{COL_END}")
print("=" * 80)

sp_rows = layers.get('SP', None)
if sp_rows:
    for r in range(len(sp_rows)):
        row_data = sp_rows[r][COL_START:COL_END + 1]
        if any(v != 0 for v in row_data):
            for i, v in enumerate(row_data):
                if v != 0:
                    col = COL_START + i
                    print(f"SP R{r} Col{col} (X={col*16}, Y={r*16}): tile {v} (sprite {v-256})")

print("\n" + "=" * 80)
print("STAIRCASE ANALYSIS - Cols 501-549")
print("=" * 80)

for c in range(501, 550):
    col_tiles = []
    for r in range(len(tile_rows)):
        v = tile_rows[r][c]
        if v != 0:
            col_tiles.append((r, v))
    if col_tiles:
        print(f"Col {c}: {col_tiles}")

print("\n" + "=" * 80)
print("WALL ANALYSIS - Cols 585-595")
print("=" * 80)

for c in range(585, 596):
    col_tiles = []
    for r in range(len(tile_rows)):
        v = tile_rows[r][c]
        if v != 0:
            col_tiles.append((r, v))
    if col_tiles:
        print(f"Col {c}: {col_tiles}")

print("\n" + "=" * 80)
print("UNIQUE TILE VALUES in cols 485-600")
print("=" * 80)
tile_set = set()
for c in range(COL_START, COL_END + 1):
    for r in range(len(tile_rows)):
        v = tile_rows[r][c]
        if v != 0:
            tile_set.add(v)
print(f"Tile IDs: {sorted(tile_set)}")

# count each tile
from collections import Counter
tile_counts = Counter()
for c in range(COL_START, COL_END + 1):
    for r in range(len(tile_rows)):
        v = tile_rows[r][c]
        if v != 0:
            tile_counts[v] += 1
print("\nTile usage counts:")
for tid, cnt in sorted(tile_counts.items()):
    print(f"  Tile {tid}: {cnt} occurrences")
