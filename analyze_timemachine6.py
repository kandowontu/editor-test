import re
with open(r'c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\timemachine.tmx','r') as f:
    content = f.read()
data_blocks = re.findall(r'<data encoding="csv">\s*([\s\S]*?)\s*</data>', content)
W=997; H=27; GR=3
tvals = [int(v.strip()) for v in data_blocks[0].replace('\n',',').split(',') if v.strip()]
svals = [int(v.strip()) for v in data_blocks[1].replace('\n',',').split(',') if v.strip()]

ct = ['COL_NONE','COL_FLOOR_CEIL','COL_FLOOR_CEIL','COL_BOTTOM','COL_DEATH_TOP','COL_FLOOR_CEIL','COL_FLOOR_CEIL','COL_NONE',
      'COL_DEATH_BOTTOM','COL_DEATH_BOTTOM','COL_DEATH_TOP','COL_DEATH_TOP','COL_DEATH_BOTTOM','COL_DEATH_TOP','COL_DEATH_LEFT','COL_DEATH_RIGHT',
      'COL_ALL','COL_DEATH','COL_DEATH_BOTTOM','COL_DEATH_BOTTOM','COL_DEATH_TOP','COL_TOP_CENTER_SPIKE','COL_ALL','COL_DEATH_TOP',
      'COL_DEATH_BOTTOM','COL_TOP','COL_DEATH','COL_DEATH','COL_DEATH','COL_DEATH_LEFT','COL_DEATH_TOP','COL_DEATH_RIGHT',
      'COL_ALL','COL_ALL','COL_ALL','COL_ALL','COL_ALL','COL_ALL','COL_ALL','COL_ALL',
      'COL_ALL','COL_ALL','COL_ALL','COL_ALL','COL_ALL','COL_ALL','COL_ALL','COL_NONE',
      'COL_ALL','COL_ALL','COL_ALL','COL_ALL','COL_NONE','COL_NONE','COL_NONE','COL_TOP_CENTER_SPIKE']

def gc(tid):
    if tid < 0 or tid >= len(ct): return 'COL_NONE'
    return ct[tid]

print('=== VISUAL MAP: cols 330-375, TMX rows 15-26 ===')
print('  # = SOLID   X = DEATH(no solid)   . = EMPTY   ~ = COL_NONE tile')
print('  C = COIN  O = ORB  P = PAD  G = GAME_MODE_PORTAL  s = other sprite')
print()

for tmx_r in range(15, 27):
    game_y = (tmx_r - GR) * 16
    line = f'gY={game_y:>3} r{tmx_r:>2}: '
    for c in range(330, 376):
        idx = tmx_r * W + c
        tv = tvals[idx]
        tid = tv-1 if tv>0 else -1
        sv = svals[idx]
        sid = sv-257 if sv>=257 else -1
        
        if sid == 0x1A or sid == 0x1B or sid == 0x07:
            line += 'C'
        elif sid == 0x0B:
            line += 'O'
        elif sid == 0x0A:
            line += 'P'
        elif sid == 0x01 or sid == 0x00:
            line += 'G'
        elif sid >= 0 and sid not in (0x2E, 0x2F, 0x2D, 0x3D, 0x2C):
            line += 's'
        elif tid < 0:
            line += '.'
        elif gc(tid) in ('COL_ALL','COL_FLOOR_CEIL','COL_TOP','COL_BOTTOM','COL_TOP_CENTER_SPIKE'):
            line += '#'
        elif 'DEATH' in gc(tid):
            line += 'X'
        elif gc(tid) == 'COL_NONE':
            line += '~'
        else:
            line += '?'
    print(line)

print()
col_markers = '            '
for c in range(330, 376):
    if c % 10 == 0:
        col_markers += str(c // 10 % 10)
    else:
        col_markers += ' '
print(f'  Tens:  {col_markers}')
col_markers2 = '            '
for c in range(330, 376):
    col_markers2 += str(c % 10)
print(f'  Ones:  {col_markers2}')
print(f'  Col:   330       340       350       360       370  375')

# Now show the same with just the coin area highlighted
print()
print('=== ZOOMED: cols 360-375, TMX rows 17-25 ===')
for tmx_r in range(17, 26):
    game_y = (tmx_r - GR) * 16
    line = f'gY={game_y:>3} r{tmx_r:>2}: '
    for c in range(360, 376):
        idx = tmx_r * W + c
        tv = tvals[idx]
        tid = tv-1 if tv>0 else -1
        sv = svals[idx]
        sid = sv-257 if sv>=257 else -1
        
        if sid == 0x1A or sid == 0x1B or sid == 0x07:
            line += 'C '
        elif sid >= 0 and sid not in (0x2E, 0x2F, 0x2D, 0x3D, 0x2C):
            line += 's '
        elif tid < 0:
            line += '. '
        elif gc(tid) in ('COL_ALL','COL_FLOOR_CEIL','COL_TOP','COL_BOTTOM','COL_TOP_CENTER_SPIKE'):
            line += '# '
        elif 'DEATH' in gc(tid):
            line += 'X '
        elif gc(tid) == 'COL_NONE':
            line += '~ '
        else:
            line += '? '
    
    label = ''
    if tmx_r == 17: label = ' <-- Floor (death tiles, NOT solid)'
    if tmx_r == 19: label = ' <-- Ship at Y=267'
    if tmx_r == 20: label = ' <-- Box top (death, NOT solid)'
    if tmx_r == 21: label = ' <-- Box wall (SOLID COL_ALL)'
    if tmx_r == 22: label = ' <-- COIN ROW (COL_NONE, passable)'
    if tmx_r == 23: label = ' <-- Box wall (SOLID COL_ALL)'
    if tmx_r == 24: label = ' <-- Box bottom (death)'
    print(line + label)

print(f'         {"".join(f"{c%10} " for c in range(360, 376))}')
print(f'  Cols:  360       365       370       375')
