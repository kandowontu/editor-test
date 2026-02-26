import re

with open(r'C:\Editor Test\native-windows\MetatileCollision.cs', 'r') as f:
    txt = f.read()

# Extract the mapping string between @" and ";
s = txt.find('@"') + 2
e = txt.find('";', s)
raw = txt[s:e]

rx = re.compile(r'COL_[A-Z0-9_]+')
lines = [l for l in raw.split('\n') if l.strip()]
table = {}
for i, l in enumerate(lines):
    m = rx.search(l.strip())
    table[i] = m.group() if m else 'COL_NONE'

print(f'Total entries parsed: {len(lines)}')
for mt in [0x0C, 0x0D, 0x10, 0x11, 0x12, 0x18, 0x19, 0x1B, 0x1C, 0x1D, 0x2D, 0x2E, 0xE8, 0xE9]:
    col = table.get(mt, 'NOT IN TABLE')
    print(f'  0x{mt:02X} ({mt:3d}) -> {col}')
