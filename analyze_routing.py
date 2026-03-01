#!/usr/bin/env python3
"""Analyze terrain around coins 2 and 3 for routing strategy."""
import csv, sys

TMX = r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\polargeist.tmx"

def parse_layers():
    with open(TMX, encoding='utf-8') as f:
        txt = f.read()
    layers = []
    pos = 0
    while True:
        idx = txt.find('<data encoding="csv">', pos)
        if idx < 0: break
        d1 = idx + len('<data encoding="csv">')
        d2 = txt.index('</data>', d1)
        csv_text = txt[d1:d2].strip()
        rows = []
        for line in csv_text.split('\n'):
            line = line.strip().rstrip(',')
            if not line: continue
            rows.append([int(x) for x in line.split(',')])
        layers.append(rows)
        pos = d2
    return layers

all_layers = parse_layers()
tiles = all_layers[0]  # first layer = tiles
sp = all_layers[1]     # second layer = SP
MAP_W = len(tiles[0])
MAP_H = len(tiles)
GRR = 3  # groundRowsToReserve

def game_y(tmx_row):
    return (tmx_row - GRR) * 16

def game_x(tmx_col):
    return tmx_col * 16

print(f"Map: {MAP_W}x{MAP_H}")
print()

# === COIN 2: TMX(25,592) ===
print("="*80)
print("COIN 2 AREA — TMX cols 550-610, rows 16-26")
print("="*80)

# Print tile layer
print("\nTILE LAYER (0=empty, tile IDs shown):")
print(f"{'Row':>3} {'gY':>4} |", end="")
for c in range(550, 611):
    if c % 5 == 0:
        print(f"{c:>4}", end="")
    else:
        print(f"{'':>4}", end="")
print()
for r in range(16, 27):
    gy = game_y(r)
    print(f"{r:>3} {gy:>4} |", end="")
    for c in range(550, 611):
        t = tiles[r][c]
        if t == 0:
            print("   .", end="")
        else:
            print(f"{t:>4}", end="")
    print()

# Print SP layer  
print(f"\nSP LAYER (interactive sprites):")
for r in range(16, 27):
    for c in range(550, 611):
        s = sp[r][c]
        if s > 0:
            sid = s - 257
            gy = game_y(r)
            gx = game_x(c)
            # Classify
            cls = "DECO"
            if sid in (0x0A, 0x0C): cls = "YELLOW_PAD"
            elif sid in (0x25, 0x26): cls = "PINK_PAD"
            elif sid in (0x52, 0x53): cls = "RED_PAD"
            elif sid in (0x0D, 0x0E, 0xFD, 0xFE): cls = "BLUE_PAD"
            elif sid == 0x65: cls = "GREEN_PAD"
            elif sid == 0x0B: cls = "YELLOW_ORB"
            elif sid == 0x1F: cls = "YELLOW_ORB_BIG"
            elif sid == 0x29: cls = "YELLOW_ORB_SM"
            elif sid == 0x06: cls = "PINK_ORB"
            elif sid == 0x28: cls = "RED_ORB"
            elif sid in (0x05, 0x7B): cls = "BLUE_ORB"
            elif sid in (0x27, 0x7C): cls = "GREEN_ORB"
            elif sid == 0x44: cls = "BLACK_ORB"
            elif sid == 0x7A: cls = "WHITE_ORB"
            elif sid in (0x07, 0x1A, 0x1B): cls = "COIN"
            elif sid in (0x00, 0x01, 0x02, 0x03, 0x04): cls = "MODE_PORTAL"
            elif sid in (0x08,0x09,0x10,0x11,0x12,0x13,0xFB,0xFC): cls = "GRAV_PORTAL"
            elif sid in (0x18, 0x19): cls = "MINI_PORTAL"
            elif sid == 0x0F: cls = "END_LEVEL"
            print(f"  TMX({r},{c}) gXY=({gx},{gy}) SP={s} sid=0x{sid:02X} → {cls}")

# Show collision types for tiles found
tile_set = set()
for r in range(16, 27):
    for c in range(550, 611):
        t = tiles[r][c]
        if t > 0: tile_set.add(t)
print(f"\nUnique tiles in area: {sorted(tile_set)}")

# === COIN 3: TMX(14,719) ===
print()
print("="*80)
print("COIN 3 AREA — TMX cols 695-735, rows 8-26")
print("="*80)

print("\nTILE LAYER:")
print(f"{'Row':>3} {'gY':>4} |", end="")
for c in range(695, 736):
    if c % 5 == 0:
        print(f"{c:>4}", end="")
    else:
        print(f"{'':>4}", end="")
