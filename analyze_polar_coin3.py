import xml.etree.ElementTree as ET

tree = ET.parse(r'c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\polargeist.tmx')
root = tree.getroot()

width = int(root.get('width'))   # 935
height = int(root.get('height')) # 27

layers = {}
for child in root:
    if child.tag == 'layer':
        lid = child.get('id')
        name = child.get('name', f'layer_{lid}')
        data = child.find('data')
        text = data.text.strip()
        tiles = [int(x) for x in text.split(',')]
        layers[name if name != f'layer_{lid}' else lid] = tiles
        print(f"Layer '{name}' (id={lid}): {len(tiles)} tiles ({width}x{height}={width*height})")

# Layer 1 = collision, Layer 2 = SP (sprites)
col_tiles = layers.get('1', layers.get('layer_1', None))
if col_tiles is None:
    # try by finding the first unnamed layer
    for k, v in layers.items():
        if k != 'SP':
            col_tiles = v
            break

sp_tiles = layers.get('SP')

# Collision type names
COL_NAMES = {
    0x00: '----',
    0x01: 'TOP ',
    0x02: 'SOLD',
    0x03: 'DT  ',  # death top
    0x04: 'C04 ',
    0x05: 'C05 ',
    0x06: 'C06 ',
    0x07: 'DETH',  # death all
    0x08: 'C08 ',
    0x09: 'C09 ',
    0x0A: 'C0A ',
    0x0B: 'C0B ',
    0x0C: 'C0C ',
    0x0D: 'C0D ',
    0x0E: 'C0E ',
    0x0F: 'C0F ',
}

SPRITE_NAMES = {
    0x00: '----',
    0x0B: 'YORB',  # yellow orb
    0x0C: 'PORB',  # pink orb
    0x0D: 'BPAD',  # blue pad
    0x0E: 'YPAD',  # yellow pad
    0x0F: 'S0F ',
    0x10: 'S10 ',
    0x11: 'S11 ',
    0x12: 'S12 ',
    0x13: 'S13 ',
    0x14: 'BPAD2', # blue pad alt
    0x1A: 'COIN',  # coin
    0x1B: 'COIN2', # coin alt
}

# Column range: 556-594, All rows
col_start = 550
col_end = 600
row_start = 0
row_end = 27

print("\n" + "="*120)
print("COLLISION LAYER - Columns 550-600, All Rows")
print("="*120)

# Header
hdr = "Row  Y_px | "
for c in range(col_start, col_end):
    if c % 2 == 0:
        hdr += f"{c:4d}"
    else:
        hdr += "    "
print(hdr)

hdr2 = "          | "
for c in range(col_start, col_end):
    hdr2 += f"{c*16:4d}"
# too wide, print col numbers separately
print(f"\nCol numbers: {col_start} to {col_end-1}")
print(f"X pixels:    {col_start*16} to {(col_end-1)*16}")

for r in range(row_start, row_end):
    y_px = r * 16
    line = f"R{r:02d} Y{y_px:03d} | "
    for c in range(col_start, col_end):
        idx = r * width + c
        tile = col_tiles[idx]
        # tile IDs from tileset with firstgid=1, so tile 0 = empty, tile N = tileset tile N-1
        if tile == 0:
            line += " .  "
        else:
            actual = tile - 1  # subtract firstgid
            name = COL_NAMES.get(actual, f'{actual:02X} ')
            line += f"{name}"
    print(line)

print("\n" + "="*120)
print("SPRITE LAYER - Columns 550-600, All Rows")
print("="*120)

for r in range(row_start, row_end):
    y_px = r * 16
    line = f"R{r:02d} Y{y_px:03d} | "
    has_data = False
    for c in range(col_start, col_end):
        idx = r * width + c
        tile = sp_tiles[idx]
        if tile == 0:
            line += " .  "
        else:
            has_data = True
            actual = tile - 257  # subtract firstgid for sprites tileset
            name = SPRITE_NAMES.get(actual, f'S{actual:02X}')
            line += f"{name}"
    if has_data:
        print(line)

# Now find ALL non-zero sprites in columns 550-600
print("\n" + "="*80)
print("ALL SPRITES in columns 550-600:")
print("="*80)
for r in range(row_start, row_end):
    for c in range(col_start, col_end):
        idx = r * width + c
        tile = sp_tiles[idx]
        if tile != 0:
            actual = tile - 257
            name = SPRITE_NAMES.get(actual, f'S{actual:02X}')
            print(f"  Sprite 0x{actual:02X} ({name}) at col={c}, row={r}, X={c*16}, Y={r*16}")

# Find ALL non-zero collision tiles in columns 556-594
print("\n" + "="*80)
print("ALL COLLISION TILES in columns 556-594:")
print("="*80)
for r in range(row_start, row_end):
    for c in range(556, 595):
        idx = r * width + c
        tile = col_tiles[idx]
        if tile != 0:
            actual = tile - 1
            name = COL_NAMES.get(actual, f'C{actual:02X}')
            print(f"  Col 0x{actual:02X} ({name}) at col={c}, row={r}, X={c*16}, Y={r*16}")

# Find highest standable surface per column
print("\n" + "="*80)
print("HIGHEST STANDABLE SURFACE per column (556-594):")
print("(Topmost COL_TOP=0x01 or COL_SOLID=0x02 tile)")
print("="*80)
for c in range(556, 595):
    for r in range(0, row_end):
        idx = r * width + c
        tile = col_tiles[idx]
        if tile != 0:
            actual = tile - 1
            if actual in (0x01, 0x02):
                print(f"  Col {c} (X={c*16}): first standable at row {r} (Y={r*16}), type={'TOP' if actual==1 else 'SOLID'}")
                break
    else:
        # check if there's any tile at all
        has_any = False
        for r in range(0, row_end):
            idx = r * width + c
            tile = col_tiles[idx]
            if tile != 0:
                actual = tile - 1
                has_any = True
                break
        if has_any:
            print(f"  Col {c} (X={c*16}): NO standable surface (has other tiles)")
        else:
            print(f"  Col {c} (X={c*16}): EMPTY column")
