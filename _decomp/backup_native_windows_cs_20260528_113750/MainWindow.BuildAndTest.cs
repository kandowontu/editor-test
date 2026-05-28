using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace FamidashEditor
{
    public partial class MainWindow
    {
        // Hard-coded paths relative to the sim-famidash root.
        private const string SimFamidashFolder     = @"C:\Editor Test\sim-famidash";
        private const string SimLevelsFolder       = SimFamidashFolder + @"\LEVELS\level data\lvlset_D";
        private const string SimLevelMetadata      = SimFamidashFolder + @"\LEVELS\metadata\lvlset_D_metadata.json5";
        private const string SimMusicMetadata      = SimFamidashFolder + @"\MUSIC\metadata\lvlset_D_metadata.json5";
        private const string SimBuildBat           = SimFamidashFolder + @"\1BigSimExport.bat";
        private const string SimOutputRom          = SimFamidashFolder + @"\Famidash.nes";

        // -------------------------------------------------------------------------
        // Menu handler
        // -------------------------------------------------------------------------
        private void MenuBuildAndTest_Click(object? sender, RoutedEventArgs e)
        {
            // Run async so the UI stays responsive during the build.
            _ = BuildAndTestAsync(useReplay: false, skipConfirm: false);
        }

        // Public entry point used by both the menu (useReplay=false) and the
        // pathfinder "Replay in Mesen" button (useReplay=true). When useReplay
        // is true the Lua overlay script gets the input-injection block and
        // the confirm dialog is suppressed.
        internal Task BuildAndTestForReplayAsync()
        {
            return BuildAndTestAsync(useReplay: true, skipConfirm: true);
        }

        private async Task BuildAndTestAsync(bool useReplay, bool skipConfirm)
        {
            string tmxPath = GetCurrentTmxPath();
            if (string.IsNullOrEmpty(tmxPath) || !File.Exists(tmxPath))
            {
                MessageBox.Show(this, "No level is currently open.", "Build and Test", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // --- Collect level info ---
            string levelName = Path.GetFileNameWithoutExtension(tmxPath).ToLowerInvariant();

            // Get the raw song name exactly as shown in the FamiTrack combo.
            string rawSong = "";
            try
            {
                if (FamiTrackCombo?.SelectedItem is ComboBoxItem cbi)
                    rawSong = cbi.Content?.ToString() ?? "";
                if (string.IsNullOrEmpty(rawSong) && FamiTrackCombo?.SelectedItem != null)
                    rawSong = FamiTrackCombo.SelectedItem.ToString() ?? "";
            }
            catch { }
            // Fallback: read SelectedSong from the current tab's FileTabData.
            if (string.IsNullOrEmpty(rawSong))
            {
                try
                {
                    if (currentFileIndex >= 0 && currentFileIndex < openFiles.Count)
                        rawSong = openFiles[currentFileIndex].SelectedSong ?? "";
                }
                catch { }
            }

            // Validate song
            if (string.IsNullOrEmpty(rawSong))
            {
                MessageBox.Show(this, "No music track is selected for this level.\nPlease select a song before building.", "Build and Test", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // --- Build the JSON segment (same format as the Export JSON function) ---
            string jsonSegment;
            try
            {
                jsonSegment = BuildLevelJsonSegment(levelName, rawSong);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Failed to generate level JSON:\n" + ex.Message, "Build and Test", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // --- Confirm ---
            if (!skipConfirm)
            {
                var confirm = MessageBox.Show(
                    this,
                    $"Build and test level: {levelName}\nSong: {rawSong}\n\nThis will:\n" +
                    "  A) Copy the TMX into sim-famidash\n" +
                    "  B) Update the level metadata\n" +
                    "  C) Update the music metadata\n" +
                    "  D) Run 1BigSimExport.bat\n" +
                    "  E) Open the ROM in Mesen\n\nContinue?",
                    "Build and Test",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (confirm != MessageBoxResult.Yes) return;
            }

            if (StatusText != null) StatusText.Text = "Build and Test: preparing...";

            try
            {
                // --- A) Copy TMX ---
                Directory.CreateDirectory(SimLevelsFolder);
                string destTmx = Path.Combine(SimLevelsFolder, Path.GetFileName(tmxPath));
                File.Copy(tmxPath, destTmx, overwrite: true);

                // --- B) Update level metadata ---
                UpdateLevelMetadata(jsonSegment);

                // --- C) Update music metadata ---
                UpdateMusicMetadata(rawSong);

                // --- D) Run 1BigSimExport.bat ---
                if (StatusText != null) StatusText.Text = "Build and Test: building ROM...";

                bool buildOk = await RunBuildBatAsync();
                if (!buildOk)
                {
                    MessageBox.Show(this, "1BigSimExport.bat failed or timed out. Check the console output.", "Build and Test", MessageBoxButton.OK, MessageBoxImage.Error);
                    if (StatusText != null) StatusText.Text = "Build and Test: build failed.";
                    return;
                }

                // --- E) Open ROM in Mesen ---
                if (!File.Exists(SimOutputRom))
                {
                    MessageBox.Show(this, $"Build completed but ROM not found at:\n{SimOutputRom}", "Build and Test", MessageBoxButton.OK, MessageBoxImage.Error);
                    if (StatusText != null) StatusText.Text = "Build and Test: ROM not found.";
                    return;
                }

                OpenRomInMesen(SimOutputRom, useReplay);
                if (StatusText != null)
                    StatusText.Text = useReplay
                        ? $"Replay: launched {levelName} in Mesen with injected pathfinder inputs."
                        : $"Build and Test: launched {levelName} in Mesen.";
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Build and Test failed:\n" + ex.Message, "Build and Test", MessageBoxButton.OK, MessageBoxImage.Error);
                if (StatusText != null) StatusText.Text = "Build and Test: error.";
            }
        }

        // -------------------------------------------------------------------------
        // A) Build the JSON metadata segment
        // -------------------------------------------------------------------------
        private string BuildLevelJsonSegment(string levelName, string rawSong)
        {
            var sb = new StringBuilder();

            // Convert rawSong → songID (same normalisation as ExportShiftJson)
            string songId = "";
            if (!string.IsNullOrEmpty(rawSong))
            {
                string s = rawSong.ToLowerInvariant();
                s = Regex.Replace(s, @"\s+", "_");
                s = Regex.Replace(s, @"[^a-z0-9_]", "");
                songId = "song_" + s;
            }

            // Helper accessors
            string GetString(string name, string def = "")
            {
                // Try private field first (e.g. loadedBlockSet), then public property.
                var f = GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null) return (f.GetValue(this) as string) ?? def;
                var p = GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                return (p?.GetValue(this) as string) ?? def;
            }
            int? GetNullableInt(string name)
            {
                var p = GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                var v = p?.GetValue(this);
                if (v == null) return null;
                try { return Convert.ToInt32(v); } catch { return null; }
            }
            bool GetBool(string name)
            {
                var p = GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                var v = p?.GetValue(this);
                if (v == null) return false;
                try { return Convert.ToBoolean(v); } catch { return false; }
            }

            // Text
            string upper = GetString("LoadedStartingUpperText", "").ToUpperInvariant();
            string lower = GetString("LoadedStartingLowerText", "").ToUpperInvariant();

            // Sets – read the live private fields that track the current loaded values
            string deco = GetString("loadedDecoSet", "DECO1");
            string blockSet = GetString("loadedBlockSet", "BLOCKSA");
            string spikeSet = GetString("loadedSpikeSet", "SPIKESA");
            string blockLetter = blockSet.StartsWith("BLOCKS", StringComparison.OrdinalIgnoreCase) ? blockSet.Substring(6) : blockSet;
            string spikeLetter = spikeSet.StartsWith("SPIKES", StringComparison.OrdinalIgnoreCase) ? spikeSet.Substring(6) : spikeSet;
            blockLetter = blockLetter.ToUpperInvariant();
            spikeLetter = spikeLetter.ToUpperInvariant();

            // Difficulty / stars
            string difficulty = "AUTO";
            var diffVal = GetNullableInt("LoadedStartingDifficulty");
            if (diffVal.HasValue)
            {
                int val = Math.Clamp(diffVal.Value, 0, 13);
                string[] normalDiffs = { "EASY", "NORMAL", "HARD", "HARDER", "INSANE", "DEMON", "AUTO" };
                string[] demonDiffs  = { "EASYDEMON", "MEDIUMDEMON", "HARDDEMON", "INSANEDEMON", "EXTREMEDEMON", "IMPOSSIBLEDEMON", "GRANDPADEMON" };
                difficulty = (val < 7) ? normalDiffs[val] : demonDiffs[val - 7];
            }
            int stars = GetNullableInt("LoadedStartingStars") ?? 3;

            // Game mode / speed
            int startingGameMode = GetNullableInt("LoadedStartingGameMode") ?? 0;
            int startingSpeedJson = 0;
            try { int ui = GetNullableInt("LoadedStartingSpeedUiIndex") ?? 1; startingSpeedJson = (ui == 0) ? 1 : (ui == 1) ? 0 : ui; } catch { }

            // Max fall speed
            int maxFallSpeed = GetNullableInt("LoadedMaxFallSpeed") ?? 6;
            if (maxFallSpeed == 0) maxFallSpeed = 6;

            // Colors
            int? bgColor     = GetNullableInt("LoadedStartingBackgroundColor");
            int? groundColor = GetNullableInt("LoadedStartingGroundColor");

            // Optional Y positions
            int? spawnHi   = GetNullableInt("LoadedSpawnYPositionHi");
            int? spawnLow  = GetNullableInt("LoadedSpawnYPositionLow");
            int? scrollHi  = GetNullableInt("LoadedScrollYPositionHi");
            int? scrollLow = GetNullableInt("LoadedScrollYPositionLow");

            // Parallax / force-platformer
            bool noParallax = GetBool("NoParallaxBg");
            bool forcePlatformer = false;
            try { var v = GetType().GetProperty("LoadedForcePlatformer", BindingFlags.Public | BindingFlags.Instance)?.GetValue(this); if (v is bool b) forcePlatformer = b; } catch { }

            // Sprite offsets
            Dictionary<(int, int), List<(int, int)>>? groupedOffsets = null;
            try
            {
                var offsets = GetSpriteOffsets();
                if (offsets != null && offsets.Count > 0)
                {
                    groupedOffsets = new Dictionary<(int, int), List<(int, int)>>();
                    foreach (var kvp in offsets)
                    {
                        int posIndex = kvp.Key;
                        int x = posIndex % MapWidth;
                        int y = posIndex / MapWidth;
                        var key = (kvp.Value.offsetX, kvp.Value.offsetY);
                        if (!groupedOffsets.ContainsKey(key)) groupedOffsets[key] = new List<(int, int)>();
                        groupedOffsets[key].Add((x, y));
                    }
                }
            }
            catch { }

            // Build
            sb.AppendLine("\t\t{");
            sb.AppendLine($"\t\t\tlevel: \"{levelName}\",");
            if (!string.IsNullOrEmpty(upper)) sb.AppendLine($"\t\t\tupperText: \"{upper}\",");
            if (!string.IsNullOrEmpty(lower)) sb.AppendLine($"\t\t\tlowerText: \"{lower}\",");
            sb.AppendLine($"\t\t\tdecoType: \"{deco}\",");
            sb.AppendLine($"\t\t\tspikeSet: \"{spikeLetter}\",");
            sb.AppendLine($"\t\t\tblockSet: \"{blockLetter}\",");
            sb.AppendLine($"\t\t\tsawSet: \"A\",");
            sb.AppendLine($"\t\t\tdifficulty: \"{difficulty}\",");
            sb.AppendLine($"\t\t\tstars: {stars},");
            if (!string.IsNullOrEmpty(songId)) sb.AppendLine($"\t\t\tsongID: \"{songId}\",");
            sb.AppendLine($"\t\t\tstartingGameMode: {startingGameMode},");
            sb.AppendLine($"\t\t\tstartingSpeed: {startingSpeedJson},");
            if (maxFallSpeed != 6) sb.AppendLine($"\t\t\tmaxFallSpeed: 0x{maxFallSpeed:X2},");
            sb.AppendLine($"\t\t\tstartingBackgroundColor: 0x{(bgColor ?? 0x12):X2},");
            sb.AppendLine($"\t\t\tstartingGroundColor: 0x{(groundColor ?? 0x02):X2},");
            if (spawnHi.HasValue)  sb.AppendLine($"\t\t\tspawnYPositionHi: 0x{spawnHi.Value:X2},");
            if (spawnLow.HasValue) sb.AppendLine($"\t\t\tspawnYPositionLow: 0x{spawnLow.Value:X2},");
            if (scrollHi.HasValue) sb.AppendLine($"\t\t\tscrollYPositionHi: 0x{scrollHi.Value:X2},");
            if (scrollLow.HasValue) sb.AppendLine($"\t\t\tscrollYPositionLow: 0x{scrollLow.Value:X2},");
            if (forcePlatformer) sb.AppendLine("\t\t\tforcePlatformer: true,");
            if (noParallax)      sb.AppendLine("\t\t\tparallaxDisable: true,");

            if (groupedOffsets != null && groupedOffsets.Count > 0)
            {
                sb.AppendLine("\t\t\tobjectOffsets: [");
                bool firstGroup = true;
                foreach (var group in groupedOffsets)
                {
                    if (!firstGroup) sb.AppendLine(",");
                    firstGroup = false;
                    sb.AppendLine("\t\t\t\t{");
                    var coords = group.Value;
                    if (coords.Count == 1)
                    {
                        sb.AppendLine($"\t\t\t\t\tcoordinates: [{coords[0].Item1}, {coords[0].Item2}],");
                    }
                    else
                    {
                        sb.AppendLine("\t\t\t\t\tcoordinates: [");
                        for (int i = 0; i < coords.Count; i++)
                            sb.AppendLine($"\t\t\t\t\t\t[{coords[i].Item1}, {coords[i].Item2}]{(i < coords.Count - 1 ? "," : "")}");
                        sb.AppendLine("\t\t\t\t\t],");
                    }
                    if (group.Key.Item2 != 0)
                    {
                        string ys = group.Key.Item2 >= 0 ? $"+{group.Key.Item2}" : group.Key.Item2.ToString();
                        sb.AppendLine($"\t\t\t\t\toffsetY: {ys}{(group.Key.Item1 != 0 ? "," : "")}");
                    }
                    if (group.Key.Item1 != 0)
                    {
                        string xs = group.Key.Item1 >= 0 ? $"+{group.Key.Item1}" : group.Key.Item1.ToString();
                        sb.AppendLine($"\t\t\t\t\toffsetX: {xs}");
                    }
                    sb.Append("\t\t\t\t}");
                }
                sb.AppendLine();
                sb.AppendLine("\t\t\t],");
            }

            sb.AppendLine("\t\t}");

            return sb.ToString();
        }

        // -------------------------------------------------------------------------
        // B) Write the level metadata file
        // -------------------------------------------------------------------------
        private static void UpdateLevelMetadata(string jsonSegment)
        {
            string content = File.ReadAllText(SimLevelMetadata);

            // Find the end of the template block comment (first "*/" in the file).
            int commentEnd = content.IndexOf("*/", StringComparison.Ordinal);
            if (commentEnd < 0) throw new InvalidOperationException("Could not find template comment block in level metadata.");
            commentEnd += 2; // step past */

            // Anchor the community_levels closing bracket by finding "globalObjectOffsets"
            // (which always follows), then searching backwards for the last ], before it.
            // This is robust even if a previous run left orphaned data before globalObjectOffsets.
            int globalOffsetsPos = content.IndexOf("globalObjectOffsets", commentEnd, StringComparison.Ordinal);
            if (globalOffsetsPos < 0) throw new InvalidOperationException("Could not find globalObjectOffsets in level metadata.");

            // Walk back to find the ], that closes community_levels.
            int arrayClose = content.LastIndexOf("],", globalOffsetsPos, StringComparison.Ordinal);
            if (arrayClose < 0) throw new InvalidOperationException("Could not find community_levels closing bracket in level metadata.");
            // Include the ], itself in what we preserve (point to the \t that precedes ],).
            // Walk further back to include the leading whitespace/newline before the bracket.
            while (arrayClose > commentEnd && content[arrayClose - 1] != '\n')
                arrayClose--;

            // Reconstruct: keep everything up to and including */, then the new level, then ],…
            string newContent =
                content.Substring(0, commentEnd) +
                "\n\n" + jsonSegment + "\n" +
                content.Substring(arrayClose);

            File.WriteAllText(SimLevelMetadata, newContent, new UTF8Encoding(false));
        }

        // -------------------------------------------------------------------------
        // C) Write the music metadata file
        // -------------------------------------------------------------------------
        private static void UpdateMusicMetadata(string rawSong)
        {
            string content = File.ReadAllText(SimMusicMetadata);

            // Find the placeholder entry: the song object whose lowerText is "TEXT".
            // Walk backwards from that position to find the fmsSongName in the same entry and replace it.
            var lowerTextMatch = Regex.Match(content, @"lowerText\s*:\s*""TEXT""", RegexOptions.IgnoreCase);
            if (lowerTextMatch.Success)
            {
                // Everything before the lowerText line — find the last fmsSongName in it (same entry)
                string before = content.Substring(0, lowerTextMatch.Index);
                var songNameMatches = Regex.Matches(before, @"fmsSongName\s*:\s*""[^""]*""");
                if (songNameMatches.Count > 0)
                {
                    var songNameMatch = songNameMatches[songNameMatches.Count - 1];
                    string updated = content.Substring(0, songNameMatch.Index)
                        + $"fmsSongName: \"{EscapeJson5(rawSong)}\""
                        + content.Substring(songNameMatch.Index + songNameMatch.Length);
                    File.WriteAllText(SimMusicMetadata, updated, new UTF8Encoding(false));
                    return;
                }
            }

            // Fallback: replace any fmsSongName with value "Jumper" (original placeholder)
            string fallback = Regex.Replace(
                content,
                @"fmsSongName:\s*""Jumper""",
                $"fmsSongName: \"{EscapeJson5(rawSong)}\"",
                RegexOptions.IgnoreCase);

            File.WriteAllText(SimMusicMetadata, fallback, new UTF8Encoding(false));
        }

        private static string EscapeJson5(string s) => (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

        // -------------------------------------------------------------------------
        // D) Run 1BigSimExport.bat
        // -------------------------------------------------------------------------
        private static Task<bool> RunBuildBatAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo("cmd.exe", $"/c \"{SimBuildBat}\"")
                    {
                        WorkingDirectory = SimFamidashFolder,
                        UseShellExecute = true,   // show the console window so the user can see progress
                        CreateNoWindow  = false,
                    };
                    using var proc = Process.Start(psi);
                    if (proc == null) return false;
                    // Wait up to 5 minutes
                    bool finished = proc.WaitForExit(300_000);
                    return finished && proc.ExitCode == 0;
                }
                catch { return false; }
            });
        }

        // -------------------------------------------------------------------------
        // E) Open ROM in Mesen
        // -------------------------------------------------------------------------
        private void OpenRomInMesen(string romPath)
        {
            OpenRomInMesen(romPath, useReplay: false);
        }

        private void OpenRomInMesen(string romPath, bool useReplay)
        {
            // Write the overlay Lua script to the per-level Documents folder.
            string luaPath = OverlayLuaPath;
            try
            {
                File.WriteAllText(luaPath, BuildOverlayLuaScript(includeReplay: useReplay, drawPathlines: true));
                File.WriteAllText(OverlayLuaNoPathlinesPath, BuildOverlayLuaScript(includeReplay: useReplay, drawPathlines: false));
            }
            catch { luaPath = ""; }

            // Embedded mode: host MesenCore.dll inside MainWindow.  No separate
            // process, no separate window — true lockstep with the editor.
            if (Option_EmbeddedMesen)
            {
                if (OpenRomInEmbeddedMesen(romPath, luaPath))
                    return;
                // Fall through to the legacy launch path on failure.
            }

            // Always use the bundled Mesen that lives next to the editor executable.
            string exePath = Path.Combine(AppContext.BaseDirectory, "mesen", "Mesen.exe");

            if (!File.Exists(exePath))
            {
                MessageBox.Show(this,
                    $"Bundled Mesen not found at:\n{exePath}\n\nRebuild the editor to copy Mesen to the output folder.",
                    "Build and Test", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string workDir = Path.GetDirectoryName(exePath)!;

            // Build argument list: <rom> [lua-script]
            string args = $"\"{romPath}\"";
            if (!string.IsNullOrEmpty(luaPath) && File.Exists(luaPath))
                args += $" \"{luaPath}\"";

            var psi = new ProcessStartInfo(exePath, args)
            {
                UseShellExecute  = false,
                CreateNoWindow   = false,
                WorkingDirectory = workDir,
            };

            var proc = Process.Start(psi);
            if (proc != null)
                StartMesenOverlay(proc);
        }
    }
}
