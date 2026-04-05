import xml.etree.ElementTree as ET
tree = ET.parse(r'c:\Editor Test\eon2.tmx')
root = tree.getroot()

# Check all SP sprites in X range 360-420 (px 5760-6720)
w = int(root.find('layer[@name="SP"]').get('width'))
h = int(root.find('layer[@name="SP"]').get('height'))
data = root.find('layer[@name="SP"]').find('data').text.strip()
tiles = [int(t) for t in data.split(',')]

print('All SP sprites in X=360-430 (px 5760-6880):')
for y in range(h):
    for x in range(360, 430):
        tid = tiles[y * w + x]
        if tid != 0:
            sid = tid - 257
            print(f'  SP ({x},{y}) px=({x*16},{y*16}) tmxTid=0x{tid:X} sid=0x{sid:02X} ({sid})')
