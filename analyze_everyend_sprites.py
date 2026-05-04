#!/usr/bin/env python3
"""Look at WIDER area around X=11000-13000 for sprites and platforms"""

import xml.etree.ElementTree as ET

TMX = r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\everyend.tmx"

tree = ET.parse(TMX)
root = tree.getroot()
mapW = int(root.get('width'))
mapH = int(root.get('height'))

groundRowsToReserve = 3
tile_layer = None
sp_layer = None

for layer in root.findall('.//layer'):
    name = layer.get('name') or ''
    data = layer.find('data')
    if data is not None:
        text = data.text.strip()
        vals = [int(x.strip()) for x in text.split(',') if x.strip()]
        grid = []
        for r in range(mapH):
            grid.append(vals[r*mapW:(r+1)*mapW])
        if name == 'SP':
            sp_layer = grid
        else:
            tile_layer = grid

SPRITE_NAMES = {
    0x01: 'SHIP_PORT', 0x02: 'CUBE_PORT', 0x03: 'BALL_PORT', 0x04: 'UFO_PORT', 0x05: 'WAVE_PORT', 0x06: 'ROBOT_PORT',
    0x07: 'SPIDER_PORT', 0x08: 'SIZE_PORT', 0x09: 'SPEED_PORT', 0x0A: 'TELE_PORT',
    0x11: 'YELLOW_ORB', 0x12: 'PINK_ORB', 0x13: 'RED_ORB', 0x14: 'BLACK_ORB', 0x15: 'GREEN_ORB', 0x16: 'BLUE_ORB',
    0x1A: 'YEL_ORB_UP', 0x1B: 'PINK_ORB_UP',
    0x21: 'YELLOW_PAD', 0x22: 'PINK_PAD', 0x23: 'RED_PAD', 0x27: 'BLACK_PAD',
    0x28: 'COIN',
}

print("=== Sprites X=11000-13000 ===")
if sp_layer:
    for row in range(0, mapH):
        for col in range(11000//16, 13000//16+1):
            if row < len(sp_layer) and col < len(sp_layer[row]):
                sid = sp_layer[row][col]
                if sid != 0:
                    name = SPRITE_NAMES.get(sid, f'UNK_0x{sid:02X}')
                    print(f"  {name} (0x{sid:02X}) at pixel ({col*16},{row*16})")
