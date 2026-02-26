import csv
import io

# Read the TMX file and extract tile data
tmx_path = r"C:\Editor Test\famidash\levels\LEVEL DATA\lvlset_HUGE\baseafterbase.tmx"

with open(tmx_path, 'r') as f:
    content = f.read()

# Find the CSV data between <data encoding="csv"> and </data>
start_marker = '<data encoding="csv">'
end_marker = '</data>'
csv_start = content.index(start_marker) + len(start_marker)
csv_end = content.index(end_marker)
csv_data = content[csv_start:csv_end].strip()

# Parse CSV - each line is one row of the map
rows = []
for line in csv_data.split('\n'):
    line = line.strip().rstrip(',')
    if line:
        values = [int(x.strip()) for x in line.split(',')]
        rows.append(values)

print(f"Map dimensions: {len(rows[0])} cols x {len(rows)} rows")
print(f"TMX firstgid=1, so GAME TILE = TMX TILE - 1")
print()

# CRITICAL: TMX firstgid=1, so game_tile = tmx_tile - 1
# Game collision tile mapping (after subtracting 1):
# 0x00 (0)  = empty (TMX 0 is always empty, no subtraction)
# 0x01-0x07 = background (non-collision) => TMX 2-8
# 0x0C (12) = COL_DEATH_BOTTOM => TMX 13
# 0x10 (16) = COL_ALL (solid) => TMX 17
# 0x18 (24) = COL_DEATH_BOTTOM => TMX 25
# 0x19 (25) = COL_TOP => TMX 26
# 0x1B (27) = COL_DEATH => TMX 28
# 0x1F (31) = COL_DEATH_RIGHT => TMX 32

def game_tile(tmx_tile):
    """Convert TMX tile ID to game collision tile ID"""
    if tmx_tile == 0:
        return 0  # empty
    return tmx_tile - 1

def collision_name(tmx_tile):
    """Returns collision name based on GAME tile (tmx - 1)"""
    if tmx_tile == 0:
        return "EMPTY"
    gt = tmx_tile - 1
    if gt in range(1, 8):
        return f"BG({gt})"
    mapping = {
        0x0C: "DEATH_BOTTOM",
        0x10: "COL_ALL",
        0x18: "DEATH_BOTTOM",
        0x19: "COL_TOP",
        0x1A: "COL_26",
        0x1B: "COL_DEATH",
        0x1C: "COL_28",
        0x1F: "DEATH_RIGHT",
    }
    if gt in mapping:
        return mapping[gt]
    return f"G{gt}(T{tmx_tile})"

def collision_short(tmx_tile):
    """Short collision type for grid display"""
    if tmx_tile == 0:
        return "----"
    gt = tmx_tile - 1
    if gt in range(1, 8):
        return f"bg{gt}"
    mapping = {
        0x0C: "DthB",
        0x10: "SOLI",
        0x18: "DthB",
        0x19: "TOP",
        0x1A: "T26",
        0x1B: "DETH",
        0x1C: "T28",
        0x1F: "DTH>",  # Death Right
    }
    if gt in mapping:
        return mapping[gt]
    return f"g{gt:02X}"

def is_dangerous(tmx_tile):
    if tmx_tile == 0:
        return False
    gt = tmx_tile - 1
    return gt in [0x0C, 0x18, 0x1B, 0x1F, 0x1C]

def is_solid(tmx_tile):
    if tmx_tile == 0:
        return False
    gt = tmx_tile - 1
    return gt in [0x10, 0x19]

# =====================================================================
# SECTION 1: Raw tile IDs AND game tile IDs for cols 465-485
# =====================================================================
print("=" * 130)
print("SECTION 1: TILE DATA - Columns 465-485, ALL 27 rows")
print("  Format: TMX_ID(game_hex)")  
print("  Column pixel X = col * 16, Row pixel Y = row * 16")
print("=" * 130)

col_start = 465
col_end = 485

# Header
print(f"{'Row':>3} {'Y':>4} |", end="")
for c in range(col_start, col_end + 1):
    print(f" {c:>7}", end="")
print()
print("-" * (11 + 8 * (col_end - col_start + 1)))

