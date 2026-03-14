import re

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

print(f'Grid: {len(grid)} rows, {len(grid[0]) if grid else 0} cols')
print()

# Collision table from MetatileCollision.cs
# Index = local_id (0-based), GID = local_id + 1 (firstgid=1)
col_names = [
    # Row 0: local_id 0-15
    "COL_NONE","COL_FLOOR_CEIL","COL_FLOOR_CEIL","COL_BOTTOM",
    "COL_DEATH_TOP","COL_FLOOR_CEIL","COL_FLOOR_CEIL","COL_NONE",
    "COL_DEATH_BOTTOM","COL_DEATH_BOTTOM","COL_DEATH_TOP","COL_DEATH_TOP",
    "COL_DEATH_BOTTOM","COL_DEATH_TOP","COL_DEATH_LEFT","COL_DEATH_RIGHT",
    # Row 1: local_id 16-31
    "COL_ALL","COL_DEATH","COL_DEATH_BOTTOM","COL_DEATH_BOTTOM",
    "COL_DEATH_TOP","COL_TOP_CENTER_SPIKE","COL_ALL","COL_DEATH_TOP",
    "COL_DEATH_BOTTOM","COL_TOP","COL_DEATH","COL_DEATH",
    "COL_DEATH","COL_DEATH_LEFT","COL_DEATH_TOP","COL_DEATH_RIGHT",
    # Row 2: local_id 32-47
    "COL_ALL","COL_ALL","COL_ALL","COL_ALL",
    "COL_ALL","COL_ALL","COL_ALL","COL_ALL",
    "COL_ALL","COL_ALL","COL_ALL","COL_ALL",
    "COL_ALL","COL_ALL","COL_ALL","COL_NONE",
    # Row 3: local_id 48-63
    "COL_ALL","COL_ALL","COL_ALL","COL_ALL",
    "COL_NONE","COL_NONE","COL_NONE","COL_TOP_CENTER_SPIKE",
    "COL_ALL","COL_ALL","COL_ALL","COL_DEATH_BOTTOM",
    "COL_ALL","COL_ALL","COL_ALL","COL_ALL",
    # Row 4: local_id 64-79
    "COL_ALL","COL_ALL","COL_ALL","COL_ALL",
    "COL_ALL","COL_ALL","COL_ALL","COL_ALL",
    "COL_ALL","COL_ALL","COL_ALL","COL_ALL",
    "COL_ALL","COL_ALL","COL_ALL","COL_NONE",
    # Row 5: local_id 80-95
    "COL_ALL","COL_ALL","COL_ALL","COL_ALL",
    "COL_TOP","COL_TOP","COL_TOP","COL_TOP",
    "COL_BOTTOM","COL_BOTTOM","COL_BOTTOM","COL_DEATH",
    "COL_DEATH_BOTTOM","COL_DEATH_TOP","COL_DEATH_RIGHT","COL_DEATH_LEFT",
    # Row 6: local_id 96-111
    "COL_ALL","COL_ALL","COL_ALL","COL_ALL",
    "COL_ALL","COL_ALL","COL_ALL","COL_ALL",
    "COL_ALL","COL_ALL","COL_ALL","COL_ALL",
    "COL_ALL","COL_ALL","COL_ALL","COL_NONE",
    # Row 7: local_id 112-127
    "COL_ALL","COL_ALL","COL_ALL","COL_ALL",
    "COL_DOWN_RIGHT_SPIKE","COL_DEATH_BOTTOM","COL_DOWN_LEFT_SPIKE","COL_DEATH_RIGHT",
    "COL_NONE","COL_DEATH_LEFT","COL_UP_RIGHT_SPIKE","COL_DEATH_TOP",
    "COL_UP_LEFT_SPIKE","COL_DEATH","COL_ALL","COL_DEATH_BOTTOM",
    # Row 8: local_id 128-143 (COL_NONE;$80 means index 128)
    "COL_NONE","COL_NONE","COL_DEATH","COL_DEATH",
    "COL_DEATH_BOTTOM","COL_DEATH_TOP","COL_DEATH_BOTTOM","COL_DEATH_TOP",
    "COL_FLOOR_CEIL","COL_FLOOR_CEIL","COL_RIGHT","COL_LEFT",
    "COL_RIGHT","COL_LEFT","COL_NONE","COL_NO_SIDE",
    # Row 9: local_id 144-159
    "COL_SLOPE_RD45","COL_SLOPE_LD45","COL_SLOPE_RU45","COL_SLOPE_LU45",
    "COL_SLOPE_RD22_RIGHT","COL_SLOPE_RD22_LEFT","COL_SLOPE_LD22_RIGHT","COL_SLOPE_LD22_LEFT",
    "COL_SLOPE_RU22_RIGHT","COL_SLOPE_RU22_LEFT","COL_SLOPE_LU22_RIGHT","COL_SLOPE_LU22_LEFT",
    "COL_SLOPE_RD66_TOP","COL_SLOPE_RD66_BOT","COL_SLOPE_LD66_BOT","COL_SLOPE_LD66_TOP",
    # Row 10: local_id 160-175
    "COL_SLOPE_RU66_TOP","COL_SLOPE_RU66_BOT","COL_SLOPE_LU66_BOT","COL_SLOPE_LU66_TOP",
    "COL_NO_SIDE","COL_NO_SIDE","COL_NO_SIDE","COL_NO_SIDE",
    "COL_LEFT_SPIKE_BLOCK","COL_RIGHT_SPIKE_BLOCK","COL_BOTTOM_LEFT_SPIKE","COL_BOTTOM_RIGHT_SPIKE",
    "COL_BOTTOM_SPIKES","COL_DOWN_LEFT_SPIKE","COL_DOWN_RIGHT_SPIKE","COL_DOWN_BOTH_SPIKES",
]

