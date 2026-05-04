import os, sys
fp = os.path.join(os.environ['TEMP'], 'famidash_pf_trace.csv')
with open(fp, 'rb') as fh:
    data = fh.read()
if data[:2] == b'\xff\xfe':
    data = data.decode('utf-16').encode('utf-8')
text = data.decode('utf-8', errors='replace')
lines = text.split('\n')
hdr = lines[0].rstrip().split(',')
in_idx = hdr.index('input')
out_lines = []
for ln in lines[1:]:
    parts = ln.rstrip().split(',')
    if not parts or not parts[0].isdigit(): continue
    inp = int(parts[in_idx])
    out_lines.append('|..|.A......|........' if inp else '|..|........|........')
with open('everyend_tas.txt', 'w') as fh:
    fh.write('\n'.join(out_lines))
print(f'wrote {len(out_lines)} TAS frames')
print(f'jump frames: {sum(1 for l in out_lines if "A" in l)}')
