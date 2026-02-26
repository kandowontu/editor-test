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
print()

# Collision type mapping
def collision_name(tile_id):
    if tile_id == 0:
        return "EMPTY"
    elif tile_id in range(1, 8):  # 1-7
        return f"BG({tile_id})"
    elif tile_id == 0x0C:  # 12
        return "DEATH_BOT"
    elif tile_id == 0x10:  # 16
        return "COL_ALL"
    elif tile_id == 0x11:  # 17
        return "COL_ALL(17)"  
    elif tile_id == 0x12:  # 18
        return "COL_18"
    elif tile_id == 0x18:  # 24
        return "DEATH_BOT"
    elif tile_id == 0x19:  # 25
        return "COL_TOP"
    elif tile_id == 0x1A:  # 26
        return "COL_26"
    elif tile_id == 0x1B:  # 27
        return "COL_DEATH"
    elif tile_id == 0x1C:  # 28
        return "COL_28"
    elif tile_id == 0x1F:  # 31
        return "DEATH_R"
    elif tile_id == 0x20:  # 32
        return "COL_32"
    elif tile_id == 0x24:  # 36
        return "COL_36"
    elif tile_id == 0x25:  # 37
        return "COL_37"
    elif tile_id == 0x26:  # 38
        return "COL_38"
    elif tile_id == 0x27:  # 39
        return "COL_39"
    elif tile_id == 0x28:  # 40
        return "COL_40"
    elif tile_id == 0x29:  # 41
        return "COL_41"
    elif tile_id == 0x2C:  # 44
        return "COL_44"
    elif tile_id == 0x2E:  # 46
        return "COL_46"
    elif tile_id == 0x30:  # 48
        return "COL_48"
    elif tile_id == 0x33:  # 51
        return "COL_51"
    elif tile_id == 0x23:  # 35
        return "COL_35"
    else:
        return f"T{tile_id}"

# Also provide a more gameplay-focused collision mapping
def collision_type(tile_id):
    """Returns the actual collision behavior"""
    if tile_id == 0:
        return "---"
    elif tile_id in range(1, 8):
        return "bg"
    elif tile_id == 0x0C:  # 12
        return "DthB"
    elif tile_id == 0x10:  # 16
        return "SOLID"
    elif tile_id == 0x11:  # 17
        return "SOLID"
    elif tile_id == 0x12:  # 18
        return "HALFUP"  # half block or similar
    elif tile_id == 0x18:  # 24
        return "DthB"
    elif tile_id == 0x19:  # 25
        return "TOP"
    elif tile_id == 0x1A:  # 26
        return "SPIKE"
    elif tile_id == 0x1B:  # 27
        return "DEATH"
    elif tile_id == 0x1C:  # 28
        return "DTH?"
    elif tile_id == 0x1F:  # 31
        return "DTH_R"
    elif tile_id == 0x20:  # 32
        return "T32"
    elif tile_id == 0x24:  # 36
        return "T36"
    elif tile_id == 0x25:  # 37
        return "T37"
    elif tile_id == 0x26:  # 38
        return "T38"
    elif tile_id == 0x27:  # 39
        return "T39"
    elif tile_id == 0x28:  # 40
        return "T40"
    elif tile_id == 0x29:  # 41
        return "T41"
    elif tile_id == 0x2C:  # 44
        return "T44"
    elif tile_id == 0x2E:  # 46
        return "T46"
    elif tile_id == 0x30:  # 48
        return "T48"
    elif tile_id == 0x33:  # 51
        return "T51"
    elif tile_id == 0x23:  # 35
        return "T35"
    elif tile_id == 4:
        return "bg4"
    else:
        return f"?{tile_id}"

# =====================================================================
# SECTION 1: Raw tile IDs for cols 465-485
# =====================================================================
print("=" * 120)
print("SECTION 1: RAW TILE IDs - Columns 465-485 (0-indexed), ALL 27 rows")
print("  Column pixel X = col * 16")
print("  Row pixel Y = row * 16")
print("=" * 120)

