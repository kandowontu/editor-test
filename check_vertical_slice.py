"""Vertical terrain slice at X=9056 (col 566) to understand corridor structure."""
import xml.etree.ElementTree as ET
import re

TMX = r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\kratos.tmx"

# Load collision table
with open(r'c:\Editor Test\native-windows\MetatileCollision.cs', 'r') as f:
    content = f.read()
m = re.search(r'mappingText\s*=\s*@"(.*?)";', content, re.DOTALL)
text = m.group(1)
lines = [l.strip() for l in text.split('\n') if l.strip()]
rx = re.compile(r'COL_[A-Z0-9_]+')
col_entries = []
for l in lines:
    m2 = rx.search(l)
    if m2:
        col_entries.append(m2.group(0))

tree = ET.parse(TMX)
root = tree.getroot()
width = int(root.attrib['width'])
height = int(root.attrib['height'])
tw = int(root.attrib.get('tilewidth', 16))
th = int(root.attrib.get('tileheight', 16))
print(f"Map: {width}x{height}, tile: {tw}x{th}")

layer = root.find('.//layer')
data = layer.find('data').text.strip()
tiles_raw = [int(x.strip()) for x in data.replace('\n', ',').split(',') if x.strip()]

def get_col(gid):
    if gid == 0: return 'EMPTY'
    if gid - 1 < len(col_entries): return col_entries[gid - 1]
    return f'UNK({gid})'

# How does TmxHandler map Y?
# In many GD clones: TMX row 0 = top of map, Y_pixel = row * tileHeight
# But game might invert Y. Let's check both conventions.

print(f"\nTotal pixel height: {height * th}")
print(f"\nVertical slice at X=9056 (col 566), all rows:")
print(f"{'Row':>4} {'Y(down)':>8} {'Y(up)':>8} {'GID':>5} {'Collision':<25}")
for row in range(height):
    idx = row * width + 566
    gid = tiles_raw[idx] if idx < len(tiles_raw) else 0
    ct = get_col(gid)
    y_down = row * th  # Y increasing downward
    y_up = (height - 1 - row) * th  # Y increasing upward
    marker = ""
    if gid != 0 and ct != 'COL_NONE':
        marker = " <-- BLOCKING"
    print(f"{row:4d} {y_down:8d} {y_up:8d} {gid:5d} {ct:<25}{marker}")

# Also check nearby columns to see corridor structure
print(f"\nHorizontal slice at multiple rows, cols 561-575 (X=8976-9200):")
for row in [4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22]:
    y_down = row * th
    y_up = (height - 1 - row) * th
    tiles_str = ""
    for col in range(561, 576):
        idx = row * width + col
        gid = tiles_raw[idx] if idx < len(tiles_raw) else 0
        ct = get_col(gid)
        if ct == 'COL_NONE' or ct == 'EMPTY':
            tiles_str += "."
        elif 'DEATH' in ct or 'SPIKE' in ct:
            tiles_str += "X"
        elif ct == 'COL_ALL':
            tiles_str += "#"
        elif ct == 'COL_FLOOR_CEIL':
            tiles_str += "="
        elif ct == 'COL_FLOOR_ONLY':
            tiles_str += "_"
        elif ct == 'COL_CEIL_ONLY':
            tiles_str += "^"
        else:
            tiles_str += "?"
    print(f"  row {row:2d} Y_d={y_down:3d} Y_u={y_up:3d}: {tiles_str}")
