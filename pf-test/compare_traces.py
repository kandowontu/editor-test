import csv, sys

def load(pf_file, nes_file):
    with open(pf_file) as f:
        pf_map = {}
        for r in csv.DictReader(f):
            if r['frame'] != 'p2':
                pf_map[int(r['frame'])] = r
    with open(nes_file) as f:
        lines = f.readlines()
    # When the NES game frame overruns an NMI (lag frame), multiple endFrame
    # rows share one sim_cursor and some capture MID-frame state (e.g. halfway
    # through a spider teleport scan). Keep ALL rows per sim; a sim counts as
    # matching if ANY of its rows matches PF.
    nes_map = {}
    for row in csv.DictReader(lines[1:]):
        sc = row.get('sim_cursor')
        if sc:
            try: nes_map.setdefault(int(sc), []).append(row)
            except: pass
    return pf_map, nes_map

def s16(v):
    v = int(v) & 0xFFFF
    return v - 65536 if v >= 32768 else v

def rows_match(pf, nr):
    return int(pf['Y_px']) == int(nr['py']) and int(pf['Y_lowB']) == (int(nr['raw_y']) & 0xFF)

def best_row(pf, rows):
    """Row matching PF if any; else the last row (post-frame state normally)."""
    for r in rows:
        if rows_match(pf, r):
            return r
    return rows[-1]

def first_div(pf_map, nes_map):
    """First frame where py or sub-pixel low byte differs on every dup row."""
    for f_ in sorted(pf_map):
        sim = f_ + 1
        if sim not in nes_map:
            print(f'NES trace ends at sim={sim-1} (PF continues to f={max(pf_map)})')
            return None
        pf = pf_map[f_]
        if not any(rows_match(pf, r) for r in nes_map[sim]):
            return f_
    return None

def detail(pf_map, nes_map, lo, hi):
    for f_ in range(lo, hi):
        sim = f_ + 1
        pf, rows = pf_map.get(f_), nes_map.get(sim)
        if not pf or not rows: continue
        nr = best_row(pf, rows)
        pf_vy = s16(int(pf['VelY_fixed'], 16))
        nes_vy = int(nr['vel_y'])
        plow = int(pf['Y_lowB']); nlow = int(nr['raw_y']) & 0xFF
        mism = not rows_match(pf, nr)
        mark = ' <<<' if mism else ''
        dup = f' [{len(rows)} rows]' if len(rows) > 1 else ''
        print(f"f={f_} sim={sim} pf=({pf['Y_px']},{plow}) nes=({nr['py']},{nlow}) "
              f"pf_vy={pf_vy} nes_vy={nes_vy} inp={pf['input']} a_cur={nr['a_cur']} "
              f"grav={nr['cp_gravity']} tidx={nr['table_idx']} gm={nr['gamemode']} dash={nr['dashing']} "
              f"px={nr['px']} slope={pf['SlopeType']}/{pf['LastSlopeType']} sf={pf['SlopeFrames']} "
              f"mode={pf['mode']}{dup}{mark}")

def all_divs(pf_map, nes_map):
    """All divergent frames, as (frame, dy) using the closest dup row."""
    divs = []
    for f_ in sorted(pf_map):
        sim = f_ + 1
        if sim not in nes_map: break
        pf = pf_map[f_]
        if not any(rows_match(pf, r) for r in nes_map[sim]):
            dy = min(abs(int(pf['Y_px']) - int(r['py'])) for r in nes_map[sim])
            divs.append((f_, dy))
    return divs

def summarize(divs, nes_map):
    if not divs:
        print('No divergent frames.')
        return
    ranges = []
    s, p, mdy = divs[0][0], divs[0][0], divs[0][1]
    for d, dy in divs[1:]:
        if d - p > 2:
            ranges.append((s, p, mdy)); s, mdy = d, dy
        else:
            mdy = max(mdy, dy)
        p = d
    ranges.append((s, p, mdy))
    print(f'Total divergent frames: {len(divs)}')
    for s, e, mdy in ranges:
        gm = nes_map.get(s + 1, [{}])[0].get('gamemode', '?')
        print(f'  f={s}-{e} ({e-s+1} frames) max_dy={mdy}px gm={gm}')

if __name__ == '__main__':
    pf_file, nes_file = sys.argv[1], sys.argv[2]
    pf_map, nes_map = load(pf_file, nes_file)
    n_rows = sum(len(v) for v in nes_map.values())
    print(f'PF rows: {len(pf_map)}, NES rows: {n_rows} ({len(nes_map)} unique sims)')
    prev = None
    for sc in sorted(nes_map):
        gm = nes_map[sc][0]['gamemode']
        if gm != prev:
            print(f'  gm change sim={sc}: gm={gm}')
            prev = gm
    divs = all_divs(pf_map, nes_map)
    summarize(divs, nes_map)
    f0 = first_div(pf_map, nes_map)
    if f0 is None:
        print('No sub-pixel divergence found in overlapping range.')
    else:
        print(f'\nFirst sub-pixel divergence: f={f0} (sim={f0+1})\n')
        detail(pf_map, nes_map, f0 - 8, f0 + 12)
