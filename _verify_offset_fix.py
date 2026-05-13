import os

temp = os.environ['TEMP']
tas_file = os.path.join(temp, 'nightmare_from_replay_offset_fixed.tas')
mesen_file = os.path.join(temp, 'famidash_mesen_trace.csv')

# Load TAS
tas_inputs = {}
with open(tas_file, 'r') as f:
    for frame_num, line in enumerate(f):
        if 'A' in line:
            tas_inputs[frame_num] = 1
        else:
            tas_inputs[frame_num] = 0

# Load Mesen inputs
mesen_inputs = {}
with open(mesen_file, 'r') as f:
    lines = f.readlines()
    if len(lines) > 1:
        hdr = lines[1].strip().split(',')
        for line in lines[2:]:
            parts = line.strip().split(',')
            if not parts or not parts[0]: continue
            try:
                d = {hdr[i]: parts[i] for i in range(min(len(hdr), len(parts)))}
                s = int(d.get('sim_cursor', -1))
                if s > 0:
                    a_cur = int(d.get('a_cur', '0'))
                    mesen_inputs[s] = a_cur
            except:
                pass

# Compare and report
print("Checking offset-corrected TAS vs Mesen (frames 70-85):")
print("Frame | TAS | Mesen | Match")
print("-" * 35)

mismatch_count = 0
for frame in range(70, 86):
    tas_input = tas_inputs.get(frame, 0)
    mesen_input = mesen_inputs.get(frame, 0)
    match = "✓" if tas_input == mesen_input else "✗"
    if tas_input != mesen_input:
        mismatch_count += 1
    print(f"{frame:5d} | {tas_input:3d} | {mesen_input:5d} | {match}")

print()
print("Summary: Checking all frames up to 5133...")
total_mismatches = 0
for frame in range(1, 5200):
    tas_input = tas_inputs.get(frame, 0)
    mesen_input = mesen_inputs.get(frame, 0)
    if tas_input != mesen_input:
        total_mismatches += 1

print(f"Total mismatches in offset-corrected TAS: {total_mismatches}")
