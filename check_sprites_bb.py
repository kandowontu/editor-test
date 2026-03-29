import xml.etree.ElementTree as ET, csv, io

tree = ET.parse(r'famidash/LEVELS/LEVEL DATA/lvlset_HUGE/bloodbath.tmx')
root = tree.getroot()

# SP tileset firstgid=257
FIRSTGID = 257

for layer in root.findall('layer'):
    if layer.get('name') != 'SP':
        continue
    data = layer.find('data')
    reader = csv.reader(io.StringIO(data.text.strip()))
    rows = []
    for r in reader:
        row = [int(x) for x in r if x.strip()]
        if row:
            rows.append(row)
    
    # Collect all unique sprite IDs
    sprite_ids = set()
    for row in rows:
        for val in row:
            if val > 0:
                sprite_ids.add(val - FIRSTGID)
    
    teleport_enter = {0x4E, 0x66, 0x68, 0x75, 0x77}
    teleport_exit = {0x4F, 0x67, 0x69, 0x76, 0x78}
    
    print("All unique sprite IDs in Bloodbath:")
    for sid in sorted(sprite_ids):
        label = ""
        if sid in teleport_enter: label = " TELEPORT_ENTER"
        elif sid in teleport_exit: label = " TELEPORT_EXIT"
        elif sid == 0x22: label = " DUAL_PORTAL"
        elif sid == 0x23: label = " DUAL_OFF"
        print(f"  0x{sid:02X} ({sid}){label}")
    
    # Also check if any sprites seem like they should subtract differently
    print("\nChecking for teleport portal raw TMX IDs:")
    for sid_raw in [0x4E + FIRSTGID, 0x4F + FIRSTGID, 0x66 + FIRSTGID, 0x67 + FIRSTGID]:
        found = False
        for ri, row in enumerate(rows):
            for ci, val in enumerate(row):
                if val == sid_raw:
                    print(f"  Found TMX id {sid_raw} (sprite 0x{sid_raw-FIRSTGID:02X}) at col={ci} row={ri} X={ci*16} Y={ri*16}")
                    found = True
        if not found:
            print(f"  TMX id {sid_raw} (sprite 0x{sid_raw-FIRSTGID:02X}) NOT FOUND")
