import xml.etree.ElementTree as ET

tree = ET.parse(r'C:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\polargeist.tmx')
root = tree.getroot()

layers = root.findall('layer')

for layer in layers:
    lid = layer.get('id')
    name = layer.get('name', 'unnamed')
    data = layer.find('data').text.strip()
    rows = [r.strip() for r in data.split('\n') if r.strip()]
    
    print(f'=== Layer {lid} ({name}) - Columns 594-602 ===')
    print(f'Row | Col594 Col595 Col596 Col597 Col598 Col599 Col600 Col601 Col602')
    print(f'----+---------------------------------------------------------------')
    for ri, row_str in enumerate(rows):
        vals = [v.strip() for v in row_str.rstrip(',').split(',')]
        cols = [f'{vals[c]:>6}' for c in range(594, 603)]
        sep = ' '
        print(f'{ri:>3} | {sep.join(cols)}')
    print()

# Now identify non-zero tiles in col 598 for layer 1
data1 = layers[0].find('data').text.strip()
rows1 = [r.strip() for r in data1.split('\n') if r.strip()]
print('=== Column 598 (X=9568) Analysis - Layer 1 (Tiles) ===')
for ri, row_str in enumerate(rows1):
    vals = [v.strip() for v in row_str.rstrip(',').split(',')]
    tid = int(vals[598])
    if tid != 0:
        print(f'  Row {ri}: tile={tid}, Y={ri*16}')

print()
data2 = layers[1].find('data').text.strip()
rows2 = [r.strip() for r in data2.split('\n') if r.strip()]
print('=== Column 598 (X=9568) Analysis - Layer 2 (SP/Sprites) ===')
print('  (Sprite firstgid=257, so raw_id - 257 = sprite_index)')
print('  0x0A (pad) = sprite 10 = raw 267, 0x0B (orb) = sprite 11 = raw 268')
for ri, row_str in enumerate(rows2):
    vals = [v.strip() for v in row_str.rstrip(',').split(',')]
    sid = int(vals[598])
    if sid != 0:
        print(f'  Row {ri}: sprite_raw={sid}, sprite_idx=0x{sid-257:02X}, Y={ri*16}')

# Also check nearby columns in sprite layer for pads/orbs
print()
print('=== Sprites near cols 594-602 (pads=267/0x0A, orbs=268/0x0B) ===')
for ri, row_str in enumerate(rows2):
    vals = [v.strip() for v in row_str.rstrip(',').split(',')]
    for c in range(594, 603):
        sid = int(vals[c])
        if sid in (267, 268):
            stype = 'PAD (0x0A)' if sid == 267 else 'ORB (0x0B)'
            print(f'  Row {ri}, Col {c} (X={c*16}): {stype}, Y={ri*16}')

# Also show ALL non-zero sprites in cols 594-602
print()
print('=== ALL non-zero sprites in cols 594-602 ===')
for ri, row_str in enumerate(rows2):
    vals = [v.strip() for v in row_str.rstrip(',').split(',')]
    for c in range(594, 603):
        sid = int(vals[c])
        if sid != 0:
            print(f'  Row {ri}, Col {c} (X={c*16}): raw={sid}, sprite_idx=0x{sid-257:02X} ({sid-257}), Y={ri*16}')
