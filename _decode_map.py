import re
content = open(r'native-windows\MetatileCollision.cs').read()
m = re.search(r'mappingText\s*=\s*@"(.*?)"\s*;', content, re.DOTALL)
text = m.group(1)
entries = []
for line in text.splitlines():
    line = line.strip()
    if not line or line.startswith('//'):
        continue
    for p in line.split(','):
        p = p.strip()
        if p:
            entries.append(p)
print(f'total entries: {len(entries)}')
for tid in (0, 1, 3, 18, 55, 96, 97, 182, 183, 186, 187, 4):
    if tid < len(entries):
        print(f'tid {tid:>4d}: {entries[tid]}')
