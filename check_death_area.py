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

# Extract collision table
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
        if dollar >= 0:
            idx = int(raw.strip()[dollar+2:], 16)
        if idx < 256: col_table[idx] = m.group()
        idx += 1

# Check terrain at cols 735-750 (where the bf=667 die-off happens at X≈11843)
print("Terrain at cols 735-750 (X=11760-12000), rows 14-24 (Y=176-336):")
for c in range(735, 751):
    tiles_str = []
    for r in range(14, 25):
        t = tiles[r * width + c]
        ct = col_table[t]
        engine_y = (r - GRR) * 16
        if ct != 'COL_NONE':
            tiles_str.append(f"r{r}/Y{engine_y}:0x{t:02X}={ct}")
    if tiles_str:
        print(f"  col={c} (X={c*16}): {', '.join(tiles_str)}")

# Also check sprites
sprites = [-1] * (width * height)
for layer in root.findall('layer'):
    if layer.get('name', '') != 'SP': continue
    data = layer.find('data')
    if data is None or data.get('encoding') != 'csv': continue
    gids = [int(x.strip()) for x in data.text.strip().split(',')]
    for i, gid in enumerate(gids):
        if i >= width * height: break
        if 257 <= gid <= 512: sprites[i] = gid - 257

print("\nPortals/sprites at cols 735-755:")
PORTAL_NAMES = {0x00:'CUBE',0x01:'SHIP',0x02:'BALL',0x03:'UFO',0x04:'ROBOT',
    0x17:'SPIDER',0x24:'WAVE',0x0F:'END_LEVEL',0x10:'GRAV_FLIP',0x11:'GRAV_NORM',
    0x14:'SPD_05',0x15:'SPD_X1',0x16:'SPD_X2',0x18:'SPD_X3',0x19:'SPD_X4'}
for c in range(735, 756):
    for r in range(height):
        sid = sprites[r * width + c]
        if sid < 0: continue
        engine_y = (r - GRR) * 16
        name = PORTAL_NAMES.get(sid, f'SID=0x{sid:02X}')
        print(f"  col={c} (X={c*16}) r={r} (Y={engine_y}): {name} (SID=0x{sid:02X})")
