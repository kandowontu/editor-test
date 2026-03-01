import csv
import io

tmx_path = r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\polargeist.tmx"

# Parse the TMX file
with open(tmx_path, 'r') as f:
    content = f.read()

# Extract the two CSV data sections
import re
data_sections = re.findall(r'<data encoding="csv">\s*(.*?)\s*</data>', content, re.DOTALL)

metatile_data = data_sections[0]  # first layer (metatile)
sp_data = data_sections[1]        # SP layer

def parse_csv_grid(csv_text):
    rows = []
    for line in csv_text.strip().split('\n'):
        line = line.strip().rstrip(',')
        if line:
            rows.append([int(x) for x in line.split(',')])
    return rows

meta_grid = parse_csv_grid(metatile_data)
sp_grid = parse_csv_grid(sp_data)

print(f"Metatile grid: {len(meta_grid)} rows x {len(meta_grid[0])} cols")
print(f"SP grid: {len(sp_grid)} rows x {len(sp_grid[0])} cols")

# === 1. SP LAYER: Game mode portals in columns 500-610 ===
# Sprites use firstgid=257, so raw value = spriteID + 257
# Portal IDs: 0x01=cube(258), 0x02=ship(259), 0x04=ball(261), 0x05=UFO(262)
portal_ids = {258: "CUBE(0x01)", 259: "SHIP(0x02)", 261: "BALL(0x04)", 262: "UFO(0x05)"}

print("\n=== SP LAYER: Game Mode Portals in columns 500-610 ===")
print("(X_px = col*16, Y_px = row*16)")
for row_idx in range(len(sp_grid)):
    for col_idx in range(500, min(611, len(sp_grid[row_idx]))):
        val = sp_grid[row_idx][col_idx]
        if val != 0:
            sprite_id = val - 257
            x_px = col_idx * 16
            y_px = row_idx * 16
            portal_str = portal_ids.get(val, "")
            print(f"  col={col_idx} row={row_idx} val={val} (sprite 0x{sprite_id:02X}) X={x_px} Y={y_px} {portal_str}")

# === Also check a wider range for context ===
print("\n=== SP LAYER: ALL non-zero sprites in columns 490-620 ===")
for row_idx in range(len(sp_grid)):
    for col_idx in range(490, min(621, len(sp_grid[row_idx]))):
        val = sp_grid[row_idx][col_idx]
        if val != 0:
            sprite_id = val - 257
            x_px = col_idx * 16
            y_px = row_idx * 16
            portal_str = portal_ids.get(val, "")
            print(f"  col={col_idx} row={row_idx} val={val} (sprite 0x{sprite_id:02X}) X={x_px} Y={y_px} {portal_str}")

# === 2. METATILE LAYER: columns 520-570, rows 20-26 ===
print("\n=== METATILE LAYER: columns 520-570, rows 20-26 ===")
print("(X_px = col*16, Y_px = row*16, groundRowsToReserve=3 means world_Y = row*16)")
print(f"{'col':>4} {'X_px':>5} | ", end="")
for r in range(20, 27):
    print(f"r{r}(Y{r*16})", end=" ")
print()
print("-" * 80)

for col_idx in range(520, 571):
    x_px = col_idx * 16
    vals = []
    has_nonzero = False
    for r in range(20, 27):
        v = meta_grid[r][col_idx]
        vals.append(v)
        if v != 0:
            has_nonzero = True
    if has_nonzero:
        print(f"{col_idx:>4} {x_px:>5} | ", end="")
        for v in vals:
            if v == 0:
                print(f"{'---':>8}", end=" ")
            else:
                print(f"{v:>8}", end=" ")
        print()

# === 3. Wider view: columns 525-565 rows 0-26 to see full picture ===
print("\n=== METATILE LAYER: columns 525-565, ALL rows with non-zero tiles ===")
for col_idx in range(525, 566):
    x_px = col_idx * 16
    nonzero_rows = []
    for r in range(27):
        v = meta_grid[r][col_idx]
        if v != 0:
            nonzero_rows.append((r, r*16, v))
    if nonzero_rows:
        items = ", ".join([f"row{r}(Y{y})=tile{v}" for r, y, v in nonzero_rows])
        print(f"  col={col_idx} X={x_px}: {items}")

# === 4. Focus on X=8991 area ===
# X=8991 / 16 = 561.9 -> column 561 or 562
# Y=353 / 16 = 22.06 -> row 22
print("\n=== Focus: X=8991 is col ~562, Y=353 is row ~22 ===")
print("Metatile columns 555-570, rows 18-26:")
for r in range(18, 27):
    print(f"  row {r:>2} (Y={r*16:>3}-{r*16+15:>3}): ", end="")
    for c in range(555, 571):
        v = meta_grid[r][c]
        if v != 0:
            print(f"c{c}={v}", end=" ")
    print()

# === 5. Check for ship portal before X=8400 too ===
print("\n=== SP LAYER: ALL game mode portals in columns 400-620 ===")
for row_idx in range(len(sp_grid)):
    for col_idx in range(400, min(621, len(sp_grid[row_idx]))):
        val = sp_grid[row_idx][col_idx]
        if val in portal_ids:
            sprite_id = val - 257
            x_px = col_idx * 16
            y_px = row_idx * 16
            print(f"  col={col_idx} row={row_idx} val={val} (sprite 0x{sprite_id:02X}) X={x_px} Y={y_px} {portal_ids[val]}")
