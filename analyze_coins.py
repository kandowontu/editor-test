import xml.etree.ElementTree as ET
import csv
import os
import glob

TMX_PATH = r'c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\polargeist.tmx'
TRACE_PATH = r'C:\Users\p_j_9\AppData\Local\Temp\famidash_pf_trace.csv'
DEBUG_PATTERN = r'C:\Users\p_j_9\AppData\Local\Temp\famidash_pf_debug*.txt'

# Find latest debug log
debug_files = glob.glob(DEBUG_PATTERN)
debug_files.sort(key=os.path.getmtime, reverse=True)
DEBUG_PATH = debug_files[0] if debug_files else None

tree = ET.parse(TMX_PATH)
root = tree.getroot()
map_w = int(root.get('width'))
map_h = int(root.get('height'))
tw = int(root.get('tilewidth'))
th = int(root.get('tileheight'))
print(f"Map: {map_w}x{map_h} tiles, tile={tw}x{th}px")

# Parse layers
layers = {}
for layer in root.findall('layer'):
    name = layer.get('name')
    data = layer.find('data')
    enc = data.get('encoding')
    comp = data.get('compression')
    print(f"Layer '{name}': encoding={enc}, compression={comp}")
    
    raw = data.text.strip()
    if enc == 'csv' or enc is None:
        rows = []
        all_vals = raw.split(',')
        # Reshape into 2D
        for r in range(map_h):
            row = []
            for c in range(map_w):
                row.append(int(all_vals[r * map_w + c].strip()))
            rows.append(row)
        layers[name] = rows
    else:
        print(f"  Unsupported encoding: {enc}")
        layers[name] = None

# Coin 2: hitbox=(9472,351)-(9488,367)
# Pixel coords: X=9472 => col = 9472/16 = 592, Y=351 => row = 351/16 = 21.9 => row 21-22
# Coin 3: idx=13809 in SP layer
# idx = row * width + col => row = 13809 // 935 = 14, col = 13809 % 935 = 619
# Coin 3 pixel: X = 619*16 = 9904, Y = 14*16 = 224

print("\n" + "="*80)
print("COIN 2 ANALYSIS")
print("="*80)
print(f"Coin 2 hitbox: (9472,351)-(9488,367)")
coin2_col = 9472 // tw  # 592
coin2_row_top = 351 // th  # 21
print(f"Coin 2 tile position: col={coin2_col}, rows={coin2_row_top}-{coin2_row_top+1}")

print(f"\n--- Tile Layer around coin 2 (cols 555-620, rows 14-26) ---")
if layers.get('layer') is not None:
    tile = layers['layer']
    # Print header
    hdr = "     "
    for c in range(555, 621):
        hdr += f"{c:4d}"
    print(hdr)
    for r in range(14, min(27, map_h)):
        line = f"R{r:2d}: "
        for c in range(555, 621):
            v = tile[r][c] if c < map_w else 0
            line += f"{v:4d}"
        print(line)

print(f"\n--- SP Layer around coin 2 (cols 555-620, rows 14-26) ---")
if layers.get('SP') is not None:
    sp = layers['SP']
    hdr = "     "
    for c in range(555, 621):
        hdr += f"{c:4d}"
    print(hdr)
    for r in range(14, min(27, map_h)):
        line = f"R{r:2d}: "
        for c in range(555, 621):
            v = sp[r][c] if c < map_w else 0
            line += f"{v:4d}"
        print(line)

