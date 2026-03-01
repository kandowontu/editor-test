import re

# Read the TMX file and parse the tile layer
with open(r'c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\polargeist.tmx', 'r') as f:
    content = f.read()

# Find all layer data blocks
pattern = r'<layer[^>]*>.*?<data encoding="csv">\s*(.*?)\s*</data>'
layers = re.findall(pattern, content, re.DOTALL)
tile_data = layers[0]  # First layer = tiles
sprite_data = layers[1] if len(layers) > 1 else None  # SP layer

rows = [line.strip() for line in tile_data.strip().split('\n') if line.strip()]
map_height = len(rows)

# Parse all rows
tile_grid = []
for r_idx, row in enumerate(rows):
    vals = [int(v.strip()) for v in row.rstrip(',').split(',')]
    tile_grid.append(vals)

map_width = len(tile_grid[0])
groundRowsToReserve = 3
TILE = 16

print(f'=== COORDINATE SYSTEM ===')
print(f'TMX: {map_width}x{map_height}')
print(f'groundRowsToReserve = {groundRowsToReserve}')
worldBottom = (map_height - groundRowsToReserve) * TILE
print(f'worldBottom = ({map_height} - {groundRowsToReserve}) * {TILE} = {worldBottom}')
print(f'Player ground Y (top of 16px cube) = {worldBottom - 16}')
print()
print('TMX row -> World pixel Y mapping:')
for tmx_row in range(map_height):
    world_y = (tmx_row - groundRowsToReserve) * TILE
    print(f'  TMX row {tmx_row:2d} -> world pixel Y = {world_y:4d} to {world_y+15:4d}')

# === COIN 2 ANALYSIS: X=9472, Y=351 ===
print()
print('=' * 60)
print('=== COIN 2: hitbox=(9472,351)-(9488,367) ===')
coin2_col = 9472 // 16  # = 592
coin2_y = 351
# hitTop = (storageTileY - groundRowsToReserve) * TILE + hyoff + pyOff - 1
# 351 = (tileY - 3) * 16 - 1  (assuming hyoff=0, pyOff=0)
# tileY = (352 / 16) + 3 = 22 + 3 = 25
coin2_tmx_row = (coin2_y + 1) // TILE + groundRowsToReserve
print(f'Coin 2 tile column: {coin2_col} (X={coin2_col*16})')
print(f'Coin 2 TMX row: {coin2_tmx_row} (world Y ~{(coin2_tmx_row-groundRowsToReserve)*TILE})')
print()

# Show tiles in columns 585-600, all rows
print(f'Tile data for columns 585-600 (X={585*16}-{600*16}), all rows:')
print(f'{"Row":>4} {"WorldY":>7} ', end='')
for c in range(585, 601):
    print(f'{c:>4}', end='')
print()
for r in range(map_height):
    world_y = (r - groundRowsToReserve) * TILE
    print(f'{r:4d} {world_y:7d} ', end='')
    for c in range(585, 601):
        if c < len(tile_grid[r]):
            v = tile_grid[r][c]
            if v == 0:
                print('   .', end='')
            else:
                print(f'{v:4d}', end='')
        else:
            print('   ?', end='')
    print()

# === COIN 3 ANALYSIS: X=11504, Y=175 ===
print()
print('=' * 60)
print('=== COIN 3: hitbox=(11504,175)-(11520,191) ===')
coin3_col = 11504 // 16  # = 719
coin3_y = 175
coin3_tmx_row = (coin3_y + 1) // TILE + groundRowsToReserve
print(f'Coin 3 tile column: {coin3_col} (X={coin3_col*16})')
print(f'Coin 3 TMX row: {coin3_tmx_row} (world Y ~{(coin3_tmx_row-groundRowsToReserve)*TILE})')
print()

# Show tiles in columns 712-726, all rows
print(f'Tile data for columns 712-726 (X={712*16}-{726*16}), all rows:')
print(f'{"Row":>4} {"WorldY":>7} ', end='')
for c in range(712, 727):
    print(f'{c:>4}', end='')
