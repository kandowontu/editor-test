import xml.etree.ElementTree as ET
tree = ET.parse('famidash/LEVELS/LEVEL DATA/lvlset_HUGE/everyend.tmx')
root = tree.getroot()
w = int(root.get('width'))
layers = root.findall('.//layer')
layer = layers[0]
data = layer.find('data').text.strip().split(',')
tiles = [int(x.strip()) for x in data]
gRR = 3

def get_tile(col, row):
    arr_row = row + gRR
    idx = arr_row * w + col
    if idx < 0 or idx >= len(tiles): return 0
    return tiles[idx]

print('c778 tiles:')
for row in range(42, 56):
    gid = get_tile(778, row)
    iid = gid - 1 if gid > 0 else -1
    print(f'  row{row} (Y={row*16}): gid={gid} iid={iid}')

print('c779 tiles:')
for row in range(42, 56):
    gid = get_tile(779, row)
    iid = gid - 1 if gid > 0 else -1
    print(f'  row{row} (Y={row*16}): gid={gid} iid={iid}')
