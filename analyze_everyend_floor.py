#!/usr/bin/env python3
"""Find where a floor/platform exists before X=12560 at the heights the BFS explores"""

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

# Collision mapping
col_table = [
    "NONE", "FLOOR_CEIL", "FLOOR_CEIL", "BOTTOM", "DEATH_TOP", "FLOOR_CEIL", "FLOOR_CEIL", "NONE",
    "DEATH_BOT", "DEATH_BOT", "DEATH_TOP", "DEATH_TOP", "DEATH_BOT", "DEATH_TOP", "DEATH_LEFT", "DEATH_RIGHT",
    "ALL", "DEATH", "DEATH_BOT", "DEATH_BOT", "DEATH_TOP", "TOP_CTR_SPIKE", "ALL", "DEATH_TOP",
    "DEATH_BOT", "TOP", "DEATH", "DEATH", "DEATH", "DEATH_LEFT", "DEATH_TOP", "DEATH_RIGHT"
]

def get_col(tile_col, tile_row):
    arrayRow = tile_row + groundRowsToReserve
    if arrayRow >= mapH or tile_col < 0 or tile_col >= mapW: return "OOB"
    tid = tile_layer[arrayRow][tile_col]
    if tid == 0: return "NONE"
    iid = tid - 1
    return col_table[iid] if 0 <= iid < len(col_table) else f"id={iid}"

# Look at the area X=11500-12600, Y=700-870
# The player falls from the top, find where there are floors
x1_px, x2_px = 11500, 12600

print("=== Searching for floors/platforms where player can land ===")
print(f"X range: {x1_px}-{x2_px}px")
print()

# Group by Y rows that have floor tiles within X range
for row in range(42, 56):  # Y=672 to Y=896
    worldY = row * 16
    floor_cols = []
    death_cols = []
    for col in range(x1_px//16, x2_px//16+1):
        c = get_col(col, row)
        if c in ["FLOOR_CEIL", "TOP", "BOTTOM", "ALL", "BOTTOM"]:
            floor_cols.append(col*16)
        elif "DEATH" in c or "SPIKE" in c:
            death_cols.append(col*16)
    
    if floor_cols or death_cols:
        print(f"Y={worldY}-{worldY+15} (row {row}):")
        if floor_cols:
            ranges = []
            start = floor_cols[0]
            prev = floor_cols[0]
            for fc in floor_cols[1:]:
                if fc != prev + 16:
                    ranges.append(f"{start}-{prev+15}")
                    start = fc
                prev = fc
            ranges.append(f"{start}-{prev+15}")
            print(f"  FLOOR: {', '.join(ranges)}")
        if death_cols:
            print(f"  DEATH: {death_cols[:10]}{'...' if len(death_cols)>10 else ''}")

print()
print("=== Sprites X=11500-12600, Y=650-900 ===")
if sp_layer:
    for row in range(40, 57):
        for col in range(x1_px//16, x2_px//16+1):
            if row < len(sp_layer) and col < len(sp_layer[row]):
                sid = sp_layer[row][col]
                if sid != 0:
                    print(f"  Sprite 0x{sid:02X} at pixel ({col*16},{row*16})")
