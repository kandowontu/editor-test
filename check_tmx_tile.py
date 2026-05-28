import xml.etree.ElementTree as ET, re, zlib, base64, struct
T=ET.parse(r"c:\Editor Test\lvlset_HUGE\dorabaebasic6.tmx").getroot()
for layer in T.iter("layer"):
    name=layer.get("name")
    data=layer.find("data")
    enc=data.get("encoding"); comp=data.get("compression")
    text=data.text.strip()
    print(f"=== {name} {enc}/{comp} w={layer.get('width')} h={layer.get('height')}")
    if enc=="csv":
        rows=[r for r in text.split("\n") if r.strip()]
        for tr in [12,13,14]:
            if tr<len(rows):
                cells=rows[tr].rstrip(",").split(",")
                print(f"  row{tr} tiles 337..345:", cells[337:346] if len(cells)>345 else cells[337:])
    elif enc=="base64":
        raw=base64.b64decode(text)
        if comp=="zlib": raw=zlib.decompress(raw)
        elif comp=="gzip":
            import gzip; raw=gzip.decompress(raw)
        w=int(layer.get('width'))
        ids=struct.unpack(f"<{len(raw)//4}I", raw)
        for tr in [12,13,14]:
            base=tr*w
            print(f"  row{tr} tiles 337..345:", ids[base+337:base+346])
