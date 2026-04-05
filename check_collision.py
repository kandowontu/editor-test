import re

text = open(r'c:\Editor Test\native-windows\MetatileCollision.cs', 'r').read()
m = re.search(r'mappingText = @"(.*?)";', text, re.DOTALL)
mapping = m.group(1)
lines = [l.strip() for l in mapping.split('\n') if l.strip()]

table = {}
idx = 0
rx = re.compile(r'COL_[A-Z0-9_]+')
for line in lines:
    m2 = rx.search(line)
    if m2:
        table[idx] = m2.group()
    idx += 1

tids = [1, 16, 17, 19, 28, 31, 53, 179, 180, 181, 182, 183, 184]
for t in tids:
    col = table.get(t, "NONE/DEFAULT")
    print(f"Tile {t:3d} (0x{t:02X}) = {col}")