print()
for r in range(map_height):
    world_y = (r - groundRowsToReserve) * TILE
    print(f'{r:4d} {world_y:7d} ', end='')
    for c in range(712, 727):
        if c < len(tile_grid[r]):
            v = tile_grid[r][c]
            if v == 0:
                print('   .', end='')
            else:
                print(f'{v:4d}', end='')
        else:
            print('   ?', end='')
    print()

# === SPRITE LAYER ANALYSIS ===
print()
print('=' * 60)
print('=== SPRITE LAYER near coins ===')
if sprite_data:
    sprite_rows = [line.strip() for line in sprite_data.strip().split('\n') if line.strip()]
    sprite_grid = []
    for row in sprite_rows:
        vals = [int(v.strip()) for v in row.rstrip(',').split(',')]
        sprite_grid.append(vals)
    
    # Near coin 2: columns 585-600
    print(f'\nSprite data columns 585-600:')
    for r in range(map_height):
        for c in range(585, 601):
            if c < len(sprite_grid[r]) and sprite_grid[r][c] != 0:
                gid = sprite_grid[r][c]
                sid = gid - 257 if gid >= 257 else gid
                world_y = (r - groundRowsToReserve) * TILE
                print(f'  Row {r:2d} Col {c:3d}: GID={gid} SpriteID=0x{sid:02X} ({sid}) worldY={world_y}')
    
    # Near coin 3: columns 712-726
    print(f'\nSprite data columns 712-726:')
    for r in range(map_height):
        for c in range(712, 727):
            if c < len(sprite_grid[r]) and sprite_grid[r][c] != 0:
                gid = sprite_grid[r][c]
                sid = gid - 257 if gid >= 257 else gid
                world_y = (r - groundRowsToReserve) * TILE
                print(f'  Row {r:2d} Col {c:3d}: GID={gid} SpriteID=0x{sid:02X} ({sid}) worldY={world_y}')

    # Wider search for coins: sprite ID 0x19 = 25 (coin), GID = 282
    print(f'\nAll coins in the level (sprite ID 0x19=25, GID=282):')
    for r in range(map_height):
        for c in range(len(sprite_grid[r])):
            if sprite_grid[r][c] == 282:
                world_y = (r - groundRowsToReserve) * TILE
                world_x = c * TILE
                # hitTop = (r - 3) * 16 - 1
                hit_top = (r - groundRowsToReserve) * TILE - 1
                hit_left = c * TILE
                print(f'  Coin at TMX({c},{r}) -> world ({world_x},{world_y}) hitbox=({hit_left},{hit_top})-({hit_left+16},{hit_top+16})')

# === Wider context around coin 2 ===
print()
print('=' * 60)
print('=== TILE DATA: Columns 550-605 near coin 2 (non-zero only) ===')
for r in range(map_height):
    world_y = (r - groundRowsToReserve) * TILE
    nonzero = []
    for c in range(550, 606):
        if c < len(tile_grid[r]) and tile_grid[r][c] != 0:
            nonzero.append((c, tile_grid[r][c]))
    if nonzero:
        tiles_str = ' '.join([f'c{c}={v}' for c, v in nonzero])
        print(f'  Row {r:2d} (Y={world_y:4d}): {tiles_str}')

print()
print('=== TILE DATA: Columns 700-735 near coin 3 (non-zero only) ===')
for r in range(map_height):
    world_y = (r - groundRowsToReserve) * TILE
    nonzero = []
    for c in range(700, 736):
        if c < len(tile_grid[r]) and tile_grid[r][c] != 0:
            nonzero.append((c, tile_grid[r][c]))
    if nonzero:
        tiles_str = ' '.join([f'c{c}={v}' for c, v in nonzero])
        print(f'  Row {r:2d} (Y={world_y:4d}): {tiles_str}')
