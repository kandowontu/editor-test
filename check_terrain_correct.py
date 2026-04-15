import xml.etree.ElementTree as ET

# Build the metatile collision table from MetatileCollision.cs mappingText
mapping_text = """COL_NONE
COL_FLOOR_CEIL
COL_FLOOR_CEIL
COL_BOTTOM
COL_DEATH_TOP
COL_FLOOR_CEIL
COL_FLOOR_CEIL
COL_NONE
COL_DEATH_BOTTOM
COL_DEATH_BOTTOM
COL_DEATH_TOP
COL_DEATH_TOP
COL_DEATH_BOTTOM
COL_DEATH_TOP
COL_DEATH_LEFT
COL_DEATH_RIGHT
COL_ALL
COL_DEATH
COL_DEATH_BOTTOM
COL_DEATH_BOTTOM
COL_DEATH_TOP
COL_TOP_CENTER_SPIKE
COL_ALL
COL_DEATH_TOP
COL_DEATH_BOTTOM
COL_TOP
COL_DEATH
COL_DEATH
COL_DEATH
COL_DEATH_LEFT
COL_DEATH_TOP
COL_DEATH_RIGHT
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_NONE
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_NONE
COL_NONE
COL_NONE
COL_TOP_CENTER_SPIKE
COL_ALL
COL_ALL
COL_ALL
COL_DEATH_BOTTOM
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_NONE
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_TOP
COL_TOP
COL_TOP
COL_TOP
COL_BOTTOM
COL_BOTTOM
COL_BOTTOM
COL_DEATH
COL_DEATH_BOTTOM
COL_DEATH_TOP
COL_DEATH_RIGHT
COL_DEATH_LEFT
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_NONE
COL_ALL
COL_ALL
COL_ALL
COL_ALL
COL_DOWN_RIGHT_SPIKE
COL_DEATH_BOTTOM
COL_DOWN_LEFT_SPIKE
COL_DEATH_RIGHT
COL_NONE
COL_DEATH_LEFT
COL_UP_RIGHT_SPIKE
COL_DEATH_TOP
COL_UP_LEFT_SPIKE
COL_DEATH
COL_ALL
COL_DEATH_BOTTOM
COL_NONE
COL_NONE
COL_DEATH
COL_DEATH
COL_DEATH_BOTTOM
COL_DEATH_TOP
COL_DEATH_BOTTOM
COL_DEATH_TOP
COL_FLOOR_CEIL
COL_FLOOR_CEIL
COL_RIGHT
COL_LEFT
COL_RIGHT
COL_LEFT
COL_NONE
COL_NO_SIDE
COL_SLOPE_RD45
COL_SLOPE_LD45
COL_SLOPE_RU45
COL_SLOPE_LU45
COL_SLOPE_RD22_RIGHT
COL_SLOPE_RD22_LEFT
COL_SLOPE_LD22_RIGHT
COL_SLOPE_LD22_LEFT
COL_SLOPE_RU22_RIGHT
COL_SLOPE_RU22_LEFT
COL_SLOPE_LU22_RIGHT
COL_SLOPE_LU22_LEFT
COL_SLOPE_RD66_TOP
COL_SLOPE_RD66_BOT
COL_SLOPE_LD66_BOT
COL_SLOPE_LD66_TOP
COL_SLOPE_RU66_TOP
COL_SLOPE_RU66_BOT
COL_SLOPE_LU66_BOT
COL_SLOPE_LU66_TOP
COL_NO_SIDE
COL_NO_SIDE
COL_NO_SIDE
COL_NO_SIDE
COL_LEFT_SPIKE_BLOCK
COL_RIGHT_SPIKE_BLOCK
COL_BOTTOM_LEFT_SPIKE
COL_BOTTOM_RIGHT_SPIKE
COL_BOTTOM_SPIKES
COL_DOWN_LEFT_SPIKE
COL_DOWN_RIGHT_SPIKE
COL_DOWN_BOTH_SPIKES
COL_UP_LEFT
COL_UP_RIGHT
COL_DOWN_LEFT
COL_DOWN_RIGHT
COL_TOP
COL_BOTTOM
COL_LEFT
COL_RIGHT
COL_TOP_LEFT_STAIRS
COL_TOP_RIGHT_STAIRS
COL_BOTTOM_LEFT_STAIRS
COL_BOTTOM_RIGHT_STAIRS
COL_TOP_LEFT_BOTTOM_RIGHT
COL_TOP_RIGHT_BOTTOM_LEFT
COL_UP_LEFT_SPIKE
COL_UP_RIGHT_SPIKE
COL_UP_BOTH_SPIKES
COL_DEATH_TOP_RIGHT
COL_DEATH_TOP_LEFT
COL_DEATH_BOTTOM_RIGHT
COL_DEATH_BOTTOM_LEFT
COL_NONE
COL_NONE
COL_BOTTOM_CENTER_SPIKE
COL_BOTTOM_CENTER_SPIKE
COL_TOP
COL_BOTTOM
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_ALL
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_DOWN_RIGHT_SPIKE
COL_DOWN_LEFT_SPIKE
COL_UP_LEFT_SPIKE
COL_UP_RIGHT_SPIKE
COL_LEFT
COL_RIGHT
COL_UP_RIGHT
COL_RIGHT
COL_TOP
COL_TOP
COL_UP_LEFT
COL_TOP
COL_RIGHT
COL_BOTTOM
COL_TOP
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_NONE
COL_DOWN_RIGHT_SPIKE
COL_DOWN_LEFT_SPIKE
COL_UP_RIGHT_SPIKE
COL_UP_LEFT_SPIKE
COL_DOWN_RIGHT_SPIKE
COL_DOWN_LEFT_SPIKE
COL_DEATH
COL_RIGHT
COL_LEFT
COL_UP_RIGHT_SPIKE
COL_UP_LEFT_SPIKE
COL_DEATH
COL_ALL
COL_LEFT
COL_DOWN_RIGHT
COL_DOWN_LEFT"""

