import xml.etree.ElementTree as ET
import re

tree = ET.parse(r'c:\Editor Test\famidash\levels\LEVEL DATA\lvlset_HUGE\eon.tmx')
root = tree.getroot()
w = 4036; groundRows = 3
tile_data = None; sprite_data = None
for layer in root.findall('layer'):
    name = layer.get('name', 'unnamed')
    data = layer.find('data')
    csv = data.text.strip()
    vals = [int(v.strip()) for v in csv.split(',') if v.strip()]
    if name == 'SP': sprite_data = vals
    else: tile_data = vals

# Collision type lookup
col_table = {}
with open(r'c:\Editor Test\native-windows\MetatileCollision.cs', 'r') as f:
    content = f.read()
m = re.search(r'@"(.*?)"', content, re.DOTALL)
if m:
    lines = [l.strip() for l in m.group(1).strip().split('\n') if l.strip()]
    for i, l in enumerate(lines):
        col_table[i] = l.split(';')[0]

def ctype(tidx):
    return col_table.get(tidx, 'UNKNOWN')

# Show terrain at cols 1830-1850, rows 16-23
print('Terrain cols 1830-1850:')
hdr = '%10s' % ''
for c in range(1830, 1851): hdr += '%6d' % c
print(hdr)
for r in range(16, 24):
    wY = (r - groundRows) * 16
    line = 'r%2d w%3d ' % (r, wY)
    for c in range(1830, 1851):
        idx = r * w + c
        tg = tile_data[idx] if idx < len(tile_data) else 0
        sg = sprite_data[idx] if idx < len(sprite_data) else 0
        sid = sg - 257 if sg > 0 else -1
        tid = tg - 1 if tg > 0 else -1
        if sid == 0xF8: t = ' H'
        elif sid == 0x28: t = ' O'
        elif sid == 0x05: t = ' B'
        elif sid >= 0 and tid < 0: t = ' s%x' % sid
        elif tid == 16: t = ' #'
        elif tid == 30: t = ' X'
        elif tid >= 0:
            ct = ctype(tid)
            if 'ALL' in ct: t = ' #'
            elif 'DEATH' in ct: t = ' DT'
            elif 'NONE' in ct: t = ' _'
            else: t = ' ?%d' % tid
        else: t = ' .'
        line += '%6s' % t
    print(line)

# Forward collision analysis at col 1847
print('\nForward collision analysis at col 1847 rightEdge:')
for Y in range(220, 310, 2):
    centerY = Y + 6  # non-mini cube center
    tileY = centerY // 16
    tileArrayY = tileY + groundRows
    col = 1847
    idx = tileArrayY * w + col
    if 0 <= idx < len(tile_data):
        tg = tile_data[idx]
        tid = tg - 1 if tg > 0 else -1
        ct = ctype(tid) if tid >= 0 else 'EMPTY'
        rightEdge = 1847 * 16  # approximate
        localX = 0  # leftmost pixel of the tile
        localY = centerY % 16
        kills = 'DEATH' in ct
        solid = 'ALL' in ct
        if tid >= 0 and ct != 'COL_NONE':
            print(f'  Y={Y} centerY={centerY} tileRow={tileY} arrRow={tileArrayY} tid={tid} col={ct} lY={localY} solid={solid} kills={kills}')
