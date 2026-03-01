#!/usr/bin/env python3
"""Analyze the tile layout around coin 2 area in polargeist.tmx"""
import xml.etree.ElementTree as ET
import sys

TMX_FILE = r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\polargeist.tmx"
COL_TABLE_FILE = r"c:\Editor Test\metatile_collision_table.txt"

# Load collision table (0-indexed: line 0 = tile 0)
with open(COL_TABLE_FILE, 'r') as f:
    col_table = [line.strip() for line in f.readlines()]

# Parse TMX
tree = ET.parse(TMX_FILE)
root = tree.getroot()

map_width = int(root.attrib['width'])
map_height = int(root.attrib['height'])
tile_w = int(root.attrib['tilewidth'])
tile_h = int(root.attrib['tileheight'])

print(f"Map: {map_width}x{map_height} tiles, {tile_w}x{tile_h}px each")
print(f"Total pixel size: {map_width*tile_w}x{map_height*tile_h}")
print()

# Parse tilesets
tilesets = []
for ts in root.findall('tileset'):
    tilesets.append({
        'firstgid': int(ts.attrib['firstgid']),
        'name': ts.attrib['name'],
        'tilecount': int(ts.attrib.get('tilecount', 256))
    })
    print(f"Tileset: {ts.attrib['name']}, firstgid={ts.attrib['firstgid']}")

print()

# Parse layers
layers = {}
for layer in root.findall('layer'):
    layer_name = layer.attrib.get('name', f"id{layer.attrib['id']}")
    layer_id = layer.attrib['id']
    data_elem = layer.find('data')
    csv_text = data_elem.text.strip()
    tiles = [int(x) for x in csv_text.replace('\n', ',').split(',') if x.strip()]
    
    # Reshape into rows
    rows = []
    for r in range(map_height):
        row = tiles[r * map_width : (r + 1) * map_width]
        rows.append(row)
    
    layers[layer_name if layer_name != f"id{layer_id}" else f"layer{layer_id}"] = rows
    print(f"Layer '{layer_name}' (id={layer_id}): {len(tiles)} tiles, {len(rows)} rows x {len(rows[0])} cols")

print()

# Define analysis region
COL_START = 570
COL_END = 600
ROW_START = 0
ROW_END = 27  # all rows

# Sprite tileset firstgid
sprite_firstgid = 257  # "sprites" tileset

# Sprite IDs of interest (0-indexed sprite IDs)
SPRITE_NAMES = {
    7: "COIN",
    10: "YELLOW_PAD(0x0A)",
    12: "YELLOW_PAD(0x0C)",
    26: "COIN_ALT_0x1A",
    27: "COIN_ALT_0x1B",
    37: "PAD_0x25",
    38: "PAD_0x26",
}

print("=" * 120)
print(f"METATILE LAYER (Layer 1) - Columns {COL_START}-{COL_END}")
print("=" * 120)

# Get the metatile layer (first layer, or layer1)
meta_layer = None
sp_layer = None
for name, rows in layers.items():
    if name in ('layer1', 'id1') or (name != 'SP' and meta_layer is None):
        meta_layer = rows
    if name == 'SP':
        sp_layer = rows

if meta_layer is None:
    meta_layer = list(layers.values())[0]
if sp_layer is None and len(layers) > 1:
    sp_layer = list(layers.values())[1]

# Print header
print(f"{'Row':>3} {'Y-range':>12} | ", end="")
for c in range(COL_START, COL_END + 1):
    print(f"{c:>6}", end="")
print()

print(f"{'':>3} {'':>12} | ", end="")
for c in range(COL_START, COL_END + 1):
    x = c * 16
    print(f"{x:>6}", end="")
print("  (X pixel)")
print("-" * 120)

for r in range(ROW_START, ROW_END):
    y_start = r * 16
    y_end = y_start + 15
    row_data = meta_layer[r]
    
    # Check if any non-zero tiles in range
    has_data = any(row_data[c] != 0 for c in range(COL_START, min(COL_END + 1, len(row_data))))
    
    if not has_data:
        continue
    
    print(f"{r:>3} {y_start:>3}-{y_end:>3}   | ", end="")
    for c in range(COL_START, min(COL_END + 1, len(row_data))):
        tid = row_data[c]
        if tid == 0:
            print(f"{'·':>6}", end="")
        else:
            # Determine if metatile or sprite
            if tid >= sprite_firstgid:
                sprite_id = tid - sprite_firstgid
                print(f"{'S'+str(sprite_id):>6}", end="")
            else:
                metatile_id = tid - 1  # firstgid=1, so metatile 0 = TMX tid 1
                print(f"{metatile_id:>6}", end="")
    print()

