import csv
from pathlib import Path

pf_trace = Path(r"C:\Users\kando\AppData\Local\Temp\famidash_pf_trace_untitled_20260517_152604_263.csv")
pf_debug = Path(r"C:\Users\kando\AppData\Local\Temp\famidash_pf_debug_untitled_20260517_150829.txt")

print("=== TRACE DATA (frames 1883-1890, wave mode only) ===")
pf_rows = {int(r['frame']): r for r in csv.DictReader(pf_trace.open(newline=''))}
for f in range(1883, 1891):
    if f in pf_rows:
        r = pf_rows[f]
        if int(r['mode']) == 6:
            y_fixed_int = int(r['Y_fixed'], 16)
            y_px_calc = y_fixed_int >> 8
            print(f"f={f:4d}: Y_px={r['Y_px']:>3s} Y_fixed={r['Y_fixed']:>6s} calc_Y={y_px_calc:>3d} VelY=0x{int(r['VelY_fixed'],16)&0xFFFF:04x}")

print("\n=== DEBUG LOG: Key state transitions ===")
debug_text = pf_debug.read_text(errors='replace')
lines = debug_text.splitlines()

for i, line in enumerate(lines):
    if '[PF f=1885]' in line and ('UP eject' in line or 'postMove' in line or '[REPLAY' in line):
        print(line)
    elif '[PF f=1886]' in line and ('UP eject' in line or 'postMove' in line or '[REPLAY' in line):
        print(line)
    elif '[PF f=1887]' in line and ('[REPLAY' in line or 'postMove' in line):
        print(line)
