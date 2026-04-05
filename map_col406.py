import xml.etree.ElementTree as ET
tree = ET.parse(r'c:\Editor Test\eon2.tmx')
root = tree.getroot()
layer = root.findall('.//layer')[0]  # first layer is collision
data = layer.find('data')
tiles_raw = [int(t.get('gid','0')) & 0xFF for t in data.findall('tile')]
w = int(layer.get('width'))
h = int(layer.get('height'))
col = 406
print(f'Column {col} (X={col*16}px):')
for row in range(h):
    tid = tiles_raw[row * w + col]
    if tid != 0:
        print(f'  row {row} (Y={row*16}px): 0x{tid:02X}')
print()
print("Cols 404-411, rows 0-20:")
for row in range(21):
    line = f'row {row:2d}: '
    for c in range(404, 412):
        tid = tiles_raw[row * w + c]
        line += f'{tid:02X} '
    print(line)
