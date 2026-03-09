#!/usr/bin/env python3
"""Analyze crossing zone with CORRECT collision table from MetatileCollision.cs."""
import xml.etree.ElementTree as ET
import re

# Parse collision table from MetatileCollision.cs
COLLISION_MAPPING_TEXT = """
COL_NONE
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
COL_DOWN_LEFT
"""

# Build collision table
collision_table = ["NONE"] * 256
rx = re.compile(r'COL_([A-Z0-9_]+)')
idx = 0
for line in COLLISION_MAPPING_TEXT.strip().split('\n'):
    line = line.strip()
    m = rx.search(line)
    if m and idx < 256:
        collision_table[idx] = m.group(1)
        idx += 1

print(f"Parsed {idx} collision entries")

# Load TMX
tree = ET.parse(r"c:\Editor Test\stereomadness.tmx")
root = tree.getroot()
map_width = int(root.attrib['width'])
map_height = int(root.attrib['height'])

# Find terrain layer (unnamed layer)
terrain_layer = None
for layer in root.findall('layer'):
    name = layer.attrib.get('name', '')
    if name == '' or name is None:
        terrain_layer = layer
        break

data = terrain_layer.find('data')
tiles_text = data.text.strip()
raw_gids = [int(x) for x in tiles_text.split(',')]

def get_gid(col, row):
    if 0 <= col < map_width and 0 <= row < map_height:
        return raw_gids[row * map_width + col]
    return 0

def get_collision(gid):
    if gid == 0:
        return "EMPTY"
    tile_idx = (gid - 1) & 0xFF
    return collision_table[tile_idx]

# Note: groundRowsToReserve = 3 in the engine  
# tileArrayY = tileY + groundRowsToReserve
# So engine row 0 = TMX row 3, engine row 17 = TMX row 20, etc.
# For TMX rows: row R in TMX = engine row R-3
GROUND_ROWS = 3

print(f"\nMap: {map_width}x{map_height}")
print(f"NOTE: Engine uses groundRowsToReserve={GROUND_ROWS}")
print(f"  Engine row = TMX row - {GROUND_ROWS}")
print(f"  Engine row 17 (Y=272-287) = TMX row {17+GROUND_ROWS}")
print(f"  Engine row 16 (Y=256-271) = TMX row {16+GROUND_ROWS}")

print(f"\n=== CROSSING ZONE: Engine rows 12-21 (TMX rows 15-24), cols 790-885 ===")
print(f"{'Col':>4}", end="")
for er in range(12, 22):
    tr = er + GROUND_ROWS
    print(f"  {'ER'+str(er)+'(TR'+str(tr)+')':>16}", end="")
print()
print("-" * 170)

for col in range(790, 886):
    print(f"{col:>4}", end="")
    for er in range(12, 22):
        tr = er + GROUND_ROWS
        gid = get_gid(col, tr)
        coll = get_collision(gid)
        # Abbreviate
        if coll == "EMPTY":
            abbr = "."
        elif coll == "NONE":
            abbr = "none"
        elif coll == "ALL":
            abbr = "ALL"
        elif "DEATH" in coll:
            abbr = "D_" + coll.replace("DEATH_", "").replace("DEATH", "ctr")[:6]
        else:
            abbr = coll[:10]
        tid = (gid - 1) & 0xFF if gid > 0 else -1
        print(f"  {abbr:>16}", end="")
    print()

print(f"\n=== DETAILED CROSSING (engine rows 16-17, cols 790-885) ===")
print(f"Looking for safe crossings (no death AND no solid in both rows 16-17):")
for col in range(790, 886):
    er16_tr = 16 + GROUND_ROWS  # TMX row 19 = engine row 16
    er17_tr = 17 + GROUND_ROWS  # TMX row 20 = engine row 17
    gid16 = get_gid(col, er16_tr)
    gid17 = get_gid(col, er17_tr)
    c16 = get_collision(gid16)
    c17 = get_collision(gid17)
    tid16 = (gid16-1) & 0xFF if gid16 > 0 else -1
    tid17 = (gid17-1) & 0xFF if gid17 > 0 else -1
    
    is_safe = c16 in ("EMPTY", "NONE") and c17 in ("EMPTY", "NONE")
    is_death = "DEATH" in c17 or "DEATH" in c16
    is_solid16 = c16 not in ("EMPTY", "NONE") and "DEATH" not in c16
    is_solid17 = c17 not in ("EMPTY", "NONE") and "DEATH" not in c17
    
    marker = ""
    if is_safe:
        marker = " *** SAFE (both NONE/EMPTY) ***"
    elif is_death:
        marker = " [DEATH TILE]"
    elif is_solid16 and is_solid17:
        marker = " [SOLID WALL]"
    elif is_solid16:
        marker = " [R16 solid, R17 free]"
    elif is_solid17:
        marker = " [R16 free, R17 solid]"
    
    print(f"  Col {col}: R16=tid{tid16:02X}({c16:>16})  R17=tid{tid17:02X}({c17:>16}){marker}")

# Also check engine row 18 (TMX row 21) - space below death tiles
print(f"\n=== ENGINE ROW 18 (TMX row 21) - lower corridor ceiling ===")
for col in range(790, 886):
    er18_tr = 18 + GROUND_ROWS
    gid18 = get_gid(col, er18_tr)
    c18 = get_collision(gid18)
    tid18 = (gid18-1) & 0xFF if gid18 > 0 else -1
    if c18 not in ("EMPTY", "NONE"):
        print(f"  Col {col}: R18=tid{tid18:02X}({c18})")
