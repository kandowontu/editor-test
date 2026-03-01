import re

tmx_path = r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\polargeist.tmx"
with open(tmx_path, 'r') as f:
    content = f.read()

data_sections = re.findall(r'<data encoding="csv">\s*(.*?)\s*</data>', content, re.DOTALL)
sp_data = data_sections[1]

def parse_csv_grid(csv_text):
    rows = []
    for line in csv_text.strip().split('\n'):
        line = line.strip().rstrip(',')
        if line:
            rows.append([int(x) for x in line.split(',')])
    return rows

sp_grid = parse_csv_grid(sp_data)

# CORRECT mapping from PathfinderEngine.cs SpriteIdToGameMode:
# 0x00 => Cube (mode 0),  raw=257
# 0x01 => Ship (mode 1),  raw=258
# 0x02 => Ball (mode 2),  raw=259
# 0x03 => UFO (mode 3),   raw=260
# 0x04 => Robot (mode 4), raw=261
# 0x17 => mode 5,         raw=280
# 0x24 => mode 6,         raw=293
portal_vals = {
    257: "CUBE(0x00,mode0)", 258: "SHIP(0x01,mode1)", 259: "BALL(0x02,mode2)",
    260: "UFO(0x03,mode3)", 261: "ROBOT(0x04,mode4)", 280: "mode5(0x17)",
    293: "mode6(0x24)"
}

print("=== ALL game mode portals in entire map (CORRECTED) ===")
for row_idx in range(len(sp_grid)):
    for col_idx in range(len(sp_grid[row_idx])):
        val = sp_grid[row_idx][col_idx]
        if val in portal_vals:
            x_px = col_idx * 16
            y_px = row_idx * 16
            print(f"  col={col_idx} row={row_idx} X={x_px} Y={y_px} -> {portal_vals[val]}")

# Also list ALL unique sprite IDs in the map for reference
unique_sprites = set()
for row in sp_grid:
    for val in row:
        if val != 0:
            unique_sprites.add(val)
print(f"\n=== ALL unique SP tile values in map: ===")
for v in sorted(unique_sprites):
    sid = v - 257
    print(f"  raw={v} sprite=0x{sid:02X} ({sid})")

# Also: check what sprite 0x2D (raw 302) is - it appears along the staircase
# And sprite 0x2B (raw 300), 0x3D (raw 318)
print("\n=== Gravity portal candidates ===")
# Common GD sprite IDs for gravity portals, speed portals, etc.
# Let me check pathfinder for gravity references
