import os

# Check the bfs_spider.txt file
temp = os.environ.get('TEMP', os.environ.get('TMP', '/tmp'))
path = os.path.join(temp, 'bfs_spider.txt')

result = []
result.append(f'TEMP={temp}')
result.append(f'Path={path}')
result.append(f'Exists={os.path.exists(path)}')

if os.path.exists(path):
    size = os.path.getsize(path)
    result.append(f'Size={size}')
    # Read first few lines and check encoding
    with open(path, 'rb') as f:
        header = f.read(100)
        result.append(f'Header bytes: {header[:20].hex()}')
    
    # Try reading as text - stderr from PowerShell is likely UTF-16-LE
    for enc in ['utf-16-le', 'utf-16', 'utf-8', 'latin-1']:
        try:
            with open(path, 'r', encoding=enc, errors='replace') as f:
                lines = f.readlines()[:5]
            first_line = lines[0][:80] if lines else "EMPTY"
            # Count spider lines
            with open(path, 'r', encoding=enc, errors='replace') as f:
                spider_count = sum(1 for l in f if 'SPIDER' in l)
            result.append(f'Enc {enc}: spider_count={spider_count}, first_line={first_line!r}')
            if spider_count > 0:
                break
        except Exception as ex:
            result.append(f'Enc {enc}: FAILED - {ex}')
else:
    # Look for similar files
    result.append('File not found. Looking for similar files:')
    for f in os.listdir(temp):
        if 'bfs' in f.lower() or 'spider' in f.lower():
            result.append(f'  {f} ({os.path.getsize(os.path.join(temp, f))})')

with open(r'c:\Editor Test\check_result.txt', 'w', encoding='utf-8') as out:
    out.write('\n'.join(result))