col_start = 465
col_end = 485

# Header
header = f"{'Row':>4} {'Y_px':>5} |"
for c in range(col_start, col_end + 1):
    header += f" {c:>4}"
print(header)
print("-" * len(header))

for r in range(len(rows)):
    y_px = r * 16
    line = f"{r:>4} {y_px:>5} |"
    for c in range(col_start, col_end + 1):
        val = rows[r][c]
        line += f" {val:>4}"
    print(line)

# =====================================================================
# SECTION 2: Collision types for cols 465-485
# =====================================================================
print()
print("=" * 120)
print("SECTION 2: COLLISION TYPES - Columns 465-485")
print("=" * 120)

header2 = f"{'Row':>4} {'Y_px':>5} |"
for c in range(col_start, col_end + 1):
    header2 += f" {c:>5}"
print(header2)
print("-" * len(header2))

for r in range(len(rows)):
    y_px = r * 16
    line = f"{r:>4} {y_px:>5} |"
    for c in range(col_start, col_end + 1):
        val = rows[r][c]
        ct = collision_type(val)
        line += f" {ct:>5}"
    print(line)

# =====================================================================
# SECTION 3: Focused analysis of cols 470-475 (the death wall area)
# =====================================================================
print()
print("=" * 120)
print("SECTION 3: DETAILED ANALYSIS - Columns 468-476 (death wall zone)")
print("  Looking for COL_DEATH_RIGHT (tile 31/0x1F) and gaps")
print("=" * 120)

focus_start = 468
focus_end = 476

for c in range(focus_start, focus_end + 1):
    x_px = c * 16
    print(f"\n  Column {c} (X={x_px}-{x_px+15}):")
    for r in range(len(rows)):
        y_px = r * 16
        val = rows[r][c]
        if val != 0 and val not in range(1, 8):
            ct = collision_type(val)
            cn = collision_name(val)
            print(f"    Row {r:>2} (Y={y_px:>3}-{y_px+15:>3}): tile={val:>3} (0x{val:02X}) = {cn} [{ct}]")

# =====================================================================
# SECTION 4: What is BEYOND the death wall (cols 473-485)?
# =====================================================================
print()
print("=" * 120)
print("SECTION 4: BEYOND THE DEATH WALL - Columns 473-485")
print("  All non-empty, non-background tiles listed")
print("=" * 120)

for c in range(473, 486):
    x_px = c * 16
    non_empty = []
    for r in range(len(rows)):
        y_px = r * 16
        val = rows[r][c]
        if val != 0 and val not in range(1, 8):
            ct = collision_type(val)
            cn = collision_name(val)
            non_empty.append(f"Row {r:>2} (Y={y_px:>3}-{y_px+15:>3}): tile={val:>3} (0x{val:02X}) = {cn}")
    print(f"\n  Column {c} (X={x_px}-{x_px+15}):")
    if non_empty:
        for item in non_empty:
            print(f"    {item}")
    else:
        print(f"    (all empty/background)")

# =====================================================================
# SECTION 5: Gap analysis at the death wall
# =====================================================================
print()
print("=" * 120)
print("SECTION 5: GAP ANALYSIS")
print("  Cube is 16px tall. Died at Y=363, hit at tile pos (7567,376)")
print("  tile pos (7567,376) => col=7567/16=472.9, row=376/16=23.5")
print("  So the cube hit something at approximately col 472-473, row 23")
print("=" * 120)

# Check columns 471-473 in detail for rows around 20-26
print("\nDetailed tile check for cols 470-474, rows 18-26:")
for c in range(470, 475):
    x_px = c * 16
    print(f"\n  Col {c} (X={x_px}):")
    for r in range(18, 27):
        y_px = r * 16
        val = rows[r][c]
        ct = collision_type(val)
        cn = collision_name(val)
        marker = ""
        if val == 31:
            marker = " <<< COL_DEATH_RIGHT!"
        elif val == 0:
            marker = " (empty - GAP)"
        elif val in range(1, 8):
            marker = " (background only - passable)"
        print(f"    Row {r:>2} (Y={y_px:>3}-{y_px+15:>3}): tile={val:>3} (0x{val:02X}) = {cn:>12} [{ct:>5}]{marker}")

