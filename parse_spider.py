import re, os, sys
path = os.path.expandvars(r'%TEMP%\bfs_spider.txt')
if not os.path.exists(path):
    print(f'ERROR: {path} not found')
    sys.exit(1)
print(f'Reading {path} ({os.path.getsize(path)} bytes)', file=sys.stderr)
entries = {}
results = {}
total = 0
with open(path, 'r', encoding='utf-16-le', errors='replace') as f:
    for line in f:
        if 'SPIDER_TELEPORT' not in line:
            continue
        total += 1
        s = line.strip().replace('\ufeff', '').replace('\ufffe', '')
        if 'goUp' in s:
            entries[s] = entries.get(s, 0) + 1
        elif 'result' in s:
            results[s] = results.get(s, 0) + 1

out_path = r'c:\Editor Test\spider_summary.txt'
with open(out_path, 'w', encoding='utf-8') as out:
    out.write(f'Total spider lines: {total}\n\n')
    out.write(f'Unique ENTRY lines ({len(entries)}):\n')
    for e in sorted(entries.keys()):
        out.write(f'  [{entries[e]:5d}x] {e}\n')
    out.write(f'\nUnique RESULT lines ({len(results)}):\n')
    for r in sorted(results.keys()):
        out.write(f'  [{results[r]:5d}x] {r}\n')
print(f'Written to {out_path}', file=sys.stderr)
