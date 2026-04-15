import xml.etree.ElementTree as ET
tree = ET.parse(r'c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\kratos.tmx')
root = tree.getroot()
layers = root.findall('layer')
layer = layers[0]  # First layer is the tile layer
data = layer.find('data').text.strip()
rows = [r.strip() for r in data.split('\n') if r.strip()]
print(f'Total rows: {len(rows)}')

# Collision table lookup (TMX tile -1 = metatile index)
# Key metatiles: 0=NONE, 1=FULL, 6=FLOOR_CEIL, etc.
# We just print raw tile IDs for now

# Check cols 448-460, rows 10-22 (Y=160-352)
print("\nTerrain at killing zone (cols 448-460, rows 10-22):")
print(f"{'':8s}", end='')
for c in range(448, 461):
    print(f"c{c:3d}", end=' ')
print(f"\n{'':8s}", end='')
for c in range(448, 461):
    print(f"X{c*16:4d}", end='')
print()

for r in range(10, 23):
    tiles = rows[r].split(',')
    print(f"r{r:2d} Y{r*16:3d}: ", end='')
    for c in range(448, 461):
        tid = int(tiles[c]) if c < len(tiles) else 0
        print(f"{tid:4d}", end=' ')
    print()

# Also check the collision types for key tiles
print("\n\nKey tile IDs seen and their metatile indices:")
seen = set()
for r in range(10, 23):
    tiles = rows[r].split(',')
    for c in range(448, 461):
        tid = int(tiles[c]) if c < len(tiles) else 0
        if tid not in seen:
            seen.add(tid)
for tid in sorted(seen):
    mt = tid - 1 if tid > 0 else -1
    print(f"  TMX tile {tid} -> metatile {mt}")
