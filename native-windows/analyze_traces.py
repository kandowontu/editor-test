import csv
import sys

mt_path = r"C:\Users\kando\AppData\Local\Temp\famidash_mesen_trace_cataclysm_20260520_171856_581.csv"
pf_path = r"C:\Users\kando\AppData\Local\Temp\famidash_pf_trace_cataclysm_20260520_165934_832.csv"

def analyze():
    # Load MT, skipping the first line (offset)
    with open(mt_path, "r") as f:
        # Skip the "nes_y_offset,528" line
        next(f)
        reader = csv.DictReader(f)
        mt_rows = list(reader)

    print(f"Loaded {len(mt_rows)} rows from MT.")
    
    # 1. MT Last 30 rows
    print("\n--- MT Last 30 Rows ---")
    header = ["sim_cursor", "px", "py", "gamemode", "death_pc", "death_ctx"]
    print("\t".join(header))
    for row in mt_rows[-30:]:
        print("\t".join([row.get(h, "") for h in header]))

    # 2. Unique values in death columns
    death_pcs = set(row["death_pc"] for row in mt_rows if row.get("death_pc"))
    death_ctxs = set(row["death_ctx"] for row in mt_rows if row.get("death_ctx"))
    print(f"\nUnique death_pc: {death_pcs}")
    print(f"Unique death_ctx: {death_ctxs}")

    # 3. Find transition from alive to dead in MT
    last_alive_idx = -1
    first_dead_idx = -1
    for i, row in enumerate(mt_rows):
        is_dead = bool(row.get("death_pc") or row.get("death_ctx"))
        if not is_dead:
            last_alive_idx = i
        elif first_dead_idx == -1:
            first_dead_idx = i

    target_sim_cursors = set()
    if last_alive_idx != -1:
        row = mt_rows[last_alive_idx]
        print(f"\nLast Alive MT: sim_cursor={row['sim_cursor']} px={row['px']} py={row['py']} mode={row['gamemode']}")
        target_sim_cursors.add(int(row['sim_cursor']))
    if first_dead_idx != -1:
        row = mt_rows[first_dead_idx]
        print(f"First Dead MT: sim_cursor={row['sim_cursor']} px={row['px']} py={row['py']} mode={row['gamemode']}")
        target_sim_cursors.add(int(row['sim_cursor']))
        
    # 4. From PF csv, print PF rows at the same frame numbers (frame = sim_cursor - 1)
    frames_to_find = sorted([sc - 1 for sc in target_sim_cursors])
    
    # Also want X_px,Y_px,GameMode,VelY_fixed,VelX_fixed from PF.
    # Note: PF header check showed: frame,X_fixed,Y_fixed,VelY_fixed,input,alive,X_px,Y_px,onGround,CamY_px,TgtCamY_px,mode,gravFlipped...
    # VelX_fixed is NOT in PF header. I will print VelY_fixed and mode.
    
    print(f"\nSearching PF for frames: {frames_to_find}")
    print("--- PF Rows for corresponding frames ---")
    with open(pf_path, "r") as f:
        pf_reader = csv.DictReader(f)
        pf_header = ["frame", "X_px", "Y_px", "mode", "VelY_fixed", "alive"]
        # Add any other available columns that might be useful
        available = [h for h in pf_header if h in pf_reader.fieldnames]
        print("\t".join(available))
        for row in pf_reader:
            if int(row["frame"]) in frames_to_find:
                print("\t".join([row.get(h, "") for h in available]))
            if int(row["frame"]) > max(frames_to_find) + 1:
                break

analyze()
