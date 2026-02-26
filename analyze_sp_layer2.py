import xml.etree.ElementTree as ET

tmx_path = r"C:\Editor Test\famidash\levels\LEVEL DATA\lvlset_HUGE\baseafterbase.tmx"

tree = ET.parse(tmx_path)
root = tree.getroot()

layers = {}
for layer_elem in root.findall('layer'):
    lid = layer_elem.get('id')
    lname = layer_elem.get('name', f'layer_{lid}')
    data_elem = layer_elem.find('data')
    csv_text = data_elem.text.strip()
    
    grid = []
    for line in csv_text.split('\n'):
        line = line.strip().rstrip(',')
        if line:
            row = [int(x) for x in line.split(',')]
            grid.append(row)
    
    layers[lid] = (lname, grid)
    print(f"Layer {lid} ({lname}): {len(grid)} rows x {len(grid[0])} cols")

# Collision lookup
collision_map = {}
with open(r"C:\Editor Test\metatile_collision_table.txt", 'r') as f:
    for i, line in enumerate(f):
        collision_map[i] = line.strip()

COL_START, COL_END = 465, 490

# ===== SP LAYER NON-ZERO TILES =====
print("\n" + "="*80)
print("SP LAYER - Non-zero tiles in cols 460-500")
print("="*80)
sp_name, sp_grid = layers.get('2', (None, None))
if sp_grid:
    found_any = False
    for row in range(len(sp_grid)):
        for col in range(460, min(501, len(sp_grid[row]))):
            tmx_val = sp_grid[row][col]
            if tmx_val != 0:
                found_any = True
                game_tile = tmx_val - 1  # firstgid for sprites tileset = 257
                # SP layer uses sprites tileset (firstgid=257)
                # so game sprite index = tmx_val - 257
                sprite_idx = tmx_val - 257
                px = (col*16, row*16)
                print(f"  Row {row:2d} Col {col:3d} | TMX={tmx_val:3d} SpriteIdx={sprite_idx:3d} | Pixel=({px[0]},{px[1]})")
    if not found_any:
        print("  (no non-zero tiles found)")

# ===== MAIN LAYER GRID =====
print("\n" + "="*80)
print("MAIN LAYER (Layer 1) - Grid cols 468-485, rows 15-26")
print("="*80)
main_name, main_grid = layers.get('1', (None, None))
if main_grid:
    print("     ", end="")
    for c in range(468, 486):
        print(f" {c:4d}", end="")
    print()
    for r in range(15, 27):
        print(f"R{r:2d}: ", end="")
        for c in range(468, 486):
            v = main_grid[r][c]
            if v == 0:
                print("    .", end="")
            else:
                print(f" {v:4d}", end="")
        print()

# ===== MAIN LAYER COLLISION GRID =====
print("\n" + "="*80)
print("MAIN LAYER (Layer 1) - Collision types cols 468-485, rows 15-26")
print("="*80)

# Short abbreviations
def short_col(game_tile):
    ct = collision_map.get(game_tile, "?")
    abbrev = {
        'COL_NONE': '....', 'COL_ALL': 'WALL', 'COL_TOP': '_TOP',
        'COL_DEATH_RIGHT': 'DR!!', 'COL_DEATH_BOTTOM': 'DB!!',
        'COL_DEATH_TOP': 'DT!!', 'COL_DEATH_LEFT': 'DL!!'
    }
    return abbrev.get(ct, ct[:4])

if main_grid:
    print("     ", end="")
    for c in range(468, 486):
        print(f" {c:4d}", end="")
    print()
    for r in range(15, 27):
        print(f"R{r:2d}: ", end="")
        for c in range(468, 486):
            v = main_grid[r][c]
            if v == 0:
                print("    .", end="")
            else:
                gt = v - 1
                print(f" {short_col(gt)}", end="")
        print()

# ===== SP LAYER GRID =====
print("\n" + "="*80)
print("SP LAYER (Layer 2) - Grid cols 468-485, rows 15-26")
print("="*80)
if sp_grid:
    print("     ", end="")
    for c in range(468, 486):
        print(f" {c:4d}", end="")
    print()
    for r in range(15, 27):
        print(f"R{r:2d}: ", end="")
        for c in range(468, 486):
            v = sp_grid[r][c]
            if v == 0:
                print("    .", end="")
            else:
                print(f" {v:4d}", end="")
        print()

# ===== DEATH WALL DETAILED =====
print("\n" + "="*80)
print("DETAILED: Col 472 both layers, all 27 rows")
print("="*80)
if main_grid and sp_grid:
    print(f"{'Row':>4} | {'Main TMX':>8} {'Game':>6} {'Collision':>16} | {'SP TMX':>8} {'SprIdx':>7}")
    for r in range(27):
        mv = main_grid[r][472]
        sv = sp_grid[r][472]
        
        if mv == 0:
            m_str = f"{'0':>8} {'---':>6} {'(empty)':>16}"
        else:
            gt = mv - 1
            ct = collision_map.get(gt, "?")
            m_str = f"{mv:>8} {f'0x{gt:02X}':>6} {ct:>16}"
        
        if sv == 0:
            s_str = f"{'0':>8} {'---':>7}"
        else:
            si = sv - 257
            s_str = f"{sv:>8} {si:>7}"
        
        print(f"{r:>4} | {m_str} | {s_str}")

# ===== PIXEL Y ANALYSIS =====
print("\n" + "="*80)
print("PIXEL Y ANALYSIS at col 472 (X = 7552-7567)")
print("="*80)
if main_grid:
    print(f"\nDeath wall gap analysis:")
    for r in range(27):
        v = main_grid[r][472]
        if v != 0:
            gt = v - 1
            ct = collision_map.get(gt, "?")
            y_top = r * 16
            y_bot = y_top + 15
            print(f"  Row {r:2d}: Y={y_top:3d}-{y_bot:3d} | TMX {v} = Game 0x{gt:02X} = {ct}")
    
    # Find gap
    gap_rows = []
    for r in range(27):
        if main_grid[r][472] == 0:
            gap_rows.append(r)
    
    # Find longest consecutive gap
    if gap_rows:
        runs = []
        start = gap_rows[0]
        prev = gap_rows[0]
        for g in gap_rows[1:]:
            if g == prev + 1:
                prev = g
            else:
                runs.append((start, prev))
                start = g
                prev = g
        runs.append((start, prev))
        
        print(f"\n  Empty (gap) regions at col 472:")
        for s, e in runs:
            y_top = s * 16
            y_bot = (e + 1) * 16 - 1
            height = (e - s + 1) * 16
            print(f"    Rows {s}-{e}: Y={y_top}-{y_bot} ({height}px tall)")
        
        print(f"\n  Cube at Y=363:")
        print(f"    If Y=363 is top of 16x16 hitbox: occupies Y=363-378")
        print(f"    If Y=363 is bottom of 16x16 hitbox: occupies Y=348-363")
        print(f"    Rows touched (top=363): row {363//16} to row {378//16}")
        print(f"    Rows touched (bot=363): row {348//16} to row {363//16}")
