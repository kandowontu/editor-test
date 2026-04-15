import xml.etree.ElementTree as ET

tmx_path = r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\clubstep.tmx"
tree = ET.parse(tmx_path)
root = tree.getroot()

portal_names = {
    0x0D: "CUBE_PORTAL", 0x0E: "SHIP_PORTAL", 
    0x0F: "SAVE_PORTAL", 0x10: "BALL_PORTAL",
    0x11: "UFO_PORTAL", 0x1D: "WAVE_PORTAL",
    0x1E: "ROBOT_PORTAL", 0x1F: "SPIDER_PORTAL",
    0x12: "SIZE_SMALL", 0x13: "SIZE_BIG",
    0x14: "MIRROR_PORTAL", 0x15: "UNMIRROR_PORTAL",
    0x16: "DUAL_PORTAL", 0x17: "SINGLE_PORTAL",
    0x09: "GRAVITY_DOWN", 0x0A: "GRAVITY_UP",
    0x0B: "SPEED_1X", 0x0C: "SPEED_2X",
    0x18: "SPEED_05X", 0x19: "SPEED_3X", 0x1A: "SPEED_4X",
}

mode_portals = {0x0D, 0x0E, 0x10, 0x11, 0x1D, 0x1E, 0x1F}

for layer in root.findall('layer'):
    layer_id = layer.get('id')
    layer_name = layer.get('name', '')
    data_elem = layer.find('data')
    if data_elem.get('encoding') != 'csv':
        continue
    raw = data_elem.text.strip()
    rows = []
    for line in raw.split('\n'):
        line = line.strip().rstrip(',')
        if line:
            rows.append([int(x) for x in line.split(',')])
    
    # Search for ALL mode portals in the entire level, cols 480-600
    print(f"=== Layer {layer_id} ({layer_name}): Mode portals cols 480-600 (X=7680-9600) ===")
    for r in range(len(rows)):
        for c in range(480, 600):
            if c < len(rows[r]):
                v = rows[r][c]
                if v >= 257:
                    sid = v - 256
                    if sid in mode_portals:
                        name = portal_names[sid]
                        print(f"  Col {c} (X={c*16}), Row {r} (Y={r*16}): 0x{sid:02X} = {name}")
    
    # Also show ALL portal-like sprites  
    print(f"\n  All portals (mode+size+gravity+speed+mirror) cols 480-600:")
    for r in range(len(rows)):
        for c in range(480, 600):
            if c < len(rows[r]):
                v = rows[r][c]
                if v >= 257:
                    sid = v - 256
                    if sid in portal_names:
                        print(f"    Col {c} (X={c*16}), Row {r} (Y={r*16}): 0x{sid:02X} = {portal_names[sid]}")

# Also: what are the non-portal sprites in the area?
print("\n=== Non-portal sprites near target area (cols 540-570) ===")
sprite_descriptions = {
    0x01: "yellow_pad", 0x02: "pink_pad", 0x03: "red_pad",
    0x04: "blue_pad", 0x05: "yellow_orb", 0x06: "pink_orb",
    0x07: "red_orb", 0x08: "blue_orb",
    0x20: "coin1", 0x21: "coin2", 0x22: "coin3",
    0x37: "sawblade_deco", 0x38: "sawblade_deco_2",
    0x82: "deco_block", 0x83: "deco_pillar", 0x84: "deco_pillar2",
    0x85: "deco_pillar3", 0x8D: "deco_special", 0x9D: "deco_special2",
    0x9B: "deco_special3", 0x9C: "deco_special4",
}
for layer in root.findall('layer'):
    layer_id = layer.get('id')
    data_elem = layer.find('data')
    if data_elem.get('encoding') != 'csv':
        continue
    raw = data_elem.text.strip()
    rows = []
    for line in raw.split('\n'):
        line = line.strip().rstrip(',')
        if line:
            rows.append([int(x) for x in line.split(',')])
    
    for r in range(len(rows)):
        for c in range(540, 570):
            if c < len(rows[r]):
                v = rows[r][c]
                if v >= 257:
                    sid = v - 256
                    if sid not in portal_names:
                        desc = sprite_descriptions.get(sid, f"unknown_0x{sid:02X}")
                        print(f"  L{layer_id} Col {c} (X={c*16}), Row {r} (Y={r*16}): sprite 0x{sid:02X} = {desc}")
