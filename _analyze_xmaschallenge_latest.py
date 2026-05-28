import csv
import glob
import os
import re
import sys
from collections import defaultdict


LEVEL = sys.argv[1] if len(sys.argv) > 1 else "xmaschallenge"
PF_LEVEL = sys.argv[2] if len(sys.argv) > 2 else LEVEL
TEMP = os.environ.get("TEMP") or os.environ.get("TMP") or r"C:\Users\kando\AppData\Local\Temp"


def newest(pattern):
    matches = [p for p in glob.glob(os.path.join(TEMP, pattern)) if os.path.isfile(p) and os.path.getsize(p) > 0]
    if not matches:
        return None
    return max(matches, key=os.path.getmtime)


def s32(value):
    n = int(value)
    if n > 0x7FFFFFFF:
        n -= 0x100000000
    return n


def signed_hex(value):
    text = str(value)
    if text.lower().startswith("0x"):
        n = int(text, 16)
        if n & 0x80000000:
            n -= 0x100000000
        return n
    return int(text)


def h16(value):
    return f"0x{int(value) & 0xFFFF:04X}"


def load_mesen(path):
    with open(path, newline="", encoding="utf-8-sig") as handle:
        first = handle.readline()
        if not first.startswith("nes_y_offset"):
            handle.seek(0)
        header = handle.readline().rstrip("\r\n").split(",")
        rows = []
        for line in handle:
            line = line.rstrip("\r\n")
            if not line:
                continue
            parts = line.split(",")
            if len(parts) < len(header):
                continue
            if len(parts) > len(header):
                # death_ctx contains unquoted comma-bearing fields like G=(50,78,15x15).
                fixed = parts[:22] + [",".join(parts[22:-7])] + parts[-7:]
            else:
                fixed = parts
            row = dict(zip(header, fixed))
            if row.get("sim_cursor"):
                rows.append(row)
        return rows


def load_pf(path):
    with open(path, newline="", encoding="utf-8-sig") as handle:
        return list(csv.DictReader(handle))


def parse_full(path):
    by_frame = defaultdict(list)
    if not path:
        return by_frame
    pattern = re.compile(r"^f=(\d+)\s+cur=(\d+)\s+gm=(\d+)\s+tag=([^\s]+)\s*(.*)$")
    kv_pattern = re.compile(r"([^=\s]+)=([^\s]+)")
    with open(path, encoding="utf-8", errors="replace") as handle:
        for line in handle:
            match = pattern.match(line.strip())
            if not match:
                continue
            frame = int(match.group(1))
            row = {"cur": int(match.group(2)), "gm": int(match.group(3)), "tag": match.group(4)}
            row.update({k: v for k, v in kv_pattern.findall(match.group(5))})
            by_frame[frame].append(row)
    return by_frame


def first_by_sim(rows):
    by_sim = {}
    for row in rows:
        sim = int(row["sim_cursor"])
        by_sim.setdefault(sim, row)
    return by_sim


def print_rows(title, rows, fields):
    print(title)
    if not rows:
        print("  <none>")
        return
    widths = {field: len(field) for field in fields}
    rendered = []
    for row in rows:
        item = {field: str(row.get(field, "")) for field in fields}
        rendered.append(item)
        for field, value in item.items():
            widths[field] = max(widths[field], len(value))
    print("  " + " ".join(field.rjust(widths[field]) for field in fields))
    for row in rendered:
        print("  " + " ".join(row[field].rjust(widths[field]) for field in fields))


