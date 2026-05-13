import os, csv

temp = os.environ['TEMP']
tas_file = os.path.join(temp, 'nightmare_from_replay.tas')
mesen_file = os.path.join(temp, 'famidash_mesen_trace.csv')

# Load TAS (each line is a frame, check for 'A' character)
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

# Compare
print("Input mismatches (TAS vs Mesen):")
print("Frame | TAS | Mesen | Status")
print("-" * 40)

mismatches = []
for frame in range(1, 5200):
    tas_input = tas_inputs.get(frame, 0)
    mesen_input = mesen_inputs.get(frame, 0)
    
    if tas_input != mesen_input:
        print(f"{frame:5d} | {tas_input:3d} | {mesen_input:5d} | MISMATCH")
        mismatches.append((frame, tas_input, mesen_input))

print()
print(f"Total mismatches found: {len(mismatches)}")
if mismatches:
    print("\nFrames with wrong input:")
    for frame, tas, mesen in mismatches[:20]:
        print(f"  Frame {frame}: TAS={tas}, Mesen={mesen}")
    if len(mismatches) > 20:
        print(f"  ... and {len(mismatches) - 20} more")
