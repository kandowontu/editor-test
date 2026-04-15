"""Check all layers in kratos.tmx and find the correct collision layer."""
import xml.etree.ElementTree as ET
import re

TMX = r"c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\kratos.tmx"

tree = ET.parse(TMX)
root = tree.getroot()

width = int(root.attrib['width'])
height = int(root.attrib['height'])
print(f"Map: {width}x{height}")

# Check all layers
for i, layer in enumerate(root.findall('.//layer')):
    name = layer.attrib.get('name', '?')
    lw = layer.attrib.get('width', '?')
    lh = layer.attrib.get('height', '?')
    data = layer.find('data')
    enc = data.attrib.get('encoding', 'none') if data is not None else 'no-data'
    
    if data is not None and data.text:
        tiles = [int(x.strip()) for x in data.text.replace('\n', ',').split(',') if x.strip()]
        nonzero = sum(1 for t in tiles if t != 0)
        unique = len(set(tiles))
        # Show some sample tiles around col 461 (X=7376), row 9 (Y=144)
        row9_col461 = tiles[9 * width + 461] if len(tiles) > 9 * width + 461 else -1
        row9_col500 = tiles[9 * width + 500] if len(tiles) > 9 * width + 500 else -1
        row15_col566 = tiles[15 * width + 566] if len(tiles) > 15 * width + 566 else -1
        print(f"  Layer {i}: '{name}' {lw}x{lh} enc={enc} tiles={len(tiles)} nonzero={nonzero} unique={unique}")
        print(f"    Y=144,X=7376: GID={row9_col461}  Y=144,X=8000: GID={row9_col500}  Y=240,X=9056: GID={row15_col566}")
    else:
        print(f"  Layer {i}: '{name}' {lw}x{lh} enc={enc} (no data)")

# Also check objectgroups (sprites)
for i, og in enumerate(root.findall('.//objectgroup')):
    name = og.attrib.get('name', '?')
    objs = og.findall('object')
    print(f"  ObjectGroup {i}: '{name}' objects={len(objs)}")
