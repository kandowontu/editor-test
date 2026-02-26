import re

with open(r'C:\Editor Test\famidash\levels\LEVEL DATA\lvlset_HUGE\baseafterbase.tmx', 'r') as f:
    content = f.read()

# Find all data sections
matches = list(re.finditer(r'<data\s+encoding=["\']csv["\']>\s*(.*?)\s*</data>', content, re.DOTALL))
print(f"Found {len(matches)} data sections")

if not matches:
    # Try finding the encoding attribute differently
    idx = content.find('encoding=')
    if idx >= 0:
        print("Encoding context:", repr(content[idx:idx+30]))
    idx = content.find('<data')
    if idx >= 0:
        print("Data tag context:", repr(content[idx:idx+50]))
    exit()

# Use first layer
csv_data = matches[0].group(1).strip()
rows = []
for line in csv_data.split('\n'):
    line = line.strip()
    if line.endswith(','):
        line = line[:-1]
    if line:
        vals = [int(x.strip()) for x in line.split(',')]
        rows.append(vals)

print(f'Map: {len(rows[0])} cols x {len(rows)} rows, tile=16x16')
print(f'X=6836 -> col {6836//16}, X=7555 -> col {7555//16}')
print()

# Show columns 420-480 for all 27 rows
col_start = 420
col_end = 480
print(f'Tile data cols {col_start}-{col_end}:')
header = 'Row | '
for c in range(col_start, min(col_end+1, len(rows[0]))):
    header += f'{c%100:3d} '
print(header)
print('-' * len(header))

for r, row in enumerate(rows):
    line = f'{r:3d} | '
    for c in range(col_start, min(col_end+1, len(rows[0]))):
        t = row[c] if c < len(row) else -1
        if t == 0:
            line += '  . '
        else:
            line += f'{t:3d} '
    print(line)

print()
print("=== LEGEND ===")
print("Tile IDs of interest:")
print("  0 = empty")
print("  31 (0x1F) = floor spike (death)")
print("  17 = spike?")
print("  28 = solid block?")
print("  7,3 = background pattern")
print("  37,48,35,36,40,41,42,43,44 = platform/structure tiles")
print("  6,2 = similar pattern (different layer?)")
print()

# Find unique non-zero tile IDs in the region
unique_ids = set()
for r, row in enumerate(rows):
    for c in range(col_start, min(col_end+1, len(rows[0]))):
        t = row[c] if c < len(row) else 0
        if t != 0:
            unique_ids.add(t)

print(f"Unique non-zero tile IDs in cols {col_start}-{col_end}: {sorted(unique_ids)}")

# Now let's specifically look for tile ID 31 (floor spike) and 17
print()
print("=== FLOOR SPIKES (ID=31/0x1F) locations ===")
for r, row in enumerate(rows):
    for c in range(col_start, min(col_end+1, len(rows[0]))):
        t = row[c] if c < len(row) else 0
        if t == 31:
            print(f"  Row {r}, Col {c} (X={c*16}-{c*16+15}, Y={r*16}-{r*16+15})")

print()
print("=== SPIKE-like tiles (ID=17) locations ===")
for r, row in enumerate(rows):
    for c in range(col_start, min(col_end+1, len(rows[0]))):
        t = row[c] if c < len(row) else 0
        if t == 17:
            print(f"  Row {r}, Col {c} (X={c*16}-{c*16+15}, Y={r*16}-{r*16+15})")

# Also check for common solid/platform tiles
print()
print("=== Solid/platform tiles in region ===")
platform_ids = {37, 48, 35, 36, 40, 41, 42, 43, 44, 28, 4}
for r, row in enumerate(rows):
    found = []
    for c in range(col_start, min(col_end+1, len(rows[0]))):
        t = row[c] if c < len(row) else 0
        if t in platform_ids:
            found.append((c, t))
    if found:
        print(f"  Row {r}: {found}")
