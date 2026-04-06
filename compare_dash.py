import re, sys

sim_file = r"C:\Users\kando\AppData\Local\Temp\famidash_sim_debug_20260406_030119.txt"
pf_file = r"C:\Users\kando\AppData\Local\Temp\famidash_pf_debug_20260406_024417.txt"

sim_re = re.compile(r'\[STEP_START\].*playerX_fixed=0x([0-9A-Fa-f]+)\s+\((\d+)px\).*playerY_fixed=0x([0-9A-Fa-f]+)\s+\((\d+)px\).*playerVelY_fixed=0x([0-9A-Fa-f]+)')
pf_re = re.compile(r'\[REPLAY.*X=0x([0-9A-Fa-f]+)\s+\((\d+)px\)\s+Y=0x([0-9A-Fa-f]+)\s+\((\d+)px\)\s+velY=0x([0-9A-Fa-f]+).*inp=(\d)')

sim = {}
with open(sim_file) as f:
    for line in f:
        m = sim_re.search(line)
        if m:
            xpx = int(m.group(2))
            if xpx not in sim:
                sim[xpx] = (m.group(3), m.group(5))  # Y_hex, velY_hex

pf = {}
with open(pf_file) as f:
    for line in f:
        m = pf_re.search(line)
        if m:
            xpx = int(m.group(2))
            if xpx not in pf:
                pf[xpx] = (m.group(3), m.group(5), m.group(6))  # Y_hex, velY_hex, inp

common = sorted(set(sim.keys()) & set(pf.keys()))
print(f"Common X positions: {len(common)}")

divergent = []
for x in common:
    sy, sv = sim[x]
    py, pv, pi = pf[x]
    if int(sy, 16) != int(py, 16) or int(sv, 16) != int(pv, 16):
        divergent.append(x)

print(f"Divergent positions: {len(divergent)}")
if divergent:
    print(f"First divergence at X={divergent[0]}")
    # Show 10 frames before first divergence
    first_div_idx = common.index(divergent[0])
    start = max(0, first_div_idx - 10)
    for i in range(start, min(len(common), first_div_idx + 15)):
        x = common[i]
        sy, sv = sim[x]
        py, pv, pi = pf[x]
        match = "  " if int(sy, 16) == int(py, 16) and int(sv, 16) == int(pv, 16) else "**"
        print(f"{match} X={x:5d} SIM(Y=0x{sy} V=0x{sv}) PF(Y=0x{py} V=0x{pv} inp={pi})")
