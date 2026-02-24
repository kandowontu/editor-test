import xml.etree.ElementTree as ET
from collections import defaultdict

tree = ET.parse(r'c:\Editor Test\famidash\fan level collection\everyend.tmx')
root = tree.getroot()

fg_layer = root.findall('layer')[0]  # Layer id=1
data_elem = fg_layer.find('data')
csv_text = data_elem.text.strip()
rows = csv_text.split('\n')

COL_START, COL_END = 1265, 1345
ROW_START, ROW_END = 28, 53

# Collision type lookup (from famidash engine, tilesFirstGid=1)
# GID is 1-indexed. The collision type is determined by the tile index.
# Key collision GIDs based on the tileset:
COLLISION = {
    1: "BG",        # background/filler - typically treated as solid depending on context
    17: "SLOPE_L",   # slope or decorative
    18: "SLOPE_R",   # slope or decorative  
    27: "CORNER_BL", # corner
    28: "CORNER_BR", # corner
    29: "CORNER_TL", # corner
    35: "COL_R",     # right edge of platform
    37: "COL_ALL",   # solid block (full collision)
    46: "COL_NONE",  # no collision (decorative/pass-through)
    47: "COL_NONE2", # no collision
    48: "COL_FC",    # floor/ceiling collision
    60: "COL_FC2",   # floor/ceiling
}

# Parse the grid
grid = {}  # (col, array_row) -> gid
for array_row in range(ROW_START, ROW_END + 1):
    row_idx = array_row - 1
    if row_idx >= len(rows):
        continue
    vals = rows[row_idx].strip().rstrip(',').split(',')
    for col in range(COL_START, COL_END + 1):
        col_idx = col - 1
        if col_idx >= len(vals):
            continue
        gid = int(vals[col_idx].strip())
        if gid != 0:
            grid[(col, array_row)] = gid

# Print collision-relevant tiles only (skip GID=1 which is background filler)
print("="*90)
print("NON-FILLER TILES (GID != 0 and GID != 1) in cols 1265-1345, rows 28-53")
print("worldRow = arrayRow - 3")
print("="*90)

by_col = defaultdict(list)
for (col, arow), gid in sorted(grid.items()):
    if gid != 1:  # Skip background filler
        wrow = arow - 3
        by_col[col].append((arow, wrow, gid))

for col in sorted(by_col.keys()):
    tiles = by_col[col]
    parts = []
    for arow, wrow, gid in tiles:
        name = COLLISION.get(gid, f"?{gid}")
        parts.append(f"wR{wrow}=0x{gid:02X}({name})")
    print(f"C{col}: {', '.join(parts)}")

# VISUAL MAP - show only collision-significant tiles
# COL_ALL=37='#', COL_FC=48='=', COL_NONE=46='.', slopes=17,18='/', background=1=' '
# 35='>', 27,28,29=corners
print("\n" + "="*90)
print("VISUAL MAP (cols 1272-1342, wRows 25-50)")
print("Legend: #=COL_ALL(37) ==COL_FC(48) .=COL_NONE(46) /=slope(17) \\=slope(18)")
print("        >=COL_R(35) <=COL_L(37) c=corner(27-29) ~=other  ' '=empty/filler(0,1)")
print("="*90)

VMAP_CS, VMAP_CE = 1272, 1342
VMAP_RS, VMAP_RE = 28, 53  # array rows

def tile_char(gid):
    if gid == 0 or gid == 1: return ' '
    if gid == 37: return '#'
    if gid == 48: return '='
    if gid == 46: return '.'
    if gid == 17: return '/'
    if gid == 18: return '\\'
    if gid == 35: return '>'
    if gid in (27, 28, 29): return 'c'
    return '~'

# Header
header = "wR\\C "
for c in range(VMAP_CS, VMAP_CE + 1):
    if c % 10 == 0:
        header += str((c // 10) % 10)
    elif c % 5 == 0:
        header += '+'
    else:
        header += ' '
print(header)

sub_header = "     "
for c in range(VMAP_CS, VMAP_CE + 1):
    sub_header += str(c % 10)
print(sub_header)

for arow in range(VMAP_RS, VMAP_RE + 1):
    wrow = arow - 3
    line = f"w{wrow:2d}: "
    for col in range(VMAP_CS, VMAP_CE + 1):
        gid = grid.get((col, arow), 0)
        line += tile_char(gid)
    print(line)

# Specific analysis
print("\n" + "="*90)
print("ANALYSIS: COL_ALL (0x25=37) walls")
print("="*90)
walls = [(c, ar, ar-3) for (c, ar), g in grid.items() if g == 37]
walls.sort()
for c, ar, wr in walls:
    if COL_START <= c <= COL_END:
        print(f"  Col={c} wRow={ar-3}")

print("\n" + "="*90)
print("ANALYSIS: Platforms (COL_FC=48 and COL_ALL=37 tiles)")
print("These are surfaces the player can potentially land on")
print("="*90)
# Find tiles where the tile above is empty (GID=0) or filler (GID=1)
for col in range(VMAP_CS, VMAP_CE + 1):
    for arow in range(VMAP_RS, VMAP_RE + 1):
        gid = grid.get((col, arow), 0)
        if gid in (37, 48):  # Solid or floor/ceiling
            gid_above = grid.get((col, arow - 1), 0)
            if gid_above in (0, 1):  # Empty above = landing surface
                wrow = arow - 3
                name = "COL_ALL" if gid == 37 else "COL_FC"
                print(f"  Col={col} wRow={wrow} ({name}) - landable from above")

print("\n" + "="*90)
print("ANALYSIS: Path from wRow~40 down to wRow~28 between cols 1272-1342")
print("Looking for solid/platform tiles at each wRow level")
print("="*90)
for target_wrow in range(28, 51):
    target_arow = target_wrow + 3
    platforms = []
    for col in range(1272, 1343):
        gid = grid.get((col, target_arow), 0)
        if gid in (37, 48, 35, 17):  # Solid-ish tiles
            platforms.append((col, gid))
    if platforms:
        parts = [f"C{c}={COLLISION.get(g,str(g))}" for c, g in platforms]
        print(f"  wRow={target_wrow}: {', '.join(parts)}")
    else:
        print(f"  wRow={target_wrow}: (empty)")
