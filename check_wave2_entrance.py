#!/usr/bin/env python3
"""Check terrain at second wave section entrance (cols 440-470, X=7040-7520) for rows 10-20"""
import xml.etree.ElementTree as ET

# Collision type names
COL_NAMES = {
    0: "NONE", 1: "ALL", 2: "FLOOR_TOP", 3: "DEATH", 4: "DEATH_TOP",
    5: "FLOOR_CEIL", 6: "SLOPE_LU66_BOT", 7: "SLOPE_LU66_TOP",
    8: "SLOPE_LD66_BOT", 9: "SLOPE_LD66_TOP", 10: "SLOPE_RU66_BOT",
    11: "SLOPE_RU66_TOP", 12: "SLOPE_RD66_BOT", 13: "SLOPE_RD66_TOP",
}

tree = ET.parse(r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\kratos.tmx")
root = tree.getroot()

# Build tile->collision map
coll_map = {}
for ts in root.findall('.//tileset'):
    firstgid = int(ts.get('firstgid', 0))
    for tile in ts.findall('.//tile'):
        tid = firstgid + int(tile.get('id'))
        for prop in tile.findall('.//property'):
            if prop.get('name') == 'collision':
                coll_map[tid] = int(prop.get('value'))

# Find SP and terrain layers
terrain_tiles = None
for layer in root.findall('.//layer'):
    name = layer.get('name', '')
    if name == 'Terrain':
        terrain_data = layer.find('data').text.strip()
        t_width = int(layer.get('width'))
        t_height = int(layer.get('height'))
        terrain_tiles = [int(x) for x in terrain_data.split(',')]

if terrain_tiles is None:
    # Try group layers
    for group in root.findall('.//group'):
        for layer in group.findall('.//layer'):
            name = layer.get('name', '')
            if name == 'Terrain':
                terrain_data = layer.find('data').text.strip()
                t_width = int(layer.get('width'))
                t_height = int(layer.get('height'))
                terrain_tiles = [int(x) for x in terrain_data.split(',')]

print(f"Map size: {t_width}x{t_height}")
print()

# Check cols 440-470, rows 5-22
print("Terrain collision types for wave section entrance:")
print(f"{'Row':>3} {'Y':>4}", end="")
for c in range(440, 471):
    print(f" {c:>4}", end="")
print()

for r in range(5, 23):
    print(f"{r:>3} {r*16:>4}", end="")
    for c in range(440, 471):
        idx = r * t_width + c
        if idx < len(terrain_tiles):
            tid = terrain_tiles[idx]
            col_type = coll_map.get(tid, -1) if tid > 0 else -1
            if col_type == -1:
                print("    .", end="")
            else:
                name = COL_NAMES.get(col_type, f"?{col_type}")[:4]
                print(f" {name:>4}", end="")
        else:
            print("    ?", end="")
    print()

# Also check if there are death tiles in the corridor
print("\n\nDeath/solid tiles near Y=200-300 (rows 12-18), cols 450-470:")
for r in range(10, 22):
    for c in range(450, 471):
        idx = r * t_width + c
        if idx < len(terrain_tiles):
            tid = terrain_tiles[idx]
            col_type = coll_map.get(tid, -1) if tid > 0 else -1
            if col_type > 0:
                print(f"  r{r} c{c} (X={c*16},Y={r*16}): {COL_NAMES.get(col_type, f'?{col_type}')}")
