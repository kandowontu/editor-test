import xml.etree.ElementTree as ET
root = ET.parse(r'c:\Editor Test\eon2.tmx').getroot()
layer = root.findall('.//layer')[0]
data = layer.find('data')
csv_text = data.text.strip()
gids = [int(x) for x in csv_text.split(',')]
w = int(layer.get('width'))
h = int(layer.get('height'))

# Show tiles at columns 404-425 (X=6464-6800) for all rows
# Account for firstgid=1: tile_index = gid - 1
print("Collision map cols 404-425 (X=6464-6800), tile indices (GID-1):")
print("     ", end="")
for c in range(404, 426):
    print(f" {c:3d}", end="")
print()
for row in range(h):
    line = f"r{row:2d}: "
    for c in range(404, 426):
        gid = gids[row * w + c] if c < w else 0
        tid = (gid - 1) & 0xFF if gid > 0 else 0
        if tid == 0 and gid <= 1:
            line += "  -- "
        else:
            line += f" {tid:02X} "
    print(line)
