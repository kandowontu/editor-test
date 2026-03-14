import xml.etree.ElementTree as ET

tmxpath = r'C:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_A\clubstep.tmx'
tree = ET.parse(tmxpath)
root = tree.getroot()
layer = root.findall('layer')[0]
data = layer.find('data')
csv_text = data.text.strip()
rows = [r.strip() for r in csv_text.split('\n') if r.strip()]
num_cols = len(rows[0].split(','))
print(f'TMX rows={len(rows)} cols={num_cols}')

# Check col 450, rows 25-32
print('\nCol 450, rows 25-32:')
for r in range(25, 33):
    cols = rows[r].split(',')
    gid = int(cols[450]) if len(cols) > 450 else 0
    editor = gid - 1 if gid > 0 else -1
    worldTileY = r - 3  # groundRowsToReserve=3
    print(f'  tmxRow={r} worldTileY={worldTileY} col=450 gid={gid} editor={editor}')

# Row 28 is the solid row. Check cols 445-455.
print('\nRow 28 (solid row, worldTileY=25), cols 445-455:')
cols28 = rows[28].split(',')
for c in range(445, min(456, len(cols28))):
    gid = int(cols28[c])
    editor = gid - 1 if gid > 0 else -1
    print(f'  col={c} gid={gid} editor={editor}')

# The FWD collision probes at centerY which maps to worldTileY=27 → tmxRow=30
print('\nRow 30 (worldTileY=27, FWD probe), cols 448-453:')
cols30 = rows[30].split(',')
for c in range(448, 453):
    gid = int(cols30[c])
    editor = gid - 1 if gid > 0 else -1
    print(f'  col={c} gid={gid} editor={editor}')

# Also scan wider area to find where solid tiles appear at the death X
print('\nAll rows at col 450 (X=7200):')
for r in range(0, len(rows)):
    cols = rows[r].split(',')
    gid = int(cols[450]) if len(cols) > 450 else 0
    if gid > 0:
        editor = gid - 1
        print(f'  tmxRow={r} worldTileY={r-3} gid={gid} editor={editor}')
