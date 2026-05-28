import re
src = open('native-windows/MetatileCollision.cs').read()
m = re.search(r'string mappingText\s*=\s*@?"(.+?)";', src, re.DOTALL)
text = m.group(1)
rx = re.compile(r'COL_[A-Z0-9_]+')
table = ['COL_NONE'] * 256
idx = 0
for raw in re.split(r'[\r\n]+', text):
    if idx >= 256:
        break
    line = raw.strip()
    mm = rx.match(line)
    if mm:
        table[idx] = mm.group(0)
    idx += 1
for gid in [0, 1, 31, 76, 80, 100, 112, 163, 164]:
    print(f'GID {gid}: {table[gid]}')
