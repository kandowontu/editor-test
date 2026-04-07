import xml.etree.ElementTree as ET

tree = ET.parse('famidash/LEVELS/LEVEL DATA/lvlset_HUGE/eighto.tmx')
root = tree.getroot()
layer = root.find('.//layer')
data = layer.find('data').text.strip()
tiles = [int(t.strip()) for t in data.split(',') if t.strip()]
width = int(layer.get('width'))
height = int(layer.get('height'))
print(f'Map: {width}x{height}')

# Collision table (simplified, matching MetatileCollision names)
COL_NAMES = {
    0x00: 'NONE', 0x01: 'NONE', 0x0D: 'DEATH2', 
    0x11: 'DEATH', 0x12: 'DEATH_BOT', 0x1C: 'DEATH',
    0x22: 'ALL', 0x24: 'UL', 0x2C: 'DL', 0x2D: 'DR',
    0x2F: 'NONE', 0x30: 'ALL',
}

# The UFO fails at X~6126px, tile col ~383
# The gravity portal is at col 385
# Check array rows 32-37 (game tiles 29-34) at columns 370-395
print('\nTerrain at corridor section (cols 370-395):')
for row in range(32, 38):
    parts = []
    for col in range(370, 396):
        idx = row * width + col
        tid = tiles[idx] if idx < len(tiles) else 0
        name = COL_NAMES.get(tid, f'?{tid:#04x}')
        parts.append(f'{name:>8s}')
    game_tile_y = row - 3  # subtract ground rows
    pixel_y = game_tile_y * 16
    print(f'  arr{row}/gt{game_tile_y}/Y{pixel_y}: {" ".join(parts[:13])}')
    print(f'  {"":>24s} {" ".join(parts[13:])}')
