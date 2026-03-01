#!/usr/bin/env python3
"""
Polargeist Coin 2 Trajectory Simulation
All physics in 8.8 fixed-point (lower 8 bits fractional).

Confirmed from trace:
  H_SPEED  = 0x2C4 = 708 fixed/frame (2.765625 px/frame)
  GRAVITY  = 0x6B  = 107 fixed/frame (0.41796875 px/frame)
  JUMP_VEL = -0x590 = -1424 fixed (-5.5625 px/frame)
  PAD_VEL  = -0x7C0 = -1984 fixed (-7.75 px/frame)
  MAX_FALL = 0x600  = 1536 fixed (6.0 px/frame)

Update order (per frame transition):
  1) VelY += GRAVITY
  2) Y_fixed += VelY
  3) X_fixed += H_SPEED
  4) Check collisions at new position
"""

GRAVITY  = 107
JUMP_VEL = -1424
PAD_VEL  = -1984
MAX_FALL = 1536
H_SPEED  = 708

# Coin hitbox (pixel coords)
COIN_X1 = 9472
COIN_X2 = 9488
COIN_Y1 = 351
COIN_Y2 = 367

# Pillar solid tiles: cols 589-590, X=9424-9455
# Top surface at Y=320  (player stands at Y=305)
# Extends down to ground
PILLAR_X1 = 9424
PILLAR_X2 = 9455
PILLAR_Y_TOP = 320

# Ground surface
GROUND_Y = 384  # player feet land here -> Y = 369

# Safe floor X range (no death spikes)
SAFE_X_MIN = 9376  # col 586
SAFE_X_MAX = 9600  # extends past coin area

# Yellow pad tile
PAD_X1 = 9472
PAD_X2 = 9487  # 16px tile
PAD_Y  = 368   # tile row


def px(fixed):
    """Fixed-point to pixel (arithmetic right shift = floor division)."""
    return fixed >> 8


def check_coin(xp, yp):
    """Check if player hitbox overlaps coin hitbox.
    Player: left=xp+1, right=xp+14, top=yp+1, bottom=yp+15
    Using <= for inclusive boundary overlap."""
    pl, pr = xp + 1, xp + 14
    pt, pb = yp + 1, yp + 15
    return pl <= COIN_X2 and pr >= COIN_X1 and pt <= COIN_Y2 and pb >= COIN_Y1


def horiz_overlap_pillar(xp):
    """Check if player hitbox overlaps pillar horizontally."""
    return xp + 14 >= PILLAR_X1 and xp + 1 <= PILLAR_X2


def horiz_overlap_pad(xp):
    """Check if player hitbox overlaps pad tile horizontally."""
    return xp + 14 >= PAD_X1 and xp + 1 <= PAD_X2


def inside_pillar(xp, yp):
    """Check if player hitbox overlaps a solid pillar tile (wall collision)."""
    if not horiz_overlap_pillar(xp):
        return False
    # Player bottom = yp+15, player top = yp+1
    # Pillar occupies Y = PILLAR_Y_TOP to GROUND_Y-1 (320 to 383)
    return yp + 15 > PILLAR_Y_TOP and yp + 1 < GROUND_Y