print()
for r in range(8, 27):
    gy = game_y(r)
    print(f"{r:>3} {gy:>4} |", end="")
    for c in range(695, 736):
        t = tiles[r][c]
        if t == 0:
            print("   .", end="")
        else:
            print(f"{t:>4}", end="")
    print()

print(f"\nSP LAYER (interactive sprites):")
for r in range(8, 27):
    for c in range(695, 736):
        s = sp[r][c]
        if s > 0:
            sid = s - 257
            gy = game_y(r)
            gx = game_x(c)
            cls = "DECO"
            if sid in (0x0A, 0x0C): cls = "YELLOW_PAD"
            elif sid in (0x25, 0x26): cls = "PINK_PAD"
            elif sid in (0x52, 0x53): cls = "RED_PAD"
            elif sid in (0x0D, 0x0E, 0xFD, 0xFE): cls = "BLUE_PAD"
            elif sid == 0x65: cls = "GREEN_PAD"
            elif sid == 0x0B: cls = "YELLOW_ORB"
            elif sid == 0x1F: cls = "YELLOW_ORB_BIG"
            elif sid == 0x29: cls = "YELLOW_ORB_SM"
            elif sid == 0x06: cls = "PINK_ORB"
            elif sid == 0x28: cls = "RED_ORB"
            elif sid in (0x05, 0x7B): cls = "BLUE_ORB"
            elif sid in (0x27, 0x7C): cls = "GREEN_ORB"
            elif sid == 0x44: cls = "BLACK_ORB"
            elif sid == 0x7A: cls = "WHITE_ORB"
            elif sid in (0x07, 0x1A, 0x1B): cls = "COIN"
            elif sid in (0x00, 0x01, 0x02, 0x03, 0x04): cls = "MODE_PORTAL"
            elif sid in (0x08,0x09,0x10,0x11,0x12,0x13,0xFB,0xFC): cls = "GRAV_PORTAL"
            elif sid in (0x18, 0x19): cls = "MINI_PORTAL"
            elif sid == 0x0F: cls = "END_LEVEL"
            print(f"  TMX({r},{c}) gXY=({gx},{gy}) SP={s} sid=0x{sid:02X} → {cls}")

tile_set3 = set()
for r in range(8, 27):
    for c in range(695, 736):
        t = tiles[r][c]
        if t > 0: tile_set3.add(t)
print(f"\nUnique tiles in area: {sorted(tile_set3)}")

# === Tile collision reference ===
print()
print("="*80)
print("TILE COLLISION REFERENCE (from metatile_collision_table.txt if available)")
print("="*80)
try:
    with open(r"c:\Editor Test\metatile_collision_table.txt") as f:
        print(f.read()[:3000])
except FileNotFoundError:
    print("(file not found)")

# === Yellow pad trajectory analysis ===
print()
print("="*80)
print("YELLOW PAD AT TMX(26,592) — TRAJECTORY ANALYSIS")
print("="*80)
# Yellow pad gives VelY = 0x7C0 in cube mode (from PadOrbHeights row 1, col 0)
# That's 1984 in fixed-point (8.8 format) = 1984/256 = 7.75 px/frame
# With gravity = 0x47 = 71/256 = 0.277 px/frame^2
# After pad hit, player goes UP
# Pad is at ground level Y=368 (game). Player on pad at Y=369 (standing on ground).
# After pad launch: initial VelY = -0x7C0 (upward)
pad_y_game = game_y(26)  # 368
print(f"Pad at game Y={pad_y_game}")
print(f"Coin 2 hitbox: Y=351 to Y=367 (game)")
print(f"Pad launch VelY = -0x7C0 = -1984 (upward, fixed 8.8)")
print(f"Gravity = 0x47 = 71 (fixed 8.8)")
print(f"Player starts at Y≈369 on ground")
print()
y_fixed = 369 * 256  # player Y in fixed point
vy = -0x7C0  # upward velocity
gravity = 0x47
print(f"{'Frame':>5} {'Y_fixed':>8} {'Y_px':>6} {'VelY':>7} {'In coin?':>10}")
for frame in range(30):
    y_px = y_fixed // 256
    in_coin = 351 <= y_px <= 367
    print(f"{frame:>5} {y_fixed:>8} {y_px:>6} {vy:>7} {'YES' if in_coin else '':>10}")
    vy += gravity
    y_fixed += vy
    if y_fixed // 256 < 200:  # stop when very high
        break
