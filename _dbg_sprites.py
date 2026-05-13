import xml.etree.ElementTree as ET
t = ET.parse(r"famidash\LEVELS\LEVEL DATA\lvlset_HUGE\shardscapes.tmx")
r = t.getroot()
print("groups:")
for og in r.findall("objectgroup"):
    print(" ", og.get("name","?"), len(og.findall("object")))
print()
for og in r.findall("objectgroup"):
    for o in og.findall("object"):
        x = float(o.get("x","0")); y = float(o.get("y","0"))
        if 940 <= x <= 1080 and 460 <= y <= 640:
            gid_raw = o.get("gid","")
            try: gid_int = int(gid_raw) & 0x0FFFFFFF if gid_raw else None
            except: gid_int = gid_raw
            print(" [%s] gid=%s(0x%X) x=%g y=%g w=%s h=%s" % (og.get("name"), gid_raw, (gid_int or 0), x, y, o.get("width",""), o.get("height","")))
