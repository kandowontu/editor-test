import xml.etree.ElementTree as ET
tree = ET.parse('famidash/LEVELS/LEVEL DATA/lvlset_HUGE/dorabaebasic4.tmx')
root = tree.getroot()
width = int(root.attrib['width'])
height = int(root.attrib['height'])
print(f"Map: {width}x{height}")
for layer in root.findall('.//layer'):
    name = layer.attrib.get('name', '?')
    print(f"Layer: {name}")

for layer in root.findall('.//layer'):
    data = layer.find('data')
    if data is not None and data.text:
        tiles = [int(x) for x in data.text.strip().split(',')]
        name = layer.attrib.get('name', '?')
        print(f"\nUsing layer: {name}")
        # Ship at X~2500-3100px, Y~704
        # col range: 2500/16=156 to 3100/16=194
        # rows near 43-44 (Y~688-720)
        # But map has GroundRowsToReserve offset. Map is 57 rows.
        # Standard GD level height is ~57 tiles. Row 0 is top.
        print("Tiles cols 155-200, rows 38-56:")
        for row in range(38, min(height, 57)):
            line = f"r{row:3d}: "
            for col in range(155, min(205, width)):
                idx = row * width + col
                t = tiles[idx] if idx < len(tiles) else 0
                if t > 0:
                    line += f"{t:3X} "
                else:
                    line += "  . "
            print(line)
        break

# Also dump sprite layer
for layer in root.findall('.//layer'):
    name = layer.attrib.get('name', '?')
    if 'sprite' in name.lower() or 'object' in name.lower() or name == 'Tile Layer 2':
        data = layer.find('data')
        if data is not None and data.text:
            tiles = [int(x) for x in data.text.strip().split(',')]
            print(f"\nSprites layer: {name}")
            print("Sprites cols 155-200, rows 38-56:")
            for row in range(38, min(height, 57)):
                line = f"r{row:3d}: "
                for col in range(155, min(205, width)):
                    idx = row * width + col
                    t = tiles[idx] if idx < len(tiles) else 0
                    if t > 0:
                        line += f"{t:3X} "
                    else:
                        line += "  . "
                print(line)
            break
