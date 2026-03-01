import re

# Read metatile collision table (0-indexed, line 0 = tile 0)
with open(r"c:\Editor Test\metatile_collision_table.txt") as f:
    col_table = [line.strip() for line in f.readlines()]

# Tiles we care about
tiles_of_interest = [13, 17, 18, 25, 26, 28, 46, 49, 51]
print("=== Metatile Collision Types ===")
for t in tiles_of_interest:
    if t < len(col_table):
        print(f"  Tile {t:3d} (0x{t:02X}): {col_table[t]}")

# Also check the SP sprite IDs
print("\n=== Sprite ID reference ===")
sprite_ids = {
    0x01: "Cube portal", 0x02: "Ship portal", 0x04: "Ball portal", 0x05: "UFO portal",
    0x0A: "?", 0x0B: "?", 0x1A: "?",
    0x2B: "?", 0x2D: "?", 0x2E: "?", 0x30: "?", 0x3D: "?"
}

# Read TMX and look for all portals in the wider range
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

# Search entire SP layer for game mode portals (258=cube, 259=ship, 261=ball, 262=UFO)
portal_vals = {258: "CUBE", 259: "SHIP", 261: "BALL", 262: "UFO"}
print("\n=== ALL game mode portals in entire map ===")
for row_idx in range(len(sp_grid)):
    for col_idx in range(len(sp_grid[row_idx])):
        val = sp_grid[row_idx][col_idx]
        if val in portal_vals:
            x_px = col_idx * 16
            y_px = row_idx * 16
            print(f"  col={col_idx} row={row_idx} X={x_px} Y={y_px} {portal_vals[val]}")

# Detailed terrain from col 549 to 565 (gap area)
print("\n=== DETAILED TERRAIN: cols 549-575, all rows ===")
for col_idx in range(549, 576):
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
        print(f"  col={col_idx} X={x_px}: (empty)")

# Check tiles 550-560 in the SP layer
print("\n=== SP LAYER cols 549-570 (all non-zero) ===")
for row_idx in range(len(sp_grid)):
    for col_idx in range(549, 571):
        val = sp_grid[row_idx][col_idx]
        if val != 0:
            sprite_id = val - 257
            x_px = col_idx * 16
            y_px = row_idx * 16
            print(f"  col={col_idx} row={row_idx} X={x_px} Y={y_px} sprite=0x{sprite_id:02X} (raw={val})")
