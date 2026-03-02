#!/usr/bin/env python3
"""Deeper analysis - look for air and understand tile meanings"""
import xml.etree.ElementTree as ET

TMX = r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\everyend.tmx"
tree = ET.parse(TMX)
root = tree.getroot()
width = int(root.attrib['width'])
height = int(root.attrib['height'])

layer = None
for l in root.findall('.//layer'):
    if l.attrib.get('id') == '1' or l.attrib.get('name', '') != 'SP':
        layer = l
        break

data_elem = layer.find('data')
csv_text = data_elem.text.strip()
values = [int(v.strip()) for v in csv_text.split(',') if v.strip()]
all_tiles = []
for row in range(height):
    start = row * width
    end = start + width
    all_tiles.append(values[start:end])

# Check ALL rows for col 2237 to understand the vertical structure
print("=== Full vertical profile at col 2237 (coin column) ===")
for r in range(height):
    tid = all_tiles[r][2237]
    if tid != 48 or r < 5 or r > 50:
        marker = " <-- AIR/SPECIAL" if tid != 48 else ""
        print(f"  Row {r:2d} (Y={r*16:4d}): tile {tid:>3}{marker}")
    else:
        print(f"  Row {r:2d} (Y={r*16:4d}): tile  48")

# Check the approach from the left - what's at row 42 (player floor level)?
print("\n=== Horizontal profile at row 42 (player floor), cols 2170-2260 ===")
for c in range(2170, 2261):
    tid = all_tiles[42][c]
    if tid != 48:
        print(f"  Col {c} (X={c*16}): tile {tid}")

# Also check row 38 (coin level)
print("\n=== Horizontal profile at row 38 (coin level), cols 2170-2260 ===")
for c in range(2170, 2261):
    tid = all_tiles[38][c]
    if tid != 48:
        print(f"  Col {c} (X={c*16}): tile {tid}")

# Let me look at ALL rows for a wider range to find air/gaps
print("\n=== Looking for non-48 tiles (possible air/surfaces) in cols 2220-2250, rows 0-56 ===")
for r in range(height):
    non48 = []
    for c in range(2220, 2251):
        tid = all_tiles[r][c]
        if tid != 48:
            non48.append((c, tid))
    if non48:
        print(f"  Row {r:2d} (Y={r*16:4d}): {non48}")

# Check what tile 1 means - look at beginning of map for context
print("\n=== Row 0, first 20 cols ===")
print([all_tiles[0][c] for c in range(20)])
print(f"\n=== Row 0, cols 2230-2240 ===")
print([all_tiles[0][c] for c in range(2230, 2241)])

# Key question: is there ANY tile 0 or tile 1 in this region?
print("\n=== Searching for tile 0 (true air) in cols 2190-2260, all rows ===")
found_zero = False
for r in range(height):
    for c in range(2190, 2261):
        if all_tiles[r][c] == 0:
            print(f"  Tile 0 at row {r}, col {c}")
            found_zero = True
if not found_zero:
    print("  No tile 0 found in this region!")

print("\n=== Searching for tile 1 in cols 2190-2260, all rows ===")
found_one = False
for r in range(height):
    for c in range(2190, 2261):
        if all_tiles[r][c] == 1:
            print(f"  Tile 1 at row {r}, col {c}")
            found_one = True
if not found_one:
    print("  No tile 1 found in this region!")

# Let's check what tile 219 is (appeared in the region)
# And check the SP layer for coins
sp_layer = None
for l in root.findall('.//layer'):
    if l.attrib.get('name') == 'SP':
        sp_layer = l
        break

if sp_layer:
    sp_data = sp_layer.find('data')
    sp_csv = sp_data.text.strip()
    sp_values = [int(v.strip()) for v in sp_csv.split(',') if v.strip()]
    sp_tiles = []
    for row in range(height):
        start = row * width
        end = start + width
        sp_tiles.append(sp_values[start:end])
    
    print("\n=== SP layer non-zero tiles in cols 2190-2260, all rows ===")
    for r in range(height):
        for c in range(2190, 2261):
            tid = sp_tiles[r][c]
            if tid != 0:
                print(f"  SP tile {tid} at row {r} (Y={r*16}), col {c} (X={c*16})")

# Check the actual structure more broadly - look for where the enclosed area boundary is
# Find the leftmost column where the solid block starts (all rows 35-50 become 48)
print("\n=== Looking for where solid block starts/ends around col 2237 ===")
print("Scanning cols 2150-2270 for row 30 (should be above the floor):")
for c in range(2150, 2271):
    tid = all_tiles[30][c]
    if tid != 48:
        print(f"  Col {c} (X={c*16}): row30 tile={tid}")

# Identify the "room" - look at the border tiles (37, 35, 34, 38, 39, etc.)
print("\n=== Border/edge tile analysis in cols 2190-2260 ===")
border_tiles = {34: 'flat_top', 37: 'left_edge', 35: 'right_edge', 
                38: 'corner_BL', 39: 'corner_BR', 42: 'corner_TR', 
                43: 'corner_TL', 27: 'special27', 29: 'special29',
                18: 'spike/deco18', 28: 'spike/deco28', 49: 'spike/deco49',
                50: 'special50', 52: 'special52', 219: 'COIN/special219',
                44: 'slope44', 45: 'slope45', 40: 'vert40', 41: 'vert41'}

for r in range(35, 56):
    for c in range(2190, 2261):
        tid = all_tiles[r][c]
        if tid != 48 and tid in border_tiles:
            print(f"  Row {r:2d} Col {c:4d} (Y={r*16:4d},X={c*16:5d}): tile {tid:>3} = {border_tiles[tid]}")
