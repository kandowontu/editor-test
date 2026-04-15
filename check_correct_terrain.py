"""Analyze kratos.tmx terrain using the CORRECT collision table from MetatileCollision.cs"""
import xml.etree.ElementTree as ET

# Exact collision table from MetatileCollision.cs static constructor
# Index = tile_id, value = collision name
COLLISION_MAP_RAW = """
COL_NONE
COL_FLOOR_CEIL
COL_FLOOR_CEIL
COL_BOTTOM
COL_DEATH_TOP
COL_FLOOR_CEIL
COL_FLOOR_CEIL
COL_NONE
COL_DEATH_BOTTOM
COL_DEATH_BOTTOM
COL_DEATH_TOP
COL_DEATH_TOP
COL_DEATH_BOTTOM
COL_DEATH_TOP
COL_DEATH_LEFT
COL_DEATH_RIGHT
COL_ALL
COL_DEATH
COL_DEATH_BOTTOM
COL_DEATH_BOTTOM
COL_DEATH_TOP
COL_TOP_CENTER_SPIKE
COL_ALL
COL_DEATH_TOP
COL_DEATH_BOTTOM
COL_TOP
COL_DEATH
COL_DEATH
COL_DEATH
COL_DEATH_LEFT
COL_DEATH_TOP
COL_DEATH_RIGHT
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_NONE
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_NONE
COL_NONE
COL_NONE
COL_TOP_CENTER_SPIKE
COL_ALL
COL_ALL
COL_ALL
COL_DEATH_BOTTOM
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_NONE
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_TOP
COL_TOP
COL_TOP
COL_TOP
COL_BOTTOM
COL_BOTTOM
COL_BOTTOM
COL_DEATH
COL_DEATH_BOTTOM
COL_DEATH_TOP
COL_DEATH_RIGHT
COL_DEATH_LEFT
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_NONE
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_DOWN_RIGHT_SPIKE
COL_DEATH_BOTTOM
COL_DOWN_LEFT_SPIKE
COL_DEATH_RIGHT
COL_NONE
COL_DEATH_LEFT
COL_UP_RIGHT_SPIKE
COL_DEATH_TOP
COL_UP_LEFT_SPIKE
COL_DEATH
COL_ALL
COL_DEATH_BOTTOM
COL_NONE
COL_NONE
COL_DEATH
COL_DEATH
COL_DEATH_BOTTOM
COL_DEATH_TOP
COL_DEATH_BOTTOM
COL_DEATH_TOP
COL_FLOOR_CEIL
COL_FLOOR_CEIL
COL_RIGHT
COL_LEFT
COL_RIGHT
COL_LEFT
COL_NONE
COL_NO_SIDE
COL_SLOPE_RD45
COL_SLOPE_LD45
COL_SLOPE_RU45
COL_SLOPE_LU45
COL_SLOPE_RD22_RIGHT
COL_SLOPE_RD22_LEFT
COL_SLOPE_LD22_RIGHT
COL_SLOPE_LD22_LEFT
COL_SLOPE_RU22_RIGHT
COL_SLOPE_RU22_LEFT
COL_SLOPE_LU22_RIGHT
COL_SLOPE_LU22_LEFT
COL_SLOPE_RD66_TOP
COL_SLOPE_RD66_BOT
COL_SLOPE_LD66_BOT
COL_SLOPE_LD66_TOP
COL_SLOPE_RU66_TOP
COL_SLOPE_RU66_BOT
COL_SLOPE_LU66_BOT
COL_SLOPE_LU66_TOP
COL_NO_SIDE
COL_NO_SIDE
COL_NO_SIDE
COL_NO_SIDE
COL_LEFT_SPIKE_BLOCK
COL_RIGHT_SPIKE_BLOCK
COL_BOTTOM_LEFT_SPIKE
COL_BOTTOM_RIGHT_SPIKE
COL_BOTTOM_SPIKES
COL_DOWN_LEFT_SPIKE
COL_DOWN_RIGHT_SPIKE
COL_DOWN_BOTH_SPIKES
COL_UP_LEFT
COL_UP_RIGHT
COL_DOWN_LEFT
COL_DOWN_RIGHT
COL_TOP
COL_BOTTOM
COL_LEFT
COL_RIGHT
COL_TOP_LEFT_STAIRS
COL_TOP_RIGHT_STAIRS
COL_BOTTOM_LEFT_STAIRS
COL_BOTTOM_RIGHT_STAIRS
COL_TOP_LEFT_BOTTOM_RIGHT
COL_TOP_RIGHT_BOTTOM_LEFT
COL_UP_LEFT_SPIKE
COL_UP_RIGHT_SPIKE
COL_UP_BOTH_SPIKES
COL_DEATH_TOP_RIGHT
COL_DEATH_TOP_LEFT
COL_DEATH_BOTTOM_RIGHT
COL_DEATH_BOTTOM_LEFT
COL_NONE
COL_NONE
COL_BOTTOM_CENTER_SPIKE
COL_BOTTOM_CENTER_SPIKE
COL_TOP
COL_BOTTOM
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_ALL
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_DOWN_RIGHT_SPIKE
COL_DOWN_LEFT_SPIKE
COL_UP_LEFT_SPIKE
COL_UP_RIGHT_SPIKE
COL_LEFT
COL_RIGHT
COL_UP_RIGHT
COL_RIGHT
COL_TOP
COL_TOP
COL_UP_LEFT
COL_TOP
COL_RIGHT
COL_BOTTOM
COL_TOP
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_DOWN_RIGHT_SPIKE
COL_DOWN_LEFT_SPIKE
COL_UP_RIGHT_SPIKE
COL_UP_LEFT_SPIKE
COL_DOWN_RIGHT_SPIKE
COL_DOWN_LEFT_SPIKE
COL_DEATH
COL_RIGHT
COL_LEFT
COL_UP_RIGHT_SPIKE
COL_UP_LEFT_SPIKE
COL_DEATH
COL_ALL
COL_LEFT
COL_DOWN_RIGHT
COL_DOWN_LEFT
""".strip().split('\n')

