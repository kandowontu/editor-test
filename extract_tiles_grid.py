import xml.etree.ElementTree as ET
import csv
import io

tmx_path = r'C:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\jumper.tmx'
tree = ET.parse(tmx_path)
root = tree.getroot()

# Get first layer
layer = root.find('layer')
data_elem = layer.find('data')
csv_text = data_elem.text.strip()

# Parse all rows
rows = []
for line in csv_text.split('\n'):
    line = line.strip().rstrip(',')
    if line:
        vals = [int(x) for x in line.split(',')]
        rows.append(vals)

print(f"Total rows: {len(rows)}, cols per row: {len(rows[0])}")

# Extract tileX=715 to 740, storage rows 3-26 (game tileY 0-23)
tx_start, tx_end = 715, 740
sy_start, sy_end = 3, 26

# Print header
header = "tileY\\tileX|"
for tx in range(tx_start, tx_end + 1):
    header += f"{tx:>5}"
print(header)
print("-" * len(header))

for sy in range(sy_start, sy_end + 1):
    gy = sy - 3
    parts = []
    for tx in range(tx_start, tx_end + 1):
        tmx_val = rows[sy][tx]
        if tmx_val > 0:
            metatile = tmx_val - 1
            parts.append(f" {metatile:3X}")
        else:
            parts.append("    .")
    print(f"  Y={gy:2d}     |{''.join(parts)}")
