import xml.etree.ElementTree as ET
tree = ET.parse(r'c:\Editor Test\native-windows\famidash\LEVELS\LEVEL DATA\lvlset_B\wintherace.tmx')
root = tree.getroot()
w = int(root.get('width'))
for li, layer in enumerate(root.findall('.//layer')):
    data = layer.find('data')
    if data is None: continue
    name = layer.get('name', '')
    tiles = [int(x) for x in data.text.strip().split(',')]
    for i, tid in enumerate(tiles):
        if tid > 0:
            sid = (tid - 1) & 0xFF
            tx = i % w
            ty = i // w
            if 428 <= tx <= 440:
                print(f'L{li}({name}) tX={tx} tY={ty} sid=0x{sid:02X} wX={tx*16} wY={ty*16}')
