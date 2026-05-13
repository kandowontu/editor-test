import os
temp = os.environ['TEMP']
pf_file = os.path.join(temp, 'famidash_pf_trace.csv')

with open(pf_file, 'r') as f:
    lines = f.readlines()
    hdr = lines[0].strip().split(',')
    
    print("Full sequence from frame 4295-4305:")
    print("Frame | grav | input | Y_px | notes")
    print("-" * 60)
    
    for line in lines[1:]:
        parts = line.strip().split(',')
        if not parts or not parts[0]: continue
        try:
            d = {hdr[j]: parts[j] for j in range(min(len(hdr), len(parts)))}
            frame = int(d.get('frame', -1))
            if 4295 <= frame <= 4305:
                grav = d.get('gravFlipped', '?')
                inp = d.get('input', '?')
                y = d.get('Y_px', '?')
                event = d.get('event', '')
                status = f"grav={grav:1s} input={inp:1s} Y={y:5s}"
                if event:
                    status += f" EVENT={event}"
                print(f"{frame:5d} | {status}")
        except:
            pass
