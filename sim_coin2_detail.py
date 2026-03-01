#!/usr/bin/env python3
"""Detailed trajectory for jump from platform and pad launch."""

GRAVITY = 107
JUMP_VEL = -1424
PAD_VEL = -1984
MAX_FALL = 1536
H_SPEED = 708

COIN_X1, COIN_X2 = 9472, 9488
COIN_Y1, COIN_Y2 = 351, 367
PILLAR_X1, PILLAR_X2 = 9424, 9455
PILLAR_Y_TOP = 320
GROUND_Y = 384

def check_coin(xp, yp):
    pl, pr = xp + 1, xp + 14
    pt, pb = yp + 1, yp + 15
    return pl <= COIN_X2 and pr >= COIN_X1 and pt <= COIN_Y2 and pb >= COIN_Y1

def in_pillar(xp, yp):
    pl, pr = xp + 1, xp + 14
    if pr < PILLAR_X1 or pl > PILLAR_X2:
        return False
    return yp + 15 > PILLAR_Y_TOP and yp + 1 < GROUND_Y

# ===== JUMP FROM Y=257, X=9364 =====
print("=" * 90)
print("JUMP TRAJECTORY: X=9364, Y=257, VelY=-1424")
print("=" * 90)
print(f"{'F':>3} {'X_px':>6} {'Y_px':>6} {'VelY':>6} {'VelY_hex':>10} {'pL':>5} {'pR':>5} "
      f"{'pT':>5} {'pB':>5} {'InPillar':>8} {'CoinX':>5} {'CoinY':>5} {'COIN':>5}")
print("-" * 90)

x_f = 9364 * 256
y_f = 257 * 256
vy = JUMP_VEL

coin_frames = []
for frame in range(50):
    xp = x_f >> 8
    yp = y_f >> 8
    pl, pr = xp + 1, xp + 14
    pt, pb = yp + 1, yp + 15
    pil = "YES" if in_pillar(xp, yp) else ""
    cx = "Y" if pl <= COIN_X2 and pr >= COIN_X1 else ""
    cy = "Y" if pt <= COIN_Y2 and pb >= COIN_Y1 else ""
    coin = "***" if check_coin(xp, yp) else ""
    
    if coin:
        coin_frames.append(frame)
    
    vy_hex = f"0x{vy & 0xFFFF:04X}" if vy >= 0 else f"-0x{(-vy) & 0xFFFF:04X}"
    
    # Only print interesting frames (near peak, near pillar, near coin)
    show = (frame <= 2 or (11 <= frame <= 16) or (20 <= frame <= 35) or frame >= 37)
    if show:
        print(f"{frame:>3} {xp:>6} {yp:>6} {vy:>6} {vy_hex:>10} {pl:>5} {pr:>5} "
              f"{pt:>5} {pb:>5} {pil:>8} {cx:>5} {cy:>5} {coin:>5}")
    
    # Physics
    vy += GRAVITY
    if vy > MAX_FALL:
        vy = MAX_FALL
    y_f += vy
    x_f += H_SPEED

print(f"\nCoin hit frames: {coin_frames}")
if coin_frames:
    print(f"Coin overlap window: frames {coin_frames[0]}-{coin_frames[-1]} ({len(coin_frames)} frames)")

# ===== Check all jump start X values more precisely =====
print("\n" + "=" * 90)
print("JUMP COIN COLLECTION: Range of valid startX")
print("=" * 90)
print(f"{'StartX':>7} {'CoinFrames':>30} {'FirstHit_X':>10} {'FirstHit_Y':>10}")
print("-" * 60)

for sx in range(9355, 9410):
    x_f = sx * 256
    y_f = 257 * 256
    vy = JUMP_VEL
    cframes = []
    cx_first = cy_first = None
    dead = False
    
    for frame in range(50):
        xp = x_f >> 8
        yp = y_f >> 8
        
        if in_pillar(xp, yp):
            dead = True
            break
        
        if check_coin(xp, yp):
            cframes.append(frame)
            if cx_first is None:
                cx_first = xp
                cy_first = yp
        
        vy += GRAVITY
        if vy > MAX_FALL:
            vy = MAX_FALL
        y_f += vy
        x_f += H_SPEED
    
    if cframes or dead:
        status = "DEAD(pillar)" if dead else f"{cframes}"
        fx = cx_first if cx_first else ""
        fy = cy_first if cy_first else ""
        print(f"{sx:>7} {str(status):>30} {str(fx):>10} {str(fy):>10}")

# ===== PAD detailed with overlap window =====
print("\n" + "=" * 90)
print("PAD TRAJECTORY: Pad activates at X=9458, Y=369, VelY=-1984")
print("=" * 90)
print(f"{'F':>3} {'X_px':>6} {'Y_px':>6} {'VelY':>6} {'pL':>5} {'pR':>5} "
      f"{'pT':>5} {'pB':>5} {'CoinX':>5} {'CoinY':>5} {'COIN':>5}")
print("-" * 80)

x_f = 9458 * 256
y_f = 369 * 256
vy = PAD_VEL
coin_frames_pad = []

