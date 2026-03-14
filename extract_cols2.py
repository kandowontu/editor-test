"""
Clubstep terrain analysis at columns 440-460, rows 25-39
GIDs referenced to collision table via: table_index = gid - 1  (since firstgid=1)
groundRowsToReserve = 3
World tileY = tileArrayY - 3  (tileArrayY = TMX row index)
"""

with open(r'C:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\clubstep.tmx', 'r') as f:
    lines = f.readlines()

csv_lines = []
in_data = False
layer_count = 0
for line in lines:
    if '<data encoding="csv">' in line:
        layer_count += 1
        if layer_count == 1:
            in_data = True
            continue
    if in_data:
        if '</data>' in line:
            break
        csv_lines.append(line.strip().rstrip(','))

grid = []
for row_str in csv_lines:
    if row_str:
        vals = [int(x) for x in row_str.split(',') if x.strip()]
        grid.append(vals)

# FULL collision table - index = stored tile value = GID-1
# Built by counting non-empty lines with RemoveEmptyEntries
col_table = [
    # idx 0-15 (GID 1-16)
    "COL_NONE","COL_FLOOR_CEIL","COL_FLOOR_CEIL","COL_BOTTOM",
    "COL_DEATH_TOP","COL_FLOOR_CEIL","COL_FLOOR_CEIL","COL_NONE",
    "COL_DEATH_BOTTOM","COL_DEATH_BOTTOM","COL_DEATH_TOP","COL_DEATH_TOP",
    "COL_DEATH_BOTTOM","COL_DEATH_TOP","COL_DEATH_LEFT","COL_DEATH_RIGHT",
    # idx 16-31 (GID 17-32)
    "COL_ALL","COL_DEATH","COL_DEATH_BOTTOM","COL_DEATH_BOTTOM",
    "COL_DEATH_TOP","COL_TOP_CENTER_SPIKE","COL_ALL","COL_DEATH_TOP",
    "COL_DEATH_BOTTOM","COL_TOP","COL_DEATH","COL_DEATH",
    "COL_DEATH","COL_DEATH_LEFT","COL_DEATH_TOP","COL_DEATH_RIGHT",
    # idx 32-47 (GID 33-48)
    "COL_ALL","COL_ALL","COL_ALL","COL_ALL",
    "COL_ALL","COL_ALL","COL_ALL","COL_ALL",
    "COL_ALL","COL_ALL","COL_ALL","COL_ALL",
    "COL_ALL","COL_ALL","COL_ALL","COL_NONE",
    # idx 48-63 (GID 49-64)
    "COL_ALL","COL_ALL","COL_ALL","COL_ALL",
    "COL_NONE","COL_NONE","COL_NONE","COL_TOP_CENTER_SPIKE",
    "COL_ALL","COL_ALL","COL_ALL","COL_DEATH_BOTTOM",
    "COL_ALL","COL_ALL","COL_ALL","COL_ALL",
    # idx 64-79 (GID 65-80)
    "COL_ALL","COL_ALL","COL_ALL","COL_ALL",
    "COL_ALL","COL_ALL","COL_ALL","COL_ALL",
    "COL_ALL","COL_ALL","COL_ALL","COL_ALL",
    "COL_ALL","COL_ALL","COL_ALL","COL_NONE",
    # idx 80-95 (GID 81-96)
    "COL_ALL","COL_ALL","COL_ALL","COL_ALL",
    "COL_TOP","COL_TOP","COL_TOP","COL_TOP",
    "COL_BOTTOM","COL_BOTTOM","COL_BOTTOM","COL_DEATH",
    "COL_DEATH_BOTTOM","COL_DEATH_TOP","COL_DEATH_RIGHT","COL_DEATH_LEFT",
    # idx 96-111 (GID 97-112)
    "COL_ALL","COL_ALL","COL_ALL","COL_ALL",
    "COL_ALL","COL_ALL","COL_ALL","COL_ALL",
    "COL_ALL","COL_ALL","COL_ALL","COL_ALL",
    "COL_ALL","COL_ALL","COL_ALL","COL_NONE",
    # idx 112-127 (GID 113-128)
    "COL_ALL","COL_ALL","COL_ALL","COL_ALL",
    "COL_DOWN_RIGHT_SPIKE","COL_DEATH_BOTTOM","COL_DOWN_LEFT_SPIKE","COL_DEATH_RIGHT",
    "COL_NONE","COL_DEATH_LEFT","COL_UP_RIGHT_SPIKE","COL_DEATH_TOP",
    "COL_UP_LEFT_SPIKE","COL_DEATH","COL_ALL","COL_DEATH_BOTTOM",
    # idx 128-143 (GID 129-144)
    "COL_NONE","COL_NONE","COL_DEATH","COL_DEATH",
    "COL_DEATH_BOTTOM","COL_DEATH_TOP","COL_DEATH_BOTTOM","COL_DEATH_TOP",
    "COL_FLOOR_CEIL","COL_FLOOR_CEIL","COL_RIGHT","COL_LEFT",
    "COL_RIGHT","COL_LEFT","COL_NONE","COL_NO_SIDE",
    # idx 144-159 (GID 145-160)
    "COL_SLOPE_RD45","COL_SLOPE_LD45","COL_SLOPE_RU45","COL_SLOPE_LU45",
    "COL_SLOPE_RD22_RIGHT","COL_SLOPE_RD22_LEFT","COL_SLOPE_LD22_RIGHT","COL_SLOPE_LD22_LEFT",
    "COL_SLOPE_RU22_RIGHT","COL_SLOPE_RU22_LEFT","COL_SLOPE_LU22_RIGHT","COL_SLOPE_LU22_LEFT",
    "COL_SLOPE_RD66_TOP","COL_SLOPE_RD66_BOT","COL_SLOPE_LD66_BOT","COL_SLOPE_LD66_TOP",
    # idx 160-175 (GID 161-176)
    "COL_SLOPE_RU66_TOP","COL_SLOPE_RU66_BOT","COL_SLOPE_LU66_BOT","COL_SLOPE_LU66_TOP",
    "COL_NO_SIDE","COL_NO_SIDE","COL_NO_SIDE","COL_NO_SIDE",
    "COL_LEFT_SPIKE_BLOCK","COL_RIGHT_SPIKE_BLOCK","COL_BOTTOM_LEFT_SPIKE","COL_BOTTOM_RIGHT_SPIKE",
    "COL_BOTTOM_SPIKES","COL_DOWN_LEFT_SPIKE","COL_DOWN_RIGHT_SPIKE","COL_DOWN_BOTH_SPIKES",
    # idx 176-191 (GID 177-192)
    "COL_UP_LEFT","COL_UP_RIGHT","COL_DOWN_LEFT","COL_DOWN_RIGHT",
    "COL_TOP","COL_BOTTOM","COL_LEFT","COL_RIGHT",
    "COL_TOP_LEFT_STAIRS","COL_TOP_RIGHT_STAIRS","COL_BOTTOM_LEFT_STAIRS","COL_BOTTOM_RIGHT_STAIRS",
    "COL_TOP_LEFT_BOTTOM_RIGHT","COL_TOP_RIGHT_BOTTOM_LEFT","COL_UP_LEFT_SPIKE","COL_UP_RIGHT_SPIKE",
    # idx 192-207 (GID 193-208)
    "COL_UP_BOTH_SPIKES","COL_DEATH_TOP_RIGHT","COL_DEATH_TOP_LEFT","COL_DEATH_BOTTOM_RIGHT",
    "COL_DEATH_BOTTOM_LEFT","COL_NONE","COL_NONE","COL_BOTTOM_CENTER_SPIKE",
    "COL_BOTTOM_CENTER_SPIKE","COL_TOP","COL_BOTTOM","COL_NONE",
    "COL_NONE","COL_NONE","COL_NONE","COL_NONE",
    # idx 208-223 (GID 209-224)
    "COL_NONE","COL_NONE","COL_NONE","COL_ALL",
    "COL_NONE","COL_NONE","COL_NONE","COL_NONE",
    "COL_NONE","COL_DOWN_RIGHT_SPIKE","COL_DOWN_LEFT_SPIKE","COL_UP_LEFT_SPIKE",
    "COL_UP_RIGHT_SPIKE","COL_LEFT","COL_RIGHT","COL_UP_RIGHT",
    # idx 224-239 (GID 225-240)
    "COL_RIGHT","COL_TOP","COL_TOP","COL_UP_LEFT",
    "COL_TOP","COL_RIGHT","COL_BOTTOM","COL_TOP",
    "COL_NONE","COL_NONE","COL_NONE","COL_NONE",
    "COL_NONE","COL_NONE","COL_NONE","COL_NONE",
    # idx 240-255 (GID 241-256)
    "COL_DOWN_RIGHT_SPIKE","COL_DOWN_LEFT_SPIKE","COL_UP_RIGHT_SPIKE","COL_UP_LEFT_SPIKE",
    "COL_DOWN_RIGHT_SPIKE","COL_DOWN_LEFT_SPIKE","COL_DEATH","COL_RIGHT",
    "COL_LEFT","COL_UP_RIGHT_SPIKE","COL_UP_LEFT_SPIKE","COL_DEATH",
    "COL_ALL","COL_LEFT","COL_DOWN_RIGHT","COL_DOWN_LEFT",
]

