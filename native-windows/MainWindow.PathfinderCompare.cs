using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;

namespace FamidashEditor
{
    // Comparator: walks the most recent Mesen-side trace
    // (%TEMP%/famidash_mesen_trace.csv) against the pathfinder sim's replay
    // CSV (%TEMP%/famidash_replay.csv) and reports the first frame where the
    // observed (px,py) diverges from the predicted (sim_x,sim_y).
    //
    // Both files are written in PF world coordinates (hitbox-center). The Lua
    // logger records `sim_cursor` per ROM frame so lag-frames in Famidash
    // (where the ROM doesn't advance physics) don't mis-align the comparison.
    public partial class MainWindow
    {
        private sealed class SimRow
        {
            public int Frame;
            public int X;
            public int Y;
            public int A;
        }

        private sealed class RomRow
        {
            public int Attempt;
            public int RomFrame;
            public int SimCursor;
            public int Px;
            public int Py;
            public int ANext;
        }

        private static List<SimRow> LoadSimRows(string path, out int yOffset)
        {
            yOffset = 0;
            var rows = new List<SimRow>();
            foreach (var raw in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                if (raw.StartsWith("nes_y_offset", StringComparison.Ordinal))
                {
                    var p = raw.Split(',');
                    if (p.Length >= 2 && int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                        yOffset = v;
                    continue;
                }
                if (raw.StartsWith("frame", StringComparison.Ordinal)) continue;
                var parts = raw.Split(',');
                if (parts.Length < 4) continue;
                if (!int.TryParse(parts[0], out int f)) continue;
                if (!int.TryParse(parts[1], out int x)) continue;
                if (!int.TryParse(parts[2], out int y)) continue;
                if (!int.TryParse(parts[3], out int a)) continue;
                rows.Add(new SimRow { Frame = f, X = x, Y = y, A = a });
            }
            return rows;
        }

        private static List<RomRow> LoadRomRows(string path)
        {
            var rows = new List<RomRow>();
            int attempt = 0;
            foreach (var raw in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                if (raw.StartsWith("#", StringComparison.Ordinal))
                {
                    // Lua writes "# --- respawn ---" delimiters between attempts.
                    // Keep these attempt boundaries so we compare one run at a time.
                    attempt++;
                    continue;
                }
                if (raw.StartsWith("nes_y_offset", StringComparison.Ordinal)) continue;
                if (raw.StartsWith("rom_frame", StringComparison.Ordinal)) continue;
                var parts = raw.Split(',');
                if (parts.Length < 5) continue;
                if (!int.TryParse(parts[0], out int rf)) continue;
                if (!int.TryParse(parts[1], out int sc)) continue;
                // Mesen Lua can emit uint32-like text for wrapped signed values.
                if (!long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long pxRaw)) continue;
                int px = unchecked((int)(uint)pxRaw);
                if (!int.TryParse(parts[3], out int py)) continue;
                if (!int.TryParse(parts[4], out int an)) continue;
                rows.Add(new RomRow { Attempt = attempt, RomFrame = rf, SimCursor = sc, Px = px, Py = py, ANext = an });
            }
            return rows;
        }

        private void PathfinderCompareButton_Click(object sender, RoutedEventArgs e)
            => RunTraceCompare(silent: false);

        private void RefreshReplayButtonVisibility()
        {
            try
            {
                PathfinderReplayButton.Visibility =
                    System.IO.File.Exists(this.ReplayTempFile)
                        ? System.Windows.Visibility.Visible
                        : System.Windows.Visibility.Collapsed;
            }
            catch { }
        }

