"""Re-analyze polargeist geometry with CORRECT collision table."""
import xml.etree.ElementTree as ET

tmx_path = r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\polargeist.tmx"
col_table_path = r"c:\Editor Test\metatile_collision_table.txt"

# Load collision table
with open(col_table_path) as f:
    col_names = [line.strip() for line in f if line.strip()]
print(f"Collision table: {len(col_names)} entries")

# Parse TMX
tree = ET.parse(tmx_path)
root = tree.getroot()
map_width = int(root.attrib['width'])
map_height = int(root.attrib['height'])

layer = root.find(".//layer/data")
csv_text = layer.text.strip()
all_tiles = []
for line in csv_text.split('\n'):
    row_tiles = [int(t.strip()) for t in line.strip().rstrip(',').split(',') if t.strip()]
    all_tiles.extend(row_tiles)

grid = []
for r in range(map_height):
    grid.append(all_tiles[r * map_width : (r + 1) * map_width])

def get_collision(tid):
    if tid < len(col_names):
        return col_names[tid]
    return f"UNKNOWN({tid})"

def is_solid(tid):
    c = get_collision(tid)
    return c in ("COL_ALL", "COL_FLOOR_CEIL")

def is_death(tid):
    c = get_collision(tid)
    return "DEATH" in c or "SPIKE" in c

def is_platform(tid):
    c = get_collision(tid)
    return c == "COL_TOP"

def is_passable(tid):
    """Can the player walk through horizontally at this tile?"""
    c = get_collision(tid)
    return c in ("COL_NONE", "COL_TOP", "COL_BOTTOM", "COL_DEATH_TOP", "COL_DEATH_BOTTOM")

def tile_label(tid):
    if tid == 0: return "air"
    c = get_collision(tid)
    if is_solid(tid): return f"SOLID({c})"
    if is_death(tid): return f"DEATH({c})"
    if is_platform(tid): return f"PLAT({c})"
    return f"{c}"

# ============================================================
print("="*80)
print("COMPLETE TILE ANALYSIS: cols 480-600, rows 20-26")
print("Using full metatile_collision_table.txt")
print("="*80)

# Print key tile collision lookups
print("\nKey tile collision types in this region:")
key_tiles = sorted(set(
    grid[r][c] for r in range(20, 27) for c in range(480, min(601, map_width))
    if grid[r][c] != 0
))
for tid in key_tiles:
    print(f"  Tile {tid:3d} = {get_collision(tid)}")

# ============================================================
print("\n" + "="*80)
print("1. ALL NON-ZERO TILES with correct collision types")
print("="*80)
for r in range(20, 27):
    row_tiles_found = []
    for c in range(480, min(601, map_width)):
        tid = grid[r][c]
        if tid != 0:
            row_tiles_found.append((c, tid))
    if row_tiles_found:
        print(f"\n  Row {r} (gameY={(r-3)*16}):")
        for c, tid in row_tiles_found:
            col_type = get_collision(tid)
            marker = ""
            if is_solid(tid): marker = " *** SOLID ***"
            elif is_death(tid): marker = " !!! DEATH !!!"
            elif is_platform(tid): marker = " --- PLATFORM ---"
            elif col_type == "COL_NONE": marker = " (passthrough)"
            print(f"    col {c:3d} (X={c*16:5d}): tile={tid:3d}  {col_type:<25s}{marker}")

# ============================================================
print("\n" + "="*80)
print("2. SOLID BLOCKS forming staircase structure")
print("="*80)
for r in range(15, 27):
    solids = [(c, grid[r][c]) for c in range(480, min(601, map_width)) if is_solid(grid[r][c])]
    if solids:
        print(f"  Row {r} (gameY={(r-3)*16}): {[(c, tid) for c, tid in solids]}")

# ============================================================
print("\n" + "="*80)
print("3. ROW 26 - THE GROUND - DETAILED WALKTHROUGH")
print("="*80)
print("Ground path from col 480 to 600:")
for c in range(480, min(601, map_width)):
    tid = grid[26][c]
    if tid != 0:
        col_type = get_collision(tid)
        status = ""
        if is_solid(tid): status = "SOLID-BLOCK"
        elif is_death(tid): status = "DEATH-KILL"
        elif is_platform(tid): status = "platform-safe"
        elif col_type == "COL_NONE": status = "passthrough"
        else: status = col_type
        print(f"  col {c:3d} (X={c*16:5d}): tile={tid:3d} {col_type:<25s} [{status}]")
    else:
        # Only print gaps as ranges
        pass

# Print gaps
print("\nEmpty columns (air) at row 26:")
gap_start = None
for c in range(480, min(601, map_width)):
    tid = grid[26][c]
    if tid == 0:
        if gap_start is None: gap_start = c
    else:
        if gap_start is not None:
            print(f"  cols {gap_start}-{c-1} (X={gap_start*16}-{(c-1)*16}): {c-gap_start} empty tiles")
            gap_start = None
if gap_start is not None:
    print(f"  cols {gap_start}-600: gap to end")

