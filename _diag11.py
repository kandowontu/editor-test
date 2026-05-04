import re
fp = r'famidash/LEVELS/LEVEL DATA/lvlset_HUGE/everyend.tmx'
with open(fp, 'rb') as fh:
    data = fh.read()
if data[:2] == b'\xff\xfe':
    data = data.decode('utf-16').encode('utf-8')
text = data.decode('utf-8', errors='replace')

# find both 'None' tile layer
m = re.search(r'<layer[^>]*>.*?<data[^>]*>(.*?)</data>', text, re.DOTALL)
csv_text = m.group(1).strip()
rows = [l.strip().rstrip(',') for l in csv_text.split('\n') if l.strip()]
grid = [[int(x) for x in r.split(',') if x.strip()] for r in rows]
H = len(grid); W = len(grid[0])
print(f'mapW={W} mapH={H}')

# r2 NES bytes (rf=10797): 10 24 2F 00 2F 11 11 11 10 24 2F 22 10 24 2F 22
# tile_id = gid - 1, so gids: 17 37 48 1 48 18 18 18 17 37 48 35 17 37 48 35
target = [17, 37, 48, 1, 48, 18, 18, 18, 17, 37, 48, 35, 17, 37, 48, 35]
print('Searching tmx for collmap r2 gid pattern (16 cols):')
for r in range(H):
    row = grid[r]
    for c in range(W - len(target)):
        if row[c:c+len(target)] == target:
            print(f'  FOUND r={r} c={c}')

# Also find shorter unique pattern
short = [48, 18, 18, 18, 17]
print(f'\nSearching for {short}:')
for r in range(H):
    row = grid[r]
    for c in range(W - len(short)):
        if row[c:c+len(short)] == short:
            ctx = row[max(0,c-3):min(W,c+10)]
            print(f'  r={r} c={c} ctx={ctx}')

# Also dump rows 30..40 around col 1860..1880
print('\n--- tmx rows 30..40 cols 1860..1880 ---')
for r in range(30, 41):
    print(f'r={r}: {grid[r][1860:1880]}')
