import xml.etree.ElementTree as ET

tree = ET.parse('famidash/LEVELS/LEVEL DATA/lvlset_HUGE/dorabaebasic4.tmx')
root = tree.getroot()
width = int(root.attrib['width'])
height = int(root.attrib['height'])

layers = list(root.findall('.//layer'))
print(f"Map: {width}x{height}, Layers: {len(layers)}")

# Get sprite layer (second layer, usually named 'SP')
for layer in layers:
    name = layer.attrib.get('name', '?')
    data = layer.find('data')
    if data is not None and data.text and name == 'SP':
        tiles = [int(x) for x in data.text.strip().split(',')]
        print(f"\nSprite layer: {name}")
        # Show sprites in the ship area: cols 120-200, rows 38-56
        print("Sprites cols 120-205, rows 38-56:")
        for row in range(38, min(height, 57)):
            sprites_in_row = []
            for col in range(120, min(205, width)):
                idx = row * width + col
                t = tiles[idx] if idx < len(tiles) else 0
                if t > 0:
                    sprites_in_row.append(f"c{col}=0x{t:X}")
            if sprites_in_row:
                print(f"  r{row:3d} (Y={row*16:4d}): {' '.join(sprites_in_row)}")
        
        # Show sprites on wider X range in ship path (rows 40-50 for yellow pads)
        print("\nSprites cols 80-205, rows 40-55 (wider view):")
        for row in range(40, min(height, 56)):
            sprites_in_row = []
            for col in range(80, min(205, width)):
                idx = row * width + col
                t = tiles[idx] if idx < len(tiles) else 0
                if t > 0:
                    sprites_in_row.append(f"c{col}(x{col*16})=0x{t:X}")
            if sprites_in_row:
                print(f"  r{row:3d} (Y={row*16:4d}): {' '.join(sprites_in_row)}")
        break
