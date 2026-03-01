import xml.etree.ElementTree as ET

tree = ET.parse(r'famidash\LEVELS\LEVEL DATA\lvlset_HUGE\polargeist.tmx')
root = tree.getroot()
w = root.attrib["width"]
h = root.attrib["height"]
print(f"Map: width={w}, height={h}")

for layer in root.findall('layer'):
    lid = layer.attrib.get('id', '?')
    name = layer.attrib.get('name', '(unnamed)')
    data = layer.find('data').text.strip()
    rows = data.split('\n')
    
    print(f"\n=== Layer id={lid} name='{name}': Non-zero tiles in cols 560-600 ===")
    print(f"{'col':>4} {'row':>4} {'csvVal':>6} {'tileID':>6}  (tileID = csvVal-1)")
    for r_idx, row_str in enumerate(rows):
        vals = [int(x.strip()) for x in row_str.rstrip(',').split(',')]
        for c in range(560, 601):
            if c < len(vals) and vals[c] != 0:
                print(f"{c:4d} {r_idx:4d} {vals[c]:6d} {vals[c]-1:6d}")
