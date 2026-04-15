import xml.etree.ElementTree as ET, re

tree = ET.parse('famidash/LEVELS/LEVEL DATA/lvlset_HUGE/kratos.tmx')
root = tree.getroot()
width = int(root.attrib['width'])
height = int(root.attrib['height'])
GRR = 3

tiles = [0] * (width * height)
for layer in root.findall('layer'):
    if layer.get('name', '') == 'SP': continue
    data = layer.find('data')
    if data is None or data.get('encoding') != 'csv': continue
    gids = [int(x.strip()) for x in data.text.strip().split(',')]
    for i, gid in enumerate(gids):
        if i >= width * height: break
        if 1 <= gid <= 256: tiles[i] = gid - 1

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
    m = rx.search(raw.strip())
    if m:
        dollar = raw.strip().find(';$')
        if dollar >= 0: idx = int(raw.strip()[dollar+2:], 16)
        if idx < 256: col_table[idx] = m.group()
        idx += 1

# Check terrain at cols 535-570 (X=8560-9120), rows 5-19 (Y=32-256)
# Wave at Y=91 needs to go down to Y=222 for coin at Y=248
print("Terrain at cols 535-570 (around fbFrame X=8667), rows 5-19 (Y=32-256):")
for c in range(535, 571):
    tiles_str = []
    for r in range(5, 20):
        t = tiles[r * width + c]
        ct = col_table[t]
        engine_y = (r - GRR) * 16
        if ct != 'COL_NONE':
            tiles_str.append(f"r{r}/Y{engine_y}:{ct}")
    if tiles_str:
        print(f"  col={c} (X={c*16}): {', '.join(tiles_str)}")
    else:
        print(f"  col={c} (X={c*16}): all NONE")
