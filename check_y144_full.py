"""Check Y=144 corridor boundary passability across the ENTIRE wave section."""
import re
import xml.etree.ElementTree as ET

TMX = r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\kratos.tmx"

# Load collision table from source
with open(r'c:\Editor Test\native-windows\MetatileCollision.cs', 'r') as f:
    content = f.read()
m = re.search(r'mappingText\s*=\s*@"(.*?)";', content, re.DOTALL)
text = m.group(1)
lines = [l.strip() for l in text.split('\n') if l.strip()]
rx = re.compile(r'COL_[A-Z0-9_]+')
col_entries = []
for l in lines:
    m2 = rx.search(l)
    if m2:
        col_entries.append(m2.group(0))

tree = ET.parse(TMX)
root = tree.getroot()
width = int(root.attrib['width'])
height = int(root.attrib['height'])

layer = root.find('.//layer')
data = layer.find('data').text.strip()
tiles_raw = [int(x.strip()) for x in data.replace('\n', ',').split(',') if x.strip()]

print(f"Map: {width}x{height}, tiles: {len(tiles_raw)}")

BLOCKING_TYPES = {'COL_ALL', 'COL_FLOOR_CEIL', 'COL_FLOOR_ONLY', 'COL_CEIL_ONLY'}
DEATH_TYPES = {'COL_DEATH_BOTTOM', 'COL_DEATH_TOP', 'COL_DEATH_LEFT', 'COL_DEATH_RIGHT',
               'COL_DOWN_LEFT_SPIKE', 'COL_DOWN_RIGHT_SPIKE', 'COL_UP_LEFT_SPIKE', 'COL_UP_RIGHT_SPIKE'}

def get_col(gid):
    if gid == 0: return 'COL_NONE'
    if gid - 1 < len(col_entries): return col_entries[gid - 1]
    return f'UNK({gid})'

# Check Y=144 (row 9, since Y=row*16) -- wait, let me compute properly.
# Y=0 is top, row 0. Y=144 means row = 144/16 = 9.
# But actually in NES GD, Y might be measured differently.
# Row index = Y_pixel / tile_height (16)
# Y=144 -> row = 9
# Y=128 -> row = 8
# Y=160 -> row = 10

# Wave section roughly X=7000 to X=11500, col=7000/16=437 to 11500/16=718
# Coin at X=9056, col=9056/16=566

# Check MULTIPLE Y rows around the corridor boundary
for y_pixel in [128, 144, 160]:
    row = y_pixel // 16
    print(f"\n=== Y={y_pixel} (row {row}) ===")
    
    # Check cols from 437 (X=6992) to 843 (full width)
    passable_cols = []
    blocking_cols = []
    
    for col in range(437, min(width, 843)):
        idx = row * width + col
        if idx >= len(tiles_raw):
            continue
        gid = tiles_raw[idx]
        ct = get_col(gid)
        x_pixel = col * 16
        
        if ct == 'COL_NONE':
            passable_cols.append((col, x_pixel, gid))
        else:
            blocking_cols.append((col, x_pixel, gid, ct))
    
    print(f"  Passable: {len(passable_cols)} cols, Blocking: {len(blocking_cols)} cols")
    
    if passable_cols:
        ranges = []
        start = passable_cols[0]
        prev_col = start[0]
        for p in passable_cols[1:]:
            if p[0] == prev_col + 1:
                prev_col = p[0]
            else:
                ranges.append((start[0], prev_col, start[1], prev_col * 16))
                start = p
                prev_col = p[0]
        ranges.append((start[0], prev_col, start[1], prev_col * 16))
        
        print(f"  Passable ranges:")
        for sc, ec, sx, ex in ranges:
            marker = " <-- POST-COIN" if sx >= 9056 else (" <-- NEAR-COIN" if sx >= 8800 else "")
            print(f"    cols {sc}-{ec} (X={sx}-{ex+16}){marker}")
    else:
        print(f"  NO passable gaps!")
    
    coin_col = 9056 // 16
    print(f"\n  Around coin X=9056 (col {coin_col}):")
    for col in range(coin_col - 5, coin_col + 10):
        idx = row * width + col
        if idx >= len(tiles_raw):
            continue
        gid = tiles_raw[idx]
        ct = get_col(gid)
        print(f"    col {col} X={col*16}: GID={gid} -> {ct}")
