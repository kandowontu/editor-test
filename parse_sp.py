import re

with open(r'c:\Editor Test\stereomadness.tmx', 'r') as f:
    content = f.read()

# Find the SP layer data
sp_match = re.search(r'<layer id="2" name="SP".*?<data encoding="csv">\s*(.*?)\s*</data>', content, re.DOTALL)
if not sp_match:
    print("SP layer not found")
    exit()

sp_data = sp_match.group(1)
values = [int(x.strip()) for x in sp_data.split(',') if x.strip()]
width = 895
print(f'Total SP values: {len(values)}')
print(f'Expected: {width * 27} = {width*27}')

# Sprite tileset firstgid = 257
# Ship portal: sprite 9 -> GID 266
# Cube portal: sprite 10 -> GID 267
# Ball portal: sprite 11 -> GID 268
# UFO portal: sprite 12 -> GID 269

portal_names = {266: 'Ship Portal', 267: 'Cube Portal', 268: 'Ball Portal', 269: 'UFO Portal'}

print('\nAll non-zero SP tiles:')
for i, v in enumerate(values):
    if v != 0:
        col = i % width
        row = i // width
        label = ''
        if v in portal_names:
            label = f' *** {portal_names[v]} ***'
        # Subtract 257 to get sprite ID
        sprite_id = v - 257 if v >= 257 else v
        print(f'  X={col}, Y={row}, GID={v}, SpriteID=0x{sprite_id:02X} ({sprite_id}){label}')

# Also check the main tile layer for portal-like values
tile_match = re.search(r'<layer id="1".*?<data encoding="csv">\s*(.*?)\s*</data>', content, re.DOTALL)
if tile_match:
    tile_data = tile_match.group(1)
    tile_values = [int(x.strip()) for x in tile_data.split(',') if x.strip()]
    print(f'\nMain layer total tiles: {len(tile_values)}')
    unique = sorted(set(tile_values))
    print(f'Unique tile IDs in main layer: {unique}')
