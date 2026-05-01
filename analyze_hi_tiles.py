import xml.etree.ElementTree as ET
tree = ET.parse('native-windows/famidash/LEVELS/LEVEL DATA/lvlset_C/hi.tmx')
root = tree.getroot()
layers = root.findall('layer')
layer = layers[0]  # First layer (BG/collision)
w = int(layer.get('width'))
h = int(layer.get('height'))
data = layer.find('data').text.strip()
tiles = [int(t) for t in data.split(',')]
print(f'Layer: w={w} h={h} tiles={len(tiles)}')

# GroundRowsToReserve = 3
# PF tileY = centerY_px / 16
# PF tileArrayY = tileY + 3
# TMX row index = tileArrayY
# So TMX row 25 = world tileY 22 (Y=352..367)

print("\nBG tiles at cols 219-226, tmxRows 18-36:")
for r in range(18, 37):
    row_tiles = []
    for c in range(219, 227):
        idx = r * w + c
        row_tiles.append(tiles[idx])
    worldY_start = (r - 3) * 16
    print(f"  tmxRow {r} (worldY={worldY_start}): {row_tiles}")

# Also check what's at the exact probe position
# P2 at Y=351, centerY=358, tileY=22, tileArrayY=25
# rightEdge_px = 3539+15=3554, tileX=3554//16=222 (col 222)
print(f"\nExact probe tile for P2 at Y=351:")
print(f"  tileArrayY=25, tileX=222: tile {tiles[25*w+222]}")

# Also check broader range for the corridor
print(f"\nExact probe tile for P1 at Y=480:")
print(f"  centerY=487, tileY=30, tileArrayY=33, tileX=222: tile {tiles[33*w+222]}")

# Check what's around col 221-224 in more detail
print("\nDetailed corridor at cols 220-225, tmxRows 24-35:")
for r in range(24, 36):
    worldY = (r - 3) * 16
    for c in range(220, 226):
        t = tiles[r * w + c]
        if t != 0:
            print(f"  tmxRow={r} col={c} worldY={worldY} tile={t}")

# Check sprites around X=3400-3600 for portals
sp_layer = layers[1]
sp_data = sp_layer.find('data').text.strip()
sp_tiles = [int(t) for t in sp_data.split(',')]
print("\nSprites at cols 210-230:")
for r in range(0, h):
    for c in range(210, 231):
        t = sp_tiles[r * w + c]
        if t != 0:
            worldY = (r - 3) * 16
            print(f"  tmxRow={r} col={c} worldX={c*16} worldY={worldY} sprite={t}")