def get_collision(gid):
    if gid == 0:
        return "EMPTY"
    # GIDs >= 257 are from sprites tileset (firstgid=257)
    if gid >= 257:
        local_id = gid - 257
        # Sprites often have different collision - but for terrain analysis, treat as non-solid
        return f"SPRITE({gid})"
    local_id = gid - 1  # firstgid=1 for famidash tileset
    if local_id < len(col_names):
        return col_names[local_id]
    return f"UNK({gid})"

# Print all rows for columns 440-460
print("=== ALL ROWS for columns 440-460 (1-indexed) ===")
header = "Row  "
for c in range(440, 461):
    header += f"{c:>6}"
print(header)

for r in range(len(grid)):
    row_data = ""
    any_nonzero = False
    for c in range(439, 460):  # 0-indexed = cols 440-460 (1-indexed)
        gid = grid[r][c] if c < len(grid[r]) else 0
        if gid != 0:
            any_nonzero = True
        if gid == 0:
            row_data += f"{'·':>6}"
        else:
            row_data += f"{gid:>6}"
    if any_nonzero:
        print(f"R{r:>2}: {row_data}")

print()
print("=== ROWS 25-39 DETAILED (0-indexed) with collision types ===")
for r in range(25, 40):
    tiles_in_row = []
    for c in range(439, 460):
        gid = grid[r][c] if c < len(grid[r]) else 0
        if gid != 0:
            col_type = get_collision(gid)
            tiles_in_row.append(f"  col{c+1}: GID={gid} ({col_type})")
    if tiles_in_row:
        print(f"Row {r}:")
        for t in tiles_in_row:
            print(t)
    else:
        print(f"Row {r}: (empty)")

print()
print("=== SPECIFIC GID COLLISION LOOKUPS ===")
for gid in [48, 36, 14, 13, 34, 117, 118, 119, 120, 121, 122, 123, 124, 125, 9, 10, 11, 12, 53, 46]:
    print(f"GID {gid:>3} (local_id {gid-1:>3}): {get_collision(gid)}")

