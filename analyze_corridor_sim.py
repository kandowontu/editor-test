#!/usr/bin/env python3
"""Simulate FindCorridorCenter at the gap location to understand PD behavior."""
import xml.etree.ElementTree as ET

t = ET.parse(r'c:\Editor Test\stereomadness.tmx')
r = t.getroot()
w = int(r.attrib['width'])
layer = None
for l in r.findall('layer'):
    name = l.attrib.get('name', '')
    if name == '' or name is None:
        layer = l
        break
data = layer.find('data')
gids = [int(x) for x in data.text.strip().split(',')]

COLLTABLE = 'NONE,FLOOR_CEIL,FLOOR_CEIL,BOTTOM,DEATH_TOP,FLOOR_CEIL,FLOOR_CEIL,NONE,DEATH_BOTTOM,DEATH_BOTTOM,DEATH_TOP,DEATH_TOP,DEATH_BOTTOM,DEATH_TOP,DEATH_LEFT,DEATH_RIGHT,ALL,DEATH,DEATH_BOTTOM,DEATH_BOTTOM,DEATH_TOP,TOP_CENTER_SPIKE,ALL,DEATH_TOP,DEATH_BOTTOM,TOP,DEATH,DEATH,DEATH,DEATH_LEFT,DEATH_TOP,DEATH_RIGHT,ALL,ALL,ALL,ALL,ALL,ALL,ALL,ALL,ALL,ALL,ALL,ALL,ALL,ALL,ALL,NONE,ALL,ALL,ALL,ALL,NONE,NONE,NONE,TOP_CENTER_SPIKE,ALL,ALL,ALL,DEATH_BOTTOM,ALL,ALL,ALL,ALL,ALL,ALL,ALL,ALL,ALL,ALL,ALL,ALL,ALL,ALL,ALL,ALL,ALL,ALL,ALL,NONE_PAL,ALL,ALL,ALL,ALL,TOP,TOP,TOP,TOP,BOTTOM,BOTTOM,BOTTOM,DEATH,DEATH_BOTTOM,DEATH_TOP,DEATH_RIGHT,DEATH_LEFT,ALL,ALL,ALL,ALL,ALL,ALL,ALL,ALL,ALL,ALL,ALL,ALL,ALL,ALL,ALL,NONE,ALL,ALL,ALL,ALL,DOWN_RIGHT_SPIKE,DEATH_BOTTOM,DOWN_LEFT_SPIKE,DEATH_RIGHT,NONE,DEATH_LEFT,UP_RIGHT_SPIKE,DEATH_TOP,UP_LEFT_SPIKE,DEATH,ALL,DEATH_BOTTOM,NONE,NONE,DEATH,DEATH,DEATH_BOTTOM,DEATH_TOP,DEATH_BOTTOM,DEATH_TOP,FLOOR_CEIL,FLOOR_CEIL,RIGHT,LEFT,RIGHT,LEFT,NONE,NO_SIDE'.split(',')

def coll(gid):
    if gid == 0: return '.'
    return COLLTABLE[(gid-1) & 0xFF] if (gid-1) & 0xFF < len(COLLTABLE) else '?'

def is_solid(c):
    return c in ('ALL', 'FLOOR_CEIL', 'TOP', 'BOTTOM', 'TOP_CENTER_SPIKE', 'LEFT', 'RIGHT', 'NO_SIDE')

GR = 3
MAP_H = 27

# Show vertical profile at gap cols
print('=== Full vertical profile at gap cols 794-810 ===')
for col in range(794, 812):
    profile = []
    for er in range(6, 23):
        tr = er + GR
        if tr >= MAP_H: break
        gid = gids[tr * w + col]
        c = coll(gid)
        if is_solid(c):
            profile.append(f'{er}=#')
        elif 'DEATH' in c:
            profile.append(f'{er}=D')
    blocked = False
    for er in range(10, 17):
        tr = er + GR
        gid = gids[tr * w + col]
        c = coll(gid)
        if is_solid(c):
            blocked = True
            break
    status = 'BLOCKED' if blocked else 'CLEAR PATH ER10-ER17'
    print(f'  Col {col}: solids=[{" ".join(profile)}]  path: {status}')

