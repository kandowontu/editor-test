import xml.etree.ElementTree as ET
tree = ET.parse(r'c:\Editor Test\cycles.tmx')
root = tree.getroot()
tw = int(root.get('tilewidth', 16))
th = int(root.get('tileheight', 16))
mw = int(root.get('width'))

# Check columns 690-720 (X ~11040-11520)
for layer in root.findall('.//layer'):
    name = layer.get('name')
    data = layer.find('data')
    if data is not None and data.get('encoding') == 'csv':
        tiles = [int(x) for x in data.text.strip().split(',')]
        rows = [tiles[r*mw:(r+1)*mw] for r in range(len(tiles)//mw)]
        print(f'Layer: {name}')
        for col in range(690, 720):
            col_tiles = [(r, rows[r][col]) for r in range(len(rows)) if col < len(rows[r]) and rows[r][col] != 0]
            if col_tiles:
                x_px = col * tw
                entries = [(r, f"0x{t:02X}", r*th) for r, t in col_tiles]
                print(f'  Col {col} (X={x_px}): {entries}')

# Also check sprites near X=11200-11500
print("\n--- Sprites near X=11200-11500 ---")
for og in root.findall('.//objectgroup'):
    for obj in og.findall('object'):
        x = float(obj.get('x', 0))
        if 10800 < x < 11600:
            y = float(obj.get('y', 0))
            gid = obj.get('gid', '')
            name = obj.get('name', '')
            print(f'x={x} y={y} gid={gid} name={name}')
