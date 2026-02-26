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
layer1 = parse_csv(matches[1].group(1))

# Part 1: Zoomed view cols 425-480, rows 14-26 (the gameplay area)
print("=" * 120)
print("LEVEL LAYOUT: baseafterbase.tmx, Cols 425-480, Rows 14-26")
print("Map: 869x27 tiles, 16x16px each. Total map = 13904x432 pixels")
print("Death zone: X=6836-7555 (cols 427-472)")
print()
print("CSV IDs -> Tileset IDs (firstgid=1, so tile_index = csv_val - 1):")
print("  CSV 17 = tile 0x10 = FLOOR SPIKE (upward)")
print("  CSV 18 = tile 0x11 = CEILING SPIKE (downward)")
print("  CSV 28 = tile 0x1B = SOLID BLOCK")
print("  CSV 32 = tile 0x1F = DEATH (col=COL_DEATH_RIGHT)")
print("  CSV 26 = tile 0x19 = BLOCK/PLATFORM")
print("  CSV 54 = tile 0x35 = PORTAL/SPECIAL")
print("  CSV 46 = tile 0x2D, CSV 49 = tile 0x30, CSV 51 = tile 0x32 = structure")
print("=" * 120)

# Build a compact character map
def tile_char(t):
    if t == 0: return '.'
    if t == 17: return '^'  # floor spike
    if t == 18: return 'v'  # ceiling spike
    if t == 28: return '#'  # solid
    if t == 32: return 'X'  # death 0x1F
    if t == 26: return 'B'  # block
    if t == 29: return '>'  # right spike
    if t == 54: return 'P'  # portal
    if t in (37, 48, 35, 36, 40, 41, 42, 43, 44): return '='  # platform
    if t == 46: return '~'  # structure
    if t == 49: return '*'  # structure
    if t == 51: return '+'  # structure
    if t in (7, 3): return ' '  # bg upper
    if t in (6, 2): return ' '  # bg lower
    if t in (13, 14, 24, 25): return '_'  # ground fill
    if t == 4: return 'S'  # maybe solid?
    if t == 23: return '-'  # ground edge
    if t == 38: return '('  
    if t == 39: return ')'  
    if t == 47: return '|'
    if t == 53: return '!'
    if t in (233, 234): return ':'
    if t in (239, 240): return '?'
    if t in (245, 246, 247): return '$'
    return 'o'  # other

# Print compact grid
col_start = 425
col_end = 480

# Column ruler (tens)
print("     ", end="")
for c in range(col_start, col_end+1):
    if c % 10 == 0:
        print(f"{c//10%10}", end="")
    else:
        print(" ", end="")
print()

# Column ruler (ones)
print("     ", end="")
for c in range(col_start, col_end+1):
    if c % 5 == 0:
        print(f"{c%10}", end="")
    else:
        print(" ", end="")
print()

# Grid
for r in range(len(layer0)):
    line = f"r{r:02d}: "
    for c in range(col_start, min(col_end+1, len(layer0[r]))):
        t = layer0[r][c]
        line += tile_char(t)
    # Pixel Y range
    line += f"  Y={r*16}-{r*16+15}"
    print(line)

print()
print("     ", end="")
for c in range(col_start, col_end+1):
    px = c * 16
    if c % 10 == 0:
        s = str(px)
        print(s, end=" " * max(0, 1-len(s)+3))
    elif c % 5 == 0:
        pass
    else:
        print(" ", end="")
print()

# Part 2: SP layer analysis 
print()
print("=" * 80)
print("SP LAYER (Layer 1) - Start positions/triggers in cols 425-480:")
print("=" * 80)
for r in range(len(layer1)):
    for c in range(col_start, min(col_end+1, len(layer1[r]))):
        t = layer1[r][c]
        if t != 0:
            px = c * 16
            py = r * 16
            print(f"  Row {r} Col {c} (X={px},Y={py}): CSV_id={t} (tileset_id={t-1}, 0x{t-1:02X})")

# Part 3: Structural analysis
print()
print("=" * 80)
print("STRUCTURAL ANALYSIS OF DEATH ZONE (cols 427-472)")
print("=" * 80)