def simulate(start_x_px, start_y_px, start_vy, max_frames=400, label=""):
    """
    Simulate from (start_x_px, start_y_px) with initial VelY.
    Player is airborne at start (for walk-off: vy=0, for jump: vy=JUMP_VEL).
    
    Returns: (events_list, coin_collected_bool)
    Events: list of (frame, xp, yp, vy, event_string)
    """
    x_f = start_x_px * 256
    y_f = start_y_px * 256
    vy = start_vy
    
    on_ground = False
    ground_surface_y = None
    ground_x1 = None
    ground_x2 = None
    pad_used = False
    coin_hit = False
    
    events = []
    
    for frame in range(max_frames):
        xp = px(x_f)
        yp = px(y_f)
        
        # Check coin
        if not coin_hit and check_coin(xp, yp):
            events.append((frame, xp, yp, vy, "COIN_HIT"))
            coin_hit = True
        
        if xp > 9600:
            break
        
        # --- Advance physics ---
        if on_ground:
            # Walking on surface
            x_f += H_SPEED
            xp_new = px(x_f)
            
            # Check pad trigger (only on ground level)
            if not pad_used and ground_surface_y == GROUND_Y:
                if horiz_overlap_pad(xp_new):
                    pad_used = True
                    vy = PAD_VEL
                    on_ground = False
                    ground_surface_y = None
                    events.append((frame, xp_new, yp, vy, "PAD_ACTIVATE"))
                    continue
            
            # Check walk-off edge
            still_supported = False
            if ground_surface_y == PILLAR_Y_TOP:
                if horiz_overlap_pillar(xp_new):
                    still_supported = True
            elif ground_surface_y == GROUND_Y:
                if xp_new + 14 >= SAFE_X_MIN:
                    still_supported = True
            
            if not still_supported:
                on_ground = False
                vy = 0
                events.append((frame, xp_new, yp, 0, "WALK_OFF"))
        
        else:
            # Airborne
            prev_yp = yp
            vy += GRAVITY
            if vy > MAX_FALL:
                vy = MAX_FALL
            y_f += vy
            x_f += H_SPEED
            
            yp_new = px(y_f)
            xp_new = px(x_f)
            
            # Check wall collision with pillar
            if inside_pillar(xp_new, yp_new):
                # Check if player entered from above (landing) vs from side (wall)
                if prev_yp + 15 <= PILLAR_Y_TOP and yp_new + 15 >= PILLAR_Y_TOP and vy > 0:
                    # Landing on top
                    yp_new = PILLAR_Y_TOP - 15  # 305
                    y_f = yp_new * 256
                    vy = 0
                    on_ground = True
                    ground_surface_y = PILLAR_Y_TOP
                    ground_x1 = PILLAR_X1
                    ground_x2 = PILLAR_X2
                    events.append((frame, xp_new, yp_new, 0, "LAND_PILLAR"))
                else:
                    # Wall collision (death in GD mechanics)
                    events.append((frame, xp_new, yp_new, vy, "WALL_DEATH"))
                    break
                continue
            
            # Check landing on pillar (from above, not already inside)
            if vy > 0 and horiz_overlap_pillar(xp_new):
                if prev_yp + 15 < PILLAR_Y_TOP and yp_new + 15 >= PILLAR_Y_TOP:
                    yp_new = PILLAR_Y_TOP - 15  # 305
                    y_f = yp_new * 256
                    vy = 0
                    on_ground = True
                    ground_surface_y = PILLAR_Y_TOP
                    ground_x1 = PILLAR_X1
                    ground_x2 = PILLAR_X2
                    events.append((frame, xp_new, yp_new, 0, "LAND_PILLAR"))
                    continue
            
            # Check landing on ground
            if vy > 0 and prev_yp + 15 < GROUND_Y and yp_new + 15 >= GROUND_Y:
                if xp_new + 14 >= SAFE_X_MIN:
                    yp_new = GROUND_Y - 15  # 369
                    y_f = yp_new * 256
                    vy = 0
                    on_ground = True
                    ground_surface_y = GROUND_Y
                    events.append((frame, xp_new, yp_new, 0, "LAND_GROUND"))
                else:
                    events.append((frame, xp_new, yp_new, vy, "DEATH_SPIKES"))
                    break
                continue
    
    return events, coin_hit


def sim_ground_to_pad(start_x_px):
    """
    Simulate player walking on ground from start_x_px at Y=369,
    hitting yellow pad, then flying upward.
    Returns (events, coin_hit).
    """
    x_f = start_x_px * 256
    y_f = 369 * 256
    vy = 0
    
    # Walk until pad
    events = []
    pad_frame = -1
    for frame in range(500):
        xp = px(x_f)
        x_f += H_SPEED
        xp_new = px(x_f)
        
        if horiz_overlap_pad(xp_new):
            pad_frame = frame
            events.append((frame, xp_new, 369, PAD_VEL, "PAD_ACTIVATE"))
            break
        
        if xp_new > 9600:
            return events, False
    
    if pad_frame < 0:
        return events, False
    
    # Now airborne with PAD_VEL from (x_f, y_f=369*256)
    vy = PAD_VEL
    coin_hit = False
    going_up = True
    
    for frame in range(pad_frame + 1, pad_frame + 300):
        xp = px(x_f)
        yp = px(y_f)
        
        # Check coin
        if not coin_hit and check_coin(xp, yp):
            events.append((frame, xp, yp, vy, "COIN_HIT"))
            coin_hit = True
        
        if xp > 9600:
            break
        
        # Physics
        prev_yp = yp
        vy += GRAVITY
        if vy > MAX_FALL:
            vy = MAX_FALL
        y_f += vy
        x_f += H_SPEED
        
        yp_new = px(y_f)
        xp_new = px(x_f)
        
        # Check if hit ground on way down
        if vy > 0 and prev_yp + 15 < GROUND_Y and yp_new + 15 >= GROUND_Y:
            yp_new = 369
            y_f = 369 * 256
            vy = 0
            events.append((frame, xp_new, yp_new, 0, "LAND_GROUND"))
            break
        
        # Check pillar collision on way up or down
        if inside_pillar(xp_new, yp_new):
            events.append((frame, xp_new, yp_new, vy, "PILLAR_COLLISION"))
            break
    
    return events, coin_hit


