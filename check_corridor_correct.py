import xml.etree.ElementTree as ET

tree = ET.parse('famidash/LEVELS/LEVEL DATA/lvlset_HUGE/eighto.tmx')
root = tree.getroot()
layer = root.find('.//layer')
data = layer.find('data').text.strip()
tiles = [int(t.strip()) for t in data.split(',') if t.strip()]
width = int(layer.get('width'))
height = int(layer.get('height'))
print(f'Map: {width}x{height}, groundRows=3')

# Build the EXACT collision table from MetatileCollision.cs
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
COL_NONE
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_NONE
COL_NONE
COL_NONE
COL_TOP_CENTER_SPIKE
COL_ALL
COL_ALL
COL_ALL
COL_DEATH_BOTTOM
COL_ALL
COL_ALL
COL_ALL
COL_ALL"""

lines = [l.strip() for l in mapping_text.strip().split('\n') if l.strip()]
col_table = {}
for i, name in enumerate(lines):
    # Strip ;$80 etc
    name = name.split(';')[0].strip()
    col_table[i] = name

# Short names for display
SHORT = {
    'COL_NONE': '----',
    'COL_FLOOR_CEIL': 'FC',
    'COL_ALL': 'ALL',
    'COL_BOTTOM': 'BOT',
    'COL_TOP': 'TOP',
    'COL_DEATH': 'DTH',
    'COL_DEATH_TOP': 'DT',
    'COL_DEATH_BOTTOM': 'DB',
    'COL_DEATH_LEFT': 'DL',
    'COL_DEATH_RIGHT': 'DR',
    'COL_TOP_CENTER_SPIKE': 'TCS',
}

# Column range centered on the failure point (col 383)
# and row range covering the corridor
for col_x in [382, 383, 384]:
    print(f'\n=== Full column at tileX={col_x} (pixelX={col_x*16}) ===')
    for arr_row in range(30, 42):
        game_row = arr_row - 3
        pixel_y = game_row * 16
        idx = arr_row * width + col_x
        tid = tiles[idx] if idx < len(tiles) else 0
        coll = col_table.get(tid, f'?{tid}')
        short = SHORT.get(coll, coll)
        print(f'  arr{arr_row:2d} gt{game_row:2d} Y={pixel_y:4d}: tid=0x{tid:02X} -> {coll:20s} ({short})')

# Also dump a wider row view at the key rows
for arr_row in [34, 35, 36, 37]:
    game_row = arr_row - 3
    pixel_y = game_row * 16
    print(f'\n=== Row arr{arr_row}/gt{game_row}/Y={pixel_y} cols 378-392 ===')
    parts = []
    for col_x in range(378, 393):
        idx = arr_row * width + col_x
        tid = tiles[idx] if idx < len(tiles) else 0
        coll = col_table.get(tid, f'?{tid}')
        short = SHORT.get(coll, coll)
        parts.append(f'{col_x}:{short}')
    print('  ' + '  '.join(parts))