print()
print("=" * 120)
print("DETAILED NON-ZERO TILES IN METATILE LAYER")
print("=" * 120)
print(f"{'Col':>5} {'Row':>5} {'X':>7} {'Y-range':>12} {'RawTID':>8} {'MetaID':>8} {'Collision':>25}")
print("-" * 80)

for r in range(ROW_START, ROW_END):
    for c in range(COL_START, min(COL_END + 1, len(meta_layer[r]))):
        tid = meta_layer[r][c]
        if tid == 0:
            continue
        
        x = c * 16
        y_start = r * 16
        y_end = y_start + 15
        
        if tid >= sprite_firstgid:
            sprite_id = tid - sprite_firstgid
            print(f"{c:>5} {r:>5} {x:>7} {y_start:>5}-{y_end:<5} {tid:>8} {'SPR'+str(sprite_id):>8} {'(sprite in meta layer)':>25}")
        else:
            metatile_id = tid - 1
            col_type = col_table[metatile_id] if metatile_id < len(col_table) else "UNKNOWN"
            print(f"{c:>5} {r:>5} {x:>7} {y_start:>5}-{y_end:<5} {tid:>8} {metatile_id:>8} {col_type:>25}")

# SP Layer
if sp_layer:
    print()
    print("=" * 120)
    print(f"SPRITE LAYER (SP) - Columns {COL_START}-{COL_END}")
    print("=" * 120)
    
    # Print header
    print(f"{'Row':>3} {'Y-range':>12} | ", end="")
    for c in range(COL_START, COL_END + 1):
        print(f"{c:>6}", end="")
    print()
    print("-" * 120)
    
    for r in range(ROW_START, ROW_END):
        y_start = r * 16
        y_end = y_start + 15
        row_data = sp_layer[r]
        has_data = any(row_data[c] != 0 for c in range(COL_START, min(COL_END + 1, len(row_data))))
        if not has_data:
            continue
        
        print(f"{r:>3} {y_start:>3}-{y_end:>3}   | ", end="")
        for c in range(COL_START, min(COL_END + 1, len(row_data))):
            tid = row_data[c]
            if tid == 0:
                print(f"{'·':>6}", end="")
            else:
                if tid >= sprite_firstgid:
                    sprite_id = tid - sprite_firstgid
                    name = SPRITE_NAMES.get(sprite_id, f"s{sprite_id}")
                    print(f"{name[:6]:>6}", end="")
                else:
                    print(f"{'m'+str(tid-1):>6}", end="")
        print()
    
    print()
    print("DETAILED SPRITES IN SP LAYER:")
    print(f"{'Col':>5} {'Row':>5} {'X':>7} {'Y-range':>12} {'RawTID':>8} {'SpriteID':>10} {'Name':>20}")
    print("-" * 80)
    
    for r in range(ROW_START, ROW_END):
        for c in range(COL_START, min(COL_END + 1, len(sp_layer[r]))):
            tid = sp_layer[r][c]
            if tid == 0:
                continue
            x = c * 16
            y_start = r * 16
            y_end = y_start + 15
            
            if tid >= sprite_firstgid:
                sprite_id = tid - sprite_firstgid
                name = SPRITE_NAMES.get(sprite_id, f"sprite_{sprite_id}")
            else:
                sprite_id = tid - 1
                name = f"metatile_{sprite_id}"
            
            print(f"{c:>5} {r:>5} {x:>7} {y_start:>5}-{y_end:<5} {tid:>8} {sprite_id:>10} {name:>20}")

