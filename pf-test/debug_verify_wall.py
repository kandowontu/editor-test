"""Verify the exact terrain at the death wall (tx=1127) with collision types."""
import xml.etree.ElementTree as ET

tmx = r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\deathmoon.tmx"
tree = ET.parse(tmx)
root = tree.getroot()

w = int(root.attrib['width'])
h = int(root.attrib['height'])
TILE = 16
GROUND = 3

layers = root.findall('layer')
tile_data = layers[0].find('data').text.strip()
tile_vals = [int(x.strip()) for x in tile_data.split(',')]

# Collision table from MetatileCollision.cs
col_names = [
    "COL_NONE", "COL_FLOOR_CEIL", "COL_FLOOR_CEIL", "COL_BOTTOM",
    "COL_DEATH_TOP", "COL_FLOOR_CEIL", "COL_FLOOR_CEIL", "COL_NONE",
    "COL_DEATH_BOTTOM", "COL_DEATH_BOTTOM", "COL_DEATH_TOP", "COL_DEATH_TOP",
    "COL_DEATH_BOTTOM", "COL_DEATH_TOP", "COL_DEATH_LEFT", "COL_DEATH_RIGHT",
    "COL_ALL", "COL_DEATH", "COL_DEATH_BOTTOM", "COL_DEATH_BOTTOM",
    "COL_DEATH_TOP", "COL_TOP_CENTER_SPIKE", "COL_ALL", "COL_DEATH_TOP",
    "COL_DEATH_BOTTOM", "COL_TOP", "COL_DEATH", "COL_DEATH",
    "COL_DEATH", "COL_DEATH_LEFT", "COL_DEATH_TOP", "COL_DEATH_RIGHT",
]
# Add 32-47 (13x COL_ALL + COL_ALL + COL_ALL + COL_NONE)
for i in range(15): col_names.append("COL_ALL")
col_names.append("COL_NONE")
# 48-63
col_names.extend(["COL_ALL", "COL_ALL", "COL_ALL", "COL_ALL",
                   "COL_NONE", "COL_NONE", "COL_NONE", "COL_TOP_CENTER_SPIKE",
                   "COL_ALL", "COL_ALL", "COL_ALL", "COL_DEATH_BOTTOM",
                   "COL_ALL", "COL_ALL", "COL_ALL", "COL_ALL"])

def get_col(tid):
    if tid < 0 or tid >= len(col_names): return f"UNKNOWN({tid})"
    return col_names[tid]

# Check death wall at tx=1127
tx = 1127
print(f"=== Death wall at tx={tx} (X={tx*TILE}) ===")
for ty in range(40, 57):
    idx = ty * w + tx
    gid = tile_vals[idx] if idx < len(tile_vals) else 0
    tid = gid - 1 if gid > 0 else -1
    py = (ty - GROUND) * TILE
    py_end = py + 15
    col = get_col(tid) if tid >= 0 else "EMPTY"
    # What centerY values does this cover?
    world_y_start = py
    world_y_end = py + 15
    print(f"  ty={ty:2d} Y={world_y_start:4d}-{world_y_end:4d} tid={tid:3d} ({gid:3d}) -> {col}")

# Check column at tx=1122 (0x0E pad column)
tx = 1122
print(f"\n=== Blue pad 0x0E column at tx={tx} (X={tx*TILE}) ===")
for ty in range(44, 57):
    idx = ty * w + tx
    gid = tile_vals[idx] if idx < len(tile_vals) else 0
    tid = gid - 1 if gid > 0 else -1
    py = (ty - GROUND) * TILE
    col = get_col(tid) if tid >= 0 else "EMPTY"
    print(f"  ty={ty:2d} Y={py:4d}-{py+15:4d} tid={tid:3d} ({gid:3d}) -> {col}")

# Actually check: for centerY = Y+10 (mini inverted), what tile is hit at tx=1127?
print(f"\n=== Forward collision check at tx=1127 for various playerY ===")
for playerY in range(755, 790):
    centerY = playerY + 10  # mini inverted cube: +4 (miniTopOff) +3 (hbH>>1) +3 (grav adjust)
    tileY = centerY // TILE
    tileArrayY = tileY + GROUND
    idx = tileArrayY * w + 1127
    gid = tile_vals[idx] if 0 <= idx < len(tile_vals) else 0
    tid = gid - 1 if gid > 0 else -1
    col = get_col(tid) if tid >= 0 else "EMPTY"
    survives = col in ("EMPTY", "COL_NONE", "COL_FLOOR_CEIL", "COL_NO_SIDE")
    if "COL_TOP" in col:
        # Top half solid: check if centerY local Y < 8
        local_y = centerY - (tileY * TILE)
        if local_y >= 8:
            survives = True  # in open bottom half
    if "COL_BOTTOM" in col and "DEATH" not in col:
        local_y = centerY - (tileY * TILE)
        if local_y < 8:
            survives = True  # in open top half
    status = "PASS" if survives else "DEAD"
    print(f"  Y={playerY} centerY={centerY} tileArrayY={tileArrayY} tid={tid} col={col} -> {status}")
