import csv, os

temp = os.environ['TEMP']
mesen_file = os.path.join(temp, 'famidash_mesen_trace.csv')

# Load Mesen trace
mesen = []
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
                    mesen.append((s, d))
            except:
                pass

# Sort by sim_cursor and identify segments (resets)
mesen.sort(key=lambda x: x[0])

# Find reset points (where sim decreases)
segments = []
cur_seg = []
for i, (s, d) in enumerate(mesen):
    if i > 0 and s < mesen[i-1][0]:
        # Reset detected
        if cur_seg:
            segments.append(cur_seg)
        cur_seg = [(s, d)]
    else:
        cur_seg.append((s, d))

if cur_seg:
    segments.append(cur_seg)

print(f"Total rows: {len(mesen)}, Segments: {len(segments)}")
print()

# Analyze each segment
for seg_idx, seg in enumerate(segments):
    min_s = min(s for s, _ in seg)
    max_s = max(s for s, _ in seg)
    print(f"Segment {seg_idx}: sims {min_s}-{max_s} ({len(seg)} rows)")
    
    # Check for input around frame 4292-4300
    for s, d in seg:
        if 4290 <= s <= 4300:
            a_cur = d.get('a_cur', '?')
            if a_cur == '1':
                print(f"  Sim {s}: INPUT FOUND")

print()
print("=" * 80)
print("Which segment reaches sim 5133?")
for seg_idx, seg in enumerate(segments):
    max_s = max(s for s, _ in seg)
    if max_s >= 5133:
        print(f"Segment {seg_idx}: reaches sim {max_s}")
        
        # Check input timing in this segment
        print(f"Input pattern in 4290-4300:")
        seg_dict = {s: d for s, d in seg}
        for s in range(4290, 4301):
            if s in seg_dict:
                a_cur = seg_dict[s].get('a_cur', '?')
                if a_cur == '1':
                    print(f"  Sim {s}: INPUT")
