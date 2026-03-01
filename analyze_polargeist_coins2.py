import re

with open(r'c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\polargeist.tmx', 'r') as f:
    content = f.read()

pattern = r'<layer[^>]*>.*?<data encoding="csv">\s*(.*?)\s*</data>'
layers = re.findall(pattern, content, re.DOTALL)
tile_data = layers[0]
sprite_data = layers[1]

tile_rows = [line.strip() for line in tile_data.strip().split('\n') if line.strip()]
tile_grid = [[int(v.strip()) for v in row.rstrip(',').split(',')] for row in tile_rows]
sprite_rows_raw = [line.strip() for line in sprite_data.strip().split('\n') if line.strip()]
sprite_grid = [[int(v.strip()) for v in row.rstrip(',').split(',')] for row in sprite_rows_raw]

GRR = 3; TILE = 16; MH = len(tile_grid); MW = len(tile_grid[0])

def sprite_name(sid):
    names = {
        0x0A: 'YellowPad', 0x0C: 'YellowPad2', 0x0B: 'YellowOrb',
        0x0D: 'BluePad', 0x0E: 'BluePad2', 0x0F: 'ShipPortal',
        0x10: 'PinkOrb', 0x11: 'PinkOrb2', 0x12: 'PinkOrb3', 0x13: 'PinkOrb4',
        0x14: 'RedOrb', 0x15: 'RedOrb2', 0x16: 'RedOrb3',
        0x1A: 'Coin', 0x1B: 'Coin',
        0x07: 'SecretCoin',
        0x6F: 'CubePortal', 0x4F: 'BallPortal',
    }
    return names.get(sid, f'Spr_0x{sid:02X}')

# COIN 2 REGION
print('=== ALL SPRITES cols 575-610 (coin 2 region) ===')
for r in range(MH):
    for c in range(575, min(611, MW)):
        if c < len(sprite_grid[r]) and sprite_grid[r][c] != 0:
            gid = sprite_grid[r][c]
            sid = gid - 257 if gid >= 257 else gid
            wy = (r - GRR) * TILE
            print(f'  ({c:3d},{r:2d}) X={c*16:5d} Y={wy:4d} SID=0x{sid:02X} {sprite_name(sid)}')

# COIN 3 REGION
print()
print('=== ALL SPRITES cols 695-735 (coin 3 region) ===')
for r in range(MH):
    for c in range(695, min(736, MW)):
        if c < len(sprite_grid[r]) and sprite_grid[r][c] != 0:
            gid = sprite_grid[r][c]
            sid = gid - 257 if gid >= 257 else gid
            wy = (r - GRR) * TILE
            print(f'  ({c:3d},{r:2d}) X={c*16:5d} Y={wy:4d} SID=0x{sid:02X} {sprite_name(sid)}')

# ALL COINS
print()
print('=== ALL COINS in level ===')
for r in range(MH):
    for c in range(len(sprite_grid[r])):
        if sprite_grid[r][c] == 0: continue
        gid = sprite_grid[r][c]
        sid = gid - 257 if gid >= 257 else gid
        if sid in (0x07, 0x1A, 0x1B):
            wy = (r - GRR) * TILE
            hitTop = wy - 1
            print(f'  ({c:3d},{r:2d}) X={c*16:5d} hitbox_Y={hitTop}-{hitTop+16} SID=0x{sid:02X} {sprite_name(sid)}')

# ALL PORTALS
print()
print('=== ALL PORTALS in level ===')
portal_sids = {0x0F: 'ShipPortal', 0x6F: 'CubePortal', 0x4F: 'BallPortal'}
for r in range(MH):
    for c in range(len(sprite_grid[r])):
        if sprite_grid[r][c] == 0: continue
        gid = sprite_grid[r][c]
        sid = gid - 257 if gid >= 257 else gid
        if sid in portal_sids:
            wy = (r - GRR) * TILE
            print(f'  ({c:3d},{r:2d}) X={c*16:5d} Y={wy:4d} SID=0x{sid:02X} {portal_sids[sid]}')

# Tile data visual for coin 2: cols 585-600, showing collision type
collision_table = []
with open(r'c:\Editor Test\metatile_collision_table.txt', 'r') as f:
    collision_table = [l.strip() for l in f if l.strip()]

def col_short(tid):
    if tid == 0: return '....'
    ct = collision_table[tid] if tid < len(collision_table) else '????'
    short = {'COL_NONE': '    ', 'COL_ALL': 'SOLI', 'COL_TOP': 'TOPP',
             'COL_BOTTOM': 'BOTT', 'COL_FLOOR_CEIL': 'F/C ',
             'COL_DEATH': 'DETH', 'COL_DEATH_TOP': 'D_T ', 'COL_DEATH_BOTTOM': 'D_B ',
             'COL_DEATH_LEFT': 'D_L ', 'COL_DEATH_RIGHT': 'D_R ',
             'COL_NO_SIDE': 'NSID'}
    return short.get(ct, ct[:4])

print()
print('=== COLLISION MAP cols 585-600 (coin 2 region) ===')
print(f'{"Row":>3} {"Y":>4} ', end='')
for c in range(585, 601):
    print(f'{c:>5}', end='')
print()
for r in range(MH):
    wy = (r - GRR) * TILE
    print(f'{r:3d} {wy:4d} ', end='')
    for c in range(585, 601):
        tid = tile_grid[r][c] if c < len(tile_grid[r]) else 0
        print(f'{col_short(tid):>5}', end='')
    print()

print()
print('=== COLLISION MAP cols 710-725 (coin 3 region) ===')
print(f'{"Row":>3} {"Y":>4} ', end='')
for c in range(710, 726):
    print(f'{c:>5}', end='')
print()
for r in range(MH):
    wy = (r - GRR) * TILE
    print(f'{r:3d} {wy:4d} ', end='')
    for c in range(710, 726):
        tid = tile_grid[r][c] if c < len(tile_grid[r]) else 0
        print(f'{col_short(tid):>5}', end='')
    print()
