import os

temp = os.environ['TEMP']
tas_file = os.path.join(temp, 'nightmare_from_mesen_exact.tas')
mesen_file = os.path.join(temp, 'famidash_mesen_trace.csv')

# Load TAS inputs (0-indexed: line N = frame N+1)
tas_inputs = {}
with open(tas_file, 'r') as f:
    for i, line in enumerate(f):
        if 'A' in line:
            tas_inputs[i+1] = 1  # frame number = line number + 1
        else:
            tas_inputs[i+1] = 0

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

# Find all jumps in each source
tas_jumps = [f for f in sorted(tas_inputs.keys()) if tas_inputs[f] == 1]
mesen_jumps = [f for f in sorted(mesen_inputs.keys()) if mesen_inputs[f] == 1]

print(f"TAS jumps: {len(tas_jumps)} frames")
print(f"Mesen jumps: {len(mesen_jumps)} frames")
print()

if len(tas_jumps) < 30:
    print("TAS jump frames:", tas_jumps)
    print()

if len(mesen_jumps) < 30:
    print("Mesen jump frames:", mesen_jumps)
    print()

# Check for alignment
print("First 20 jumps comparison:")
print("Index | TAS_frame | Mesen_frame | Match")
print("-" * 45)
for i in range(min(20, len(tas_jumps), len(mesen_jumps))):
    tas_f = tas_jumps[i]
    mesen_f = mesen_jumps[i]
    match = "✓" if tas_f == mesen_f else "✗"
    print(f"{i:5d} | {tas_f:9d} | {mesen_f:11d} | {match}")

# Check misalignment pattern
if len(mesen_jumps) > 0:
    diffs = [mesen_jumps[i] - tas_jumps[i] for i in range(min(len(tas_jumps), len(mesen_jumps)))]
    print(f"\nMesen_frame - TAS_frame differences: {set(diffs)}")
