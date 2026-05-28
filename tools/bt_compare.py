import argparse
import csv
import os
from pathlib import Path
from typing import Dict, List, Tuple


def s32(v: str) -> int:
    n = int(v)
    return n - 4294967296 if n > 2147483647 else n


def latest_matching(patterns: List[str]) -> Path:
    temp = Path(os.environ["TEMP"])
    files: List[Path] = []
    for pat in patterns:
        files.extend(temp.glob(pat))
    files = [p for p in files if p.is_file() and p.stat().st_size > 0]
    if not files:
        raise FileNotFoundError(f"no files matched patterns: {patterns}")
    return sorted(files, key=lambda p: p.stat().st_mtime)[-1]


def load_pf_rows(pf_path: Path) -> Dict[int, Dict[str, str]]:
    rows = list(csv.DictReader(open(pf_path, newline="", encoding="utf-8")))
    return {int(r["frame"]): r for r in rows if r.get("frame", "").isdigit()}


def load_mesen_rows(mesen_path: Path) -> List[Dict[str, str]]:
    lines = mesen_path.read_text(encoding="utf-8", errors="ignore").splitlines()
    hidx = next(i for i, l in enumerate(lines) if l.startswith("rom_frame,"))
    header = lines[hidx].split(",")
    resp = [
        i
        for i, l in enumerate(lines[hidx + 1 :], start=hidx + 1)
        if l.startswith("# --- respawn ---")
    ]
    start = (resp[-1] + 1) if resp else (hidx + 1)

    out: List[Dict[str, str]] = []
    for l in lines[start:]:
        if not l or l.startswith("#"):
            continue
        parts = l.split(",")
        if len(parts) < len(header):
            continue
        d = {header[i]: parts[i] for i in range(len(header))}
        if d.get("rom_frame", "").isdigit():
            out.append(d)
    return out


def compute_rows(
    mes_rows: List[Dict[str, str]], pf_by_frame: Dict[int, Dict[str, str]], off: int
) -> List[Tuple[int, int, int, int, int, int, int, int, str, str]]:
    rows: List[Tuple[int, int, int, int, int, int, int, int, str, str]] = []
    for m in mes_rows:
        rf = int(m["rom_frame"])
        pf_f = rf + off
        p = pf_by_frame.get(pf_f)
        if not p:
            continue
        mx = s32(m["px"])
        my = s32(m["py"])
        px = int(p["X_px"])
        py = int(p["Y_px"])
        dx = mx - px
        dy = my - py
        rows.append((rf, pf_f, dx, dy, mx, my, px, py, m["vel_y"], p["VelY_fixed"]))
    return rows


def windows(rows):
    wins = []
    in_run = False
    start = 0
    for i, r in enumerate(rows):
        bad = (r[2] != 0 or r[3] != 0)
        if bad and not in_run:
            in_run = True
            start = i
        elif not bad and in_run:
            wins.append((start, i - 1))
            in_run = False
    if in_run:
        wins.append((start, len(rows) - 1))
    return wins


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--offset", type=int, default=-12)
    ap.add_argument("--pf", default="")
    ap.add_argument("--mesen", default="")
    ap.add_argument("--first", type=int, default=30, help="windows to print")
    args = ap.parse_args()

    if args.mesen:
        mesen = Path(args.mesen)
    else:
        mesen = latest_matching(["famidash_mesen_trace_backontrack_*.csv"])

    if args.pf:
        pf = Path(args.pf)
    else:
        pf = latest_matching(
            [
                "famidash_pf_trace_C__famidash_LEVELS_LEVEL_DATA_lvlset_HUGE_backontrack_tmx_*.csv",
                "famidash_pf_trace_unknown_*.csv",
            ]
        )

    pf_by_frame = load_pf_rows(pf)
    mes_rows = load_mesen_rows(mesen)
    rows = compute_rows(mes_rows, pf_by_frame, args.offset)
    wins = windows(rows)

    print(f"mesen={mesen.name}")
    print(f"pf={pf.name}")
    print(f"rows {len(rows)} wins {len(wins)}")

    for wi, (a, b) in enumerate(wins[: args.first], start=1):
        ra, rb = rows[a], rows[b]
        maxdy = max(abs(rows[k][3]) for k in range(a, b + 1))
        maxdx = max(abs(rows[k][2]) for k in range(a, b + 1))
        self_corr = (b < len(rows) - 1)
        print(
            f"w{wi}: rom[{ra[0]}..{rb[0]}] pf[{ra[1]}..{rb[1]}] len={b-a+1} "
            f"max|dx|={maxdx} max|dy|={maxdy} selfCorrect={self_corr}"
        )

    mis = [r for r in rows if r[2] != 0 or r[3] != 0]
    print("first_mis")
    for r in mis[:40]:
        print(r[0], r[1], r[2], r[3], r[4], r[5], r[6], r[7], r[8], r[9])


if __name__ == "__main__":
    main()
