import xml.etree.ElementTree as ET
tree = ET.parse(r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\kratos.tmx")
root = tree.getroot()

collision_table = [
    0,1,2,3,3,3,3,3,3,3,3,3,3,3,3,3,
    5,5,5,5,5,5,5,5,5,5,5,5,5,5,5,5,
    3,3,3,3,3,3,3,3,3,3,3,3,3,3,3,3,
    5,5,5,5,5,5,5,0,0,0,0,0,0,0,0,0,
    0,0,0,0,0,0,0,0,0,3,0,3,0,0,0,0,
    0,0,0,0,0,0,0,0,3,0,3,0,0,0,0,0,
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
    8: "DEATH_BTM", 9: "DTH_TL", 10: "DTH_TR",
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
    
    coin_col = 9064 // 16  # = 566
    
    print(f"=== Vertical slice at coin col {coin_col}, rows 10-22 ===")
    for r in range(10, 23):
        idx = r * width + coin_col
        tid = tile_ids[idx]
        if tid == 0:
            ctype = "EMPTY(0)"
        else:
            mt = collision_table[tid - 1] if tid - 1 < len(collision_table) else 0
            ctype = f"{COL_NAMES.get(mt, f'?{mt}')}(tid={tid})"
        print(f"  r{r:2d} Y={r*16:3d}-{r*16+15:3d}: {ctype}")
    
    print(f"\n=== Rows 13-16 summary at cols 508-570 ===")
    for r in [13, 14, 15, 16]:
        tiles_on_row = {}
        for col in range(508, 571):
            idx = r * width + col
            tid = tile_ids[idx]
            if tid != 0:
                mt = collision_table[tid - 1] if tid - 1 < len(collision_table) else 0
                ctype = COL_NAMES.get(mt, f"?{mt}")
                if ctype not in tiles_on_row:
                    tiles_on_row[ctype] = []
                tiles_on_row[ctype].append(col)
        if tiles_on_row:
            print(f"  r{r:2d} Y={r*16:3d}-{r*16+15:3d}: ", end="")
            for ct, cols in sorted(tiles_on_row.items()):
                if len(cols) > 5:
                    print(f"{ct}(cols {cols[0]}-{cols[-1]}, #{len(cols)}) ", end="")
                else:
                    print(f"{ct}(cols {','.join(str(c) for c in cols)}) ", end="")
            print()
        else:
            print(f"  r{r:2d} Y={r*16:3d}-{r*16+15:3d}: ALL EMPTY")

    print(f"\n=== r12(FLOOR_CEIL?) and r13-16 at cols 556-576 around coin ===")
    print(f"{'Col':>4}", end="")
    for r in range(12, 20):
        print(f" r{r:2d}({r*16:3d})", end="")
    print()
    for col in range(556, 577):
        marker = " <<COIN" if col == coin_col else ""
        print(f"{col:4d}", end="")
        for r in range(12, 20):
            idx = r * width + col
            tid = tile_ids[idx]
            if tid == 0:
                c = "."
            else:
                mt = collision_table[tid - 1] if tid - 1 < len(collision_table) else 0
                c = COL_NAMES.get(mt, "?")[:5]
            print(f" {c:>8}", end="")
        print(marker)
