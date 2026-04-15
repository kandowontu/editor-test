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
    
    # Check r12 tile types across wide range
    print("=== Row 12 (Y=192) tile types, cols 450-640 ===")
    r = 12
    current_type = None
    start_col = None
    for col in range(450, 641):
        idx = r * width + col
        tid = tile_ids[idx]
        if tid == 0:
            ctype = "EMPTY"
        else:
            mt = collision_table[tid - 1] if tid - 1 < len(collision_table) else 0
            ctype = COL_NAMES.get(mt, f"?{mt}")
        
        if ctype != current_type:
            if current_type is not None:
                print(f"  cols {start_col}-{col-1} ({col-start_col} cols): {current_type}")
            current_type = ctype
            start_col = col
    if current_type is not None:
        print(f"  cols {start_col}-640: {current_type}")
    
    # Also check specific FLOOR_CEIL tiles in this region
    print("\n=== Actual FLOOR_CEIL tiles in rows 11-13, cols 450-640 ===")
    for r in [11, 12, 13]:
        fc_cols = []
        for col in range(450, 641):
            idx = r * width + col
            tid = tile_ids[idx]
            if tid != 0:
                mt = collision_table[tid - 1] if tid - 1 < len(collision_table) else 0
                if mt == 2:  # FLOOR_CEIL
                    fc_cols.append(col)
        if fc_cols:
            # Group contiguous
            groups = []
            gs = fc_cols[0]
            ge = fc_cols[0]
            for c in fc_cols[1:]:
                if c == ge + 1:
                    ge = c
                else:
                    groups.append((gs, ge))
                    gs = c
                    ge = c
            groups.append((gs, ge))
            print(f"  r{r}: {', '.join(f'{s}-{e} ({e-s+1} cols)' for s, e in groups)}")
        else:
            print(f"  r{r}: NONE")
