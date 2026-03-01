import xml.etree.ElementTree as ET
import csv
import os
import glob

TMX_PATH = r'c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\polargeist.tmx'
TRACE_PATH = r'C:\Users\p_j_9\AppData\Local\Temp\famidash_pf_trace.csv'

tree = ET.parse(TMX_PATH)
root = tree.getroot()
map_w = int(root.get('width'))
map_h = int(root.get('height'))
tw = int(root.get('tilewidth'))
th = int(root.get('tileheight'))

# Parse tilesets
print("=== TILESETS ===")
for ts in root.findall('tileset'):
    firstgid = ts.get('firstgid')
    source = ts.get('source', 'inline')
    name = ts.get('name', 'unnamed')
    print(f"  firstgid={firstgid} source={source} name={name}")

# Parse layers
layers = {}
for layer in root.findall('layer'):
    name = layer.get('name') or 'tiles'
    data = layer.find('data')
    raw = data.text.strip()
    all_vals = [int(x.strip()) for x in raw.split(',') if x.strip()]
    rows = []
    for r in range(map_h):
        row = all_vals[r * map_w : (r+1) * map_w]
        rows.append(row)
    layers[name] = rows
    print(f"Layer '{name}': {len(all_vals)} values, {map_h} rows x {map_w} cols")

tile_layer_name = [k for k in layers if k != 'SP'][0]
tile = layers[tile_layer_name]
sp = layers['SP']

# ============================================================
# COIN 2 ANALYSIS
# ============================================================
print("\n" + "="*80)
print("COIN 2 ANALYSIS - hitbox=(9472,351)-(9488,367)")
print("="*80)
coin2_col = 9472 // tw  # 592
coin2_row = 351 // th   # 21

# Show tile layer
print(f"\n--- Tile Layer cols 555-610, rows 18-26 ---")
print("     " + "".join(f"{c:4d}" for c in range(555, 611)))
for r in range(18, min(27, map_h)):
    line = f"R{r:2d}: " + "".join(f"{tile[r][c]:4d}" for c in range(555, 611))
    print(line)

# Show SP layer
print(f"\n--- SP Layer cols 555-610, rows 18-26 ---")
print("     " + "".join(f"{c:4d}" for c in range(555, 611)))
for r in range(18, min(27, map_h)):
    line = f"R{r:2d}: " + "".join(f"{sp[r][c]:4d}" for c in range(555, 611))
    print(line)

# ============================================================
# Find ALL coin sprites in SP layer  
# ============================================================
print("\n" + "="*80)
print("ALL UNIQUE SP VALUES IN ENTIRE MAP")
print("="*80)
sp_vals = {}
for r in range(map_h):
    for c in range(map_w):
        v = sp[r][c]
        if v != 0:
            if v not in sp_vals:
                sp_vals[v] = []
            sp_vals[v].append((r, c))

for v in sorted(sp_vals.keys()):
    count = len(sp_vals[v])
    locs = sp_vals[v][:5]
    loc_str = ", ".join(f"({r},{c})px({c*tw},{r*th})" for r,c in locs)
    if count > 5:
        loc_str += f" ...+{count-5} more"
    print(f"  SP={v:4d} (0x{v:04X}): count={count:3d}  locs={loc_str}")

# ============================================================
# Find coin sprites specifically
# ============================================================
print("\n" + "="*80)
print("SEARCHING FOR COIN SPRITES")
print("="*80)

# Coin 1 is at idx=17243, sid=0x07 => hitbox=(6608,239)-(6624,255)
# col = 6608/16 = 413, row = 239/16 = 14
c1_r, c1_c = 14, 413
print(f"Coin 1 expected location: row={c1_r}, col={c1_c}, SP value = {sp[c1_r][c1_c]}")
# Check surroundings
for dr in range(-2, 3):
    for dc in range(-2, 3):
        rr, cc = c1_r+dr, c1_c+dc
        if 0 <= rr < map_h and 0 <= cc < map_w and sp[rr][cc] != 0:
            print(f"  SP[{rr}][{cc}] = {sp[rr][cc]} (px {cc*tw},{rr*th})")

# Coin 2 is at hitbox=(9472,351)-(9488,367)
# col = 592, row = 21-22
print(f"\nCoin 2 expected location: row=21-22, col=592")
for dr in range(-3, 4):
    for dc in range(-3, 4):
        rr, cc = 22+dr, 592+dc
        if 0 <= rr < map_h and 0 <= cc < map_w and sp[rr][cc] != 0:
            print(f"  SP[{rr}][{cc}] = {sp[rr][cc]} (px {cc*tw},{rr*th})")

# ============================================================
# SPRITES IN COIN 2 REGION (cols 540-620)
# ============================================================
print("\n" + "="*80)
print("ALL SPRITES cols 540-620 (sorted by col)")
print("="*80)
sprites_c2 = []
for r in range(map_h):
    for c in range(540, min(621, map_w)):
        v = sp[r][c]
        if v != 0:
            sprites_c2.append((c, r, v))
for c, r, v in sorted(sprites_c2):
    print(f"  col={c:3d} row={r:2d} px=({c*tw:5d},{r*th:3d}) SP={v}")

# ============================================================
# COIN 3 - Search entire SP for likely coin values
# ============================================================
print("\n" + "="*80)
print("COIN 3 ANALYSIS")
print("="*80)
# We know coin 1 SP value from above. Let's see what value coin 1 has, 
# then search for other cells with same value (coins)
c1_val = sp[c1_r][c1_c]
print(f"Coin 1 SP value: {c1_val}")

# Check nearby too
for dr in range(-1, 2):
    for dc in range(-1, 2):
        rr, cc = c1_r+dr, c1_c+dc
        if 0 <= rr < map_h and 0 <= cc < map_w and sp[rr][cc] != 0:
            print(f"  Coin 1 nearby: SP[{rr}][{cc}] = {sp[rr][cc]}")

# ============================================================
# GROUND LEVEL GEOMETRY cols 555-600 (tile layer)
# ============================================================
print("\n" + "="*80)
print("TILE LAYER cols 555-600 - Full view rows 0-26")
print("="*80)
print("     " + "".join(f"{c:4d}" for c in range(555, 601)))
for r in range(map_h):
    vals = [tile[r][c] for c in range(555, 601)]
    if any(v != 0 for v in vals):
        line = f"R{r:2d}: " + "".join(f"{v:4d}" for v in vals)
        print(line)

# ============================================================
# STAIRCASE AREA - WHERE DOES ELEVATION CHANGE? cols 500-560
# ============================================================
print("\n" + "="*80)
print("TILE LAYER cols 500-560 (staircase area)")
print("="*80)
print("     " + "".join(f"{c:4d}" for c in range(500, 561)))
for r in range(map_h):
    vals = [tile[r][c] for c in range(500, 561)]
    if any(v != 0 for v in vals):
        line = f"R{r:2d}: " + "".join(f"{v:4d}" for v in vals)
        print(line)

# ============================================================
# SP LAYER cols 500-560
# ============================================================
print("\n" + "="*80)
print("SP LAYER cols 500-560 (staircase area)")
print("="*80)
print("     " + "".join(f"{c:4d}" for c in range(500, 561)))
for r in range(map_h):
    vals = [sp[r][c] for c in range(500, 561)]
    if any(v != 0 for v in vals):
        line = f"R{r:2d}: " + "".join(f"{v:4d}" for v in vals)
        print(line)
