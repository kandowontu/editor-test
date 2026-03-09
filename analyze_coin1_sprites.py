import xml.etree.ElementTree as ET
import csv, io

tree = ET.parse(r'c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_A\xstep.tmx')
root = tree.getroot()

width = int(root.get('width'))
height = int(root.get('height'))

layers = root.findall('layer')
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

sprites = parse_layer(sp_layer)

# Coin SIDs are maybe different values in the TMX
# Let's look for ALL non-zero sprites near where coin 1 should be
# coin 1 hitbox: (2912,351)-(2928,367), so tile col=182, row=21-22
print("=== All non-zero sprites in rows 18-25, cols 165-200 ===")
for r in range(18, 26):
    for c in range(165, 201):
        if c < len(sprites[r]):
            v = sprites[r][c]
            if v != 0:
                idx = r * width + c
                print(f"  row={r} col={c} sid=0x{v:04X}={v} pixel=({c*16},{r*16})-({c*16+16},{r*16+16}) idx={idx}")

# Also find coin SIDs used elsewhere
print("\n=== All unique sprite IDs in the map ===")
all_sids = set()
for r in sprites:
    for v in r:
        if v != 0:
            all_sids.add(v)
print(f"Count: {len(all_sids)}")
# Look for 7, 0x1a, 0x1b 
for s in sorted(all_sids):
    if s <= 30 or s in (7, 26, 27):
        print(f"  SID {s} (0x{s:X})")
