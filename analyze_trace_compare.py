import re, os

path = os.path.join(os.environ['TEMP'], 'famidash_trace_compare.txt')
with open(path) as f:
    lines = f.readlines()

# Print header summary
for line in lines[:15]:
    print(line, end='')
print()

# Find per-frame section
in_data = False
all_rows = []
for line in lines:
    line = line.strip()
    if line.startswith('rom_f, sim_f'):
        in_data = True
        continue
    if not in_data:
        continue
    parts = line.split(',')
    if len(parts) < 6:
        continue
    try:
        rf = int(parts[0])
        sf = int(parts[1])
        m = re.findall(r'\((-?\d+),(-?\d+)\)', line)
        if len(m) < 2:
            continue
        rom_x,rom_y = int(m[0][0]),int(m[0][1])
        sim_x,sim_y = int(m[1][0]),int(m[1][1])
        dx = rom_x - sim_x
        dy = rom_y - sim_y
        all_rows.append((rf,sf,rom_x,rom_y,sim_x,sim_y,dx,dy))
    except:
        pass

print(f'Total rows parsed: {len(all_rows)}')

real_bad = [(rf,sf,rx,ry,sx,sy,dx,dy) for rf,sf,rx,ry,sx,sy,dx,dy in all_rows if rf >= 13 and (abs(dx)>1 or abs(dy)>1)]
print(f'Real divergences (rf>=13, |dx|>1 or |dy|>1): {len(real_bad)}')

if real_bad:
    print('\nFirst 25 divergent rows:')
    for row in real_bad[:25]:
        rf,sf,rx,ry,sx,sy,dx,dy = row
        print(f'  rf={rf} sf={sf} nes=({rx},{ry}) sim=({sx},{sy}) dx={dx} dy={dy}')
    print('\nLast 10 rows:')
    for row in real_bad[-10:]:
        rf,sf,rx,ry,sx,sy,dx,dy = row
        print(f'  rf={rf} sf={sf} nes=({rx},{ry}) sim=({sx},{sy}) dx={dx} dy={dy}')

# Show last 20 rows of all_rows to see where trace ends
print('\nLast 20 all_rows:')
for row in all_rows[-20:]:
    rf,sf,rx,ry,sx,sy,dx,dy = row
    print(f'  rf={rf} sf={sf} nes=({rx},{ry}) sim=({sx},{sy}) dx={dx} dy={dy}')
