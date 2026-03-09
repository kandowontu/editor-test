import xml.etree.ElementTree as ET
tree = ET.parse('cycles.tmx')
root = tree.getroot()
layers = root.findall('.//layer')
layer = layers[0]
data = layer.find('data')
width = int(layer.get('width'))
height = int(layer.get('height'))
tiles = [int(x) for x in data.text.strip().split(',')]
print('Ship section terrain (cols 450-480)')
for row in range(height):
    line = ''
    for col in range(450, min(480, width)):
        idx = row * width + col
        t = tiles[idx]
        if t > 0:
            line += '#'
        else:
            line += '.'
    marker = '  <-- Y=304 ship entry' if row == 19 else ''
    print(f'r{row:2d} Y={row*16:3d}: {line}{marker}')
