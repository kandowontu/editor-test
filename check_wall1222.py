"""Analyze exact tile layout and collision types at column 1222 for bloodbath.tmx dual wall."""
import xml.etree.ElementTree as ET
import csv, sys

TMX = r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\bloodbath.tmx"
tree = ET.parse(TMX)
root = tree.getroot()
mapW = int(root.get('width'))
mapH = int(root.get('height'))
print(f"Map: {mapW}x{mapH}")

# Find tilesets
for ts in root.findall('tileset'):
    name = ts.get('name', ts.get('source', '?'))
    fgid = int(ts.get('firstgid'))
    print(f"Tileset '{name}' firstgid={fgid}")

terrain_firstgid = 1  # from prior analysis

# Load terrain layer
terrain_layer = None
for layer in root.findall('layer'):
    if 'terrain' in layer.get('name','').lower() or layer == root.findall('layer')[0]:
        terrain_layer = layer
        break

data = terrain_layer.find('data')
encoding = data.get('encoding', 'csv')
tiles = []
if encoding == 'csv':
    text = data.text.strip()
    for val in text.split(','):
        v = val.strip()
        if v:
            tiles.append(int(v))

print(f"Total tiles: {len(tiles)}, expected: {mapW*mapH}")

# Collision table (from MetatileCollision.cs)
COL_TABLE = {
    0: "COL_NONE",
    1: "COL_FULL", 2: "COL_FULL", 3: "COL_FULL", 4: "COL_FULL",
    36: "COL_ALL",
    111: "COL_NO_SIDE",
}

def map_tile_for_collision(tid):
    """Mirror MapTileForCollision from SharedPhysics.cs"""
    if tid in (0xFC, 0xDF, 0xE3, 0xFE, 0xFF):
        return 0x00
    if tid == 0xFD:
        return 0x26
    return tid

# Analyze column 1222 and neighbors
GR = 3  # groundRowsToReserve
for col in [1221, 1222, 1223]:
    print(f"\n=== Column {col} (X_px={col*16}) ===")
    for row in range(mapH):
        idx = row * mapW + col
        if idx >= len(tiles):
            print(f"  TMX row {row}: OUT OF BOUNDS")
            continue
        gid = tiles[idx]
        if gid == 0:
            tid = -1
            col_type = "EMPTY(gid=0)"
        else:
            tid = gid - terrain_firstgid
            mapped = map_tile_for_collision(tid)
            col_type = COL_TABLE.get(mapped, f"tile_{mapped}")
        game_tileY = row - GR
        pixel_Y = game_tileY * 16
        is_safe = 246 <= pixel_Y <= 357
        marker = " <<<SAFE" if is_safe and tid == 0 else ""
        print(f"  TMX row {row:2d} (gameTileY={game_tileY:3d}, pixY={pixel_Y:4d}): gid={gid:3d} tid={tid:3d} → {col_type}{marker}")

# Check what P2 at various Y values would probe
print("\n=== P2 Forward Collision Probe at col 1222 ===")
print("(mini cube, grav flipped: centerY = Y + 10)")
COL_WIDTH = 1222
for p2y in [3, 50, 100, 125, 200, 246, 250, 294, 300, 320, 340, 350, 357, 360, 363]:
    centerY = p2y + 10  # mini cube, grav flipped
    tileY = centerY // 16
    tileArrayY = tileY + GR
    idx = tileArrayY * mapW + COL_WIDTH
    if idx >= len(tiles) or tileArrayY >= mapH or tileArrayY < 0:
        print(f"  P2_Y={p2y:3d} centerY={centerY:3d} tileY={tileY:2d} arrY={tileArrayY:2d} → OOB (death)")
        continue
    gid = tiles[idx]
    if gid == 0:
        tid = -1
        col_type = "EMPTY"
    else:
        tid = gid - terrain_firstgid
        mapped = map_tile_for_collision(tid)
        col_type = COL_TABLE.get(mapped, f"tile_{mapped}")
    result = "SURVIVE" if (gid == 0 or tid == 0) else "DIE"
    print(f"  P2_Y={p2y:3d} centerY={centerY:3d} tileY={tileY:2d} arrY={tileArrayY:2d} gid={gid:3d} tid={tid:3d} → {col_type} → {result}")
