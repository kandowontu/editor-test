import xml.etree.ElementTree as ET
import re

# Load collision table
with open(r'C:\Editor Test\native-windows\MetatileCollision.cs', 'r') as f:
    content = f.read()
start = content.find('mappingText = @"') + len('mappingText = @"')
end = content.find('";', start)
mapping_text = content[start:end]
lines = [l for l in mapping_text.split('\n') if l.strip()]
rx = re.compile(r'COL_[A-Z0-9_]+')
coll_table = ['COL_NONE'] * 256
for idx, raw in enumerate(lines):
    if idx >= 256: break
    m = rx.search(raw.strip())
    if m:
        coll_table[idx] = m.group()

# Load TMX
tmxpath = r'C:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_A\clubstep.tmx'
tree = ET.parse(tmxpath)
root = tree.getroot()
layer = root.findall('layer')[0]
data = layer.find('data')
csv_text = data.text.strip()
rows = [r.strip() for r in csv_text.split('\n') if r.strip()]

# Show terrain from col 405 to 460, rows 25-32 (worldTileY 22-29)
# groundRowsToReserve = 3
print("Terrain map (cols 405-460, worldTileY 20-30)")
print("Legend: .=NONE A=ALL D=DEATH T=TOP B=BOTTOM s=spike-type F=FLOOR_CEIL X=other")

def char_for(coll):
    if coll == 'COL_NONE': return '.'
    if coll == 'COL_ALL': return 'A'
    if coll == 'COL_TOP': return 'T'
    if coll == 'COL_BOTTOM': return 'B'
    if coll == 'COL_FLOOR_CEIL': return 'F'
    if 'DEATH' in coll: return 'D'
    if 'SPIKE' in coll: return 's'
    if 'SLOPE' in coll: return '/'
    return 'X'

col_start = 400
col_end = 465
row_start = 23  # tmxRow 23 = worldTileY 20
row_end = 35    # tmxRow 35 = worldTileY 32

# Print column headers
header = "wtY tmx | "
for c in range(col_start, col_end):
    if c % 10 == 0:
        header += str(c // 100 % 10)
    else:
        header += " "
print(header)
header2 = "        | "
for c in range(col_start, col_end):
    if c % 10 == 0:
        header2 += str(c // 10 % 10)
    else:
        header2 += " "
print(header2)
header3 = "        | "
for c in range(col_start, col_end):
    header3 += str(c % 10)
print(header3)
print("--------+-" + "-" * (col_end - col_start))

for r in range(row_start, row_end):
    wty = r - 3
    line = f" {wty:2d}  {r:2d} | "
    cols = rows[r].split(',')
    for c in range(col_start, col_end):
        gid = int(cols[c]) if c < len(cols) else 0
        if gid == 0:
            line += '.'
        else:
            editor = gid - 1
            coll = coll_table[editor]
            line += char_for(coll)
    print(line)

# Also show the X pixel ranges
print(f"\nCol 405 = X {405*16}px, Col 450 = X {450*16}px, Col 460 = X {460*16}px")
print(f"Ball enters ball mode at ~X=6500px = col {6500//16}")
print(f"Ball dies at X=7196px, rightEdge=7204px, probe col = {7204//16}")
