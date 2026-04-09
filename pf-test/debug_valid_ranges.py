"""Map valid centerY ranges for forward collision at each column in the death wall section."""
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

# Full collision mapping
col_text = """COL_NONE,COL_FLOOR_CEIL,COL_FLOOR_CEIL,COL_BOTTOM,COL_DEATH_TOP,COL_FLOOR_CEIL,COL_FLOOR_CEIL,COL_NONE,COL_DEATH_BOTTOM,COL_DEATH_BOTTOM,COL_DEATH_TOP,COL_DEATH_TOP,COL_DEATH_BOTTOM,COL_DEATH_TOP,COL_DEATH_LEFT,COL_DEATH_RIGHT,COL_ALL,COL_DEATH,COL_DEATH_BOTTOM,COL_DEATH_BOTTOM,COL_DEATH_TOP,COL_TOP_CENTER_SPIKE,COL_ALL,COL_DEATH_TOP,COL_DEATH_BOTTOM,COL_TOP,COL_DEATH,COL_DEATH,COL_DEATH,COL_DEATH_LEFT,COL_DEATH_TOP,COL_DEATH_RIGHT"""
# Continue from index 32
col_text += "," + ",".join(["COL_ALL"]*15) + ",COL_NONE"  # 32-47
col_text += ",COL_ALL,COL_ALL,COL_ALL,COL_ALL,COL_NONE,COL_NONE,COL_NONE,COL_TOP_CENTER_SPIKE,COL_ALL,COL_ALL,COL_ALL,COL_DEATH_BOTTOM,COL_ALL,COL_ALL,COL_ALL,COL_ALL"  # 48-63
col_names = col_text.split(',')

def get_col(tid):
    if tid < 0 or tid >= len(col_names): return "UNKNOWN"
    return col_names[tid]

def is_passable(col, local_y):
    """Returns True if forward collision passes at this point."""
    if col in ("COL_NONE", "COL_FLOOR_CEIL"):
        return True
    if col == "COL_ALL" or "COL_DEATH" in col:
        return False
    if col == "COL_TOP":
        return local_y >= 8  # bottom half open
    if col == "COL_BOTTOM":
        return local_y < 8  # top half open
    if col == "COL_TOP_CENTER_SPIKE":
        return local_y >= 8  # bottom half might be spike?
    return False  # conservative

# For each column from tx=1118 to tx=1128, find valid centerY ranges
print("=== Valid centerY ranges for forward collision (inverted grav: cY = Y+10, normal grav: cY = Y+5) ===")
for tx in range(1118, 1129):
    valid_ranges = []
    start = None
    for centerY in range(640, 870):
        tileY = centerY // TILE
        tileArrayY = tileY + GROUND
        if tileArrayY < 0 or tileArrayY >= h:
            passable = False
        else:
            idx = tileArrayY * w + tx
            gid = tile_vals[idx] if 0 <= idx < len(tile_vals) else 0
            tid = gid - 1 if gid > 0 else -1
            col = get_col(tid) if tid >= 0 else "COL_NONE"
            local_y = centerY - (tileY * TILE)
            passable = is_passable(col, local_y)
        
        if passable:
            if start is None: start = centerY
        else:
            if start is not None:
                valid_ranges.append((start, centerY-1))
                start = None
    if start is not None:
        valid_ranges.append((start, 869))
    
    px = tx * TILE
    ranges_str = " ".join(f"[{s}-{e}]" for s,e in valid_ranges)
    # Convert to playerY ranges for inverted grav (Y = centerY - 10) and normal grav (Y = centerY - 5)
    inv_ranges = " ".join(f"[{s-10}-{e-10}]" for s,e in valid_ranges)
    norm_ranges = " ".join(f"[{s-5}-{e-5}]" for s,e in valid_ranges)
    print(f"  tx={tx} (X={px}): centerY valid: {ranges_str}")
    print(f"    Inverted grav Y: {inv_ranges}")
    print(f"    Normal grav Y:   {norm_ranges}")
