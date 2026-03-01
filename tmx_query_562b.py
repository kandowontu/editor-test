with open(r'c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\polargeist.tmx', 'r') as f:
    lines = f.readlines()

data_start = None
for i, line in enumerate(lines):
    if 'encoding="csv"' in line:
        data_start = i + 1
        break

with open(r'c:\Editor Test\metatile_collision_table.txt', 'r') as f:
    col_table = [l.strip() for l in f.readlines()]

all_rows = []
for row_idx in range(27):
    line = lines[data_start + row_idx].strip().rstrip(',')
    vals = line.split(',')
    all_rows.append([int(v) for v in vals])

print('=== DETAILED GRID: COLS 549-565, TMX ROWS 24-26 ===')
print('          ', end='')
for col in range(549, 566):
    print(f'{col:5}', end='')
print()
print('     X:   ', end='')
for col in range(549, 566):
    print(f'{col*16:5}', end='')
print()
for tmx_row in range(24, 27):
    wy = (tmx_row-3)*16
    print(f'r{tmx_row} Y={wy:3}: ', end='')
    for col in range(549, 566):
        tile = all_rows[tmx_row][col]
        if tile == 0:
            print('    .', end='')
        else:
            print(f'{tile:5}', end='')
    print()

# Check SP layer
print()
print('=== SP LAYER: COLS 540-570, TMX ROWS 22-26 ===')
sp_start = None
for i, line in enumerate(lines):
    if 'name="SP"' in line:
        for j in range(i, min(i+5, len(lines))):
            if 'encoding="csv"' in lines[j]:
                sp_start = j + 1
                break
        break
if sp_start:
    sp_rows = []
    for row_idx in range(27):
        ln = lines[sp_start + row_idx].strip().rstrip(',')
        vals = ln.split(',')
        sp_rows.append([int(v) for v in vals])
    for tmx_row in range(22, 27):
        wy = (tmx_row-3)*16
        nz = []
        for col in range(540, 571):
            tile = sp_rows[tmx_row][col]
            if tile != 0:
                nz.append(f'c{col}=sp{tile}')
        if nz:
            print(f'r{tmx_row} Y={wy}: {", ".join(nz)}')

# Staircase cols 540-550
print()
print('=== STAIRCASE COLS 540-550, TMX ROWS 22-26 ===')
for tmx_row in range(22, 27):
    wy = (tmx_row-3)*16
    tiles = []
    for col in range(540, 551):
        t = all_rows[tmx_row][col]
        ct = col_table[t] if t < len(col_table) else '???'
        if t:
            tiles.append(f'c{col}=t{t}({ct})')
    if tiles:
        print(f'r{tmx_row} Y={wy}: {", ".join(tiles)}')
    else:
        print(f'r{tmx_row} Y={wy}: (empty)')

# Check for any tiles at all between cols 550-559, all rows
print()
print('=== ANY TILES IN COLS 550-559 (any row)? ===')
for col in range(550, 560):
    for tmx_row in range(27):
        t = all_rows[tmx_row][col]
        if t != 0:
            wy = (tmx_row-3)*16
            ct = col_table[t] if t < len(col_table) else '???'
            print(f'  col={col} X={col*16}, TMX row={tmx_row} Y={wy}: tile={t} ({ct})')