# Simulate FindCorridorCenter at col 796 with ship at Y=272
def sim_find_corridor(ship_col, ship_y, look_ahead=15):
    topTile = ship_y // 16
    botTile = (ship_y + 14) // 16
    centerX = ship_col * 16 + 7
    startTX = centerX // 16
    endTX = min(startTX + look_ahead, w - 1)
    worldBottom = (MAP_H - GR) * 16

    ceilingY = 0
    floorY = worldBottom
    updates = []

    for tx in range(startTX, endTX + 1):
        # Scan upward
        for ty in range(topTile - 1, -1, -1):
            tr = ty + GR
            if tr < 0 or tr >= MAP_H: continue
            gid = gids[tr * w + tx]
            c = coll(gid)
            if c != '.' and c != 'NONE' and c != 'NONE_PAL':
                if is_solid(c):
                    thisCeiling = ty * 16 + 16
                    if thisCeiling > ceilingY:
                        updates.append(f'  Ceil: tx={tx} ER{ty} solid({c}) -> {thisCeiling}')
                        ceilingY = thisCeiling
                    break
                elif 'DEATH' in c:
                    thisCeiling = ty * 16 + 16
                    if thisCeiling > ceilingY:
                        updates.append(f'  Ceil: tx={tx} ER{ty} death({c}) -> {thisCeiling}')
                        ceilingY = thisCeiling
                    break

        # Scan downward
        for ty in range(botTile + 1, MAP_H - GR):
            tr = ty + GR
            if tr >= MAP_H: break
            gid = gids[tr * w + tx]
            c = coll(gid)
            if c != '.' and c != 'NONE' and c != 'NONE_PAL':
                if is_solid(c):
                    thisFloor = ty * 16
                    if thisFloor < floorY:
                        updates.append(f'  Floor: tx={tx} ER{ty} solid({c}) -> {thisFloor}')
                        floorY = thisFloor
                    break
                elif 'DEATH' in c:
                    thisFloor = ty * 16
                    if thisFloor < floorY:
                        updates.append(f'  Floor: tx={tx} ER{ty} death({c}) -> {thisFloor}')
                        floorY = thisFloor
                    break

    return ceilingY, floorY, updates

# Full 15-tile look-ahead at col 796
print('\n=== FindCorridorCenter at col 796, Y=272, look-ahead=15 ===')
ceilY, floorY, up = sim_find_corridor(796, 272, 15)
for u in up: print(u)
center = (ceilY + floorY) // 2
print(f'  ceilingY={ceilY} floorY={floorY} center={center}')
print(f'  Ship at Y=272, posError=272-{center}={272-center}  -> push {"UP" if 272>center else "DOWN"}')

# Short look-ahead (2 tiles) at col 796
print('\n=== FindCorridorCenter at col 796, Y=272, look-ahead=2 ===')
ceilY, floorY, up = sim_find_corridor(796, 272, 2)
for u in up: print(u)
center = (ceilY + floorY) // 2
print(f'  ceilingY={ceilY} floorY={floorY} center={center}')
print(f'  Ship at Y=272, posError=272-{center}={272-center}  -> push {"UP" if 272>center else "DOWN"}')

# What if ship were at Y=200 (already in upper corridor)?
print('\n=== FindCorridorCenter at col 810, Y=200, look-ahead=15 ===')
ceilY, floorY, up = sim_find_corridor(810, 200, 15)
for u in up: print(u)
center = (ceilY + floorY) // 2
print(f'  ceilingY={ceilY} floorY={floorY} center={center}')

# Check what the corridor looks like from the upper corridor
print('\n=== FindCorridorCenter at col 830, Y=190, look-ahead=15 ===')
ceilY, floorY, up = sim_find_corridor(830, 190, 15)
for u in up: print(u)
center = (ceilY + floorY) // 2
print(f'  ceilingY={ceilY} floorY={floorY} center={center}')
print(f'  With coinY=183, posError=190-{center}={190-center}')
