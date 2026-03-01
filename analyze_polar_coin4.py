import xml.etree.ElementTree as ET

# Read metatile collision table
with open(r'c:\Editor Test\metatile_collision_table.txt', 'r') as f:
    col_table = [line.strip() for line in f.readlines()]

print(f"Collision table has {len(col_table)} entries")
print()

# Map the tile IDs found in our area to their collision types
found_tiles = [0x0C, 0x10, 0x11, 0x18, 0x19, 0x22, 0x24, 0x25, 0x26, 0x2D, 0x2F, 0x30, 0x34, 0x35]
print("=== Tile ID to Collision Type Mapping ===")
for t in found_tiles:
    if t < len(col_table):
        print(f"  Tile 0x{t:02X} ({t:3d}) = {col_table[t]}")
    else:
        print(f"  Tile 0x{t:02X} ({t:3d}) = OUT OF RANGE")

# Now re-do the full analysis with proper names
tree = ET.parse(r'c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\polargeist.tmx')
root = tree.getroot()
width = int(root.get('width'))
height = int(root.get('height'))

layers_data = {}
for child in root:
    if child.tag == 'layer':
        name = child.get('name', f"layer_{child.get('id')}")
        data = child.find('data')
        tiles = [int(x) for x in data.text.strip().split(',')]
        layers_data[name] = tiles

col_tiles = layers_data.get('layer_1')
sp_tiles = layers_data.get('SP')

# Define short collision names for grid display
def get_col_name(tile_id):
    """Get short collision name for a tile_id (0-based after firstgid subtraction)"""
    if tile_id < len(col_table):
        full = col_table[tile_id]
        mapping = {
            'COL_NONE': '____',
            'COL_FLOOR_CEIL': 'FC  ',
            'COL_BOTTOM': 'BOT ',
            'COL_DEATH_TOP': 'D_T ',
            'COL_DEATH_BOTTOM': 'D_B ',
            'COL_DEATH': 'DETH',
            'COL_DEATH_LEFT': 'D_L ',
            'COL_DEATH_RIGHT': 'D_R ',
            'COL_ALL': 'ALL ',
            'COL_TOP': 'TOP ',
            'COL_TOP_SPIKES': 'T_SP',
        }
        return mapping.get(full, full[:4])
    return '??? '

# Print grid for rows 15-26, columns 556-600 with actual collision types
print("\n" + "="*120)
print("COLLISION GRID (with actual collision types) - Rows 15-26, Every column 556-600")
print("Format: TileHex:CollisionType")
print("="*120)

# Print column headers
hdr = "          "
for c in range(556, 600, 2):
    hdr += f" {c:3d} "
print("Cols:   " + hdr)
hdr2 = "          "
for c in range(556, 600, 2):
    hdr2 += f"{c*16:5d}"
print("X_px:   " + hdr2)

for r in range(15, 27):
    y_px = r * 16
    line = f"R{r:02d} Y{y_px:03d} | "
    for c in range(556, 600, 2):
        idx = r * width + c
        raw_tile = col_tiles[idx]
        if raw_tile == 0:
            line += " ..  "
        else:
            actual = raw_tile - 1
            cn = get_col_name(actual)
            line += f"{cn} "
    print(line)

# Now also show every single column for rows 22-26 (near the coin)
print("\n" + "="*120)
print("DETAILED COLLISION GRID - Rows 22-26, Every column 582-598")
print("="*120)
hdr = "          "
for c in range(582, 599):
    hdr += f"{c:4d} "
print("Cols:   " + hdr)
hdr2 = "          "
for c in range(582, 599):
    hdr2 += f"{c*16:4d} "
print("X_px:   " + hdr2)

for r in range(22, 27):
    y_px = r * 16
    line = f"R{r:02d} Y{y_px:03d} | "
    for c in range(582, 599):
        idx = r * width + c
        raw_tile = col_tiles[idx]
        if raw_tile == 0:
            line += " ..  "
        else:
            actual = raw_tile - 1
            cn = get_col_name(actual)
            line += f"{cn} "
    print(line)

# And show the wide staircase area with every column
print("\n" + "="*120)
print("STAIRCASE COLLISION GRID - Rows 18-26, Every column 560-598")
print("="*120)
for r in range(18, 27):
    y_px = r * 16
    line = f"R{r:02d} Y{y_px:03d} | "
    for c in range(560, 598):
        idx = r * width + c
        raw_tile = col_tiles[idx]
        if raw_tile == 0:
            line += ". "
        else:
            actual = raw_tile - 1
            cn = get_col_name(actual)
            # Use 2-char abbreviation for dense view
            abbrev = {
                'COL_NONE': '_ ',
                'COL_FLOOR_CEIL': 'FC',
                'COL_BOTTOM': 'Bo',
                'COL_DEATH_TOP': 'Dt',
                'COL_DEATH_BOTTOM': 'Db',
                'COL_DEATH': 'DD',
                'COL_DEATH_LEFT': 'Dl',
                'COL_DEATH_RIGHT': 'Dr',
                'COL_ALL': '##',
                'COL_TOP': 'TT',
                'COL_TOP_SPIKES': 'TS',
            }
            full_name = col_table[actual] if actual < len(col_table) else '??'
            a = abbrev.get(full_name, full_name[:2])
            line += a + ""
    print(line)

