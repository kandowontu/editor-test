import xml.etree.ElementTree as ET, csv, io, sys

tree = ET.parse(r'c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\deathmoon.tmx')
root = tree.getroot()

# Use MetatileCollision table from the C# code
# Key tile IDs and their collision types
COL_NAMES = {
    0: '---', 1: 'FC', 2: 'TOP', 3: 'BOT', 4: 'RIGHT', 5: 'LEFT',
    6: 'NOSID', 16: 'ALL', 17: 'DEATH', 126: 'ALL_2'
}

# Get the first non-SP layer
layer = None
for l in root.findall('layer'):
    if (l.get('name') or '') != 'SP':
        layer = l
        break

w = int(layer.get('width'))
h = int(layer.get('height'))
data = layer.find('data').text.strip()
rows = list(csv.reader(io.StringIO(data)))
ground = 3
print(f'Map {w}x{h}, ground={ground}', flush=True)

# Print tiles at tileX=1120-1135 for tileY=44-55
for tx in range(1120, 1136):
    sys.stdout.write(f'\t{tx}')
sys.stdout.write('\n')
for tx in range(1120, 1136):
    sys.stdout.write(f'\t{tx*16}')
sys.stdout.write('\n')

for ty in range(44, 56):
    ay = ty + ground
    if ay < len(rows):
        row = rows[ay]
        sys.stdout.write(f'{ty}(pY{ty*16}-{ty*16+15})')
        for tx in range(1120, 1136):
            tid = int(row[tx].strip()) if tx < len(row) and row[tx].strip() else 0
            name = COL_NAMES.get(tid, f't{tid}')
            sys.stdout.write(f'\t{name}')
        sys.stdout.write('\n')
sys.stdout.flush()
