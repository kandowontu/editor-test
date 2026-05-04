import re
text = open(r'native-windows/MetatileCollision.cs', encoding='utf-8').read()
m = re.search(r'string mappingText = @"(.*?)";', text, re.DOTALL)
mt = m.group(1)
lines = re.split(r'[\n\r]', mt)
lines_kept = [l for l in lines if l != '']
print(f'total kept lines: {len(lines_kept)}')
rx = re.compile(r'COL_[A-Z0-9_]+')
for idx in range(0, min(40, len(lines_kept))):
    raw = lines_kept[idx]
    m2 = rx.search(raw.strip())
    val = m2.group(0) if m2 else '(NONE)'
    print(f'  idx {idx}: {repr(raw)[:50]} -> {val}')
