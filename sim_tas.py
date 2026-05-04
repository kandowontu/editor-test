"""
Replay the TAS input sequence through NES Famidash physics and track trajectory.
Reports X/Y/VelY at every frame, and highlights the c787 area (X=12576-12640).
"""
import xml.etree.ElementTree as ET

# --- Physics constants (from SharedPhysics.cs) ---
VEL_X       = 0x02C4   # horizontal speed, 8.8 fixed
JUMP_VEL    = -0x590   # initial jump VelY, 8.8 fixed
GRAVITY     = 0x6B     # VelY increment per frame, 8.8 fixed
MAX_FALL    = 0x700    # max downward VelY, 8.8 fixed
HITBOX_H    = 15
HITBOX_W    = 15
GRR         = 3        # groundRowsToReserve

# --- Load TMX tiles ---
tree = ET.parse('famidash/LEVELS/LEVEL DATA/lvlset_HUGE/everyend.tmx')
root = tree.getroot()
MAP_W = int(root.get('width'))
MAP_H = int(root.get('height'))
layer = root.findall('.//layer')[0]
raw = layer.find('data').text.strip().split(',')
TILES = [int(x.strip()) for x in raw]

# --- Load collision table (same logic as MetatileCollision.cs RemoveEmptyEntries) ---
cs = open('native-windows/MetatileCollision.cs', encoding='utf-8').read()
start = cs.index('string mappingText = @"') + len('string mappingText = @"')
end = cs.index('";', start)
mapping = cs[start:end]
coll_table = [l.strip().split(';')[0].strip()
              for l in mapping.split('\n')
              if l.strip() and not l.strip().startswith('//')]

def get_coll(gid):
    if gid == 0: return 'COL_NONE'
    iid = gid - 1
    if iid < 0 or iid >= len(coll_table): return 'COL_NONE'
    return coll_table[iid]

def tile_at(col, row):
    """Get collision type at (tile col, tile row) in world tile coordinates."""
    arr_row = row + GRR
    if col < 0 or col >= MAP_W or arr_row < 0 or arr_row >= MAP_H:
        return 'COL_NONE'
    return get_coll(TILES[arr_row * MAP_W + col])

def provides_floor(coll, local_x):
    if coll == 'COL_ALL': return True
    if coll == 'COL_TOP': return True  # top-solid
    if coll == 'COL_UP_RIGHT' and local_x >= 8: return True
    return False

def tile_kills_at(coll, lx, ly):
    if coll == 'COL_DEATH':        return ly >= 4 and ly <= 11 and lx >= 4 and lx <= 8
    if coll == 'COL_DEATH_BOTTOM': return ly > 10 and lx >= 3 and lx <= 9
    if coll == 'COL_DEATH_TOP':    return ly < 6 and lx >= 3 and lx <= 9
    if coll == 'COL_DOWN_LEFT_SPIKE':  return ly >= 8 and lx >= 2 and lx <= 5
    if coll == 'COL_DOWN_RIGHT_SPIKE': return ly >= 8 and lx >= 10 and lx <= 12
    if coll == 'COL_UP_LEFT_SPIKE':    return ly < 8 and lx >= 2 and lx <= 5
    if coll == 'COL_UP_RIGHT_SPIKE':   return ly < 8 and lx >= 10 and lx <= 12
    return False

def tile_occupies(coll):
    return coll in ('COL_ALL', 'COL_LEFT', 'COL_RIGHT', 'COL_UP_RIGHT')

def is_on_ground(x_px, y_px):
    """Check if player foot has a floor tile beneath."""
    foot = y_px + HITBOX_H  # foot pixel Y
    foot_row = foot >> 4     # tile row at foot
    for probe_x in (x_px + 1, x_px + 7, x_px + HITBOX_W):
        col = probe_x >> 4
        local_x = probe_x & 0xF
        coll = tile_at(col, foot_row)
        if provides_floor(coll, local_x):
            return True, foot_row * 16 - HITBOX_H  # snap Y
    return False, y_px

