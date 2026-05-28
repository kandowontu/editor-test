import csv

mt_path = r"C:\Users\kando\AppData\Local\Temp\famidash_mesen_trace_cataclysm_20260520_171856_581.csv"
pf_path = r"C:\Users\kando\AppData\Local\Temp\famidash_pf_trace_cataclysm_20260520_165934_832.csv"

def analyze():
    # Load MT
    mt_rows = []
    with open(mt_path, "r") as f:
        next(f)  # skip nes_y_offset
        reader = csv.DictReader(f)
        mt_rows = list(reader)

    # Load PF
    pf_rows = []
    with open(pf_path, "r") as f:
        reader = csv.DictReader(f)
        pf_rows = list(reader)

    pf_by_frame = {int(r["frame"]): r for r in pf_rows if r.get("frame") is not None}

    print("--- MT GameMode Transitions ---")
    mt_transitions = []
    prev_mode = None
    for i, row in enumerate(mt_rows):
        mode = row.get("gamemode")
        # Handle cases where gamemode or sim_cursor might be empty/None
        if not row.get("sim_cursor") or mode is None:
            continue
            
        if prev_mode is not None and mode != prev_mode:
            print(f"sim_cursor={row['sim_cursor']}, {prev_mode} -> {mode}, px={row['px']}, py={row['py']}")
            mt_transitions.append(i)
        prev_mode = mode

    print("\n--- PF Mode Transitions ---")
    pf_transitions = []
    prev_mode = None
    for i, row in enumerate(pf_rows):
        mode = row.get("mode")
        if not row.get("frame") or mode is None:
            continue
            
        if prev_mode is not None and mode != prev_mode:
            print(f"frame={row['frame']}, {prev_mode} -> {mode}, X_px={row['X_px']}, Y_px={row['Y_px']}")
            pf_transitions.append(i)
        prev_mode = mode

    print("\n--- MT transitions vs PF state ---")
    first_disagreement_si = -1
    for idx in mt_transitions:
        mt_row = mt_rows[idx]
        sc_str = mt_row.get('sim_cursor')
        if not sc_str: continue
        sc = int(sc_str)
        target_frame = sc - 1
        
        pf_row = pf_by_frame.get(target_frame)
        if not pf_row:
             pf_row = pf_by_frame.get(target_frame - 1)
        
        if pf_row:
            mt_mode = mt_row['gamemode']
            pf_mode = pf_row['mode']
            status = "MATCH" if mt_mode == pf_mode else "MISMATCH"
            print(f"MT sim_cursor {sc} mode={mt_mode} | PF frame {pf_row['frame']} mode={pf_mode} -> {status}")
            if status == "MISMATCH" and first_disagreement_si == -1:
                first_disagreement_si = idx
        else:
            print(f"MT sim_cursor {sc} mode={mt_row['gamemode']} | PF frame {target_frame} NOT FOUND")

    # If no mismatch found in transitions, look for general mismatch
    if first_disagreement_si == -1:
        print("\nChecking all frames for first mode disagreement...")
        for i, mt_r in enumerate(mt_rows):
            sc_str = mt_r.get('sim_cursor')
            if not sc_str: continue
            sc = int(sc_str)
            tf = sc - 1
            pf_r = pf_by_frame.get(tf)
            if pf_r and mt_r['gamemode'] != pf_r['mode']:
                first_disagreement_si = i
                print(f"First general mismatch at MT sim_cursor {sc}: MT mode {mt_r['gamemode']} != PF mode {pf_r['mode']}")
                break

    if first_disagreement_si != -1:
        print(f"\n--- Side-by-side around first disagreement (MT index {first_disagreement_si}) ---")
        start = max(0, first_disagreement_si - 10)
        end = min(len(mt_rows), first_disagreement_si + 10)
        
        header = f"{'MT sim':<8} {'MT mode':<10} {'MT px':<8} {'MT py':<8} | {'PF frame':<8} {'PF mode':<10} {'PF X':<8} {'PF Y':<8}"
        print(header)
        print("-" * len(header))
        
        for i in range(start, end):
            mt_r = mt_rows[i]
            sc_str = mt_r.get('sim_cursor')
            if not sc_str: 
                print(f"{'N/A':<8} | {'N/A':<8}")
                continue
            sc = int(sc_str)
            tf = sc - 1
            pf_r = pf_by_frame.get(tf, {})
            
            mt_str = f"{mt_r['sim_cursor']:<8} {mt_r['gamemode']:<10} {mt_r['px']:<8} {mt_r['py']:<8}"
            pf_str = f"{pf_r.get('frame',''):<8} {pf_r.get('mode',''):<10} {pf_r.get('X_px',''):<8} {pf_r.get('Y_px',''):<8}"
            print(f"{mt_str} | {pf_str}")

analyze()
