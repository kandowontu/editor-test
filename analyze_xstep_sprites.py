import xml.etree.ElementTree as ET

tmx_path = r"C:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\xstep.tmx"
tree = ET.parse(tmx_path)
root = tree.getroot()

width = int(root.attrib['width'])
height = int(root.attrib['height'])

# Sprite name lookup
SPRITE_NAMES = {}
# Orbs 0x40-0x4F
SPRITE_NAMES[64] = "Yellow Orb"
SPRITE_NAMES[65] = "Magenta Orb"
SPRITE_NAMES[66] = "Green Orb"
SPRITE_NAMES[67] = "Blue Orb"
SPRITE_NAMES[68] = "Red Orb"
for i in range(69, 80):
    SPRITE_NAMES[i] = f"Orb 0x{i:02X}"

# Pads 0x30-0x3F
SPRITE_NAMES[48] = "Yellow Pad"
SPRITE_NAMES[49] = "Magenta Pad"
SPRITE_NAMES[50] = "Green Pad"
SPRITE_NAMES[51] = "Blue Pad"
SPRITE_NAMES[52] = "Red Pad"
for i in range(53, 64):
    SPRITE_NAMES[i] = f"Pad 0x{i:02X}"

# Speed portals 0xC0-0xC4
SPRITE_NAMES[192] = "Speed Portal: Yellow (2x)"
SPRITE_NAMES[193] = "Speed Portal: Orange (3x)"
SPRITE_NAMES[194] = "Speed Portal: Blue (1x)"
SPRITE_NAMES[195] = "Speed Portal: Green (4x)"
SPRITE_NAMES[196] = "Speed Portal: Purple (0.5x)"

# Game mode portals 0xD0-0xDF
SPRITE_NAMES[208] = "Game Mode Portal: Cube"
SPRITE_NAMES[209] = "Game Mode Portal: Ship"
SPRITE_NAMES[210] = "Game Mode Portal: Ball"
SPRITE_NAMES[211] = "Game Mode Portal: UFO"
SPRITE_NAMES[212] = "Game Mode Portal: Wave"
SPRITE_NAMES[213] = "Game Mode Portal: Robot"
SPRITE_NAMES[214] = "Game Mode Portal: Spider"

# Gravity portals
SPRITE_NAMES[224] = "Gravity Portal: Normal (down)"
SPRITE_NAMES[225] = "Gravity Portal: Flipped (up)"

# Mirror portals
SPRITE_NAMES[240] = "Mirror Portal: On"
SPRITE_NAMES[241] = "Mirror Portal: Off"

# Common hazards / objects
SPRITE_NAMES[0] = "Sprite 0x00"
SPRITE_NAMES[1] = "Sprite 0x01"
SPRITE_NAMES[2] = "Sprite 0x02"
SPRITE_NAMES[3] = "Sprite 0x03"
SPRITE_NAMES[4] = "Sprite 0x04"
SPRITE_NAMES[5] = "Sprite 0x05"

def sprite_name(idx):
    if idx in SPRITE_NAMES:
        return SPRITE_NAMES[idx]
    return f"Sprite 0x{idx:02X} ({idx})"

sprites = []

for layer in root.findall('.//layer'):
    layer_name = layer.attrib.get('name', '?')
    layer_id = layer.attrib.get('id', '?')
    data_elem = layer.find('data')
    if data_elem is None:
        continue
    encoding = data_elem.attrib.get('encoding', '')
    if encoding != 'csv':
        continue
    
    csv_text = data_elem.text.strip()
    values = [int(v.strip()) for v in csv_text.split(',') if v.strip()]
    
    for i, gid in enumerate(values):
        if gid >= 257 and gid < 513:
            col = i % width
            row = i // width
            sprite_idx = gid - 257
            sprites.append({
                'layer': layer_name,
                'layer_id': layer_id,
                'col': col,
                'row': row,
                'gid': gid,
                'sprite_idx': sprite_idx,
                'px_x': col * 16,
                'px_y': row * 16,
            })

# Sort by column (left to right), then row
sprites.sort(key=lambda s: (s['col'], s['row']))

# Check for objectgroups
objgroups = root.findall('.//objectgroup')