print()
print("=== PASSABILITY ANALYSIS: Columns 440-460, Rows 25-39 ===")
print("S=Solid, D=Death/Spike, ·=Empty/Passable, ?=Sprite")
header2 = "     "
for c in range(440, 461):
    header2 += f"{c:>3}"
print(header2)
for r in range(25, 40):
    line = f"R{r:>2}: "
    for c in range(439, 460):
        gid = grid[r][c] if c < len(grid[r]) else 0
        if gid == 0:
            line += "  ·"
        else:
            ct = get_collision(gid)
            if "SPRITE" in ct:
                line += "  ?"
            elif ct == "EMPTY" or ct == "COL_NONE":
                line += "  ·"
            elif "DEATH" in ct or "SPIKE" in ct:
                line += "  D"
            elif ct in ("COL_ALL", "COL_FLOOR_CEIL", "COL_TOP", "COL_BOTTOM",
                        "COL_LEFT", "COL_RIGHT", "COL_NO_SIDE",
                        "COL_TOP_CENTER_SPIKE"):
                line += "  S"
            elif "SLOPE" in ct:
                line += "  /"
            else:
                line += "  ?"
        # also mark COL_TOP_CENTER_SPIKE specially
    print(line)

# Now compute Y pixel positions
print()
print("=== Y PIXEL POSITIONS ===")
print("Each row is 16px. Row 0 = Y 0..15, Row 1 = Y 16..31, etc.")
print(f"Row 25: Y={25*16}..{25*16+15} (400-415)")
print(f"Row 26: Y={26*16}..{26*16+15} (416-431)")
print(f"Row 27: Y={27*16}..{27*16+15} (432-447)")
print(f"Row 28: Y={28*16}..{28*16+15} (448-463)")
print()

# Ball at Y=425: which row?
y_ball = 425
print(f"Ball at Y=425: row {y_ball // 16} (Y range {(y_ball//16)*16}-{(y_ball//16)*16+15})")
print()

# X=7196: which column?
x_ball = 7196
col_ball = x_ball // 16
print(f"Ball at X=7196: column {col_ball} (X range {col_ball*16}-{col_ball*16+15})")
# The right edge of mini hitbox: X + 8
print(f"Mini ball hitbox W=8, right edge = {x_ball + 8} = column {(x_ball+8)//16}")
print()

# Compute center Y for forward collision check
# mode=2 (ball), mini=true, gravFlipped=false
# GetHitboxOffsetY: for ball (mode 2), mini → GetMiniCenterOffsetY = (16-7)>>1 = 4
hbW = 8
hbH = 7
hbOffY = 4  # (16-7)>>1 
# For CheckForwardCollision:
# miniTopOffset = (16 - 7) >> 1 = 4
# centerY_px = playerY_px + 4 + (7>>1) = playerY_px + 4 + 3 = playerY_px + 7
# gameMode == 2 (ball), so no extra -2/+3 adjustment
playerY = 425
centerY = playerY + 4 + 3  # = 432
rightEdge = x_ball + hbW  # = 7204
print(f"ForwardCollision probe point: X={rightEdge}, Y={centerY}")
print(f"  tileX = {rightEdge // 16} (column {rightEdge//16})")
print(f"  tileY = {centerY // 16} (row {centerY//16})")
print(f"  localX = {rightEdge % 16}")
print(f"  localY = {centerY % 16}")
print()

# Check the tile at (tileX, tileY)
probe_col = rightEdge // 16  # 0-indexed
probe_row = centerY // 16
if probe_row < len(grid) and probe_col < len(grid[probe_row]):
    gid_at_probe = grid[probe_row][probe_col]
    print(f"Tile at probe point: column {probe_col}, row {probe_row}")
    print(f"  GID = {gid_at_probe}")
    print(f"  Collision = {get_collision(gid_at_probe)}")
