import xml.etree.ElementTree as ET
import os, sys

# Write output directly to a file
out = open(r'c:\Editor Test\route_result.txt', 'w', encoding='utf-8')
def pr(s=''):
    out.write(s + '\n')

tree = ET.parse(os.path.join('..', 'famidash', 'levels', 'LEVEL DATA', 'lvlset_HUGE', 'eon.tmx'))
root = tree.getroot()
w = int(root.get('width'))
h = int(root.get('height'))
data = root.findall('layer')[0].find('data').text.strip()
tiles = [int(x) for x in data.split(',')]
spdata = root.findall('layer')[1].find('data').text.strip()
sptiles = [int(x) for x in spdata.split(',')]
grr = 3

# Collision type names
col_names = {
    0: 'NONE', 16: 'ALL', 17: 'HALF_TOP', 18: 'DEATH_BTM',
    27: 'DEATH', 30: 'DEATH_TOP', 52: 'DECO',
    176: 'CUP_LEFT', 177: 'CUP_RIGHT', 178: 'CDN_LEFT', 179: 'CDN_RIGHT',
    180: 'COL_TOP', 181: 'COL_BTM', 182: 'COL_LEFT', 183: 'COL_RIGHT',
    184: 'STAIR_TL', 185: 'STAIR_TR', 186: 'STAIR_BL', 187: 'STAIR_BR',
}

# Sprite names
sp_names = {
    0x28: 'YELLOW_ORB', 0x05: 'BLUE_ORB', 0xF8: 'H_BLOCK',
    0xF7: 'J_BLOCK', 0xF6: 'F_BLOCK', 0x54: 'SPIDER_UP',
    0x55: 'SPIDER_DN', 0x22: 'DUAL', 0x23: 'SINGLE',
}

print("=== TERRAIN DETAIL: cols 1828-1850, rows 18-27 ===")
print(f"{'row':>3s} {'wY':>4s} |", end='')
for col in range(1828, 1851):
    print(f'{col:>5d}', end='')
print()
print("-" * 120)

for tmx_row in range(18, 28):
    wY = (tmx_row - grr) * 16
    print(f'{tmx_row:3d} {wY:4d} |', end='')
    for col in range(1828, 1851):
        if tmx_row >= h or col >= w:
            print('  OOB', end='')
            continue
        gid = tiles[tmx_row * w + col]
        ed = gid - 1 if gid > 0 else 0
        name = col_names.get(ed, f't{ed}')
        short = name[:5]
        print(f'{short:>5s}', end='')
    print()

print()
print("=== SPRITES: cols 1828-1850, rows 18-27 ===")
for tmx_row in range(18, 28):
    wY = (tmx_row - grr) * 16
    line = f'{tmx_row:3d} {wY:4d} | '
    for col in range(1828, 1851):
        if tmx_row >= h or col >= w:
            line += '  .  '
            continue
        sp = sptiles[tmx_row * w + col]
        if sp <= 256:
            line += '  .  '
        else:
            sid = sp - 257
            name = sp_names.get(sid, f's{sid:02X}')
            line += f'{name[:5]:>5s}'
    print(line)

print()
print("=== FORWARD COLLISION ANALYSIS AT COL 1847 ===")
col = 1847
print(f"Forward collision probe at rightEdge in col {col}")
print(f"For non-mini cube: hbW=12, hbH=12, hbOffY=0")
print(f"centerY = playerY + 6")
print()
for playerY in range(270, 340, 2):
    centerY = playerY + 6
    tileY = centerY // 16
    tileArrayY = tileY + grr
    gid = tiles[tileArrayY * w + col] if 0 <= tileArrayY < h else 0
    ed = gid - 1 if gid > 0 else 0
    name = col_names.get(ed, f't{ed}')
    localY = centerY - tileY * 16
    localX_start = 0  # right edge just enters tile
    
    occupy = ed in (16,) or (180 <= ed <= 183)  # simplified
    death_kill = False
    if ed == 27:  # COL_DEATH
        death_kill = (4 <= localY <= 11)  # only if localX in [4,8] too
    elif ed == 30:  # DEATH_TOP
        death_kill = (localY < 6)  # only if localX in [5,7] too
    
    result = 'SOLID_KILL' if occupy else ('DEATH_POSSIBLE' if death_kill else 'PASS')
    print(f'  Y={playerY:3d} cY={centerY:3d} tileY={tileY:2d} row={tileArrayY:2d} ed={ed:3d} ({name:10s}) localY={localY:2d} -> {result}')

print()
print("=== FORWARD COLLISION ANALYSIS AT COL 1833 ===")
col = 1833
for playerY in range(280, 340, 2):
    centerY = playerY + 6
    tileY = centerY // 16
    tileArrayY = tileY + grr
    gid = tiles[tileArrayY * w + col] if 0 <= tileArrayY < h else 0
    ed = gid - 1 if gid > 0 else 0
    name = col_names.get(ed, f't{ed}')
    localY = centerY - tileY * 16
    
    occupy = ed in (16,) or (180 <= ed <= 183)
    death_kill = False
    if ed == 27:
        death_kill = (4 <= localY <= 11)
    elif ed == 30:
        death_kill = (localY < 6)
    
    result = 'SOLID_KILL' if occupy else ('DEATH_POSSIBLE' if death_kill else 'PASS')
    print(f'  Y={playerY:3d} cY={centerY:3d} tileY={tileY:2d} row={tileArrayY:2d} ed={ed:3d} ({name:10s}) localY={localY:2d} -> {result}')