# ================================================================
# DETAILED FRAME-BY-FRAME for specific scenarios
# ================================================================
def detailed_sim(start_x_px, start_y_px, start_vy, label, max_frames=60):
    """Print detailed frame-by-frame trajectory."""
    x_f = start_x_px * 256
    y_f = start_y_px * 256
    vy = start_vy
    
    print(f"\n{'='*70}")
    print(f"DETAILED: {label}")
    print(f"Start: X={start_x_px}, Y={start_y_px}, VelY={start_vy} ({start_vy/256:.4f} px/f)")
    print(f"{'Frame':>5} {'X_px':>7} {'Y_px':>7} {'VelY':>7} {'Y+1':>5} {'Y+15':>5} {'CoinY?':>6} {'CoinX?':>6} {'Coin?':>5}")
    print(f"{'-'*70}")
    
    for frame in range(max_frames):
        xp = px(x_f)
        yp = px(y_f)
        
        pt, pb = yp + 1, yp + 15
        pl, pr = xp + 1, xp + 14
        
        coin_y = "Y" if pt <= COIN_Y2 and pb >= COIN_Y1 else ""
        coin_x = "Y" if pl <= COIN_X2 and pr >= COIN_X1 else ""
        coin = "***" if check_coin(xp, yp) else ""
        
        print(f"{frame:>5} {xp:>7} {yp:>7} {vy:>7} {pt:>5} {pb:>5} {coin_y:>6} {coin_x:>6} {coin:>5}")
        
        if xp > 9550:
            break
        
        # Physics
        vy += GRAVITY
        if vy > MAX_FALL:
            vy = MAX_FALL
        y_f += vy
        x_f += H_SPEED


# ================================================================
# RUN ALL SCENARIOS
# ================================================================
print("=" * 70)
print("POLARGEIST COIN 2 TRAJECTORY ANALYSIS")
print("=" * 70)
print(f"Coin hitbox: X=[{COIN_X1},{COIN_X2}], Y=[{COIN_Y1},{COIN_Y2}]")
print(f"Player hitbox: left=X+1, right=X+14, top=Y+1, bottom=Y+15")
print(f"Pillar: X=[{PILLAR_X1},{PILLAR_X2}], surface Y={PILLAR_Y_TOP}")
print(f"Pad: X=[{PAD_X1},{PAD_X2}], Y={PAD_Y}")
print(f"H_SPEED={H_SPEED} ({H_SPEED/256:.6f} px/frame)")
print(f"GRAVITY={GRAVITY}, JUMP_VEL={JUMP_VEL}, PAD_VEL={PAD_VEL}")

# ================================================================
# SCENARIO 1: Walk off platform at Y=257, VelY=0
# ================================================================
print("\n" + "=" * 70)
print("SCENARIO 1: Walk off platform at Y=257, VelY=0 (falling)")
print("=" * 70)

all_hits = []
for sx in range(9364, 9411, 3):
    events, coin = simulate(sx, 257, 0)
    if events:
        ev_summary = "; ".join(f"F{e[0]}:{e[4]} X={e[1]} Y={e[2]}" for e in events)
        if coin:
            print(f"  StartX={sx}: *** COIN *** {ev_summary}")
            all_hits.append(("walkoff_257", sx, events))
        else:
            print(f"  StartX={sx}: {ev_summary}")

# ================================================================
# SCENARIO 2: Jump from platform at Y=257, VelY=-1424
# ================================================================
print("\n" + "=" * 70)
print("SCENARIO 2: Jump from platform at Y=257, VelY=-1424")
print("=" * 70)

for sx in range(9364, 9401, 3):
    events, coin = simulate(sx, 257, JUMP_VEL)
    if events:
        ev_summary = "; ".join(f"F{e[0]}:{e[4]} X={e[1]} Y={e[2]}" for e in events)
        if coin:
            print(f"  StartX={sx}: *** COIN *** {ev_summary}")
            all_hits.append(("jump_257", sx, events))
        else:
            print(f"  StartX={sx}: {ev_summary}")

# ================================================================
# SCENARIO 3: Ground level to yellow pad
# ================================================================
print("\n" + "=" * 70)
print("SCENARIO 3: Ground level Y=369 walking to yellow pad")
print("=" * 70)

for sx in range(9376, 9472, 2):
    events, coin = sim_ground_to_pad(sx)
    if events:
        ev_summary = "; ".join(f"F{e[0]}:{e[4]} X={e[1]} Y={e[2]}" for e in events)
        if coin:
            print(f"  StartX={sx}: *** COIN *** {ev_summary}")
            all_hits.append(("pad_ground", sx, events))
        else:
            short = "; ".join(f"F{e[0]}:{e[4]} X={e[1]} Y={e[2]}" for e in events[:3])
            if "COIN" in ev_summary:
                print(f"  StartX={sx}: {ev_summary}")

# Also try walking off pillar (start at Y=305 on pillar)
print("\n" + "=" * 70)
print("SCENARIO 4: Walk off pillar at Y=305")
print("=" * 70)

