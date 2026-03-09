import xml.etree.ElementTree as ET
import csv, io

tree = ET.parse(r'c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_A\xstep.tmx')
root = tree.getroot()

width = int(root.get('width'))
height = int(root.get('height'))

layers = root.findall('layer')
tile_layer = layers[0]
sp_layer = layers[1]

def parse_layer(layer):
    data = layer.find('data')
    text = data.text.strip()
    reader = csv.reader(io.StringIO(text))
    rows = []
    for row in reader:
        vals = [int(x.strip()) for x in row if x.strip()]
        rows.append(vals)
    return rows

tiles = parse_layer(tile_layer)
sprites = parse_layer(sp_layer)

# Find all coins (SID 7, 0x1A=26, 0x1B=27) in sprite layer
print("=== All coins in sprite layer ===")
for r in range(len(sprites)):
    for c in range(len(sprites[r])):
        sid = sprites[r][c]
        if sid in (7, 26, 27):
            px = c * 16
            py = r * 16
            idx = r * width + c
            print(f"Coin SID=0x{sid:02X} at tile ({c},{r}), pixel ({px},{py}), idx={idx}")

# Coin 1 hit: (2912,351)-(2928,367)
# 2912/16 = 182, 351/16 = 21.9
# Tile col 182, row ~22 (pixel 2912, 352)
# But the hitbox is at (2912,351) so center is at tile col 182
print("\n=== Terrain around coin 1 (cols 175-195, tile rows 15-25) ===")
print("Tile IDs - showing solid terrain blocks")
for r in range(15, 26):
    line = f"row {r:2d} (y={r*16:3d}-{r*16+15:3d}): "
    for c in range(175, 196):
        t = tiles[r][c] if c < len(tiles[r]) else 0
        s = sprites[r][c] if c < len(sprites[r]) else 0
        if s in (7, 26, 27):
            line += " C  "
        elif t == 0 and s == 0:
            line += " .  "
        elif s != 0:
            line += f"s{s:02x} "
        else:
            line += f"t{t:02x} "
    print(line)

# Print the collision significance
print("\n=== Ship corridor and coin position ===")
print("Ship corridor Y ~ 308-313 (row 19)")
print("Coin 1 center Y ~ 359 (row 22)")
print("Vertical gap: ~46 pixels")
