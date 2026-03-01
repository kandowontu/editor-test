import xml.etree.ElementTree as ET

TMX_PATH = r'c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\polargeist.tmx'

tree = ET.parse(TMX_PATH)
root = tree.getroot()
map_w = int(root.get('width'))
map_h = int(root.get('height'))
tw = int(root.get('tilewidth'))
th = int(root.get('tileheight'))

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

tile = layers['tiles']
sp = layers['SP']

# CENTER_DEATH positions from debug log:
# X=8960, Y=369 => col=560, row=23 (369/16=23.06)
# X=9087, Y=337 => col=567-568, row=21 (337/16=21.06) 
# X=9215, Y=305 => col=575-576, row=19 (305/16=19.06)
# X=9342, Y=273 => col=583-584, row=17 (273/16=17.06)
# X=9439, Y=241 => col=589-590, row=15 (241/16=15.06)

print("="*80)
print("CENTER_DEATH POSITION ANALYSIS")
print("="*80)

death_positions = [
    (8960, 369, "ground level"),
    (9087, 337, "elevated Y=337"),
    (9173, 364, "eject death"),
    (9215, 305, "jumped higher"),
    (9342, 273, "jumped higher"),
    (9439, 241, "jumped highest"),
]

for px, py, desc in death_positions:
    col = px // tw
    row = py // th
    print(f"\nDeath at px=({px},{py}) => col={col}, row={row} [{desc}]")
    # Check tile and SP at this position and surrounding area
    for dr in range(-2, 3):
        for dc in range(-2, 3):
            r, c = row+dr, col+dc
            if 0 <= r < map_h and 0 <= c < map_w:
                t = tile[r][c]
                s = sp[r][c]
                if t != 0 or s != 0:
                    print(f"  [{r:2d},{c:3d}] px({c*tw},{r*th}) tile={t:3d} SP={s:3d} (SP-257={s-257 if s > 0 else 'n/a'} = 0x{s-257:02X} if s>0)")

# Show complete columns at each death X
print("\n" + "="*80)
print("COMPLETE COLUMN DATA AT DEATH X POSITIONS")
print("="*80)

for px in [8960, 9088, 9216, 9344, 9440]:
    col = px // tw
    print(f"\n--- Column {col} (X={col*tw}) ---")
    for r in range(map_h):
        t = tile[r][col]
        s = sp[r][col]
        if t != 0 or s != 0:
            sid_str = f"sid=0x{s-257:02X}" if s > 0 else ""
            print(f"  row={r:2d} Y={r*th:3d} tile={t:3d} SP={s:3d} {sid_str}")

# ============================================================
# Now look at COIN 3 area
# ============================================================
print("\n" + "="*80)
print("COIN 3 DETAILED ANALYSIS")
print("="*80)
# Coin 3: SP=284 at (14,719) pixel (11504,224)
# hitbox=(11504,175)-(11520,191)

# Portal at sid=0x10 near coin 3: anchorX=11384, box=(11380,176)-(11420,190)
# This is very close to coin 3 hitbox Y!

c3_r, c3_c = 14, 719
print(f"Coin 3 SP tile at row={c3_r}, col={c3_c}, pixel=({c3_c*tw},{c3_r*th})")
print(f"Coin 3 hitbox: (11504,175)-(11520,191)")
print(f"Portal/orb sid=0x10 at anchorX=11384, box=(11380,176)-(11420,190)")

# Show tile/SP around coin 3
print(f"\n--- Tile Layer around coin 3 (cols 700-740, rows 8-20) ---")
hdr = "     " + "".join(f"{c:4d}" for c in range(700, 741))
print(hdr)
for r in range(8, 21):
    vals = [tile[r][c] for c in range(700, 741)]
    if any(v != 0 for v in vals):
        line = f"R{r:2d}: " + "".join(f"{v:4d}" for v in vals)
        print(line)
    else:
        print(f"R{r:2d}:  (all zeros)")

print(f"\n--- SP Layer around coin 3 (cols 700-740, rows 8-20) ---")
hdr = "     " + "".join(f"{c:4d}" for c in range(700, 741))
print(hdr)
for r in range(8, 21):
    vals = [sp[r][c] for c in range(700, 741)]
    if any(v != 0 for v in vals):
        line = f"R{r:2d}: " + "".join(f"{v:4d}" for v in vals)
        print(line)

# Death regions around coin 3:
# CENTER_DEATH at X=11294, Y=337 => col=706, row=21
# FWD_DEATH at X=11209, Y=312 => col=700, row=19
# FWD_DEATH at X=11457, Y=363 => col=716, row=22
# FLOOR_SPIKE at X=11380, Y=369 => col=711, row=23
# FLOOR_SPIKE at X=11383, Y=382 => col=711, row=23

print(f"\n--- Coin 3 death positions ---")
c3_deaths = [
    (11209, 312, "FWD_DEATH"),
    (11294, 337, "CENTER_DEATH"),
    (11380, 369, "FLOOR_SPIKE"),
    (11457, 363, "FWD_DEATH"),
    (11504, 175, "MISSED_COIN dY"),
]
for px, py, desc in c3_deaths:
    col = px // tw
    row = py // th
    print(f"  {desc}: px=({px},{py}) => col={col}, row={row}")
    for dr in range(-1, 2):
        for dc in range(-1, 2):
            r, c = row+dr, col+dc
            if 0 <= r < map_h and 0 <= c < map_w:
                t = tile[r][c]
                s = sp[r][c]
                if t != 0 or s != 0:
                    sid_str = f"sid=0x{s-257:02X}" if s > 0 else ""
                    print(f"    [{r},{c}] px({c*tw},{r*th}) tile={t} SP={s} {sid_str}")

# ============================================================
# Show complete columns around coin 3 death area
# ============================================================
print(f"\n--- Complete columns 706-720 (coin 3 area) ---")
for col in range(706, 721):
    data_lines = []
    for r in range(map_h):
        t = tile[r][col]
        s = sp[r][col]
        if t != 0 or s != 0:
            sid_str = f"sid=0x{s-257:02X}" if s > 0 else ""
            data_lines.append(f"  r={r:2d} Y={r*th:3d} tile={t:3d} SP={s:3d} {sid_str}")
    if data_lines:
        print(f"\nCol {col} (X={col*tw}):")
        for dl in data_lines:
            print(dl)

# ============================================================
# SP value -> SID mapping summary for sprites in coin 2 & 3 areas
# ============================================================
print("\n" + "="*80)
print("SPRITE ID REFERENCE (SP_value - 257 = SID)")
print("="*80)

interesting_sp = set()
for r in range(map_h):
    for c in range(540, min(740, map_w)):
        v = sp[r][c]
        if v != 0:
            interesting_sp.add(v)

for v in sorted(interesting_sp):
    sid = v - 257
    locs = []
    for r in range(map_h):
        for c in range(map_w):
            if sp[r][c] == v:
                locs.append((r, c))
    print(f"  SP={v:3d} => sid=0x{sid:02X} ({sid:3d})  count={len(locs)}  first_at=({locs[0][0]},{locs[0][1]}) px({locs[0][1]*tw},{locs[0][0]*th})")
