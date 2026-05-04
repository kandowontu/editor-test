lines_raw = open('native-windows/MetatileCollision.cs', encoding='utf-8').read()
start = lines_raw.index('string mappingText = @"') + len('string mappingText = @"')
end = lines_raw.index('";', start)
mapping = lines_raw[start:end]
entries = [l.strip().split(';')[0].strip() for l in mapping.split('\n') if l.strip() and not l.strip().startswith('//')]
print(f'Total entries: {len(entries)}')
for iid in range(210, 230):
    if iid < len(entries): print(f'iid={iid}: {entries[iid]}')
