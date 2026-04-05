import xml.etree.ElementTree as ET
tree = ET.parse(r'c:\Editor Test\eon2.tmx')
root = tree.getroot()
for layer in root.findall('layer'):
    name = layer.get('name')
    w = int(layer.get('width'))
    h = int(layer.get('height'))
    data = layer.find('data').text.strip()
    tiles = [int(t) for t in data.split(',')]
    print(f'Layer: {name} ({w}x{h})')
    # X range: tiles 400-420 (px 6400-6720)
    for y in range(h):
        row_tiles = []
        for x in range(395, min(420, w)):
            tid = tiles[y * w + x]
            if tid != 0:
                row_tiles.append(f'({x},{y})=0x{tid:X}')
        if row_tiles:
            print(f'  Row {y}: {" ".join(row_tiles)}')
