"""Analyze what tiles/sprites are at the PF death point in eighto.tmx"""
import xml.etree.ElementTree as ET

tmx = ET.parse(r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\eighto.tmx")
root = tmx.getroot()
map_w = int(root.get('width'))
map_h = int(root.get('height'))
print(f"Map size: {map_w}x{map_h}")

# Parse tile layer
tile_layer = None
sprite_layer = None
layers = root.findall('layer')
if len(layers) >= 1:
    tile_layer = layers[0]
if len(layers) >= 2:
    sprite_layer = layers[1]

# Check tiles near death point
# Death at X~6126px Y~528px in pixel coords
# Ground rows reserved=3 (from BFS params)
# Tile = 16px
# tileX = 6126/16 = 382
# In pixel Y=528: tileY = 528/16 = 33 
# tileArrayY = tileY + groundReserve
TILE = 16
GROUND_RESERVE = 3

death_x_px = 6126
death_y_px = 528

tile_x = death_x_px // TILE
tile_y = death_y_px // TILE
tile_array_y = tile_y + GROUND_RESERVE

print(f"\nDeath point: ({death_x_px}, {death_y_px})px -> tile ({tile_x}, {tile_y}), array ({tile_x}, {tile_array_y})")

# Parse tile data
for layer in [tile_layer, sprite_layer]:
    if layer is None:
        continue
    name = layer.get('name', 'Unknown')
    data = layer.find('data')
    if data is None:
        continue
    text = data.text.strip()
    values = [int(v) for v in text.split(',')]
    
    print(f"\n=== Layer: {name} ===")
    # Show tiles in a 10x10 area around death point
    for ay in range(max(0, tile_array_y - 5), min(map_h, tile_array_y + 5)):
        row = []
        for tx in range(max(0, tile_x - 5), min(map_w, tile_x + 5)):
            idx = ay * map_w + tx
            if idx < len(values):
                v = values[idx]
                if v == 0:
                    row.append("  . ")
                else:
                    row.append(f" {v:02X} ")
            else:
                row.append("  ? ")
        wy = ay - GROUND_RESERVE  # world tile Y
        marker = " <--" if ay == tile_array_y else ""
        print(f"  Y={wy:3d} (ay={ay:2d}): {''.join(row)}{marker}")
    print(f"  X range: {max(0, tile_x - 5)} to {min(map_w, tile_x + 4)}, death tileX={tile_x}")