# ============================================================
print("\n" + "="*80)
print("4. FULL VERTICAL STAIRCASE STRUCTURE (cols 500-550)")
print("="*80)
for c in range(500, 551):
    col_data = []
    for r in range(18, 27):
        tid = grid[r][c]
        if tid != 0:
            col_data.append((r, tid, get_collision(tid)))
    if col_data:
        print(f"  Col {c} (X={c*16}):")
        for r, tid, ct in col_data:
            print(f"    row {r} (Y={(r-3)*16}): tile={tid} {ct}")

# ============================================================
print("\n" + "="*80)
print("5. PATH TO YELLOW PAD AT COL 592")
print("="*80)
print("The yellow pad is at col 592, row 26 (tile 54 = COL_NONE)")
print("The coin is near col 592")
print()

# Find the safe path from col 549 onwards (after the staircase solid wall)
print("After the staircase wall at col 549:")
print("Cols 550-592 at row 26:")
for c in range(549, 601):
    tid = grid[26][c]
    col_type = get_collision(tid) if tid != 0 else "air"
    safe = "SAFE" if (tid == 0 or is_platform(tid)) else ("PASSTHROUGH" if get_collision(tid) == "COL_NONE" else ("DEATH" if is_death(tid) else ("SOLID" if is_solid(tid) else col_type)))
    print(f"  col {c} (X={c*16}): tile={tid:3d} {col_type:<25s} [{safe}]")

# Also check rows 24-25 in the approach area (cols 580-600)
print("\nApproach area rows 24-25 (cols 580-600):")
for r in [24, 25]:
    print(f"  Row {r} (Y={(r-3)*16}):")
    for c in range(580, min(601, map_width)):
        tid = grid[r][c]
        if tid != 0:
            print(f"    col {c} (X={c*16}): tile={tid} {get_collision(tid)}")

# ============================================================
print("\n" + "="*80)
print("6. VISUAL MAP WITH CORRECT COLLISION TYPES")
print("Rows 20-26, cols 545-600")
print("="*80)
# Legend
print("Legend: # = SOLID, X = DEATH, = = PLATFORM, . = air, ~ = passthrough(COL_NONE)")
print()

for chunk_start in [545, 575]:
    chunk_end = min(chunk_start + 30, 601)
    print(f"\n--- Cols {chunk_start}-{chunk_end-1} ---")
    print("    ", end="")
    for c in range(chunk_start, chunk_end):
        print(f"{c:4d}", end="")
    print()
    for r in range(20, 27):
        print(f"r{r:2d} ", end="")
        for c in range(chunk_start, chunk_end):
            if c < map_width:
                tid = grid[r][c]
                if tid == 0:
                    print("   .", end="")
                elif is_solid(tid):
                    print(f"  #{tid}", end="")
                elif is_death(tid):
                    print(f"  X{tid}", end="")
                elif is_platform(tid):
                    print(f"  ={tid}", end="")
                elif get_collision(tid) == "COL_NONE":
                    print(f"  ~{tid}", end="")
                else:
                    print(f" ?{tid}", end="")
            else:
                print("    ", end="")
        print()

# ============================================================
print("\n" + "="*80)
print("7. SUMMARY: PATH VIABILITY ANALYSIS")
print("="*80)

# Check: can you walk at row 26 from col 563 (after death zone) to col 592?
print("\nRow 26 path check from col 563 to col 592:")
path_ok = True
for c in range(563, 593):
    tid = grid[26][c]
    col_type = get_collision(tid) if tid != 0 else "air"
    if is_solid(tid):
        print(f"  BLOCKED at col {c}: solid {tid} ({col_type})")
        path_ok = False
    elif is_death(tid):
        print(f"  DEADLY at col {c}: {tid} ({col_type})")
        path_ok = False
    elif tid == 0:
        # Check if there's ground below (but row 26 IS the last row)
        pass  # air - would fall through

if path_ok:
    print("  PATH CLEAR (but has gaps and death tiles to navigate)")

# Check if safe tiles exist
print("\nSafe landing tiles on row 26 from col 563 to 592:")
for c in range(563, 593):
    tid = grid[26][c]
    if is_platform(tid) or is_solid(tid):
        print(f"  col {c} (X={c*16}): tile={tid} {get_collision(tid)} - LANDABLE")

# The gap analysis
print("\nGap/hazard analysis between safe tiles on row 26 (cols 549-595):")
prev_safe = None
for c in range(549, 596):
    tid = grid[26][c]
    if is_platform(tid) or (is_solid(tid) and not is_death(tid)):
        if prev_safe is not None and c - prev_safe > 1:
            gap_tiles = []
            for gc in range(prev_safe+1, c):
                gt = grid[26][gc]
                gap_tiles.append((gc, gt, get_collision(gt) if gt != 0 else "air"))
            hazards = [f"col{gc}={gt}({ct})" for gc, gt, ct in gap_tiles if is_death(gt)]
            empty = [gc for gc, gt, ct in gap_tiles if gt == 0]
            print(f"  Gap from col {prev_safe} to {c}: width={c-prev_safe-1}")
            if hazards: print(f"    Hazards: {hazards}")
            if empty: print(f"    Empty: cols {empty}")
        prev_safe = c

print("\nDone!")
