import os, csv

trace_path = os.path.join(os.environ['TEMP'], 'famidash_pf_trace.csv')
with open(trace_path, 'r') as f:
    reader = csv.DictReader(f)
    rows = list(reader)

print(f"Total frames: {len(rows)}")
print(f"Header: {list(rows[0].keys())}")

# Coin 2: X~9472, SpriteID 0x1A, hitbox Y=351-367
# Coin 3: X~11504, SpriteID 0x1B, hitbox Y=175-191
COIN2_X = 9472
COIN3_X = 11504

print("\n=== Frames near Coin 2 (X=9450-9510) ===")
for row in rows:
    x = int(row['X_px'])
    if 9450 <= x <= 9510:
        print(f"  frame={row['frame']} X={x} Y={row['Y_px']} VelY={row['VelY_fixed']} input={row['input']} ground={row['onGround']}")

print("\n=== Frames near Coin 3 (X=11470-11540) ===")
for row in rows:
    x = int(row['X_px'])
    if 11470 <= x <= 11540:
        print(f"  frame={row['frame']} X={x} Y={row['Y_px']} VelY={row['VelY_fixed']} input={row['input']} ground={row['onGround']}")

# Also check what Y values the player had at the coin X ranges
print("\n=== Player Y at coin 2 X range (9460-9490) ===")
for row in rows:
    x = int(row['X_px'])
    if 9460 <= x <= 9490:
        y = int(row['Y_px'])
        print(f"  X={x} Y={y}  (coin hitbox Y=351-367, need Y in 336-366 for overlap)")

print("\n=== Player Y at coin 3 X range (11490-11520) ===")
for row in rows:
    x = int(row['X_px'])
    if 11490 <= x <= 11520:
        y = int(row['Y_px'])
        print(f"  X={x} Y={y}  (coin hitbox Y=175-191, need Y in 160-190 for overlap)")
