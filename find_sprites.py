import xml.etree.ElementTree as ET
tree = ET.parse(r'C:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\clubstep.tmx')
root = tree.getroot()

# Get tile size
tw = int(root.get('tilewidth', 16))
th = int(root.get('tileheight', 16))
print(f"Tile size: {tw}x{th}")

# Search for objects near X=7100-7300
print("\n=== Objects near X=7100-7300 ===")
for group in root.iter('objectgroup'):
    layer_name = group.get('name', 'unnamed')
    for obj in group.iter('object'):
        x = float(obj.get('x', 0))
        if 7100 <= x <= 7300:
            props = {}
            for p in obj.iter('property'):
                props[p.get('name')] = p.get('value')
            oid = obj.get('id')
            y = obj.get('y')
            w = obj.get('width', '?')
            h = obj.get('height', '?')
            t = obj.get('type', '')
            n = obj.get('name', '')
            gid = obj.get('gid', '')
            print(f"  Layer={layer_name} id={oid} x={x} y={y} w={w} h={h} type={t} name={n} gid={gid} props={props}")

# Also search by tile column (x/16 = 449-451)
print("\n=== Also checking tile columns 440-460 (x=7040-7360) ===")
for group in root.iter('objectgroup'):
    layer_name = group.get('name', 'unnamed')
    for obj in group.iter('object'):
        x = float(obj.get('x', 0))
        if 7040 <= x <= 7360:
            props = {}
            for p in obj.iter('property'):
                props[p.get('name')] = p.get('value')
            oid = obj.get('id')
            y = obj.get('y')
            w = obj.get('width', '?')
            h = obj.get('height', '?')
            t = obj.get('type', '')
            n = obj.get('name', '')
            gid = obj.get('gid', '')
            print(f"  Layer={layer_name} id={oid} x={x} y={y} w={w} h={h} type={t} name={n} gid={gid} props={props}")

# List all object layer names
print("\n=== Object layer names ===")
for group in root.iter('objectgroup'):
    print(f"  {group.get('name', 'unnamed')}")
