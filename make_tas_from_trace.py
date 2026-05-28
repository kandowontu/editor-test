import csv
from pathlib import Path

src = Path(r"C:\Users\kando\AppData\Local\Temp\famidash_pf_trace_untitled_20260517_152604_263.csv")
out = Path(r"C:\Editor Test\silentcircles_baseline_trace.tas")

IDLE = "|..|........|........"
PRESS = "|A.|........|........"

rows = sorted(csv.DictReader(src.open(newline="")), key=lambda r: int(r["frame"]))
lines = []
for r in rows:
    inp = int(r["input"])
    lines.append(PRESS if inp else IDLE)

out.write_text("\n".join(lines) + "\n")
print(f"wrote {len(lines)} lines to {out}")
print(f"jump frames: {sum(1 for l in lines if l != IDLE)}")