def main():
    mesen = newest(f"famidash_mesen_trace_{LEVEL}_*.csv")
    pf = newest(f"famidash_pf_trace_*{PF_LEVEL}*.csv")
    pf_full = newest(f"famidash_pf_trace_FULL_*{PF_LEVEL}*.log")
    pf_debug = newest(f"famidash_pf_debug_*{PF_LEVEL}*.txt")
    mesen_physics = newest(f"famidash_mesen_physics_debug_{LEVEL}_*.log")
    mesen_orb = newest(f"famidash_mesen_orb_debug_{LEVEL}_*.log")

    print(f"Latest non-empty {LEVEL} logs:")
    for label, path in [
        ("Mesen trace", mesen),
        ("Mesen physics", mesen_physics),
        ("Mesen orb", mesen_orb),
        ("PF trace", pf),
        ("PF FULL", pf_full),
        ("PF debug", pf_debug),
    ]:
        if path:
            print(f"  {label:14} {os.path.basename(path)} ({os.path.getsize(path)} bytes)")
        else:
            print(f"  {label:14} <missing>")

    if not mesen or not pf:
        raise SystemExit("Need both Mesen and PF CSV traces.")

    m_rows = load_mesen(mesen)
    p_rows = load_pf(pf)
    p_by_frame = {int(row["frame"]): row for row in p_rows}
    full_by_frame = parse_full(pf_full)

    m_sims = [int(row["sim_cursor"]) for row in m_rows]
    m_roms = [int(row["rom_frame"]) for row in m_rows]
    p_frames = [int(row["frame"]) for row in p_rows]
    print()
    print(f"Mesen rows={len(m_rows)} sim={min(m_sims)}..{max(m_sims)} rom={min(m_roms)}..{max(m_roms)}")
    print(f"PF rows={len(p_rows)} frame={min(p_frames)}..{max(p_frames)}")

    deaths = [row for row in m_rows if row.get("death_pc") or row.get("death_ctx")]
    death_summary = []
    for row in deaths[:10]:
        death_summary.append({
            "rom": int(row["rom_frame"]),
            "sim": int(row["sim_cursor"]),
            "x": s32(row["px"]),
            "y": int(row["py"]),
            "rawY": h16(row["raw_y"]),
            "vy": int(row["vel_y"]),
            "mode": int(row["gamemode"]),
            "grav": int(row["cp_gravity"]),
            "pc": row.get("death_pc", ""),
            "ctx": row.get("death_ctx", ""),
        })
    print_rows("\nMesen death rows:", death_summary, ["rom", "sim", "x", "y", "rawY", "vy", "mode", "grav", "pc", "ctx"])

    transitions = []
    prev = None
    for row in m_rows:
        key = (row.get("gamemode"), row.get("dual"), row.get("cp_gravity"), row.get("mini"))
        if key != prev:
            transitions.append({
                "rom": int(row["rom_frame"]),
                "sim": int(row["sim_cursor"]),
                "x": s32(row["px"]),
                "y": int(row["py"]),
                "rawY": h16(row["raw_y"]),
                "vy": int(row["vel_y"]),
                "mode": int(row["gamemode"]),
                "dual": int(row["dual"]),
                "grav": int(row["cp_gravity"]),
                "mini": int(row["mini"]),
                "input": int(row["a_cur"]),
            })
            prev = key
    print_rows("\nMesen mode/dual/gravity/mini transitions:", transitions, ["rom", "sim", "x", "y", "rawY", "vy", "mode", "dual", "grav", "mini", "input"])

    first_sim = first_by_sim(m_rows)
    scored = []
    for offset in range(-20, 21):
        count = 0
        exact = 0
        abs_dx = abs_dy = abs_dv = 0
        for sim, m in first_sim.items():
            if sim < 5:
                continue
            frame = sim + offset
            p = p_by_frame.get(frame)
            if not p:
                continue
            mx = s32(m["px"])
            my = int(m["py"])
            mv = int(m["vel_y"])
            px = int(p.get("X_px", 0))
            py = int(p.get("Y_px", 0))
            pv = signed_hex(p.get("VelY_fixed", "0"))
            count += 1
            abs_dx += abs(mx - px)
            abs_dy += abs(my - py)
            abs_dv += abs(mv - pv)
            if mx == px and my == py and mv == pv:
                exact += 1
        if count:
            scored.append((exact, -abs_dx, -abs_dy, -abs_dv, offset, count, abs_dx, abs_dy, abs_dv))
    scored.sort(reverse=True)
    print("\nBest display/velocity alignment candidates (frame = sim + offset):")
    for exact, ndx, ndy, ndv, offset, count, abs_dx, abs_dy, abs_dv in scored[:8]:
        print(f"  offset={offset:3d} count={count:4d} exact={exact:4d} abs_dx={abs_dx:7d} abs_dy={abs_dy:5d} abs_dv={abs_dv:7d}")

    offset = -1
    death_sim = int(deaths[0]["sim_cursor"]) if deaths else max(m_sims)
    print(f"\nUsing parity convention frame = sim - 1. Death sim/window center: {death_sim}")

    mismatches = []
    for sim in sorted(first_sim):
        if sim < 2:
            continue
        m = first_sim[sim]
        p = p_by_frame.get(sim - 1)
        if not p:
            continue
        mx = s32(m["px"])
        my = int(m["py"])
        mv = int(m["vel_y"])
        px = int(p.get("X_px", 0))
        py = int(p.get("Y_px", 0))
        pv = signed_hex(p.get("VelY_fixed", "0"))
        mi = int(m.get("a_cur") or 0)
        pi = int(p.get("input") or 0)
        alive = int(p.get("alive") or 0)
        if mx != px or my != py or mv != pv or mi != pi or alive != 1:
            mismatches.append({
                "rom": int(m["rom_frame"]),
                "sim": sim,
                "frame": sim - 1,
                "mx": mx,
                "pfX": px,
                "dx": mx - px,
                "my": my,
                "pfY": py,
                "dy": my - py,
                "mvy": mv,
                "pfVy": pv,
                "dvy": mv - pv,
                "mi": mi,
                "pfI": pi,
                "alive": alive,
                "mode": int(m.get("gamemode") or 0),
                "grav": int(m.get("cp_gravity") or 0),
                "rawY": h16(m["raw_y"]),
            })
    print_rows("\nFirst mismatches under frame=sim-1:", mismatches[:40], ["rom", "sim", "frame", "mx", "pfX", "dx", "my", "pfY", "dy", "mvy", "pfVy", "dvy", "mi", "pfI", "alive", "mode", "grav", "rawY"])

    window = []
    for sim in range(max(2, death_sim - 20), death_sim + 8):
        m = first_sim.get(sim)
        p = p_by_frame.get(sim - 1)
        if not m:
            continue
        full = full_by_frame.get(sim - 1, [])
        death_tags = [r for r in full if r.get("tag") == "CheckDeathCollision.in" or r.get("dblk") not in (None, "0")]
        pf_mode = ""
        if full:
            pf_mode = str(full[0].get("gm", ""))
        window.append({
            "rom": int(m["rom_frame"]),
            "sim": sim,
            "frame": sim - 1,
            "mx": s32(m["px"]),
            "pfX": int(p.get("X_px", 0)) if p else "",
            "my": int(m["py"]),
            "pfY": int(p.get("Y_px", 0)) if p else "",
            "mvy": int(m["vel_y"]),
            "pfVy": signed_hex(p.get("VelY_fixed", "0")) if p else "",
            "mi": int(m.get("a_cur") or 0),
            "pfI": int(p.get("input") or 0) if p else "",
            "alive": int(p.get("alive") or 0) if p else "",
            "mMode": int(m.get("gamemode") or 0),
            "pfGm": pf_mode,
            "grav": int(m.get("cp_gravity") or 0),
            "rawY": h16(m["raw_y"]),
            "death": (m.get("death_pc") or m.get("death_ctx") or ""),
            "pfDeathTags": ";".join(f"{r.get('tag')}:{r.get('dblk', '')}" for r in death_tags[:2]),
        })
    print_rows("\nDeath-window compare:", window, ["rom", "sim", "frame", "mx", "pfX", "my", "pfY", "mvy", "pfVy", "mi", "pfI", "alive", "mMode", "pfGm", "grav", "rawY", "death", "pfDeathTags"])

    interesting = []
    for frame in range(max(0, death_sim - 25), death_sim + 10):
        for row in full_by_frame.get(frame, []):
            tag = row.get("tag", "")
            if tag in {"CheckDeathCollision.in", "CheckForwardCollision.in", "CheckFloorSpikes2.in", "CheckCeiling.in", "CheckFloorDetailed.in", "SP.ShipUfoEject.in"} or row.get("dblk") not in (None, "0"):
                interesting.append({
                    "f": frame,
                    "gm": row.get("gm", ""),
                    "tag": tag,
                    "X": row.get("X", row.get("cx", "")),
                    "Y": row.get("Y", row.get("cy", "")),
                    "w": row.get("hbW", row.get("w", "")),
                    "h": row.get("hbH", row.get("h", "")),
                    "vy": row.get("vy", row.get("vyf", "")),
                    "dblk": row.get("dblk", ""),
                    "mode": row.get("mode", ""),
                    "gF": row.get("gF", ""),
                })
    print_rows("\nPF FULL tags near death:", interesting[:120], ["f", "gm", "tag", "X", "Y", "w", "h", "vy", "dblk", "mode", "gF"])


if __name__ == "__main__":
    main()