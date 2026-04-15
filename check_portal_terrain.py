import xml.etree.ElementTree as ET

tree = ET.parse('famidash/LEVELS/LEVEL DATA/lvlset_HUGE/kratos.tmx')
root = tree.getroot()
width = int(root.attrib['width'])
height = int(root.attrib['height'])
GRR = 3

tiles = [0] * (width * height)
for layer in root.findall('layer'):
    name = layer.get('name', '')
    if name == 'SP':
        continue
    data = layer.find('data')
    if data is None or data.get('encoding') != 'csv':
        continue
    gids = [int(x.strip()) for x in data.text.strip().split(',')]
    for i, gid in enumerate(gids):
        if i >= width * height:
            break
        if 1 <= gid <= 256:
            tiles[i] = gid - 1

# Extract collision table from MetatileCollision.cs
import re
with open('native-windows/MetatileCollision.cs', 'r') as f:
    content = f.read()
start = content.find('string mappingText = @"') + len('string mappingText = @"')
end = content.find('";', start)
mapping = content[start:end]

col_table = ['COL_NONE'] * 256
lines = [l for l in mapping.split('\n') if l.strip()]
idx = 0
rx = re.compile(r'COL_[A-Z0-9_]+')
for raw in lines:
    if idx >= 256: break
    line = raw.strip()
    m = rx.search(line)
    if m:
        name = m.group()
        dollar = line.find(';$')
        if dollar >= 0:
            idx = int(line[dollar+2:].strip(), 16)
        if idx < 256:
            col_table[idx] = name
        idx += 1

# Check terrain at cols 630-650, rows 10-20 (Y=112-272)
print("Terrain at cols 630-650 (X=10080-10400), rows 10-22:")
for c in range(630, 651):
    tiles_str = []
    for r in range(10, 23):
        t = tiles[r * width + c]
        ct = col_table[t]
        engine_y = (r - GRR) * 16
        if ct != 'COL_NONE':
            tiles_str.append(f"r{r}/Y{engine_y}:0x{t:02X}={ct}")
    if tiles_str:
        print(f"  col={c} (X={c*16}): {', '.join(tiles_str)}")
    else:
        print(f"  col={c} (X={c*16}): all NONE")
