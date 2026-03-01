import xml.etree.ElementTree as ET
import sys

tree = ET.parse(r'c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\polargeist.tmx')
root = tree.getroot()
print("Map:", root.attrib)

for child in root:
    tag = child.tag
    attrib = child.attrib
    if tag == 'tileset':
        print(f"tileset: {attrib}")
    elif tag == 'layer':
        print(f"layer: {attrib}")
        data = child.find('data')
        if data is not None:
            enc = data.get('encoding')
            comp = data.get('compression')
            print(f"  encoding={enc}, compression={comp}")
            text = data.text
            if text:
                text = text.strip()
                print(f"  data text length: {len(text)}")
                print(f"  first 300 chars: {text[:300]}")
    elif tag == 'objectgroup':
        print(f"objectgroup: {attrib}")
        objs = child.findall('object')
        print(f"  {len(objs)} objects")
        for o in objs[:5]:
            print(f"  obj: {o.attrib}")
