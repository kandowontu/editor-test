import re
text = open(r'c:\Editor Test\native-windows\MetatileCollision.cs', 'r', encoding='utf-8').read()
m = re.search(r'mappingText\s*=\s*@"(.*?)";', text, re.DOTALL)
s = m.group(1)
lines = [l.strip() for l in s.replace('\r','').split('\n') if l.strip()]
print('total lines:', len(lines))
for i in range(75, 90):
    print(i, hex(i), lines[i])
print('---')
for i in range(157, 175):
    print(i, hex(i), lines[i])
