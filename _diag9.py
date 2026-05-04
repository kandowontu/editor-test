import re, os
text = open('native-windows/MetatileCollision.cs').read()
m = re.search(r'string mappingText = @"(.+?)";', text, re.DOTALL)
body = m.group(1)
lines = []
for raw in body.split('\n'):
    s = raw.strip()
    rx = re.search(r'COL_[A-Z0-9_]+', s)
    if rx:
        lines.append(rx.group(0))
print('total mapped:', len(lines))
for i in [16, 17, 18, 28, 29, 30, 35, 38, 48, 49]:
    if i < len(lines):
        print(f' tile {i:3d} (0x{i:02X}) = {lines[i]}')
