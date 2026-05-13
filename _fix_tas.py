import os

temp = os.environ['TEMP']
src = os.path.join(temp, 'nightmare_from_replay.tas')
dst = os.path.join(temp, 'nightmare_from_replay_fixed.tas')

# Read original
with open(src, 'r') as f:
    lines = f.readlines()

# Fix: move jump from frame 4292 to 4299
if len(lines) > 4299:
    # Remove jump at line 4292 (0-indexed)
    if lines[4292].strip() == '|..|....A...|........':
        lines[4292] = '|..|........|........\n'
    
    # Add jump at line 4299 (0-indexed)
    if '|..|....A...|........' not in lines[4299]:
        lines[4299] = '|..|....A...|........\n'

# Write fixed version
with open(dst, 'w') as f:
    f.writelines(lines)

print(f"Fixed TAS written: {len(lines)} lines")
print(f"Frame 4292: {lines[4292].strip()}")
print(f"Frame 4299: {lines[4299].strip()}")
