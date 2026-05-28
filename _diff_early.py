import csv
import sys

if len(sys.argv) >= 3:
    PF_PATH = sys.argv[1]
    MT_PATH = sys.argv[2]
else:
    PF_PATH = r"C:\Users\kando\AppData\Local\Temp\famidash_pf_trace_cataclysm_20260520_165934_832.csv"
    MT_PATH = r"C:\Users\kando\AppData\Local\Temp\famidash_mesen_trace_cataclysm_20260520_171856_581.csv"

def load_pf():
    rows = []
    with open(PF_PATH, newline='') as f:
        r = csv.DictReader(f)
        for row in r:
            rows.append(row)
    return rows

def load_mt():
    with open(MT_PATH) as f:
        lines = f.read().splitlines()
    # skip metadata first line if it doesn't look like header
    if not lines[0].startswith("rom_frame"):
        lines = lines[1:]
    import io
    r = csv.DictReader(io.StringIO("\n".join(lines)))
    return list(r)

pf = load_pf()
mt = load_mt()

# Build mt-by-sim_cursor: store ALL rows
mt_by_sim = {}
for row in mt:
    sv = row.get('sim_cursor')
    if not sv:
        continue
    s = int(sv)
    mt_by_sim.setdefault(s, []).append(row)

print(f"PF rows: {len(pf)}, MT rows: {len(mt)}, sim range: {min(mt_by_sim)}..{max(mt_by_sim)}")
print(f"Multi-row sim_cursors: {sum(1 for v in mt_by_sim.values() if len(v) > 1)}")

# Align PF f=N with MT sim_cursor=N+1, use LAST row
print("\n=== diff (LAST mt row per sim) ===")
print(f"{'fr':>5} | {'PF x':>5} {'PF y':>5} {'PF m':>4} {'PF vy':>8} | {'MT x':>5} {'MT y':>5} {'MT m':>4} {'MT vy':>8} | nrows | dx dy")
last_clean_fr = -1
first_div_fr = None
for pfrow in pf:
    fr = int(pfrow['frame'])
    sim = fr + 1
    if sim not in mt_by_sim:
        continue
    mts = mt_by_sim[sim]
    mlast = mts[-1]
    pfx = int(pfrow['X_px']); pfy = int(pfrow['Y_px'])
    pfm = int(pfrow['mode']); pfv = pfrow['VelY_fixed']
    mtx = int(mlast['px']); mty = int(mlast['py'])
    mtm = int(mlast['gamemode']); mtv = mlast['vel_y']
    dx = pfx - mtx; dy = pfy - mty
    if dx == 0 and dy == 0:
        last_clean_fr = fr
    if (dx != 0 or dy != 0) and first_div_fr is None:
        first_div_fr = fr

print(f"\nLast clean fr (dx=dy=0): {last_clean_fr}")
print(f"First divergence fr: {first_div_fr}")

# Print 30 frames around first_div
print(f"\n=== Around fr {first_div_fr} (FIRST any divergence) ===")
for fr in range(max(0,first_div_fr-5), min(len(pf), first_div_fr+25)):
    pfrow = pf[fr]
    sim = fr + 1
    if sim not in mt_by_sim:
        continue
    mts = mt_by_sim[sim]
    mlast = mts[-1]
    pfx = int(pfrow['X_px']); pfy = int(pfrow['Y_px']); pfm = int(pfrow['mode'])
    mtx = int(mlast['px']); mty = int(mlast['py']); mtm = int(mlast['gamemode'])
    dx = pfx - mtx; dy = pfy - mty
    mark = ' !' if (dx or dy) else '  '
    print(f"{fr:>5} | {pfx:>5} {pfy:>5} {pfm:>4} {pfrow['VelY_fixed']:>8} | {mtx:>5} {mty:>5} {mtm:>4} {mlast['vel_y']:>8} | n={len(mts):>2} | {dx:+d} {dy:+d}{mark}")

# Find SECOND-stage divergence: first fr where |dy|>=4
print("\n=== Looking for first fr where |dy|>=4 ===")
big_div = None
for pfrow in pf:
    fr = int(pfrow['frame'])
    sim = fr + 1
    if sim not in mt_by_sim:
        continue
    mlast = mt_by_sim[sim][-1]
    dy = int(pfrow['Y_px']) - int(mlast['py'])
    dx = int(pfrow['X_px']) - int(mlast['px'])
    if abs(dy) >= 4 or abs(dx) >= 4:
        big_div = fr
        break
print(f"First big-div fr: {big_div}")
if big_div:
    for fr in range(max(0,big_div-15), min(len(pf), big_div+15)):
        pfrow = pf[fr]
        sim = fr + 1
        if sim not in mt_by_sim:
            continue
        mts = mt_by_sim[sim]
        mlast = mts[-1]
        pfx = int(pfrow['X_px']); pfy = int(pfrow['Y_px']); pfm = int(pfrow['mode'])
        mtx = int(mlast['px']); mty = int(mlast['py']); mtm = int(mlast['gamemode'])
        dx = pfx - mtx; dy = pfy - mty
        mark = ' !' if (abs(dx)>=2 or abs(dy)>=2) else '  '
        print(f"{fr:>5} | {pfx:>5} {pfy:>5} {pfm:>4} {pfrow['VelY_fixed']:>8} | {mtx:>5} {mty:>5} {mtm:>4} {mlast['vel_y']:>8} | n={len(mts):>2} | {dx:+d} {dy:+d}{mark}")
