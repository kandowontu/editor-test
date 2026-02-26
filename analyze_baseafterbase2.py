import re

with open(r'C:\Editor Test\famidash\levels\LEVEL DATA\lvlset_HUGE\baseafterbase.tmx', 'r') as f:
    content = f.read()

# Find all data sections
matches = list(re.finditer(r'<data\s+encoding=["\']csv["\']>\s*(.*?)\s*</data>', content, re.DOTALL))
print(f"Found {len(matches)} data/layer sections")

# Find layer names
layers = re.findall(r'<layer\s+id="(\d+)"\s+name="([^"]*)"', content)
print(f"Layers: {layers}")

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

all_layers = []
for i, m in enumerate(matches):
    rows = parse_csv(m.group(1))
    all_layers.append(rows)
    print(f"  Layer {i}: {len(rows[0])} x {len(rows)}")

# firstgid = 1, so CSV id 1 = tile index 0 in tileset
# CSV tid -> actual tile index = CSV_val - 1 (for firstgid=1 tileset)
# But 0 = empty in CSV
# So CSV 17 -> actual tile 16 (0x10), CSV 32 -> tile 31 (0x1F), CSV 31 -> tile 30 (0x1E)

print("\n=== TILE ID MAPPING (firstgid=1) ===")
print("CSV value -> Tileset index (hex)")
for csv_val in [17, 28, 31, 32, 26, 18, 29, 46, 49, 51, 54, 37, 48, 35, 4, 6, 2, 7, 3, 13, 14, 24, 25]:
    actual = csv_val - 1
    print(f"  CSV {csv_val:3d} -> tile {actual:3d} (0x{actual:02X})")

# Focusing on the death region: X=6836-7555 = cols 427-472
# Wider context: cols 420-490
col_start = 420
col_end = 490

print(f"\n=== LAYER 0 (main) - Rows 14-26, Cols {col_start}-{col_end} ===")
print(f"(These are the gameplay-relevant bottom rows)")
print(f"CSV tile IDs shown. 0x1F death spike = CSV 32")
print()

# Compact display for rows 14-26
rows = all_layers[0]
# Print column markers
print("        ", end="")
for c in range(col_start, min(col_end+1, len(rows[0]))):
    if c % 5 == 0:
        print(f"{c:>3}", end=" ")
    else:
        print("   ", end=" ")
print()

for r in range(14, min(27, len(rows))):
    line = f"Row {r:2d}: "
    for c in range(col_start, min(col_end+1, len(rows[0]))):
        t = rows[r][c] if c < len(rows[r]) else 0
        if t == 0:
            line += "  . "
        elif t == 17:
            line += " ^^ "  # spike-like
        elif t == 28:
            line += " ## "  # solid
        elif t == 32:
            line += " XX "  # 0x1F death?
        elif t == 26:
            line += " [] "  # block
        elif t == 18:
            line += " vv "  # down spike?
        elif t == 29:
            line += " >> "  # right spike?
        elif t in (37, 48, 35, 36, 40, 41, 42, 43, 44):
            line += " PP "  # platform structure
        elif t == 46:
            line += " ~~ "  # decoration?
        elif t == 49:
            line += " ** "  # something
        elif t == 51:
            line += " ++ "  # something
        elif t == 54:
            line += " %% "  # portal/special
        elif t == 4:
            line += " == "  # another solid?
        elif t in (6, 2):
            line += " bg "  # background
        elif t in (7, 3):
            line += " BG "  # background
        elif t in (13, 14, 24, 25):
            line += " .. "  # ground fill
        elif t == 23:
            line += " -- "  # ground border?
        elif t == 233 or t == 234:
            line += " :: "  # something
        elif t == 239 or t == 240:
            line += " ?? "  # something
        elif t == 245 or t == 246 or t == 247:
            line += " $$ "  # something special
        elif t == 38 or t == 39:
            line += " <> "  # bracket tiles
        elif t == 47:
            line += " || "  # pipe?
        elif t == 53:
            line += " !! "  # something
        else:
            line += f"{t:3d} "
    print(line)

print()
print("Legend: ^^=17(spike?) ##=28(solid) []=26(block) vv=18 >>=29 PP=platform bg=6,2 BG=7,3 ..=ground ~~=46 **=49 ++=51 %%=54(portal) XX=32(0x1F)")

# Check SP layer too
if len(all_layers) > 1:
    sp_rows = all_layers[1]
    print(f"\n=== LAYER 1 (SP) - Rows 14-26, Cols {col_start}-{col_end} ===")
    non_zero_found = False
    for r in range(14, min(27, len(sp_rows))):
        found = []
        for c in range(col_start, min(col_end+1, len(sp_rows[0]))):
            t = sp_rows[r][c] if c < len(sp_rows[r]) else 0
            if t != 0:
                found.append((c, t))
        if found:
            non_zero_found = True
            print(f"  Row {r}: {found}")
    if not non_zero_found:
        print("  (all empty in this region)")

# Now map X pixel ranges to columns
print("\n=== KEY X POSITIONS ===")
print(f"X=6836 = col 427.25, X=7555 = col 472.19")
print(f"Spikes (tile 17) at row 16, cols 441-453 = X=7056-7263")
print(f"Solid blocks (tile 28) at row 17, cols 441-453 = X=7056-7263")

# Look for the transition area around col 453-472
print(f"\n=== DETAILED COLS 440-480, ROWS 14-26 ===")
for c in range(440, min(481, len(rows[0]))):
    col_tiles = []
    for r in range(14, min(27, len(rows))):
        t = rows[r][c]
        if t != 0:
            actual = t - 1
            col_tiles.append(f"r{r}={t}(0x{actual:02X})")
    if col_tiles:
        print(f"  Col {c} (X={c*16}-{c*16+15}): {', '.join(col_tiles)}")
    else:
        print(f"  Col {c} (X={c*16}-{c*16+15}): empty")
