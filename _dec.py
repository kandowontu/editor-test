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
mp = {k: v for k, v in entries}
for g in [97, 183, 53, 224, 31, 308, 380]:
    print('gid=%s -> %s (gid-1=%s -> %s)' % (g, mp.get(g, '?'), g-1, mp.get(g-1, '?')))
