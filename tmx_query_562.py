import csv, io, sys

# Read the TMX file
with open(r'c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\polargeist.tmx', 'r') as f:
    lines = f.readlines()

# Find the CSV data start (after <data encoding="csv">)
data_start = None
for i, line in enumerate(lines):
    if 'encoding="csv"' in line:
        data_start = i + 1
        break

print(f'Data starts at line {data_start+1}')
print(f'Map is 935x27')

# Read collision table
with open(r'c:\Editor Test\metatile_collision_table.txt', 'r') as f:
    col_table = [l.strip() for l in f.readlines()]

# Parse all 27 rows
all_rows = []
for row_idx in range(27):
    line = lines[data_start + row_idx].strip().rstrip(',')
    vals = line.split(',')
    all_rows.append([int(v) for v in vals])

print(f'Parsed {len(all_rows)} rows, first row has {len(all_rows[0])} cols')
print()

# groundRowsToReserve = 3, so world Y = (TMX_row - 3) * 16
# Player at X=8991, Y=353 -> col = 8991/16 = 562 (fractional 561.9375)
# Player bottom at Y=368 -> world row 23, TMX row 26

print('=== TILE LAYOUT FOR COLS 540-575, TMX ROWS 18-26 ===')
print('(world Y = (TMX_row - 3) * 16)')
print()

for tmx_row in range(18, 27):
    world_row = tmx_row - 3
    world_y_top = world_row * 16
    world_y_bot = world_y_top + 15
    non_zero = []
    for col in range(540, 576):
        tile = all_rows[tmx_row][col]
        if tile != 0:
            col_type = col_table[tile] if tile < len(col_table) else '???'
            non_zero.append(f'  col={col} (X={col*16}-{col*16+15}) tile={tile} ({col_type})')
    if non_zero:
        print(f'TMX row {tmx_row} (world row {world_row}, Y={world_y_top}-{world_y_bot}):')
        for s in non_zero:
            print(s)
    else:
        print(f'TMX row {tmx_row} (world row {world_row}, Y={world_y_top}-{world_y_bot}): all empty')
    print()

# Focus on col 562 specifically
print('=== COLUMN 562 (X=8992-9007) ALL ROWS ===')
for tmx_row in range(27):
    tile = all_rows[tmx_row][562]
    if tile != 0:
        world_row = tmx_row - 3
        world_y_top = world_row * 16
        col_type = col_table[tile] if tile < len(col_table) else '???'
        print(f'  TMX row {tmx_row} (world Y={world_y_top}-{world_y_top+15}): tile={tile} ({col_type})')

# Also check col 561
print()
print('=== COLUMN 561 (X=8976-8991) ALL ROWS ===')
for tmx_row in range(27):
    tile = all_rows[tmx_row][561]
    if tile != 0:
        world_row = tmx_row - 3
        world_y_top = world_row * 16
        col_type = col_table[tile] if tile < len(col_table) else '???'
        print(f'  TMX row {tmx_row} (world Y={world_y_top}-{world_y_top+15}): tile={tile} ({col_type})')

# Check cols 558-565 at TMX rows 22-26 for elevated platforms
print()
print('=== DETAILED GRID: COLS 555-570, TMX ROWS 20-26 ===')
print('     ', end='')
for col in range(555, 571):
    print(f'{col:4}', end='')
print()
for tmx_row in range(20, 27):
    world_row = tmx_row - 3
    world_y = world_row * 16
    print(f'r{tmx_row:2} Y={world_y:3}: ', end='')
    for col in range(555, 571):
        tile = all_rows[tmx_row][col]
        if tile == 0:
            print('   .', end='')
        else:
            print(f'{tile:4}', end='')
    print()

# Game structure overview
print()
print('=== GAME STRUCTURE OVERVIEW: COLS 540-575, TMX ROWS 15-26 ===')
for tmx_row in range(15, 27):
    world_row = tmx_row - 3
    world_y = world_row * 16
    tiles_in_row = []
    for col in range(540, 576):
        tile = all_rows[tmx_row][col]
        if tile != 0:
            tiles_in_row.append((col, tile))
    if tiles_in_row:
        col_type_strs = []
        for col, tile in tiles_in_row:
            ct = col_table[tile] if tile < len(col_table) else '???'
            col_type_strs.append(f'c{col}=t{tile}({ct})')
        print(f'TMXr{tmx_row} Y={world_y}: {", ".join(col_type_strs)}')

# What is at player bottom Y=368 = world row 23 = TMX row 26?
print()
print('=== PLAYER BOTTOM (Y=368, world row 23, TMX row 26), cols 555-570 ===')
for col in range(555, 571):
    tile = all_rows[26][col]
    ct = col_table[tile] if tile < len(col_table) else '???'
    print(f'  col={col} X={col*16}-{col*16+15}: tile={tile} ({ct})')
    
# TMX row 25 (world row 22, Y=352-367)
print()
print('=== PLAYER POSITION ROW (Y=352-367, world row 22, TMX row 25), cols 555-570 ===')
for col in range(555, 571):
    tile = all_rows[25][col]
    ct = col_table[tile] if tile < len(col_table) else '???'
    print(f'  col={col} X={col*16}-{col*16+15}: tile={tile} ({ct})')

# Player column calc
print()
player_col = 8991 // 16
print(f'Player X=8991 -> col={player_col} (X={player_col*16}-{player_col*16+15})')
print(f'Player right edge=8991+15=9006 -> col={9006//16} (X={9006//16*16}-{9006//16*16+15})')
print(f'Player Y=353 -> world row={353//16} (Y={353//16*16}-{353//16*16+15}), TMX row={353//16+3}')
print(f'Player bottom=368 -> world row={368//16} (Y={368//16*16}-{368//16*16+15}), TMX row={368//16+3}')
