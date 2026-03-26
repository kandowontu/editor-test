import xml.etree.ElementTree as ET
tree = ET.parse(r'c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\hexagonforce.tmx')
root = tree.getroot()

# Collision table (simplified - get from the editor)
# Need to check tiles around death at pixel (2471,447) = tile (154,27)
# Also the SIM's ground layer offset

for layer in root.findall('layer'):
    name = layer.get('name')
    if name is not None:
        continue  # Skip SP layer
    width = int(layer.get('width'))
    height = int(layer.get('height'))
    data = layer.find('data').text.strip()
    tiles = [int(x) for x in data.split(',')]
    
    print(f"Terrain layer: {width}x{height}")
    print(f"Death at pixel (2471,447) = tile ({2471//16},{447//16})")
    print()
    
    # Check tiles around the death area: X=148-160, Y=24-32
    print("Tile map around death area (X=148-160, Y=24-32):")
    print("    ", end="")
    for x in range(148, 161):
        print(f"x{x:3d}", end=" ")
    print()
    
    for y in range(24, 33):
        print(f"Y={y:2d}:", end=" ")
        for x in range(148, 161):
            t = tiles[y*width+x]
            if t == 0:
                print("  . ", end=" ")
            else:
                print(f"0x{t:02X}", end=" ")
        print()
    
    # Also check with ground layer offset (+3 rows)
    print("\nWith ground layer offset (+3 rows, arrayY = tileY + 3):")
    print("Same data but shifted interpretation:")
    for y in range(24, 33):
        arrayY = y + 3
        if arrayY >= height:
            continue
        print(f"Y={y:2d} (arr={arrayY:2d}):", end=" ")
        for x in range(148, 161):
            t = tiles[arrayY*width+x]
            if t == 0:
                print("  . ", end=" ")
            else:
                print(f"0x{t:02X}", end=" ")
        print()
