#!/usr/bin/env python3
"""Find what the NES player should be doing in this section by tracking 
what's the rightmost floor before X=12558 at the Y where player lands"""

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
    if arrayRow >= mapH or tile_col < 0 or tile_col >= mapW: return "NONE"
    tid = tile_layer[arrayRow][tile_col]
    if tid == 0: return "NONE"
    iid = tid - 1
    return col_table[iid] if 0 <= iid < len(col_table) else "NONE"

def is_solid(c):
    return c in ["FLOOR_CEIL", "TOP", "BOTTOM", "ALL"]

def is_death(c):
    return "DEATH" in c or "SPIKE" in c

# Look at row Y=704-719 (row 44) which seems to have floor at X=12528
# That's where the player should land after jumping
# Player walks on floor at Y=704-720 and falls at some point

# Let me scan what the terrain looks like column by column from X=11000 to X=13000
# at key heights
print("=== Detailed column scan X=11000-13200, checking rows 44-53 ===")
print(f"{'PxX':>6} | {'row44':>8} | {'row45':>8} | {'row46':>8} | {'row47':>8} | {'row48':>8} | {'row49':>8} | {'row50':>8} | {'row51':>8} | {'row52':>8} | {'row53':>8}")

for col in range(687, 826):
    px = col * 16
    row44 = get_col(col, 44)  # Y=704-719
    row45 = get_col(col, 45)  # Y=720-735
    row46 = get_col(col, 46)  # Y=736-751
    row47 = get_col(col, 47)  # Y=752-767
    row48 = get_col(col, 48)  # Y=768-783
    row49 = get_col(col, 49)  # Y=784-799
    row50 = get_col(col, 50)  # Y=800-815
    row51 = get_col(col, 51)  # Y=816-831
    row52 = get_col(col, 52)  # Y=832-847
    row53 = get_col(col, 53)  # Y=848-863
    
    # Only print if something interesting here
    all_cols = [row44, row45, row46, row47, row48, row49, row50, row51, row52, row53]
    if any(is_solid(c) or is_death(c) for c in all_cols):
        def fmt(c):
            if is_solid(c): return f"[FLOOR]"
            if is_death(c): return f"[DEATH]"
            return "."
        print(f"{px:6} | {fmt(row44):>8} | {fmt(row45):>8} | {fmt(row46):>8} | {fmt(row47):>8} | {fmt(row48):>8} | {fmt(row49):>8} | {fmt(row50):>8} | {fmt(row51):>8} | {fmt(row52):>8} | {fmt(row53):>8}")
