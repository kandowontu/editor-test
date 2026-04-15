import xml.etree.ElementTree as ET
import re

tree = ET.parse(r'c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\kratos.tmx')
root = tree.getroot()
layer = root.find('.//layer')
data = layer.find('data').text.strip()
rows = data.split('\n')

# Load collision table - use same logic as C#: tiles[i] = gid - firstgid (firstgid=1)
# So metatile_id = gid - 1, and collision = table[metatile_id]
with open(r'c:\Editor Test\native-windows\MetatileCollision.cs', 'r') as f:
    content = f.read()
m = re.search(r'mappingText\s*=\s*@"(.*?)";', content, re.DOTALL)
text = m.group(1)
lines = [l.strip() for l in text.split('\n') if l.strip()]
rx = re.compile(r'COL_[A-Z0-9_]+')
col_table = ['COL_NONE'] * 256  # default
idx = 0
for l in lines:
    if idx >= 256: break
    m2 = rx.search(l)
    if m2:
        col_table[idx] = m2.group(0)
    idx += 1

def get_col(gid):
    if gid == 0: return 'EMPTY'
    metatile = gid - 1  # C# does tiles[i] = gid - firstgid where firstgid=1
    if 0 <= metatile < 256:
        return col_table[metatile]
    return f'UNK({gid})'

# Verify some known values
print("Verification:")
print(f"  GID 1 -> {get_col(1)}")   # Should be COL_FLOOR_CEIL (entry 0)
print(f"  GID 7 -> {get_col(7)}")   # Should be COL_FLOOR_CEIL (entry 6)
print(f"  GID 18 -> {get_col(18)}") # Should be COL_DEATH (entry 17)
print(f"  GID 48 -> {get_col(48)}") # entry 47
print(f"  GID 79 -> {get_col(79)}") # entry 78
print(f"  GID 112 -> {get_col(112)}") # entry 111
print(f"  GID 117 -> {get_col(117)}") # entry 116
print(f"  GID 118 -> {get_col(118)}") # entry 117

# Scan X=7200-7500 (cols 450-469), checking Y=240 (row 18) and Y=248 region
print()
print("Y=240 passability scan (cols 450-470, X=7200-7520):")
row18 = rows[18].strip().rstrip(',').split(',')
for c in range(450, 470):
    gid = int(row18[c].strip()) if c < len(row18) and row18[c].strip() else 0
    col = get_col(gid)
    passable = col in ['COL_NONE', 'EMPTY']
    marker = '.' if passable else '#'
    if not passable:
        print(f"  col {c} (X={c*16}): GID={gid} = {col}")

# Find first passable col at Y=240 and Y=256
print()
for target_row, y_label in [(18, 'Y=240'), (19, 'Y=256'), (20, 'Y=272')]:
    row_data = rows[target_row].strip().rstrip(',').split(',')
    first_pass = None
    for c in range(450, 570):
        gid = int(row_data[c].strip()) if c < len(row_data) and row_data[c].strip() else 0
        col = get_col(gid)
        if col in ['COL_NONE', 'EMPTY', 'COL_FLOOR_CEIL', 'COL_TOP', 'COL_BOTTOM']:
            first_pass = c
            break
    if first_pass:
        print(f"First ~passable col at {y_label}: col {first_pass} (X={first_pass*16}) = {get_col(int(row_data[first_pass].strip()))}")

# Check exact coin area terrain
print()
print("Terrain at coin area (X=9040-9088, Y=224-272):")
for row_idx in [17, 18, 19, 20]:
    y_world = (row_idx - 3) * 16
    row_data = rows[row_idx].strip().rstrip(',').split(',')
    parts = []
    for c in range(565, 569):
        gid = int(row_data[c].strip()) if c < len(row_data) and row_data[c].strip() else 0
        col = get_col(gid)
        parts.append(f"c{c}={col[:15]}")
    print(f"  {y_label}: {', '.join(parts)}" if row_idx == 18 else f"  Y={y_world}: {', '.join(parts)}")
