import re

with open(r'c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\timemachine.tmx', 'r') as f:
    content = f.read()

data_blocks = re.findall(r'<data encoding="csv">\s*([\s\S]*?)\s*</data>', content)

W = 997
H = 27
GROUND_ROWS = 3  # groundRowsToReserve from pf-test/Program.cs

tvals = [int(v.strip()) for v in data_blocks[0].replace('\n', ',').split(',') if v.strip()]
svals = [int(v.strip()) for v in data_blocks[1].replace('\n', ',').split(',') if v.strip()]

# Full collision table from MetatileCollision.cs (256 entries)
collision_text = """COL_NONE
COL_FLOOR_CEIL
COL_FLOOR_CEIL
COL_BOTTOM
COL_DEATH_TOP
COL_FLOOR_CEIL
COL_FLOOR_CEIL
COL_NONE
COL_DEATH_BOTTOM
COL_DEATH_BOTTOM
COL_DEATH_TOP
COL_DEATH_TOP
COL_DEATH_BOTTOM
COL_DEATH_TOP
COL_DEATH_LEFT
COL_DEATH_RIGHT
COL_ALL
COL_DEATH
COL_DEATH_BOTTOM
COL_DEATH_BOTTOM
COL_DEATH_TOP
COL_TOP_CENTER_SPIKE
COL_ALL
COL_DEATH_TOP
COL_DEATH_BOTTOM
COL_TOP
COL_DEATH
COL_DEATH
COL_DEATH
COL_DEATH_LEFT
COL_DEATH_TOP
COL_DEATH_RIGHT
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_NONE
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_NONE
COL_NONE
COL_NONE
COL_TOP_CENTER_SPIKE"""

col_lines = [l.strip() for l in collision_text.strip().split('\n') if l.strip().startswith('COL_')]
col_table = {}
for i, c in enumerate(col_lines):
    col_table[i] = c

# Which collision types are SOLID (block movement)?
SOLID_TYPES = {
    'COL_ALL', 'COL_FLOOR_CEIL', 'COL_TOP', 'COL_BOTTOM',
    'COL_LEFT', 'COL_RIGHT', 'COL_UP_LEFT', 'COL_UP_RIGHT',
    'COL_DOWN_LEFT', 'COL_DOWN_RIGHT', 'COL_NO_SIDE',
    'COL_TOP_CENTER_SPIKE', 'COL_BOTTOM_CENTER_SPIKE',
    'COL_LEFT_SPIKE_BLOCK', 'COL_RIGHT_SPIKE_BLOCK',
    'COL_TOP_LEFT_STAIRS', 'COL_TOP_RIGHT_STAIRS',
    'COL_BOTTOM_LEFT_STAIRS', 'COL_BOTTOM_RIGHT_STAIRS',
    'COL_TOP_LEFT_BOTTOM_RIGHT', 'COL_TOP_RIGHT_BOTTOM_LEFT',
    'COL_BOTTOM_LEFT_SPIKE', 'COL_BOTTOM_RIGHT_SPIKE',
    'COL_BOTTOM_SPIKES',
}
DEATH_TYPES = {
    'COL_DEATH', 'COL_DEATH_TOP', 'COL_DEATH_BOTTOM', 'COL_DEATH_LEFT', 'COL_DEATH_RIGHT',
    'COL_DEATH_TOP_RIGHT', 'COL_DEATH_TOP_LEFT', 'COL_DEATH_BOTTOM_RIGHT', 'COL_DEATH_BOTTOM_LEFT',
    'COL_DEATH_TOP_RIGHT_LEFT', 'COL_DEATH_TOP_BOTTOM', 'COL_DEATH_LEFT_RIGHT', 'COL_DEATH_TOP_LEFT_BOTTOM',
}

def tile_collision(tid):
    """Get collision type for a tile ID"""
    if tid < 0:
        return 'COL_NONE'
    return col_table.get(tid, 'COL_NONE')

def is_solid(tid):
    c = tile_collision(tid)
    return c in SOLID_TYPES

def is_death(tid):
    c = tile_collision(tid)
    return c in DEATH_TYPES

print("=" * 80)
print("TIME MACHINE COIN ANALYSIS")
print("Coin sprite 0x1A at TMX (col=368, row=22)")
print("Ship hitbox: 15x15 pixels")
print(f"Ground offset: {GROUND_ROWS} rows")
print("=" * 80)

