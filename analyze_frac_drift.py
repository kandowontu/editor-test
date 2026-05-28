"""Find first frame where PF and NES Y fractional positions diverge."""
import re, os
T = os.environ['TEMP']
nes = os.environ['NES']
pf_trace = os.environ['PF']

# NES: cpy from first ufo/ship_movement event per sim
nes_state = {}
cur = None
re_sim = re.compile(r'^F=\d+ sim=(\d+)')
re_ev = re.compile(r'(?:ufo|ship)_movement\.(?:in|out)\b.*cpy=([0-9A-Fa-f]{4}).*cpvy=(-?\d+)')
with open(nes, 'r', encoding='utf-8', errors='ignore') as f:
    for ln in f:
        m = re_sim.match(ln)
        if m:
            cur = int(m.group(1)); continue
        if cur is None or cur in nes_state: continue
        m = re_ev.search(ln)
        if m:
            cpy = int(m.group(1), 16)
            nes_state[cur] = (cpy >> 8, cpy & 0xFF, int(m.group(2)))

# PF frame.in: Y high.frac and Vy
re_pf = re.compile(r'^f=(\d+) cur=\d+ gm=\d+ tag=frame\.in .*Y=(\d+)\.([0-9A-Fa-f]+).*Vy=(-?\d+)')
pf = {}
with open(pf_trace, 'r', encoding='utf-8', errors='ignore') as f:
    for ln in f:
        m = re_pf.match(ln)
        if m:
            yint = int(m.group(2))
            yfrac = int(m.group(3), 16)
            pf[int(m.group(1))] = (yint, yfrac, int(m.group(4)))

# Sync offset: PF f corresponds to NES sim. Find by matching first non-trivial frame.
# Both should have same Y_world. NES Y_world = nes_high + offset. We need offset.
# Use the analyze_div_latest output's calibration; PF Y matches NES at start.
# Simpler: align by f where both show same Y_world.
# Iterate aligned frames and report first divergence in fraction or integer.

# Look at first 200 common frames to find sync. Just print frac diff over time.
common = sorted(set(pf.keys()) & set(nes_state.keys()))
prev_diff = None
for f in common[:4100]:
    pi, pf_, pvy = pf[f]
    nh, nl, nvy = nes_state[f]
    # We don't know absolute pixel alignment; compare fractions and Y deltas
    frac_diff = pf_ - nl
    if abs(frac_diff) > 0x10 and (prev_diff is None or abs(frac_diff - prev_diff) > 4):
        print(f"f={f}: PF=Y={pi}.{pf_:02X} Vy={pvy} | NES cpy_hi={nh:02X} cpy_lo={nl:02X} Vy={nvy} | fracDiff={frac_diff:+d}")
        prev_diff = frac_diff
        if prev_diff is not None and abs(frac_diff) > 80:
            break
