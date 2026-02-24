import xml.etree.ElementTree as ET

tree = ET.parse(r'c:\Editor Test\famidash\fan level collection\everyend.tmx')
root = tree.getroot()

# Find main layer
# List all layers and pick the first one (tile data)
all_layers = root.findall('.//layer')
print(f"Layers found: {len(all_layers)}")
for l in all_layers:
    print(f"  name={l.get('name')} width={l.get('width')} height={l.get('height')}")
layer = all_layers[0]  # First layer = main tiles
sprite_layer_idx = 1 if len(all_layers) > 1 else None

data = layer.find('data').text.strip()
rows = [r.strip() for r in data.split('\n') if r.strip()]
width = int(root.get('width'))
height = int(root.get('height'))

# Read collision table
coll_names = []
with open(r'c:\Editor Test\metatile_collision_table.txt') as f:
    for line in f:
        coll_names.append(line.strip())

# MapTileForCollision equivalents
TILE_MAP = {0xFC: 0x00, 0xDF: 0x00, 0xE3: 0x00, 0xFE: 0x00, 0xFF: 0x00, 0xFD: 0x26}

def get_coll(tid):
    mapped = TILE_MAP.get(tid, tid)
    if mapped < len(coll_names):
        return coll_names[mapped]
    return "???"

print(f"Map dimensions: {width}x{height}")
print(f"Number of CSV rows: {len(rows)}")
print()

# Death point: X=21475 Y=827
# Player hitbox: 15x15, top-left origin
# rightEdge_px = 21475 + 15 = 21490
# centerY_px = 827 + 7 = 834 (normal cube)
# tileX = 21490 / 16 = 1343.125 -> col 1343
# tileY = 834 / 16 = 52.125 -> row 52 (world)
# arrayRow = 52 + 3 = 55 (groundRowsToReserve=3)

# localX = 21490 - 1343*16 = 21490 - 21488 = 2
# localY = 834 - 52*16 = 834 - 832 = 2

print("=== DEATH ANALYSIS ===")
print(f"Player: X=21475 Y=827")
print(f"Forward probe: rightEdge=21490 centerY=834")
print(f"Probe tile: col=1343 worldRow=52 arrayRow=55")
print(f"Local coords in tile: localX=2 localY=2")

# Get the probed tile
ar = 55
cols_data = rows[ar].rstrip(',').split(',')
tid = int(cols_data[1343].strip())
coll = get_coll(tid)
print(f"Probed tile: 0x{tid:02X} collision={coll}")
print()

# Dump wider area
COL_START = 1335
COL_END = 1355
ROW_START = 48
ROW_END = 58

print(f"=== TILE IDs (cols {COL_START}-{COL_END-1}, arrayRows {ROW_START}-{ROW_END-1}) ===")
header = "aRow(wRow)  |"
for c in range(COL_START, COL_END):
    header += f" c{c:4d}"
print(header)
print("-" * len(header))

for ar in range(ROW_START, ROW_END):
    wr = ar - 3
    cols_data = rows[ar].rstrip(',').split(',')
    line = f" r{ar:2d}(w{wr:2d})   |"
    for c in range(COL_START, COL_END):
        tid = int(cols_data[c].strip()) if c < len(cols_data) else 0
        line += f"   {tid:02X}"
    print(line)

print()
print(f"=== COLLISION TYPES (cols {COL_START}-{COL_END-1}) ===")

for ar in range(ROW_START, ROW_END):
    wr = ar - 3
    cols_data = rows[ar].rstrip(',').split(',')
    line = f" r{ar:2d}(w{wr:2d})   |"
    for c in range(COL_START, COL_END):
        tid = int(cols_data[c].strip()) if c < len(cols_data) else 0
        coll = get_coll(tid)
        short = coll.replace('COL_', '')
        if short == 'NONE':
            short = '---'
        elif len(short) > 6:
            short = short[:6]
        line += f" {short:>6}"
    print(line)

print()

# Also check sprite layer for portals/orbs
if sprite_layer_idx is not None:
    sprite_layer = all_layers[sprite_layer_idx]

if sprite_layer_idx is not None:
    sdata = sprite_layer.find('data').text.strip()
    srows = [r.strip() for r in sdata.split('\n') if r.strip()]
    
    print(f"=== SPRITE LAYER (cols {COL_START}-{COL_END-1}) ===")
    any_sprites = False
    for ar in range(ROW_START, ROW_END):
        wr = ar - 3
        cols_data = srows[ar].rstrip(',').split(',')
        for c in range(COL_START, COL_END):
            sid = int(cols_data[c].strip()) if c < len(cols_data) else 0
            if sid != 0:
                print(f"  Sprite 0x{sid:02X} at col={c} arrayRow={ar} worldRow={wr}")
                any_sprites = True
    if not any_sprites:
        print("  (no sprites in this area)")