        private void RunTraceCompare(bool silent = false)
        {
            try
            {
                if (!File.Exists(this.ReplayTempFile))
                {
                    if (!silent) StatusText.Text = "Compare: no sim replay CSV on disk.";
                    string snapTs = System.DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                    string dir = System.IO.Path.GetTempPath();
                    string levelTag = this.CurrentLevelTag;
                    string mtSnap = System.IO.Path.Combine(dir, $"famidash_mesen_trace_{levelTag}_{snapTs}.csv");
                    string rpSnap = System.IO.Path.Combine(dir, $"famidash_replay_{levelTag}_{snapTs}.csv");
                    if (!silent) StatusText.Text = "Compare: no Mesen trace on disk. Run \"Replay in Mesen\" first.";
                    return;
                }

                // Snapshot the live trace files into timestamped siblings so each
                // compare run is preserved for later analysis. Timestamp matches
                // the format used by famidash_pf_debug_*.txt for easy pairing.
                try
                {
                    string snapTs = System.DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                    string dir = System.IO.Path.GetTempPath();
                    string levelTag = this.CurrentLevelTag;
                    string mtSnap = System.IO.Path.Combine(dir, $"famidash_mesen_trace_{levelTag}_{snapTs}.csv");
                    string rpSnap = System.IO.Path.Combine(dir, $"famidash_replay_{levelTag}_{snapTs}.csv");
                    File.Copy(this.MesenTraceFile, mtSnap, overwrite: true);
                    File.Copy(this.ReplayTempFile, rpSnap, overwrite: true);
                }
                catch { /* best-effort archival */ }

                var sim = LoadSimRows(this.ReplayTempFile, out int yOffset);
                var romAll = LoadRomRows(this.MesenTraceFile);

                if (sim.Count == 0 || romAll.Count == 0)
                {
                    StatusText.Text = $"Compare: empty trace (sim={sim.Count}, rom={romAll.Count}).";
                    return;
                }

                // Compare against a single attempt, not a merged multi-respawn stream.
                // We pick the attempt that progressed furthest into the sim path.
                var attemptInfos = romAll
                    .GroupBy(r => r.Attempt)
                    .Select(g => new
                    {
                        Attempt = g.Key,
                        Rows = g.OrderBy(r => r.RomFrame).ToList(),
                        MaxCursor = g.Max(r => r.SimCursor),
                        ValidCount = g.Count(r => (r.SimCursor - 1) >= 0 && (r.SimCursor - 1) < sim.Count)
                    })
                    .Where(a => a.ValidCount > 0)
                    .OrderByDescending(a => a.MaxCursor)
                    .ThenByDescending(a => a.Attempt)
                    .ToList();

                if (attemptInfos.Count == 0)
                {
                    StatusText.Text = "Compare: no valid ROM rows matched replay range.";
                    return;
                }

                var picked = attemptInfos[0];
                var rom = picked.Rows;

                // Walk ROM frames; for each, compare against sim row at the
                // cursor index the Lua reported. Sim rows are 1-indexed in the
                // Lua side because tables there start at 1.
                int firstDivergeRom = -1;
                int firstDivergeSim = -1;
                int dxAtDiverge = 0;
                int dyAtDiverge = 0;
                int maxDx = 0, maxDy = 0;
                int maxDxRom = -1, maxDyRom = -1;
                int compared = 0;
                int aCompared = 0;
                int aMatchSame = 0;
                int aMatchNext = 0;
                const int TolPx = 1; // sub-pixel rounding tolerance
                var comparedRows = new List<(RomRow r, SimRow s, int dx, int dy)>();

                int groundRowsToReserve = (groundBitmap != null && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                string DescribeCollisionAt(string who, int px, int py)
                {
                    int tileX = px / SharedPhysics.TILE;
                    int tileY = py / SharedPhysics.TILE;
                    int tileArrayY = tileY + groundRowsToReserve;
                    int localX = ((px % SharedPhysics.TILE) + SharedPhysics.TILE) % SharedPhysics.TILE;
                    int localY = ((py % SharedPhysics.TILE) + SharedPhysics.TILE) % SharedPhysics.TILE;

                    if (tileX < 0 || tileX >= mapWidth)
                        return $"  {who}: p=({px},{py}) tile=({tileX},{tileY}) out_of_bounds_x";

                    if (tileArrayY < 0)
                        return $"  {who}: p=({px},{py}) tile=({tileX},{tileY}) arrayY={tileArrayY} above_map";

                    if (tileArrayY >= mapHeight)
                        return $"  {who}: p=({px},{py}) tile=({tileX},{tileY}) arrayY={tileArrayY} implicit_ground col={MetatileCollision.COL_ALL} local=({localX},{localY})";

                    int idx = tileArrayY * mapWidth + tileX;
                    if (idx < 0 || idx >= tiles.Length)
                        return $"  {who}: p=({px},{py}) tile=({tileX},{tileY}) arrayY={tileArrayY} bad_index={idx}";

                    int tid = tiles[idx];
                    int mappedTid = SharedPhysics.MapTileForCollision(tid);
                    var col = MetatileCollisionTable.GetCollision((byte)mappedTid);
                    bool kills = MetatileCollisionTable.TileKillsAtPixel(col, localX, localY);
                    return $"  {who}: p=({px},{py}) tile=({tileX},{tileY}) arrayY={tileArrayY} tid=0x{tid:X2} mapped=0x{mappedTid:X2} col={col} local=({localX},{localY}) kills={kills}";
                }

                var preview = new StringBuilder();
                preview.AppendLine("rom_f, sim_f, rom_xy, sim_xy, dx, dy, a_next, sim_a");
                var fullRows = new StringBuilder();
                fullRows.AppendLine("rom_f, sim_f, rom_xy, sim_xy, dx, dy, a_next, sim_a");

                // Frame alignment: Lua's `cursor` is 1-based and replay[1] =
                // PathPoints[0], the first recorded post-physics position after
                // Pathfinder's NES intro-freeze pre-step.  Lua uses that first
                // X only as a startup latch, then increments the cursor once per
                // NES-local physics movement.  NES cursor=N therefore matches
                // PF.PathPoints[N-1] (single Lua-base offset).
                foreach (var r in rom)
                {
                    if (r.SimCursor < 1) continue; // skip pre-spawn rows
                    int simIdx = r.SimCursor - 1; // Lua 1-based -> C# 0-based
                    if (simIdx < 0 || simIdx >= sim.Count) continue;
                    var s = sim[simIdx];
                    int dx = r.Px - s.X;
                    int dy = r.Py - s.Y;
                    compared++;
                    comparedRows.Add((r, s, dx, dy));

                    aCompared++;
                    if (r.ANext == s.A) aMatchSame++;
                    if ((simIdx + 1) < sim.Count && r.ANext == sim[simIdx + 1].A) aMatchNext++;

                    if (Math.Abs(dx) > Math.Abs(maxDx)) { maxDx = dx; maxDxRom = r.RomFrame; }
                    if (Math.Abs(dy) > Math.Abs(maxDy)) { maxDy = dy; maxDyRom = r.RomFrame; }

                    if (firstDivergeRom < 0 && (Math.Abs(dx) > TolPx || Math.Abs(dy) > TolPx))
                    {
                        firstDivergeRom = r.RomFrame;
                        firstDivergeSim = s.Frame;
                        dxAtDiverge = dx;
                        dyAtDiverge = dy;
                    }

                    if (preview.Length < 8000)
                    {
                        preview.AppendLine($"{r.RomFrame},{s.Frame},({r.Px},{r.Py}),({s.X},{s.Y}),{dx},{dy},{r.ANext},{s.A}");
                    }
                    fullRows.AppendLine($"{r.RomFrame},{s.Frame},({r.Px},{r.Py}),({s.X},{s.Y}),{dx},{dy},{r.ANext},{s.A}");
                }

                // Also detect early ROM termination (death): ROM trace stopped
                // well before the sim path completed.
                int romLast = rom[rom.Count - 1].RomFrame;
                int simCursorAtEnd = rom[rom.Count - 1].SimCursor;
                bool romEndedEarly = simCursorAtEnd < sim.Count - 4;

                (RomRow? r, SimRow? s, int dx, int dy) fatalDiverge = (null, null, 0, 0);
                if (romEndedEarly)
                {
                    foreach (var row in comparedRows)
                    {
                        if (row.r.SimCursor >= simCursorAtEnd - 1 && (Math.Abs(row.dx) > TolPx || Math.Abs(row.dy) > TolPx))
                        {
                            fatalDiverge = (row.r, row.s, row.dx, row.dy);
                            break;
                        }
                    }
                }

                (RomRow? r, SimRow? s, int dx, int dy) maxDyRow = (null, null, 0, 0);
                foreach (var row in comparedRows)
                {
                    if (row.r.RomFrame == maxDyRom)
                    {
                        maxDyRow = (row.r, row.s, row.dx, row.dy);
                        break;
                    }
                }

                var summary = new StringBuilder();
                summary.AppendLine("=== Pathfinder vs Mesen trace comparison ===");
                summary.AppendLine($"sim rows:   {sim.Count}");
                summary.AppendLine($"rom rows:   {rom.Count}  (last rom_frame={romLast}, sim_cursor={simCursorAtEnd})");
                summary.AppendLine($"attempts:   {attemptInfos.Count} parsed; using attempt #{picked.Attempt} (max sim_cursor={picked.MaxCursor}, valid rows={picked.ValidCount})");
                summary.AppendLine($"compared:   {compared}");
                summary.AppendLine($"nes_y_off:  {yOffset}");
                if (aCompared > 0)
                {
                    double samePct = (100.0 * aMatchSame) / aCompared;
                    double nextPct = (100.0 * aMatchNext) / aCompared;
                    summary.AppendLine($"A phase:    same={aMatchSame}/{aCompared} ({samePct:F1}%), next={aMatchNext}/{aCompared} ({nextPct:F1}%)");
                }
                summary.AppendLine();
                if (firstDivergeRom < 0)
                    summary.AppendLine("No position divergence detected (within ±1 px tolerance).");
                else
                    summary.AppendLine($"FIRST DIVERGE: rom_frame={firstDivergeRom}, sim_frame={firstDivergeSim}, dx={dxAtDiverge}, dy={dyAtDiverge}");
                summary.AppendLine($"MAX |dx|={Math.Abs(maxDx)} at rom_frame={maxDxRom}");
                summary.AppendLine($"MAX |dy|={Math.Abs(maxDy)} at rom_frame={maxDyRom}");
                if (romEndedEarly)
                    summary.AppendLine($"ROM ended early: stopped at sim_cursor {simCursorAtEnd} of {sim.Count} (likely death).");
                if (fatalDiverge.r != null && fatalDiverge.s != null)
                {
                    summary.AppendLine($"FIRST FATAL DIVERGE: rom_frame={fatalDiverge.r.RomFrame}, sim_frame={fatalDiverge.s.Frame}, dx={fatalDiverge.dx}, dy={fatalDiverge.dy}");
                    summary.AppendLine("Fatal divergence collision samples:");
                    summary.AppendLine(DescribeCollisionAt("ROM", fatalDiverge.r.Px, fatalDiverge.r.Py));
                    summary.AppendLine(DescribeCollisionAt("SIM", fatalDiverge.s.X, fatalDiverge.s.Y));
                    summary.AppendLine();
                }
                if (maxDyRow.r != null && maxDyRow.s != null)
                {
                    summary.AppendLine($"MAX-DY collision samples at rom_frame={maxDyRow.r.RomFrame}:");
                    summary.AppendLine(DescribeCollisionAt("ROM", maxDyRow.r.Px, maxDyRow.r.Py));
                    summary.AppendLine(DescribeCollisionAt("SIM", maxDyRow.s.X, maxDyRow.s.Y));
                    summary.AppendLine();
                }

                // ROM-death scene: when the ROM ended early but PF and Mesen
                // were tracking perfectly (no fatal divergence flagged), dump
                // collision samples at hitbox corners of the last few ROM frames
                // so we can see what tile killed the ROM that the sim shrugs off.
                if (romEndedEarly && comparedRows.Count > 0)
                {
                    summary.AppendLine("ROM death scene (last 6 frames before ROM trace ended):");
                    int startIdx = Math.Max(0, comparedRows.Count - 6);
                    for (int i = startIdx; i < comparedRows.Count; i++)
                    {
                        var row = comparedRows[i];
                        int px = row.r.Px;
                        int py = row.r.Py;
                        // Cube hitbox: 15x15 starting at (px, py).  Sample the
                        // four corners + center so any half-slab/spike interaction
                        // on the corresponding tile is visible.
                        summary.AppendLine($"  rom_f={row.r.RomFrame} sim_f={row.s.Frame} pos=({px},{py})");
                        summary.AppendLine(DescribeCollisionAt("    TL ", px,      py));
                        summary.AppendLine(DescribeCollisionAt("    TR ", px + 15, py));
                        summary.AppendLine(DescribeCollisionAt("    CTR", px + 7,  py + 7));
                        summary.AppendLine(DescribeCollisionAt("    BL ", px,      py + 15));
                        summary.AppendLine(DescribeCollisionAt("    BR ", px + 15, py + 15));
                    }
                    summary.AppendLine();

                    // Tile-region dump around death position so we can see what
                    // is actually loaded in tiles[] (not just the cube hitbox).
                    // Wide window: full vertical map height, ±7 tiles horizontally.
                    {
                        var last = comparedRows[comparedRows.Count - 1];
                        int centerTileX = (last.r.Px + 7) / SharedPhysics.TILE;
                        int centerTileY = (last.r.Py + 7) / SharedPhysics.TILE;
                        summary.AppendLine($"Tile region around ROM death pos=({last.r.Px},{last.r.Py}) center_tile=({centerTileX},{centerTileY}) groundRowsToReserve={groundRowsToReserve} mapW={mapWidth} mapH={mapHeight}");
                        const int WX = 7; // ±7 cols (15 wide)
                        summary.Append("    tileX:");
                        for (int dx = -WX; dx <= WX; dx++)
                            summary.Append($"  {centerTileX + dx,4}");
                        summary.AppendLine();
                        // Walk every map row (aY=0..mapHeight-1) so we can see any
                        // hazard/COL_TOP block in the column regardless of cube Y.
                        for (int aY = 0; aY < mapHeight; aY++)
                        {
                            int ty = aY - groundRowsToReserve;
                            string yLabel = $"y={ty,3}(aY={aY,2})";
                            summary.Append($"    {yLabel}:");
                            bool anyTile = false;
                            for (int dx = -WX; dx <= WX; dx++)
                            {
                                int tx = centerTileX + dx;
                                if (tx < 0 || tx >= mapWidth)
                                {
                                    summary.Append("    --");
                                    continue;
                                }
                                int idx = aY * mapWidth + tx;
                                if (idx < 0 || idx >= tiles.Length) { summary.Append("    !!"); continue; }
                                int tid = tiles[idx];
                                if (tid < 0) summary.Append("    ..");
                                else { summary.Append($"  0x{tid:X2}"); anyTile = true; }
                            }
                            summary.AppendLine();
                            // Skip empty rows after we've shown the first/last few?
                            // No — keep them all, level is short anyway.
                            _ = anyTile;
                        }
                        summary.AppendLine();

                        // Also: show ALL non-empty tiles in cube's death-column ±2
                        // and their collision type, to spot hazards at any Y.
                        summary.AppendLine($"Non-empty tiles in columns {centerTileX-2}..{centerTileX+2}:");
                        for (int tx = centerTileX - 2; tx <= centerTileX + 2; tx++)
                        {
                            if (tx < 0 || tx >= mapWidth) continue;
                            for (int aY = 0; aY < mapHeight; aY++)
                            {
                                int idx = aY * mapWidth + tx;
                                if (idx < 0 || idx >= tiles.Length) continue;
                                int tid = tiles[idx];
                                if (tid < 0) continue;
                                int mappedTid = SharedPhysics.MapTileForCollision(tid);
                                var col = MetatileCollisionTable.GetCollision((byte)mappedTid);
                                int ty = aY - groundRowsToReserve;
                                summary.AppendLine($"    tile=({tx},{ty}) aY={aY} tid=0x{tid:X2} mapped=0x{mappedTid:X2} col={col}");
                            }
                        }
                        summary.AppendLine();
                    }
                }
                summary.AppendLine();
                summary.AppendLine("First rows of per-frame comparison (rom_f,sim_f,rom_xy,sim_xy,dx,dy,a_next,sim_a):");
                summary.Append(preview);

                var fullReport = new StringBuilder();
                fullReport.Append(summary);
                fullReport.AppendLine();
                fullReport.AppendLine("Full per-frame comparison (rom_f,sim_f,rom_xy,sim_xy,dx,dy,a_next,sim_a):");
                fullReport.Append(fullRows);

                // Write the full report to %TEMP% with a timestamp so each run is preserved.
                string ts = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string reportPath = Path.Combine(Path.GetTempPath(), $"famidash_trace_compare_{ts}.txt");
                try { File.WriteAllText(reportPath, fullReport.ToString()); } catch { }

                string statusLine;
                if (firstDivergeRom < 0)
                    statusLine = $"Compare: no divergence ({compared} frames). Report -> {reportPath}";
                else
                    statusLine = $"Compare: DIVERGE at rom_frame {firstDivergeRom} (sim {firstDivergeSim}) dx={dxAtDiverge} dy={dyAtDiverge}. Report -> {reportPath}";
                StatusText.Text = statusLine;

                if (!silent)
                {
                    // Show a summary dialog so the user sees the breakdown immediately.
                    MessageBox.Show(summary.ToString(),
                        "Trace comparison",
                        MessageBoxButton.OK,
                        firstDivergeRom < 0 ? MessageBoxImage.Information : MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Compare error: {ex.Message}";
            }
        }
    }
}