for r in range(len(rows)):
    y_px = r * 16
    print(f"{r:>3} {y_px:>4} |", end="")
    for c in range(col_start, col_end + 1):
        val = rows[r][c]
        if val == 0:
            print("       .", end="")
        else:
            gt = val - 1
            print(f" {val:>2}({gt:02X})", end="")
    print()

# =====================================================================
# SECTION 2: Collision-type grid for cols 465-485
# =====================================================================
print()
print("=" * 130)
print("SECTION 2: COLLISION TYPE GRID - Columns 465-485")
print("  DTH> = COL_DEATH_RIGHT, DthB = COL_DEATH_BOTTOM, SOLI = COL_ALL")
print("  TOP = COL_TOP, DETH = COL_DEATH, bg# = background, ---- = empty")
print("=" * 130)

print(f"{'Row':>3} {'Y':>4} |", end="")
for c in range(col_start, col_end + 1):
    print(f" {c:>5}", end="")
print()
print("-" * (11 + 6 * (col_end - col_start + 1)))

for r in range(len(rows)):
    y_px = r * 16
    print(f"{r:>3} {y_px:>4} |", end="")
    for c in range(col_start, col_end + 1):
        val = rows[r][c]
        cs = collision_short(val)
        print(f" {cs:>5}", end="")
    print()

# =====================================================================
# SECTION 3: The death wall at col 472 (X=7552)
# =====================================================================
print()
print("=" * 130)
print("SECTION 3: THE DEATH WALL - Column 472 (X=7552-7567) detailed breakdown")
print("=" * 130)

c = 472
x_px = c * 16
print(f"\nColumn {c} (X={x_px}-{x_px+15}):")
print(f"{'Row':>4} {'Y range':>12} {'TMX':>4} {'Game':>5} {'Hex':>5} {'Collision':>15} {'Status':>10}")
print("-" * 65)

gap_rows = []
death_rows = []
for r in range(len(rows)):
    y_px = r * 16
    val = rows[r][c]
    gt = game_tile(val)
    cn = collision_name(val)
    
    if val == 0:
        status = "EMPTY"
        if r >= 17:  # Only care about rows near the wall
            gap_rows.append(r)
    elif gt in range(1, 8):
        status = "passable"
    elif gt == 0x1F:
        status = "*** DEATH ***"
        death_rows.append(r)
    else:
        status = collision_short(val)
    
    if r >= 15 or val not in [0] + list(range(2, 9)):
        print(f"{r:>4} {y_px:>4}-{y_px+15:>4} {val:>4} {gt:>5} {f'0x{gt:02X}':>5} {cn:>15} {status:>10}")

print(f"\nDeath wall (COL_DEATH_RIGHT) rows: {death_rows}")
print(f"Gap (empty) rows in wall area (rows 17-26): {[r for r in gap_rows if 17 <= r <= 26]}")

# =====================================================================
# SECTION 4: Exact gap dimensions
# =====================================================================
print()
print("=" * 130)
print("SECTION 4: EXACT GAP DIMENSIONS AT COLUMN 472")
print("=" * 130)

# Column 472 structure:
wall_rows_above = []
wall_rows_below = []
gap_rows_472 = []

for r in range(len(rows)):
    val = rows[r][c]
    gt = game_tile(val)
    if gt == 0x1F:  # DEATH_RIGHT
        if not gap_rows_472:
            wall_rows_above.append(r)
        else:
            wall_rows_below.append(r)
    elif r >= 17 and val == 0:
        gap_rows_472.append(r)

print(f"\nWall structure at col 472 (X=7552):")
print(f"  DEATH_RIGHT above gap: rows {wall_rows_above}")
if wall_rows_above:
    print(f"    Y range: {wall_rows_above[0]*16} to {(wall_rows_above[-1]+1)*16 - 1}")
print(f"  GAP (empty):           rows {gap_rows_472}")
if gap_rows_472:
    gap_y_top = gap_rows_472[0] * 16
    gap_y_bot = (gap_rows_472[-1] + 1) * 16
    gap_h = gap_y_bot - gap_y_top
    print(f"    Y range: {gap_y_top} to {gap_y_bot - 1} ({gap_h}px tall)")
    print(f"    {len(gap_rows_472)} tile-rows = {gap_h}px")
print(f"  DEATH_RIGHT below gap: rows {wall_rows_below}")
if wall_rows_below:
    print(f"    Y range: {wall_rows_below[0]*16} to {(wall_rows_below[-1]+1)*16 - 1}")