# =====================================================================
# SECTION 6: Find all DEATH_RIGHT tiles in the map around col 470-475
# =====================================================================
print()
print("=" * 120)
print("SECTION 6: ALL DEATH_RIGHT (tile 31) positions near the death wall")
print("=" * 120)

for c in range(468, 477):
    for r in range(len(rows)):
        val = rows[r][c]
        if val == 31:
            x_px = c * 16
            y_px = r * 16
            print(f"  DEATH_RIGHT at col={c}, row={r} (pixel X={x_px}, Y={y_px}-{y_px+15})")

# =====================================================================
# SECTION 7: Find where the floor is after the death wall
# =====================================================================
print()
print("=" * 120)
print("SECTION 7: FLOOR/PLATFORM SEARCH after death wall (cols 473-500)")
print("  Looking for solid tiles (16,17) and top-only tiles (25)")
print("=" * 120)

for c in range(473, min(501, len(rows[0]))):
    x_px = c * 16
    solids = []
    for r in range(len(rows)):
        val = rows[r][c]
        if val in [16, 17, 25, 26, 28, 36, 37, 40, 41, 44, 48]:  # solid/platform tiles
            y_px = r * 16
            cn = collision_name(val)
            solids.append(f"Row {r} (Y={y_px}): {cn}")
    if solids:
        print(f"  Col {c} (X={x_px}): {', '.join(solids)}")

# =====================================================================
# SECTION 8: Wider context - what tiles are 31 (DEATH_RIGHT)?
# =====================================================================
print()
print("=" * 120)
print("SECTION 8: The exact gap dimensions")
print("=" * 120)

# Find the column(s) that have tile 31
for c in range(468, 477):
    death_right_rows = []
    empty_rows = []
    for r in range(len(rows)):
        val = rows[r][c]
        if val == 31:
            death_right_rows.append(r)
        elif val == 0 or val in range(1, 8):
            empty_rows.append(r)
    
    if death_right_rows:
        print(f"\n  Col {c}: DEATH_RIGHT at rows {death_right_rows}")
        # Find gaps (consecutive empty rows between death_right rows)
        min_dr = min(death_right_rows)
        max_dr = max(death_right_rows)
        gap_rows = [r for r in range(min_dr, max_dr + 1) if r not in death_right_rows]
        if gap_rows:
            gap_y_start = min(gap_rows) * 16
            gap_y_end = (max(gap_rows) + 1) * 16
            gap_height = gap_y_end - gap_y_start
            print(f"    GAP in death wall: rows {min(gap_rows)}-{max(gap_rows)}")
            print(f"    Gap Y range: {gap_y_start} to {gap_y_end} (height={gap_height}px)")
            print(f"    Gap tiles: {len(gap_rows)} tiles = {len(gap_rows)*16}px")
            for gr in gap_rows:
                val = rows[gr][c]
                print(f"      Row {gr} (Y={gr*16}-{gr*16+15}): tile={val} ({collision_name(val)})")
            
            print(f"\n    Cube is 16px tall. To pass through gap, cube top must be >= {gap_y_start}")
            print(f"    and cube bottom must be <= {gap_y_end}")
            print(f"    So cube Y (top) must be in range [{gap_y_start}, {gap_y_end - 16}]")
            print(f"    Cube died at Y=363. Gap Y range for cube top = [{gap_y_start}, {gap_y_end - 16}]")
            if gap_y_start <= 363 <= gap_y_end - 16:
                print(f"    >>> Y=363 IS within the gap range!")
            else:
                print(f"    >>> Y=363 is NOT within the gap range!")
                if 363 < gap_y_start:
                    print(f"    >>> Cube was {gap_y_start - 363}px ABOVE the gap")
                else:
                    print(f"    >>> Cube was {363 - (gap_y_end - 16)}px BELOW the gap")
