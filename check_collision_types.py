import re

with open(r'c:\Editor Test\native-windows\MetatileCollision.cs', 'r') as f:
    content = f.read()

# Find the mapping text between @" and ";
start = content.find('string mappingText = @"') + len('string mappingText = @"')
end = content.find('";', start)
mapping = content[start:end]

# Extract collision names
lines = [l.strip() for l in mapping.split('\n') if l.strip().startswith('COL_')]
print(f'Total entries: {len(lines)}')
for tid in [79, 144, 145, 146, 147, 136, 137, 0, 1, 2, 3, 5, 6]:
    if tid < len(lines):
        print(f'Tile {tid:3d}: {lines[tid]}')
    else:
        print(f'Tile {tid:3d}: OUT OF RANGE')
