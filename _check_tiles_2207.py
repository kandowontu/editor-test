"""Check tile collision types around (337, 13-14) for ball f=2207 region."""
import xml.etree.ElementTree as ET
import re
from pathlib import Path

TMX = r"c:\Editor Test\lvlset_HUGE\dorabaebasic6.tmx"

tree = ET.parse(TMX)
root = tree.getroot()

# find collision layer or main tile layer
for layer in root.findall('.//layer'):
    name = layer.get('name', '')
    w = int(layer.get('width'))
    h = int(layer.get('height'))
    data_elem = layer.find('data')
    encoding = data_elem.get('encoding')
    if encoding == 'csv':
        data = [int(x) for x in re.split(r'[,\s]+', data_elem.text.strip()) if x]
        print(f"Layer '{name}' {w}x{h} CSV first16: {data[:16]}")
        # extract tiles around (335-342, 11-16)
        for ty in range(11, 17):
            row = []
            for tx in range(333, 343):
                gid = data[ty * w + tx] if ty * w + tx < len(data) else 0
                row.append(f"({tx},{ty})={gid}")
            print(f"  Row {ty}: " + " ".join(row))
        print()
