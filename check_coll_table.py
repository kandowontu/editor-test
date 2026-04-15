lines = open(r'c:\Editor Test\native-windows\MetatileCollision.cs').readlines()
in_map = False
entries = []
for l in lines:
    l = l.strip()
    if l.startswith('COL_'):
        in_map = True
        entries.append(l)
    elif in_map and l == '':
        continue
    elif in_map and not l.startswith('COL_') and l != '':
        break

for tid in [0, 6, 17, 30, 47, 70, 71, 77, 78, 111]:
    col = entries[tid] if tid < len(entries) else '???'
    print(f'tile {tid}: {col}')