# Also check the wider context for the SP layer to find coin 2
if sp_layer:
    print()
    print("=" * 120)
    print("SEARCHING FOR COINS AND PADS IN SP LAYER (wider area cols 550-620)")
    print("=" * 120)
    
    coin_ids = {7, 26, 27}
    pad_ids = {10, 12, 37, 38}
    
    for r in range(ROW_START, ROW_END):
        for c in range(550, min(620, len(sp_layer[r]))):
            tid = sp_layer[r][c]
            if tid == 0:
                continue
            
            if tid >= sprite_firstgid:
                sprite_id = tid - sprite_firstgid
            else:
                sprite_id = tid - 1
            
            if sprite_id in coin_ids or sprite_id in pad_ids:
                x = c * 16
                y_start = r * 16
                y_end = y_start + 15
                name = SPRITE_NAMES.get(sprite_id, f"sprite_{sprite_id}")
                idx = r * map_width + c
                print(f"Col={c} Row={r} X={x} Y={y_start}-{y_end} SpriteID={sprite_id} Name={name} LinearIndex={idx}")

# Print an ASCII visualization
print()
print("=" * 120)
print("ASCII MAP (Metatile layer + SP layer combined)")
print(f"Columns {COL_START}-{COL_END}, All rows with data")
print("Legend: # = COL_ALL, ^ = DEATH_TOP, v = DEATH_BOTTOM, X = DEATH, S = spike variants")
print("        . = COL_NONE, F = FLOOR_CEIL, T = TOP, B = BOTTOM, / \\ = slopes")
print("        C = COIN(SP), P = PAD(SP), ? = other sprite")
print("=" * 120)

def tile_char(metatile_id):
    if metatile_id < 0 or metatile_id >= len(col_table):
        return '?'
    ct = col_table[metatile_id]
    if ct == 'COL_ALL': return '#'
    if ct == 'COL_NONE': return '.'
    if 'DEATH_TOP' in ct: return '^'
    if 'DEATH_BOTTOM' in ct: return 'v'
    if 'DEATH_LEFT' in ct: return '<'
    if 'DEATH_RIGHT' in ct: return '>'
    if ct == 'COL_DEATH': return 'X'
    if 'SPIKE' in ct: return 'S'
    if ct == 'COL_FLOOR_CEIL': return 'F'
    if ct == 'COL_TOP': return 'T'
    if ct == 'COL_BOTTOM': return 'B'
    if 'SLOPE' in ct: return '/'
    if ct == 'COL_LEFT': return '['
    if ct == 'COL_RIGHT': return ']'
    if ct == 'COL_NO_SIDE': return '-'
    return '~'

# Find which rows have data
active_rows = []
for r in range(ROW_START, ROW_END):
    has_meta = any(meta_layer[r][c] != 0 for c in range(COL_START, min(COL_END+1, len(meta_layer[r]))))
    has_sp = sp_layer and any(sp_layer[r][c] != 0 for c in range(COL_START, min(COL_END+1, len(sp_layer[r]))))
    if has_meta or has_sp:
        active_rows.append(r)

# Print col headers
print(f"     Y     |", end="")
for c in range(COL_START, COL_END + 1):
    d = c % 10
    print(d, end="")
print(f"  (cols {COL_START}-{COL_END})")

print(f"     X→    |", end="")
for c in range(COL_START, COL_END + 1):
    x = c * 16
    print(str((x // 100) % 10), end="")
print(f"  (X hundreds digit)")

for r in range(min(active_rows) if active_rows else 0, max(active_rows)+1 if active_rows else 0):
    y = r * 16
    print(f"R{r:>2} Y{y:>3}   |", end="")
    for c in range(COL_START, COL_END + 1):
        # Check SP layer first
        sp_char = None
        if sp_layer and c < len(sp_layer[r]):
            sp_tid = sp_layer[r][c]
            if sp_tid != 0:
                if sp_tid >= sprite_firstgid:
                    sid = sp_tid - sprite_firstgid
                else:
                    sid = sp_tid - 1
                if sid in {7, 26, 27}: sp_char = 'C'
                elif sid in {10, 12, 37, 38}: sp_char = 'P'
                else: sp_char = '?'
        
        # Check metatile layer
        meta_char = ' '
        if c < len(meta_layer[r]):
            mt = meta_layer[r][c]
            if mt != 0:
                if mt >= sprite_firstgid:
                    meta_char = '?'
                else:
                    meta_char = tile_char(mt - 1)
        
        # Combine: prefer SP overlay if both exist
        if sp_char:
            print(sp_char, end="")
        else:
            print(meta_char, end="")
    
    print(f"  R{r} Y={y}-{y+15}")

print()
print("Coin 2 expected at X=9472 → col 592, Y=351-367 → rows 21-22")
print(f"Col 592 X = {592*16}")
