import xml.etree.ElementTree as ET
tree = ET.parse(r'famidash\LEVELS\LEVEL DATA\lvlset_HUGE\groundtospace.tmx')
root = tree.getroot()
bg_data = None
for layer in root.findall('.//layer'):
    name = layer.get('name')
    data = layer.find('data')
    if data is not None and bg_data is None and name != 'SP':
        rows = []
        for line in data.text.strip().split('\n'):
            line = line.strip().rstrip(',')
            if line:
                rows.append([int(x) for x in line.split(',')])
        bg_data = rows

# Show unique tile values in the coin 2 area (cols 510-680)
tiles = set()
for row in range(len(bg_data)):
    for col in range(510, 681):
        v = bg_data[row][col]
        if v != 0:
            tiles.add(v)
print(f'Unique non-zero tiles in area: {sorted(tiles)}')
print(f'Count: {len(tiles)}')

# Show a vertical slice at coin column (657) ± 5 cols
print()
print('Vertical slice at coin col 657 +/- 5:')
for row in range(len(bg_data)):
    vals = []
    for col in range(652, 663):
        vals.append(f'{bg_data[row][col]:3d}')
    marker = ' << COIN' if row == 12 else ''
    print(f'r{row:02d}: ' + ' '.join(vals) + marker)

# Show area with just 0 vs non-zero, but narrow range around coin path
# Cols 630-680, to see if there's an open corridor
print()
print('Terrain detail cols 630-680:')
for row in range(len(bg_data)):
    line = ''
    for col in range(630, 681):
        v = bg_data[row][col]
        if v == 0:
            line += '.'
        elif v < 10:
            line += str(v)
        else:
            line += '#'
    marker = ' << COIN' if row == 12 else ''
    print(f'r{row:02d} {line}{marker}')
