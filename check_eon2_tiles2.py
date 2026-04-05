import xml.etree.ElementTree as ET
tree = ET.parse(r'c:\Editor Test\eon2.tmx')
root = tree.getroot()

# Print tilesets
for ts in root.findall('tileset'):
    print(f'Tileset: firstgid={ts.get("firstgid")} source={ts.get("source")} name={ts.get("name")}')

# Also look at tiles 380-400 for context leading up to failure
for layer in root.findall('layer'):
    name = layer.get('name')
    w = int(layer.get('width'))
    h = int(layer.get('height'))
    data = layer.find('data').text.strip()
    tiles = [int(t) for t in data.split(',')]
    print(f'\nLayer: {name} ({w}x{h}) -- tiles 380-395')
    for y in range(h):
        row_tiles = []
        for x in range(380, 395):
            tid = tiles[y * w + x]
            if tid != 0:
                row_tiles.append(f'({x},{y})=0x{tid:X}')
        if row_tiles:
            print(f'  Row {y}: {" ".join(row_tiles)}')

# Also check the SP layer for all sprites in X 380-420
print('\n--- SP layer sprites X=380-420 ---')
for layer in root.findall('layer'):
    name = layer.get('name')
    if name == 'SP':
        w = int(layer.get('width'))
        h = int(layer.get('height'))
        data = layer.find('data').text.strip()
        tiles = [int(t) for t in data.split(',')]
        for y in range(h):
            for x in range(380, 420):
                tid = tiles[y * w + x]
                if tid != 0:
                    print(f'  SP ({x},{y})=0x{tid:X} dec={tid} px=({x*16},{y*16})')