print(f"\n  Cube dimensions: 16x16 pixels")
print(f"  To pass through the gap:")
print(f"    Cube TOP (Y) must be >= {gap_y_top}")
print(f"    Cube BOTTOM (Y+16) must be <= {gap_y_bot}")
print(f"    So cube Y must be in range [{gap_y_top}, {gap_y_bot - 16}]")

print(f"\n  Cube died at Y=363:")
if gap_y_top <= 363 <= gap_y_bot - 16:
    print(f"    363 is WITHIN the gap range [{gap_y_top}, {gap_y_bot - 16}]")
    print(f"    Cube bottom would be at Y=363+16=379, gap bottom at Y={gap_y_bot}")
    print(f"    Margin: {gap_y_bot - 379}px above gap bottom, {363 - gap_y_top}px below gap top")
else:
    if 363 < gap_y_top:
        print(f"    363 is {gap_y_top - 363}px ABOVE the gap top ({gap_y_top})")
    else:
        print(f"    363 is {363 - (gap_y_bot - 16)}px BELOW the gap bottom ({gap_y_bot - 16})")
        print(f"    Cube bottom at 363+16=379 vs gap bottom at {gap_y_bot}")

# =====================================================================
# SECTION 5: Check column 473 too (adjacent to 472)
# =====================================================================
print()
print("=" * 130)
print("SECTION 5: ADJACENT COLUMN 473 (X=7568-7583) - what the cube enters next")
print("=" * 130)

c = 473
x_px = c * 16
for r in range(len(rows)):
    val = rows[r][c]
    gt = game_tile(val)
    cn = collision_name(val)
    if r >= 15 or (val != 0 and gt not in range(1, 8)):
        y_px = r * 16
        if val == 0:
            status = "empty"
        elif gt in range(1, 8):
            status = "bg"
        elif gt == 0x1F:
            status = "DTH_RIGHT!"
        elif is_dangerous(val):
            status = "DANGER!"
        else:
            status = collision_short(val)
        print(f"  Row {r:>2} (Y={y_px:>3}-{y_px+15:>3}): TMX={val:>3} game=0x{gt:02X} = {cn:>15} [{status}]")

# =====================================================================
# SECTION 6: What's beyond the wall - detailed scan cols 473-495
# =====================================================================
print()
print("=" * 130)
print("SECTION 6: BEYOND THE WALL - Non-empty/non-bg tiles, cols 473-500")
print("  Checking for floors, platforms, death tiles, and obstacles")
print("=" * 130)

for c in range(473, 501):
    x_px = c * 16
    interesting = []
    for r in range(len(rows)):
        val = rows[r][c]
        gt = game_tile(val)
        if val != 0 and gt not in range(1, 8):
            y_px = r * 16
            cn = collision_name(val)
            interesting.append((r, y_px, val, gt, cn))
    
    if interesting:
        print(f"\n  Col {c} (X={x_px}-{x_px+15}):")
        for r, y_px, val, gt, cn in interesting:
            danger_marker = " <<<DEATH" if is_dangerous(val) else ""
            solid_marker = " <<<SOLID/FLOOR" if is_solid(val) else ""
            print(f"    Row {r:>2} (Y={y_px:>3}-{y_px+15:>3}): TMX={val:>3} game=0x{gt:02X} = {cn}{danger_marker}{solid_marker}")

# =====================================================================
# SECTION 7: Visual map of the wall area
# =====================================================================
print()
print("=" * 130)
print("SECTION 7: VISUAL MAP - Cols 468-490, Rows 15-26")
print("  '#' = DEATH_RIGHT, 'X' = other death, '=' = solid, '^' = TOP")
print("  '.' = empty, '~' = background, 'o' = other collision")
print("=" * 130)

print(f"     ", end="")
for c in range(468, 491):
    print(f"{c%100:>3}", end="")
print()
print(f"     ", end="")
for c in range(468, 491):
    x = c * 16
    print(f"{(x//100)%100:>3}", end="")
print("  (X hundreds)")

