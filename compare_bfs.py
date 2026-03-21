import re

def parse_bfs(path):
    with open(path, 'r', encoding='utf-16-le', errors='replace') as f:
        text = f.read()
    lines = {}
    for m in re.finditer(r'\[BFS\] f=(\d+) front=(\d+) dedup=(\d+) deaths=(\d+) Y=\[([^\]]+)\] mode=(\d+) gravN=(\d+) gravF=(\d+)', text):
        frame = int(m.group(1))
        lines[frame] = {
            'front': int(m.group(2)),
            'dedup': int(m.group(3)),
            'deaths': int(m.group(4)),
            'Y': m.group(5),
            'mode': int(m.group(6)),
            'gravN': int(m.group(7)),
            'gravF': int(m.group(8)),
            'raw': m.group(0)
        }
    return lines

passing = parse_bfs(r'C:\Editor Test\pf_cycles_verify_err.txt')
failing = parse_bfs(r'C:\Editor Test\pf_cycles8_err.txt')

print(f'PASSING frames: {min(passing)}..{max(passing)} ({len(passing)} total)')
print(f'FAILING frames: {min(failing)}..{max(failing)} ({len(failing)} total)')

all_frames = sorted(set(passing.keys()) | set(failing.keys()))
first_diff = None
for f in all_frames:
    if f not in passing:
        print(f'\nFrame {f}: only in FAILING')
        first_diff = f
        break
    if f not in failing:
        print(f'\nFrame {f}: only in PASSING')
        first_diff = f
        break
    p = passing[f]
    fl = failing[f]
    diffs = []
    for key in ['front','dedup','deaths','Y','mode','gravN','gravF']:
        if p[key] != fl[key]:
            diffs.append(f'{key}: PASS={p[key]} FAIL={fl[key]}')
    if diffs:
        if first_diff is None:
            first_diff = f
        print(f'Frame {f}: ' + ', '.join(diffs))
        if f > first_diff + 15:
            print('... (stopping after 15 frames of diffs)')
            break

if first_diff is None:
    print('\nNo differences found in any frame!')
else:
    print(f'\n=== Context around first divergence (frame {first_diff}) ===')
    for f in range(max(0, first_diff-5), first_diff+6):
        if f in passing:
            print(f'PASS f={f}: {passing[f]["raw"]}')
        if f in failing:
            print(f'FAIL f={f}: {failing[f]["raw"]}')
        if f in passing and f in failing:
            p = passing[f]
            fl = failing[f]
            diffs = []
            for key in ['front','dedup','deaths','Y','mode','gravN','gravF']:
                if p[key] != fl[key]:
                    diffs.append(f'{key}: PASS={p[key]} FAIL={fl[key]}')
            if diffs:
                print(f'  ^^^ DIFF: ' + ', '.join(diffs))
