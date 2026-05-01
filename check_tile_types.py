import re
text = open('native-windows/MetatileCollision.cs', encoding='utf-8').read()
m = re.search(r'string mappingText = @"(.*?)";', text, re.DOTALL)
raw = m.group(1)
lines = [l.strip() for l in re.split(r'[\r\n]+', raw) if l.strip()]
rx = re.compile(r'COL_[A-Z0-9_]+')
table = {}
for i, line in enumerate(lines):
    m2 = rx.search(line)
    if m2:
        table[i] = m2.group(0)
    else:
        table[i] = '???'

for t in [17,27,33,34,35,36,37,38,39,40,45,46,47,48,49,50,51,53,54,156,157,160,161,173,174,182,183,190,191]:
    print(f'Tile {t:3d}: {table.get(t,"???")}')

print()
print("=== Tiles around col 220-222 arrY=25-27 ===")
# arrY=25: col220=0, col221=35, col222=35, col223=0
# arrY=26: col220=0, col221=36, col222=47, col223=0  
# arrY=27: col220=0, col221=36, col222=34, col223=0
for t in [35, 36, 47, 34]:
    print(f'Tile {t}: {table.get(t,"???")}')
