import xml.etree.ElementTree as ET

tree = ET.parse(r'c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\kratos.tmx')
root = tree.getroot()
layers = root.findall('layer')
layer = layers[0]
data = layer.find('data').text.strip()
rows_data = [r.strip() for r in data.split('\n') if r.strip()]

# Collision table (metatile index -> collision type)
# From MetatileCollision.cs
col_names = [
    "NONE","FC","FC","BOT","DtT","FC","FC","NONE","DtB","DtB","DtT","DtT","DtB","DtT","DtL","DtR",
    "ALL","DTH","DtB","DtB","DtT","TCS","ALL","DtT","DtB","TOP","DTH","DTH","DTH","DtL","DtT","DtR",
    "ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","NONE",
    "ALL","ALL","ALL","ALL","NONE","NONE","NONE","TCS","ALL","ALL","ALL","DtB","ALL","ALL","ALL","ALL",
    "ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","NONE",
    "ALL","ALL","ALL","ALL","TOP","TOP","TOP","TOP","BOT","BOT","BOT","DTH","DtB","DtT","DtR","DtL",
    "ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","NONE",
    "ALL","ALL","ALL","ALL","DRS","DtB","DLS","DtR","NONE","DtL","URS","DtT","ULS","DTH","ALL","DtB",
    "NONE","NONE","DTH","DTH","DtB","DtT","DtB","DtT","FC","FC","RT","LT","RT","LT","NONE","NSD",
    "SRD","SLD","SRU","SLU","SRD22R","SRD22L","SLD22R","SLD22L","SRU22R","SRU22L","SLU22R","SLU22L","SRD66T","SRD66B","SLD66B","SLD66T",
    "SRU66T","SRU66B","SLU66B","SLU66T","NSD","NSD","NSD","NSD","LSB","RSB","BLS","BRS","BSP","DLS2","DRS2","DBS",
]

def get_col_name(tmx_tile):
    if tmx_tile == 0:
        return "   "
    mt = tmx_tile - 1
    if mt < len(col_names):
        return col_names[mt][:3]
    return f"?{mt}"

# Map corridor from col 453 to col 570 (X=7248 to X=9120), rows 11-22
# Coin at X=9064, Y=248 -> col 566, row 15
print("Corridor collision map (cols 453-570, rows 11-22)")
print("Coin at col 566, row 15 (Y=240-248)")
print()

# Check if Y=240 row (row 15) is clear all the way
row15_blocks = []
for c in range(453, 571):
    tiles = rows_data[15].split(',')
    tid = int(tiles[c]) if c < len(tiles) else 0
    cn = get_col_name(tid)
    if cn.strip() not in ("", "NON"):
        row15_blocks.append((c, tid, cn))

print(f"Row 15 (Y=240) obstacles from col 453-570:")
if not row15_blocks:
    print("  NONE - completely clear!")
else:
    for c, tid, cn in row15_blocks:
        print(f"  col {c} (X={c*16}): TMX {tid} -> {cn}")

# Also check row 14 and 16
for row_idx in [14, 16]:
    blocks = []
    for c in range(453, 571):
        tiles = rows_data[row_idx].split(',')
        tid = int(tiles[c]) if c < len(tiles) else 0
        cn = get_col_name(tid)
        if cn.strip() not in ("", "NON"):
            blocks.append((c, tid, cn))
    print(f"\nRow {row_idx} (Y={row_idx*16}) obstacles from col 453-570:")
    if not blocks:
        print("  NONE - completely clear!")
    else:
        for c, tid, cn in blocks:
            print(f"  col {c} (X={c*16}): TMX {tid} -> {cn}")

# Print detailed grid for cols 453-475 (first section)
print("\n\nDetailed grid cols 453-465:")
print(f"{'':8s}", end='')
for c in range(453, 466):
    print(f"c{c:3d}", end=' ')
print()
for r in range(11, 23):
    tiles = rows_data[r].split(',')
    print(f"r{r:2d} Y{r*16:3d}: ", end='')
    for c in range(453, 466):
        tid = int(tiles[c]) if c < len(tiles) else 0
        cn = get_col_name(tid)
        print(f"{cn:4s}", end=' ')
    print()

# Print grid for cols 560-570 (near coin)
print("\n\nDetailed grid cols 560-572 (near coin at col 566):")
print(f"{'':8s}", end='')
for c in range(560, 573):
    print(f"c{c:3d}", end=' ')
print()
for r in range(11, 23):
    tiles = rows_data[r].split(',')
    print(f"r{r:2d} Y{r*16:3d}: ", end='')
    for c in range(560, 573):
        tid = int(tiles[c]) if c < len(tiles) else 0
        cn = get_col_name(tid)
        print(f"{cn:4s}", end=' ')
    print()
