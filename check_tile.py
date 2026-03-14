import xml.etree.ElementTree as ET
import csv, io

tree = ET.parse(r"C:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_A\clubstep.tmx")
root = tree.getroot()

width = int(root.attrib['width'])
height = int(root.attrib['height'])
print(f"Map: {width}x{height}")

# Load tile layer
tile_layer = None
layers = root.findall('layer')
tile_layer = layers[0] if layers else None

data = tile_layer.find('data')
reader = csv.reader(io.StringIO(data.text.strip()))
tiles = []
for row in reader:
    for val in row:
        val = val.strip()
        if val:
            tiles.append(int(val))

print(f"Total tiles: {len(tiles)}")

# Collision table (mapped tile IDs)
# From MetatileCollision.cs: GetCollision maps byte tile ID to collision type
# Let's look at what tile is at the probe position

# The probe is at pixel (7420, 472)
# tileX = 7420 // 16 = 463
# tileY = 472 // 16 = 29
# groundRowsToReserve = 3
# tileArrayY = 29 + 3 = 32

tileX = 463
tileArrayY = 32
print(f"\nChecking tile at col={tileX}, arrayRow={tileArrayY} (worldTileY={tileArrayY-3})")

idx = tileArrayY * width + tileX
gid = tiles[idx]
print(f"TMX GID: {gid}")
if gid == 0:
    print("EMPTY (no tile)")
else:
    editor_tile = gid - 1
    print(f"Editor tile: {editor_tile} (0x{editor_tile:02X})")

# Check surrounding area too
print("\n--- Tiles around col 463, rows 28-35 ---")
for r in range(28, 36):
    row_tiles = []
    for c in range(460, 468):
        idx2 = r * width + c
        g = tiles[idx2]
        if g == 0:
            row_tiles.append(f"c{c-3}:___")
        else:
            et = g - 1
            row_tiles.append(f"c{c-3}:x{et:02X}")
    print(f"  aRow{r} wTY{r-3}: {' '.join(row_tiles)}")

# Also check the SPRITE layer
sprite_layer = layers[1] if len(layers) > 1 else None

if sprite_layer:
    sdata = sprite_layer.find('data')
    sreader = csv.reader(io.StringIO(sdata.text.strip()))
    stiles = []
    for row in sreader:
        for val in row:
            val = val.strip()
            if val:
                stiles.append(int(val))
    
    print("\n--- Sprites around col 460-467, rows 28-35 ---")
    for r in range(28, 36):
        sprites = []
        for c in range(460, 468):
            idx2 = r * width + c
            g = stiles[idx2]
            if g == 0:
                sprites.append(f"c{c-3}:___")
            else:
                sid = g - 257
                sprites.append(f"c{c-3}:s{sid:02X}")
        non_empty = [s for s in sprites if '___' not in s]
        if non_empty:
            print(f"  aRow{r} wTY{r-3}: {' '.join(sprites)}")
