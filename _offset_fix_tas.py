import os

temp = os.environ['TEMP']
src = os.path.join(temp, 'nightmare_from_replay.tas')
dst = os.path.join(temp, 'nightmare_from_replay_offset_fixed.tas')

# Read original
with open(src, 'r') as f:
    lines = f.readlines()

# Shift all inputs forward by 1 frame
# Start with a blank frame (no input)
new_lines = ['|..|........|........\n']

# Copy all but last line
for i in range(len(lines) - 1):
    new_lines.append(lines[i])

# Append a blank frame at end
new_lines.append('|..|........|........\n')

# Write corrected version
with open(dst, 'w') as f:
    f.writelines(new_lines)

print(f"Offset-corrected TAS written: {len(new_lines)} lines (was {len(lines)})")
print(f"Sample: Frame 77-79:")
for i in [77, 78, 79]:
    if i < len(new_lines):
        print(f"  Frame {i}: {new_lines[i].strip()}")
