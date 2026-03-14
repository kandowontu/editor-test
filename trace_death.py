"""
Full frame-by-frame trace of ball death at the convergence point.
Ball: mode=2, mini=true, normal gravity
State: X~7196, Y=425, VelY=0x32A
"""

# Constants
TILE = 16
VEL_X = 0x2C4  # x1 speed fixed-point 8.8
BALL_GRAVITY_MINI = 0x57
HB_W = 8  # mini hitbox width
HB_H = 7  # mini hitbox height
MINI_OFF_Y = (16 - 7) >> 1  # = 4
GRR = 3  # groundRowsToReserve

# Load TMX
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
        grid.append([int(x) for x in row_str.split(',') if x.strip()])

# Simplified collision table (only types we care about)
# Full table indexed by stored_tile_value = GID - 1
col_table_raw = [
    "NONE","FLOOR_CEIL","FLOOR_CEIL","BOTTOM","DEATH_TOP","FLOOR_CEIL","FLOOR_CEIL","NONE",
    "DEATH_BOTTOM","DEATH_BOTTOM","DEATH_TOP","DEATH_TOP","DEATH_BOTTOM","DEATH_TOP","DEATH_LEFT","DEATH_RIGHT",
    "ALL","DEATH","DEATH_BOTTOM","DEATH_BOTTOM","DEATH_TOP","TOP_CENTER_SPIKE","ALL","DEATH_TOP",
    "DEATH_BOTTOM","TOP","DEATH","DEATH","DEATH","DEATH_LEFT","DEATH_TOP","DEATH_RIGHT",
    "ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","NONE",
    "ALL","ALL","ALL","ALL","NONE","NONE","NONE","TOP_CENTER_SPIKE","ALL","ALL","ALL","DEATH_BOTTOM","ALL","ALL","ALL","ALL",
    "ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","NONE",
    "ALL","ALL","ALL","ALL","TOP","TOP","TOP","TOP","BOTTOM","BOTTOM","BOTTOM","DEATH",
    "DEATH_BOTTOM","DEATH_TOP","DEATH_RIGHT","DEATH_LEFT",
    "ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","NONE",
    "ALL","ALL","ALL","ALL","DOWN_RIGHT_SPIKE","DEATH_BOTTOM","DOWN_LEFT_SPIKE","DEATH_RIGHT",
    "NONE","DEATH_LEFT","UP_RIGHT_SPIKE","DEATH_TOP","UP_LEFT_SPIKE","DEATH","ALL","DEATH_BOTTOM",
    "NONE","NONE","DEATH","DEATH","DEATH_BOTTOM","DEATH_TOP","DEATH_BOTTOM","DEATH_TOP",
    "FLOOR_CEIL","FLOOR_CEIL","RIGHT","LEFT","RIGHT","LEFT","NONE","NO_SIDE",
]

def get_col(gid):
    if gid == 0: return "EMPTY"
    idx = gid - 1
    if idx < len(col_table_raw): return col_table_raw[idx]
    return "NONE"

def tile_kills_at_pixel(col, lx, ly):
    """COL_DEATH spike hitbox: localY in [4,11], localX in [4,8]"""
    if col == "DEATH":
        return (4 <= ly <= 11) and (4 <= lx <= 8)
    if col == "DEATH_TOP":
        return (ly < 6) and (5 <= lx <= 7)
    if col == "DEATH_BOTTOM":
        return (ly > 10) and (5 <= lx <= 7)
    if col == "DEATH_LEFT":
        return (lx < 6) and (6 <= ly <= 8)
    if col == "DEATH_RIGHT":
        return (lx >= 10) and (6 <= ly <= 8)
    if col == "DEATH_TOP_RIGHT":
        return ((lx >= 10) and (6 <= ly <= 8)) or ((ly < 6) and (5 <= lx <= 7))
    return False

def tile_occupies_pixel(col, lx, ly):
    if col in ("DEATH","DEATH_TOP","DEATH_BOTTOM","DEATH_LEFT","DEATH_RIGHT",
               "DEATH_TOP_RIGHT","DEATH_TOP_LEFT","DEATH_BOTTOM_RIGHT","DEATH_BOTTOM_LEFT",
               "FLOOR_CEIL","NO_SIDE","NONE","EMPTY"):
        return False
    if col == "ALL": return True
    if col in ("TOP","TOP_CENTER_SPIKE"): return ly <= 7
    if col == "BOTTOM": return ly >= 8
    if col == "RIGHT": return lx >= 8
    if col == "LEFT": return lx <= 7
    return col not in ("NONE","EMPTY")

