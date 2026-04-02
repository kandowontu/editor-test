import os
path = os.path.expandvars(r'%TEMP%\bfs_diag45.txt')
size = os.path.getsize(path) if os.path.exists(path) else 0

with open(r'c:\Editor Test\diag45_summary.txt', 'w') as out:
    out.write(f'File: {path}\nSize: {size}\n\n')
    if size == 0:
        out.write('FILE IS EMPTY\n')
    else:
        die_lines = {}
        fron_lines = []
        with open(path, 'r', encoding='utf-16-le', errors='replace') as f:
            for line in f:
                s = line.strip().replace('\ufeff', '')
                if 'BFS_DIE45' in s:
                    die_lines[s] = die_lines.get(s, 0) + 1
                elif 'BFS_FRON45' in s:
                    fron_lines.append(s)

        out.write(f'Unique DIE45 lines: {len(die_lines)}\n')
        out.write(f'Total DIE45 count: {sum(die_lines.values())}\n')
        for k in sorted(die_lines.keys()):
            out.write(f'  [{die_lines[k]:5d}x] {k}\n')

        out.write(f'\nFRON45 lines ({len(fron_lines)}):\n')
        for l in fron_lines:
            out.write(f'  {l}\n')