# Column 368 profile with game coords
print("\n=== COLUMN 368 PROFILE (coin column) ===")
print(f"{'TMX Row':>8} {'Game Row':>9} {'Game Y Range':>15} {'Tile':>5} {'Collision':>25} {'Block?':>7} {'Death?':>7} {'Sprite':>12}")
for tmx_r in range(15, 27):
    game_r = tmx_r - GROUND_ROWS
    game_y_top = game_r * 16
    game_y_bot = game_y_top + 16
    
    idx = tmx_r * W + 368
    tv = tvals[idx]
    tid = tv - 1 if tv > 0 else -1
    
    sv = svals[idx]
    sid_str = ""
    if sv >= 257:
        sid = sv - 257
        names = {0x07: "COIN", 0x1A: "COIN", 0x1B: "COIN", 0x0B: "Y_ORB", 0x2F: "DECO", 0x2E: "DECO", 0x0A: "Y_PAD"}
        sid_str = f"0x{sid:02X} {names.get(sid, '')}"
    
    col_type = tile_collision(tid)
    solid = "SOLID" if is_solid(tid) else ""
    death = "DEATH" if is_death(tid) else ""
    
    marker = ""
    if game_r == 16: marker = " <-- SHIP Y=267"
    if tmx_r == 22: marker = " <-- COIN Y=303-319"
    
    print(f"  {tmx_r:>5}   {game_r:>6}   {game_y_top:>4}-{game_y_bot:<4}  {tid:>4}  {col_type:>24}  {solid:>6}  {death:>6}  {sid_str:>10}{marker}")

# Horizontal cross-section at coin row (TMX row 22, game row 19)
print("\n=== HORIZONTAL CROSS-SECTION at COIN ROW (TMX row 22, game Y=304-320) ===")
print(f"{'Col':>5} {'GameX':>6} {'Tile':>5} {'Collision':>25} {'Solid?':>7} {'Death?':>7} {'Sprite':>10}")
for c in range(362, 375):
    idx = 22 * W + c
    tv = tvals[idx]
    tid = tv - 1 if tv > 0 else -1
    col_type = tile_collision(tid)
    
    sv = svals[idx]
    sid_str = ""
    if sv >= 257:
        sid = sv - 257
        sid_str = f"0x{sid:02X}"
        if sid == 0x1A: sid_str += " COIN!"
    
    solid = "SOLID" if is_solid(tid) else ""
    death = "DEATH" if is_death(tid) else ""
    
    print(f"  {c:>3}  {c*16:>5}  {tid:>4}  {col_type:>24}  {solid:>6}  {death:>6}  {sid_str:>10}")

# The box structure with collision analysis
print("\n=== BOX STRUCTURE COLLISION (cols 365-371, TMX rows 19-25) ===")
print(f"{'':>20}", end="")
for c in range(365, 372):
    print(f"  c{c}", end="")
print()
for tmx_r in range(19, 26):
    game_r = tmx_r - GROUND_ROWS
    game_y = game_r * 16
    print(f"TMX{tmx_r:>2} gY={game_y:>3}:{' ':>3}", end="")
    for c in range(365, 372):
        idx = tmx_r * W + c
        tv = tvals[idx]
        tid = tv - 1 if tv > 0 else -1
        if tid < 0:
            print("  ...", end="")
        elif is_solid(tid):
            print(f" S{tid:02d}", end="")
        elif is_death(tid):
            print(f" D{tid:02d}", end="")
        else:
            print(f" N{tid:02d}", end="")
        
    # Sprites
    for c in range(365, 372):
        idx = tmx_r * W + c
        sv = svals[idx]
        if sv >= 257:
            sid = sv - 257
            print(f"  [spr 0x{sid:02X}]", end="")
    print()
print("  S=SOLID, D=DEATH(no solid), N=NONE(passable)")

# Path analysis
print("\n" + "=" * 80)
print("PATH ANALYSIS: Ship at Y=267 to Coin at Y=303")
print("=" * 80)

