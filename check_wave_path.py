"""
Check if a wave path exists through the kratos corridor maze.
Wave physics: advance 5.7px in X each frame, ±5.7px in Y based on input.
Forward collision kills if the tile at (X+15, Y+7)/16 is solid/death.
"""
import xml.etree.ElementTree as ET
from collections import defaultdict

# Load TMX
tree = ET.parse(r'c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\kratos.tmx')
root = tree.getroot()
layers = root.findall('layer')
layer = layers[0]
data = layer.find('data').text.strip()
rows_data = [r.strip().rstrip(',') for r in data.split('\n') if r.strip()]

MAP_W = int(root.get('width'))  # 843
MAP_H = int(root.get('height'))  # 27
TILE = 16
GROUND_RESERVE = 3

# Build tile array
tiles = []
for r in range(MAP_H):
    row_tiles = [int(x) for x in rows_data[r].split(',')]
    tiles.append(row_tiles)

# Collision table (simplified: just need to know if blocking/death/passthrough)
# From MetatileCollision.cs analysis
col_names_raw = [
    "NONE","FC","FC","BOT","DtT","FC","FC","NONE","DtB","DtB","DtT","DtT","DtB","DtT","DtL","DtR",
    "ALL","DTH","DtB","DtB","DtT","TCS","ALL","DtT","DtB","TOP","DTH","DTH","DTH","DtL","DtT","DtR",
    "ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","NONE",
    "ALL","ALL","ALL","ALL","NONE","NONE","NONE","TCS","ALL","ALL","ALL","DtB","ALL","ALL","ALL","ALL",
    "ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","NONE",
    "ALL","ALL","ALL","ALL","TOP","TOP","TOP","TOP","BOT","BOT","BOT","DTH","DtB","DtT","DtR","DtL",
    "ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","ALL","NONE",
    "ALL","ALL","ALL","ALL","DRS","DtB","DLS","DtR","NONE","DtL","URS","DtT","ULS","DTH","ALL","DtB",
    "NONE","NONE","DTH","DTH","DtB","DtT","DtB","DtT","FC","FC","RT","LT","RT","LT","NONE","NSD",
    "SRD","SLD","SRU","SLU","s","s","s","s","s","s","s","s","s","s","s","s",
    "s","s","s","s","NSD","NSD","NSD","NSD","LSB","RSB","BLS","BRS","BSP","s","s","s",
]

def is_blocking(tmx_tile):
    """Check if a tile blocks forward collision (returns True for solid/death/spike)"""
    if tmx_tile == 0:
        return False
    mt = tmx_tile - 1
    if mt >= len(col_names_raw):
        return False
    cn = col_names_raw[mt]
    # FC and NONE/NSD don't block forward collision
    if cn in ("NONE", "FC", "NSD"):
        return False
    # TOP, BOT, RT, LT only block from specific directions — for forward collision,
    # they're treated as blocking if the center point hits them
    # Slopes are complex — for simplicity, treat slopes as passthrough (wave rides them)
    if cn.startswith('s') or cn.startswith('S'):
        return False  # slopes
    if cn in ("TOP", "BOT", "RT", "LT"):
        return True  # directional solids
    # ALL, death tiles, spikes — all blocking
    return True

def get_tile_blocking(col, row):
    """Check if tile at (col, row) blocks forward collision"""
    if row < 0 or row >= MAP_H or col < 0 or col >= MAP_W:
        return True  # out of bounds = blocking
    return is_blocking(tiles[row][col])

def check_fwd_collision(x_px, y_px):
    """Check if forward collision would kill wave at position (x_px, y_px)
    
    Wave hitbox: W=15, H=15
    Forward collision checks tile at (X+15, Y+7)
    """
    right_edge = x_px + 15
    center_y = y_px + 7
    tile_col = right_edge // TILE
    tile_row = center_y // TILE
    return get_tile_blocking(tile_col, tile_row)

