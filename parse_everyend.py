import xml.etree.ElementTree as ET

tree = ET.parse(r'c:\Editor Test\famidash\fan level collection\everyend.tmx')
root = tree.getroot()

# Find FG layer
fg_layer = None
for layer in root.findall('layer'):
    lid = layer.get('id')
    lname = layer.get('name', '(none)')
    w = layer.get('width')
    h = layer.get('height')
    print(f"Layer id={lid} name={lname} w={w} h={h}")
    if lid == '1':  # FG layer
        fg_layer = layer

if not fg_layer:
    print("No FG layer found!")
    exit(1)

# Parse CSV data
data_elem = fg_layer.find('data')
csv_text = data_elem.text.strip()
rows = csv_text.split('\n')
print(f"Total rows parsed: {len(rows)}")

# Extract tiles for columns 1265-1345 (1-indexed), rows 28-53 (1-indexed array rows)
COL_START = 1265  # 1-indexed
COL_END = 1345    # 1-indexed inclusive
ROW_START = 28    # 1-indexed array row
ROW_END = 53      # 1-indexed array row

# Tile collision types
TILE_NAMES = {
    0x25: "COL_ALL",      # 37
    0x2F: "COL_NONE",     # 47
    0x3C: "COL_FLOOR_CEIL", # 60
    0x01: "T_01",          # 1
    0x11: "T_17",          # 17
    0x12: "T_18",          # 18
    0x1C: "T_28",          # 28
    0x1D: "T_29",          # 29
    0x1B: "T_27",          # 27
    0x23: "T_35",          # 35
    0x30: "T_48",          # 48
    0x2E: "T_46",          # 46
}

# Collect non-zero tiles grouped by column
from collections import defaultdict
by_col = defaultdict(list)

for array_row in range(ROW_START, ROW_END + 1):
    row_idx = array_row - 1  # 0-indexed
    if row_idx >= len(rows):
        continue
    vals = rows[row_idx].strip().rstrip(',').split(',')
    for col in range(COL_START, COL_END + 1):
        col_idx = col - 1  # 0-indexed
        if col_idx >= len(vals):
            continue
        gid = int(vals[col_idx].strip())
        if gid != 0:
            world_row = array_row - 3
            name = TILE_NAMES.get(gid, f"T_{gid}")
            by_col[col].append((array_row, world_row, gid, name))

# Print results grouped by column
print("\n" + "="*80)
print(f"Non-zero tiles in cols {COL_START}-{COL_END}, array rows {ROW_START}-{ROW_END}")
print(f"(worldRow = arrayRow - 3)")
print("="*80)

for col in sorted(by_col.keys()):
    tiles = by_col[col]
    print(f"\nCol {col}:")
    for arr_row, w_row, gid, name in tiles:
        print(f"  aRow={arr_row:2d} wRow={w_row:2d}  GID=0x{gid:02X}({gid:3d}) {name}")

# Summary: identify COL_ALL walls
print("\n" + "="*80)
print("COL_ALL (0x25=37) WALL POSITIONS:")
print("="*80)
wall_positions = []
for col in sorted(by_col.keys()):
    for arr_row, w_row, gid, name in by_col[col]:
        if gid == 0x25:
            wall_positions.append((col, arr_row, w_row))
            print(f"  Col={col} aRow={arr_row} wRow={w_row}")

# Identify platforms (tiles 48=COL_NONE for platform surfaces, 37=COL_ALL for solid)
print("\n" + "="*80)
print("POTENTIAL PLATFORMS (solid tiles that could be landed on):")
print("="*80)
# A platform is a solid tile (37) with empty space above it
for col in sorted(by_col.keys()):
    for arr_row, w_row, gid, name in by_col[col]:
        if gid == 0x25:  # COL_ALL
            # Check if row above is empty
            row_above = arr_row - 1
            has_above = any(a == row_above for a, _, _, _ in by_col.get(col, []))
            if not has_above:
                print(f"  Col={col} wRow={w_row} (landing surface at worldRow {w_row})")
