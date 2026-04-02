import xml.etree.ElementTree as ET
tree = ET.parse(r'..\famidash\levels\LEVEL DATA\lvlset_HUGE\eon.tmx')
root = tree.getroot()
layers = root.findall('.//layer')
layer = layers[0]  # First layer is terrain (name=None)
data = layer.find('data').text.strip()
rows = data.split('\n')

# Check TMX rows 19-26 for cols 1842-1848
for r in range(19, 27):
    row = rows[r].strip().rstrip(',').split(',')
    tiles = []
    for c in range(1842, 1849):
        gid = int(row[c])
        tid = gid - 1 if gid > 0 else -1
        tiles.append(f"c{c}={tid:4d}")
    print(f"TMX_row{r:2d} wY={(r-3)*16:3d}: {' '.join(tiles)}")

# Also check the sprite layer
for layer in root.findall('.//layer'):
    if layer.get('name') == 'SP':
        data = layer.find('data').text.strip()
        rows_s = data.split('\n')
        print("\nSprites:")
        for r in range(19, 27):
            row = rows_s[r].strip().rstrip(',').split(',')
            tiles = []
            for c in range(1842, 1849):
                gid = int(row[c])
                sid = gid - 257 if gid >= 257 else 0
                tiles.append(f"c{c}=0x{sid:02X}" if sid > 0 else f"c{c}=  .")
                
            print(f"TMX_row{r:2d} wY={(r-3)*16:3d}: {' '.join(tiles)}")