# Column legend
print("\nLegend: . = empty, ## = COL_ALL (solid), TT = COL_TOP (platform),")
print("DD = COL_DEATH (death all), Dt = COL_DEATH_TOP, Db = COL_DEATH_BOTTOM,")
print("FC = COL_FLOOR_CEIL, _ = COL_NONE")

# Sprite analysis
print("\n" + "="*80)
print("ALL SPRITES in columns 556-600 with positions:")
print("="*80)
sprite_names = {
    0x0A: 'PINK_PAD',  # guess
    0x0B: 'YELLOW_ORB',
    0x0C: 'PINK_ORB',
    0x0D: 'BLUE_PAD',
    0x0E: 'YELLOW_PAD',
    0x14: 'BLUE_PAD2',
    0x1A: 'COIN',
    0x1B: 'COIN2',
    0x2B: 'GRAVITY_PORTAL?',
    0x2E: 'UNKNOWN_2E',
    0x30: 'UNKNOWN_30',
    0x3D: 'UNKNOWN_3D',
}
for r in range(0, height):
    for c in range(556, 600):
        idx = r * width + c
        tile = sp_tiles[idx]
        if tile != 0:
            actual = tile - 257
            name = sprite_names.get(actual, f'SPRITE_0x{actual:02X}')
            print(f"  0x{actual:02X} ({name:20s}) at col={c}, row={r}, X_px={c*16}, Y_px={r*16}")

# Also look at the wider context - columns 530-556 to see where player comes from
print("\n" + "="*80)
print("APPROACH AREA - Collision tiles 530-560, rows 22-26:")
print("="*80)
for r in range(22, 27):
    y_px = r * 16
    line = f"R{r:02d} Y{y_px:03d} | "
    for c in range(530, 560):
        idx = r * width + c
        raw_tile = col_tiles[idx]
        if raw_tile == 0:
            line += ". "
        else:
            actual = raw_tile - 1
            full_name = col_table[actual] if actual < len(col_table) else '??'
            abbrev = {
                'COL_NONE': '_ ',
                'COL_FLOOR_CEIL': 'FC',
                'COL_BOTTOM': 'Bo',
                'COL_DEATH_TOP': 'Dt',
                'COL_DEATH_BOTTOM': 'Db',
                'COL_DEATH': 'DD',
                'COL_DEATH_LEFT': 'Dl',
                'COL_DEATH_RIGHT': 'Dr',
                'COL_ALL': '##',
                'COL_TOP': 'TT',
                'COL_TOP_SPIKES': 'TS',
            }
            a = abbrev.get(full_name, full_name[:2])
            line += a + ""
    print(line)

# Sprites in approach area
print("\nSprites in approach area (530-556):")
for r in range(0, height):
    for c in range(530, 556):
        idx = r * width + c
        tile = sp_tiles[idx]
        if tile != 0:
            actual = tile - 257
            name = sprite_names.get(actual, f'SPRITE_0x{actual:02X}')
            print(f"  0x{actual:02X} ({name:20s}) at col={c}, row={r}, X_px={c*16}, Y_px={r*16}")

# Also look further right after the coin - cols 592-620
print("\n" + "="*80)
print("AFTER-COIN AREA - Collision tiles 594-620, rows 20-26:")
print("="*80)
for r in range(20, 27):
    y_px = r * 16
    line = f"R{r:02d} Y{y_px:03d} | "
    for c in range(594, 620):
        idx = r * width + c
        raw_tile = col_tiles[idx]
        if raw_tile == 0:
            line += ". "
        else:
            actual = raw_tile - 1
            full_name = col_table[actual] if actual < len(col_table) else '??'
            abbrev = {
                'COL_NONE': '_ ',
                'COL_FLOOR_CEIL': 'FC',
                'COL_BOTTOM': 'Bo',
                'COL_DEATH_TOP': 'Dt',
                'COL_DEATH_BOTTOM': 'Db',
                'COL_DEATH': 'DD',
                'COL_DEATH_LEFT': 'Dl',
                'COL_DEATH_RIGHT': 'Dr',
                'COL_ALL': '##',
                'COL_TOP': 'TT',
                'COL_TOP_SPIKES': 'TS',
            }
            a = abbrev.get(full_name, full_name[:2])
            line += a + ""
    print(line)

print("\nSprites after coin (594-620):")
for r in range(0, height):
    for c in range(594, 620):
        idx = r * width + c
        tile = sp_tiles[idx]
        if tile != 0:
            actual = tile - 257
            name = sprite_names.get(actual, f'SPRITE_0x{actual:02X}')
            print(f"  0x{actual:02X} ({name:20s}) at col={c}, row={r}, X_px={c*16}, Y_px={r*16}")
