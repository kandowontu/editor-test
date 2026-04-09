import xml.etree.ElementTree as ET

tree = ET.parse('famidash/LEVELS/LEVEL DATA/lvlset_HUGE/deathmoon.tmx')
root = tree.getroot()
width = int(root.get('width'))
height = int(root.get('height'))
print(f'Map: {width}x{height}')

# Collision table (from MetatileCollision.cs)
COL_NAMES = {
    0: 'EMPTY', 1: 'FLOOR_CEIL', 2: 'TOP', 3: 'BOTTOM', 4: 'ALL',
    5: 'DEATH', 6: 'NO_SIDE', 7: 'RIGHT', 
}

# Read collision table from the C# file
col_table = {}
try:
    with open('native-windows/MetatileCollision.cs', 'r') as f:
        content = f.read()
    import re
    # Find the collision array
    match = re.search(r'new byte\[\]\s*\{([^}]+)\}', content, re.DOTALL)
    if match:
        vals = [int(x.strip(), 0) for x in match.group(1).split(',') if x.strip()]
        for i, v in enumerate(vals):
            col_table[i] = v
except:
    pass

for layer in root.findall('layer'):
    name = layer.get('name')
    data = layer.find('data').text.strip()
    tiles = [int(x) for x in data.split(',')]
    print(f'\nLayer: {name}, tiles={len(tiles)}')
    
    # Show tiles around tileX=1124-1134, tileY=47-54 (Y=752-864)
    for ty in range(47, 55):
        row = []
        for tx in range(1120, 1140):
            idx = ty * width + tx
            if idx < len(tiles):
                tid = tiles[idx]
                if name == 'SP':
                    if tid != 0:
                        row.append(f'{tid:3X}')
                    else:
                        row.append('  .')
                else:
                    if tid != 0:
                        col = col_table.get(tid, -1)
                        cname = COL_NAMES.get(col, f'c{col}')
                        row.append(f'{tid:3d}({cname[:3]})')
                    else:
                        row.append('   .    ')
            else:
                row.append('  ?')
        ypx = ty * 16
        print(f'  Y={ypx:4d} (ty={ty}): {" ".join(row)}')
