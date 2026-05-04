import os
fp = r'C:\Users\kando\Documents\Famidash Editor\Replays\everyend\famidash_mesen_trace.csv'
hdr = None
with open(fp) as fh:
    for line in fh:
        s = line.strip()
        if not s or s.startswith('nes_y_offset'):
            continue
        if s.startswith('rom_frame'):
            hdr = s.split(',')
            continue
        if s.startswith('#'):
            continue
        p = s.split(',')
        try:
            rf = int(p[0])
        except Exception:
            continue
        if 10780 <= rf <= 10800:
            d = dict(zip(hdr, p))
            print(f"rf={rf} sc={d['sim_cursor']} py={d['py']} a_n={d['a_next']} a_c={d['a_cur']} dash={d['dashing']} table={d['table_idx']} gm={d['gamemode']} sub={d['scroll_y_subpx']} cube=0x{int(d['cube_data']):02X} cp_y={d['cp_y']} sy_raw=0x{int(d['scroll_y_raw']):04X} tgt_sy={d['tgt_scroll_y']} grav={d['gravity_mod']} vel_y={d['vel_y']}")
