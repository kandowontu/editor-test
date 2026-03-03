import re

f = open(r'c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\cantletgo.tmx', 'r')
content = f.read()
f.close()

# Parse layers
layers = []
layer_names = []
in_data = False
current = []
for line in content.split('\n'):
    m = re.search(r'<layer\s+.*?name="([^"]+)"', line)
    if m:
        layer_names.append(m.group(1))
    if '<data encoding=' in line:
        in_data = True
        current = []
        continue
    if '</data>' in line:
        in_data = False
        layers.append(current)
        continue
    if in_data and line.strip():
        vals = [int(x) for x in line.strip().rstrip(',').split(',')]
        current.append(vals)

print(f'Layers: {layer_names}')
print(f'Layer count: {len(layers)}')
for i, l in enumerate(layers):
    name = layer_names[i] if i < len(layer_names) else f'L{i}'
    print(f'  Layer {i} ({name}): {len(l)} rows, {len(l[0]) if l else 0} cols')

firstgid = 257

def classify(sid):
    game_modes = {0:'Cube',1:'Ship',2:'Ball',3:'UFO',4:'Wave',0x17:'Spider',0x24:'Robot',0x4B:'Swingcopter',0x58:'Ninja',0x6A:'Pogo',0x6B:'Mode10',0x6C:'Football'}
    if sid in game_modes:
        return f'GameModePortal({game_modes[sid]})'
    speed_map = {0x6D:'0.5x',0x14:'1x',0x15:'2x',0x16:'3x',0x20:'4x',0x21:'5x'}
    if sid in speed_map:
        return f'SpeedPortal({speed_map[sid]})'
    grav_normal = {0x08, 0x10, 0x11, 0xFC}
    grav_reverse = {0x09, 0x12, 0x13, 0xFB}
    if sid in grav_normal:
        return 'GravityPortal(Normal)'
    if sid in grav_reverse:
        return 'GravityPortal(Reverse)'
    if sid == 0x18: return 'MiniPortal'
    if sid == 0x19: return 'GrowthPortal'
    if sid == 0x0F: return 'EndLevel'
    if sid in (0x0A, 0x0C): return 'YellowPad'
    if sid in (0x25, 0x26): return 'PinkPad'
    if sid in (0x52, 0x53): return 'RedPad'
    if sid in (0x0D, 0x0E, 0xFD, 0xFE): return 'BluePad'
    if sid == 0x65: return 'GreenPad'
    if sid == 0x0B: return 'YellowOrb'
    if sid == 0x1F: return 'YellowOrbBigger'
    if sid == 0x29: return 'YellowOrbSmaller'
    if sid == 0x06: return 'PinkOrb'
    if sid == 0x28: return 'RedOrb'
    if sid in (0x05, 0x7B): return 'BlueOrb'
    if sid in (0x27, 0x7C): return 'GreenOrb'
    if sid == 0x44: return 'BlackOrb'
    if sid == 0x7A: return 'WhiteOrb'
    if sid == 0x07: return 'Coin'
    if sid in (0x3E, 0x3F, 0x40, 0x41, 0x42, 0x43): return 'CoinPiece'
    if sid == 0x45: return 'DashOrb'
    if sid == 0x46: return 'ReverseDashOrb'
    return f'Sprite(0x{sid:02X}={sid})'

sp_layer = layers[1] if len(layers) > 1 else None
main_layer = layers[0]

col_start = 550
col_end = 650

print()
print(f'=== SP LAYER SPRITES: cols {col_start}-{col_end} (X={col_start*16}-{col_end*16}) ===')
if sp_layer:
    for row_idx, row in enumerate(sp_layer):
        for col in range(col_start, min(col_end+1, len(row))):
            val = row[col]
            if val == 0:
                continue
            sid = val - firstgid if val >= firstgid else -1
            x_px = col * 16
            y_px = row_idx * 16
            if sid >= 0:
                typ = classify(sid)
                print(f'  col={col:4d} row={row_idx:3d} X={x_px:5d} Y={y_px:4d}  sid=0x{sid:02X}({sid:3d})  {typ}')
            else:
                print(f'  col={col:4d} row={row_idx:3d} X={x_px:5d} Y={y_px:4d}  tileId={val}  (terrain on SP layer?)')

print()
print(f'=== MAIN LAYER: gaps/openings around X=9600-9700 (cols 600-606) ===')
print(f'    Showing rows 6-20 (Y=96-320), marking empty vs filled')
for col in range(600, 607):
    x_px = col * 16
    tiles_str = ''
    for row_idx in range(6, 21):
        val = main_layer[row_idx][col] if row_idx < len(main_layer) and col < len(main_layer[row_idx]) else 0
        if val == 0:
            tiles_str += '.'
        else:
            tiles_str += '#'
    print(f'  col={col} X={x_px}: rows 6-20: [{tiles_str}]  (solid=#, empty=.)')

print()
print('  Row mapping: row6=Y96, row7=Y112, row8=Y128, row9=Y144, row10=Y160, row11=Y176, row12=Y192, row13=Y208, row14=Y224, row15=Y240, row16=Y256, row17=Y272, row18=Y288, row19=Y304, row20=Y320')

# Also wider view for wall gaps
print()
print(f'=== MAIN LAYER DETAIL: cols 595-615 (X=9520-9840), rows 6-20 ===')
print('     ', end='')
for col in range(595, 616):
    print(f'{col%10}', end='')
print(f'  <- col%10')
for row_idx in range(6, 21):
    y_px = row_idx * 16
    line = f'  r{row_idx:02d} Y={y_px:3d} '
    for col in range(595, 616):
        val = main_layer[row_idx][col] if row_idx < len(main_layer) and col < len(main_layer[row_idx]) else 0
        if val == 0:
            line += '.'
        else:
            line += '#'
    print(line)
print(f'     cols: ', end='')
for col in range(595, 616):
    print(f'{col%10}', end='')
print()
print(f'     cols 595-615 = X {595*16}-{615*16}')
