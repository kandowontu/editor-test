#!/usr/bin/env python3
"""Quick check: what collision types are at Y=128-224 between the corridors?"""

import xml.etree.ElementTree as ET

tree = ET.parse(r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\kratos.tmx")
root = tree.getroot()
width = int(root.attrib['width'])
height = int(root.attrib['height'])

layer = root.findall('.//layer')[0]
data = layer.find('data').text.strip()
tiles = [int(x) for x in data.split(',')]

GRR = 3

COLL_TABLE_TEXT = """NONE FC FC BOT DT FC FC NONE DB DB DT DT DB DT DL DR
ALL DEATH DB DB DT TCS ALL DT DB TOP DEATH DEATH DEATH DL DT DR
ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL NONE
ALL ALL ALL ALL NONE NONE NONE TCS ALL ALL ALL DB ALL ALL ALL ALL
ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL NONE
ALL ALL ALL ALL TOP TOP TOP TOP BOT BOT BOT DEATH DB DT DR DL
ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL NONE
ALL ALL ALL ALL DRS DB DLS DR NONE DL URS DT ULS DEATH ALL DB
NONE NONE DEATH DEATH DB DT DB DT FC FC RIGHT LEFT RIGHT LEFT NONE NS
SRD45 SLD45 SRU45 SLU45 SRD22R SRD22L SLD22R SLD22L SRU22R SRU22L SLU22R SLU22L SRD66T SRD66B SLD66B SLD66T
SRU66T SRU66B SLU66B SLU66T NS NS NS NS LSB RSB BLS BRS BS DLS2 DRS2 DBS
UL UR DwL DwR TOP BOT LEFT RIGHT TLS TRS BLS2 BRS2 TLBR TRBL ULS2 URS2
UBS DTR DTL DBR DBL NONE NONE BCS BCS TOP BOT NONE NONE NONE NONE NONE
NONE NONE NONE ALL NONE NONE NONE NONE NONE DRS3 DLS3 ULS3 URS3 LEFT RIGHT UR2
RIGHT2 TOP2 TOP3 UL2 TOP4 RIGHT3 BOT2 TOP5 NONE NONE NONE NONE NONE NONE NONE NONE
DRS4 DLS4 URS4 ULS4 DRS5 DLS5 DEATH2 RIGHT4 LEFT2 URS5 ULS5 DEATH3 ALL2 LEFT3 DwR2 DwL2"""

coll_names = []
for line in COLL_TABLE_TEXT.strip().split('\n'):
    coll_names.extend(line.split())

def get_coll_name(col, row):
    if col < 0 or col >= width or row < 0 or row >= height:
        return "FC_GROUND" if row >= height else "OOB"
    gid = tiles[row * width + col]
    if gid == 0: return "EMPTY"
    if gid > 256: return f"SPR{gid-257}"
    idx = gid - 1
    if idx < len(coll_names): return coll_names[idx]
    return f"?{idx}"

# Check what's at Y=128-224 for representative columns
print("=== TILE TYPES AT Y=128-224 (gap between corridors) ===")
print("Checking at X=8500 (col 531), X=8700 (col 544), X=9000 (col 563)")
for col in [531, 544, 556, 563]:
    print(f"\nCol {col} (X={col*16}):")
    for y in range(80, 320, 16):
        row = y // 16 + GRR
        name = get_coll_name(col, row)
        blocking = name not in ("NONE", "EMPTY")
        marker = " <-- PASSABLE" if not blocking else ""
        print(f"  Y={y:3d} (row={row:2d}): {name}{marker}")

# Check how many passable columns exist in the gap
print("\n=== PASSABLE VERTICAL COLUMNS Y=128-224 (X=7248-9120) ===")
passable_cols = []
for col in range(453, 571):
    passable = True
    for y in range(128, 224, 16):  # Y=128 to Y=208 (7 tiles)
        row = y // 16 + GRR
        name = get_coll_name(col, row)
        if name not in ("NONE", "EMPTY"):
            passable = False
            break
    if passable:
        passable_cols.append(col)

if passable_cols:
    print(f"Found {len(passable_cols)} passable columns: {passable_cols}")
    print(f"X range: {passable_cols[0]*16} to {passable_cols[-1]*16}")
else:
    print("NO passable vertical columns found between corridors!")
    # Show what's blocking
    print("\nSample blocking tiles at Y=160 (middle of gap):")
    row = 160 // 16 + GRR  # = 13
    for col in range(508, 571, 5):
        name = get_coll_name(col, row)
        print(f"  Col {col} (X={col*16}): {name}")
