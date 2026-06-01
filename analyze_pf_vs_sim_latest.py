import csv
import glob
import os
import re
from dataclasses import dataclass


STEP_RE = re.compile(
    r"\[STEP_START\]\s+step=(?P<step>\d+)\s+pfFrame=(?P<frame>\d+)\s+"
    r"playerX_fixed=0x(?P<xhex>[0-9A-F]+)\s+\((?P<xpx>-?\d+)px\),\s+"
    r"playerY_fixed=0x(?P<yhex>[0-9A-F]+)\s+\((?P<ypx>-?\d+)px\),\s+"
    r"playerVelY_fixed=0x(?P<vyhex>[0-9A-F]+)",
    re.IGNORECASE,
)
DEATH_RE = re.compile(r"\[DEATH\].*", re.IGNORECASE)
PF_FULL_FRAME_RE = re.compile(
    r"^f=(?P<frame>\d+)\s+.*tag=frame\.in\s+X=(?P<x>\d+)\.[0-9A-F]{2}\s+Y=(?P<y>\d+)\.[0-9A-F]{2}"
    r"\s+Ypx=(?P<ypx>\d+)\s+Vx=(?P<vx>-?\d+)\s+Vy=(?P<vy>-?\d+)\s+.*\sgm=(?P<mode>-?\d+)",
    re.IGNORECASE,
)


@dataclass
class SimRow:
    step: int
    frame: int
    x: int
    y: int
    vy_hex: str
    line_no: int


def _latest(paths):
    if not paths:
        return None
    return max(paths, key=lambda p: os.path.getmtime(p))


def _pick_latest_files(tmp):
    pf_csv = _latest(glob.glob(os.path.join(tmp, "famidash_pf_trace_C__*stereomadness*")))
    pf_full = _latest(glob.glob(os.path.join(tmp, "famidash_pf_trace_FULL_C__*stereomadness*.log")))
    sim_log = _latest(glob.glob(os.path.join(tmp, "famidash_sim_debug_*.txt")))
    return pf_csv, pf_full, sim_log


def _read_pf_csv(path):
    rows = {}
    with open(path, newline="", encoding="utf-8", errors="replace") as f:
        for r in csv.DictReader(f):
            try:
                fr = int(r["frame"])
                rows[fr] = {
                    "x": int(r["X_px"]),
                    "y": int(r["Y_px"]),
                    "mode": int(r.get("mode", -1)),
                    "vy_hex": r.get("VelY_fixed", ""),
                }
            except Exception:
                continue
    return rows


def _read_pf_full(path):
    rows = {}
    with open(path, "r", encoding="utf-8", errors="replace") as f:
        for line in f:
            m = PF_FULL_FRAME_RE.match(line)
            if not m:
                continue
            fr = int(m.group("frame"))
            rows[fr] = {
                "x": int(m.group("x")),
                "y": int(m.group("ypx")),
                "mode": int(m.group("mode")),
                "vy_hex": m.group("vy"),
            }
    return rows


def _read_sim(path):
    rows = []
    death_line = None
    death_line_no = None
    with open(path, "r", encoding="utf-8", errors="replace") as f:
        for idx, line in enumerate(f, start=1):
            m = STEP_RE.search(line)
            if m:
                rows.append(
                    SimRow(
                        step=int(m.group("step")),
                        frame=int(m.group("frame")),
                        x=int(m.group("xpx")),
                        y=int(m.group("ypx")),
                        vy_hex=m.group("vyhex"),
                        line_no=idx,
                    )
                )
                continue
            if death_line is None and DEATH_RE.search(line):
                death_line = line.strip()
                death_line_no = idx
    return rows, death_line, death_line_no


def main():
    tmp = os.environ.get("TEMP") or os.environ.get("TMP")
    if not tmp:
        print("TEMP not set")
        return 2

    pf_csv, pf_full, sim_log = _pick_latest_files(tmp)
    print(f"PF_CSV={pf_csv}")
    print(f"PF_FULL={pf_full}")
    print(f"SIM_LOG={sim_log}")

    if not pf_full or not sim_log:
        print("Missing files. Need both latest PF stereomadness FULL log and SIM debug log.")
        return 2

    pf = _read_pf_full(pf_full)
    # Keep CSV fallback available for quick sanity checks if needed.
    if not pf and pf_csv:
        pf = _read_pf_csv(pf_csv)
    sim_rows, death_line, death_line_no = _read_sim(sim_log)

    print(f"Loaded PF frames: {len(pf)}")
    print(f"Loaded SIM steps: {len(sim_rows)}")

    first = None
    for s in sim_rows:
        p = pf.get(s.frame)
        if not p:
            continue
        dx = s.x - p["x"]
        dy = s.y - p["y"]
        if dx != 0 or dy != 0:
            first = (s, p, dx, dy)
            break

    if not first:
        print("No XY divergence found in overlapping frames.")
    else:
        s, p, dx, dy = first
        print("FIRST_DIVERGENCE")
        print(f"  step={s.step} frame={s.frame} line={s.line_no}")
        print(f"  sim: x={s.x} y={s.y} vy=0x{s.vy_hex}")
        print(f"  pf : x={p['x']} y={p['y']} vy={p['vy_hex']} mode={p['mode']}")
        print(f"  delta: dx={dx} dy={dy}")

        lo = s.frame - 12
        hi = s.frame + 20
        print("WINDOW frame step dx dy simX pfX simY pfY")
        for r in sim_rows:
            if r.frame < lo or r.frame > hi:
                continue
            p2 = pf.get(r.frame)
            if not p2:
                continue
            print(
                f"{r.frame:5d} {r.step:5d} {r.x - p2['x']:3d} {r.y - p2['y']:3d} "
                f"{r.x:5d} {p2['x']:5d} {r.y:4d} {p2['y']:4d}"
            )

    if death_line:
        print(f"SIM_DEATH line={death_line_no}: {death_line}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
