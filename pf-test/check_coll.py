import re

with open(r'C:\Editor Test\native-windows\MetatileCollision.cs', 'r') as f:
    content = f.read()

start = content.find('mappingText = @"') + len('mappingText = @"')
end = content.find('";', start)
mapping_text = content[start:end]

lines = [l for l in mapping_text.split('\n') if l.strip()]
rx = re.compile(r'COL_[A-Z0-9_]+')
table = ['COL_NONE'] * 256
for idx, raw in enumerate(lines):
    if idx >= 256: break
    m = rx.search(raw.strip())
    if m:
        table[idx] = m.group()

for tid in [27, 35, 47, 72, 79]:
    print(f'  tile {tid}: {table[tid]}')
