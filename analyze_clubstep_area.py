import xml.etree.ElementTree as ET
import csv
import io

tmx_path = r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\clubstep.tmx"
tree = ET.parse(tmx_path)
root = tree.getroot()

# Get tile layer data
for layer in root.findall('layer'):
    layer_name = layer.get('name', 'unnamed')
    layer_id = layer.get('id')
    data_elem = layer.find('data')
    encoding = data_elem.get('encoding')
    print(f"Layer id={layer_id} name='{layer_name}' encoding={encoding}")
    
    if encoding == 'csv':
        raw = data_elem.text.strip()
        rows = []
        for line in raw.split('\n'):
            line = line.strip().rstrip(',')
            if line:
                vals = [int(x) for x in line.split(',')]
                rows.append(vals)
        
        print(f"  Total rows: {len(rows)}, cols in row0: {len(rows[0])}")
        
        # Print columns 546-562, rows 18-32 (0-indexed)
        print(f"\n  Tile data for columns 546-562, rows 18-32 (0-indexed):")
        print(f"  (Tile column = pixel_X / 16, Tile row = pixel_Y / 16)")
        print(f"  Col:  ", end="")
        for c in range(546, 563):
            print(f"{c:>5}", end="")
        print()
        print(f"  PxX:  ", end="")
        for c in range(546, 563):
            print(f"{c*16:>5}", end="")
        print()
        
        for r in range(18, 33):
            if r < len(rows):
                print(f"  R{r:02d} (Y={r*16:>4}): ", end="")
                for c in range(546, 563):
                    if c < len(rows[r]):
                        print(f"{rows[r][c]:>5}", end="")
                    else:
                        print("  OOB", end="")
                print()

# Look for sprite/object layers
print("\n\n=== Sprite Layer (tileset 'sprites', firstgid=257) ===")
# In Famidash TMX, sprites are usually in a separate layer or same layer with gid >= 257
# Let's also check for object groups
for objgroup in root.findall('objectgroup'):
    print(f"Object group: {objgroup.get('name')}")
    for obj in objgroup.findall('object'):
        x = float(obj.get('x', 0))
        y = float(obj.get('y', 0))
        if 8700 <= x <= 8900:
            print(f"  Object at ({x}, {y}): {obj.attrib}")

# Now let's look at ALL layers for sprite data in the column range
for layer in root.findall('layer'):
    layer_id = layer.get('id')
    data_elem = layer.find('data')
    if data_elem.get('encoding') == 'csv':
        raw = data_elem.text.strip()
        rows = []
        for line in raw.split('\n'):
            line = line.strip().rstrip(',')
            if line:
                vals = [int(x) for x in line.split(',')]
                rows.append(vals)
        
        # Check for sprite tiles (gid >= 257) in the area
        found_sprites = []
        for r in range(len(rows)):
            for c in range(540, 570):
                if c < len(rows[r]):
                    v = rows[r][c]
                    if v >= 257:
                        found_sprites.append((r, c, v, v-256))
        if found_sprites:
            print(f"\nLayer {layer_id} - Sprites (gid>=257) in cols 540-570:")
            for r, c, gid, sprite_id in found_sprites:
                print(f"  Row {r} (Y={r*16}), Col {c} (X={c*16}): gid={gid}, sprite_id=0x{sprite_id:02X} ({sprite_id})")

