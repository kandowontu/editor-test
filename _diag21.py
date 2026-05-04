import re
fp = r'famidash/LEVELS/LEVEL DATA/lvlset_HUGE/everyend.tmx'
data = open(fp, 'rb').read()
if data[:2] == b'\xff\xfe': data = data.decode('utf-16').encode('utf-8')
text = data.decode('utf-8', errors='replace')
m = re.search(r'<layer id="1"[^>]*>.*?<data[^>]*>(.*?)</data>', text, re.DOTALL)
csv_text = m.group(1).strip()
rows = [l.strip().rstrip(',') for l in csv_text.split('\n') if l.strip()]
grid = [[int(x) for x in r.split(',') if x.strip()] for r in rows]
W = len(grid[0])

# Player TL world (29835, 519) in PF coords. Probes (PF coords):
# BL feet: (29838, 532) tile (1864, 33) local (6, 4) → tmx[36][1864]
# BR feet: (29847, 532) tile (1865, 33) local (15, 4) → tmx[36][1865]
# TL top:  (29838, 521) tile (1864, 32) local (6, 9) → tmx[35][1864]
# TR top:  (29847, 521) tile (1865, 32) local (15, 9) → tmx[35][1865]

# tile_id = gid - 1
def tid(r, c):
    return grid[r][c] - 1

print("Tiles around death (rows 33..38, cols 1860..1870)")
for r in range(33, 39):
    print(f'  tmx r={r}: ' + ' '.join(f'{c}:{tid(r,c)}' for c in range(1860, 1871)))

print()
print(f'BL feet probe: tile_id={tid(36, 1864)} local=(6,4)')
print(f'BR feet probe: tile_id={tid(36, 1865)} local=(15,4)')
print(f'TL top   probe: tile_id={tid(35, 1864)} local=(6,9)')
print(f'TR top   probe: tile_id={tid(35, 1865)} local=(15,9)')
