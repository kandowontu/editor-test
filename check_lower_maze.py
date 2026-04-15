import xml.etree.ElementTree as ET, csv, io

tree = ET.parse(r'c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\kratos.tmx')
root = tree.getroot()
width = int(root.attrib['width'])
height = int(root.attrib['height'])
layer = root.findall('.//layer')[0]
data = layer.find('data').text.strip()
reader = csv.reader(io.StringIO(data))
tiles = []
for row in reader:
    tiles.extend([int(x.strip()) for x in row if x.strip()])

# Collision table for metatiles (after subtracting firstgid=1)
# Simplified: just categorize as PASS/SOLID/DEATH/SLOPE/FC
col_table_raw = """NONE FC FC BOT DT FC FC NONE DB DB DT DT DB DT DL DR
ALL D DB DB DT TCS ALL DT DB TOP D D D DL DT DR
ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL NONE
ALL ALL ALL ALL NONE NONE NONE TCS ALL ALL ALL DB ALL ALL ALL ALL
ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL NONE
ALL ALL ALL ALL TOP TOP TOP TOP BOT BOT BOT D DB DT DR DL
ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL NONE
ALL ALL ALL ALL DRS DB DLS DR NONE DL URS DT ULS D ALL DB
NONE NONE D D DB DT DB DT FC FC R L R L NONE NS
SRD SLD SRU SLU SRD22R SRD22L SLD22R SLD22L SRU22R SRU22L SLU22R SLU22L SRD66T SRD66B SLD66B SLD66T
SRU66T SRU66B SLU66B SLU66T NS NS NS NS LSB RSB BLS BRS BS DLS2 DRS2 DBS
UL UR DL DR TOP BOT L R TLS TRS BLS2 BRS2 TLBR TRBL ULS2 URS2
UBS DRT DTL DBR DBL NONE NONE BCS BCS TOP BOT NONE NONE NONE NONE NONE
NONE NONE NONE ALL NONE NONE NONE NONE NONE DRS DLS ULS URS L R UR
R TOP TOP UL TOP R BOT TOP NONE NONE NONE NONE NONE NONE NONE NONE
DRS DLS URS ULS DRS DLS D R L URS ULS D ALL L DDR DDL"""

def parse_col_table():
    table = ['NONE'] * 256
    idx = 0
    for line in col_table_raw.strip().split('\n'):
        for tok in line.strip().split():
            if idx < 256:
                table[idx] = tok
                idx += 1
    return table

ct = parse_col_table()

def classify(mt):
    """Classify metatile for display"""
    if mt < 0 or mt >= 256:
        return ' '
    c = ct[mt]
    if c == 'NONE':
        return '.'
    if c == 'ALL':
        return '#'
    if c == 'FC':
        return '='
    if c in ('TOP', 'BOT', 'L', 'R', 'UL', 'UR', 'DL', 'DR', 'NS', 'TLBR', 'TRBL'):
        return 'H'  # half-solid
    if c.startswith('S') or c.endswith('22R') or c.endswith('22L') or c.endswith('66T') or c.endswith('66B'):
        return '/'  # slope
    if c.startswith('D') or c.startswith('U') or c == 'TCS' or c == 'BCS' or c == 'D':
        return '!'  # death
    if c.endswith('SB') or c.endswith('LS') or c.endswith('RS') or c.endswith('BS'):
        return 'X'  # spike block
    return '?'

# Map lower terrain from cols 454 to 575, rows 11-22
print("Lower terrain below corridor (rows 11-22, cols 454-530):")
print("  Wave portal at col 454. Corridor floor starts at row 11 (Y=176).")
print("  Coin at col 566, row 15-16 (Y=248)")
print("  . = passable, # = solid, = = floor/ceil, ! = death, / = slope, H = half")
print()

header = "     "
for c in range(454, 531):
    header += f"{c%100:2d}"
print(header)

for r in range(11, 23):
    line = f"r{r:2d}: "
    for c in range(454, 531):
        t = tiles[r * width + c]
        mt = t - 1 if t > 0 else -1
        if t == 0:
            ch = '.'
        else:
            ch = classify(mt)
        line += f"{ch} "
    print(f"{line} Y={r*16}")
