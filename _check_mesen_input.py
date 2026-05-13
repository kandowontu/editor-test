import csv, os
temp = os.environ['TEMP']
mesen_file = os.path.join(temp, 'famidash_mesen_trace.csv')

mesen = {}
with open(mesen_file, 'r') as f:
    lines = f.readlines()
    if len(lines) > 1:
        hdr = lines[1].strip().split(',')
        for line in lines[2:]:
            parts = line.strip().split(',')
            if not parts or not parts[0]: continue
            try:
                d = {hdr[i]: parts[i] for i in range(min(len(hdr), len(parts)))}
                s = int(d.get('sim_cursor', -1))
                if 4290 <= s <= 4310:
                    a_cur = d.get('a_cur','?')
                    input_state = d.get('input_state','?')
                    print(f'Sim {s}: a_cur={a_cur} input_state={input_state}')
            except:
                pass
