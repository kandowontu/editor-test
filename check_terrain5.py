import xml.etree.ElementTree as ET
cs = open('native-windows/MetatileCollision.cs', encoding='utf-8').read()
start = cs.index('string mappingText = @"') + len('string mappingText = @"')
end = cs.index('";', start)
mapping = cs[start:end]
coll_table = [l.strip().split(';')[0].strip() for l in mapping.split('\n') if l.strip() and not l.strip().startswith('//')]

def get_coll(gid):
    if gid == 0: return 'EMPTY'
    iid = gid - 1
    if iid < 0 or iid >= len(coll_table): return f'OOB({iid})'
    return coll_table[iid].replace('COL_', '')

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

# Summarize floors for broader view c730-c760
for c in range(730, 760):
    non_empty = []
    for row in range(40, 57):
        gid = get_tile(c, row)
        if gid > 0 and gid != 1:
            non_empty.append(f'r{row}(Y={row*16})={get_coll(gid)}')
    if non_empty:
        print(f'c{c}(X={c*16}): {", ".join(non_empty)}')
