"""Analyze terrain profile between the blue pads in the death zone area."""
import xml.etree.ElementTree as ET

tmx = r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\deathmoon.tmx"
tree = ET.parse(tmx)
root = tree.getroot()

w = int(root.attrib['width'])
h = int(root.attrib['height'])
TILE = 16
GROUND = 3

# Parse tile layer
layers = root.findall('layer')
tile_data = layers[0].find('data').text.strip()
tile_vals = [int(x.strip()) for x in tile_data.split(',')]

# Collision mapping (common tile IDs → collision types)
# tile 0 = COL_NONE, many others
COLLISION_NAMES = {}
# Let's just print raw tile IDs and figure it out

# Print terrain grid for tx=1110-1135, ty=44-56
print("=== Terrain grid tx=1110 to 1135, ty=44 to 56 ===")
print(f"{'ty':>3} {'py':>4} |", end="")
for tx in range(1110, 1136):
    print(f"{tx:>5}", end="")
print()
print("-" * (10 + 26*5))

for ty in range(44, min(57, h)):
    py = (ty - GROUND) * TILE
    print(f"{ty:>3} {py:>4} |", end="")
    for tx in range(1110, 1136):
        idx = ty * w + tx
        gid = tile_vals[idx] if idx < len(tile_vals) else 0
        tid = gid - 1 if gid > 0 else -1
        if tid == -1:
            print("    .", end="")
        else:
            print(f" {tid:>3}x", end="")
    print()

# Also show the sprite layer for the same area
sp_layer = None
for l in layers:
    if l.attrib.get('name','').upper() == 'SP':
        sp_layer = l
        break
if sp_layer is None and len(layers) > 1:
    sp_layer = layers[-1]

sp_data = sp_layer.find('data').text.strip()
sp_vals = [int(x.strip()) for x in sp_data.split(',')]
SPRITE_FIRSTGID = 257

print("\n=== Sprite grid tx=1110 to 1135, ty=44 to 56 ===")
print(f"{'ty':>3} {'py':>4} |", end="")
for tx in range(1110, 1136):
    print(f"{tx:>5}", end="")
print()
print("-" * (10 + 26*5))

for ty in range(44, min(57, h)):
    py = (ty - GROUND) * TILE
    print(f"{ty:>3} {py:>4} |", end="")
    for tx in range(1110, 1136):
        idx = ty * w + tx
        gid = sp_vals[idx] if idx < len(sp_vals) else 0
        if gid <= 0:
            print("    .", end="")
        elif gid >= SPRITE_FIRSTGID and gid < SPRITE_FIRSTGID + 256:
            sid = gid - SPRITE_FIRSTGID
            print(f"  {sid:02X}s", end="")
        else:
            print(f" {gid:>3}?", end="")
    print()

# What collision types do common tile IDs have?
# From the code: MetatileCollisionTable maps tile IDs to collision types
# Let me list the tiles that appear in this area
tiles_in_area = set()
for ty in range(44, 57):
    for tx in range(1110, 1136):
        idx = ty * w + tx
        gid = tile_vals[idx] if idx < len(tile_vals) else 0
        tid = gid - 1 if gid > 0 else -1
        if tid >= 0:
            tiles_in_area.add(tid)

print(f"\n=== Unique tile IDs in area: {sorted(tiles_in_area)} ===")
print("Need to check collision type for each in MetatileCollisionTable")
