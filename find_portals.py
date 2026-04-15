import xml.etree.ElementTree as ET
tree = ET.parse(r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\kratos.tmx")
root = tree.getroot()
for ts in root.findall('.//tileset'):
    print(f"Tileset: name={ts.get('name')} firstgid={ts.get('firstgid')} tilecount={ts.get('tilecount')}")

# Check SP layer for sprite data
for layer in root.findall('.//layer'):
    name = layer.get('name')
    print(f"Layer: name={name}")

# Check objectgroups
for group in root.findall('.//objectgroup'):
    objs = group.findall('object')
    print(f"ObjectGroup: {group.get('name')} with {len(objs)} objects")
    for obj in objs[:5]:
        print(f"  gid={obj.get('gid')} x={obj.get('x')} y={obj.get('y')}")

# Sprites are in the SP layer as tile data, not object layer
# Read SP layer and find portal tiles  
import csv, io
for layer in root.findall('.//layer'):
    name = layer.get('name')
    if name != 'SP':
        continue
    w = int(layer.get('width'))
    h = int(layer.get('height'))
    data = layer.find('data')
    reader = csv.reader(io.StringIO(data.text.strip()))
    tiles = []
    for row in reader:
        tiles.append([int(x.strip()) for x in row if x.strip()])
    
    firstgid = 257  # sprite tileset
    portals = {0x0C:'ship',0x0D:'ball',0x0E:'ufo',0x0F:'wave',0x10:'robot',0x11:'spider',
               0x12:'swing',0x22:'dual',0x23:'single',0x24:'mini',0x25:'growth',
               0x26:'gravflip',0x27:'gravnorm'}
    
    print(f"\nPortals in SP layer ({w}x{h}):")
    for r in range(h):
        for c in range(w):
            tid = tiles[r][c]
            if tid == 0:
                continue
            sid = tid - firstgid
            if sid in portals:
                eng_y = (r - 3) * 16  # groundRowsToReserve=3
                print(f"  col={c:4d} row={r:2d} X={c*16:5d} engY={eng_y:3d} sid=0x{sid:02X} {portals[sid]}")
