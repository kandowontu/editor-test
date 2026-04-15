import xml.etree.ElementTree as ET, csv, io

tree = ET.parse(r'c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\kratos.tmx')
root = tree.getroot()
width = int(root.attrib['width'])
height = int(root.attrib['height'])

layer = root.findall('.//layer')[0]  # first layer is tiles

data = layer.find('data').text.strip()
reader = csv.reader(io.StringIO(data))
tiles = []
for row in reader:
    tiles.extend([int(x.strip()) for x in row if x.strip()])

col_table = [
    'NONE','FC','FC','BOT','DT','FC','FC','NONE',
    'DB','DB','DT','DT','DB','DT','DL','DR',
    'ALL','D','DB','DB','DT','TCS','ALL','DT',
    'DB','TOP','D','D','D','DL','DT','DR',
    'ALL','ALL','ALL','ALL','ALL','ALL','ALL','ALL',
    'ALL','ALL','ALL','ALL','ALL','ALL','ALL','NONE',
    'ALL','ALL','ALL','ALL','NONE','NONE','NONE','TCS',
    'ALL','ALL','ALL','DB','ALL','ALL','ALL','ALL',
    'ALL','ALL','ALL','ALL','ALL','ALL','ALL','ALL',
    'ALL','ALL','ALL','ALL','ALL','ALL','ALL','NONE',
    'ALL','ALL','ALL','ALL','TOP','TOP','TOP','TOP',
    'BOT','BOT','BOT','D','DB','DT','DR','DL',
    'ALL','ALL','ALL','ALL','ALL','ALL','ALL','ALL',
    'ALL','ALL','ALL','ALL','ALL','ALL','ALL','NONE',
    'ALL','ALL','ALL','ALL','DRS','DB','DLS','DR',
    'NONE','DL','URS','DT','ULS',
]

def get_col(tid):
    if tid == 0: return 'NONE'
    if tid >= len(col_table): return '???'
    return col_table[tid]

print("Coin at col=566 (X=9056), Y=248 (rows 15-16)")
print(f"{'':10s}", end='')
for c in range(545, 580):
    print(f"{c:5d}", end='')
print()

for r in range(height):
    print(f"r{r:2d} Y={r*16:3d}:", end='')
    for c in range(545, 580):
        t = tiles[r * width + c]
        col = get_col(t)
        if col == 'NONE':
            print("    .", end='')
        elif col == 'ALL':
            print("  ###", end='')
        elif col.startswith('D'):
            print("  !!!", end='')
        elif col == 'TOP' or col == 'BOT' or col == 'FC':
            print("  ---", end='')
        else:
            print(f"  ???", end='')
    print()

print("\nRaw tile IDs rows 0-26, cols 555-575:")
for r in range(height):
    print(f"r{r:2d} Y={r*16:3d}:", end='')
    for c in range(555, 576):
        t = tiles[r * width + c]
        print(f" {t:3d}", end='')
    print()
