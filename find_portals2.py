import xml.etree.ElementTree as ET

# Portal SIDs (from SharedPhysics.cs SpriteIdToGameMode)
PORTAL_NAMES = {
    0x00: 'CUBE(0)', 0x01: 'SHIP(1)', 0x02: 'BALL(2)', 0x03: 'UFO(3)',
    0x04: 'ROBOT(4)', 0x17: 'SPIDER(5)', 0x24: 'WAVE(6)', 0x4B: 'SWING(7)', 0x58: 'NINJA(8)',
    0x0F: 'END_LEVEL', 0x10: 'GRAVITY_FLIP', 0x11: 'GRAVITY_NORMAL',
    0x12: 'MINI', 0x13: 'NORMAL_SIZE',
    0x14: 'SPEED_05', 0x15: 'SPEED_X1', 0x16: 'SPEED_X2', 0x18: 'SPEED_X3', 0x19: 'SPEED_X4',
}

tree = ET.parse('famidash/LEVELS/LEVEL DATA/lvlset_HUGE/kratos.tmx')
root = tree.getroot()
width = int(root.attrib['width'])
height = int(root.attrib['height'])
GRR = 3

# Read sprite layer
sprites = [-1] * (width * height)
for layer in root.findall('layer'):
    name = layer.get('name', '')
    if name != 'SP':
        continue
    data = layer.find('data')
    if data is None or data.get('encoding') != 'csv':
        continue
    gids = [int(x.strip()) for x in data.text.strip().split(',')]
    for i, gid in enumerate(gids):
        if i >= width * height:
            break
        if 257 <= gid <= 512:
            sprites[i] = gid - 257

# Find all portals between cols 600 and 750
print(f"Portals/triggers between cols 600-750 (X=9600-12000):")
for col in range(600, 751):
    for row in range(height):
        sid = sprites[row * width + col]
        if sid < 0:
            continue
        engine_y = (row - GRR) * 16
        name = PORTAL_NAMES.get(sid, f'SID=0x{sid:02X}')
        if sid in PORTAL_NAMES or sid < 0x60:
            print(f"  col={col} (X={col*16}) row={row} (Y={engine_y}) SID=0x{sid:02X} {name}")
