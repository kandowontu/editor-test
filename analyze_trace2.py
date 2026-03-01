import os, csv

trace_path = os.path.join(os.environ['TEMP'], 'famidash_pf_trace.csv')
out_path = os.path.join(os.environ['TEMP'], 'trace_analysis.txt')

with open(trace_path, 'r') as f:
    reader = csv.DictReader(f)
    rows = list(reader)

lines = []
lines.append(f"Total frames: {len(rows)}")

# Coin 2: col 592, X=9472, hitbox Y=351-367
# Coin 3: col 719, X=11504, hitbox Y=175-191

lines.append("\n=== Frames near Coin 2 (X=9430-9510) ===")
for row in rows:
    x = int(row['X_px'])
    if 9430 <= x <= 9510:
        lines.append(f"  frame={row['frame']} X={x} Y={row['Y_px']} VelY={row['VelY_fixed']} input={row['input']} ground={row['onGround']}")

lines.append("\n=== Frames near Coin 3 (X=11460-11550) ===")
for row in rows:
    x = int(row['X_px'])
    if 11460 <= x <= 11550:
        lines.append(f"  frame={row['frame']} X={x} Y={row['Y_px']} VelY={row['VelY_fixed']} input={row['input']} ground={row['onGround']}")

# Wider range for coin 2 approach
lines.append("\n=== Approach to Coin 2 (X=9350-9520) ===")
for row in rows:
    x = int(row['X_px'])
    if 9350 <= x <= 9520:
        lines.append(f"  frame={row['frame']} X={x} Y={row['Y_px']} VelY={row['VelY_fixed']} input={row['input']} ground={row['onGround']}")

# Wider range for coin 3 approach
lines.append("\n=== Approach to Coin 3 (X=11300-11550) ===")
for row in rows:
    x = int(row['X_px'])
    if 11300 <= x <= 11550:
        lines.append(f"  frame={row['frame']} X={x} Y={row['Y_px']} VelY={row['VelY_fixed']} input={row['input']} ground={row['onGround']}")

with open(out_path, 'w') as f:
    f.write('\n'.join(lines))

print(f"Analysis written to {out_path}")
print(f"Total lines: {len(lines)}")
