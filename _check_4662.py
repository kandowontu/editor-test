import os
temp = os.environ['TEMP']
mesen_file = os.path.join(temp, 'famidash_mesen_trace.csv')

with open(mesen_file, 'r') as f:
    lines = f.readlines()
    if len(lines) > 1:
        hdr = lines[1].strip().split(',')
        count = 0
        for line in lines[2:]:
            parts = line.strip().split(',')
            if not parts or not parts[0]: continue
            try:
                d = {hdr[i]: parts[i] for i in range(min(len(hdr), len(parts)))}
                s = int(d.get('sim_cursor', -1))
                if s in range(4660, 4666):
                    mode = d.get('gamemode', '?')
                    py = d.get('py', '?')
                    print(f'Sim {s}: mode={mode} py={py}')
                count += 1
            except:
                pass
print(f'Total sims: {count}')