for r in range(15, 27):
    y_px = r * 16
    print(f"r{r:>2} {y_px:>3}|", end="")
    for c in range(468, 491):
        val = rows[r][c]
        gt = game_tile(val)
        if val == 0:
            ch = " . "
        elif gt in range(1, 8):
            ch = " ~ "
        elif gt == 0x1F:
            ch = " # "
        elif gt in [0x0C, 0x18, 0x1B]:
            ch = " X "
        elif gt == 0x10:
            ch = " = "
        elif gt == 0x19:
            ch = " ^ "
        else:
            ch = f" {gt:02X}"
        print(ch, end="")
    print()

# =====================================================================
# SECTION 8: Pixel-precise death analysis
# =====================================================================
print()
print("=" * 130)
print("SECTION 8: PIXEL-PRECISE DEATH ANALYSIS")
print("=" * 130)
print(f"""
The cube died at:
  Cube Y position = 363 (top of cube)
  Cube bottom = 363 + 16 = 379
  Hit reported at tile position (7567, 376)
  
Tile coords:
  X=7567 ÷ 16 = 472.9375 → Column 472 (right side, pixel 15 of tile)  
  Y=376 ÷ 16 = 23.5 → Row 23 (middle of tile)

Column 472 tile map:
  Row 17 (Y=272-287): TMX 32, game 0x1F = COL_DEATH_RIGHT  ← WALL TOP
  Row 18 (Y=288-303): TMX 32, game 0x1F = COL_DEATH_RIGHT
  Row 19 (Y=304-319): TMX 32, game 0x1F = COL_DEATH_RIGHT  ← WALL TOP END
  Row 20 (Y=320-335): EMPTY ← GAP START
  Row 21 (Y=336-351): EMPTY
  Row 22 (Y=352-367): EMPTY
  Row 23 (Y=368-383): EMPTY ← GAP END (cube was here!)
  Row 24 (Y=384-399): TMX 32, game 0x1F = COL_DEATH_RIGHT  ← WALL BOTTOM START
  Row 25 (Y=400-415): TMX 32, game 0x1F = COL_DEATH_RIGHT
  Row 26 (Y=416-431): TMX 32, game 0x1F = COL_DEATH_RIGHT  ← WALL BOTTOM END

Gap analysis:
  Gap rows: 20-23 (Y=320 to Y=383, 64px tall)
  Cube height: 16px
  Valid Y range for cube top: 320 to 368 (gap_bottom - cube_height = 384 - 16)
  
  Cube top = 363
  Cube bottom = 363 + 16 = 379
  Gap bottom = 384 (row 24 starts at Y=384)
  
  363 >= 320 ✓ (cube top is below gap top)
  379 <= 384 ✓ (cube bottom is above gap bottom)
  
  >>> The cube Y=363 FITS in the gap with only 5px margin at the bottom.
  >>> Row 23 (Y=368-383) at col 472 IS empty - it's part of the gap.
  
  The death at (7567, 376) means:
  - X=7567 is at the RIGHT edge of col 472 tile (pixel 15 of 16)
  - Y=376 is in the MIDDLE of row 23 which IS empty in the TMX!
  
  CONCLUSION: The TMX data shows the gap exists and the cube position
  should fit through. The death might be caused by:
  1. The pathfinder hitting the RIGHT SIDE of col 472's death tiles (rows 17-19 or 24-26)
     while not perfectly aligned vertically
  2. Sub-pixel collision at the edge 
  3. The cube's collision box being slightly larger than 16x16
  4. Column 473 having tiles that kill (TMX 49 at row 24 = game tile 48)
""")

# Check what game tile 48 is at col 473 row 24
val_473_24 = rows[24][473]
gt_473_24 = game_tile(val_473_24)
print(f"Col 473, Row 24: TMX={val_473_24}, game=0x{gt_473_24:02X} ({gt_473_24})")
print(f"  This is right at the bottom of the gap exit!")
print(f"  If 0x30 is a death or solid tile, the cube crashes when exiting the gap.")

# Check the first solid floor after the wall
print(f"\nFirst floor/landing after the wall:")
for c in range(473, 510):
    for r in range(20, 27):
        val = rows[r][c]
        gt = game_tile(val)
        if gt == 0x10 or gt == 0x19:  # COL_ALL or COL_TOP
            y_px = r * 16
            x_px = c * 16
            print(f"  Col {c} (X={x_px}), Row {r} (Y={y_px}): game=0x{gt:02X} = {collision_name(val)}")