def check_eject_death(x_px, y_px):
    """Simplified eject check: if top or bottom of hitbox is in a blocking tile"""
    # Top of hitbox
    top_col = (x_px + 7) // TILE  # center X
    top_row = y_px // TILE
    # Bottom of hitbox  
    bot_row = (y_px + 14) // TILE
    
    # Check if inside a blocking tile
    if get_tile_blocking(top_col, top_row):
        return True
    if get_tile_blocking(top_col, bot_row):
        return True
    return False

# Wave BFS through the maze
# State: (X_px, Y_px) quantized to integer pixels
# Wave speed: ~5.7 px/frame ≈ 6 px for simplicity (or use exact fixed-point)
# Actually, let's use the exact speed: VelX = 0x0597 in fixed-point >> 8 = 5.59 px/f

WAVE_SPEED_PX = 6  # approximate (rounding up to be conservative)
# Actually, use 5 and 6 alternating... or just try both common values

# Start: X=7265 (wave portal), Y=192..280 (below FC ceiling)
# Goal: reach X=9064, Y=239..255 (coin hitbox)

START_X = 7265
GOAL_X_MIN = 9056  # coin hitbox left
GOAL_X_MAX = 9072  # coin hitbox right
GOAL_Y_MIN = 239  # coin hitbox top  
GOAL_Y_MAX = 255  # coin hitbox bottom

# BFS with pixel-level states, quantized to 2px resolution
QUANT = 2

print(f"Starting wave path BFS from X={START_X}, Y=192..310")
print(f"Goal: reach coin at X={GOAL_X_MIN}-{GOAL_X_MAX}, Y={GOAL_Y_MIN}-{GOAL_Y_MAX}")

# Try multiple wave speeds
for VSPEED in [5, 6]:
    print(f"\n=== Wave speed = {VSPEED} px/frame ===")
    
    # Start with states at various Y below the FC ceiling
    frontier = set()
    for y in range(192, 340, QUANT):
        if not check_fwd_collision(START_X, y) and not check_eject_death(START_X, y):
            frontier.add((START_X // QUANT, y // QUANT))
    
    print(f"Initial states: {len(frontier)}")
    
    reached_coin = False
    max_x = START_X
    
    for frame in range(400):  # ~400 frames to cover 2000px at 5px/f
        next_frontier = set()
        
        for (qx, qy) in frontier:
            x = qx * QUANT
            y = qy * QUANT
            
            # Wave advances: X += VSPEED, Y += ±VSPEED
            new_x = x + VSPEED
            
            for dy in [-VSPEED, VSPEED]:
                new_y = y + dy
                
                # Check bounds
                if new_y < 0 or new_y >= MAP_H * TILE:
                    continue
                
                # Check forward collision at new position
                if check_fwd_collision(new_x, new_y):
                    continue
                
                # Check eject (simplified: check if hitbox overlaps blocking tiles)
                if check_eject_death(new_x, new_y):
                    continue
                
                # Check if we reached the coin
                if new_x >= GOAL_X_MIN and new_x <= GOAL_X_MAX:
                    # Check if wave hitbox overlaps coin hitbox
                    wave_top = new_y
                    wave_bot = new_y + 14
                    if wave_bot >= GOAL_Y_MIN and wave_top <= GOAL_Y_MAX:
                        print(f"  COIN REACHED at frame {frame}! X={new_x} Y={new_y}")
                        reached_coin = True
                
                if new_x > max_x:
                    max_x = new_x
                
                next_frontier.add((new_x // QUANT, new_y // QUANT))
        
        frontier = next_frontier
        
        if frame % 20 == 0 or len(frontier) == 0:
            # Count Y distribution
            below_fc = sum(1 for (_, qy) in frontier if qy * QUANT > 192)
            if frontier:
                ys = [qy * QUANT for (_, qy) in frontier]
                print(f"  f={frame}: states={len(frontier)}, maxX={max_x}, Y=[{min(ys)}..{max(ys)}], belowFC={below_fc}")
            else:
                print(f"  f={frame}: states=0, maxX={max_x}")
        
        if len(frontier) == 0:
            print(f"  All states dead at frame {frame}!")
            break
        
        if reached_coin:
            print(f"  Path to coin EXISTS!")
            break
    
    if not reached_coin:
        print(f"  No path found after {frame+1} frames. Max X reached: {max_x}")
