import xml.etree.ElementTree as ET

tmx = r'c:\Editor Test\native-windows\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\deathmoon.tmx'
tree = ET.parse(tmx)
root = tree.getroot()
map_w = int(root.get('width'))
map_h = int(root.get('height'))
print(f"Map: {map_w}x{map_h}")

# Find collision layer (usually the first layer, or named 'collision')
for layer in root.findall('.//layer'):
    name = layer.get('name')
    print(f"Layer: {name}")

# Get first layer's data
layer = root.findall('.//layer')[0]
data = layer.find('data')
encoding = data.get('encoding')
print(f"Encoding: {encoding}")

if encoding == 'csv':
    all_tiles = []
    for val in data.text.strip().replace('\n', ',').split(','):
        val = val.strip()
        if val:
            all_tiles.append(int(val))
    print(f"Total tiles: {len(all_tiles)}")
    print(f"Expected: {map_w * map_h}")
    
    # Ground rows to reserve = 3
    ground = 3
    
    # Ship is at X=28850-28995 pixels, Y=55-133 pixels
    # Tile columns: 28850/16=1803 to 28995/16=1812
    # Tile rows (visual): 55/16=3 to 133/16=8
    # tileArrayY = visual_row + ground
    
    print("\n=== Tiles at ship's path ===")
    print("Ship X range: 28850-28995 px (cols 1803-1812)")
    print("Ship Y range: 55-133 px (visual rows 3-8, array rows 6-11)")
    print()
    
    # Check tiles at columns 1803-1812, array rows 0-15
    for array_row in range(0, 16):
        visual_row = array_row - ground
        line = f"ArrayRow={array_row:2d} (vis={visual_row:3d}, Y={visual_row*16:4d}-{visual_row*16+15:4d}): "
        tiles_in_row = []
        for col in range(1803, 1813):
            idx = array_row * map_w + col
            if idx < len(all_tiles):
                t = all_tiles[idx]
                tiles_in_row.append(f"{t:3d}")
            else:
                tiles_in_row.append("OOB")
        line += " ".join(tiles_in_row)
        print(line)
    
    # Also check the ceiling probe row specifically
    # At Y=128: probeY=127, tileAboveY=127/16=7, tileArrayY=7+3=10
    # At Y=133: probeY=132, tileAboveY=132/16=8, tileArrayY=8+3=11
    # At Y=55: probeY=54, tileAboveY=54/16=3, tileArrayY=3+3=6
    print("\n=== Ceiling probe specific checks ===")
    for y_px in [55, 64, 80, 96, 112, 128, 133]:
        probe_y = y_px - 1
        tile_above_y = probe_y // 16
        tile_array_y = tile_above_y + ground
        col = 1808  # middle of ship X range
        idx = tile_array_y * map_w + col
        if idx < len(all_tiles):
            t = all_tiles[idx]
            print(f"Y={y_px:3d}: probeY={probe_y}, tileAboveY={tile_above_y}, arrayY={tile_array_y}, tile={t}")
        else:
            print(f"Y={y_px:3d}: probeY={probe_y}, tileAboveY={tile_above_y}, arrayY={tile_array_y}, OOB")
