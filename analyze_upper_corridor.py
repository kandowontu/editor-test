"""Analyze terrain from cols 790-890, engine rows 6-18, to see the path
from lower corridor through the ER13 gap to the coin at col 877 ER11."""

import xml.etree.ElementTree as ET, re

COLLISION_TEXT = """,COL_NONE,
,COL_FLOOR_CEIL,
,COL_FLOOR_CEIL,
,COL_BOTTOM,
,COL_DEATH_TOP,
,COL_FLOOR_CEIL,
,COL_FLOOR_CEIL,
,COL_NONE,
,COL_DEATH_BOTTOM,
,COL_DEATH_BOTTOM,
,COL_DEATH_TOP,
,COL_DEATH_TOP,
,COL_DEATH_BOTTOM,
,COL_DEATH_TOP,
,COL_DEATH_LEFT,
,COL_DEATH_RIGHT,
,COL_ALL,
,COL_DEATH,
,COL_DEATH_BOTTOM,
,COL_DEATH_BOTTOM,
,COL_DEATH_TOP,
,COL_TOP_CENTER_SPIKE,
,COL_ALL,
,COL_DEATH_TOP,
,COL_DEATH_BOTTOM,
,COL_TOP,
,COL_DEATH,
,COL_DEATH,
,COL_DEATH,
,COL_DEATH_LEFT,
,COL_DEATH_TOP,
,COL_DEATH_RIGHT,
,COL_ALL,
,COL_ALL,
,COL_ALL,
,COL_ALL,
,COL_ALL,
,COL_ALL,
,COL_ALL,
,COL_ALL,
,COL_ALL,
,COL_ALL,
,COL_ALL,
,COL_ALL,
,COL_ALL,
,COL_ALL,
,COL_ALL,
,COL_NONE,
,COL_ALL,
,COL_ALL,
,COL_ALL,
,COL_ALL,
,COL_NONE,
,COL_NONE,
,COL_NONE,
,COL_TOP_CENTER_SPIKE,
,COL_ALL,
,COL_ALL,
,COL_ALL,
,COL_DEATH_BOTTOM,
,COL_ALL,
,COL_ALL,
,COL_ALL,
,COL_ALL,
,COL_ALL,
,COL_ALL,
,COL_ALL,
,COL_ALL,
,COL_ALL,
,COL_ALL,
,COL_ALL,
,COL_ALL,
,COL_ALL,
,COL_ALL,
,COL_ALL,
,COL_ALL,
,COL_ALL,
,COL_ALL,
,COL_ALL,
,COL_ALL,
,PAL_0,
,COL_NONE,
,COL_NONE,
,COL_NONE,
,COL_NONE,
,COL_NONE,
,COL_NONE,
,COL_NONE,
,COL_NONE,
,COL_NONE,
,COL_NONE,
,COL_NONE,
,COL_NONE,
,COL_NONE,
,COL_NONE,
,COL_NONE,
,COL_NONE,"""

collision_table = ['COL_NONE'] * 256
rx = re.compile(r'COL_[A-Z0-9_]+')
idx = 0
for line in COLLISION_TEXT.strip().split('\n'):
    if idx >= 256: break
    m = rx.search(line.strip())
    if m:
        collision_table[idx] = m.group(0)
    idx += 1

tree = ET.parse('stereomadness.tmx')
root = tree.getroot()
w = int(root.get('width'))
h = int(root.get('height'))

terrain_layer = None
for layer in root.findall('.//layer'):
    name = layer.get('name')
    if name is None or name == '' or name.lower() != 'sp':
        terrain_layer = layer
        break

data = terrain_layer.find('data')
gids = [int(x) for x in data.text.strip().replace('\n', ',').split(',') if x.strip()]

GR = 3
TILE_FIRST_GID = 1

def coll(gid):
    if gid == 0: return '.'
    tid = gid - TILE_FIRST_GID
    if 0 <= tid < 256:
        return collision_table[tid]
    return f'?{tid}'

