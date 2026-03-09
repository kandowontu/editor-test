import xml.etree.ElementTree as ET

tree = ET.parse(r'c:\Editor Test\cycles.tmx')
root = tree.getroot()
w = int(root.get('width'))
h = int(root.get('height'))

# Find tilesets
for ts in root.findall('.//tileset'):
    print(f'Tileset: firstgid={ts.get("firstgid")} name={ts.get("name")}')

# Find SP layer and analyze
layers = list(root.findall('.//layer'))
sp_layer = None
for l in layers:
    name = l.get('name') or ''
    if name == 'SP':
        sp_layer = l
        break

if sp_layer is None:
    print("No SP layer found")
else:
    data = sp_layer.find('data').text.strip()
    tiles = [int(x) for x in data.split(',')]
    
    # Subtract firstgid to get sprite IDs
    # Try firstgid=257 (common for second tileset)
    firstgid = 257  # adjust if needed
    
    # Interesting sprite IDs (after subtracting firstgid)  
    interesting = {
        0x1B: 'COIN', 0x0E: 'SHIP_PORT', 0x06: 'CUBE_PORT', 0x20: 'BALL_PORT',
        0x0D: 'UFO_PORT', 0x1E: 'WAVE_PORT',
        0x0F: 'SPD1x', 0x0C: 'SPD2x', 0x1C: 'SPD3x', 0x24: 'SPDhalf',
        0x08: 'YELLOW_PAD', 0x09: 'YELLOW_ORB', 0x0A: 'BLUE_PAD', 0x0B: 'BLUE_ORB',
        0x21: 'PINK_PAD', 0x22: 'PINK_ORB', 0x17: 'GREEN_PAD', 0x18: 'GREEN_ORB',
        0x2C: 'RED_PAD', 0x2D: 'RED_ORB', 0x28: 'DASH_ORB',
        0x29: 'GRAV_PORT', 0x2A: 'GRAV_TRIG',
        0x2E: 'SPIDER_PORT', 0x1F: 'ROBOT_PORT',
    }
    
    print(f'\nSprite analysis (firstgid={firstgid}):')
    for i, t in enumerate(tiles):
        if t == 0:
            continue
        sid = t - firstgid
        if sid < 0:
            continue
        col = i % w
        row = i // w
        x_px = col * 16
        pct = col * 100 // w
        label = interesting.get(sid, f'OTHER_0x{sid:X}')
        if sid in interesting:
            print(f'  col={col:4d} row={row:2d} X={x_px:5d}px ({pct:2d}%) sid=0x{sid:02X} = {label}')
