#!/usr/bin/env python3
"""Extract tile data around the coin at col~2237, row~38 in everyend.tmx"""
import xml.etree.ElementTree as ET
import sys

TMX = r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\everyend.tmx"

# Parse TMX
tree = ET.parse(TMX)
root = tree.getroot()

width = int(root.attrib['width'])   # 4573
height = int(root.attrib['height']) # 57

print(f"Map: {width}x{height} tiles, {width*16}x{height*16} pixels")

# Get main tile layer (id=1)
layer = None
for l in root.findall('.//layer'):
    if l.attrib.get('id') == '1' or l.attrib.get('name', '') != 'SP':
        layer = l
        break

data_elem = layer.find('data')
csv_text = data_elem.text.strip()

# Parse all tiles into 2D array
all_tiles = []
values = [int(v.strip()) for v in csv_text.split(',') if v.strip()]
print(f"Total tile values: {len(values)}, expected: {width*height} = {width*height}")

for row in range(height):
    start = row * width
    end = start + width
    all_tiles.append(values[start:end])

# Region of interest
COL_START = 2190
COL_END = 2250
ROW_START = 35
ROW_END = 50

print(f"\n=== Tile grid: cols {COL_START}-{COL_END}, rows {ROW_START}-{ROW_END} ===")
print(f"=== Pixel X: {COL_START*16}-{(COL_END+1)*16}, Pixel Y: {ROW_START*16}-{(ROW_END+1)*16} ===")
print(f"=== Coin target: col~2237 (X={2237*16}), row~38 (Y={38*16}) ===")
print(f"=== Player floor: row~42-43 (Y={42*16}-{43*16}) ===\n")

# Print header
print("Row\\Col", end="")
for c in range(COL_START, COL_END+1):
    print(f" {c:>4}", end="")
print()

# Print tile data
for r in range(ROW_START, ROW_END+1):
    y_px = r * 16
    label = f"R{r:02d}({y_px:>3})"
    print(f"{label:>10}", end="")
    for c in range(COL_START, COL_END+1):
        tid = all_tiles[r][c]
        if tid == 0:
            print("   .", end="")
        else:
            print(f" {tid:>3}", end="")
    print()

# Now let's analyze per-column
print("\n=== Per-column analysis (cols 2220-2250) ===")
print(f"{'Col':>4} {'X_px':>6} | Floor tiles (solid at row>=40) | Elevated (row<40) | All non-zero")
for c in range(2220, 2251):
    floor_tiles = []
    elevated_tiles = []
    all_nonzero = []
    for r in range(ROW_START, ROW_END+1):
        tid = all_tiles[r][c]
        if tid != 0:
            all_nonzero.append((r, tid))
            if r >= 40:
                floor_tiles.append((r, tid))
            else:
                elevated_tiles.append((r, tid))
    
    floor_str = ", ".join(f"r{r}={tid}" for r,tid in floor_tiles) if floor_tiles else "none"
    elev_str = ", ".join(f"r{r}={tid}" for r,tid in elevated_tiles) if elevated_tiles else "none"
    all_str = ", ".join(f"r{r}={tid}" for r,tid in all_nonzero) if all_nonzero else "empty"
    print(f"{c:>4} {c*16:>6} | {floor_str:>30} | {elev_str:>20} | {all_str}")

# Also check what tile IDs appear in this region
print("\n=== Unique tile IDs in region ===")
tile_ids = set()
for r in range(ROW_START, ROW_END+1):
    for c in range(COL_START, COL_END+1):
        tid = all_tiles[r][c]
        if tid != 0:
            tile_ids.add(tid)
print(f"Non-zero tile IDs: {sorted(tile_ids)}")

# Check the SP layer too
sp_layer = None
for l in root.findall('.//layer'):
    if l.attrib.get('name') == 'SP':
        sp_layer = l
        break

if sp_layer is not None:
    sp_data = sp_layer.find('data')
    sp_csv = sp_data.text.strip()
    sp_values = [int(v.strip()) for v in sp_csv.split(',') if v.strip()]
    sp_tiles = []
    for row in range(height):
        start = row * width
        end = start + width
        sp_tiles.append(sp_values[start:end])
    
    print("\n=== SP layer (cols 2220-2250, rows 35-50) ===")
    print("Row\\Col", end="")
    for c in range(2220, 2251):
        print(f" {c:>4}", end="")
    print()
    for r in range(ROW_START, ROW_END+1):
        y_px = r * 16
        label = f"R{r:02d}({y_px:>3})"
        print(f"{label:>10}", end="")
        for c in range(2220, 2251):
            tid = sp_tiles[r][c]
            if tid == 0:
                print("   .", end="")
            else:
                print(f" {tid:>3}", end="")
        print()

# Extended analysis: look for step patterns
print("\n=== Step/staircase analysis (cols 2210-2250) ===")
print("Looking for columns where solid tiles exist between floor and coin level:")
for c in range(2210, 2251):
    tiles_in_range = []
    for r in range(35, 50):
        tid = all_tiles[r][c]
        if tid != 0:
            tiles_in_range.append((r, tid))
    if tiles_in_range:
        highest_solid = min(r for r,t in tiles_in_range)
        lowest_solid = max(r for r,t in tiles_in_range)
        print(f"  Col {c} (X={c*16}): tiles at rows {[(r,t) for r,t in tiles_in_range]}, "
              f"highest=row{highest_solid}(Y={highest_solid*16}), lowest=row{lowest_solid}(Y={lowest_solid*16})")
