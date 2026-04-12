import xml.etree.ElementTree as ET
import struct

# Parse TMX
tree = ET.parse(r'famidash\LEVELS\LEVEL DATA\lvlset_HUGE\groundtospace.tmx')
root = tree.getroot()
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

# The PF coordinate system has Y offset of 40 from TMX.
# PF Y = TMX_Y - 40   (as deduced: TMX Y=192 → PF Y=152)
# So PF Y=159 → TMX Y=199 → row 199/16 = 12.44 → row 12
# Coin hitbox: PF Y=151-167 → TMX Y=191-207 → rows 11-12

# Show detailed tile values around coin X=10512 (col 657)
# For a ship player at various PF Y positions (135-180)
# Corresponding TMX rows
print("Ship player hitbox overlap with terrain:")
print("PF coords: coin hitbox Y=151-167, X=10512-10528")
print()

# Check BG tiles in the area
# PF Y to TMX row: row = (Y + 40) / 16  
# Actually: the offset is not exactly 40. Let me compute from known positions:
# Coin: SP layer row=12 (TMX Y=192), PF pos Y=152. So PF Y = TMX_Y - 40.
# Player starts at PF Y=369. TMX equivalent: 369+40 = 409. Row 409/16 = 25.56 → row 25-26 area.
# Level has 27 rows (0-26), height=432. Bottom = row 26 = Y=416 TMX.
# PF start Y=369 → TMX Y=409. In a 432-pixel level with death floor at bottom, row 25-26 is near bottom. Makes sense.

# So PF_Y = TMX_Y - 40, or TMX_Y = PF_Y + 40.
# For PF Y=151: TMX Y=191, TMX row=191//16 = 11, local_y=191%16 = 15
# For PF Y=167: TMX Y=207, TMX row=207//16 = 12, local_y=207%16 = 15

# For the coin's area (cols 655-660), show BG tiles at rows 9-15
print("BG tiles near coin (cols 655-660, rows 9-15):")
for row in range(9, 16):
    vals = []
    for col in range(655, 661):
        vals.append(f"{bg_data[row][col]:3d}")
    pf_y = row * 16 - 40
    print(f"  row={row} TMX_Y={row*16} PF_Y={pf_y}  tiles: {' '.join(vals)}")

# Also show the collision type interpretation
# Tile 1 = metatile 1 (likely passable)
# Tile 7 = metatile 7 (likely solid/passable background)
# Need to check the collision map
print()

# Show a wider vertical strip at the coin's exact column (657)
print("Full vertical strip at coin col=657 (X=10512):")
for row in range(len(bg_data)):
    v = bg_data[row][657]
    pf_y = row * 16 - 40
    coin_mark = " << COIN CENTER (PF Y=159)" if row == 12 else ""
    print(f"  row={row:2d} TMX_Y={row*16:3d} PF_Y={pf_y:4d}  tile={v:3d}{coin_mark}")