GRR = 3  # groundRowsToReserve

def get_collision(gid):
    if gid == 0:
        return "EMPTY"
    stored = gid - 1  # firstgid=1 subtracted during loading
    if stored < 0 or stored >= len(col_table):
        return f"OUT_OF_RANGE({gid})"
    return col_table[stored]

def is_solid(col_type):
    """Returns True if the tile blocks movement (TileOccupiesPixel returns true for center)"""
    if col_type in ("COL_ALL",):
        return True
    if col_type in ("COL_TOP", "COL_TOP_CENTER_SPIKE"):
        return True  # solid in top half
    if col_type == "COL_BOTTOM":
        return True  # solid in bottom half
    if col_type in ("COL_RIGHT", "COL_LEFT", "COL_UP_LEFT", "COL_UP_RIGHT",
                     "COL_DOWN_LEFT", "COL_DOWN_RIGHT"):
        return True  # partial solid
    return False

def is_death(col_type):
    return ("DEATH" in col_type or "SPIKE" in col_type) and col_type != "EMPTY"

print("=" * 80)
print("CLUBSTEP TERRAIN ANALYSIS - Columns 440-460, Rows 25-39")
print("=" * 80)
print(f"Map: 906x40, groundRowsToReserve={GRR}")
print(f"World tileY = TMX_row - {GRR}")
print(f"World Y for TMX row R: Y = (R-{GRR})*16 .. (R-{GRR})*16+15")
print()