for frame in range(40):
    xp = x_f >> 8
    yp = y_f >> 8
    pl, pr = xp + 1, xp + 14
    pt, pb = yp + 1, yp + 15
    cx = "Y" if pl <= COIN_X2 and pr >= COIN_X1 else ""
    cy = "Y" if pt <= COIN_Y2 and pb >= COIN_Y1 else ""
    coin = "***" if check_coin(xp, yp) else ""
    if coin:
        coin_frames_pad.append(frame)
    
    if frame <= 10 or (25 <= frame <= 35):
        print(f"{frame:>3} {xp:>6} {yp:>6} {vy:>6} {pl:>5} {pr:>5} "
              f"{pt:>5} {pb:>5} {cx:>5} {cy:>5} {coin:>5}")
    
    vy += GRAVITY
    if vy > MAX_FALL:
        vy = MAX_FALL
    y_f += vy
    x_f += H_SPEED

print(f"\nPad coin hit frames: {coin_frames_pad}")
if coin_frames_pad:
    print(f"Coin overlap window: frames {coin_frames_pad[0]}-{coin_frames_pad[-1]} ({len(coin_frames_pad)} frames)")

# ===== Can Scenario 1 (walkoff) -> pad work? =====
print("\n" + "=" * 90)
print("SCENARIO 1 -> PAD: Walk off platform, land on ground, reach pad?")
print("=" * 90)
# After landing on ground from pillar walkoff, player is at X~9504-9507
# Pad is at X=9472. Player is PAST the pad. Cannot reach it.
print("Player lands on ground at X≈9504-9507 (from pillar walkoff)")
print("Pad is at X=9472-9487. Player is PAST the pad → CANNOT use pad from this path.")

# ===== What if player falls directly to ground (no pillar)? =====
print("\n" + "=" * 90)
print("DIRECT FALL TO GROUND (bypassing pillar)?")
print("=" * 90)
# Player needs to reach ground level while X < pillar start (9424)
# Or need to be at X > pillar end (9455) when reaching pillar Y level

# From Y=257, VelY=0: how many frames to reach Y+15 >= 320?
x_f = 9364 * 256
y_f = 257 * 256
vy = 0
for frame in range(50):
    xp = x_f >> 8
    yp = y_f >> 8
    if yp + 15 >= PILLAR_Y_TOP:
        print(f"Player reaches pillar surface level at frame {frame}: X={xp}, Y={yp}, bottom={yp+15}")
        break
    vy += GRAVITY
    if vy > MAX_FALL:
        vy = MAX_FALL
    y_f += vy
    x_f += H_SPEED

print("Pillar X range: 9424-9455")
print(f"Player X at that frame: {xp}. Player right edge: {xp+14}")
print(f"Since player right ({xp+14}) reaches pillar ({PILLAR_X1}) → {'HITS PILLAR' if xp+14 >= PILLAR_X1 else 'MISSES PILLAR'}")

# ===== Determine exact viable paths =====
print("\n" + "=" * 90)
print("WALKOFF FROM PILLAR: Exact X at walk-off edge")
print("=" * 90)
# Player walks on pillar until X+1 > 9455 (= PILLAR_X2)
# Walk-off X where X+1 = 9456 → X = 9455
# From pillar Y=305, VelY=0, X=9455:
x_f = 9455 * 256
y_f = 305 * 256
vy = 0
print(f"{'F':>3} {'X_px':>6} {'Y_px':>6} {'VelY':>6} {'pL':>5} {'pR':>5} {'pB':>5} {'CoinX':>5} {'CoinY':>5} {'COIN':>5}")
for frame in range(25):
    xp = x_f >> 8
    yp = y_f >> 8
    pl, pr = xp + 1, xp + 14
    pb = yp + 15
    cx = "Y" if pl <= COIN_X2 and pr >= COIN_X1 else ""
    cy = "Y" if yp+1 <= COIN_Y2 and pb >= COIN_Y1 else ""
    coin = "***" if check_coin(xp, yp) else ""
    
    print(f"{frame:>3} {xp:>6} {yp:>6} {vy:>6} {pl:>5} {pr:>5} {pb:>5} {cx:>5} {cy:>5} {coin:>5}")
    
    vy += GRAVITY
    if vy > MAX_FALL: vy = MAX_FALL
    y_f += vy
    x_f += H_SPEED

print("\nPlayer walks off pillar at X=9455. By the time Y reaches coin range,")
print("player left edge = X+1 is past coin right edge (9488). MISSES by ~1px.")

# Check what walk-off X would be needed
print("\n--- What pillar right edge X would allow coin collection? ---")
for test_walkoff_x in range(9448, 9458):
    x_f = test_walkoff_x * 256
    y_f = 305 * 256
    vy = 0
    hit = False
    for frame in range(25):
        xp = x_f >> 8
        yp = y_f >> 8
        if check_coin(xp, yp):
            hit = True
            print(f"  Walkoff X={test_walkoff_x}: COIN at frame {frame}, X={xp}, Y={yp}")
            break
        vy += GRAVITY
        if vy > MAX_FALL: vy = MAX_FALL
        y_f += vy
        x_f += H_SPEED
    if not hit:
        print(f"  Walkoff X={test_walkoff_x}: no coin (too far right)")
