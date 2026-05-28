import re
text = open(r'c:\Editor Test\native-windows\MetatileCollision.cs', 'r', encoding='utf-8').read()
m = re.search(r'mappingText\s*=\s*@"(.*?)";', text, re.DOTALL)
s = m.group(1)
lines = [l.strip() for l in s.replace('\r','').split('\n') if l.strip()]
for v in range(0x80, 0x95):
    print(hex(v), lines[v])
