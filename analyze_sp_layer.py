import csv
import io

tmx_path = r"C:\Editor Test\famidash\levels\LEVEL DATA\lvlset_HUGE\baseafterbase.tmx"

with open(tmx_path, 'r') as f:
    content = f.read()

# Find both layers
layers = []
import re
for match in re.finditer(r'<layer id="(\d+)" name="([^"]*)"[^>]*>.*?<data encoding="csv">\s*(.*?)\s*</data>', content, re.DOTALL):
    layer_id = match.group(1)
    layer_name = match.group(2)
    csv_data = match.group(3)
    
    reader = csv.reader(io.StringIO(csv_data))
    grid = []
    for row_data in reader:
        row_vals = [int(x.strip()) for x in row_data if x.strip()]
        if row_vals:
            grid.append(row_vals)
    
    layers.append((layer_id, layer_name, grid))
    print(f"Layer {layer_id} ({layer_name}): {len(grid)} rows x {len(grid[0]) if grid else 0} cols")

# Focus on cols 465-490 for death wall area
COL_START = 465
COL_END = 490

# Collision type lookup (game_tile = tmx_tile - 1)
collision_map = {}
try:
    with open(r"C:\Editor Test\metatile_collision_table.txt", 'r') as f:
        for i, line in enumerate(f):
            collision_map[i] = line.strip()
except:
    pass

print("\n" + "="*80)
print("SP LAYER (Layer 2) - Non-zero tiles in cols 465-490, all rows")
print("="*80)

if len(layers) >= 2:
    sp_grid = layers[1][2]
    for row in range(len(sp_grid)):
        for col in range(COL_START, min(COL_END+1, len(sp_grid[row]))):
            tmx_val = sp_grid[row][col]
            if tmx_val != 0:
                game_tile = tmx_val - 1
                px_x = col * 16
                px_y = row * 16
                col_type = collision_map.get(game_tile, "UNKNOWN")
                print(f"  Row {row:2d} Col {col:3d} | TMX={tmx_val:3d} Game=0x{game_tile:02X}({game_tile:3d}) | Pixel=({px_x},{px_y}) | {col_type}")

print("\n" + "="*80)
print("SP LAYER - Full grid for cols 468-485, rows 15-26 (death wall area)")
print("="*80)

if len(layers) >= 2:
    sp_grid = layers[1][2]
    # Header
    print("     ", end="")
    for col in range(468, 486):
        print(f"  {col:3d}", end="")
    print()
    
    for row in range(15, 27):
        print(f"R{row:2d}: ", end="")
        for col in range(468, 486):
            val = sp_grid[row][col]
            if val == 0:
                print("    .", end="")
            else:
                print(f"  {val:3d}", end="")
        print()

print("\n" + "="*80)
print("MAIN LAYER (Layer 1) - Grid for cols 468-485, rows 15-26 (for comparison)")
print("="*80)

if len(layers) >= 1:
    main_grid = layers[0][2]
    # Header
    print("     ", end="")
    for col in range(468, 486):
        print(f"  {col:3d}", end="")
    print()
    
    for row in range(15, 27):
        print(f"R{row:2d}: ", end="")
        for col in range(468, 486):
            val = main_grid[row][col]
            if val == 0:
                print("    .", end="")
            else:
                print(f"  {val:3d}", end="")
        print()

# Also check the wider area for SP layer
print("\n" + "="*80)
print("SP LAYER - ALL non-zero tiles in cols 460-500")
print("="*80)

if len(layers) >= 2:
    sp_grid = layers[1][2]
    for row in range(len(sp_grid)):
        for col in range(460, min(501, len(sp_grid[row]))):
            tmx_val = sp_grid[row][col]
            if tmx_val != 0:
                game_tile = tmx_val - 1
                px_x = col * 16
                px_y = row * 16
                col_type = collision_map.get(game_tile, "UNKNOWN")
                print(f"  Row {row:2d} Col {col:3d} | TMX={tmx_val:3d} Game=0x{game_tile:02X}({game_tile:3d}) | Pixel=({px_x},{px_y}) | {col_type}")