print("""
COORDINATE MAPPING (groundRowsToReserve = 3):
  TMX row = game_tileY + 3
  game_Y  = (TMX_row - 3) * 16
  
Ship position: game Y=267 → game tile row 16 → TMX row 19 (EMPTY)
Coin position: game Y=303-319 → game tile row 18-19 → TMX row 21-22

VERTICAL OBSTACLE AT COL 368 (direct descent):
  TMX row 20 (game Y=272-288): tile 18 = COL_DEATH_BOTTOM
    → NO solid collision (death-only), ship can pass through
    → BUT has death zone at bottom pixels: localY>10, localX in [5,7]
  TMX row 21 (game Y=288-304): tile 33 = COL_ALL 
    → FULLY SOLID — BLOCKS DESCENT ★★★
  TMX row 22 (game Y=304-320): tile 47 = COL_NONE
    → No collision, COIN HERE

RESULT: The ship CANNOT descend straight down at col 368.
        Tile 33 (COL_ALL) at game Y=288-304 forms a SOLID WALL.

SIDE ENTRY ANALYSIS (through the box opening):
  At TMX row 22 (game Y=304-320), the box interior is COL_NONE:
    col 365: EMPTY (open approach)
    col 366: EMPTY (open approach)
    col 367: tile 47 = COL_NONE (passable!)
    col 368: tile 47 = COL_NONE + COIN
    col 369: tile 47 = COL_NONE (passable!)
    col 370: EMPTY (open exit)
  
  The vertical gap is between:
    TMX row 21 (game Y=288-304): COL_ALL at cols 367-369 (SOLID ceiling)
    TMX row 23 (game Y=320-336): COL_ALL at cols 367-369 (SOLID floor)
  Gap height = 16 pixels (Y=304 to Y=320)
  Ship hitbox = 15 pixels tall
  → Ship CAN fit, but with only 1 pixel clearance!

  Side walls:
    col 366, TMX row 21: tile 28 = COL_DEATH_LEFT (death-only, NOT solid)
    col 366, TMX row 23: tile 28 = COL_DEATH_LEFT (death-only, NOT solid)
    col 370, TMX row 21: tile 26 = COL_DEATH (death-only, NOT solid)
    col 370, TMX row 23: tile 26 = COL_DEATH (death-only, NOT solid)
  → Side walls have DEATH ZONES but NO solid collision

APPROACH PATH:
  The ship must:
  1. Be at Y=267 (TMX row 19) flying rightward
  2. Begin descending BEFORE reaching col 367 (where box walls start)
  3. Drop from Y=267 to Y=304+ (a drop of 37 pixels)
  4. Navigate through the 16px-tall horizontal gap at Y=304-320
  5. Reach col 368 at exactly the right Y position
  
  At col 365 (just left of box), ALL rows 19-25 are EMPTY — 
  so the ship has clear space to descend there.
  
WHY THE SHIP CAN'T REACH THE COIN:
  The 1-tile-tall (16px) horizontal gap requires the ship (15px tall)
  to be positioned at Y=304-305 exactly. The ship must:
  - Drop 37 pixels from Y=267 to Y=304 
  - Level off at EXACTLY Y=304-305 (1px tolerance)
  - Fly horizontally through 2+ tiles of 16px-tall corridor
  - Avoid death zones at the entry (tile 28 COL_DEATH_LEFT at sides)
  
  This is essentially a 1-pixel-tolerance maneuver through a death-lined
  corridor — extremely difficult or impossible for the pathfinder's 
  corridor-following algorithm.
""")

# Also show sprites in the approach zone
print("=== INTERACTIVE SPRITES IN APPROACH ZONE (cols 330-375) ===")
for r in range(H):
    for c in range(330, 376):
        idx = r * W + c
        sv = svals[idx]
        if sv > 0:
            sid = sv - 257 if sv >= 257 else sv
            # Only show interactive sprites
            h_table = [
                0x34,0x34,0x34,0x34,0x34,0x12,0x12,0x10,
                0x28,0x28,0x03,0x12,0x03,0x03,0x03,0x10,
                0x0e,0x0e,0x0e,0x0e,0x24,0x24,0x24,0x34,
                0x34,0x34,0x10,0x10,0x10,0x10,0x10,0x12,
            ]
            names = {
                0x00: "CUBE_PORTAL", 0x01: "SHIP_PORTAL", 0x02: "BALL_PORTAL", 0x03: "UFO_PORTAL",
                0x04: "WAVE_PORTAL", 0x05: "PAD_Y_UP2", 0x06: "PAD_Y_UP3",
                0x07: "COIN", 0x08: "GRAV_NORMAL", 0x09: "GRAV_FLIP",
                0x0A: "PAD_YELLOW", 0x0B: "ORB_YELLOW", 0x0C: "PAD_YELLOW2",
                0x0D: "PAD_BLUE", 0x0E: "PAD_BLUE2", 0x0F: "END_LEVEL",
                0x10: "GRAV_NOR_2T", 0x11: "GRAV_FLIP_2T", 0x12: "GRAV_NOR_3T", 0x13: "GRAV_FLIP_3T",
                0x14: "SPEED_05X", 0x15: "SPEED_1X", 0x16: "SPEED_2X",
                0x17: "ROBOT_PORTAL", 0x18: "MINI_PORTAL", 0x19: "GROW_PORTAL",
                0x1A: "COIN_2", 0x1B: "COIN_3", 0x1F: "PAD?",
                0x20: "SPEED_3X", 0x21: "SPEED_4X", 0x24: "SPIDER_PORTAL",
                0x25: "PAD_PINK", 0x26: "PAD_PINK2",
                0x2D: "DECO", 0x2E: "DECO", 0x2F: "DECO",
                0x3D: "DECO",
                0x4B: "SWING_PORTAL",
                0x52: "PAD_RED", 0x53: "PAD_RED2",
                0x58: "DASH_PORTAL",
                0x65: "PAD_GREEN",
                0x6D: "SPEED_SLOW",
                0x6E: "MINI_COIN",
            }
            name = names.get(sid, f"UNK")
            # Skip deco sprites
            if name == "DECO":
                continue
            game_r = r - GROUND_ROWS
            game_y = game_r * 16
            print(f"  col={c} TMX_row={r} game_Y={game_y} sid=0x{sid:02X} = {name}")
