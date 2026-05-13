import re
src = open('native-windows/MetatileCollision.cs', encoding='utf-8').read()
m = re.search(r'string mappingText = @"(.+?)";', src, re.S)
text = m.group(1)
entries = []
i = 0
for ln in text.splitlines():
    s = ln.strip()
    if not s:
        continue
    if ';' in s:
        nm, idx = s.split(';')
        idx = int(idx.strip().lstrip('$'), 16)
        entries.append((idx, nm.strip()))
        i = idx + 1
    else:
        entries.append((i, s))
        i += 1
print('Total entries:', len(entries), 'last index:', entries[-1][0])
for tgt in [0xB6, 0xBA, 0x37, 0x12, 0x60, 0x03, 0xB7, 0xBB, 0x04]:
    found = [e for e in entries if e[0] == tgt]
    print(f'0x{tgt:02X} ({tgt:3}): {found}')
