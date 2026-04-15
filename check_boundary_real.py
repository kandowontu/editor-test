"""Check corridor boundary at Y=192 (row 12) across full wave section."""
import xml.etree.ElementTree as ET
import re

TMX = r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\kratos.tmx"

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

def get_col(gid):
    if gid == 0: return 'EMPTY'
    if gid - 1 < len(col_entries): return col_entries[gid - 1]
    return f'UNK({gid})'

# Corridor boundary is at row 12 (Y_down=192)
# Also check rows 11,13 for additional barriers
# Wave section: X=7000-11500 → cols 437-718
# Coin at X=9056 → col 566

for row in [11, 12, 13]:
    y_pixel = row * 16
    print(f"\n=== Row {row} (Y_down={y_pixel}) ===")
    
    passable_cols = []
    blocking_cols = []
    
    for col in range(437, min(width, 750)):
        idx = row * width + col
        if idx >= len(tiles_raw):
            continue
        gid = tiles_raw[idx]
        ct = get_col(gid)
        x_pixel = col * 16
        
        if ct == 'COL_NONE' or ct == 'EMPTY':
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
    
    if blocking_cols:
        # Group blocking
        ranges = []
        start = blocking_cols[0]
        prev_col = start[0]
        for p in blocking_cols[1:]:
            if p[0] == prev_col + 1:
                prev_col = p[0]
            else:
                ranges.append((start[0], prev_col, start[1], prev_col * 16, start[3]))
                start = p
                prev_col = p[0]
        ranges.append((start[0], prev_col, start[1], prev_col * 16, start[3]))
        
        print(f"  Blocking ranges:")
        for sc, ec, sx, ex, ct in ranges:
            marker = " <-- POST-COIN" if sx >= 9056 else ""
            print(f"    cols {sc}-{ec} (X={sx}-{ex+16}) type={ct}{marker}")

# Also check row 12 SPECIFICALLY near death zone (X=11500-12500)
print(f"\n=== Post-wave zone (cols 700-780, X=11200-12480) ===")
for row in [11, 12, 13, 14, 15, 16, 17, 18, 19]:
    y_pixel = row * 16
    tiles_str = ""
    for col in range(700, 780):
        idx = row * width + col
        gid = tiles_raw[idx] if idx < len(tiles_raw) else 0
        ct = get_col(gid)
        if ct == 'COL_NONE' or ct == 'EMPTY':
            tiles_str += "."
        elif 'DEATH' in ct or 'SPIKE' in ct:
            tiles_str += "X"
        elif ct == 'COL_ALL':
            tiles_str += "#"
        elif ct == 'COL_FLOOR_CEIL':
            tiles_str += "="
        elif ct == 'COL_FLOOR_ONLY':
            tiles_str += "_"
        elif ct == 'COL_CEIL_ONLY':
            tiles_str += "^"
        else:
            tiles_str += "?"
    print(f"  row {row:2d} Y_d={y_pixel:3d}: {tiles_str}")
    print(f"         X=:  {''.join(str((700+i)%10) for i in range(80))}")
    break  # just show X ticks once
for row in [11, 12, 13, 14, 15, 16, 17, 18, 19]:
    y_pixel = row * 16
    tiles_str = ""
    for col in range(700, 780):
        idx = row * width + col
        gid = tiles_raw[idx] if idx < len(tiles_raw) else 0
        ct = get_col(gid)
        if ct == 'COL_NONE' or ct == 'EMPTY':
            tiles_str += "."
        elif 'DEATH' in ct or 'SPIKE' in ct:
            tiles_str += "X"
        elif ct == 'COL_ALL':
            tiles_str += "#"
        elif ct == 'COL_FLOOR_CEIL':
            tiles_str += "="
        elif ct == 'COL_FLOOR_ONLY':
            tiles_str += "_"
        elif ct == 'COL_CEIL_ONLY':
            tiles_str += "^"
        else:
            tiles_str += "?"
    print(f"  row {row:2d} Y_d={y_pixel:3d}: {tiles_str}")
