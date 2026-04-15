#!/usr/bin/env python3
"""Check FLOOR_CEIL at row 12 for gaps beyond col 508"""
import xml.etree.ElementTree as ET, re

tree = ET.parse(r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\kratos.tmx")
root = tree.getroot()

collision_names_raw = """COL_NONE COL_FLOOR_CEIL COL_FLOOR_CEIL COL_BOTTOM COL_DEATH_TOP COL_FLOOR_CEIL COL_FLOOR_CEIL COL_NONE
COL_DEATH_BOTTOM COL_DEATH_BOTTOM COL_DEATH_TOP COL_DEATH_TOP COL_DEATH_BOTTOM COL_DEATH_TOP COL_DEATH_LEFT COL_DEATH_RIGHT
COL_ALL COL_DEATH COL_DEATH_BOTTOM COL_DEATH_BOTTOM COL_DEATH_TOP COL_TOP_CENTER_SPIKE COL_ALL COL_DEATH_TOP
COL_DEATH_BOTTOM COL_TOP COL_DEATH COL_DEATH COL_DEATH COL_DEATH_LEFT COL_DEATH_TOP COL_DEATH_RIGHT
COL_ALL COL_ALL COL_ALL COL_ALL COL_ALL COL_ALL COL_ALL COL_ALL COL_ALL COL_ALL COL_ALL COL_ALL COL_ALL COL_ALL COL_ALL COL_NONE
COL_ALL COL_ALL COL_ALL COL_ALL COL_NONE COL_NONE COL_NONE COL_TOP_CENTER_SPIKE COL_ALL COL_ALL COL_ALL COL_DEATH_BOTTOM COL_ALL COL_ALL COL_ALL COL_ALL
COL_ALL COL_ALL COL_ALL COL_ALL COL_ALL COL_ALL COL_ALL COL_ALL COL_ALL COL_ALL COL_ALL COL_ALL COL_ALL COL_ALL COL_ALL COL_NONE
COL_ALL COL_ALL COL_ALL COL_ALL COL_TOP COL_TOP COL_TOP COL_TOP COL_BOTTOM COL_BOTTOM COL_BOTTOM COL_DEATH COL_DEATH_BOTTOM COL_DEATH_TOP COL_DEATH_RIGHT COL_DEATH_LEFT
COL_ALL COL_ALL COL_ALL COL_ALL COL_ALL COL_ALL COL_ALL COL_ALL COL_ALL COL_ALL COL_ALL COL_ALL COL_ALL COL_ALL COL_ALL COL_NONE
COL_ALL COL_ALL COL_ALL COL_ALL COL_DOWN_RIGHT_SPIKE COL_DEATH_BOTTOM COL_DOWN_LEFT_SPIKE COL_DEATH_RIGHT COL_NONE COL_DEATH_LEFT COL_UP_RIGHT_SPIKE COL_DEATH_TOP COL_UP_LEFT_SPIKE COL_DEATH COL_ALL COL_DEATH_BOTTOM
COL_NONE COL_NONE COL_DEATH COL_DEATH COL_DEATH_BOTTOM COL_DEATH_TOP COL_DEATH_BOTTOM COL_DEATH_TOP COL_FLOOR_CEIL COL_FLOOR_CEIL COL_RIGHT COL_LEFT COL_RIGHT COL_LEFT COL_NONE COL_NO_SIDE"""

col_table = re.findall(r'COL_\w+', collision_names_raw)

def get_collision(tmx_tid):
    if tmx_tid == 0: return "."
    local = tmx_tid - 1
    if local in (0xDF, 0xE3, 0xFE, 0xFF): local = 0x00
    elif local == 0xFD: local = 0x26
    if local < len(col_table): return col_table[local]
    return f"?{local}"

layers = root.findall('layer')
tl = layers[0]
w = int(tl.get('width'))
tiles = [int(x) for x in tl.find('data').text.strip().split(',')]

# Check row 12 for FLOOR_CEIL from col 500 to end of level (col 843)
print("Row 12 FLOOR_CEIL status (col 500-700):")
current_status = None
start_col = 500
for c in range(500, min(701, w)):
    tid = tiles[12 * w + c]
    cname = get_collision(tid)
    is_fc = "FLOOR_CEIL" in cname
    if is_fc != current_status:
        if current_status is not None:
            print(f"  cols {start_col}-{c-1} (X={start_col*16}-{(c-1)*16+15}): {'FLOOR_CEIL' if current_status else 'OPEN'}")
        current_status = is_fc
        start_col = c
if current_status is not None:
    print(f"  cols {start_col}-700 (X={start_col*16}-{700*16+15}): {'FLOOR_CEIL' if current_status else 'OPEN'}")

# Also check row 11
print("\nRow 11 FLOOR_CEIL status (col 440-700):")
current_status = None
start_col = 440
for c in range(440, min(701, w)):
    tid = tiles[11 * w + c]
    cname = get_collision(tid)
    is_fc = "FLOOR_CEIL" in cname
    if is_fc != current_status:
        if current_status is not None:
            print(f"  cols {start_col}-{c-1} (X={start_col*16}-{(c-1)*16+15}): {'FLOOR_CEIL' if current_status else 'OPEN'}")
        current_status = is_fc
        start_col = c
if current_status is not None:
    print(f"  cols {start_col}-700 (X={start_col*16}-{700*16+15}): {'FLOOR_CEIL' if current_status else 'OPEN'}")