print("\n" + "="*80)
print("COIN 3 ANALYSIS")
print("="*80)
# Find coin 3 in SP layer
if layers.get('SP') is not None:
    sp = layers['SP']
    # Coin 3 sid=0x1B = 27
    # Search for sid 27 in SP layer
    coin3_locs = []
    for r in range(map_h):
        for c in range(map_w):
            v = sp[r][c]
            if v == 27 or v == 0x1B:
                coin3_locs.append((r, c, v))
    print(f"SP tiles with value 27 (0x1B): {len(coin3_locs)}")
    for r, c, v in coin3_locs:
        print(f"  row={r}, col={c}, pixel=({c*tw},{r*th}), idx={r*map_w+c}")
    
    # Also check idx 13809
    r13809 = 13809 // map_w
    c13809 = 13809 % map_w
    print(f"\nSP layer at idx 13809: row={r13809}, col={c13809}, val={sp[r13809][c13809]}")
    print(f"  pixel=({c13809*tw},{r13809*th})")

    # Show area around coin 3
    if coin3_locs:
        cr, cc = coin3_locs[0][0], coin3_locs[0][1]
    else:
        cr, cc = r13809, c13809
    
    c_start = max(0, cc - 30)
    c_end = min(map_w, cc + 31)
    r_start = max(0, cr - 6)
    r_end = min(map_h, cr + 8)
    
    print(f"\n--- Tile Layer around coin 3 (cols {c_start}-{c_end}, rows {r_start}-{r_end}) ---")
    tile = layers['layer']
    hdr = "     "
    for c in range(c_start, c_end):
        hdr += f"{c:4d}"
    print(hdr)
    for r in range(r_start, r_end):
        line = f"R{r:2d}: "
        for c in range(c_start, c_end):
            line += f"{tile[r][c]:4d}"
        print(line)
    
    print(f"\n--- SP Layer around coin 3 (cols {c_start}-{c_end}, rows {r_start}-{r_end}) ---")
    hdr = "     "
    for c in range(c_start, c_end):
        hdr += f"{c:4d}"
    print(hdr)
    for r in range(r_start, r_end):
        line = f"R{r:2d}: "
        for c in range(c_start, c_end):
            line += f"{sp[r][c]:4d}"
        print(line)

print("\n" + "="*80)
print("SPRITES NEAR COIN 2 (cols 555-620)")
print("="*80)
# Find all interesting sprites in SP layer near coin 2
# Yellow pad = 0x03 = 3
# Blue pad = 0x04 = 4
# Pink pad = 0x02 = 2
# Yellow orb = 0x08 = 8
# Blue orb = ?
# Coin sprites: 0x07, 0x1A, 0x1B
SPRITE_NAMES = {
    0x02: "PINK_PAD",
    0x03: "YELLOW_PAD", 
    0x04: "BLUE_PAD",
    0x05: "GRAVITY_PAD",
    0x06: "PORTAL_SHIP",
    0x07: "COIN",
    0x08: "YELLOW_ORB",
    0x09: "BLUE_ORB",
    0x0A: "PORTAL_CUBE",
    0x0B: "GRAVITY_DOWN",
    0x0C: "GRAVITY_UP",
    0x0D: "PORTAL_BALL",
    0x0E: "PORTAL_UFO",
    0x0F: "MIRROR",
    0x10: "PINK_ORB",
    0x11: "GREEN_ORB",
    0x12: "GREEN_PAD",
    0x1A: "COIN2",
    0x1B: "COIN3",
}

if layers.get('SP') is not None:
    sp = layers['SP']
    sprites_near_coin2 = []
    for r in range(map_h):
        for c in range(555, min(621, map_w)):
            v = sp[r][c]
            if v != 0:
                name = SPRITE_NAMES.get(v, f"UNK_{v:#04x}")
                sprites_near_coin2.append((r, c, v, name))
    
    print(f"Found {len(sprites_near_coin2)} sprites in cols 555-620:")
    for r, c, v, name in sorted(sprites_near_coin2, key=lambda x: x[1]):
        px = c * tw
        py = r * th
        print(f"  col={c} row={r} pixel=({px},{py}) sid={v:#04x} {name}")

# Also search wider area cols 540-620
print(f"\n--- All sprites cols 540-620 ---")
if layers.get('SP') is not None:
    sp = layers['SP']
    for r in range(map_h):
        for c in range(540, min(621, map_w)):
            v = sp[r][c]
            if v != 0:
                name = SPRITE_NAMES.get(v, f"UNK_{v:#04x}")
                px = c * tw
                py = r * th
                print(f"  col={c} row={r} pixel=({px},{py}) sid={v:#04x} {name}")

print("\n" + "="*80)
print("DEATH TILES NEAR COIN 2 (cols 555-600)")  
print("="*80)
# Death tiles in the tile layer - which tile IDs are spikes/death?
# Let's find what tile IDs appear and check for known spike patterns
if layers.get('layer') is not None:
    tile = layers['layer']
    tile_set = set()
    for r in range(map_h):
        for c in range(555, min(601, map_w)):
            v = tile[r][c]
            if v != 0:
                tile_set.add(v)
    print(f"Unique tile IDs in cols 555-600: {sorted(tile_set)}")
