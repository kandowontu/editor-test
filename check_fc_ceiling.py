import xml.etree.ElementTree as ET

tree = ET.parse(r'c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\kratos.tmx')
root = tree.getroot()
layers = root.findall('layer')
layer = layers[0]
data = layer.find('data').text.strip()
rows_data = [r.strip() for r in data.split('\n') if r.strip()]

# Check FC ceiling continuity from col 453 to col 570
# Row 11 and Row 12 are the two ceiling rows
print("=== FC Ceiling scan (TMX tile 7 = FC) ===")
print("\nRow 11 (Y=176) - FC tiles:")
row11 = rows_data[11].split(',')
fc_start = None
fc_end = None
gaps = []
for c in range(400, 600):
    tid = int(row11[c]) if c < len(row11) else 0
    is_fc = (tid == 7)
    if is_fc:
        if fc_start is None:
            fc_start = c
        fc_end = c
    else:
        if fc_start is not None and fc_end is not None:
            print(f"  FC run: cols {fc_start}-{fc_end} (X={fc_start*16}-{(fc_end+1)*16-1})")
            fc_start = None
            fc_end = None

if fc_start is not None:
    print(f"  FC run: cols {fc_start}-{fc_end} (X={fc_start*16}-{(fc_end+1)*16-1})")

print("\nRow 12 (Y=192) - FC tiles:")
row12 = rows_data[12].split(',')
fc_start = None
for c in range(400, 600):
    tid = int(row12[c]) if c < len(row12) else 0
    is_fc = (tid == 7)
    if is_fc:
        if fc_start is None:
            fc_start = c
        fc_end = c
    else:
        if fc_start is not None:
            print(f"  FC run: cols {fc_start}-{fc_end} (X={fc_start*16}-{(fc_end+1)*16-1})")
            fc_start = None
if fc_start is not None:
    print(f"  FC run: cols {fc_start}-{fc_end} (X={fc_start*16}-{(fc_end+1)*16-1})")

# Check what's at col 507-509 transition zone in detail
print("\n=== Transition zone cols 505-512, rows 10-13 ===")
for r in range(10, 14):
    tiles = rows_data[r].split(',')
    print(f"r{r} Y{r*16}: ", end='')
    for c in range(505, 513):
        tid = int(tiles[c]) if c < len(tiles) else 0
        print(f"c{c}={tid:3d} ", end='')
    print()

# Also check: is there ANY non-FC tile in row 11 between col 453 and 507?
print("\n=== Row 11 non-FC tiles between cols 453-507 ===")
for c in range(453, 508):
    tid = int(row11[c])
    if tid != 7:
        print(f"  col {c}: TMX {tid}")
print("(empty = all FC)")

# Check row 12 non-FC between 508-570
print("\n=== Row 12 non-FC tiles between cols 508-570 ===")
for c in range(508, 571):
    tid = int(row12[c])
    if tid != 7:
        print(f"  col {c}: TMX {tid}")
print("(empty = all FC)")

# Check Row 22 (bottom FC floor) continuity
print("\n=== Row 22 (Y=352) FC scan cols 453-570 ===")
row22 = rows_data[22].split(',')
fc_start = None
for c in range(453, 571):
    tid = int(row22[c])
    is_fc = (tid == 7)
    if is_fc:
        if fc_start is None:
            fc_start = c
        fc_end = c
    else:
        if fc_start is not None:
            print(f"  FC run: cols {fc_start}-{fc_end}")
            fc_start = None
        print(f"  col {c}: TMX {tid} (NOT FC)")
if fc_start is not None:
    print(f"  FC run: cols {fc_start}-{fc_end}")