# Print specific GID lookups
print("=== GID COLLISION LOOKUPS (table_index = GID - 1) ===")
for gid in [48, 36, 14, 13, 34, 117, 118, 119, 120, 121, 122, 123, 124, 125,
            68, 73, 77, 69, 80, 28, 31, 32, 18, 19, 81, 194, 195, 196, 197, 198, 199, 233, 234]:
    stored = gid - 1
    ct = get_collision(gid)
    solid = is_solid(ct)
    death = is_death(ct)
    flag = " [SOLID]" if solid else (" [DEATH]" if death else " [PASS]")
    print(f"  GID {gid:>3} -> idx {stored:>3} -> {ct:<30}{flag}")

print()
print("=== TERRAIN MAP: TMX Rows 25-39, Columns 440-460 ===")
print("Legend: S=Solid, D=Death, .=Empty/Passable, ~=COL_NONE(visual)")
print(f"{'TMX':>4} {'World':>5} {'Y range':>11}  ", end="")
for c in range(440, 461):
    print(f"{c:>4}", end="")
print()
print("-" * (22 + 21*4))

for r in range(25, 40):
    world_row = r - GRR
    y_start = world_row * 16
    y_end = y_start + 15
    line = f"R{r:>2}  wR{world_row:>2}  {y_start:>4}-{y_end:<4}  "
    for c in range(440, 461):
        gid = grid[r][c] if c < len(grid[r]) else 0
        if gid == 0:
            line += "   ."
        else:
            ct = get_collision(gid)
            if ct == "COL_NONE" or ct == "EMPTY":
                line += "   ~"
            elif is_solid(ct):
                line += "   S"
            elif is_death(ct):
                line += "   D"
            else:
                line += "   ?"
    print(line)

print()
print("=== DETAILED VIEW: Non-empty tiles in rows 25-39, cols 440-460 ===")
for r in range(25, 40):
    world_row = r - GRR
    entries = []
    for c in range(440, 461):
        gid = grid[r][c] if c < len(grid[r]) else 0
        if gid != 0:
            ct = get_collision(gid)
            if ct != "COL_NONE":  # Skip background tiles for clarity
                entries.append(f"c{c}:GID{gid}={ct}")
    if entries:
        print(f"TMX R{r} (wR{world_row}, Y={world_row*16}-{world_row*16+15}):")
        for e in entries:
            print(f"  {e}")
    else:
        # Check if all COL_NONE
        all_none = all(get_collision(grid[r][c]) == "COL_NONE" 
                       for c in range(440, 461) if grid[r][c] != 0)
        if all_none and grid[r][440] != 0:
            print(f"TMX R{r} (wR{world_row}, Y={world_row*16}-{world_row*16+15}): ALL GID {grid[r][440]} = COL_NONE (passable background)")
        else:
            print(f"TMX R{r} (wR{world_row}, Y={world_row*16}-{world_row*16+15}): empty")

