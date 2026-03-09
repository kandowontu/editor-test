"""Show ER14 death spikes in approach to gap (cols 765-803)."""
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
,COL_NONE,"""

collision_table = ['COL_NONE'] * 256
rx = re.compile(r'COL_[A-Z0-9_]+')
idx = 0
for line in COLLISION_TEXT.strip().split('\n'):
    if idx >= 256: break
    m = rx.search(line.strip())
    if m: collision_table[idx] = m.group(0)
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
    if 0 <= tid < 256: return collision_table[tid]
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

# Focus on ER12-ER16 for cols 765-803 to see the approach path
print("Lower corridor approach to gap (cols 765-803)")
print("Ship enters at Y~302-327, must fly up to Y~224 then through gap to Y~170")
print()

# Show ER12-ER16 for these columns
er_range = range(12, 17)
col_range = range(765, 804)

hdr = f"{'col':>4}"
for er in er_range:
    hdr += f" ER{er:<3}"
    hdr += f"({er*16})"
print(hdr)

for col in col_range:
    row_str = f"{col:>4}"
    for er in er_range:
        tmx_row = er + GR
        gid = gids[tmx_row * w + col]
        c = coll(gid)
        s = short(c)
        row_str += f"  {s:>5}"
    print(row_str)

# Now show the vertical profile at the gap (col 796-803) from ER9 to ER17
print("\n\nGap vertical profile (cols 796-803)")
er_range2 = range(9, 18)
for col in range(796, 804):
    line = f"col {col}: "
    for er in er_range2:
        tmx_row = er + GR
        gid = gids[tmx_row * w + col]
        c = coll(gid)
        s = short(c)
        line += f"ER{er}={s} "
    print(line)

# Show what FindCorridorCenter would compute at the gap
# Ship hitbox: 15x15. hbH=15
# Ceiling scan: goes up from ship center until hitting solid/death
# Floor scan: goes down from ship center until hitting solid/death
# Then corridor center = (ceiling + floor) / 2 - hbH/2
print("\n\nSimulated FindCorridorCenter at gap cols (no lookahead)")
hbH = 15
for col in range(794, 806):
    # Find ceiling (lowest obstacle going up from ER13)
    ceiling_y = 0  # top of screen  
    for er in range(12, 5, -1):  # scan upward from ER12
        tmx_row = er + GR
        gid = gids[tmx_row * w + col]
        c = coll(gid)
        if c in ('COL_ALL', 'COL_FLOOR_CEIL', 'COL_TOP', 'COL_BOTTOM') or 'DEATH' in c or c == 'COL_DEATH':
            ceiling_y = (er + 1) * 16  # bottom of the blocking tile
            break
    
    # Find floor (highest obstacle going down from ER14)
    floor_y = 384  # bottom of screen
    for er in range(14, 22):  # scan downward from ER14
        tmx_row = er + GR
        if tmx_row >= h: break
        gid = gids[tmx_row * w + col]
        c = coll(gid)
        if c in ('COL_ALL', 'COL_FLOOR_CEIL', 'COL_TOP', 'COL_BOTTOM') or 'DEATH' in c or c == 'COL_DEATH':
            floor_y = er * 16  # top of the blocking tile
            break
    
    center = (ceiling_y + floor_y) // 2 - hbH // 2
    print(f"  col {col}: ceiling_y={ceiling_y} floor_y={floor_y} center={center}")
