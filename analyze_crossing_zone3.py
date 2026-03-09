#!/usr/bin/env python3
"""Analyze terrain for the second ship section entry (cols 740-810) to find upper corridor path."""
import xml.etree.ElementTree as ET
import re

# Parse collision table from MetatileCollision.cs
COLLISION_MAPPING_TEXT = """COL_NONE
COL_FLOOR_CEIL
COL_FLOOR_CEIL
COL_BOTTOM
COL_DEATH_TOP
COL_FLOOR_CEIL
COL_FLOOR_CEIL
COL_NONE
COL_DEATH_BOTTOM
COL_DEATH_BOTTOM
COL_DEATH_TOP
COL_DEATH_TOP
COL_DEATH_BOTTOM
COL_DEATH_TOP
COL_DEATH_LEFT
COL_DEATH_RIGHT
COL_ALL
COL_DEATH
COL_DEATH_BOTTOM
COL_DEATH_BOTTOM
COL_DEATH_TOP
COL_TOP_CENTER_SPIKE
COL_ALL
COL_DEATH_TOP
COL_DEATH_BOTTOM
COL_TOP
COL_DEATH
COL_DEATH
COL_DEATH
COL_DEATH_LEFT
COL_DEATH_TOP
COL_DEATH_RIGHT
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_NONE
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_NONE
COL_NONE
COL_NONE
COL_TOP_CENTER_SPIKE
COL_ALL
COL_ALL
COL_ALL
COL_DEATH_BOTTOM
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
,PAL_0,
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_TOP
COL_TOP
COL_TOP
COL_TOP
COL_BOTTOM
COL_BOTTOM
COL_BOTTOM
COL_DEATH
COL_DEATH_BOTTOM
COL_DEATH_TOP
COL_DEATH_RIGHT
COL_DEATH_LEFT
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_NONE
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_DOWN_RIGHT_SPIKE
COL_DEATH_BOTTOM
COL_DOWN_LEFT_SPIKE
COL_DEATH_RIGHT
COL_NONE
COL_DEATH_LEFT
COL_UP_RIGHT_SPIKE
COL_DEATH_TOP
COL_UP_LEFT_SPIKE
COL_DEATH
COL_ALL
COL_DEATH_BOTTOM
COL_NONE
COL_NONE
COL_DEATH
COL_DEATH
COL_DEATH_BOTTOM
COL_DEATH_TOP
COL_DEATH_BOTTOM
COL_DEATH_TOP
COL_FLOOR_CEIL
COL_FLOOR_CEIL
COL_RIGHT
COL_LEFT
COL_RIGHT
COL_LEFT
COL_NONE
COL_NO_SIDE
COL_SLOPE_RD45
COL_SLOPE_LD45
COL_SLOPE_RU45
COL_SLOPE_LU45
COL_SLOPE_RD22_RIGHT
COL_SLOPE_RD22_LEFT
COL_SLOPE_LD22_RIGHT
COL_SLOPE_LD22_LEFT
COL_SLOPE_RU22_RIGHT
COL_SLOPE_RU22_LEFT
COL_SLOPE_LU22_RIGHT
COL_SLOPE_LU22_LEFT
COL_SLOPE_RD66_TOP
COL_SLOPE_RD66_BOT
COL_SLOPE_LD66_BOT
COL_SLOPE_LD66_TOP
COL_SLOPE_RU66_TOP
COL_SLOPE_RU66_BOT
COL_SLOPE_LU66_BOT
COL_SLOPE_LU66_TOP
COL_NO_SIDE
COL_NO_SIDE
COL_NO_SIDE
COL_NO_SIDE
COL_LEFT_SPIKE_BLOCK
COL_RIGHT_SPIKE_BLOCK
COL_BOTTOM_LEFT_SPIKE
COL_BOTTOM_RIGHT_SPIKE
COL_BOTTOM_SPIKES
COL_DOWN_LEFT_SPIKE
COL_DOWN_RIGHT_SPIKE
COL_DOWN_BOTH_SPIKES
COL_UP_LEFT
COL_UP_RIGHT
COL_DOWN_LEFT
COL_DOWN_RIGHT
COL_TOP
COL_BOTTOM
COL_LEFT
COL_RIGHT
COL_TOP_LEFT_STAIRS
COL_TOP_RIGHT_STAIRS
COL_BOTTOM_LEFT_STAIRS
COL_BOTTOM_RIGHT_STAIRS
COL_TOP_LEFT_BOTTOM_RIGHT
COL_TOP_RIGHT_BOTTOM_LEFT
COL_UP_LEFT_SPIKE
COL_UP_RIGHT_SPIKE
COL_UP_BOTH_SPIKES
COL_DEATH_TOP_RIGHT
COL_DEATH_TOP_LEFT
COL_DEATH_BOTTOM_RIGHT
COL_DEATH_BOTTOM_LEFT
COL_NONE
COL_NONE
COL_BOTTOM_CENTER_SPIKE
COL_BOTTOM_CENTER_SPIKE
COL_TOP
COL_BOTTOM
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_ALL
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_DOWN_RIGHT_SPIKE
COL_DOWN_LEFT_SPIKE
COL_UP_LEFT_SPIKE
COL_UP_RIGHT_SPIKE
COL_LEFT
COL_RIGHT
COL_UP_RIGHT
COL_RIGHT
COL_TOP
COL_TOP
COL_UP_LEFT
COL_TOP
COL_RIGHT
COL_BOTTOM
COL_TOP
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_DOWN_RIGHT_SPIKE
COL_DOWN_LEFT_SPIKE
COL_UP_RIGHT_SPIKE
COL_UP_LEFT_SPIKE
COL_DOWN_RIGHT_SPIKE
COL_DOWN_LEFT_SPIKE
COL_DEATH
COL_RIGHT
COL_LEFT
COL_UP_RIGHT_SPIKE
COL_UP_LEFT_SPIKE
COL_DEATH
COL_ALL
COL_LEFT
COL_DOWN_RIGHT
COL_DOWN_LEFT"""

