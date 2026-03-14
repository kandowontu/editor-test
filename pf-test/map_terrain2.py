import xml.etree.ElementTree as ET
import re
import sys
import traceback

outfile = open(r"C:\Editor Test\pf-test\terrain_output.txt", "w")
def printout(*args, **kwargs):
    print(*args, **kwargs, file=outfile)
    outfile.flush()

try:

# === 1. Parse collision table from MetatileCollision.cs ===
cs_path = r"C:\Editor Test\native-windows\MetatileCollision.cs"
with open(cs_path, 'r') as f:
    cs_text = f.read()

# Extract the mapping string between @" and ";
match = re.search(r'string mappingText = @"(.*?)";', cs_text, re.DOTALL)
mapping_text = match.group(1)

# Split with RemoveEmptyEntries equivalent - split on newlines, skip empty
lines = [l for l in mapping_text.split('\n') if l.strip()]

# Parse COL_* entries
col_rx = re.compile(r'COL_[A-Z0-9_]+')
collision_table = ['COL_NONE'] * 256
for idx, raw in enumerate(lines):
    if idx >= 256:
        break
    m = col_rx.search(raw.strip())
    if m:
        collision_table[idx] = m.group(0)

# === 2. Character mapping ===
def col_to_char(col_name):
    if col_name == 'COL_NONE':
        return '.'
    if col_name == 'COL_ALL':
        return 'A'
    if col_name == 'COL_TOP':
        return 'T'
    if col_name == 'COL_BOTTOM':
        return 'B'
    if col_name == 'COL_FLOOR_CEIL':
        return 'F'
    if 'DEATH' in col_name:
        return 'D'
    if 'SPIKE' in col_name:
        return 's'
    if 'SLOPE' in col_name:
        return '/'
    if col_name in ('COL_LEFT', 'COL_RIGHT', 'COL_UP_LEFT', 'COL_UP_RIGHT',
                     'COL_DOWN_LEFT', 'COL_DOWN_RIGHT', 'COL_NO_SIDE',
                     'COL_TOP_LEFT_STAIRS', 'COL_TOP_RIGHT_STAIRS',
                     'COL_BOTTOM_LEFT_STAIRS', 'COL_BOTTOM_RIGHT_STAIRS',
                     'COL_TOP_LEFT_BOTTOM_RIGHT', 'COL_TOP_RIGHT_BOTTOM_LEFT'):
        return 'X'
    return 'X'

# === 3. Parse TMX ===
tmx_path = r"C:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_A\clubstep.tmx"
tree = ET.parse(tmx_path)
root = tree.getroot()

map_width = int(root.attrib['width'])
map_height = int(root.attrib['height'])

layers = root.findall('layer')
# Layer 0 = terrain, Layer 1 = sprites
terrain_layer = layers[0]
sprite_layer = layers[1] if len(layers) > 1 else None

def parse_layer_csv(layer_elem):
    data_elem = layer_elem.find('data')
    csv_text = data_elem.text.strip()
    all_values = [int(v) for v in csv_text.replace('\n', ',').split(',') if v.strip()]
    # Build 2D array [row][col]
    grid = []
    for r in range(map_height):
        row = all_values[r * map_width : (r + 1) * map_width]
        grid.append(row)
    return grid

terrain_grid = parse_layer_csv(terrain_layer)
sprite_grid = parse_layer_csv(sprite_layer) if sprite_layer is not None else None

# === 4. Output terrain map ===
col_start, col_end = 400, 465
row_start, row_end = 20, 38  # inclusive
ground_rows_reserve = 3

print("=" * 80)
print("TERRAIN MAP: Clubstep columns 400-465, rows 20-38")
print("groundRowsToReserve = 3, so worldTileY = tmxRow - 3")
print("=" * 80)

