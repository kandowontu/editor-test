#!/usr/bin/env python3
"""Analyze everyend map tiles at X=12500-12700 wider Y range to find the floor"""

import xml.etree.ElementTree as ET

TMX = r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\everyend.tmx"

tree = ET.parse(TMX)
root = tree.getroot()
mapW = int(root.get('width'))
mapH = int(root.get('height'))

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

# Focus only on column around X=12560 from Y=550 to Y=880
target_x = 12560
target_col = target_x // 16
print(f"Column analysis at X={target_x}, tile col={target_col}")
print()

# Collision mapping (first 32 entries)
col_table = [
    "NONE", "FLOOR_CEIL", "FLOOR_CEIL", "BOTTOM", "DEATH_TOP", "FLOOR_CEIL", "FLOOR_CEIL", "NONE",
    "DEATH_BOT", "DEATH_BOT", "DEATH_TOP", "DEATH_TOP", "DEATH_BOT", "DEATH_TOP", "DEATH_LEFT", "DEATH_RIGHT",
    "ALL", "DEATH", "DEATH_BOT", "DEATH_BOT", "DEATH_TOP", "TOP_CTR_SPIKE", "ALL", "DEATH_TOP",
    "DEATH_BOT", "TOP", "DEATH", "DEATH", "DEATH", "DEATH_LEFT", "DEATH_TOP", "DEATH_RIGHT"
]

for row in range(35, 57):  # Y=560 to Y=912
    arrayRow = row + groundRowsToReserve
    if arrayRow >= mapH or target_col >= mapW:
        continue
    tid = tile_layer[arrayRow][target_col]
    internal_id = tid - 1  # TMX is 1-based
    col = col_table[internal_id] if 0 <= internal_id < len(col_table) else f"id={internal_id}"
    worldY = row * 16
    solid = "FLOOR" if col in ["FLOOR_CEIL", "TOP", "BOTTOM", "ALL"] else ("DEATH" if "DEATH" in col or "SPIKE" in col else "EMPTY")
    mark = " <-- !!!" if "DEATH" in solid else (" <-- SOLID" if "FLOOR" in solid else "")
    print(f"  Y={worldY}-{worldY+15} (row {row}): tid={tid} ({col}){mark}")

print()
print("=== Sprites at X=12500-12700, Y=550-900 ===")
if sp_layer:
    for row in range(35, 57):
        for col in range(781, 794):
            sid = sp_layer[row][col]
            if sid != 0:
                print(f"  Sprite 0x{sid:02X} at tile ({col},{row}) = pixel ({col*16},{row*16})")
