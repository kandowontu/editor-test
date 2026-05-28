import xml.etree.ElementTree as ET
import re

tmx = r'c:\Editor Test\lvlset_HUGE\cataclysm.tmx'
tree = ET.parse(tmx)
root = tree.getroot()

# Find sp layer
for layer in root.iter('layer'):
    name = layer.attrib.get('name', '')
    if name.upper() == 'SP':
        data = layer.find('data').text
        w = int(layer.attrib['width'])
        # Read CSV
        ids = [int(v) for v in re.findall(r'-?\d+', data)]
        # Show sprites in tile range x=530-550 (covers 8480-8800)
        print(f"sp layer w={w} h={len(ids)//w}")
        print("Sprites at tile_x=530-550:")
        for tile_y in range(len(ids)//w):
            for tile_x in range(530, 555):
                idx = tile_y*w + tile_x
                if idx < len(ids):
                    sid = ids[idx] - 1  # TMX gid offset
                    if sid >= 0 and sid < 0xFC and ids[idx] != 0:
                        # Compute level-stream order: idx in sprites array
                        print(f"  tile=({tile_x:3d},{tile_y:2d}) idx={idx} sid=0x{sid:02X} (raw_gid={ids[idx]})")
        break
