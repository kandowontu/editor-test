import xml.etree.ElementTree as ET
tree = ET.parse(r"native-windows\famidash\LEVELS\LEVEL DATA\lvlset_C\hi.tmx")
root = tree.getroot()
layers = [l for l in root.findall('layer') if l.get('name') != 'Image Layer 2']
layer = layers[0]
w = int(layer.get('width')); h = int(layer.get('height'))
data = [int(x) for x in layer.find('data').text.strip().split(',')]
GRR = 3

# Print tiles at arrY=31,32,33,34 for cols 190-230
print('Checking cols 190-230 at arrY=31,32,33,34 (lower corridor and floor):')
for arrY in [31,32,33,34]:
    row_tiles = []
    for col in range(190, 231):
        idx = arrY * w + col
        if 0 <= idx < len(data):
            t = data[idx] - 1 if data[idx] > 0 else -1
            if t > 0:
                row_tiles.append((col, t))
    worldY = (arrY - GRR) * 16
    print(f'  arrY={arrY} (worldY={worldY}-{worldY+15}): {row_tiles}')

# Also check the obstacle structure and extended area
print()
print('Obstacle area cols 218-230 at arrY=27-36:')
for arrY in range(27, 37):
    row_tiles = []
    for col in range(218, 231):
        idx = arrY * w + col
        if 0 <= idx < len(data):
            t = data[idx] - 1 if data[idx] > 0 else -1
            if t >= 0:
                row_tiles.append((col, t))
    worldY = (arrY - GRR) * 16
    print(f'  arrY={arrY} (worldY={worldY}-{worldY+15}): {row_tiles}')

print()
print('Corridor cols 190-220 at arrY=29-34:')
for arrY in range(29, 35):
    row_tiles = []
    for col in range(190, 221):
        idx = arrY * w + col
        if 0 <= idx < len(data):
            t = data[idx] - 1 if data[idx] > 0 else -1
            if t > 0:
                row_tiles.append((col, t))
    worldY = (arrY - GRR) * 16
    print(f'  arrY={arrY} (worldY={worldY}-{worldY+15}): {row_tiles}')
