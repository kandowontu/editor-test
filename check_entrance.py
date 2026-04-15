import xml.etree.ElementTree as ET, csv, io

tree = ET.parse(r'c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\kratos.tmx')
root = tree.getroot()
width = int(root.attrib['width'])
height = int(root.attrib['height'])
layer = root.findall('.//layer')[0]
data = layer.find('data').text.strip()
reader = csv.reader(io.StringIO(data))
tiles = []
for row in reader:
    tiles.extend([int(x.strip()) for x in row if x.strip()])

print('Terrain around corridor entrance (cols 440-465):')
hdr = '          '
for c in range(440, 466):
    hdr += f'{c:5d}'
print(hdr)

for r in range(6, 22):
    line = f'r{r:2d} Y={r*16:3d}:'
    for c in range(440, 466):
        t = tiles[r * width + c]
        mt = t - 1 if t > 0 else -1
        if t == 0:
            line += '    .'
        elif mt == 111:
            line += '    ~'
        elif mt == 0:
            line += '    .'
        elif mt == 6:
            line += '  ==='
        elif mt == 64:
            line += '  ###'
        else:
            line += f'  {mt:3d}'
    print(line)

# Check sprites layer near corridor for gravity portals, mode portals, etc
sp_layer = root.findall('.//layer')[1]
sp_data = sp_layer.find('data').text.strip()
sp_reader = csv.reader(io.StringIO(sp_data))
sp_tiles = []
for row in sp_reader:
    sp_tiles.extend([int(x.strip()) for x in row if x.strip()])

print()
print('Sprites in cols 415-470:')
for r in range(height):
    for c in range(415, 470):
        st = sp_tiles[r * width + c]
        if st > 0:
            sid = st - 257
            print(f'  col={c} X={c*16} row={r} Y={r*16} sid=0x{sid:02X}')

# Also look at terrain col 454-456 (wave portal area) all rows
print()
print('Vertical at col 454 (wave portal X=7264):')
for r in range(height):
    t = tiles[r * width + 454]
    mt = t - 1 if t > 0 else -1
    print(f'  r{r} Y={r*16}: tile={t} mt={mt}')
