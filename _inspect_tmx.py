import xml.etree.ElementTree as ET
tree = ET.parse('test.tmx')
root = tree.getroot()
print('Map:', root.attrib)
layers = []
for child in root:
    n = child.attrib.get('name', '')
    layers.append((child.tag, n, child.attrib))
    if child.tag == 'layer':
        data = child.find('data')
        if data is not None:
            w = int(child.attrib.get('width', 0))
            h = int(child.attrib.get('height', 0))
            all_tiles = [int(x.strip()) for x in data.text.strip().split(',') if x.strip()]
            print(f'Layer: {n!r} {w}x{h}')
            # Show all non-zero rows for this layer
            for row in range(h):
                row_tiles = all_tiles[row*w:(row+1)*w]
                nz = [(i, v) for i, v in enumerate(row_tiles) if v != 0]
                if nz:
                    print(f'  Row {row}: {nz[:15]}')
print('All layers:', [(t, n) for t, n, _ in layers])
