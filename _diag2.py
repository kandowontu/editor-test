import os, re, csv

REPLAY_DIR = r"C:\Users\kando\Documents\Famidash Editor\Replays\everyend"
SIM_CSV = os.path.join(REPLAY_DIR, "famidash_replay.csv")
ROM_CSV = os.path.join(REPLAY_DIR, "famidash_mesen_trace.csv")

# Load sim
sim_rows = []  # (frame, x, y, a)
with open(SIM_CSV) as f:
    for line in f:
        line=line.strip()
        if not line: continue
        if line.startswith("nes_y_offset"): continue
        if line.startswith("frame"): continue
        p = line.split(",")
        sim_rows.append((int(p[0]), int(p[1]), int(p[2]), int(p[3])))
print(f"sim rows: {len(sim_rows)}")

# Load rom (multi-attempt; '#' = attempt boundary)
attempts = []
cur = []
attempts.append(cur)
with open(ROM_CSV) as f:
    for line in f:
        s=line.strip()
        if not s: continue
        if s.startswith("#"):
            cur=[]; attempts.append(cur); continue
        if s.startswith("nes_y_offset") or s.startswith("rom_frame"): continue
        p = s.split(",")
        rf = int(p[0]); sc = int(p[1])
        try: px = int(p[2])
        except: px = int(p[2]) & 0xFFFFFFFF
        py = int(p[3]); an = int(p[4]); ac = int(p[5]) if len(p)>5 else 0
        cur.append((rf, sc, px, py, an, ac))
attempts = [a for a in attempts if a]
print(f"attempts: {len(attempts)}, sizes:", [len(a) for a in attempts][:10])

# pick attempt with max sim_cursor
best = max(attempts, key=lambda a: max(r[1] for r in a))
print(f"best attempt: rows={len(best)} max_sc={max(r[1] for r in best)} last_rf={best[-1][0]}")

# Walk frames; sim row index = sc - 1 (Lua 1-indexed)
diverges = []
for rf, sc, px, py, an, ac in best:
    si = sc - 1
    if si < 0 or si >= len(sim_rows): continue
    sf, sx, sy, sa = sim_rows[si]
    dx = sx - px; dy = sy - py
    diverges.append((rf, sf, px, py, sx, sy, dx, dy, an, sa))

# Find first dy != 0 after frame 30
first = None
for r in diverges:
    if r[0] >= 30 and r[7] != 0:
        first = r; break
print("first dy!=0 (rf>=30):", first)

# Find first |dy|>=4
firstbig = None
for r in diverges:
    if r[0] >= 30 and abs(r[7])>=4:
        firstbig = r; break
print("first |dy|>=4:", firstbig)

# Last 20 entries
print("\nlast 20:")
for r in diverges[-20:]:
    print(f"  rf={r[0]:>5} sf={r[1]:>5} rom=({r[2]:>5},{r[3]:>4}) sim=({r[4]:>5},{r[5]:>4}) dx={r[6]:>4} dy={r[7]:>4} a_n={r[8]} sim_a={r[9]}")

if firstbig:
    target = firstbig[0]
    print(f"\ncontext around rf={target}:")
    for r in diverges:
        if target-3 <= r[0] <= target+30:
            print(f"  rf={r[0]:>5} sf={r[1]:>5} rom=({r[2]:>5},{r[3]:>4}) sim=({r[4]:>5},{r[5]:>4}) dx={r[6]:>4} dy={r[7]:>4} a_n={r[8]} sim_a={r[9]}")