for sx in range(9424, 9456, 1):
    events, coin = simulate(sx, 305, 0)
    if events:
        ev_summary = "; ".join(f"F{e[0]}:{e[4]} X={e[1]} Y={e[2]}" for e in events)
        if coin:
            print(f"  StartX={sx}: *** COIN *** {ev_summary}")
            all_hits.append(("walkoff_pillar", sx, events))
        else:
            # Only print walk-off and landing events
            pass

# Scenario 4b: What if player lands on pillar, walks off, falls through coin area?
# Let's trace key walkoff positions from pillar right edge
print("\n--- Pillar walk-off detail ---")
for sx in range(9438, 9456):
    events, coin = simulate(sx, 305, 0)
    evs = [(e[0], e[4], e[1], e[2]) for e in events]
    label = "COIN!" if coin else ""
    print(f"  X={sx}: {evs} {label}")

# ================================================================
# SCENARIO 5: From staircase top Y=241, walk off or jump
# ================================================================
print("\n" + "=" * 70)
print("SCENARIO 5: Walk off staircase top at Y=241")
print("=" * 70)

for sx in range(9430, 9460, 2):
    events, coin = simulate(sx, 241, 0)
    if events:
        ev_summary = "; ".join(f"F{e[0]}:{e[4]} X={e[1]} Y={e[2]}" for e in events)
        if coin:
            print(f"  StartX={sx}: *** COIN *** {ev_summary}")
            all_hits.append(("walkoff_stair", sx, events))
        else:
            print(f"  StartX={sx}: {ev_summary}")

print("\n" + "=" * 70)
print("SCENARIO 6: Jump from staircase top at Y=241")
print("=" * 70)

for sx in range(9420, 9456, 2):
    events, coin = simulate(sx, 241, JUMP_VEL)
    if events:
        ev_summary = "; ".join(f"F{e[0]}:{e[4]} X={e[1]} Y={e[2]}" for e in events)
        if coin:
            print(f"  StartX={sx}: *** COIN *** {ev_summary}")
            all_hits.append(("jump_stair", sx, events))
        else:
            print(f"  StartX={sx}: {ev_summary}")

# ================================================================
# DETAILED pad trajectory
# ================================================================
# Try specific starting X for ground->pad
print("\n" + "=" * 70)
print("SCENARIO 3 DETAILED: Ground to pad frame-by-frame")
print("=" * 70)

# Find X where pad activates
for test_start in [9430, 9440, 9450, 9458]:
    x_f = test_start * 256
    for f in range(200):
        xp = px(x_f)
        if horiz_overlap_pad(xp):
            print(f"\n  Start={test_start}: Pad activates at frame {f}, X_px={xp}, X_fixed={x_f} (0x{x_f:X})")
            # Now show trajectory after pad
            detailed_sim(xp, 369, PAD_VEL, f"Pad at X={xp} Y=369", max_frames=30)
            break
        x_f += H_SPEED

# ================================================================
# SUMMARY
# ================================================================
print("\n" + "=" * 70)
print("SUMMARY: ALL TRAJECTORIES THAT COLLECT COIN 2")
print("=" * 70)
if all_hits:
    for h in all_hits:
        scenario, sx = h[0], h[1]
        evs = h[2]
        coin_ev = [e for e in evs if e[4] == "COIN_HIT"]
        if coin_ev:
            ce = coin_ev[0]
            print(f"  {scenario}: startX={sx} -> COIN at frame {ce[0]}, X={ce[1]}, Y={ce[2]}, VelY={ce[3]}")
else:
    print("  NO coin hits found with current parameters!")
    print("\n  Analyzing why...")
    # Show nearest approaches
    print("\n  Pad trajectory check:")
    x_f = 9458 * 256
    y_f = 369 * 256
    vy = PAD_VEL
    for frame in range(20):
        xp = px(x_f)
        yp = px(y_f)
        pt, pb = yp + 1, yp + 15
        pl, pr = xp + 1, xp + 14
        coin_x_ok = pl <= COIN_X2 and pr >= COIN_X1
        coin_y_ok = pt <= COIN_Y2 and pb >= COIN_Y1
        coin = check_coin(xp, yp)
        dist_x = max(COIN_X1 - pr, pl - COIN_X2, 0)
        dist_y = max(COIN_Y1 - pb, pt - COIN_Y2, 0)
        print(f"    F{frame}: X={xp} Y={yp} vy={vy} top={pt} bot={pb} L={pl} R={pr} " +
              f"coinX={coin_x_ok} coinY={coin_y_ok} coin={coin} dX={dist_x} dY={dist_y}")
        
        vy += GRAVITY
        if vy > MAX_FALL: vy = MAX_FALL
        y_f += vy
        x_f += H_SPEED
