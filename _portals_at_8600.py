import xml.etree.ElementTree as ET
import os
fn = r'C:\Editor Test\lvlset_HUGE\cataclysm.tmx'
if not os.path.exists(fn):
    # try the dump
    for cand in (r'C:\Editor Test\aftercatabath.tmx',):
        if os.path.exists(cand):
            fn = cand
            break
print('Using:', fn)
tree = ET.parse(fn)
root = tree.getroot()
for objg in root.iter('objectgroup'):
    name = objg.get('name','')
    print(f'\n[objgroup] {name}')
    for obj in objg.findall('object'):
        x = float(obj.get('x',0))
        y = float(obj.get('y',0))
        if 8500 <= x <= 8900:
            sid = None
            for p in obj.iter('property'):
                if p.get('name','').lower() in ('sid','sprite_id'):
                    sid = p.get('value')
            objname = obj.get('name','')
            objtype = obj.get('type','')
            gid = obj.get('gid','')
            oid = obj.get('id','')
            print(f'  x={x:.0f} y={y:.0f} gid={gid} sid={sid} oid={oid} name={objname} type={objtype}')
