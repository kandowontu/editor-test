"""Definitive terrain analysis: print every tile gid/tid/collision
for columns 760-810, engine rows 6-21 (TMX rows 9-24)."""

import xml.etree.ElementTree as ET, csv, re, io

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

# Parse collision table matching C# idx++ per line (including non-COL lines)
collision_table = ['COL_NONE'] * 256
rx = re.compile(r'COL_[A-Z0-9_]+')
idx = 0
for line in COLLISION_TEXT.strip().split('\n'):
    if idx >= 256:
        break
    m = rx.search(line.strip())
    if m:
        collision_table[idx] = m.group(0)
    # else: stays COL_NONE (e.g., PAL_0 line)
    idx += 1

# Verify key entries
print("=== Collision table verification ===")
for i in [0x00, 0x10, 0x16, 0x21, 0x2F, 0x4F, 0x50]:
    print(f"  0x{i:02X} ({i:3d}): {collision_table[i]}")

# Load TMX
tree = ET.parse('stereomadness.tmx')
root = tree.getroot()
w = int(root.get('width'))
h = int(root.get('height'))
print(f"\nMap: {w}x{h}")

# Find terrain layer (first unnamed or first layer)
terrain_layer = None
for layer in root.findall('.//layer'):
    name = layer.get('name')
    if name is None or name == '' or name.lower() != 'sp':
        terrain_layer = layer
        break

data = terrain_layer.find('data')
encoding = data.get('encoding', '')
if encoding == 'csv':
    gids = [int(x) for x in data.text.strip().replace('\n', ',').split(',') if x.strip()]
else:
    gids = list(map(int, data.text.strip().split()))

GR = 3  # groundRowsToReserve
TILE_FIRST_GID = 1

def coll(gid):
    if gid == 0:
        return '.'  # empty
    tid = gid - TILE_FIRST_GID
    if 0 <= tid < 256:
        return collision_table[tid]
    return f'?{tid}'

# Check cols 760-810, ER 6-21 (TMX rows 9-24)
er_range = range(6, 22)
col_range = range(760, 811)

# Print header
hdr = f"{'col':>4}"
for er in er_range:
    hdr += f"  ER{er:<3}"
print(f"\n{hdr}")

# Short collision name
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

for col in col_range:
    row_str = f"{col:>4}"
    for er in er_range:
        tmx_row = er + GR
        if tmx_row >= h or col >= w:
            row_str += "  ---"
        else:
            gid = gids[tmx_row * w + col]
            c = coll(gid)
            s = short(c)
            row_str += f"  {s:>4}"
    print(row_str)

# Also show specific tiles with full details for key positions
print("\n=== Key tile details ===")
key_positions = [
    (764, 13), (764, 16), (764, 18),
    (765, 13), (765, 16),
    (796, 13), (796, 15), (796, 16),
    (797, 13), (797, 16),
    (800, 13), (800, 16),
    (803, 13), (803, 15), (803, 16),
    (804, 13), (804, 14), (804, 16),
    (877, 11), (877, 13),
]
for (col, er) in key_positions:
    tmx_row = er + GR
    gid = gids[tmx_row * w + col]
    tid = gid - TILE_FIRST_GID if gid > 0 else -1
    c = coll(gid) if gid > 0 else 'EMPTY'
    print(f"  col={col} ER{er} (TMX row {tmx_row}): gid={gid} tid=0x{tid:02X}({tid}) → {c}")

# Check: at which columns between 765-878 is ER13 NOT solid?
print("\n=== ER13 gap analysis (cols 765-878) ===")
gaps = []
for col in range(765, 879):
    tmx_row = 13 + GR  # = 16
    gid = gids[tmx_row * w + col]
    c = coll(gid)
    is_solid = c in ('COL_ALL', 'COL_FLOOR_CEIL', 'COL_TOP', 'COL_BOTTOM', 'COL_TOP_CENTER_SPIKE')
    if not is_solid:
        gaps.append((col, c))
if gaps:
    print(f"  Gaps found at {len(gaps)} columns:")
    for col, c in gaps:
        print(f"    col {col}: {c}")
else:
    print("  No gaps found - ER13 is solid everywhere")

# At gap columns, check what other solid tiles exist between ER10-ER17
print("\n=== Barrier tiles at ER13 gap columns ===")
for col, _ in gaps:
    barriers = []
    for er in range(10, 18):
        tmx_row = er + GR
        gid = gids[tmx_row * w + col]
        c = coll(gid)
        is_solid = c in ('COL_ALL', 'COL_FLOOR_CEIL', 'COL_TOP', 'COL_BOTTOM', 'COL_TOP_CENTER_SPIKE')
        is_death = 'DEATH' in c
        if is_solid or is_death:
            barriers.append(f"ER{er}={short(c)}")
    if barriers:
        print(f"  col {col}: {', '.join(barriers)}")
    else:
        print(f"  col {col}: CLEAR (no solid/death tiles ER10-17)")
