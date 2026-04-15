import xml.etree.ElementTree as ET
tree = ET.parse(r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\kratos.tmx")
root = tree.getroot()

collision_table = [
    0,1,2,3,3,3,3,3,3,3,3,3,3,3,3,3,
    5,5,5,5,5,5,5,5,5,5,5,5,5,5,5,5,
    3,3,3,3,3,3,3,3,3,3,3,3,3,3,3,3,
    5,5,5,5,5,5,5,0,0,0,0,0,0,0,0,0,
    0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,
    0,0,0,0,0,0,0,0,0,3,0,3,0,0,0,0,
    6,6,6,6,6,6,6,6,7,7,7,7,7,7,7,7,
    6,6,6,6,6,6,6,6,7,7,7,7,7,7,7,7,
    4,4,4,4,4,4,4,4,4,4,4,4,4,4,4,4,
    8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,8,
    9,9,9,9,9,9,9,9,10,10,10,10,10,10,10,10,
    9,9,9,9,9,9,9,9,10,10,10,10,10,10,10,10,
    3,3,3,3,3,3,3,3,3,3,3,5,5,5,5,5,
    5,5,5,3,3,3,3,3,11,11,11,11,0,0,0,0,
    0,0,0,0,0,0,0,0,0,0,0,5,5,5,5,5,
    5,5,5,0,0,0,0,0,12,12,12,12,0,0,0,0,
]

COL_NAMES = {
    0: "EMPTY", 1: "ALL", 2: "FLOOR_CEIL", 3: "DEATH",
    4: "BOTTOM", 5: "TOP", 6: "LEFT", 7: "RIGHT",
    8: "DEATH_BOTTOM", 9: "DEATH_TOP_LEFT", 10: "DEATH_TOP_RIGHT",
    11: "NO_SIDE", 12: "SLOPE"
}

for layer in root.findall('.//layer'):
    name = layer.get('name')
    if name == 'SP':
        continue
    width = int(layer.get('width'))
    data = layer.find('data')
    tiles_text = data.text.strip()
    tile_ids = [int(x) for x in tiles_text.split(',')]
    
    print("=== Terrain at cols 500-515, rows 16-22 ===")
    print(f"{'Col':>4}", end="")
    for r in range(16, 23):
        print(f"  r{r:2d}(Y={r*16:3d})", end="")
    print()
    
    for col in range(500, 516):
        print(f"{col:4d}", end="")
        for r in range(16, 23):
            idx = r * width + col
            tid = tile_ids[idx]
            if tid == 0:
                ctype = "EMPTY"
            else:
                mt = collision_table[tid - 1] if tid - 1 < len(collision_table) else 0
                ctype = COL_NAMES.get(mt, f"?{mt}")
            print(f"  {ctype:>12}", end="")
        print()
    
    print("\n=== Raw tile IDs at cols 505-512, rows 16-22 ===")
    for r in range(16, 23):
        print(f"r{r:2d} (Y={r*16:3d}-{r*16+15:3d}): ", end="")
        for col in range(505, 513):
            idx = r * width + col
            print(f" c{col}={tile_ids[idx]:3d}", end="")
        print()
    
    print("\n=== Forward collision check: cols 505-515, rows 18-19 ===")
    print("(Wave at Y=297-313, right edge hits these tiles)")
    for r in [17, 18, 19, 20]:
        print(f"r{r:2d} (Y={r*16:3d}-{r*16+15:3d}): ", end="")
        for col in range(505, 516):
            idx = r * width + col
            tid = tile_ids[idx]
            if tid == 0:
                ct = "EMPTY"
            else:
                mt = collision_table[tid - 1] if tid - 1 < len(collision_table) else 0
                ct = COL_NAMES.get(mt, f"?{mt}")
            print(f" [{col}]{ct}", end="")
        print()
