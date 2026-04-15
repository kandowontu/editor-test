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

# Check Y=144 (row 12: (144//16)+3 = 9+3 = 12) across the wave section (X=7200-9200)
# TMX row = worldY//16 + GRR where GRR=3
# Y=144: tileY = 144/16 = 9, row = 9+3 = 12
print("Y=144 (row 12) terrain scan - THE CORRIDOR BOUNDARY:")
row12 = rows[12].strip().rstrip(',').split(',')
blocking = []
passable = []
for c in range(450, 576):  # X=7200-9200
    gid = int(row12[c].strip()) if c < len(row12) and row12[c].strip() else 0
    col = get_col(gid)
    if col in ['COL_NONE', 'EMPTY']:
        passable.append(c)
    else:
        blocking.append((c, gid, col))

print(f"Total cols 450-575: {len(blocking)} blocking, {len(passable)} passable")
if len(blocking) > 0:
    print(f"First 20 blocking: {[(c, col[:15]) for c, gid, col in blocking[:20]]}")
if len(passable) > 0:
    print(f"First 20 passable: {[c for c in passable[:20]]}")

# Also check Y=128 and Y=160 for context
for check_y, check_label in [(128, "Y=128"), (144, "Y=144"), (160, "Y=160"), (176, "Y=176")]:
    tile_y = check_y // 16
    tmx_row = tile_y + 3
    row_data = rows[tmx_row].strip().rstrip(',').split(',')
    blocking_count = 0
    gid_counts = {}
    for c in range(450, 576):
        gid = int(row_data[c].strip()) if c < len(row_data) and row_data[c].strip() else 0
        col = get_col(gid)
        if col not in ['COL_NONE', 'EMPTY']:
            blocking_count += 1
            gid_counts[f"GID{gid}={col[:12]}"] = gid_counts.get(f"GID{gid}={col[:12]}", 0) + 1
    print(f"\n{check_label} (row {tmx_row}): {blocking_count}/126 blocking tiles")
    if gid_counts:
        for k, v in sorted(gid_counts.items(), key=lambda x: -x[1])[:5]:
            print(f"  {k}: {v}")
