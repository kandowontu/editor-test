import csv, os

temp = os.environ['TEMP']
pf_file = os.path.join(temp, 'famidash_pf_trace.csv')

# Load PF trace
with open(pf_file, 'r') as f:
    lines = f.readlines()
    hdr = lines[0].strip().split(',')
    print("Gravity flip status at frame 4299 (minimal fix TAS):")
    print("Frame | gravFlipped | VelY_fixed")
    print("-" * 45)
    
    for i, line in enumerate(lines[1:]):
        parts = line.strip().split(',')
        if not parts or not parts[0]:
            continue
        try:
            d = {hdr[j]: parts[j] for j in range(min(len(hdr), len(parts)))}
            frame = int(d.get('frame', -1))
            if 4297 <= frame <= 4302:
                grav = d.get('gravFlipped', '?')
                vely = d.get('VelY_fixed', '?')
                print(f"{frame:5d} | {grav:11s} | {vely:10s}")
        except:
            pass
