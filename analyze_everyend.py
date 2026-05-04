import re, os, csv

replay_path = os.path.expanduser('~/Documents/Famidash Editor/Replays/everyend/famidash_replay.csv')
nes_path = os.path.expanduser('~/Documents/Famidash Editor/Replays/everyend/famidash_mesen_trace.csv')

# Load replay (sim rows): frame -> (x,y,a)
replay = {}
y_off_replay = 0
with open(replay_path, newline='') as f:
    for line in f:
        s = line.strip()
        if s.startswith('nes_y_offset'):
            y_off_replay = int(s.split(',')[1])
        elif not s.startswith('frame') and s:
            p = s.split(',')
            if len(p) >= 4:
                frame,x,y,a = int(p[0]),int(p[1]),int(p[2]),int(p[3])
                replay[frame] = (x,y,a)

# Load NES trace
y_off = 0
nes_rows = []
with open(nes_path, newline='') as f:
    lines = f.readlines()
    if lines[0].startswith('nes_y_offset'):
        y_off = int(lines[0].strip().split(',')[1])
        lines = lines[1:]
    reader = csv.DictReader(lines)
    attempt = 0
    for row in reader:
        rf = row.get('rom_frame','').strip()
        if not rf or rf.startswith('#'):
            attempt += 1
            continue
        try:
            sc = int(row['sim_cursor'])
            px_raw = int(row['px'])
            px = px_raw if px_raw < 2**31 else px_raw - 2**32
            py = int(row['py'])
            vel_y = int(row.get('vel_y','0'))
            gamemode = int(row.get('gamemode','0'))
            nes_rows.append({'rf': int(rf), 'sc': sc, 'px': px, 'py': py, 'vel_y': vel_y, 'gamemode': gamemode, 'attempt': attempt})
        except Exception as e:
            pass

print(f'Replay frames: {len(replay)}, NES rows: {len(nes_rows)}, y_off={y_off}')
print(f'Replay y_off_replay={y_off_replay}')

# Group by attempt, pick best
from itertools import groupby
by_attempt = {}
for row in nes_rows:
    by_attempt.setdefault(row['attempt'], []).append(row)

best_attempt = max(by_attempt, key=lambda a: max(r['sc'] for r in by_attempt[a]))
rom = by_attempt[best_attempt]
print(f'Using attempt {best_attempt} with {len(rom)} rows, max_sc={max(r["sc"] for r in rom)}')

# Compare
all_rows = []
for r in rom:
    sc0 = r['sc'] - 1  # 1-indexed -> 0-indexed
    if sc0 not in replay:
        continue
    sim_x, sim_y, sim_a = replay[sc0]
    nes_py_world = r['py'] + y_off
    dx = r['px'] - sim_x
    dy = nes_py_world - sim_y
    all_rows.append((r['rf'], sc0, r['px'], nes_py_world, sim_x, sim_y, dx, dy, r['vel_y'], r['gamemode']))

print(f'Compared: {len(all_rows)} rows')

real_bad = [(rf,sf,rx,ry,sx,sy,dx,dy,vy,gm) for rf,sf,rx,ry,sx,sy,dx,dy,vy,gm in all_rows if rf>=13 and (abs(dx)>1 or abs(dy)>1)]
print(f'Divergences (rf>=13, |d|>1): {len(real_bad)}')
print('\nFirst 30 divergent:')
for row in real_bad[:30]:
    rf,sf,rx,ry,sx,sy,dx,dy,vy,gm = row
    print(f'  rf={rf} sf={sf} nes=({rx},{ry}) sim=({sx},{sy}) dx={dx} dy={dy} nes_vel_y={vy} mode={gm}')

print('\nLast 30 of all_rows:')
for row in all_rows[-30:]:
    rf,sf,rx,ry,sx,sy,dx,dy,vy,gm = row
    print(f'  rf={rf} sf={sf} nes=({rx},{ry}) sim=({sx},{sy}) dx={dx} dy={dy} nes_vel_y={vy}')
