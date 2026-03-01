import csv

TRACE_PATH = r'C:\Users\p_j_9\AppData\Local\Temp\famidash_pf_trace.csv'

with open(TRACE_PATH) as f:
    reader = csv.DictReader(f)
    rows = list(reader)

print(f"Total frames: {len(rows)}")

# Find Y transitions (delta > 2)
print("\n--- Y value transitions (delta > 2) ---")
prev_y = None
for r in rows:
    y = int(r['Y_px'])
    x = int(r['X_px'])
    frame = int(r['frame'])
    if prev_y is not None and abs(y - prev_y) > 2:
        print(f"  Frame {frame}: X={x}, Y: {prev_y} -> {y} (delta={y-prev_y})")
    prev_y = y

# Trajectory from X=7900 to end
print("\n--- Full trajectory X=7900 to end ---")
last_printed_x = 0
for r in rows:
    x = int(r['X_px'])
    y = int(r['Y_px'])
    frame = int(r['frame'])
    inp = r['input']
    alive = r['alive']
    on_ground = r['onGround']
    if x >= 7900 and (x - last_printed_x >= 15 or x >= 9050):
        print(f"  F{frame:5d} X={x:5d} Y={y:3d} inp={inp} alive={alive} gnd={on_ground}")
        last_printed_x = x

# When does Y first go below 369?
print("\n--- When does Y first drop below 369? ---")
for r in rows:
    y = int(r['Y_px'])
    if y < 369:
        print(f"  Frame {int(r['frame'])}: X={int(r['X_px'])}, Y={y}")
        break

# When does Y first reach 337?
print("\n--- When does Y first reach 337? ---")
for r in rows:
    y = int(r['Y_px'])
    if y == 337:
        print(f"  Frame {int(r['frame'])}: X={int(r['X_px'])}, Y={y}")
        break

# Show trajectory from X=5000 to X=8500 (to see staircase climb) sampled every 100px
print("\n--- Trajectory X=5000 to X=8500 (sampled every ~100px) ---")
last_printed_x = 0
for r in rows:
    x = int(r['X_px'])
    y = int(r['Y_px'])
    frame = int(r['frame'])
    if 5000 <= x <= 8500 and x - last_printed_x >= 100:
        print(f"  F{frame:5d} X={x:5d} Y={y:3d} inp={r['input']} gnd={r['onGround']}")
        last_printed_x = x
