import csv, os
T=os.environ['TEMP']

# Find PF death
last_alive=None
with open(os.path.join(T,'famidash_pf_trace.csv')) as f:
    r=csv.DictReader(f)
    for row in r:
        if int(row['alive'])==1:
            last_alive=row
        else:
            print(f"PF DIED at frame {row['frame']} X_px={row['X_px']} Y_px={row['Y_px']} VelY={row['VelY_fixed']} input={row['input']} mode={row['mode']} grav={row['gravFlipped']} mini={row['mini']}")
            break
print(f"Last alive: frame={last_alive['frame']} ({last_alive['X_px']},{last_alive['Y_px']}) VelY={last_alive['VelY_fixed']} mode={last_alive['mode']} mini={last_alive['mini']}")

# walk last 20 frames before death
import collections
buf=collections.deque(maxlen=25)
with open(os.path.join(T,'famidash_pf_trace.csv')) as f:
    r=csv.DictReader(f)
    for row in r:
        buf.append(row)
        if int(row['alive'])==0:
            break
print("\n--- last 25 PF frames ---")
for row in buf:
    print(f"f={row['frame']} X={row['X_px']} Y={row['Y_px']} VelY={row['VelY_fixed']} inp={row['input']} alive={row['alive']} mode={row['mode']} mini={row['mini']} grav={row['gravFlipped']}")
