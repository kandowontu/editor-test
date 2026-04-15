import xml.etree.ElementTree as ET, re
tree = ET.parse(r'c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\kratos.tmx')
root = tree.getroot()
layer = root.find('.//layer')
data = layer.find('data').text.strip()
rows = data.split('\n')
with open(r'c:\Editor Test\native-windows\MetatileCollision.cs', 'r') as f:
    content = f.read()
m = re.search(r'mappingText\s*=\s*@"(.*?)";', content, re.DOTALL)
text = m.group(1)
lines = [l.strip() for l in text.split('\n') if l.strip()]
rx = re.compile(r'COL_[A-Z0-9_]+')
col_table = ['COL_NONE'] * 256
for idx, l in enumerate(lines):
    if idx >= 256: break
    m2 = rx.search(l)
    if m2: col_table[idx] = m2.group(0)
def get_col(gid):
    if gid == 0: return 'EMPTY'
    mt = gid - 1
    if 0 <= mt < 256: return col_table[mt]
    return 'UNK'

# Check X=11800-12200 (cols 737-762), Y=256-320  
print('Terrain at Y=256-320, X=11800-12200:')
for row_idx in [19,20,21,22,23]:
    y = (row_idx-3)*16
    r = rows[row_idx].strip().rstrip(',').split(',')
    parts = []
    for c in [737, 740, 743, 746, 749, 752, 755, 758, 761]:
        gid = int(r[c].strip()) if c < len(r) and r[c].strip() else 0
        col = get_col(gid)
        parts.append(f'{col[:8]:8s}')
    print(f'Y={y:3d}: {"  ".join(parts)}')
print(f'Cols:  {"  ".join(f"X={c*16:5d}" for c in [737, 740, 743, 746, 749, 752, 755, 758, 761])}')

# Find obstacles at Y=272-288 between X=11800-12400
print()
print("Blocking tiles at Y=272 (row 20) cols 737-775:")
r20 = rows[20].strip().rstrip(',').split(',')
for c in range(737, 776):
    gid = int(r20[c].strip()) if c < len(r20) and r20[c].strip() else 0
    col = get_col(gid)
    if col not in ['COL_NONE', 'EMPTY']:
        print(f'  col {c} X={c*16}: {col}')

print()
print("Blocking tiles at Y=288 (row 21) cols 737-775:")
r21 = rows[21].strip().rstrip(',').split(',')
for c in range(737, 776):
    gid = int(r21[c].strip()) if c < len(r21) and r21[c].strip() else 0
    col = get_col(gid)
    if col not in ['COL_NONE', 'EMPTY']:
        print(f'  col {c} X={c*16}: {col}')
