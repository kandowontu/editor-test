import re

with open(r'C:\Editor Test\famidash\levels\LEVEL DATA\lvlset_HUGE\baseafterbase.tmx', 'r') as f:
    content = f.read()

matches = list(re.finditer(r'<data\s+encoding=["\']csv["\']>\s*(.*?)\s*</data>', content, re.DOTALL))

def parse_csv(csv_text):
    rows = []
    for line in csv_text.strip().split('\n'):
        line = line.strip()
        if line.endswith(','):
            line = line[:-1]
        if line:
            vals = [int(x.strip()) for x in line.split(',')]
            rows.append(vals)
    return rows

layer0 = parse_csv(matches[0].group(1))

# Read collision table
with open(r'C:\Editor Test\metatile_collision_table.txt', 'r') as f:
    col_table = [line.strip() for line in f if line.strip()]

def get_collision(csv_val):
    if csv_val == 0:
        return "EMPTY"
    idx = csv_val - 1  # firstgid=1
    if idx < len(col_table):
        return col_table[idx]
    return "UNKNOWN"

# Show full collision map for cols 425-480, rows 0-26
print("COLLISION MAP: baseafterbase.tmx cols 425-480")
print("Each cell shows: CSV_id -> collision type")
print()

# Key area: show rows 14-26, cols 440-478 with collision types
print("=" * 100)
print("DETAILED COLLISION VIEW: Rows 13-26, Cols 440-478")
print("=" * 100)

for c in range(440, 479):
    entries = []
    for r in range(0, 27):
        csv_val = layer0[r][c]
        col_type = get_collision(csv_val)
        if csv_val != 0:
            short_col = col_type.replace("COL_", "")
            entries.append(f"r{r:02d}=csv{csv_val:3d}(0x{csv_val-1:02X})={short_col}")
    if entries:
        print(f"Col {c} X={c*16:5d}-{c*16+15}: {'; '.join(entries)}")

# Compact collision grid view
print()
print("=" * 100)
print("COMPACT COLLISION GRID: rows 14-26, cols 425-478")
print("Legend: S=SOLID(ALL)  F=FLOOR_CEIL  T=COL_TOP  D=DEATH  Dt=DEATH_TOP  Db=DEATH_BOT  Dr=DEATH_RIGHT  Dl=DEATH_LEFT  N=NONE  .=EMPTY")
print("=" * 100)

def col_char(csv_val):
    if csv_val == 0: return '. '
    col = get_collision(csv_val)
    if col == "COL_ALL": return 'S '
    if col == "COL_FLOOR_CEIL": return 'F '
    if col == "COL_TOP": return 'T '
    if col == "COL_BOTTOM": return 'Bt'
    if col == "COL_DEATH": return 'D!'
    if col == "COL_DEATH_TOP": return 'Dt'
    if col == "COL_DEATH_BOTTOM": return 'Db'
    if col == "COL_DEATH_RIGHT": return 'Dr'
    if col == "COL_DEATH_LEFT": return 'Dl'
    if col == "COL_NONE": return 'N '
    if col == "COL_TOP_SPIKES": return 'Ts'
    return '? '

col_start = 425
col_end = 478

# Column ruler
print("       ", end="")
for c in range(col_start, col_end+1):
    if c % 5 == 0:
        print(f"{c:<4d}", end="")
    else:
        print("    ", end="")
print()

for r in range(14, 27):
    line = f"r{r:02d} Y{r*16:03d}: "
    for c in range(col_start, col_end+1):
        csv_val = layer0[r][c]
        line += col_char(csv_val)
    print(line)

# Show where the background tiles start per row (the "wall")
print()
print("=" * 100)
print("WHERE BACKGROUND (FLOOR_CEIL) TILES START per row (cols 440-480)")
print("=" * 100)
for r in range(0, 27):
    first_bg = None
    for c in range(440, 480):
        csv_val = layer0[r][c]
        if csv_val in (2, 3, 6, 7):  # background tiles
            first_bg = c
            break
    if first_bg:
        print(f"  Row {r:2d}: background starts at col {first_bg} (X={first_bg*16})")

# Final analysis
print()
print("=" * 100)
print("CRITICAL PATH ANALYSIS")
print("=" * 100)
print("""
The cube travels left-to-right. Key constraints based on collision types:

UPPER SECTION (rows 0-13+):
  - Rows 0-13 at cols 454+: filled with background tiles (COL_FLOOR_CEIL = solid)
  - This forms a solid ceiling/wall starting at col 454

FLOOR LEVEL (row 16):
  - Cols 441-453: CSV 17 = tile 0x10 = COL_ALL (full solid)
  - Cols 454+: CSV 6/2 = tile 0x05/0x01 = COL_FLOOR_CEIL (solid floor/ceiling)
  - The floor at row 16 is CONTINUOUS through the spike field and beyond
  - BUT: rows 14-15 at cols 454+ are ALSO COL_FLOOR_CEIL, forming a wall ABOVE row 16

SPIKE FIELD (cols 441-453, row 17):
  - CSV 28 = tile 0x1B = COL_DEATH (death from any direction)
  - These death tiles are immediately below the solid floor at row 16

LOWER PLATFORMS:
  - Row 20, cols 434-435: CSV 26 = COL_TOP (one-way platform)
  - Row 21, cols 440-442, 445-447, 450-452: CSV 26 = COL_TOP (stepping platforms)
  - Row 20, cols 442, 447, 452: CSV 54 = tile 0x35 = COL_NONE (portals, no collision)
  - Row 23, cols 454-458: CSV 26 = COL_TOP (landing platform in open area)

OPEN DROP (cols 454-471, rows 17-25):
  - Mostly empty (COL_NONE/empty tiles)
  - Only platform: row 23, cols 454-458 (COL_TOP)

DEATH WALL (col 472):
  - Rows 17-19: CSV 32 = tile 0x1F = COL_DEATH_RIGHT
  - Rows 20-23: EMPTY (64px gap, Y=320-383)
  - Rows 24-26: CSV 32 = tile 0x1F = COL_DEATH_RIGHT

GROUND (row 26):
  - CSV 13 = tile 0x0C = COL_DEATH_BOTTOM (floor spike - kills from above)
  - CSV 25 = tile 0x18 = COL_DEATH_BOTTOM (floor spike - kills from above)
  - The ground is DEADLY across the entire region!

PAST THE DEATH WALL (col 473+, row 17):
  - Structure tiles: CSV 46 = COL_ALL, CSV 14/24 = COL_DEATH_TOP
  - These form the new floor/ceiling with ceiling spikes

LIKELY REQUIRED PATH:
  1. Descend staircase on solid platforms (row 14->15->16)
  2. Run right on solid floor at row 16 across spike field (cols 441-453)
  3. Before col 454 (where ceiling blocks the way), must DROP DOWN
  4. Use one-way platforms in rows 20-21 (cols 440-452) to navigate lower
  5. Land on platform at row 23, cols 454-458
  6. From row 23 platform, approach death wall gap at rows 20-23, col 472
  7. Must be at Y=320-383 to pass through the 64px gap
  8. Continue past col 472 into the next section

THE PROBLEM: The gap at rows 20-23 (Y=320-383) requires the cube to be 
at the right height. The platform at row 23 (Y=368-383) is at the BOTTOM 
of the gap. A 16px cube standing on row 23 would have its center at ~Y=360,
which is within the gap. But it needs horizontal momentum to reach col 472
from the platform at cols 454-458 (a gap of 14 tiles = 224 pixels).
""")
