import xml.etree.ElementTree as ET
import re

tree = ET.parse('famidash/LEVELS/LEVEL DATA/lvlset_HUGE/deathmoon.tmx')
root = tree.getroot()
width = int(root.get('width'))
height = int(root.get('height'))
TILE = 16

# Read collision table
col_table = {}
with open('native-windows/MetatileCollision.cs', 'r') as f:
    content = f.read()

# Parse the enum
enum_vals = {}
enum_match = re.search(r'public enum MetatileCollision\s*\{([^}]+)\}', content, re.DOTALL)
if enum_match:
    idx = 0
    for line in enum_match.group(1).strip().split('\n'):
        line = line.strip().rstrip(',')
        if line and not line.startswith('//'):
            name = line.split('=')[0].strip()
            if '=' in line:
                idx = int(line.split('=')[1].strip())
            enum_vals[idx] = name
            idx += 1

# Parse the mapping text
mapping_match = re.search(r'string mappingText = @"([^"]+)"', content, re.DOTALL)
if mapping_match:
    lines = [l.strip() for l in mapping_match.group(1).strip().split('\n') if l.strip()]
    for i, name in enumerate(lines):
        for ev, en in enum_vals.items():
            if en == name:
                col_table[i] = (ev, name)
                break
        else:
            col_table[i] = (-1, name)

layers = {}
for layer in root.findall('layer'):
    name = layer.get('name') or 'Terrain'
    data = layer.find('data').text.strip()
    tiles = [int(x) for x in data.split(',')]
    layers[name] = tiles

sp = layers.get('SP', [])
terrain = None
for k, v in layers.items():
    if k != 'SP':
        terrain = v
        break

# Find ALL blue pad sprites near the death zone
print("=== SP LAYER: All sprites near X=17500-19000 ===")
for ty in range(height):
    for tx in range(width):
        idx = ty * width + tx
        if idx < len(sp):
            sid = sp[idx]
            if sid != 0 and tx * TILE >= 17500 and tx * TILE <= 19000:
                base_sid = sid & 0xFF
                print(f"  tx={tx} ty={ty} X={tx*TILE} Y={ty*TILE} sid=0x{sid:X} (base=0x{base_sid:02X})")

# BFS death info
print("\n=== BFS death analysis ===")
print("Cube: mode=0, mini=True, grav=True (inverted)")
print("Y range [776..802], X=18025")
print("hitboxW=8, hitboxH=7, hbOffY=4")
print(f"rightEdge = 18025 + 8 = 18033, rightEdge tileX = {18033//16}")

# Forward collision check: probes at (rightEdge, centerY)
# centerY for mini grav-flipped cube:
#   miniTopOffset = (0x10 - 7) >> 1 = 4
#   centerY = playerY + 4 + 3 = playerY + 7 ... then +3 for gravFlipped
#   Actually: centerY = playerY + miniTopOffset + (hbH >> 1) + (gravFlipped ? 3 : -2)
#   = playerY + 4 + 3 + 3 = playerY + 10
print(f"centerY = playerY + 10")

right_tx = 18033 // 16  # = 1127
print(f"\nTerrain column at tileX={right_tx} (forward collision):")
for ty in range(47, 55):
    idx = ty * width + right_tx
    tid = terrain[idx] if idx < len(terrain) else -1
    col_info = col_table.get(tid, (-1, "UNKNOWN"))
    print(f"  ty={ty} Y={ty*TILE}-{ty*TILE+15}: tile={tid:3d} col={col_info[1]}")

# Show what centerY maps to for each Y in the frontier
print(f"\nForward collision at X=18025 for Y range:")
for y in [776, 780, 784, 788, 792, 796, 800, 802]:
    centerY = y + 10
    ty = centerY // 16
    idx = ty * width + right_tx
    tid = terrain[idx] if idx < len(terrain) else -1
    col_info = col_table.get(tid, (-1, "?"))
    # Check if this tile blocks sideways
    cname = col_info[1]
    blocks = "BLOCKS" if "ALL" in cname or "DEATH" in cname or "RIGHT" in cname else "pass"
    if "FLOOR_CEIL" in cname or "NO_SIDE" in cname or "NONE" in cname or "TOP" in cname or "BOTTOM" in cname:
        blocks = "pass"
    print(f"  Y={y:3d} centerY={centerY} ty={ty} tile={tid:3d} col={cname:25s} -> {blocks}")

# Where is the blue pad? Let's figure out if the cube can reach it
print("\n=== Can cube reach blue pad? ===")
# Speed 3 X advance per frame
speed3 = 0x02EE
px_per_frame = speed3 / 256.0
print(f"Speed 3 = 0x{speed3:X} = {px_per_frame:.2f} px/frame")

# Show terrain for previous few columns to understand approach
print(f"\nTerrain at rightEdge for X positions leading up to death:")
for px in range(17990, 18040, 2):
    re_px = px + 8  # rightEdge
    re_tx = re_px // 16
    # Check at Y=790 (middle of range)
    cy = 800  # centerY
    ty_check = cy // 16
    idx = ty_check * width + re_tx
    tid = terrain[idx] if idx < len(terrain) else -1
    col_info = col_table.get(tid, (-1, "?"))
    cname = col_info[1]
    blocks = "*BLOCK*" if ("ALL" in cname or "DEATH" in cname or "RIGHT" in cname) and "FLOOR_CEIL" not in cname and "NO_SIDE" not in cname and "NONE" not in cname and "TOP" not in cname and "BOTTOM" not in cname else ""
    print(f"  X={px:5d} rightEdge={re_px} tileX={re_tx} tile={tid:3d} col={cname:25s} {blocks}")
