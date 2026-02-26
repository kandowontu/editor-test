import xml.etree.ElementTree as ET
import csv, io

tree = ET.parse(r'C:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\polargeist.tmx')
root = tree.getroot()

layers = root.findall('layer')
print(f'Found {len(layers)} layers')
for i, l in enumerate(layers):
    name = l.get("name")
    w = l.get("width")
    h = l.get("height")
    print(f'  Layer {i}: name={name!r}, width={w}, height={h}')

# Parse layer 1 (first layer - main tiles)
layer1 = layers[0]
data_elem = layer1.find('data')
encoding = data_elem.get('encoding')
print(f'Encoding: {encoding}')

csv_text = data_elem.text.strip()
rows = []
for line in csv.reader(io.StringIO(csv_text)):
    row = [int(x.strip()) for x in line if x.strip()]
    if row:
        rows.append(row)

print(f'Parsed {len(rows)} rows, first row has {len(rows[0])} cols')

# Extract columns 593-602, rows 16-26
print()
print('=== Layer 1: Columns 593-602, Rows 16-26 ===')
header = 'Row\t' + '\t'.join(str(c) for c in range(593, 603))
print(header)

for r in range(16, 27):
    vals = '\t'.join(str(rows[r][c]) for c in range(593, 603))
    print(f'R{r}\t{vals}')

# Question 1: array rows 20-23, columns 596-600
print()
print('=== Q1: Array rows 20-23 (world rows 17-20), Columns 596-600 ===')
header2 = 'ArrRow\tWldRow\tY_px\t' + '\t'.join(f'c{c}' for c in range(596, 601))
print(header2)
for ar in range(20, 24):
    wr = ar - 3
    y = wr * 16
    vals = '\t'.join(str(rows[ar][c]) for c in range(596, 601))
    print(f'{ar}\t{wr}\t{y}\t{vals}')

# Question 2: world row 18 = array row 21, columns 596-598
print()
print('=== Q2: World row 18 (array row 21), Columns 596-598 ===')
for c in range(596, 599):
    print(f'  Col {c}: tile {rows[21][c]}')

# Question 3: Y=289px -> floor surface at Y=289 means block top at Y=288
# Y=288 -> world row = 288/16 = 18, array row = 18+3 = 21
print()
print('=== Q3: Floor at Y=289px ===')
print(f'  Y=289 -> world row {289//16}, array row {289//16 + 3}')
print(f'  Y=288 -> world row {288//16}, array row {288//16 + 3}')
ar_288 = 288 // 16 + 3
print(f'  Tiles at array row {ar_288} (world row {288//16}, Y=288), cols 593-602:')
for c in range(593, 603):
    print(f'    Col {c}: tile {rows[ar_288][c]}')
