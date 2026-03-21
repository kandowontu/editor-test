import xml.etree.ElementTree as ET
tree = ET.parse('cycles.tmx')
root = tree.getroot()
for layer in root.findall('.//layer'):
    name = layer.get('name','')
    if 'sprite' in name.lower() or 'object' in name.lower():
        data = layer.find('data')
        if data is None: continue
        tiles = [int(x) for x in data.text.strip().split(',')]
        w = int(layer.get('width'))
        h = int(layer.get('height'))
        print(f"Sprite layer: {name} {w}x{h}")
        # X=3968 is tile column 248. Check columns 240-260 for pads
        for row in range(h):
            for col in range(240, min(260, w)):
                tid = tiles[row * w + col]
                if tid != 0:
                    sid = tid & 0xFF
                    print(f"  sprite at col={col} row={row} tid=0x{tid:04X} sid=0x{sid:02X} (worldX={col*16}, worldY={row*16})")
