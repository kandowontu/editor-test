import os

temp = os.environ['TEMP']
mesen_file = os.path.join(temp, 'famidash_mesen_trace.csv')
out_tas = os.path.join(temp, 'nightmare_from_mesen_exact.tas')

# Load Mesen trace and extract inputs
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

print(f"Loaded Mesen trace: {len(mesen_inputs)} frames")

# Find max frame
if mesen_inputs:
    max_frame = max(mesen_inputs.keys())
    print(f"Max frame in trace: {max_frame}")
    
    # Generate TAS
    tas_lines = []
    jump_count = 0
    for frame in range(1, max_frame + 1):
        if mesen_inputs.get(frame, 0) == 1:
            tas_lines.append('|..|....A...|........\n')
            jump_count += 1
        else:
            tas_lines.append('|..|........|........\n')
    
    # Write TAS
    with open(out_tas, 'w') as f:
        f.writelines(tas_lines)
    
    print(f"Generated TAS: {len(tas_lines)} lines, {jump_count} jumps")
    print(f"Output: {out_tas}")
    
    # Show samples (note: tas_lines[i] is frame i+1)
    print("\nSample frames around 4295-4305:")
    for f in range(4295, 4306):
        if f-1 < len(tas_lines):
            inp = mesen_inputs.get(f, 0)
            line = tas_lines[f-1].strip()
            mark = " <-- JUMP" if inp == 1 else ""
            print(f"  Frame {f}: {line}{mark}")
