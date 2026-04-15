"""Find all portals (game mode, mini, gravity, etc.) in the coin corridor area"""
import xml.etree.ElementTree as ET
tree = ET.parse(r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\kratos.tmx")
root = tree.getroot()

PORTAL_NAMES = {
    0x01: "cube", 0x02: "ship", 0x03: "ball", 0x04: "ufo",
    0x05: "wave", 0x06: "robot", 0x07: "spider",
    0x08: "speed_slow", 0x09: "gravity_reverse", 0x0A: "gravity_normal",
    0x0B: "speed_normal", 0x0C: "speed_fast", 0x0D: "speed_faster",
    0x0E: "speed_fastest",
    0x11: "size_mini", 0x12: "size_normal",
    0x22: "dual_on", 0x23: "dual_off",
}

for layer in root.findall('.//layer'):
    name = layer.get('name')
    if name != 'SP':
        continue
    width = int(layer.get('width'))
    data = layer.find('data')
    tile_ids = [int(x) for x in data.text.strip().split(',')]
    
    print("=== All portals/sprites from X=7000 to X=10000 ===")
    for row in range(0, 30):
        for col in range(7000//16, 10000//16 + 1):
            idx = row * width + col
            if idx >= len(tile_ids):
                continue
            tid = tile_ids[idx]
            if tid == 0:
                continue
            x = col * 16
            y = row * 16
            sid = tid
            pname = PORTAL_NAMES.get(sid, None)
            if pname:
                print(f"  X={x:5d} Y={y:3d} col={col:4d} row={row:2d} sprite=0x{sid:02X} = {pname}")
    
    # Also show ALL non-zero sprites in the corridor region
    print("\n=== All non-zero sprites X=7000-10000 ===")
    for row in range(0, 30):
        for col in range(7000//16, 10000//16 + 1):
            idx = row * width + col
            if idx >= len(tile_ids):
                continue
            tid = tile_ids[idx]
            if tid == 0:
                continue
            x = col * 16
            y = row * 16
            pname = PORTAL_NAMES.get(tid, f"unknown(0x{tid:02X})")
            print(f"  X={x:5d} Y={y:3d} col={col:4d} row={row:2d} sprite=0x{tid:02X} = {pname}")
