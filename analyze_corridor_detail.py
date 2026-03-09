"""Check the full vertical profile at cols 780-800 to understand corridorCenter."""
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

# Full vertical profile at cols 780-803
print(f"Map: {w}x{h}, groundRowsToReserve=3, worldBottom={(h-3)*16}")
print(f"Engine rows 0 to {h-GR-1} (TMX rows {GR} to {h-1})")
print()

er_range = range(0, h - GR)  # All engine rows
col_range = range(780, 804)

# Just show ER13-ER23 to see what's below
print("Full profile ER13-ER23 at cols 780-803:")
er_sub = range(13, min(h - GR, 24))
hdr = f"{'col':>4}"
for er in er_sub:
    hdr += f" ER{er}({er*16})"
print(hdr)

for col in col_range:
    row_str = f"{col:>4}"
    for er in er_sub:
        tmx_row = er + GR
        if tmx_row >= h:
            row_str += "  ---   "
        else:
            gid = gids[tmx_row * w + col]
            c = coll(gid)
            s = short(c)
            row_str += f"  {s:>5} "
    print(row_str)

# Simulate FindCorridorCenter with lookAhead=0
# Ship at Y=272, hbH=15
print("\n\nSimulated FindCorridorCenter (lookAhead=0) with ship at Y=272:")
hbH = 15
ship_y = 272
topTile = ship_y // 16  # 17
botTile = (ship_y + hbH - 1) // 16  # 17

for col in range(780, 804):
    ceiling_y = 0
    # Scan UP from topTile-1
    for ty in range(topTile - 1, -1, -1):
        tmx_row = ty + GR
        if tmx_row >= h: continue
        gid = gids[tmx_row * w + col]
        c = coll(gid)
        if c == '.' or c == 'COL_NONE': continue
        # Check solid: COL_ALL has bounds 0,0,16,16
        if c in ('COL_ALL', 'COL_FLOOR_CEIL', 'COL_TOP', 'COL_BOTTOM'):
            # cB for COL_ALL = 16
            ceiling_y = ty * 16 + 16
            break
        if 'DEATH' in c or c == 'COL_DEATH':
            ceiling_y = ty * 16 + 16
            break
    
    # Scan DOWN from botTile+1
    floor_y = (h - GR) * 16
    for ty in range(botTile + 1, h - GR):
        tmx_row = ty + GR
        if tmx_row >= h: break
        gid = gids[tmx_row * w + col]
        c = coll(gid)
        if c == '.' or c == 'COL_NONE': continue
        if c in ('COL_ALL', 'COL_FLOOR_CEIL', 'COL_TOP', 'COL_BOTTOM'):
            floor_y = ty * 16  # cT for COL_ALL = 0
            break
        if 'DEATH' in c or c == 'COL_DEATH':
            floor_y = ty * 16
            break
    
    center = (ceiling_y + floor_y) // 2 - hbH // 2
    print(f"  col {col}: ceiling={ceiling_y} floor={floor_y} center={center} (ER13_tile={short(coll(gids[(13+GR)*w+col]))})")
