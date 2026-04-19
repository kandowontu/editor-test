import xml.etree.ElementTree as ET
tree = ET.parse(r'c:\Editor Test\native-windows\famidash\LEVELS\LEVEL DATA\lvlset_B\wintherace.tmx')
root = tree.getroot()
w = int(root.get('width'))
h = int(root.get('height'))
layers = root.findall('.//layer')
print(f'{len(layers)} layers, w={w} h={h}')
for li, layer in enumerate(layers):
    name = layer.get('name', '')
    data = layer.find('data')
    if data is None:
        continue
    tiles = [int(x) for x in data.text.strip().split(',')]
    portals = []
    for i, tid in enumerate(tiles):
        if tid > 0:
            sid = (tid - 1) & 0xFF
            if sid in (0x09, 0x0A):
                tx = i % w
                ty = i // w
                portals.append((tx, ty, tid, sid))
    if portals:
        print(f'Layer {li} ({name}): {len(portals)} gravity portals')
        for tx, ty, tid, sid in portals:
            print(f'  tX={tx} tY={ty} tid={tid} sid=0x{sid:02X} wX={tx*16} wY={ty*16}')
