import xml.etree.ElementTree as ET

# Metatile collision name mapping (partial)
COL_NAMES = {
    0x00: "NONE", 0x01: "ALL", 0x02: "FC", 0x03: "NOSIDE",
    0x04: "TOP", 0x05: "BOTTOM", 0x06: "DEATH",
    0x07: "DEATH_BOT", 0x08: "DEATH_TOP", 0x09: "DEATH_LEFT",
    0x0A: "DEATH_RIGHT",
}

tree = ET.parse(r'c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\kratos.tmx')
root = tree.getroot()

for layer in root.findall('layer'):
    if layer.get('name') is None:  # collision layer
        data = layer.find('data').text.strip()
        tiles = [int(x) for x in data.split(',')]
        width = int(layer.get('width'))
        height = int(layer.get('height'))
        groundRows = 3
        
        # Coin at X=9064, Y=248 => col=566, display_row=248/16=15.5 => rows 15-16
        # But with groundRowsToReserve=3, display Y = (row - groundRows) * 16
        # So display Y=248 => row = 248/16 + 3 = 15.5 + 3 = 18.5
        # Check cols 560-575, rows 11-22 (the coin area)
        
        print("Terrain map around coin (cols 555-580, rows 11-22)")
        print("groundRowsToReserve =", groundRows)
        print("Display Y = (row - groundRows) * 16")
        print()
        
        # Header
        header = "     "
        for col in range(555, 581):
            header += f"{col:>4}"
        print(header)
        
        for row in range(11, 23):
            display_y = (row - groundRows) * 16
            line = f"r{row:02d} Y{display_y:>3} "
            for col in range(555, 581):
                idx = row * width + col
                if idx >= len(tiles):
                    line += "  ??"
                    continue
                tid = tiles[idx] - 1  # firstgid offset
                if tid < 0:
                    line += "   ."
                    continue
                # Map special tiles
                if tid in (0xDF, 0xE3, 0xFE, 0xFF): tid = 0x00
                elif tid == 0xFD: tid = 0x26
                
                # Classify
                if tid == 0x00:
                    line += "   ."
                elif tid == 0x01:
                    line += " ALL"
                elif tid == 0x02:
                    line += "  FC"
                elif tid == 0x06:
                    line += "  DT"  # death
                elif tid >= 0x07 and tid <= 0x0A:
                    line += "  D+"  # directional death
                elif tid >= 0x0B and tid <= 0x0E:
                    line += "  D+"
                elif tid >= 0x70 and tid <= 0xC0:
                    line += "  SL"  # slope
                elif tid == 0x2F:
                    line += "  2F"
                elif tid == 0x40:
                    line += "  40"
                elif tid == 0x11:
                    line += "  11"
                elif tid == 0x12:
                    line += "  12"
                elif tid == 0x1B:
                    line += "  1B"
                elif tid == 0x1D:
                    line += "  1D"
                elif tid == 0x1E:
                    line += "  1E"
                elif tid == 0x1F:
                    line += "  1F"
                elif tid == 0x4D:
                    line += "  4D"
                elif tid == 0x4F:
                    line += "  4F"
                elif tid == 0x50:
                    line += "  50"
                elif tid == 0x52:
                    line += "  52"
                elif tid == 0x6D:
                    line += "  6D"
                elif tid == 0x6E:
                    line += "  6E"
                elif tid == 0x6F:
                    line += "  6F"
                else:
                    line += f" {tid:02X}"
                    
            print(line)
            
        # Now check the specific collision types for key tiles
        print("\nDetailed check of metatile collision types in coin area:")
        from collections import Counter
        ct = Counter()
        for row in range(14, 20):
            for col in range(560, 575):
                idx = row * width + col
                tid = tiles[idx] - 1
                if tid < 0: continue
                if tid in (0xDF, 0xE3, 0xFE, 0xFF): tid = 0x00
                elif tid == 0xFD: tid = 0x26
                ct[tid] += 1
        for tid, cnt in sorted(ct.items()):
            print(f"  0x{tid:02X}: {cnt}")
        break
