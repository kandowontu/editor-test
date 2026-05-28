"""Bisect Y-drift: find first frame PF Y_world != NES Y_world."""
import re, os
T = os.environ['TEMP']
nes = os.environ.get('NES') or os.path.join(T, 'famidash_mesen_physics_debug_dorabaebasic6_20260526_031117_183.log')
pf_trace = os.environ.get('PF') or os.path.join(T, 'famidash_pf_trace_FULL_C__famidash_LEVELS_LEVEL_DATA_lvlset_HUGE_dorabaebasic6_tmx_20260526_031111.log')

# --- NES: pick (cpy, cpx, vy, sy) at sim entry (first ufo/ship_movement.in) ---
nes_state = {}  # sim -> (cpx, cpy, vx, vy, sy_raw)
cur = None
re_sim = re.compile(r'^F=\d+ sim=(\d+)')
re_ev = re.compile(
    r'(?:ufo|ship)_movement\.(?:in|out)\b'
    r'.*cpx=([0-9A-Fa-f]{4}) cpy=([0-9A-Fa-f]{4})'
    r'.*cpvx=(-?\d+) cpvy=(-?\d+)'
    r'.*sy=([0-9A-Fa-f]{4})'
)
with open(nes, 'r', encoding='utf-8', errors='ignore') as f:
    for ln in f:
        m = re_sim.match(ln)
        if m:
            cur = int(m.group(1)); continue
        if cur is None: continue
        m = re_ev.search(ln)
        if m and cur not in nes_state:
            sy_raw = int(m.group(5), 16)
            nes_state[cur] = (int(m.group(1),16), int(m.group(2),16),
                              int(m.group(3)), int(m.group(4)), sy_raw)

# --- PF: frame.in (state at frame entry, pre-gravity) ---
# f=2177 cur=0 gm=1 tag=frame.in X=5312.D8 Y=343.4D Ypx=343 Vx=708 Vy=-414 ... camY=190.00
re_pf = re.compile(
    r'^f=(\d+) cur=\d+ gm=(\d+) tag=frame\.in '
    r'X=(\d+)\.([0-9A-Fa-f]+) Y=(\d+)\.([0-9A-Fa-f]+) '
    r'Ypx=(\d+) Vx=(-?\d+) Vy=(-?\d+).*camY=([0-9.]+)'
)
pf = {}
with open(pf_trace, 'r', encoding='utf-8', errors='ignore') as f:
    for ln in f:
        m = re_pf.match(ln)
        if m:
            fr = int(m.group(1))
            if fr in pf: continue
            pf[fr] = {
                'gm': int(m.group(2)),
                'X_int': int(m.group(3)),
                'X_frac': int(m.group(4),16),
                'Y_int': int(m.group(5)),
                'Y_frac': int(m.group(6),16),
                'Ypx': int(m.group(7)),
                'Vx': int(m.group(8)),
                'Vy': int(m.group(9)),
                'camY': float(m.group(10)),
            }

# NES sy raw â†’ linear pixels (skip-F0): high*240 + low
def sy_to_linear(raw):
    return ((raw >> 8) & 0xFF) * 240 + (raw & 0xFF)

# _nesCoordOffset: solve from a known matching frame (assume f=2140 matches)
# PF camY at f=2140 vs NES sy at sim=2140
ref_f = 2140
if ref_f in pf and ref_f in nes_state:
    pf_cam = pf[ref_f]['camY']
    nes_lin = sy_to_linear(nes_state[ref_f][4])
    offs = nes_lin - int(pf_cam)
    print(f"Calibration: PF camY={pf_cam}, NES sy raw=0x{nes_state[ref_f][4]:04X}, lin={nes_lin}, _nesCoordOffset={offs}")
else:
    offs = 528
    print(f"Using assumed _nesCoordOffset={offs}")

print()
print(f"{'f':>5} | PF: Y_world Ypx Y_frac Vy hold | NES: Yh camPF Y_world Vy | dY")
print('-'*120)
diverged_at = None
import sys
LO = int(sys.argv[1]) if len(sys.argv) > 1 else 2130
HI = int(sys.argv[2]) if len(sys.argv) > 2 else 8000
for fr in range(LO, HI):
    if fr not in pf: continue
    p = pf[fr]
    n = nes_state.get(fr + 1)  # PF f=N corresponds to NES sim=N+1
    if not n: continue
    nes_cam_pf = sy_to_linear(n[4]) - offs
    nes_y_world = (n[1] >> 8) + nes_cam_pf
    # But NES cpy is POST-integration (start-of-next-sim), so compare PF Y entry
    # to NES sim=fr-1's cpy (end of prev sim = start of current)?
    # Actually ufo_movement.in is post-gravity-pre-integration. ufo_movement.out
    # is post-integration. We grabbed ".in or .out" first match. Let's just show.
    dY = p['Ypx'] - nes_y_world
    flag = ' <-- FIRST DIVERGE' if dY != 0 and diverged_at is None else ''
    if dY != 0 and diverged_at is None: diverged_at = fr
    print(f"{fr:>5} | {p['Y_int']:>4} {p['Ypx']:>3} .{p['Y_frac']:02X} Vy={p['Vy']:>5} | "
          f"Yh={n[1]>>8:>3} camPF={nes_cam_pf:>4} Yw={nes_y_world:>4} Vy={n[3]:>5} | dY={dY:>+2}{flag}")

