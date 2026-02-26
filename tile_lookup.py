import re

text = open(r'C:\Editor Test\native-windows\MetatileCollision.cs').read()
start = text.find('string mappingText = @"') + len('string mappingText = @"')
end = text.find('";', start)
mapping = text[start:end]
lines = [l for l in mapping.split('\n') if l.strip()]
table = []
for line in lines:
    m = re.search(r'COL_[A-Z0-9_]+', line.strip())
    if m:
        table.append(m.group())
print('Total collision entries:', len(table))
for tid in [0, 13, 16, 17, 18, 25, 29, 46, 233, 234]:
    if tid < len(table):
        print(f'  Tile {tid:3d} (0x{tid:02X}) = {table[tid]}')
    else:
        print(f'  Tile {tid:3d} (0x{tid:02X}) = OUT OF TABLE (idx {tid}, max {len(table)-1})')
