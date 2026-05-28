import xml.etree.ElementTree as ET

tmx_path = r'sim-famidash\LEVELS\LEVEL DATA\lvlset_D\dorabaebasic7.tmx'
t = ET.parse(tmx_path)
root = t.getroot()
W = int(root.get('width'))
H = int(root.get('height'))
print(f'map {W}x{H}')

cx0, cx1 = 530, 562
ry0, ry1 = 32, 40
for l in root.findall('layer'):
    nm = l.get('name')
    data = l.find('data').text.strip()
    rows = [r for r in data.split('\n')]
    grid = []
    for r in rows:
        grid.append([int(x) for x in r.strip().rstrip(',').split(',')])
    print(f'\n--- layer "{nm}" ({len(grid)}x{len(grid[0]) if grid else 0}) ---')
    print('       ' + ' '.join(f'{c:5d}' for c in range(cx0, cx1)))
    for r in range(ry0, ry1):
        if r >= len(grid):
            continue
        row = grid[r]
        vals = [row[c] if c < len(row) else 0 for c in range(cx0, cx1)]
        print(f'r={r:3d} y={r*16:4d}: ' + ' '.join(f'{v:5d}' for v in vals))
