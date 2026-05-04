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
def tid(r,c): return grid[r][c]-1

# ROM trace shows player at PF coords (2574, 736). Hitbox 15x15.
# PF replay y is sprite center; hitbox TL = (2574-8, 736-8) = (2566, 728)? 
# But PathPoints code: PathPoints.Add((X>>8)+8, worldY+8). So x_replay = X+8, y_replay = worldY+8.
# Internal: X = 2566, worldY = 728.
# Hitbox box: (X..X+15, Y..Y+15) = (2566..2581, 728..743).

# Let me check WIDER tiles. Player x range 2566..2581 -> tiles 160-161.
# Player y range 728..743 -> PF tile row 45-46, tmx row 48-49.
print('Tiles around hitbox')
for r in range(45, 53):
    print(f'  tmx r={r} (PF row {r-3}): ' + ' '.join(f'{c}:{tid(r,c)}' for c in range(157, 168)))

# CheckFloorSpikes probes
playerX, playerY = 2566, 728
hbW, hbH = 15, 15
leftX = playerX + 3      # 2569
rightX = playerX + hbW - 3  # 2578
rowBottomY = playerY + hbH - 2  # 741
rowTopY = playerY + 2     # 730
print(f'\nCheckFloorSpikes probes (hitbox TL ({playerX},{playerY})):')
for nm, x, y in [('BL', leftX, rowBottomY), ('BR', rightX, rowBottomY),
                  ('TL', leftX, rowTopY), ('TR', rightX, rowTopY)]:
    tx = x // 16
    ty_pf = y // 16
    tmx_r = ty_pf + 3
    t = tid(tmx_r, tx) if 0 <= tmx_r < len(grid) else -1
    lx, ly = x % 16, y % 16
    print(f'  {nm} ({x},{y}) tile=({tx},{tmx_r}) tid={t} local=({lx},{ly})')

# bg_coll_R probe (forward right edge)
print(f'\nbg_coll_R probes (mid-Y right edge):')
fwdX = playerX + hbW       # 2581
fwdY = playerY + hbH//2    # 735
tx = fwdX // 16
ty_pf = fwdY // 16
tmx_r = ty_pf + 3
print(f'  R ({fwdX},{fwdY}) tile=({tx},{tmx_r}) tid={tid(tmx_r,tx)} local=({fwdX%16},{fwdY%16})')

# bg_coll_death probe (center)
print(f'\nbg_coll_death probe (center):')
cX = playerX + (hbW>>1) - 1  # 2572 (NES: width/2 - 1)
cY = playerY + (hbH>>1)      # 735
tx = cX // 16
ty_pf = cY // 16
tmx_r = ty_pf + 3
print(f'  C ({cX},{cY}) tile=({tx},{tmx_r}) tid={tid(tmx_r,tx)} local=({cX%16},{cY%16})')
