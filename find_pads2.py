import xml.etree.ElementTree as ET
tree = ET.parse('cycles.tmx')
root = tree.getroot()
for layer in root.findall('.//layer'):
    name = layer.get('name','')
    w = layer.get('width')
    h = layer.get('height')
    print(f"Layer: '{name}' {w}x{h}")

# Also look for the sprite data
print("\nLooking for all non-zero tiles in all layers near col 240-260...")
for layer in root.findall('.//layer'):
    name = layer.get('name','')
    data = layer.find('data')
    if data is None: continue
    tiles = [int(x) for x in data.text.strip().split(',')]
    w = int(layer.get('width'))
    h = int(layer.get('height'))
    found = False
    for row in range(h):
        for col in range(240, min(260, w)):
            tid = tiles[row * w + col]
            if tid != 0:
                if not found:
                    print(f"\n--- Layer '{name}' ---")
                    found = True
                sid = tid & 0xFF
                if sid in [0x0A, 0x09, 0x35, 0x41, 0x42, 0x0B]:
                    print(f"  *** PAD/SPRING at col={col} row={row} tid=0x{tid:04X} sid=0x{sid:02X} worldXY=({col*16},{row*16})")
                else:
                    pass  # skip non-pad tiles for brevity