def check_death(x_px, y_px):
    """Check center-point death and forward collision death."""
    # Center point
    cx = x_px + 7
    cy = y_px + 7
    cx_col = cx >> 4; cy_row = cy >> 4
    lx = cx & 0xF; ly = cy & 0xF
    coll = tile_at(cx_col, cy_row)
    if tile_kills_at(coll, lx, ly): return 'CENTER'
    if tile_occupies(coll): return 'CENTER_SOLID'
    # Forward (right edge, mid height)
    rx = x_px + HITBOX_W
    ry = y_px + 7
    rx_col = rx >> 4; ry_row = ry >> 4
    lx2 = rx & 0xF; ly2 = ry & 0xF
    coll2 = tile_at(rx_col, ry_row)
    if tile_kills_at(coll2, lx2, ly2): return 'FWD_KILL'
    if tile_occupies(coll2): return 'FWD_SOLID'
    return None

# --- Load TAS inputs ---
tas_lines = open('mmo_extract/Input.txt').read().splitlines()
IDLE = '|..|........|........'
# jump = button held for that frame
jump_pressed = [l != IDLE for l in tas_lines]

# --- Starting state ---
# TAS starts at game frame 60 (1 second in).
# Level start: X=0, Y=849 at game frame 0.
# At frame 60, X_fixed = 60 * VEL_X
TAS_OFFSET = 60  # TAS frame 0 = game frame 60

x_fixed = TAS_OFFSET * VEL_X
y_fixed = 849 << 8  # Y in 8.8 fixed
vel_y   = 0
on_ground = True  # assume starting on ground

print(f"Starting state: X={x_fixed>>8} Y={y_fixed>>8} (TAS frame 0 = game frame {TAS_OFFSET})")
print(f"TAS frames: {len(tas_lines)}")
print()
print(f"{'Frame':>6} {'GameF':>6} {'X':>6} {'Y':>5} {'VelY':>6} {'Jump':>4} {'Gnd':>3} {'Event'}")
print("-" * 70)

REPORT_X_LO = 12300
REPORT_X_HI = 12800

dead = False
for tas_frame, jump in enumerate(jump_pressed):
    game_frame = tas_frame + TAS_OFFSET
    x_px = x_fixed >> 8
    y_px = y_fixed >> 8

    # Report near c787 area
    report = (x_px >= REPORT_X_LO and x_px <= REPORT_X_HI)

    # --- Physics step ---
    # 1. Jump trigger: only when on ground and jump newly pressed
    prev_jump = jump_pressed[tas_frame - 1] if tas_frame > 0 else False
    jump_start = jump and not prev_jump

    event = ''
    if on_ground and jump:
        vel_y = JUMP_VEL
        on_ground = False
        event = 'JUMP'

    # 2. Gravity
    vel_y += GRAVITY
    if vel_y > MAX_FALL:
        vel_y = MAX_FALL

    # 3. Move Y
    y_fixed += vel_y

    # 4. Move X
    x_fixed += VEL_X

    x_px_new = x_fixed >> 8
    y_px_new = y_fixed >> 8

    # 5. Check death
    death = check_death(x_px_new, y_px_new)
    if death:
        event = f'DEAD:{death}'
        dead = True

    # 6. Floor check
    if not on_ground:
        grnd, snap_y = is_on_ground(x_px_new, y_px_new)
        if grnd and vel_y >= 0:
            y_fixed = snap_y << 8
            y_px_new = snap_y
            vel_y = 0
            on_ground = True
            if not event: event = 'LAND'

    # 7. World bottom
    world_bot = (MAP_H - GRR) * 16 - HITBOX_H
    if y_px_new > world_bot:
        event = 'DEAD:DBOT'
        dead = True

    if report or dead or event:
        print(f"{tas_frame:>6} {game_frame:>6} {x_px_new:>6} {y_px_new:>5} 0x{vel_y & 0xFFFF:04X} {'Y' if jump else ' ':>4} {'G' if on_ground else ' ':>3}  {event}")

    if dead:
        print(f"\n*** DEAD at game_frame={game_frame} X={x_px_new} Y={y_px_new} ***")
        break

if not dead:
    print(f"\nCompleted all {len(tas_lines)} TAS frames. Final X={x_fixed>>8} Y={y_fixed>>8}")
