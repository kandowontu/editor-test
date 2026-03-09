#!/usr/bin/env python3
"""List all interactive sprites in the second ship section."""
import xml.etree.ElementTree as ET

t = ET.parse(r'c:\Editor Test\stereomadness.tmx')
r = t.getroot()
w = int(r.attrib['width'])

sp_layer = None
for l in r.findall('layer'):
    if l.attrib.get('name', '') == 'SP':
        sp_layer = l
        break
sp_data = sp_layer.find('data')
sp_gids = [int(x) for x in sp_data.text.strip().split(',')]

SPRITE_FG = 257

print('=== ALL interactive sprites cols 760-885 ===')
for col in range(760, 886):
    for row in range(0, 27):
        gid = sp_gids[row * w + col]
        if gid > 0:
            sid = (gid - SPRITE_FG) & 0xFF
            er = row - 3
            y = er * 16
            label = f'0x{sid:02X}'
            
            # Game mode portals
            modes = {0:'Cube', 1:'Ship', 2:'Ball', 3:'UFO', 4:'Wave', 0x17:'Spider', 0x24:'Robot'}
            if sid in modes:
                label += f' [{modes[sid]} Portal]'
            elif sid in (0x08,0x09,0x10,0x11,0x12,0x13,0xFB,0xFC):
                gt = 'Flip' if sid in (0x09,0x12,0x13,0xFB) else 'Normal'
                label += f' [Gravity Portal {gt}]'
            elif sid in (0x0A,0x0C):
                label += ' [Yellow Pad]'
            elif sid in (0x25,0x26):
                label += ' [Pink Pad]'
            elif sid in (0x52,0x53):
                label += ' [Red Pad]'
            elif sid in (0x0D,0x0E,0xFD,0xFE):
                label += ' [Blue Pad]'
            elif sid == 0x0B:
                label += ' [Yellow Orb]'
            elif sid == 0x06:
                label += ' [Pink Orb]'
            elif sid == 0x28:
                label += ' [Red Orb]'
            elif sid in (0x05,0x7B):
                label += ' [Blue Orb]'
            elif sid in (0x27,0x7C):
                label += ' [Green Orb]'
            elif sid in (0x07,0x1A,0x1B):
                label += ' [COIN]'
            elif sid in (0x14,0x15,0x16,0x20,0x21,0x6D):
                speeds = {0x14:'0.5x', 0x15:'1x', 0x16:'2x', 0x20:'3x', 0x21:'4x', 0x6D:'slow'}
                label += f' [Speed {speeds[sid]}]'
            elif sid == 0x0F:
                label += ' [END LEVEL]'
            elif sid in (0x18,0x19):
                label += ' [Mini/Growth Portal]'
            else:
                label += ' [deco/other]'
            
            print(f'  Col {col} ER{er} (Y={y}): {label}')
