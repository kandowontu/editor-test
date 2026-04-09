"""Analyze blue pad positions and cube trajectory around 61% death zone in deathmoon."""
import xml.etree.ElementTree as ET

tmx = r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\deathmoon.tmx"
tree = ET.parse(tmx)
root = tree.getroot()

w = int(root.attrib['width'])
h = int(root.attrib['height'])
TILE = 16
GROUND = 3  # groundRowsToReserve

# Parse sprite layer
layers = root.findall('layer')
sp_layer = None
for l in layers:
    if l.attrib.get('name','').upper() == 'SP':
        sp_layer = l
        break

if not sp_layer:
    # Use last layer if not named
    sp_layer = layers[-1] if len(layers) > 1 else layers[0]

sp_data = sp_layer.find('data').text.strip()
sp_vals = [int(x.strip()) for x in sp_data.split(',')]

SPRITE_FIRSTGID = 257

# Find all blue pads (sid 0x0D, 0x0E, 0xFD, 0xFE) in the area around X=17000-18500
print("=== Blue pads near death zone (X=16000-19000) ===")
for idx, gid in enumerate(sp_vals):
    if gid <= 0:
        continue
    if gid >= SPRITE_FIRSTGID and gid < SPRITE_FIRSTGID + 256:
        sid = gid - SPRITE_FIRSTGID
        tx = idx % w
        ty = idx // w
        px = tx * TILE
        py = (ty - GROUND) * TILE
        if px >= 16000 and px <= 19000:
            pad_type = ""
            if sid in (0x0D, 0x0E, 0xFD, 0xFE):
                pad_type = " <<< BLUE PAD"
            elif sid in (0x02, 0x03, 0x04, 0x05):
                pad_type = " <<< YELLOW PAD"
            elif sid in (0x08, 0x09, 0x25, 0x26):
                pad_type = " <<< PINK PAD"
            elif sid in (0x0A, 0x0B):
                pad_type = " <<< RED PAD"
            print(f"  tx={tx} ty={ty} sid=0x{sid:02X} px={px} py={py}{pad_type}")

# Now show ALL sprites in the range X=17600-18200 (around the blue pad)
print("\n=== ALL sprites X=17600-18200 ===")
for idx, gid in enumerate(sp_vals):
    if gid <= 0:
        continue
    if gid >= SPRITE_FIRSTGID and gid < SPRITE_FIRSTGID + 256:
        sid = gid - SPRITE_FIRSTGID
        tx = idx % w
        ty = idx // w
        px = tx * TILE
        py = (ty - GROUND) * TILE
        if px >= 17600 and px <= 18200:
            print(f"  tx={tx} ty={ty} sid=0x{sid:02X} px={px} py={py}")

# Check terrain layer for the same area
tile_layer = layers[0]
tile_data = tile_layer.find('data').text.strip()
tile_vals = [int(x.strip()) for x in tile_data.split(',')]

print("\n=== Terrain at X=17840 (tx=1115), rows ty=45-55 ===")
for ty in range(45, min(56, h)):
    idx = ty * w + 1115
    if idx < len(tile_vals):
        gid = tile_vals[idx]
        tid = gid - 1 if gid > 0 else -1
        py = (ty - GROUND) * TILE
        print(f"  ty={ty} py={py} tid={tid}")

# The cube dies at X=18025 with Y=776-802, grav=True (flipped), mini=True
# Let's check: what terrain is at X=18025?
death_tx = 18025 // TILE  # tileX = 1126
print(f"\n=== Terrain at death X=18025 (tx={death_tx}), rows ty=45-55 ===")
for ty in range(45, min(56, h)):
    idx = ty * w + death_tx
    if idx < len(tile_vals):
        gid = tile_vals[idx]
        tid = gid - 1 if gid > 0 else -1
        py = (ty - GROUND) * TILE
        print(f"  ty={ty} py={py} tid={tid}")

# Also check tx=1127 (the next tile)
death_tx2 = death_tx + 1
print(f"\n=== Terrain at tx={death_tx2}, rows ty=45-55 ===")
for ty in range(45, min(56, h)):
    idx = ty * w + death_tx2
    if idx < len(tile_vals):
        gid = tile_vals[idx]
        tid = gid - 1 if gid > 0 else -1
        py = (ty - GROUND) * TILE
        print(f"  ty={ty} py={py} tid={tid}")

# Blue pad hitbox: sid 0x0D 
# sprite_widths[0x0D] = 3, sprite_heights[0x0D] = 3, sprite_x_offset[0x0D] = ?, sprite_y_offset[0x0D] = ?
# The cube at death is Y=776-802. With the blue pad at ty=50, py = (50-3)*16 = 752
# Need to check if cube path passes through the blue pad hitbox
print("\n=== Summary ===")
print("Cube dies at X=18025, Y=776-802, grav=True (flipped upward), mini=True")
print("Mini cube hitbox: W=8, H=7, hbOffY=4")
print("So cube bottom = Y+4+7 = Y+11, cube top = Y+4")
print("At Y=776: top=780, bottom=787")
print("At Y=802: top=806, bottom=813")