def short(c):
    if c == '.': return '.'
    if c == 'COL_NONE': return 'n'
    if c == 'COL_ALL': return '#'
    if c == 'COL_FLOOR_CEIL': return 'FC'
    if c == 'COL_TOP': return 'T'
    if c == 'COL_BOTTOM': return 'B'
    if c == 'COL_DEATH': return 'D'
    if c == 'COL_DEATH_TOP': return 'DT'
    if c == 'COL_DEATH_BOTTOM': return 'DB'
    if c == 'COL_DEATH_LEFT': return 'DL'
    if c == 'COL_DEATH_RIGHT': return 'DR'
    if c == 'COL_TOP_CENTER_SPIKE': return 'CS'
    return c[:4]

def is_solid(c):
    """Returns True if this collision type blocks movement."""
    return c in ('COL_ALL', 'COL_FLOOR_CEIL', 'COL_TOP', 'COL_BOTTOM')

def is_death(c):
    return 'DEATH' in c or c == 'COL_DEATH'

# Print terrain grid cols 790-890, ER 6-18
er_range = range(6, 19)
col_range = range(790, min(891, w))

print(f"Map: {w}x{h}")
print(f"ER = engine row (TMX row - {GR})")
print(f"Y coordinate for ER = ER * 16")
print()

# Header
hdr = f"{'col':>4}"
for er in er_range:
    hdr += f" ER{er:<3}"
print(hdr)
print(f"{'Y->':>4}", end='')
for er in er_range:
    print(f" {er*16:<4}", end='')
print()

for col in col_range:
    row_str = f"{col:>4}"
    for er in er_range:
        tmx_row = er + GR
        if tmx_row >= h or col >= w:
            row_str += " --- "
        else:
            gid = gids[tmx_row * w + col]
            c = coll(gid)
            s = short(c)
            row_str += f" {s:>4}"
    print(row_str)

# For the coin path analysis, find where vertical passage is clear
# Ship needs to go from lower corridor (ER14-ER16, Y=224-272) to 
# upper corridor (ER9-ER12, Y=144-208), crossing ER13 (Y=208-224)
print("\n=== Vertical passage analysis (ER13 wall) ===")
print("Can ship cross from ER14 to ER12 (through ER13)?")
for col in col_range:
    er13_tmx = 13 + GR
    gid13 = gids[er13_tmx * w + col]
    c13 = coll(gid13)
    
    # Also check ER12 (above) and ER14 (below)
    er12_tmx = 12 + GR
    gid12 = gids[er12_tmx * w + col] if er12_tmx < h else 0
    c12 = coll(gid12)
    
    er14_tmx = 14 + GR
    gid14 = gids[er14_tmx * w + col] if er14_tmx < h else 0
    c14 = coll(gid14)
    
    s13 = 'SOLID' if is_solid(c13) else ('DEATH' if is_death(c13) else 'OPEN')
    s12 = 'SOLID' if is_solid(c12) else ('DEATH' if is_death(c12) else 'open')
    s14 = 'SOLID' if is_solid(c14) else ('DEATH' if is_death(c14) else 'open')
    
    if s13 != 'SOLID':
        print(f"  col {col}: ER13={s13}({short(c13)})  ER12={s12}({short(c12)})  ER14={s14}({short(c14)})")

# Upper corridor analysis: what blocks exist above ER13?
print("\n=== Upper corridor (ER8-ER12) obstacles ===")
print("Showing any non-empty tiles in the upper zone")
for col in col_range:
    obstacles = []
    for er in range(8, 13):
        tmx_row = er + GR
        if tmx_row >= h: continue
        gid = gids[tmx_row * w + col]
        c = coll(gid)
        if c != '.':
            obstacles.append(f"ER{er}={short(c)}")
    if obstacles:
        print(f"  col {col}: {', '.join(obstacles)}")

# Coin position
print("\n=== Coin at col 877 ER11 ===")
for er in range(8, 18):
    tmx_row = er + GR
    gid = gids[tmx_row * w + 877]
    c = coll(gid)
    print(f"  ER{er} (Y={er*16}): {short(c)} ({c})")
