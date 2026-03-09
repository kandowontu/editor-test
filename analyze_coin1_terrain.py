import xml.etree.ElementTree as ET
import csv, io

tree = ET.parse(r'c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_A\xstep.tmx')
root = tree.getroot()

width = int(root.get('width'))
height = int(root.get('height'))
print(f"Map: {width}x{height}")

# Parse layers
layers = root.findall('layer')
for l in layers:
    name = l.get('name')
    w = int(l.get('width'))
    h = int(l.get('height'))
    print(f"Layer: {name} {w}x{h}")
    data = l.find('data')
    if data is not None:
        encoding = data.get('encoding')
        print(f"  encoding: {encoding}")
        if encoding == 'csv':
            text = data.text.strip()
            reader = csv.reader(io.StringIO(text))
            rows = []
            for row in reader:
                vals = [int(x.strip()) for x in row if x.strip()]
                rows.append(vals)
            
            # Coin 1: pixel (2912,351)-(2928,367)
            # Tile col = 2912/16 = 182, row = 351/16 = 21-22
            # Show tiles around the coin area (cols 170-195, rows 15-25)
            print(f"\n  Tiles around coin 1 (cols 170-195, rows 15-25):")
            for r in range(15, min(26, len(rows))):
                line = f"  row {r:2d}: "
                for c in range(170, min(196, len(rows[r]))):
                    v = rows[r][c]
                    if v == 0:
                        line += "  . "
                    else:
                        line += f"{v:3d} "
                print(line)
            
            # Also show the sprite layer for coin position
            if 'prite' in (name or '').lower() or layers.index(l) == 1:
                print(f"\n  Sprites around coin 1 area:")
                for r in range(15, min(26, len(rows))):
                    line = f"  row {r:2d}: "
                    for c in range(170, min(196, len(rows[r]))):
                        v = rows[r][c]
                        if v == 0:
                            line += "  . "
                        elif v == 7 or v == 0x1a or v == 0x1b:
                            line += f"*{v:2d} "  # coin!
                        else:
                            line += f"{v:3d} "
                    print(line)