# Also let's look at a wider range for mode portals
# Mode portals in Famidash are specific sprite IDs
# Ship portal = 0x0E (14), Cube portal = 0x0D (13), Ball portal = 0x10 (16), etc.
print("\n\n=== Searching for mode portals (sprites) near X=8700-8900 ===")
for layer in root.findall('layer'):
    layer_id = layer.get('id')
    data_elem = layer.find('data')
    if data_elem.get('encoding') == 'csv':
        raw = data_elem.text.strip()
        rows = []
        for line in raw.split('\n'):
            line = line.strip().rstrip(',')
            if line:
                vals = [int(x) for x in line.split(',')]
                rows.append(vals)
        
        # Search wider range for portals
        for r in range(len(rows)):
            for c in range(530, 580):
                if c < len(rows[r]):
                    v = rows[r][c]
                    if v >= 257:
                        sprite_id = v - 256
                        # Common portal sprite IDs
                        portal_names = {
                            0x0D: "CUBE_PORTAL", 0x0E: "SHIP_PORTAL", 
                            0x0F: "SAVE_PORTAL", 0x10: "BALL_PORTAL",
                            0x11: "UFO_PORTAL", 0x1D: "WAVE_PORTAL",
                            0x1E: "ROBOT_PORTAL", 0x1F: "SPIDER_PORTAL",
                            0x12: "SIZE_PORTAL_SMALL", 0x13: "SIZE_PORTAL_BIG",
                            0x14: "MIRROR_PORTAL", 0x15: "UNMIRROR_PORTAL",
                            0x09: "GRAVITY_PORTAL_DOWN", 0x0A: "GRAVITY_PORTAL_UP",
                            0x16: "DUAL_PORTAL", 0x17: "SINGLE_PORTAL",
                            0x0B: "SPEED_1X", 0x0C: "SPEED_2X",
                            0x18: "SPEED_05X", 0x19: "SPEED_3X", 0x1A: "SPEED_4X",
                        }
                        name = portal_names.get(sprite_id, f"SPRITE_0x{sprite_id:02X}")
                        print(f"  Layer {layer_id}, Row {r} (Y={r*16}), Col {c} (X={c*16}): sprite_id=0x{sprite_id:02X} = {name}")

# Also look for tile collision types
print("\n\n=== Tile ID Reference ===")
print("Common Famidash tile IDs in the area:")
# Collect unique tile IDs in the area
unique_tiles = set()
for layer in root.findall('layer'):
    data_elem = layer.find('data')
    if data_elem.get('encoding') == 'csv':
        raw = data_elem.text.strip()
        rows = []
        for line in raw.split('\n'):
            line = line.strip().rstrip(',')
            if line:
                vals = [int(x) for x in line.split(',')]
                rows.append(vals)
        for r in range(18, 33):
            if r < len(rows):
                for c in range(546, 563):
                    if c < len(rows[r]):
                        v = rows[r][c]
                        if v != 0:
                            unique_tiles.add(v)

print(f"Unique non-zero tile IDs in area: {sorted(unique_tiles)}")

# Known tile types
tile_types = {
    0: "empty",
    9: "death_spike_top?", 10: "death_spike_top?",
    11: "death_spike_bottom?", 12: "death_spike_bottom?",
    13: "bg/deco",
    14: "bg_tile",
    18: "bg_deco", 19: "bg_deco",
    28: "solid_bg?",
    30: "special",
    31: "solid_block",
    32: "corner",
    35: "solid_right_wall", 37: "solid_left_wall",
    36: "solid_block_alt",
    38: "slope_BL", 39: "slope_BR",
    40: "slope_TL", 41: "slope_TR", 
    42: "solid_left_inner", 43: "solid_right_inner",
    44: "solid_bottom_left", 45: "solid_bottom_right",
    46: "death_spike?",
    47: "solid_half?",
    48: "solid_block",
    49: "spike_pointing_up",
    50: "death?",
    51: "solid?",
    52: "slope?", 53: "death_spike",
    54: "spike", 55: "spike/death",
    56: "deco",
    66: "solid_block_special",  67: "solid_right", 69: "solid_left",
    78: "transition", 80: "solid_block",
    97: "special",
    99: "solid_right_edge", 101: "solid_left_edge",
    102: "solid_UL", 103: "solid_UR", 104: "solid_DR", 105: "solid_DL",
    110: "death_spike", 111: "solid_connection",
    112: "solid/solid_fill",
    114: "solid_UR_diagonal", 116: "solid_UL_diagonal",
    117: "deco_top1", 118: "deco_top2", 119: "deco_top3",
    120: "deco_mid1", 121: "deco_mid2", 122: "deco_mid3",
    123: "deco_bot1", 124: "deco_bot2", 125: "deco_bot3",
    126: "orb",
    183: "right_cap", 184: "left_cap",
    194: "portal_edge_R", 195: "portal_inner",
    196: "portal_frame_L", 197: "portal_frame_R",
    198: "bg_deco", 199: "bg_deco", 204: "bg_deco", 205: "bg_deco",
    209: "special", 210: "special",
    233: "coin_top?", 234: "coin_bottom?", 235: "coin?", 236: "coin?",
}

for tid in sorted(unique_tiles):
    desc = tile_types.get(tid, "???")
    print(f"  Tile {tid:>3} (0x{tid:02X}): {desc}")
