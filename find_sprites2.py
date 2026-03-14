import xml.etree.ElementTree as ET
tree = ET.parse(r'C:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\clubstep.tmx')
root = tree.getroot()

# Show all top-level elements
print("=== Top-level structure ===")
print(f"Root tag: {root.tag}, attribs: {root.attrib}")
for child in root:
    attrs = dict(child.attrib)
    name = attrs.get('name', '')
    print(f"  <{child.tag}> name={name} attribs={attrs}")
    # Show first few sub-elements
    count = 0
    for sub in child:
        if count < 3:
            print(f"    <{sub.tag}> attribs={dict(sub.attrib)}")
        count += 1
    if count > 3:
        print(f"    ... ({count} total sub-elements)")

# Check if there are tile layers - sprites might be in tile data
print("\n=== Checking for layer data ===")
for layer in root.iter('layer'):
    name = layer.get('name', '')
    w = layer.get('width', '?')
    h = layer.get('height', '?')
    print(f"  Layer: {name} ({w}x{h})")
