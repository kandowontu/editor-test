"""Analyze all sprites in xstep.tmx SP layer, categorize them, and report left-to-right."""

import xml.etree.ElementTree as ET
from collections import defaultdict

# ---------- Known type-ID categories ----------
SPEED_PORTALS = {
    20: "0.5x speed", 21: "1x speed", 22: "2x speed",
    32: "3x speed", 33: "4x speed", 109: "slow (0.5x)"
}

GAME_MODE_PORTALS = {
    0: "cube", 1: "ship", 2: "ball", 3: "ufo",
    4: "robot", 23: "spider", 36: "wave", 75: "swing"
}

ORBS = {
    5: "blue orb", 6: "pink orb", 7: "green orb", 8: "yellow orb", 9: "red orb",
}
# Extend orbs for 0x44-0x5E range
for i in range(0x44, 0x5F):
    ORBS[i] = f"orb_0x{i:02X}"

PADS = {
    10: "yellow pad", 11: "pink pad (up)", 12: "gravity pad",
    13: "pad_0x0D", 14: "pad_0x0E",
    26: "pad_0x1A", 27: "pad_0x1B",
}

SPECIAL = {
    15: "LEVEL_END_TRIGGER",
    16: "type_0x10",
    19: "type_0x13",
}

def categorize(tid):
    if tid in SPEED_PORTALS:
        return "SPEED_PORTAL", SPEED_PORTALS[tid]
    if tid in GAME_MODE_PORTALS:
        return "GAME_MODE_PORTAL", GAME_MODE_PORTALS[tid]
    if tid in ORBS:
        return "ORB", ORBS[tid]
    if tid in PADS:
        return "PAD", PADS[tid]
    if tid in SPECIAL:
        return "SPECIAL", SPECIAL[tid]
    if 0x80 <= tid <= 0xEF:
        return "COLOR_TRIGGER", f"color_0x{tid:02X}"
    if 0xF0 <= tid <= 0xFF:
        return "HIGH_TRIGGER", f"trig_0x{tid:02X}"
    return "OTHER", f"type_0x{tid:02X}"


# ---------- Parse TMX ----------
tree = ET.parse(r"C:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\xstep.tmx")
root = tree.getroot()
map_w = int(root.attrib["width"])
map_h = int(root.attrib["height"])

sp_layer = None
for layer in root.findall("layer"):
    if layer.get("name") == "SP":
        sp_layer = layer
        break

csv_text = sp_layer.find("data").text.strip()

sprites = []  # (col, row, gid, type_id, category, label)
rows = csv_text.split("\n")
for row_idx, row_str in enumerate(rows):
    row_str = row_str.strip().rstrip(",")
    if not row_str:
        continue
    vals = row_str.split(",")
    for col_idx, val_str in enumerate(vals):
        gid = int(val_str.strip()) if val_str.strip() else 0
        if gid > 0:
            tid = gid - 257
            cat, label = categorize(tid)
            sprites.append((col_idx, row_idx, gid, tid, cat, label))

# Sort left to right (by column), then top to bottom
sprites.sort(key=lambda s: (s[0], s[1]))

# ---------- Summary by type_id ----------
print("=" * 100)
print(f"xstep.tmx sprite layer analysis  |  Map: {map_w}x{map_h}  |  Total sprites: {len(sprites)}")
print("=" * 100)

# Count per type
type_counts = defaultdict(int)
type_cols = defaultdict(list)
type_info = {}
for col, row, gid, tid, cat, label in sprites:
    type_counts[tid] += 1
    type_cols[tid].append(col)
    type_info[tid] = (cat, label)

print(f"\n{'TID':>5} {'Hex':>6} {'GID':>5} {'Category':<18} {'Label':<22} {'Count':>5}  Columns (first 20)")
print("-" * 120)
for tid in sorted(type_counts.keys()):
    cat, label = type_info[tid]
    cols = type_cols[tid]
    col_preview = ", ".join(str(c) for c in cols[:20])
    if len(cols) > 20:
        col_preview += f" ... (+{len(cols)-20} more)"
    print(f"{tid:>5} 0x{tid:02X}   {tid+257:>5} {cat:<18} {label:<22} {type_counts[tid]:>5}  {col_preview}")

# ---------- Category summary ----------
cat_counts = defaultdict(int)
for _, _, _, _, cat, _ in sprites:
    cat_counts[cat] += 1

print(f"\n{'Category':<20} {'Count':>6}")
print("-" * 30)
for cat in sorted(cat_counts.keys()):
    print(f"{cat:<20} {cat_counts[cat]:>6}")

# ---------- Full left-to-right listing ----------
print("\n" + "=" * 100)
print("FULL SPRITE LIST  (sorted left-to-right by column, then top-to-bottom by row)")
print("=" * 100)
print(f"{'Col':>5} {'Row':>4} {'GID':>5} {'TID':>5} {'TIDhex':>8} {'Category':<18} {'Label'}")
print("-" * 100)
for col, row, gid, tid, cat, label in sprites:
    print(f"{col:>5} {row:>4} {gid:>5} {tid:>5} 0x{tid:02X}     {cat:<18} {label}")
