import xml.etree.ElementTree as ET

tree = ET.parse('famidash/LEVELS/LEVEL DATA/lvlset_HUGE/eighto.tmx')
root = tree.getroot()
layer = root.find('.//layer')
data = layer.find('data').text.strip()
raw_gids = [int(t.strip()) for t in data.split(',') if t.strip()]
width = int(layer.get('width'))
height = int(layer.get('height'))

# Convert TMX GIDs to editor tile IDs (subtract firstgid=1)
# GID 0 = empty -> tile -1, GID 1 = tile 0, GID 2 = tile 1, etc.
tiles = [(g - 1) if g > 0 else -1 for g in raw_gids]

# Collision table from MetatileCollision.cs
mapping_text = """COL_NONE
COL_FLOOR_CEIL
COL_FLOOR_CEIL
COL_BOTTOM
COL_DEATH_TOP
COL_FLOOR_CEIL
COL_FLOOR_CEIL
COL_NONE
COL_DEATH_BOTTOM
COL_DEATH_BOTTOM
COL_DEATH_TOP
COL_DEATH_TOP
COL_DEATH_BOTTOM
COL_DEATH_TOP
COL_DEATH_LEFT
COL_DEATH_RIGHT
COL_ALL
COL_DEATH
COL_DEATH_BOTTOM
COL_DEATH_BOTTOM
COL_DEATH_TOP
COL_TOP_CENTER_SPIKE
COL_ALL
COL_DEATH_TOP
COL_DEATH_BOTTOM
COL_TOP
COL_DEATH
COL_DEATH
COL_DEATH
COL_DEATH_LEFT
COL_DEATH_TOP
COL_DEATH_RIGHT
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_NONE"""

col_names = [l.strip().split(';')[0].strip() for l in mapping_text.strip().split('\n')]
def get_collision(tid):
    if tid < 0 or tid >= len(col_names):
        return f'?{tid}'
    return col_names[tid]

SHORT = {
    'COL_NONE': '----', 'COL_FLOOR_CEIL': ' FC ', 'COL_ALL': ' ALL',
    'COL_BOTTOM': ' BOT', 'COL_TOP': ' TOP', 'COL_DEATH': ' DTH',
    'COL_DEATH_TOP': ' D_T', 'COL_DEATH_BOTTOM': ' D_B',
    'COL_DEATH_LEFT': ' D_L', 'COL_DEATH_RIGHT': ' D_R',
    'COL_TOP_CENTER_SPIKE': ' TCS',
}

print(f'Map: {width}x{height}, groundRows=3')

# Full columns at failure X
for col_x in [382, 383, 384]:
    print(f'\n=== Column tileX={col_x} (pixelX={col_x*16}) ===')
    for arr_row in range(30, 42):
        game_row = arr_row - 3
        pixel_y = game_row * 16
        idx = arr_row * width + col_x
        tid = tiles[idx] if idx < len(tiles) else -1
        coll = get_collision(tid)
        short = SHORT.get(coll, coll[:4])
        print(f'  arr{arr_row:2d} gt{game_row:2d} Y={pixel_y:4d}: tid=0x{tid:02X} -> {coll:20s} ({short})')

# Wide row views
for arr_row in [33, 34, 35, 36, 37, 38]:
    game_row = arr_row - 3
    pixel_y = game_row * 16
    print(f'\nRow arr{arr_row}/gt{game_row}/Y={pixel_y} cols 378-392:')
    parts = []
    for col_x in range(378, 393):
        idx = arr_row * width + col_x
        tid = tiles[idx] if idx < len(tiles) else -1
        coll = get_collision(tid)
        short = SHORT.get(coll, coll[:4])
        parts.append(f'{col_x}:{short}')
    print('  ' + '  '.join(parts))
