import xml.etree.ElementTree as ET
tree = ET.parse('cycles.tmx')
root = tree.getroot()

# Find all SP layer sprites from col 245 to 260 (any row)
for layer in root.findall('.//layer'):
    name = layer.get('name','')
    if name != 'SP': continue
    data = layer.find('data')
    tiles = [int(x) for x in data.text.strip().split(',')]
    w = int(layer.get('width'))
    h = int(layer.get('height'))
    for row in range(h):
        for col in range(245, min(260, w)):
            tid = tiles[row * w + col]
            if tid != 0:
                sid = tid & 0xFF
                print(f"SP col={col} row={row} tid=0x{tid:04X} sid=0x{sid:02X} worldXY=({col*16},{row*16})")

# Also check what terrain tiles are there
print("\nTerrain near death zone (col 244-256):")
for layer in root.findall('.//layer'):
    name = layer.get('name','')
    if name != '': continue  # empty name = terrain
    data = layer.find('data')
    tiles = [int(x) for x in data.text.strip().split(',')]
    w = int(layer.get('width'))
    h = int(layer.get('height'))
    for row in range(h):
        for col in range(244, min(256, w)):
            tid = tiles[row * w + col]
            if tid != 0:
                print(f"  terrain col={col} row={row} tid=0x{tid:02X} worldXY=({col*16},{row*16})")
