import re

with open(r'c:\Editor Test\native-windows\MetatileCollision.cs', 'r') as f:
    content = f.read()

# Extract mapping text
m = re.search(r'@"(.*?)"', content, re.DOTALL)
if m:
    lines = [l.strip() for l in m.group(1).strip().split('\n') if l.strip()]
    for idx in [0, 16, 18, 27, 30, 52, 180, 181, 182, 183]:
        if idx < len(lines):
            col_name = lines[idx].split(';')[0]
            print(f'tile {idx}: {col_name}')
        else:
            print(f'tile {idx}: OUT OF RANGE (only {len(lines)} entries)')
    print(f'Total collision entries: {len(lines)}')
