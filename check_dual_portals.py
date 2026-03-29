import xml.etree.ElementTree as ET, csv, io

tree = ET.parse('famidash/LEVELS/LEVEL DATA/lvlset_HUGE/bloodbath.tmx')
root = tree.getroot()

for layer in root.findall('layer'):
    if layer.get('name') != 'SP':
        continue
    data = layer.find('data')
    reader = csv.reader(io.StringIO(data.text.strip()))
    rows = [[int(x) for x in r if x.strip()] for r in reader if any(x.strip() for x in r)]
    
    # Find dual_on (0x22+257=291) and dual_off (0x23+257=292) portals
    for ri, row in enumerate(rows):
        for ci, val in enumerate(row):
            if val in (291, 292):
                sid = val - 257
                label = "DUAL_ON" if sid == 0x22 else "DUAL_OFF"
                print(f"Sprite 0x{sid:02X} at col={ci} row={ri} X={ci*16} Y={ri*16} ({label})")
    
    # Also find mini (0x18+257=281) and growth (0x19+257=282) portals near dual area
    print("\nSize portals near dual area (cols 1190-1260):")
    for ri, row in enumerate(rows):
        for ci, val in enumerate(row):
            if (val == 281 or val == 282) and 1190 <= ci <= 1260:
                sid = val - 257
                label = "MINI" if sid == 0x18 else "GROWTH"
                print(f"  Sprite 0x{sid:02X} at col={ci} row={ri} X={ci*16} Y={ri*16} ({label})")
    
    # Speed portals near dual area
    speed_ids = {0x6D+257, 0x14+257, 0x15+257, 0x16+257, 0x20+257, 0x21+257}
    print("\nSpeed portals near dual area (cols 1190-1260):")
    for ri, row in enumerate(rows):
        for ci, val in enumerate(row):
            if val in speed_ids and 1190 <= ci <= 1260:
                sid = val - 257
                print(f"  Sprite 0x{sid:02X} at col={ci} row={ri} X={ci*16} Y={ri*16}")