# Build lookup: tile_id -> collision_name
collision_table = {}
for i, line in enumerate(COLLISION_MAP_RAW):
    line = line.strip()
    if line.startswith('COL_'):
        name = line.split(';')[0].strip()  # handle COL_NONE;$80
        collision_table[i] = name
    else:
        collision_table[i] = 'COL_NONE'

def get_collision(tid):
    if tid == 0:
        return 'NONE(empty)'
    return collision_table.get(tid, 'COL_NONE')

def short_name(cname):
    return cname.replace('COL_', '').replace('FLOOR_CEIL', 'FC').replace('DEATH_BOTTOM', 'D_B').replace('DEATH_TOP', 'D_T').replace('DEATH_LEFT', 'D_L').replace('DEATH_RIGHT', 'D_R').replace('DEATH', 'DTH')

# Parse TMX
tree = ET.parse(r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\kratos.tmx")
root = tree.getroot()

for layer in root.findall('.//layer'):
    name = layer.get('name')
    if name == 'SP':
        continue
    width = int(layer.get('width'))
    data = layer.find('data')
    tile_ids = [int(x) for x in data.text.strip().split(',')]
    
    # 1. Vertical slice at coin column
    coin_col = 9064 // 16  # 566
    print(f"=== Vertical slice at coin col {coin_col} (X=9064), rows 10-22 ===")
    for r in range(10, 23):
        idx = r * width + coin_col
        tid = tile_ids[idx]
        cname = get_collision(tid)
        print(f"  r{r:2d} Y={r*16:3d}-{r*16+15:3d}: tid={tid:3d} -> {cname}")
    
    # 2. FLOOR_CEIL tiles in rows 11-20
    print(f"\n=== FLOOR_CEIL tile locations in rows 11-20, cols 450-640 ===")
    for r in range(11, 21):
        fc_cols = []
        for col in range(450, 641):
            idx = r * width + col
            tid = tile_ids[idx]
            cname = get_collision(tid)
            if 'FLOOR_CEIL' in cname:
                fc_cols.append(col)
        if fc_cols:
            groups = []
            gs = ge = fc_cols[0]
            for c in fc_cols[1:]:
                if c == ge + 1:
                    ge = c
                else:
                    groups.append((gs, ge))
                    gs = ge = c
            groups.append((gs, ge))
            desc = ', '.join(f'{s}-{e}({e-s+1})' for s, e in groups)
            print(f"  r{r:2d} Y={r*16:3d}: {desc}")
        else:
            print(f"  r{r:2d} Y={r*16:3d}: (none)")
    
    # 3. Transition zone cols 505-515, rows 11-20 with correct table
    print(f"\n=== Transition zone cols 505-515, rows 11-20 (CORRECT table) ===")
    print(f"{'Col':>4}", end="")
    for r in range(11, 21):
        print(f" {'r'+str(r):>8}", end="")
    print()
    for col in range(505, 516):
        print(f"{col:4d}", end="")
        for r in range(11, 21):
            idx = r * width + col
            tid = tile_ids[idx]
            cname = get_collision(tid)
            print(f" {short_name(cname):>8}", end="")
        print()
    
    # 4. Corridor at cols 556-576 (around coin), rows 12-20
    print(f"\n=== Around coin (cols 556-576), rows 12-20 (CORRECT) ===")
    print(f"{'Col':>4}", end="")
    for r in range(12, 21):
        print(f" {'r'+str(r):>8}", end="")
    print()
    for col in range(556, 577):
        marker = " <<COIN" if col == coin_col else ""
        print(f"{col:4d}", end="")
        for r in range(12, 21):
            idx = r * width + col
            tid = tile_ids[idx]
            cname = get_collision(tid)
            sn = short_name(cname)
            if 'empty' in cname:
                sn = '.'
            print(f" {sn:>8}", end="")
        print(marker)
    
    # 5. Check r12 across the corridor with correct table
    print(f"\n=== Row 12 (Y=192) collision types, cols 450-640 (CORRECT) ===")
    curr = None
    sc = None
    for col in range(450, 641):
        idx = 12 * width + col
        tid = tile_ids[idx]
        cname = get_collision(tid)
        if cname != curr:
            if curr is not None:
                print(f"  cols {sc}-{col-1}: {curr}")
            curr = cname
            sc = col
    print(f"  cols {sc}-640: {curr}")
