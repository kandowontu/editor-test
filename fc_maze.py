import xml.etree.ElementTree as ET

tree = ET.parse(r'c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\kratos.tmx')
root = tree.getroot()
for layer in root.findall('layer'):
    if layer.get('name') is None:  # collision layer has no name
        data = layer.find('data').text.strip()
        tiles = [int(x) for x in data.split(',')]
        width = int(layer.get('width'))
        
        # Check for FC tiles (metatile 0x02 = COL_FLOOR_CEIL) in maze area
        # cols 453-600, rows 12-22 (below FC ceiling at row 11)
        print('Checking tiles in maze area (cols 453-600, rows 12-22):')
        fc_positions = []
        for row in range(12, 23):
            for col in range(453, 601):
                idx = row * width + col
                tid = tiles[idx] - 1  # TMX firstgid offset
                if tid < 0: continue
                # Map special tiles
                if tid in (0xDF, 0xE3, 0xFE, 0xFF): tid = 0x00
                elif tid == 0xFD: tid = 0x26
                if tid == 0x02:
                    fc_positions.append((col, row))
        print(f'FC tiles found: {len(fc_positions)}')
        for p in fc_positions[:50]:
            print(f'  col={p[0]} row={p[1]} X={p[0]*16} Y={p[1]*16}')
            
        # Also check what tile types ARE present
        print('\nAll tile types in maze area:')
        type_counts = {}
        for row in range(12, 23):
            for col in range(453, 601):
                idx = row * width + col
                tid = tiles[idx] - 1
                if tid < 0: continue
                if tid in (0xDF, 0xE3, 0xFE, 0xFF): tid = 0x00
                elif tid == 0xFD: tid = 0x26
                type_counts[tid] = type_counts.get(tid, 0) + 1
        for tid, cnt in sorted(type_counts.items()):
            print(f'  metatile 0x{tid:02X} count={cnt}')
        break
