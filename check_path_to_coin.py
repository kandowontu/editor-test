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

# Find where row 12 floor ends (transitions from tile 7 to something else)
print("Row 12 floor boundary (where FC ends):")
for c in range(530, 575):
    t = tiles[12 * width + c]
    mt = t - 1 if t > 0 else -1
    if t != 7:
        print(f"  Floor ends before col {c} X={c*16}: tile={t} mt={mt}")
        break

# Map terrain from col 508 to 570, rows 11-22 with proper collision
# Using direct tile IDs to be precise
col_map = {}
for line_idx, line in enumerate("""NONE FC FC BOT DT FC FC NONE DB DB DT DT DB DT DL DR
ALL D DB DB DT TCS ALL DT DB TOP D D D DL DT DR
ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL NONE
ALL ALL ALL ALL NONE NONE NONE TCS ALL ALL ALL DB ALL ALL ALL ALL
ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL NONE
ALL ALL ALL ALL TOP TOP TOP TOP BOT BOT BOT D DB DT DR DL
ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL ALL NONE
ALL ALL ALL ALL DRS DB DLS DR NONE DL URS DT ULS D ALL DB
NONE NONE D D DB DT DB DT FC FC R L R L NONE NS
SRD SLD SRU SLU S22R S22L S22R S22L S22R S22L S22R S22L S66T S66B S66B S66T
S66T S66B S66B S66T NS NS NS NS LSB RSB BLS BRS BS DLS DRS DBS
UL UR DL DR TOP BOT L R TLS TRS BLS BRS TLBR TRBL ULS URS
UBS DRT DTL DBR DBL NONE NONE BCS BCS TOP BOT NONE NONE NONE NONE NONE
NONE NONE NONE ALL NONE NONE NONE NONE NONE DRS DLS ULS URS L R UR
R TOP TOP UL TOP R BOT TOP NONE NONE NONE NONE NONE NONE NONE NONE
DRS DLS URS ULS DRS DLS D R L URS ULS D ALL L DDR DDL""".strip().split('\n')):
    for tok_idx, tok in enumerate(line.strip().split()):
        col_map[line_idx * 16 + tok_idx] = tok

def classify(mt):
    if mt < 0: return ' '
    c = col_map.get(mt, '?')
    if c == 'NONE': return '.'
    if c == 'ALL': return '#'
    if c == 'FC': return '='
    if c in ('TOP','BOT','L','R','UL','UR','DL','DR','NS','TLBR','TRBL'): return 'H'
    if c.startswith('S') or c.startswith('TLS') or c.startswith('TRS') or c.startswith('BLS') or c.startswith('BRS'): return '/'
    if c in ('D','DT','DB','DL','DR','DRT','DTL','DBR','DBL','DRS','DLS','URS','ULS','UBS','DBS','LSB','RSB','BCS','TCS','DDR','DDL'): return '!'
    if c == 'BS': return '!'
    return '?'

# Print terrain from col 505 to 570, rows 4-22
print("\nTerrain from col 505 to 570 (X=8080 to X=9120), rows 4-22:")
print("  Coin at col 566 (X=9056), rows 15-16 (Y=248)")
print("  . = passable, # = solid, = = floor/ceil, ! = death, / = slope, H = half")
print()

hdr = "      "
for c in range(505, 571):
    hdr += str(c % 10)
print(hdr)
hdr2 = "      "
for c in range(505, 571):
    if c % 10 == 0:
        hdr2 += str(c // 10 % 10)
    else:
        hdr2 += " "
print(hdr2)

for r in range(4, 23):
    line = f"r{r:2d} {r*16:3d}: "
    for c in range(505, 571):
        t = tiles[r * width + c]
        mt = t - 1 if t > 0 else -1
        if t == 0:
            ch = ' '
        else:
            ch = classify(mt)
    line += ch
    print(line)

# Actually, rebuild properly
for r in range(4, 23):
    line = f"r{r:2d} {r*16:3d}: "
    for c in range(505, 571):
        t = tiles[r * width + c]
        mt = t - 1 if t > 0 else -1
        if t == 0:
            line += ' '
        else:
            line += classify(mt)
    print(line)