collision_table = ["NONE"] * 256
rx = re.compile(r'COL_([A-Z0-9_]+)')
idx = 0
for line in COLLISION_MAPPING_TEXT.strip().split('\n'):
    line = line.strip()
    m = rx.search(line)
    if m and idx < 256:
        collision_table[idx] = m.group(1)
        idx += 1

tree = ET.parse(r"c:\Editor Test\stereomadness.tmx")
root = tree.getroot()
map_width = int(root.attrib['width'])
map_height = int(root.attrib['height'])

terrain_layer = None
for layer in root.findall('layer'):
    name = layer.attrib.get('name', '')
    if name == '' or name is None:
        terrain_layer = layer
        break

data = terrain_layer.find('data')
tiles_text = data.text.strip()
raw_gids = [int(x) for x in tiles_text.split(',')]

GROUND_ROWS = 3

def get_gid(col, tmx_row):
    if 0 <= col < map_width and 0 <= tmx_row < map_height:
        return raw_gids[tmx_row * map_width + col]
    return 0

def get_collision(gid):
    if gid == 0:
        return "EMPTY"
    tile_idx = (gid - 1) & 0xFF
    return collision_table[tile_idx]

def abbr(coll):
    if coll == "EMPTY": return "."
    if coll == "NONE": return "n"
    if coll == "ALL": return "#"
    if coll == "FLOOR_CEIL": return "FC"
    if coll == "TOP": return "T"
    if coll == "BOTTOM": return "B"
    if "DEATH" in coll:
        return "D" + coll.replace("DEATH_","").replace("DEATH","c")[:3]
    if "SPIKE" in coll:
        return "S" + coll[:3]
    if "SLOPE" in coll:
        return "/" + coll[-3:]
    return coll[:4]

# Print engine rows 6-21 for cols 740-885
print(f"Engine rows 6-21 (Y values), cols 740-885")
print(f"ER  = engine row, Y = pixel range")
print(f"# = ALL (solid), . = EMPTY, n = NONE, D = death, T = TOP, FC = FLOOR_CEIL")
print()

# Header
print(f"{'Col':>4}", end="")
for er in range(6, 22):
    label = f"R{er}"
    print(f" {label:>4}", end="")
print()
print("-" * 70)

for col in range(740, 886):
    print(f"{col:>4}", end="")
    for er in range(6, 22):
        tr = er + GROUND_ROWS
        gid = get_gid(col, tr)
        coll = get_collision(gid)
        a = abbr(coll)
        print(f" {a:>4}", end="")
    print()

# Find the ship portal (cube→ship transition)
print("\n\n=== SPRITE LAYER: Looking for ship portal near cols 740-800 ===")
sp_layer = None
for layer in root.findall('layer'):
    name = layer.attrib.get('name', '')
    if name == 'SP':
        sp_layer = layer
        break
if sp_layer:
    sp_data = sp_layer.find('data')
    sp_text = sp_data.text.strip()
    sp_gids = [int(x) for x in sp_text.split(',')]
    
    # SpriteFirstGid = 257
    SPRITE_FIRST_GID = 257
    # Ship portal = sprite 0x01 (mode 1 = ship)
    # Mode portals: 0x00=cube, 0x01=ship, 0x02=ball, etc.
    
    for col in range(700, 890):
        for row in range(0, map_height):
            i = row * map_width + col
            if i < len(sp_gids):
                gid = sp_gids[i]
                if gid > 0:
                    sid = (gid - SPRITE_FIRST_GID) & 0xFF
                    er = row - GROUND_ROWS
                    # Print all sprites in the region
                    if col >= 740 and col <= 885:
                        print(f"  Col {col} ER{er} (TMX row {row}): sprite 0x{sid:02X} (GID {gid})")

# Find coin sprite
print("\n=== COIN SPRITE (0x1B) LOCATIONS ===")
if sp_layer:
    for col in range(0, map_width):
        for row in range(0, map_height):
            i = row * map_width + col
            if i < len(sp_gids):
                gid = sp_gids[i]
                if gid > 0:
                    sid = (gid - SPRITE_FIRST_GID) & 0xFF
                    if sid == 0x1B:  # Coin
                        er = row - GROUND_ROWS
                        y_px = er * 16
                        print(f"  Coin at col {col} ER{er} (Y={y_px}-{y_px+15})")
