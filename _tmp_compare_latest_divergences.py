import csv
import glob
import os


def signed32(value):
    parsed = int(value)
    return parsed - 0x100000000 if parsed >= 0x80000000 else parsed


def parse_int(value):
    return int(value, 16) if isinstance(value, str) and value.startswith("0x") else int(value)


temp_dir = os.environ["TEMP"]
mesen_trace = max(
    (path for path in glob.glob(os.path.join(temp_dir, "famidash_mesen_trace_dorabaebasic7_*.csv")) if os.path.getsize(path) > 0),
    key=os.path.getmtime,
)
pf_trace = max(
    (path for path in glob.glob(os.path.join(temp_dir, "famidash_pf_trace_C__*dorabaebasic7*.csv")) if os.path.getsize(path) > 0),
    key=os.path.getmtime,
)

print("MESEN", mesen_trace, os.path.getmtime(mesen_trace), os.path.getsize(mesen_trace))
print("PF   ", pf_trace, os.path.getmtime(pf_trace), os.path.getsize(pf_trace))

pf_rows = {}
with open(pf_trace, newline="") as handle:
    for row in csv.DictReader(handle):
        pf_rows[int(row["frame"])] = row

mesen_by_pf_frame = {}
with open(mesen_trace, newline="") as handle:
    reader = csv.reader(handle)
    first = next(reader, None)
    if first and first[0] == "nes_y_offset":
        header = next(reader, None)
    else:
        header = first
    if not header:
        raise SystemExit("missing Mesen header")
    death_ctx_index = header.index("death_ctx")
    suffix_count = len(header) - death_ctx_index - 1
    for parts in reader:
        if not parts:
            continue
        if len(parts) > len(header):
            parts = parts[:death_ctx_index] + [",".join(parts[death_ctx_index : len(parts) - suffix_count])] + parts[-suffix_count:]
        if len(parts) != len(header):
            continue
        row = dict(zip(header, parts))
        if not row.get("sim_cursor"):
            continue
        sim_cursor = int(row["sim_cursor"])
        if sim_cursor < 2:
            continue
        px = signed32(row["px"])
        if px < 0:
            continue
        mesen_by_pf_frame[sim_cursor - 1] = row

fields = [
    ("X_px", lambda pf, mesen: int(pf["X_px"]), lambda pf, mesen: signed32(mesen["px"])),
    ("Y_px", lambda pf, mesen: int(pf["Y_px"]), lambda pf, mesen: int(mesen["py"])),
    ("VelY", lambda pf, mesen: signed32(parse_int(pf["VelY_fixed"])), lambda pf, mesen: signed32(mesen["vel_y"])),
    ("mode", lambda pf, mesen: int(pf["mode"]), lambda pf, mesen: int(mesen["gamemode"])),
    ("grav", lambda pf, mesen: int(pf["gravFlipped"]), lambda pf, mesen: 1 if int(mesen["cp_gravity"]) in (255, -1) else int(mesen["cp_gravity"])),
    ("sub", lambda pf, mesen: int(pf["ScrollYSubpx"]), lambda pf, mesen: int(mesen["scroll_y_subpx"])),
    ("mini", lambda pf, mesen: int(pf["mini"]), lambda pf, mesen: int(mesen["mini"])),
]

mismatches = []
for frame in sorted(set(pf_rows) & set(mesen_by_pf_frame)):
    pf = pf_rows[frame]
    mesen = mesen_by_pf_frame[frame]
    diffs = []
    for name, pf_value, mesen_value in fields:
        left = pf_value(pf, mesen)
        right = mesen_value(pf, mesen)
        if left != right:
            diffs.append((name, left, right))
    if diffs:
        mismatches.append((frame, diffs, pf, mesen))

runs = []
if mismatches:
    start = previous = mismatches[0][0]
    current = [mismatches[0]]
    for item in mismatches[1:]:
        frame = item[0]
        if frame == previous + 1:
            current.append(item)
            previous = frame
        else:
            runs.append((start, previous, current))
            start = previous = frame
            current = [item]
    runs.append((start, previous, current))

print("mismatch runs:", len(runs))
for start, end, items in runs[:30]:
    print(f"RUN {start}-{end} len={end - start + 1}")
    sample = items if len(items) <= 6 else items[:3] + [None] + items[-3:]
    for item in sample:
        if item is None:
            print("  ...")
            continue
        frame, diffs, pf, mesen = item
        print(
            " ",
            frame,
            "sim",
            int(mesen["sim_cursor"]),
            "diffs",
            diffs,
            "PF",
            "x",
            pf["X_px"],
            "y",
            pf["Y_px"],
            "vy",
            pf["VelY_fixed"],
            "in",
            pf["input"],
            "mode",
            pf["mode"],
            "grav",
            pf["gravFlipped"],
            "slope",
            pf.get("SlopeType"),
            "last",
            pf.get("LastSlopeType"),
            "swon",
            pf.get("SlopeWasOn"),
            "sub",
            pf["ScrollYSubpx"],
            "MES",
            "x",
            signed32(mesen["px"]),
            "y",
            mesen["py"],
            "vy",
            signed32(mesen["vel_y"]),
            "a",
            mesen["a_cur"],
            "mode",
            mesen["gamemode"],
            "grav",
            mesen["cp_gravity"],
            "sub",
            mesen["scroll_y_subpx"],
            "death",
            mesen.get("death_pc"),
            mesen.get("death_ctx"),
        )

visible_mismatches = [item for item in mismatches if any(diff[0] != "sub" for diff in item[1])]
visible_runs = []
if visible_mismatches:
    start = previous = visible_mismatches[0][0]
    current = [visible_mismatches[0]]
    for item in visible_mismatches[1:]:
        frame = item[0]
        if frame == previous + 1:
            current.append(item)
            previous = frame
        else:
            visible_runs.append((start, previous, current))
            start = previous = frame
            current = [item]
    visible_runs.append((start, previous, current))

print("visible mismatch runs:", len(visible_runs))
for start, end, items in visible_runs[:40]:
    modes = sorted({int(item[2]["mode"]) for item in items})
    slopes = sorted({int(item[2].get("SlopeType") or 0) for item in items})
    print(f"VRUN {start}-{end} len={end - start + 1} modes={modes} slopes={slopes}")
    sample = items if len(items) <= 4 else items[:2] + [None] + items[-2:]
    for item in sample:
        if item is None:
            print("  ...")
            continue
        frame, diffs, pf, mesen = item
        print(
            " ", frame,
            "sim", int(mesen["sim_cursor"]),
            "diffs", [diff for diff in diffs if diff[0] != "sub"],
            "PF", "x", pf["X_px"], "y", pf["Y_px"], "vy", pf["VelY_fixed"], "in", pf["input"], "mode", pf["mode"], "grav", pf["gravFlipped"], "slope", pf.get("SlopeType"), "last", pf.get("LastSlopeType"), "swon", pf.get("SlopeWasOn"), "sub", pf["ScrollYSubpx"],
            "MES", "x", signed32(mesen["px"]), "y", mesen["py"], "vy", signed32(mesen["vel_y"]), "a", mesen["a_cur"], "mode", mesen["gamemode"], "grav", mesen["cp_gravity"], "sub", mesen["scroll_y_subpx"], "death", mesen.get("death_pc"), mesen.get("death_ctx"),
        )