print()
print("=" * 80)
print("FORWARD COLLISION ANALYSIS")
print("=" * 80)
print()
print("Ball: mode=2(ball), mini=true, normal gravity, VelY=0x32A")
print(f"  hbW=8, hbH=7")
print(f"  GetHitboxOffsetY(2,true,false) = GetMiniCenterOffsetY(true) = (16-7)>>1 = 4")
print()
print("CheckForwardCollision center probe:")
print(f"  miniTopOffset = (16 - 7) >> 1 = 4")
print(f"  centerY = playerY + 4 + (7>>1) = playerY + 7")
print(f"  rightEdge = playerX + 8")
print(f"  (gameMode==2, so NO extra -2/+3 cube adjustment)")
print()

for playerY in [425, 430, 435, 440, 445, 450, 455, 460, 465, 470]:
    centerY = playerY + 7
    tileY = centerY // 16
    tileArrayY = tileY + GRR
    localY_val = centerY % 16
    
    # Check columns 449, 450, 451 (typical range as ball moves forward)
    results = []
    for col in [449, 450, 451]:
        if tileArrayY < len(grid) and col < len(grid[tileArrayY]):
            gid = grid[tileArrayY][col]
            stored = gid - 1 if gid > 0 else -1
            ct = get_collision(gid)
            solid_flag = "SOLID" if is_solid(ct) else ("DEATH" if is_death(ct) else "pass")
            results.append(f"c{col}:GID{gid}={ct}({solid_flag})")
        else:
            results.append(f"c{col}:OOB")
    
    print(f"  Y={playerY}: centerY={centerY} tileY={tileY} tmxR={tileArrayY} localY={localY_val}")
    for r_str in results:
        print(f"    {r_str}")

print()
print("=" * 80)
print("SPECIFIC PROBE AT DEATH POINT")
print("=" * 80)
playerX = 7196
playerY = 425
centerY = playerY + 7  # = 432
rightEdge = playerX + 8  # = 7204
tileX = rightEdge // 16  # = 450
tileY_val = centerY // 16  # = 27
tileArrayY_val = tileY_val + GRR  # = 30
localX = rightEdge % 16  # = 4
localY_val2 = centerY % 16  # = 0

print(f"playerX={playerX}, playerY={playerY}")
print(f"rightEdge={rightEdge}, centerY={centerY}")
print(f"tileX={tileX}, tileY={tileY_val}, tileArrayY={tileArrayY_val}")
print(f"localX={localX}, localY={localY_val2}")

gid_probe = grid[tileArrayY_val][tileX]
ct_probe = get_collision(gid_probe)
print(f"TMX[{tileArrayY_val}][{tileX}] = GID {gid_probe} -> collision = {ct_probe}")
print(f"TileOccupiesPixel({ct_probe}, {localX}, {localY_val2}) = {is_solid(ct_probe)}")
print(f"Result: {'BLOCKED/DEATH' if is_solid(ct_probe) or is_death(ct_probe) else 'PASS (no forward collision)'}")

print()
print("=== CORRIDOR STRUCTURE SUMMARY (at column 450) ===")
print("Looking at column 450 through the corridor:")
for tmx_r in range(28, 38):
    world_r = tmx_r - GRR
    gid = grid[tmx_r][450]
    ct = get_collision(gid)
    flag = "SOLID" if is_solid(ct) else ("DEATH" if is_death(ct) else "pass")
    print(f"  TMX R{tmx_r} (world Y={world_r*16}-{world_r*16+15}): GID {gid:>3} = {ct:<25} [{flag}]")

print()
print("=== CORRIDOR GAP ANALYSIS ===")
print("For each column, find the passable Y range (rows without solid/death):")
for c in range(443, 461):
    passable_rows = []
    for tmx_r in range(28, 37):  # corridor range
        gid = grid[tmx_r][c]
        ct = get_collision(gid)
        if not is_solid(ct) and not is_death(ct):
            world_r = tmx_r - GRR
            passable_rows.append((tmx_r, world_r, world_r*16, world_r*16+15))
    
    if passable_rows:
        top_y = passable_rows[0][2]
        bot_y = passable_rows[-1][3]
        gap = bot_y - top_y + 1
        row_str = ",".join(f"wR{r[1]}" for r in passable_rows)
        print(f"  Col {c}: passable Y={top_y}-{bot_y} ({gap}px) rows: {row_str}")
    else:
        print(f"  Col {c}: FULLY BLOCKED")
