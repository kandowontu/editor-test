import csv
import re

TRACE_PATH = r'C:\Users\p_j_9\AppData\Local\Temp\famidash_pf_trace.csv'
DEBUG_PATH = r'C:\Users\p_j_9\AppData\Local\Temp\famidash_pf_debug_20260228_040329.txt'

# ============================================================
# TRACE ANALYSIS
# ============================================================
print("="*80)
print("TRACE FILE ANALYSIS")
print("="*80)

with open(TRACE_PATH) as f:
    reader = csv.DictReader(f)
    rows = list(reader)

print(f"Total frames: {len(rows)}")
print(f"Columns: {list(rows[0].keys())}")

# Find Y transitions
print("\n--- Y value transitions (delta > 2) ---")
prev_y = None
for r in rows:
    y = int(r['Y_px'])
    x = int(r['X_px'])
    frame = int(r['frame'])
    if prev_y is not None and abs(y - prev_y) > 2:
        print(f"  Frame {frame}: X={x}, Y changed {prev_y} -> {y} (delta={y-prev_y})")
    prev_y = y

# Show trajectory from X=8000 to X=9600
print("\n--- Trajectory X=8000 to X=9600 (sampled every ~30px) ---")
last_printed_x = 0
for r in rows:
    x = int(r['X_px'])
    y = int(r['Y_px'])
    frame = int(r['frame'])
    inp = r['input']
    alive = r['alive']
    on_ground = r['onGround']
    if 8000 <= x <= 9600 and (x - last_printed_x >= 30 or x == 9087):
        print(f"  F{frame:5d} X={x:5d} Y={y:3d} inp={inp} alive={alive} ground={on_ground}")
        last_printed_x = x

# Show last 30 frames
print("\n--- Last 30 frames ---")
for r in rows[-30:]:
    x = int(r['X_px'])
    y = int(r['Y_px'])
    frame = int(r['frame'])
    inp = r['input']
    alive = r['alive']
    on_ground = r['onGround']
    vel_y = r['VelY_fixed']
    print(f"  F{frame:5d} X={x:5d} Y={y:3d} velY={vel_y} inp={inp} alive={alive} ground={on_ground}")

# Find when Y first reaches ~337
print("\n--- When does Y first reach <=340? ---")
for r in rows:
    y = int(r['Y_px'])
    x = int(r['X_px'])
    frame = int(r['frame'])
    if y <= 340:
        print(f"  First: Frame {frame}: X={x}, Y={y}")
        break

# Find all frames near coins
print("\n--- Frames near coin 1 X=6590-6640 ---")
for r in rows:
    x = int(r['X_px'])
    if 6590 <= x <= 6640:
        y = int(r['Y_px'])
        frame = int(r['frame'])
        print(f"  F{frame} X={x} Y={y} alive={r['alive']} gnd={r['onGround']}")

print("\n--- Frames near coin 2 X=9450-9500 ---")
for r in rows:
    x = int(r['X_px'])
    if 9450 <= x <= 9500:
        y = int(r['Y_px'])
        frame = int(r['frame'])
        print(f"  F{frame} X={x} Y={y} alive={r['alive']} gnd={r['onGround']}")

# ============================================================
# DEBUG LOG ANALYSIS
# ============================================================
print("\n" + "="*80)
print("DEBUG LOG ANALYSIS")
print("="*80)

with open(DEBUG_PATH) as f:
    debug_lines = f.readlines()

print(f"Debug log lines: {len(debug_lines)}")

# Find coin-related lines
print("\n--- Coin-related lines ---")
for line in debug_lines:
    if 'coin' in line.lower():
        print(f"  {line.rstrip()}")

# Find mode/portal lines
print("\n--- Mode/portal lines ---")
for line in debug_lines:
    l = line.lower()
    if any(kw in l for kw in ['mode', 'portal', 'ship', 'ball', 'ufo', 'wave', 'robot', 'spider']):
        print(f"  {line.rstrip()}")

# Find death/kill lines
print("\n--- Death/kill lines ---")
for line in debug_lines:
    l = line.lower()
    if any(kw in l for kw in ['death', 'die', 'kill', 'dead', 'CENTER_DEATH']):
        print(f"  {line.rstrip()}")

# Find forgive lines
print("\n--- Forgive lines ---")
for line in debug_lines:
    if 'forgiv' in line.lower() or 'skip' in line.lower() or 'unreachable' in line.lower():
        print(f"  {line.rstrip()}")

# Show first and last lines
print("\n--- First 20 debug lines ---")
for line in debug_lines[:20]:
    print(f"  {line.rstrip()}")

print("\n--- Last 20 debug lines ---")
for line in debug_lines[-20:]:
    print(f"  {line.rstrip()}")
