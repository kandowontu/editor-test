import csv
from pathlib import Path

pf_trace = Path(r"C:\Users\kando\AppData\Local\Temp\famidash_pf_trace_untitled_20260517_152604_263.csv")
pf_rows = {int(r["frame"]): r for r in csv.DictReader(pf_trace.open(newline=""))}

print("=== Frame 1883-1890: Camera, Y_fixed, and Y_px calculations ===")
for f in range(1883, 1891):
    if f not in pf_rows:
        continue
    r = pf_rows[f]
    if int(r["mode"]) != 6:
        continue
    
    y_fixed = int(r["Y_fixed"], 16)
    cam_y_fixed = int(r["CamY_fixed"], 16)
    y_px_trace = int(r["Y_px"])
    
    # Recalculate yDisplayPx as per the code
    cam_y_high = cam_y_fixed >> 8
    delta_y_high = (y_fixed - cam_y_fixed) >> 8
    y_display_recalc = cam_y_high + delta_y_high
    
    # What it would be as simple Y_fixed >> 8
    y_simple = y_fixed >> 8
    
    print(f"f={f:4d}: Y_fixed=0x{y_fixed:06x}({y_simple:>3d}) CamY=0x{cam_y_fixed:06x}({cam_y_high:>3d}) YDisplay={y_display_recalc:>3d} (+8={y_display_recalc+8:>3d}) Trace_Y_px={y_px_trace:>3d}")
    if (y_display_recalc + 8) != y_px_trace:
        print(f"    ⚠️  Formula mismatch! {y_display_recalc + 8} != {y_px_trace}")