print()
print("--- Section 1: Staircase (cols 427-440) ---")
print("Descending platforms with spikes on leading edges:")
for c in range(427, 441):
    tiles = [(r, layer0[r][c]) for r in range(27) if layer0[r][c] != 0 and layer0[r][c] not in (7,3,6,2,13,14,24,25)]
    if tiles:
        desc = ", ".join(f"r{r}={t}({tile_char(t)})" for r, t in tiles)
        print(f"  Col {c} (X={c*16}): {desc}")

print()
print("--- Section 2: Spike Field (cols 441-453) ---")
print("Floor spikes and solid blocks:")
for c in range(441, 454):
    tiles = [(r, layer0[r][c]) for r in range(27) if layer0[r][c] != 0 and layer0[r][c] not in (7,3,6,2,13,14,24,25)]
    if tiles:
        desc = ", ".join(f"r{r}={t}({tile_char(t)})" for r, t in tiles)
        print(f"  Col {c} (X={c*16}): {desc}")

print()
print("--- Section 3: Open Gap (cols 454-471) ---")
print("Non-background tiles only:")
for c in range(454, 472):
    tiles = [(r, layer0[r][c]) for r in range(27) if layer0[r][c] != 0 and layer0[r][c] not in (7,3,6,2,13,14,24,25)]
    if tiles:
        desc = ", ".join(f"r{r}={t}({tile_char(t)})" for r, t in tiles)
        print(f"  Col {c} (X={c*16}): {desc}")

print()
print("--- Section 4: Death Wall at Col 472 (X=7552-7567) ---")
for r in range(27):
    t = layer0[r][472]
    if t != 0 and t not in (7,3,6,2):
        print(f"  Row {r} (Y={r*16}-{r*16+15}): CSV {t} = tile 0x{t-1:02X} ({tile_char(t)})")

print()
print("--- DEATH WALL GAP ANALYSIS ---")
death_rows = [r for r in range(27) if layer0[r][472] == 32]
non_death_rows = [r for r in range(min(death_rows), max(death_rows)+1) if r not in death_rows]
print(f"Death (0x1F) at rows: {death_rows}")
print(f"Gap in death wall at rows: {non_death_rows}")
if non_death_rows:
    gap_top = min(non_death_rows) * 16
    gap_bot = (max(non_death_rows) + 1) * 16 - 1
    print(f"Gap Y range: {gap_top}-{gap_bot} ({(max(non_death_rows)-min(non_death_rows)+1)*16}px tall)")

# Part 4: Summary
print()
print("=" * 80)
print("SUMMARY")
print("=" * 80)
print("""
The death zone X=6836-7555 contains these obstacles:

1. STAIRCASE DESCENT (X=6832-7055, cols 427-440):
   - Descending platforms (blocks at rows 14->21) with spikes at leading edges
   - Spike at row 14/col 428, then row 15/col 431, then row 16/col 434
   - Blocks form stepping platforms going down and right

2. FLOOR SPIKE FIELD (X=7056-7263, cols 441-453):
   - 13 consecutive floor spike tiles (CSV 17, tile 0x10) at ROW 16 (Y=256-271)
   - 13 solid blocks (CSV 28, tile 0x1B) at ROW 17 (Y=272-287) underneath
   - Portals (CSV 54) at cols 442, 447, 452 below the spikes (row 20)
   - Block platforms at various positions in rows 20-21

3. OPEN DROP (X=7264-7551, cols 454-471):
   - Only background tiles in rows 14-16
   - Mostly empty from row 17 down to row 25
   - Small block platform at row 23, cols 454-458 (Y=368-383)
   - Ground fill at row 26

4. DEATH WALL (X=7552-7567, col 472):
   - Vertical column of DEATH tiles (CSV 32 = tile 0x1F, COL_DEATH_RIGHT)
   - Death at rows 17-19 (Y=272-319) - upper portion
   - GAP at rows 20-23 (Y=320-383) - 64px tall opening
   - Death at rows 24-26 (Y=384-431) - lower portion
   - The gap is 4 tiles (64px) tall
""")
