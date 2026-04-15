#!/usr/bin/env python3
"""Check for gaps between upper and lower corridors after the barrier (X=8256+).
Upper corridor: Y=76-128. Lower corridor: Y=224-300.
Gap region: Y=128-224. Is there any passage?"""

import xml.etree.ElementTree as ET

tree = ET.parse(r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\kratos.tmx")
root = tree.getroot()
width = int(root.attrib['width'])
height = int(root.attrib['height'])

layer = root.findall('.//layer')[0]
data = layer.find('data').text.strip()
tiles = [int(x) for x in data.split(',')]

GRR = 3

# Full collision table (simplified - just key categories)
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

def is_blocking(name):
    """Does this collision type block movement?"""
    if name in ('NONE', '.'): return False
    if name.startswith('S') and ('45' in name or '22' in name or '66' in name): 
        return True  # slopes - partial blocking
    if name in ('ALL', 'FC', 'NS', 'ALL2'): return True  # fully solid
    if name.startswith('D') or name == 'DEATH' or 'DEATH' in name: return True  # death
    if name in ('TOP', 'BOT', 'LEFT', 'RIGHT', 'TOP2', 'TOP3', 'TOP4', 'TOP5', 
                'BOT2', 'RIGHT2', 'RIGHT3', 'RIGHT4', 'LEFT2', 'LEFT3'): return True  # directional
    if name in ('TLS', 'TRS', 'BLS', 'BRS', 'BLS2', 'BRS2', 'TLBR', 'TRBL'): return True
    if name in ('UL', 'UR', 'DwL', 'DwR', 'UL2', 'UR2', 'DwR2', 'DwL2'): return True
    if name in ('TCS', 'BCS'): return True  # center spikes
    if name in ('LSB', 'RSB'): return True  # spike blocks
    if name.endswith('S') and name[0] in ('U', 'D'): return True  # various spikes
    return True  # assume blocking if unknown

def get_coll_name(col, row):
    if col < 0 or col >= width or row < 0 or row >= height:
        if row >= height: return "FC_GROUND"
        return "OOB"
    gid = tiles[row * width + col]
    if gid == 0: return "."
    if gid > 256: return f"SPR{gid-257}"
    idx = gid - 1
    if idx < len(coll_names): return coll_names[idx]
    return f"?{idx}"

def world_to_row(y):
    return y // 16 + GRR

# Check vertical terrain profile at every column from 516 to 570 (X=8256 to 9120)
# Look for any column where ALL Y levels from upper corridor (Y=128) to lower corridor (Y=272) are EMPTY
print("=== VERTICAL PROFILES: Looking for gaps between corridors ===")
print("Checking X=8000-9200 (cols 500-575)")
print("Upper corridor ~Y=76-128, Lower corridor ~Y=224-300")
print("Need: continuous EMPTY column from ~Y=128 down to ~Y=224")
print()

for col in range(500, 576):
    x = col * 16
    row_data = []
    all_empty = True
    for y in range(128, 240, 16):  # Check Y=128 to Y=224
        row = world_to_row(y)
        name = get_coll_name(col, row)
        row_data.append((y, name))
        if name != ".":
            all_empty = False
    
    if all_empty:
        print(f"Col {col} (X={x}): ALL EMPTY from Y=128-224! ***GAP***")
        # Also check what's at Y=96-112 (top of gap) and Y=240-272 (bottom of gap)
        for y in [96, 112, 128, 144, 160, 176, 192, 208, 224, 240, 256, 272, 288]:
            row = world_to_row(y)
            name = get_coll_name(col, row)
            print(f"  Y={y}: {name}")
    else:
        # Show what blocks the gap
        blocking = [(y, n) for y, n in row_data if n != "."]
        if len(blocking) <= 3:  # Near-gap: only a few blockers
            print(f"Col {col} (X={x}): Near-gap, blockers: {blocking}")

# Also look for ANY empty path at specific Y levels through the whole range
print("\n=== HORIZONTAL CONTINUITY AT KEY Y LEVELS (X=8128-9120) ===")
for y in [128, 144, 160, 176, 192, 208, 224]:
    row = world_to_row(y)
    empty_count = 0
    max_empty_run = 0
    current_run = 0
    for col in range(508, 571):
        name = get_coll_name(col, row)
        if name == ".":
            empty_count += 1
            current_run += 1
            max_empty_run = max(max_empty_run, current_run)
        else:
            current_run = 0
    print(f"Y={y} (row={row}): {empty_count}/63 empty tiles, longest empty run={max_empty_run}")