def lookup_tile(x_px, y_px):
    tileX = x_px // TILE
    tileY = y_px // TILE
    tileArrayY = tileY + GRR
    if tileArrayY < 0 or tileArrayY >= len(grid): return "OOB", 0, 0, 0, 0, 0
    if tileX < 0 or tileX >= len(grid[tileArrayY]): return "OOB", 0, 0, 0, 0, 0
    gid = grid[tileArrayY][tileX]
    col = get_col(gid)
    localX = x_px - tileX * TILE
    localY = y_px - tileY * TILE
    return col, gid, tileX, tileArrayY, localX, localY

# Trace from convergence point
print("=" * 90)
print("FRAME-BY-FRAME TRACE from convergence: X=7196, Y=425, VelY=0x32A")
print("=" * 90)

# Initial state (assume pixel-aligned fixed point)
X_fixed = 7196 << 8  # = 1842176
Y_fixed = 425 << 8   # = 108800
VelY = 0x32A

for frame in range(8):
    X_px = X_fixed >> 8
    Y_px = Y_fixed >> 8
    
    print(f"\n--- Frame {frame} START: X={X_px} Y={Y_px} VelY=0x{VelY:X} ---")
    
    # Step 2: compute newX (not applied yet)
    newX_fixed = X_fixed + VEL_X
    
    # Step 5: Ball gravity
    VelY_new = VelY + BALL_GRAVITY_MINI
    Y_fixed_new = Y_fixed + VelY_new
    Y_px_new = Y_fixed_new >> 8
    print(f"  After gravity: VelY=0x{VelY_new:X} Y_fixed={Y_fixed_new} Y={Y_px_new}")
    
    # Ball eject - simplified: check if the ball is touching floor/ceiling
    # Hitbox bottom = Y + MINI_OFF_Y + HB_H = Y + 11
    hb_bottom = Y_px_new + MINI_OFF_Y + HB_H
    hb_top = Y_px_new + MINI_OFF_Y
    # Check floor at current column
    col_x = X_px  # old X for eject
    floor_tileY = hb_bottom // TILE
    floor_tileArrayY = floor_tileY + GRR
    if floor_tileArrayY < len(grid):
        floor_col_left = X_px + 3
        floor_tileX = floor_col_left // TILE
        floor_gid = grid[floor_tileArrayY][floor_tileX] if floor_tileX < len(grid[floor_tileArrayY]) else 0
        floor_ct = get_col(floor_gid)
        if tile_occupies_pixel(floor_ct, floor_col_left % TILE, hb_bottom % TILE):
            # Floor collision: snap to top of floor tile
            snap_y = floor_tileY * TILE - MINI_OFF_Y - HB_H
            print(f"  EJECT to floor: Y snapped from {Y_px_new} to {snap_y}")
            Y_px_new = snap_y
            Y_fixed_new = snap_y << 8
            VelY_new = 0
    
    # Step 7a: CheckFloorSpikes at OLD X, new Y
    topRowY = Y_px_new + MINI_OFF_Y + HB_H - 2  # bottom edge - 2
    botRowY = Y_px_new + MINI_OFF_Y               # top edge
    leftX = X_px + 3
    rightX = X_px + HB_W - 3
    
    corners = {
        "TL(phys BL)": (leftX, topRowY),
        "TR(phys BR)": (rightX, topRowY),
        "BL(phys TL)": (leftX, botRowY),
        "BR(phys TR)": (rightX, botRowY),
    }
    
    floor_spike_death = False
    print(f"  CheckFloorSpikes at X={X_px}, Y={Y_px_new}:")
    for name, (cx, cy) in corners.items():
        col, gid, tx, tay, lx, ly = lookup_tile(cx, cy)
        kills = tile_kills_at_pixel(col, lx, ly)
        if kills or col not in ("NONE","EMPTY","ALL","TOP","BOTTOM"):
            mark = " *** KILLS! ***" if kills else ""
            print(f"    {name}: ({cx},{cy}) -> tile({tx},{tay}) GID={gid} col={col} local=({lx},{ly}){mark}")
            if kills:
                floor_spike_death = True
    
    if floor_spike_death:
        print(f"\n  *** DEATH: FLOOR_SPIKE at frame {frame}! ***")
        print(f"  Position: X={X_px}, Y={Y_px_new}")
        break
    
    # Step 7b: CheckForwardCollision at OLD X, new Y
    rightEdge = X_px + HB_W
    miniTopOff = (16 - HB_H) >> 1
    centerY = Y_px_new + miniTopOff + (HB_H >> 1)  # Y + 4 + 3 = Y + 7
    
    fwd_col, fwd_gid, fwd_tx, fwd_tay, fwd_lx, fwd_ly = lookup_tile(rightEdge, centerY)
    fwd_solid = tile_occupies_pixel(fwd_col, fwd_lx, fwd_ly)
    fwd_kills = tile_kills_at_pixel(fwd_col, fwd_lx, fwd_ly)
    
    print(f"  CheckForwardCollision at rightEdge={rightEdge}, centerY={centerY}:")
    print(f"    tile({fwd_tx},{fwd_tay}) GID={fwd_gid} col={fwd_col} local=({fwd_lx},{fwd_ly})")
    print(f"    solid={fwd_solid} kills={fwd_kills}")
    
    if fwd_solid or fwd_kills:
        death_type = "FWD_COLL (solid)" if fwd_solid else "FWD_SPIKE (death)"
        print(f"\n  *** DEATH: {death_type} at frame {frame}! ***")
        break
    
    # Step 7c: CheckDeathCollision (center point)
    centerX_death = X_px + (HB_W >> 1) - 1  # X + 3
    hbOffY = MINI_OFF_Y  # for ball mode
    centerY_death = Y_px_new + (HB_H // 2) + hbOffY  # Y + 3 + 4 = Y + 7
    dc_col, dc_gid, _, _, dc_lx, dc_ly = lookup_tile(centerX_death, centerY_death)
    dc_kills = tile_kills_at_pixel(dc_col, dc_lx, dc_ly)
    if dc_kills:
        print(f"  *** DEATH: DEATH_COLL at ({centerX_death},{centerY_death})! ***")
        break
    
    # Step 8: Apply new X
    X_fixed = newX_fixed
    Y_fixed = Y_fixed_new
    VelY = VelY_new
    
    print(f"  End of frame: X={X_fixed>>8}, Y={Y_fixed>>8}, VelY=0x{VelY:X}")

print()
print("=" * 90)
print("ALSO CHECK: What if ball entered corridor 1-2 frames earlier?")
print("=" * 90)

# Trace backwards: what X/Y was the ball at 2 frames before convergence?
# Work backwards from (7196, 425, 0x32A)
# Frame -1: VelY_prev = 0x32A - 0x57 = 0x2D3, Y_fixed_prev = (425<<8) - 0x32A = 108800 - 810 = 107990
# Y_prev = 107990 >> 8 = 421
# X_fixed_prev = (7196<<8) - 0x2C4 = 1842176 - 708 = 1841468
# X_prev = 1841468 >> 8 = 7193

print("\nEstimated positions 1-2 frames before convergence:")
vely = 0x32A
y_fixed = 425 << 8
x_fixed = 7196 << 8
for back in range(1, 5):
    vely = vely - BALL_GRAVITY_MINI
    y_fixed = y_fixed - vely
    x_fixed = x_fixed - VEL_X
    print(f"  Frame -{back}: X={x_fixed>>8} Y={y_fixed>>8} VelY=0x{vely:X}")
    
    # Check forward collision at these positions
    re = (x_fixed >> 8) + HB_W
    cy = (y_fixed >> 8) + 7
    col, gid, tx, tay, lx, ly = lookup_tile(re, cy)
    print(f"    FWD probe: rightEdge={re} centerY={cy} -> tile({tx},{tay}) GID={gid} col={col} local=({lx},{ly})")
    print(f"    solid={tile_occupies_pixel(col, lx, ly)} kills={tile_kills_at_pixel(col, lx, ly)}")
