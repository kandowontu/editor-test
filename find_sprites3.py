import xml.etree.ElementTree as ET
import csv
import io

tree = ET.parse(r'C:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\clubstep.tmx')
root = tree.getroot()

# Parse tilesets
tilesets = []
for ts in root.iter('tileset'):
    tilesets.append({
        'name': ts.get('name'),
        'firstgid': int(ts.get('firstgid')),
        'tilecount': int(ts.get('tilecount', 256))
    })
print("Tilesets:", tilesets)

# Get SP layer data
for layer in root.iter('layer'):
    name = layer.get('name', '')
    if name == 'SP':
        data_elem = layer.find('data')
        csv_text = data_elem.text.strip()
        rows = []
        for line in csv_text.split('\n'):
            line = line.strip().rstrip(',')
            if line:
                rows.append([int(x) for x in line.split(',')])
        
        print(f"\nSP layer: {len(rows)} rows x {len(rows[0])} cols")
        
        # Show tiles at columns 440-460 (x=7040-7360)
        print(f"\n=== SP layer tiles at columns 440-460 ===")
        for col in range(440, 461):
            for row in range(len(rows)):
                gid = rows[row][col]
                if gid != 0:
                    # Determine which tileset
                    ts_name = "?"
                    local_id = gid
                    for ts in reversed(tilesets):
                        if gid >= ts['firstgid']:
                            ts_name = ts['name']
                            local_id = gid - ts['firstgid']
                            break
                    x_px = col * 16
                    y_px = row * 16
                    print(f"  col={col} row={row} gid={gid} tileset={ts_name} local_id={local_id} x_px={x_px} y_px={y_px}")

        # Wider view: columns 430-470
        print(f"\n=== SP layer tiles at columns 430-470 (wider) ===")
        for col in range(430, 471):
            for row in range(len(rows)):
                gid = rows[row][col]
                if gid != 0:
                    ts_name = "?"
                    local_id = gid
                    for ts in reversed(tilesets):
                        if gid >= ts['firstgid']:
                            ts_name = ts['name']
                            local_id = gid - ts['firstgid']
                            break
                    x_px = col * 16
                    y_px = row * 16
                    print(f"  col={col} row={row} gid={gid} tileset={ts_name} local_id={local_id} x_px={x_px} y_px={y_px}")

    elif name == '':
        # Also check the terrain layer
        data_elem = layer.find('data')
        csv_text = data_elem.text.strip()
        rows = []
        for line in csv_text.split('\n'):
            line = line.strip().rstrip(',')
            if line:
                rows.append([int(x) for x in line.split(',')])
        
        print(f"\n=== Terrain layer tiles at columns 440-460 ===")
        for col in range(440, 461):
            for row in range(len(rows)):
                gid = rows[row][col]
                if gid != 0:
                    x_px = col * 16
                    y_px = row * 16
                    ts_name = "?"
                    local_id = gid
                    for ts in reversed(tilesets):
                        if gid >= ts['firstgid']:
                            ts_name = ts['name']
                            local_id = gid - ts['firstgid']
                            break
                    print(f"  col={col} row={row} gid={gid} tileset={ts_name} local_id={local_id} x_px={x_px} y_px={y_px}")
