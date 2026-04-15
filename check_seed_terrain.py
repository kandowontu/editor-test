import xml.etree.ElementTree as ET
import re

tree = ET.parse(r'c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\kratos.tmx')
root = tree.getroot()
layer = root.find('.//layer')
data = layer.find('data').text.strip()
rows = data.split('\n')

# Load collision table
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

def get_col(gid):
    if gid == 0: return 'EMPTY'
    if gid - 1 < len(col_entries): return col_entries[gid - 1]
    return f'UNK({gid})'

# Check X=7200-7280 (cols 450-455), Y=224-320
print('Terrain at X=7200-7280 (cols 450-455), Y=224-320:')
for row_idx in range(17, 24):
    y_world = (row_idx - 3) * 16
    tiles_row = rows[row_idx].strip().rstrip(',').split(',')
    line_parts = []
    for c in range(450, 456):
        gid = int(tiles_row[c].strip()) if c < len(tiles_row) and tiles_row[c].strip() else 0
        col = get_col(gid)
        line_parts.append(f'{gid:3d}={col[:8]:8s}')
    print(f'Y={y_world:3d} (r{row_idx:2d}): {"  ".join(line_parts)}')

print()
print('Cols:', [f'{c}=X{c*16}' for c in range(450, 456)])

# Also check broader area: cols 450-510 (X=7200-8160), rows 17-23
print()
print('Broad scan: which cols have COL_NONE/EMPTY at Y=240-272 (passable for wave):')
for row_idx in [18, 19, 20]:  # Y=240, 256, 272
    y_world = (row_idx - 3) * 16
    tiles_row = rows[row_idx].strip().rstrip(',').split(',')
    passable_cols = []
    for c in range(450, 570):
        gid = int(tiles_row[c].strip()) if c < len(tiles_row) and tiles_row[c].strip() else 0
        col = get_col(gid)
        if col in ['EMPTY', 'COL_NONE']:
            passable_cols.append(c)
    x_ranges = [f'{c}(X{c*16})' for c in passable_cols[:20]]
    print(f'Y={y_world}: {len(passable_cols)} passable cols: {", ".join(x_ranges)}')

# Check Y=248 area: what's directly at coin position
print()
print('Coin area (X=9056-9072, Y=239-255):')
for row_idx in [18]:  # Y=240 (row 18)
    y_world = (row_idx - 3) * 16
    tiles_row = rows[row_idx].strip().rstrip(',').split(',')
    for c in range(566, 568):  # X=9056-9072
        gid = int(tiles_row[c].strip()) if c < len(tiles_row) and tiles_row[c].strip() else 0
        col = get_col(gid)
        print(f'  col={c} X={c*16} Y={y_world}: GID={gid} = {col}')
