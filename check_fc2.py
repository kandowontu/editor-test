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
        
        print("FC tiles (metatile 0x02) in cols 450-510:")
        for row in range(height):
            for col in range(450, 511):
                idx = row * width + col
                tid = tiles[idx] - 1
                if tid < 0: continue
                if tid in (0xDF, 0xE3, 0xFE, 0xFF): tid = 0x00
                elif tid == 0xFD: tid = 0x26
                if tid == 0x02:
                    display_y = (row - groundRows) * 16
                    print(f"  TMX_row={row} col={col} display_Y={display_y} raw_Y={row*16}")
        
        print("\nFC tiles (metatile 0x02) in cols 510-600:")
        for row in range(height):
            for col in range(510, 601):
                idx = row * width + col
                tid = tiles[idx] - 1
                if tid < 0: continue
                if tid in (0xDF, 0xE3, 0xFE, 0xFF): tid = 0x00
                elif tid == 0xFD: tid = 0x26
                if tid == 0x02:
                    display_y = (row - groundRows) * 16
                    print(f"  TMX_row={row} col={col} display_Y={display_y} raw_Y={row*16}")
        
        # Also verify: what tile is metatile 0x6F?
        print(f"\nMetatile 0x6F at row 11 col 555: what is it?")
        # Check the MetatileCollision enum values
        # Let me just print all unique tiles in the wave section with their frequency
        
        break
