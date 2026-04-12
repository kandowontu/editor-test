import xml.etree.ElementTree as ET

tree = ET.parse(r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\groundtospace.tmx")
root = tree.getroot()

# Parse BG and SP layers
bg_data = None
sp_data = None
for layer in root.findall('.//layer'):
    name = layer.get('name')
    data = layer.find('data')
    if data is not None:
        rows = []
        for line in data.text.strip().split('\n'):
            line = line.strip().rstrip(',')
            if line:
                rows.append([int(x) for x in line.split(',')])
        if name == 'SP':
            sp_data = rows
        elif bg_data is None:
            bg_data = rows

print(f"BG rows={len(bg_data)}, cols={len(bg_data[0])}")

# Ship portal at X=8176 (col 511), coin2 at X=10512 (col 657), UFO portal at X=10848 (col 678)
# Show cols 505-680 (X=8080 to X=10880)
col_start, col_end = 505, 680

# Find coin and portal positions in SP layer
coin_col = 10512 // 16  # 657
coin_row = 192 // 16    # 12
ship_col = 8176 // 16   # 511
ufo_col = 10848 // 16   # 678

print(f"\nTerrain map (cols {col_start}-{col_end}, X={col_start*16}-{col_end*16}):")
print(f"Ship portal: col {ship_col}, Coin 2: col {coin_col} row {coin_row}, UFO portal: col {ufo_col}")
print()

for row in range(len(bg_data)):
    line = ""
    for col in range(col_start, col_end + 1):
        if row == coin_row and col == coin_col:
            ch = 'C'  # coin
        elif col == ship_col:
            ch = 'S' if bg_data[row][col] == 0 else '$'
        elif col == ufo_col:
            ch = 'U' if bg_data[row][col] == 0 else '%'
        else:
            ch = '.' if bg_data[row][col] == 0 else '#'
    line += ch
    # Compress: show every 4th column for readability
    print(f"r{row:02d} ", end="")
    for col in range(col_start, col_end + 1):
        if row == coin_row and col == coin_col:
            ch = 'C'
        elif col == ship_col:
            ch = '|'
        elif col == ufo_col:
            ch = '|'
        else:
            ch = '.' if bg_data[row][col] == 0 else '#'
        print(ch, end="")
    print()

# Also show SP layer sprites in this area
print("\nSprites in area:")
mode_names = {0:'cube',1:'ship',2:'ball',3:'ufo',4:'robot',0x17:'spider',0x24:'wave'}
for row in range(len(sp_data)):
    for col in range(col_start, col_end + 1):
        v = sp_data[row][col]
        if v > 0:
            sid = v - 257
            x = col * 16
            y = row * 16
            label = ""
            if sid in (0x07, 0x1A, 0x1B):
                label = " [COIN]"
            elif sid in mode_names:
                label = f" [{mode_names[sid]}]"
            elif sid in (0x08, 0x09):
                label = f" [grav {'down' if sid==8 else 'up'}]"
            print(f"  sid=0x{sid:02X} X={x} Y={y} row={row} col={col}{label}")
