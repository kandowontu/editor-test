import csv, os, glob

pat = os.path.join(os.environ['TEMP'], 'famidash_pf_trace_test_20260516_034242_884.csv')
files = glob.glob(pat)
if not files:
    pat2 = os.path.join(os.environ['TEMP'], 'famidash_pf_trace_test_*.csv')
    files = sorted(glob.glob(pat2))
    if files: pat = files[-1]

print(f"Reading: {pat}")
with open(pat, newline='') as f:
    rdr = csv.DictReader(f)
    count = 0
    prev_y = None
    for row in rdr:
        if row.get('mode') == '2' and row.get('gravFlipped') == '1':
            xpx = int(row.get('X_px', 0))
            if 740 <= xpx <= 870:
                ypx = int(row.get('Y_px', 0))
                vy = row.get('VelY_fixed', '0')
                og = row.get('onGround', '0')
                fr = row.get('frame', '?')
                dy = (ypx - prev_y) if prev_y is not None else 0
                print(f"f={fr:>5}  X={xpx:>4}  Y={ypx:>4}  dY={dy:>4}  VY={vy:>8}  og={og}")
                prev_y = ypx
                count += 1
                if count >= 80: break
