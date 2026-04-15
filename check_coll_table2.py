import re

text = open(r'c:\Editor Test\native-windows\MetatileCollision.cs').read()
# Find the mapping text
start = text.find('string mappingText = @"') + len('string mappingText = @"')
end = text.find('";', start)
mapping = text[start:end]

pattern = re.compile(r'COL_[A-Z0-9_]+')
entries = []
for line in mapping.split('\n'):
    line = line.strip()
    m = pattern.search(line)
    if m:
        entries.append(m.group(0))

for tid in [0, 6, 17, 30, 47, 70, 71, 77, 78, 111]:
    col = entries[tid] if tid < len(entries) else '???'
    print(f'tile {tid}: {col}')

# Also print the corridor at col 451
import xml.etree.ElementTree as ET
tree = ET.parse(r'c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\kratos.tmx')
root = tree.getroot()
width = int(root.get('width'))
data = root.findall('layer')[0].find('data').text.strip()
gids = [int(x) for x in data.split(',')]
print('\nColumn 451 (X=7216-7231):')
for row in range(10, 22):
    idx = row * width + 451
    gid = gids[idx]
    tid = gid - 1 if gid > 0 else -1
    col_name = entries[tid] if 0 <= tid < len(entries) else 'EMPTY'
    print(f'  r{row} Y={row*16}-{row*16+15}: tile={tid} -> {col_name}')
