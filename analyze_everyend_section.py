#!/usr/bin/env python3
"""Analyze everyend map tiles at the critical section around X=12500-12700, Y=730-870"""

import xml.etree.ElementTree as ET

TMX = r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\everyend.tmx"

tree = ET.parse(TMX)
root = tree.getroot()
mapW = int(root.get('width'))
mapH = int(root.get('height'))
print(f"Map: {mapW}x{mapH} tiles ({mapW*16}x{mapH*16} px)")

groundRowsToReserve = 3
tile_layer = None
sp_layer = None

for layer in root.findall('.//layer'):
    name = layer.get('name') or ''
    data = layer.find('data')
    if data is not None:
        text = data.text.strip()
        vals = [int(x.strip()) for x in text.split(',') if x.strip()]
        grid = []
        for r in range(mapH):
            grid.append(vals[r*mapW:(r+1)*mapW])
        if name == 'SP':
            sp_layer = grid
        else:
            tile_layer = grid

# We want to look at X=12500-12700, Y=730-870
x1, x2 = 12500, 12700
y1, y2 = 730, 880

tileX1 = x1 // 16
tileX2 = x2 // 16
tileY1 = y1 // 16  # = world Y row
tileY2 = y2 // 16

# In the map array: tileArrayY = tileY + groundRowsToReserve
# So if world tileY = 45, arrayRow = 45 + 3 = 48

print(f"\nSearching X=[{x1},{x2}]px = tile col [{tileX1},{tileX2}]")
print(f"Searching Y=[{y1},{y2}]px = tile row [{tileY1},{tileY2}]")
print(f"Array rows = [{tileY1+groundRowsToReserve},{tileY2+groundRowsToReserve}]")
print()

# Collision ID mapping
# We just print the raw tile IDs in this area
KNOWN_COL = {
    0x00: "NONE",
    0x01: "SOLID",
    0x02: "TOP",
    0x03: "BOTTOM",
    0x04: "LEFT_R",
    0x05: "RIGHT_R",
    0x06: "CEIL",
    0x07: "FLOOR_CEIL",
    0x08: "DEATH",
    0x09: "DEATH_TOP",
    0x0A: "DEATH_BOT",
    0x0B: "DEATH_LEFT",
    0x0C: "DEATH_RIGHT",
    0x18: "DEATH_BOT",  # tile 0x18 = COL_DEATH_BOTTOM per MetatileCollision
}

print("=== Tile IDs in critical area ===")
for row in range(tileY1, min(tileY2+2, mapH-groundRowsToReserve)):
    arrayRow = row + groundRowsToReserve
    if arrayRow >= mapH:
        continue
    rowData = []
    for col in range(tileX1, min(tileX2+2, mapW)):
        if arrayRow < len(tile_layer) and col < len(tile_layer[arrayRow]):
            tid = tile_layer[arrayRow][col]
            rowData.append(f"{col*16:5d}:{tid:3d}")
    worldY = row * 16
    print(f"  Y={worldY}-{worldY+15} (row {row}, arr {arrayRow}): {', '.join(rowData)}")

print()
print("=== Sprite data in area ===")
if sp_layer:
    for row in range(max(0, tileY1-2), min(tileY2+2, mapH)):
        arrayRow = row
        for col in range(tileX1, min(tileX2+2, mapW)):
            if arrayRow < len(sp_layer) and col < len(sp_layer[arrayRow]):
                sid = sp_layer[arrayRow][col]
                if sid != 0:
                    print(f"  Sprite 0x{sid:02X} at tile ({col},{row}) = pixel ({col*16},{row*16})")
