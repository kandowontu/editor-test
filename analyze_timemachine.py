import re

with open(r'c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\timemachine.tmx', 'r') as f:
    content = f.read()

# Find all CSV data blocks
data_blocks = re.findall(r'<data encoding="csv">\s*([\s\S]*?)\s*</data>', content)
print(f'Found {len(data_blocks)} data blocks')

W = 997
H = 27

for i, block in enumerate(data_blocks):
    vals = [int(v.strip()) for v in block.replace('\n', ',').split(',') if v.strip()]
    print(f'Block {i}: {len(vals)} values (expected {W*H})')

# Columns 365-375, Rows 14-25
cols = range(363, 378)
rows = range(12, 27)

for bi, block in enumerate(data_blocks):
    vals = [int(v.strip()) for v in block.replace('\n', ',').split(',') if v.strip()]
    layer_name = 'TILES' if bi == 0 else 'SPRITES' if bi == 1 else f'LAYER{bi}'
    print(f'\n=== {layer_name} (block {bi}) ===')
    # Header
    hdr = 'R\\C '
    for c in cols:
        hdr += f'{c:>5}'
    print(hdr)
    for r in rows:
        line = f'{r:>3} '
        for c in cols:
            idx = r * W + c
            if idx < len(vals):
                v = vals[idx]
                if bi == 0:  # tile layer
                    tid = v - 1 if v > 0 else -1  # -1 = empty, 0+ = tile index
                    if tid < 0:
                        line += '    .'
                    else:
                        line += f'  {tid:3d}'
                else:  # sprite layer  
                    if v == 0:
                        line += '    .'
                    elif v >= 257:
                        sid = v - 257
                        line += f'  x{sid:02X}'
                    else:
                        line += f'  t{v:02X}'
            else:
                line += '   ??'
        print(line)

# Also show what sprite IDs mean
print("\n=== SPRITE LEGEND ===")
print("Common sprite IDs:")
print("x00-x03 = yellow pads (various)")
print("x04-x07 = orbs")
print("x08-x09 = gravity portals")
print("x0A = coin")
print("x0B-x0E = size/mode portals")
print("x10-x13 = ship/ball/ufo/wave portals")
print("x14-x16 = cube/robot/spider portals")
print("x44-x46 = blue/magenta pads")
print("x47-x48 = blue/green orbs")
print("x4B = spike block")
print("x50-x57 = various triggers/portals")

# Specifically check for the coin
print("\n=== SEARCHING FOR COINS (sprite 0x0A) in cols 360-380 ===")
for bi, block in enumerate(data_blocks):
    if bi != 1:
        continue
    vals = [int(v.strip()) for v in block.replace('\n', ',').split(',') if v.strip()]
    for r in range(H):
        for c in range(360, 381):
            idx = r * W + c
            if idx < len(vals):
                v = vals[idx]
                if v >= 257:
                    sid = v - 257
                    if sid == 0x0A:  # coin
                        print(f"  COIN at col={c}, row={r}, pixel=({c*16},{r*16})-({c*16+16},{r*16+16})")

# Search wider for coins
print("\n=== ALL COINS in the level (sprite 0x0A) ===")
for bi, block in enumerate(data_blocks):
    if bi != 1:
        continue
    vals = [int(v.strip()) for v in block.replace('\n', ',').split(',') if v.strip()]
    for r in range(H):
        for c in range(W):
            idx = r * W + c
            if idx < len(vals):
                v = vals[idx]
                if v >= 257:
                    sid = v - 257
                    if sid == 0x0A:
                        print(f"  COIN at col={c}, row={r}, pixel=({c*16},{r*16})-({c*16+16},{r*16+16})")
