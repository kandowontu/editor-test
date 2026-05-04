#!/usr/bin/env python3
"""Comprehensive floor and death analysis for everyend X=11000-13000"""

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

col_table = [
    "NONE", "FLOOR_CEIL", "FLOOR_CEIL", "BOTTOM", "DEATH_TOP", "FLOOR_CEIL", "FLOOR_CEIL", "NONE",
    "DEATH_BOT", "DEATH_BOT", "DEATH_TOP", "DEATH_TOP", "DEATH_BOT", "DEATH_TOP", "DEATH_LEFT", "DEATH_RIGHT",
    "ALL", "DEATH", "DEATH_BOT", "DEATH_BOT", "DEATH_TOP", "TOP_CTR_SPIKE", "ALL", "DEATH_TOP",
    "DEATH_BOT", "TOP", "DEATH", "DEATH", "DEATH", "DEATH_LEFT", "DEATH_TOP", "DEATH_RIGHT",
    "ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","NONE",
    "ALL","ALL","ALL","ALL",
]

def get_col(tile_col, tile_row):
    arrayRow = tile_row + groundRowsToReserve
    if arrayRow >= mapH or tile_col < 0 or tile_col >= mapW: return "OOB"
    tid = tile_layer[arrayRow][tile_col]
    if tid == 0: return "NONE"
    iid = tid - 1
    return col_table[iid] if 0 <= iid < len(col_table) else f"id={iid}"

# Look at the 'floor' row -- at the NES ground level (Y=848+ seems like abyss)
# At what Y is the actual ground/floor where player should stand?
# Let me map where solid floor exists near X=11000-13000

print("=== Floor tiles that could support player (Y=688-848) ===")
print("   (Only showing columns with solid floor tiles)")
for row in range(43, 54):
    worldY = row * 16
    for col_start in range(11000//16, 13000//16, 4):
        floors = []
        for dc in range(4):
            c = get_col(col_start+dc, row)
            if c in ["FLOOR_CEIL", "TOP", "BOTTOM", "ALL"]:
                floors.append((col_start+dc)*16)
        if floors:
            for f in floors:
                print(f"  Floor at pixel ({f},{worldY})")

print()
print("=== All tiles at Y=848-864 (death row), X=11000-13000 ===")
row = 53
worldY = row * 16
print(f"Row {row} (Y={worldY}-{worldY+15}):")
for col in range(11000//16, 13000//16+1):
    c = get_col(col, row)
    if c != "NONE" and c != "OOB":
        print(f"  X={col*16}: {c}")
