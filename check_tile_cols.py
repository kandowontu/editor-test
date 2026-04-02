import re

with open(r'c:\Editor Test\native-windows\MetatileCollision.cs', 'r') as f:
    content = f.read()

m = re.search(r'@"(.*?)"', content, re.DOTALL)
if m:
    lines = [l.strip() for l in m.group(1).strip().split('\n') if l.strip()]
    for idx in [176, 177, 178, 179, 180, 181, 182, 183, 184, 185, 186]:
        if idx < len(lines):
            col_name = lines[idx].split(';')[0]
            print(f'tile {idx}: {col_name}')
