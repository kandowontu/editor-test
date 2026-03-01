import re

tmx_path = r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\polargeist.tmx"
with open(tmx_path, 'r') as f:
    content = f.read()

data_sections = re.findall(r'<data encoding="csv">\s*(.*?)\s*</data>', content, re.DOTALL)
sp_data = data_sections[1]
meta_data = data_sections[0]

def parse_csv_grid(csv_text):
    rows = []
    for line in csv_text.strip().split('\n'):
        line = line.strip().rstrip(',')
        if line:
            rows.append([int(x) for x in line.split(',')])
    return rows

sp_grid = parse_csv_grid(sp_data)
meta_grid = parse_csv_grid(meta_data)

# Collision table
with open(r"c:\Editor Test\metatile_collision_table.txt") as f:
    col_table = [line.strip() for line in f.readlines()]

# Classify ALL sprites
def classify_sprite(sid):
    # Game mode portals
    gm = {0x00: "CUBE_PORTAL", 0x01: "SHIP_PORTAL", 0x02: "BALL_PORTAL", 0x03: "UFO_PORTAL",
           0x04: "ROBOT_PORTAL", 0x17: "MODE5_PORTAL", 0x24: "MODE6_PORTAL", 0x4B: "MODE7_PORTAL",
           0x58: "MODE8_PORTAL", 0x6A: "MODE9_PORTAL", 0x6B: "MODE10_PORTAL", 0x6C: "MODE11_PORTAL"}
    if sid in gm: return gm[sid]
    
    # Speed portals
    sp = {0x6D: "SPEED_0.5x", 0x14: "SPEED_1x", 0x15: "SPEED_2x", 0x16: "SPEED_3x", 0x20: "SPEED_4x", 0x21: "SPEED_5x"}
    if sid in sp: return sp[sid]
    
    # Gravity portals
    grav = {0x08: "GRAV_NORMAL", 0x09: "GRAV_REVERSE", 0x10: "GRAV_NORMAL2", 0x11: "GRAV_REVERSE2",
            0x12: "GRAV_REV3", 0x13: "GRAV_REV4", 0xFB: "GRAV_REV5", 0xFC: "GRAV_NORM6"}
    if sid in grav: return grav[sid]
    
    # Mini/growth portals
    if sid == 0x18: return "MINI_PORTAL"
    if sid == 0x19: return "GROWTH_PORTAL"
    
    # End level
    if sid == 0x0F: return "END_LEVEL"
    
    # Pads
    pads = {0x0A: "YELLOW_PAD_BOT", 0x0C: "YELLOW_PAD_TOP", 0x25: "PINK_PAD_BOT", 0x26: "PINK_PAD_TOP",
            0x52: "RED_PAD_BOT", 0x53: "RED_PAD_TOP", 0x0D: "BLUE_PAD_BOT", 0x0E: "BLUE_PAD_TOP",
            0xFD: "BLUE_PAD_BOT2", 0xFE: "BLUE_PAD_TOP2", 0x65: "GREEN_PAD"}
    if sid in pads: return pads[sid]
    
    # Orbs
    orbs = {0x0B: "YELLOW_ORB", 0x1F: "YELLOW_ORB_BIG", 0x29: "YELLOW_ORB_SMALL",
            0x06: "PINK_ORB", 0x28: "RED_ORB", 0x05: "BLUE_ORB", 0x7B: "BLUE_ORB_MULTI",
            0x27: "GREEN_ORB", 0x7C: "GREEN_ORB_MULTI", 0x44: "BLACK_ORB", 0x7A: "WHITE_ORB"}
    if sid in orbs: return orbs[sid]
    
    # Coins
    if sid == 0x07: return "SECRET_COIN"
    if sid == 0x1A: return "COIN2"
    if sid == 0x1B: return "COIN3"
    
    return f"DECO(0x{sid:02X})"

# Show all FUNCTIONAL sprites in cols 480-580
print("=== ALL FUNCTIONAL SPRITES in cols 480-580 ===")
print("(Excluding decorations)")
for row_idx in range(len(sp_grid)):
    for col_idx in range(480, min(581, len(sp_grid[row_idx]))):
        val = sp_grid[row_idx][col_idx]
        if val != 0:
            sid = val - 257
            cls = classify_sprite(sid)
            if not cls.startswith("DECO"):
                x_px = col_idx * 16
                y_px = row_idx * 16
                print(f"  col={col_idx} row={row_idx} X={x_px} Y={y_px} sid=0x{sid:02X} -> {cls}")

# Show ALL sprites (incl decoration) in cols 540-575
print("\n=== ALL SPRITES in cols 540-575 (including decoration) ===")
for row_idx in range(len(sp_grid)):
    for col_idx in range(540, min(576, len(sp_grid[row_idx]))):
        val = sp_grid[row_idx][col_idx]
        if val != 0:
            sid = val - 257
            cls = classify_sprite(sid)
            x_px = col_idx * 16
            y_px = row_idx * 16
            print(f"  col={col_idx} row={row_idx} X={x_px} Y={y_px} sid=0x{sid:02X} -> {cls}")

# Show complete metatile map for cols 520-570, ALL ROWS
print("\n=== COMPLETE METATILE MAP: cols 515-570, all rows 0-26 ===")
for col_idx in range(515, 571):
    x_px = col_idx * 16
    entries = []
    for r in range(27):
        v = meta_grid[r][col_idx]
        if v != 0:
            collision = col_table[v] if v < len(col_table) else "?"
            entries.append(f"r{r}(Y{r*16})=t{v}({collision})")
    if entries:
        print(f"  col={col_idx} X={x_px}: {', '.join(entries)}")
    else:
        print(f"  col={col_idx} X={x_px}: EMPTY")

# Visual grid of the staircase area
print("\n=== VISUAL MAP: cols 520-570, rows 14-26 ===")
print("Legend: ## = solid(COL_ALL), DD = death, TT = top-only, !! = death_top, __ = death_bottom, .. = empty")
print(f"{'':>4}", end="")
for c in range(520, 571):
    print(f"{c%100:2d}", end=" ")
print()
for r in range(14, 27):
    print(f"r{r:2d} ", end="")
    for c in range(520, 571):
        v = meta_grid[r][c]
        if v == 0:
            print(" . ", end="")
        else:
            ct = col_table[v] if v < len(col_table) else "?"
            if "ALL" in ct:
                print("##", end=" ")
            elif ct == "COL_DEATH":
                print("DD", end=" ")
            elif ct == "COL_TOP":
                print("TT", end=" ")
            elif ct == "COL_DEATH_TOP":
                print("!!", end=" ")
            elif ct == "COL_DEATH_BOTTOM":
                print("__", end=" ")
            else:
                print(f"??", end=" ")
    print(f" Y={r*16}")
