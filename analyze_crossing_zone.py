#!/usr/bin/env python3
"""Analyze the exact terrain at rows 14-20 for cols 790-890 in stereomadness.tmx."""

import xml.etree.ElementTree as ET

# Collision table from MetatileCollision.cs - maps tile ID to collision type
COLLISION_TABLE = [
    "NONE","ALL","ALL","ALL","ALL","ALL","NONE","ALL",
    "ALL","ALL","ALL","ALL","ALL","ALL","DEATH_LEFT","ALL",
    "ALL","DEATH","ALL","ALL","ALL","ALL","ALL","ALL",
    "DEATH_BOTTOM","DEATH_TOP","ALL","ALL","ALL","ALL","ALL","ALL",
    "NONE","NONE","ALL","NONE","ALL","ALL","ALL","ALL",
    "ALL","ALL","ALL","ALL","ALL","ALL","ALL","NONE",
    "ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL",
    "ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL",
    "ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL",
    "ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL",
    "UP_LEFT_SPIKE","ALL","ALL","UP_RIGHT_SPIKE","ALL","ALL","ALL","ALL",
    "UP_BOTH_SPIKES","ALL","ALL","ALL","ALL","ALL","ALL","ALL",
    "DOWN_LEFT_SPIKE","ALL","DOWN_RIGHT_SPIKE","ALL","DOWN_BOTH_SPIKES","ALL","ALL","ALL",
    "ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL",
    "ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL",
    "ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL",
    "ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL",
    "ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL",
    "ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL",
    "ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL",
    "ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL",
    "ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL",
    "ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL",
    "ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL",
    "ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL",
    "ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL",
    "ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL",
    "ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL",
    "ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL",
    "ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL",
    "ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL",
    "ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL",
]

def get_collision(tile_id):
    if tile_id == 0:
        return "EMPTY"
    # GID is 1-based in TMX
    idx = (tile_id - 1) & 0xFF
    if idx < len(COLLISION_TABLE):
        return COLLISION_TABLE[idx]
    return f"UNK({idx})"

tree = ET.parse(r"c:\Editor Test\stereomadness.tmx")
root = tree.getroot()

map_width = int(root.attrib['width'])
map_height = int(root.attrib['height'])

# Find terrain layer
terrain_layer = None
for layer in root.findall('layer'):
    name = layer.attrib.get('name', '')
    if name == '' or name is None or name == 'None':
        terrain_layer = layer
        break

if not terrain_layer:
    print("No Terrain layer found!")
    exit(1)

data = terrain_layer.find('data')
tiles_text = data.text.strip()
tiles = [int(x) for x in tiles_text.split(',')]

def get_tile(col, row):
    if 0 <= col < map_width and 0 <= row < map_height:
        return tiles[row * map_width + col]
    return 0

print(f"Map: {map_width}x{map_height}")
print(f"\nDetailed terrain at rows 14-21, cols 790-885:")
print(f"{'Col':>4}", end="")
for r in range(14, 22):
    print(f"  {'R'+str(r)+' (Y'+str(r*16)+'-'+str(r*16+15)+')':>20}", end="")
print()
print("-" * 170)

for col in range(790, 886):
    print(f"{col:>4}", end="")
    for row in range(14, 22):
        tid = get_tile(col, row)
        coll = get_collision(tid)
        # Abbreviate
        abbr = coll[:6] if len(coll) > 6 else coll
        print(f"  {abbr:>20}", end="")
    print()

# Specifically look for safe crossing points
print("\n\n=== CROSSING ANALYSIS (rows 16-17) ===")
print("Looking for columns where row 17 is NOT a death tile (safe crossing):")
for col in range(790, 886):
    t16 = get_tile(col, 16)
    t17 = get_tile(col, 17)
    c16 = get_collision(t16)
    c17 = get_collision(t17)
    if "DEATH" not in c17:
        marker = " *** SAFE CROSSING ***" if c17 in ("EMPTY", "NONE") else ""
        print(f"  Col {col}: row16=0x{t16:02X}({c16}) row17=0x{t17:02X}({c17}){marker}")

print("\n=== ALL DEATH TILE COLUMNS (row 17) ===")
for col in range(790, 886):
    t17 = get_tile(col, 17)
    c17 = get_collision(t17)
    if "DEATH" in c17:
        print(f"  Col {col}: row17=0x{t17:02X}({c17})")
