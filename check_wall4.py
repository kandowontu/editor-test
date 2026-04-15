import xml.etree.ElementTree as ET

# Load collision names
COL_NAMES = {}
# From MetatileCollision.cs collision table
col_init = "1,1,1,1,0,1,1,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,6,6,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,1,1,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,7,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0"
col_vals = [int(x) for x in col_init.split(',')]
# 0=NONE, 1=FC, 2=ALL, 3=DEATH, 4=SLOPE_*, 5=MINI_*, 6=SL45_*, 7=HALF
col_type_names = ['NONE', 'FC', 'ALL', 'DEATH', 'SLOPE', 'MINI', 'SL45', 'HALF']

tree = ET.parse('famidash/LEVELS/LEVEL DATA/lvlset_HUGE/kratos.tmx')
root = tree.getroot()
width = int(root.attrib['width'])
height = int(root.attrib['height'])
print(f'Map: {width}x{height}')

tiles = [0] * (width * height)
for layer in root.findall('layer'):
    name = layer.get('name', '')
    if name == 'SP':
        continue  # skip sprite layer
    data = layer.find('data')
    if data is None or data.get('encoding') != 'csv':
        continue
    gids = [int(x.strip()) for x in data.text.strip().split(',')]
    for i, gid in enumerate(gids):
        if i >= width * height:
            break
        if 1 <= gid <= 256:
            tiles[i] = gid - 1
        elif gid == 0:
            tiles[i] = 0  # empty = 0, which maps to metatile 0x00

GRR = 3  # groundRowsToReserve

# Check cols 708-716 (X=11328 to X=11456)
for c in range(705, 720):
    print(f'\nCol {c} (X={c*16}):')
    for r in range(height):
        t = tiles[r * width + c]
        engine_y = (r - GRR) * 16
        ct = col_vals[t] if t < 256 else 0
        name = col_type_names[ct]
        if name != 'NONE':
            print(f'  row={r} engineY={engine_y} tile=0x{t:02X} col={name}')
