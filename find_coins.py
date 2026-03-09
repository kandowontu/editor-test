import xml.etree.ElementTree as ET
tree = ET.parse(r'c:\Editor Test\cycles.tmx')
root = tree.getroot()
for layer in root.findall('layer'):
    name = layer.get('name', '')
    print(f'layer: {name}')
for ts in root.findall('tileset'):
    print(f'tileset: firstgid={ts.get("firstgid")} name={ts.get("name")}')

# Find coins in all layers
for layer in root.findall('layer'):
    data = layer.find('data').text.strip()
    tiles = [int(x) for x in data.split(',')]
    w = int(layer.get('width'))
    name = layer.get('name', '')
    for i, t in enumerate(tiles):
        if t >= 257:
            sid = t - 257
            if sid in (0x07, 0x1A, 0x1B):
                col = i % w
                row = i // w
                names = {0x07: 'SECRET', 0x1A: 'COIN2', 0x1B: 'COIN3'}
                print(f'{names[sid]} sid=0x{sid:02X} layer={name} col={col} row={row} X={col*16} Y={row*16}')