table = ['COL_NONE'] * 256
lines = [L.strip() for L in mapping_text.strip().split('\n') if L.strip()]
for i, line in enumerate(lines):
    if i >= 256: break
    # handle ";$80" suffix
    name = line.split(';')[0].strip()
    table[i] = name

# Short names for display
def short(name):
    m = {
        'COL_NONE': '.', 'COL_ALL': 'A', 'COL_FLOOR_CEIL': 'F',
        'COL_TOP': 'T', 'COL_BOTTOM': 'B', 'COL_LEFT': 'L', 'COL_RIGHT': 'R',
        'COL_NO_SIDE': 'N', 'COL_DEATH': 'X', 'COL_UP_LEFT': 'u',
        'COL_UP_RIGHT': 'v', 'COL_DOWN_LEFT': 'w', 'COL_DOWN_RIGHT': 'x',
    }
    if name in m: return m[name]
    if 'DEATH' in name: return 'D'
    if 'SLOPE' in name: return 'S'
    if 'SPIKE' in name: return 'K'
    if 'STAIRS' in name: return 's'
    return '?'

tree = ET.parse(r'c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\kratos.tmx')
root = tree.getroot()
for layer in root.findall('layer'):
    if layer.get('name') is None:
        data = layer.find('data').text.strip()
        tiles = [int(x) for x in data.split(',')]
        width = int(layer.get('width'))
        groundRows = 3
        
        def get_col(row, col):
            idx = row * width + col
            tid = tiles[idx] - 1
            if tid < 0: return 'COL_NONE'
            if tid in (0xDF, 0xE3, 0xFE, 0xFF): tid = 0x00
            elif tid == 0xFD: tid = 0x26
            return table[tid]
        
        # Map terrain around coin (cols 555-580, rows 10-23)
        print("COLLISION MAP around coin (cols 555-580)")
        print("F=FC, A=ALL, .=NONE, D=death, S=slope, X=DEATH, K=spike, T=TOP, B=BOT")
        print()
        header = "         "
        for col in range(555, 581):
            header += f"{col%10}"
        print(header)
        for row in range(10, 24):
            dy = (row - groundRows) * 16
            line = f"r{row:02d} Y{dy:>3}  "
            for col in range(555, 581):
                line += short(get_col(row, col))
            print(line)
        
        # Now map the full FC ceiling area (cols 450-560)
        print("\nFC CEILING MAP (cols 450-560)")
        header2 = "         "
        # Print every 5th column for header
        for col in range(450, 561):
            if col % 10 == 0:
                header2 += str(col // 10 % 10)
            else:
                header2 += " "
        print(header2)
        header3 = "         "
        for col in range(450, 561):
            header3 += str(col % 10)
        print(header3)
        for row in range(10, 24):
            dy = (row - groundRows) * 16
            line = f"r{row:02d} Y{dy:>3}  "
            for col in range(450, 561):
                line += short(get_col(row, col))
            print(line)
            
        # Count FC tiles per row in wave section
        print("\nFC tiles per row (cols 450-600):")
        for row in range(8, 26):
            fc_count = 0
            fc_cols = []
            for col in range(450, 601):
                if get_col(row, col) == 'COL_FLOOR_CEIL':
                    fc_count += 1
                    fc_cols.append(col)
            if fc_count > 0:
                dy = (row - groundRows) * 16
                print(f"  TMX_row={row} Y={dy}: {fc_count} tiles, cols={fc_cols[0]}..{fc_cols[-1]}")
                
        break
