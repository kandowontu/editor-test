import xml.etree.ElementTree as ET

tree = ET.parse(r'c:\Editor Test\cycles.tmx')
root = tree.getroot()
w = int(root.get('width'))
h = int(root.get('height'))
print(f'Map: {w}x{h}')

for layer in root.findall('.//layer'):
    name = layer.get('name')
    print(f'  Layer: {name}')
    data = layer.find('data').text.strip()
    tiles = [int(x) for x in data.split(',')]
    
    # Define sprite IDs of interest
    # Coins: 0x1B=27
    # Ship portal: 0x0E=14, Cube portal: 0x06=6, Ball portal: 0x20=32
    # UFO portal: 0x0D=13, Wave portal: 0x1E=30
    # Speed portals: 0x0F=15 (1x), 0x0C=12 (2x), 0x1C=28 (3x), 0x24=36 (half)
    interesting = {27: 'COIN', 14: 'SHIP', 6: 'CUBE', 32: 'BALL', 13: 'UFO', 
                   30: 'WAVE', 15: 'SPD1x', 12: 'SPD2x', 28: 'SPD3x', 36: 'SPDhalf',
                   8: 'YELLOW_PAD', 9: 'YELLOW_ORB', 10: 'BLUE_PAD', 11: 'BLUE_ORB',
                   33: 'PINK_PAD', 34: 'PINK_ORB', 23: 'GREEN_PAD', 24: 'GREEN_ORB',
                   44: 'RED_PAD', 45: 'RED_ORB', 40: 'DASH_ORB', 41: 'GRAV_PORT',
                   42: 'GRAV_TRIG'}
    
    for i, t in enumerate(tiles):
        if t in interesting:
            col = i % w
            row = i // w
            x_px = col * 16
            pct = col * 100 // w
            print(f'    col={col} row={row} X={x_px}px ({pct}%) tile=0x{t:X} = {interesting[t]}')