print(f"=== xStep TMX Sprite Analysis ===")
print(f"Map size: {width} x {height} tiles ({width*16} x {height*16} px)")
print(f"Layers found: {[l.attrib.get('name','?') for l in root.findall('.//layer')]}")
print(f"Object groups found: {len(objgroups)}")
for og in objgroups:
    print(f"  ObjectGroup: name={og.attrib.get('name','?')}")
    for obj in og.findall('object'):
        print(f"    Object: id={obj.attrib.get('id')}, x={obj.attrib.get('x')}, y={obj.attrib.get('y')}, "
              f"width={obj.attrib.get('width','?')}, height={obj.attrib.get('height','?')}, "
              f"type={obj.attrib.get('type','?')}, name={obj.attrib.get('name','?')}")

print(f"\nTotal sprite tiles found: {len(sprites)}")
print()

# Summary by type
from collections import Counter
type_counts = Counter(s['sprite_idx'] for s in sprites)
print("=== Sprite Type Summary ===")
for idx, count in sorted(type_counts.items()):
    print(f"  {sprite_name(idx):40s} (idx={idx:3d} / 0x{idx:02X}, GID={idx+257:3d}): {count} occurrence(s)")

print()
print("=== All Sprites (sorted left to right) ===")
print(f"{'#':>4s}  {'Layer':>5s}  {'Col':>4s}  {'Row':>4s}  {'PxX':>6s}  {'PxY':>5s}  {'GID':>4s}  {'SprIdx':>6s}  Name")
print("-" * 100)
for i, s in enumerate(sprites):
    idx = s['sprite_idx']
    print(f"{i+1:4d}  {s['layer']:>5s}  {s['col']:4d}  {s['row']:4d}  {s['px_x']:6d}  {s['px_y']:5d}  {s['gid']:4d}  0x{idx:02X}({idx:3d})  {sprite_name(idx)}")

# Highlight important gameplay sprites
print()
print("=== Key Gameplay Sprites ===")
print()
print("--- Orbs (0x40-0x4F) ---")
orbs = [s for s in sprites if 64 <= s['sprite_idx'] <= 79]
for s in orbs:
    idx = s['sprite_idx']
    print(f"  Col {s['col']:4d}, Row {s['row']:2d} ({s['px_x']:5d}, {s['px_y']:3d} px)  Layer={s['layer']}  {sprite_name(idx)}")

print()
print("--- Pads (0x30-0x3F) ---")
pads = [s for s in sprites if 48 <= s['sprite_idx'] <= 63]
for s in pads:
    idx = s['sprite_idx']
    print(f"  Col {s['col']:4d}, Row {s['row']:2d} ({s['px_x']:5d}, {s['px_y']:3d} px)  Layer={s['layer']}  {sprite_name(idx)}")

print()
print("--- Speed Portals (0xC0-0xC4) ---")
speed = [s for s in sprites if 192 <= s['sprite_idx'] <= 196]
for s in speed:
    idx = s['sprite_idx']
    print(f"  Col {s['col']:4d}, Row {s['row']:2d} ({s['px_x']:5d}, {s['px_y']:3d} px)  Layer={s['layer']}  {sprite_name(idx)}")

print()
print("--- Game Mode Portals (0xD0-0xDF) ---")
mode = [s for s in sprites if 208 <= s['sprite_idx'] <= 223]
for s in mode:
    idx = s['sprite_idx']
    print(f"  Col {s['col']:4d}, Row {s['row']:2d} ({s['px_x']:5d}, {s['px_y']:3d} px)  Layer={s['layer']}  {sprite_name(idx)}")

print()
print("--- Gravity Portals ---")
grav = [s for s in sprites if s['sprite_idx'] in (224, 225)]
for s in grav:
    idx = s['sprite_idx']
    print(f"  Col {s['col']:4d}, Row {s['row']:2d} ({s['px_x']:5d}, {s['px_y']:3d} px)  Layer={s['layer']}  {sprite_name(idx)}")

print()
print("--- Mirror Portals ---")
mirror = [s for s in sprites if s['sprite_idx'] in (240, 241)]
for s in mirror:
    idx = s['sprite_idx']
    print(f"  Col {s['col']:4d}, Row {s['row']:2d} ({s['px_x']:5d}, {s['px_y']:3d} px)  Layer={s['layer']}  {sprite_name(idx)}")

print()
print("--- Other/Unknown Sprites ---")
known = set(range(48, 80)) | set(range(192, 197)) | set(range(208, 226)) | {240, 241}
other = [s for s in sprites if s['sprite_idx'] not in known]
for s in other:
    idx = s['sprite_idx']
    print(f"  Col {s['col']:4d}, Row {s['row']:2d} ({s['px_x']:5d}, {s['px_y']:3d} px)  Layer={s['layer']}  {sprite_name(idx)}")
