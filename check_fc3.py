import xml.etree.ElementTree as ET

tree = ET.parse(r'c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\kratos.tmx')
root = tree.getroot()
for layer in root.findall('layer'):
    if layer.get('name') is None:
        data = layer.find('data').text.strip()
        tiles = [int(x) for x in data.split(',')]
        width = int(layer.get('width'))
        height = int(layer.get('height'))
        groundRows = 3
        
        # COL_FLOOR_CEIL = 0x0A (10th in enum, 0-indexed)
        FC = 0x0A
        
        print("FC tiles (metatile 0x0A = COL_FLOOR_CEIL) in cols 450-510:")
        for row in range(height):
            for col in range(450, 511):
                idx = row * width + col
                tid = tiles[idx] - 1
                if tid < 0: continue
                if tid in (0xDF, 0xE3, 0xFE, 0xFF): tid = 0x00
                elif tid == 0xFD: tid = 0x26
                if tid == FC:
                    display_y = (row - groundRows) * 16
                    print(f"  TMX_row={row} col={col} display_Y={display_y}")
        
        print("\nFC tiles (metatile 0x0A = COL_FLOOR_CEIL) in cols 510-600:")
        for row in range(height):
            for col in range(510, 601):
                idx = row * width + col
                tid = tiles[idx] - 1
                if tid < 0: continue
                if tid in (0xDF, 0xE3, 0xFE, 0xFF): tid = 0x00
                elif tid == 0xFD: tid = 0x26
                if tid == FC:
                    display_y = (row - groundRows) * 16
                    print(f"  TMX_row={row} col={col} display_Y={display_y}")
                    
        # Also check the maze area for FC tiles
        print("\nFC tiles in maze area (cols 453-600, rows 3-25):")
        fc_in_maze = 0
        for row in range(3, 26):
            for col in range(453, 601):
                idx = row * width + col
                tid = tiles[idx] - 1
                if tid < 0: continue
                if tid in (0xDF, 0xE3, 0xFE, 0xFF): tid = 0x00
                elif tid == 0xFD: tid = 0x26
                if tid == FC:
                    display_y = (row - groundRows) * 16
                    fc_in_maze += 1
                    print(f"  TMX_row={row} col={col} display_Y={display_y}")
        print(f"Total FC in maze area: {fc_in_maze}")
        
        break
