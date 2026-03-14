import xml.etree.ElementTree as ET

tree = ET.parse(r'C:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\backontrack.tmx')
root = tree.getroot()
w = int(root.attrib['width'])
h = int(root.attrib['height'])
print(f"Map: {w}x{h}")

for layer in root.findall('layer'):
    name = layer.attrib.get('name', '(unnamed)')
    data = layer.find('data').text.strip()
    tiles = [int(x) for x in data.split(',')]
    nonzero = sum(1 for t in tiles if t != 0)
    print(f"  Layer '{name}': {nonzero} non-zero tiles out of {len(tiles)}")
    
    # Show first nonzero tile location
    if nonzero > 0:
        for i, t in enumerate(tiles):
            if t != 0:
                r = i // w
                c = i % w
                print(f"    First nonzero: [{c},{r}] = 0x{t:X} (pixel {c*16},{r*16})")
                break
        
        # Show ground level at col 2
        for r in range(h):
            idx = r * w + 2
            if tiles[idx] != 0:
                print(f"    Col 2 first nonzero: row {r} (Y={r*16}) = 0x{tiles[idx]:X}")
                break