# Print column header
header_tens = "         "
header_ones = "         "
col_numbers = "         "
for c in range(col_start, col_end + 1):
    if c % 5 == 0:
        header_tens += str(c // 100 % 10)
    else:
        header_tens += " "
    if c % 5 == 0:
        header_ones += str(c // 10 % 10)
    else:
        header_ones += " "

# Print column labels every 5 columns
print("\nColumn numbers (every 5):")
label_line = "         "
for c in range(col_start, col_end + 1):
    if c % 5 == 0:
        s = str(c)
        label_line += s
        # skip chars for the width of the number
        for _ in range(len(s) - 1):
            col_start_skip = True  # just to mark
    else:
        label_line += " "

# Better approach: just print column indices at top
print("tmxR wY  ", end="")
for c in range(col_start, col_end + 1):
    print(str(c % 10), end="")
print()
print("---- --  ", end="")
for c in range(col_start, col_end + 1):
    if c % 10 == 0:
        print("|", end="")
    elif c % 5 == 0:
        print("+", end="")
    else:
        print("-", end="")
print()

# Print column tens digit row
print("         ", end="")
for c in range(col_start, col_end + 1):
    if c % 10 == 0:
        print(str((c // 10) % 10), end="")
    else:
        print(" ", end="")
print("  (tens of col#)")

for tmx_row in range(row_start, row_end + 1):
    world_y = tmx_row - ground_rows_reserve
    print(f"{tmx_row:4d} {world_y:2d}  ", end="")
    for col in range(col_start, col_end + 1):
        gid = terrain_grid[tmx_row][col]
        if gid == 0:
            ch = '.'
        else:
            tile_idx = gid - 1
            if tile_idx < 256:
                col_type = collision_table[tile_idx]
                ch = col_to_char(col_type)
            else:
                ch = '?'
        print(ch, end="")
    print()

# === 5. Legend ===
print("\nLEGEND:")
print("  . = empty/COL_NONE")
print("  A = COL_ALL (solid)")
print("  D = death (COL_DEATH*)")
print("  T = COL_TOP (one-way top)")
print("  B = COL_BOTTOM")
print("  s = spike collision")
print("  F = COL_FLOOR_CEIL")
print("  / = slope")
print("  X = other (directional, stairs, etc.)")

# === 6. Tile index details for non-empty tiles ===
print("\n" + "=" * 80)
print("TILE INDEX DETAILS (non-empty terrain tiles in view):")
print("=" * 80)
tile_usage = {}
for tmx_row in range(row_start, row_end + 1):
    for col in range(col_start, col_end + 1):
        gid = terrain_grid[tmx_row][col]
        if gid != 0:
            tile_idx = gid - 1
            col_type = collision_table[tile_idx] if tile_idx < 256 else "UNKNOWN"
            key = (tile_idx, col_type)
            if key not in tile_usage:
                tile_usage[key] = 0
            tile_usage[key] += 1

for (tile_idx, col_type), count in sorted(tile_usage.items()):
    print(f"  Tile {tile_idx:3d} (GID {tile_idx+1:3d}): {col_type:30s} '{col_to_char(col_type)}' x{count}")

# === 7. Sprite layer analysis ===
print("\n" + "=" * 80)
print("SPRITE LAYER (layer 1): Non-zero entries in columns 400-465")
print("=" * 80)
if sprite_grid is not None:
    sprite_entries = []
    for tmx_row in range(map_height):
        for col in range(col_start, col_end + 1):
            gid = sprite_grid[tmx_row][col]
            if gid != 0:
                world_y = tmx_row - ground_rows_reserve
                sprite_idx = gid - 257 if gid >= 257 else gid - 1
                sprite_entries.append((tmx_row, world_y, col, gid, sprite_idx))
    
    if sprite_entries:
        print(f"Found {len(sprite_entries)} sprite entries:")
        print(f"  {'tmxRow':>6s} {'worldY':>6s} {'col':>5s} {'GID':>5s} {'sprIdx':>6s}")
        for tmx_row, world_y, col, gid, sprite_idx in sprite_entries:
            tileset = "sprites" if gid >= 257 else "famidash"
            local_id = gid - 257 if gid >= 257 else gid - 1
            print(f"  {tmx_row:6d} {world_y:6d} {col:5d} {gid:5d} {local_id:6d} ({tileset})")
    else:
        print("No sprite entries found in this column range.")
else:
    print("No sprite layer found in TMX.")

print("\nDone.")
