using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace FamidashEditor
{
    public partial class MainWindow
    {
        private Process?        _mesenRunProcess;
        private string?         _mesenLogStamp;
        private bool            _pathfinderLoggingEnabled = true;
        private bool            _mesenLuaLoggingEnabled = true;
        // ── Per-level replay/trace paths ────────────────────────────────────
        // All Mesen-related files live in My Documents under a per-level folder.
        //   <Documents>/Famidash Editor/Replays/<level>/famidash_overlay.lua
        //   <Documents>/Famidash Editor/Replays/<level>/famidash_replay.csv
        //   <Documents>/Famidash Editor/Replays/<level>/famidash_mesen_trace.csv
        // Snapshots from "Compare Traces" land in the same per-level folder.
        internal static string ReplayRootDir =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "Famidash Editor", "Replays");

        private static string SanitizeLevelName(string? raw)
        {
            string name;
            if (string.IsNullOrWhiteSpace(raw))
                name = "untitled";
            else
                name = Path.GetFileNameWithoutExtension(raw);
            if (string.IsNullOrWhiteSpace(name)) name = "untitled";
            foreach (char ch in Path.GetInvalidFileNameChars())
                name = name.Replace(ch, '_');
            return name;
        }

        internal static string GetLevelReplayDir(string? levelFilePath)
        {
            string dir = Path.Combine(ReplayRootDir, SanitizeLevelName(levelFilePath));
            try { Directory.CreateDirectory(dir); } catch { }
            return dir;
        }

        internal string CurrentReplayDir =>
            GetLevelReplayDir(currentFilePath);

        internal string OverlayLuaPath =>
            Path.Combine(CurrentReplayDir, "famidash_overlay.lua");

        internal string OverlayLuaNoPathlinesPath =>
            Path.Combine(CurrentReplayDir, "famidash_overlay (nopathlines).lua");

        internal string OverlayLuaHitboxesPath =>
            Path.Combine(CurrentReplayDir, "famidash_overlay (hitboxes and pathlines).lua");

        internal string OverlayLuaHitboxesNoPathlinesPath =>
            Path.Combine(CurrentReplayDir, "famidash_overlay (hitboxes, no pathlines or player).lua");

        internal string ReplayTempFile =>
            Path.Combine(CurrentReplayDir, "famidash_replay.csv");

        internal string CurrentLevelTag =>
            SanitizeLevelName(currentFilePath);

        private string MesenLogStamp =>
            _mesenLogStamp ??= DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");

        private void RefreshMesenLogStamp()
        {
            _mesenLogStamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
        }

        // Mesen trace is an output written by the overlay Lua; it is not read
        // back by the overlay or the replay pipeline, so it lives in %TEMP%
        // alongside the other compare/debug artefacts instead of cluttering the
        // per-level Replays folder.
        internal string MesenTraceFile =>
            Path.Combine(Path.GetTempPath(), $"famidash_mesen_trace_{CurrentLevelTag}_{MesenLogStamp}.csv");

        // Comprehensive per-frame orb/sprite-state dump for PF↔NES divergence
        // diagnosis.  Written by the overlay Lua alongside the regular trace.
        // Includes Generic/Generic2 hitboxes (player + most-recent sprite tested),
        // every active sprite slot's type/realx/realy/active/activated, and key
        // physics scalars.  See AppendOrbDebugLuaSnippet() in the overlay Lua.
        internal string MesenOrbDebugFile =>
            Path.Combine(Path.GetTempPath(), $"famidash_mesen_orb_debug_{CurrentLevelTag}_{MesenLogStamp}.log");

        // Per-frame log of NES physics-routine entries (movement / eject /
        // collision).  Captures pre-call state of currplayer_y, currplayer_vel_y,
        // Generic.x/y/w/h, eject_U/D, collision, temp_x/y/room, scroll_x/y at
        // every entry to ufo_ship_eject / cube_eject / ball_eject / spider_eject /
        // bg_coll_U / bg_coll_D / bg_coll_R / bg_coll_L / bg_coll_death /
        // x_movement_coll / x_movement / common_gravity_routine / *_movement.
        // PCs are baked from BUILD/huge/famidash.dbg at script-generation time;
        // if the ROM is rebuilt with shifted addresses the C# side must regenerate.
        internal string MesenPhysicsDebugFile =>
            Path.Combine(Path.GetTempPath(), $"famidash_mesen_physics_debug_{CurrentLevelTag}_{MesenLogStamp}.log");

        // Track process lifetime so trace cleanup and post-run path display remain
        // available without repositioning Mesen or scrolling the editor.
        internal void StartMesenRunMonitor(Process proc)
        {
            // Do not refresh the log stamp here. BuildOverlayLuaScript() bakes
            // the current stamp into the Lua file names before Mesen launches;
            // changing it after script generation makes C# point at a new empty
            // triplet while Lua writes the previous valid triplet.
            //
            // Also do not pre-create/truncate the log files here. The Lua script
            // opens them lazily on the first real gameplay arm and writes headers
            // immediately, which prevents stray 0-byte sets when Mesen starts,
            // reloads, or exits without gameplay.

            _mesenRunProcess = proc;

            // Watch for process exit and clean up
            Task.Run(() =>
            {
                try { proc.WaitForExit(); } catch { }
                Dispatcher.BeginInvoke(StopMesenRunMonitor);
            });
        }

        internal void StopMesenRunMonitor()
        {
            string traceFile = MesenTraceFile;
            string orbDebugFile = MesenOrbDebugFile;
            string physicsDebugFile = MesenPhysicsDebugFile;
            Process? proc = _mesenRunProcess;
            _mesenRunProcess = null;
            try
            {
                if (proc != null && !proc.HasExited)
                {
                    proc.Kill(true);
                }
            }
            catch { }

            // Mesen has exited.  Parse the per-frame trace CSV and show the
            // actual NES path on the editor canvas — magenta polyline above
            // the pathfinder bias-colored paths so divergences pop visually.
            try { LoadAndShowMesenTracePath(traceFile); } catch { }
            try { DeleteIfZeroByteFile(traceFile); } catch { }
            try { DeleteIfZeroByteFile(orbDebugFile); } catch { }
            try { DeleteIfZeroByteFile(physicsDebugFile); } catch { }
        }

        private static void DeleteIfZeroByteFile(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                var fi = new FileInfo(path);
                if (fi.Exists && fi.Length == 0)
                    fi.Delete();
            }
            catch { }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            try { StopMesenRunMonitor(); } catch { }
            base.OnClosing(e);
        }

        /// <summary>
        /// Writes the two existing replay overlays plus two variants augmented
        /// with the authoritative Famidash tile/sprite hitbox overlay.
        /// Existing replay/F9 callers continue to launch OverlayLuaPath.
        /// </summary>
        internal void WriteGeneratedOverlayLuaScripts(bool includeReplay)
        {
            string withPathlines = BuildOverlayLuaScript(
                includeReplay, drawPathlines: true, _mesenLuaLoggingEnabled);
            string withoutPathlines = BuildOverlayLuaScript(
                includeReplay, drawPathlines: false, _mesenLuaLoggingEnabled);

            File.WriteAllText(OverlayLuaPath, withPathlines);
            File.WriteAllText(OverlayLuaNoPathlinesPath, withoutPathlines);

            // Do not make the established replay/F9 flow depend on this optional
            // companion source. Normal builds and publishes package it locally;
            // the resolver also repairs a missing local copy from the workspace.
            try
            {
                string hitboxSourcePath = LocalRuntimeFolders.EnsureFamidashHitboxOverlayScript();
                string hitboxSource = File.ReadAllText(hitboxSourcePath);

                File.WriteAllText(
                    OverlayLuaHitboxesNoPathlinesPath,
                    withoutPathlines + PrepareHitboxOverlaySource(
                        hitboxSource,
                        showPlayer: false,
                        enableLogging: _mesenLuaLoggingEnabled));
                File.WriteAllText(
                    OverlayLuaHitboxesPath,
                    withPathlines + PrepareHitboxOverlaySource(
                        hitboxSource,
                        showPlayer: true,
                        enableLogging: _mesenLuaLoggingEnabled));
            }
            catch
            {
                // The standard generated scripts above remain valid and usable.
            }
        }

        private static string PrepareHitboxOverlaySource(
            string source,
            bool showPlayer,
            bool enableLogging)
        {
            string configured = source.Replace(
                "local SHOW_PLAYER     = true",
                $"local SHOW_PLAYER     = {(showPlayer ? "true" : "false")}",
                StringComparison.Ordinal);

            if (!enableLogging)
            {
                var filtered = new System.Text.StringBuilder(configured.Length);
                using var reader = new StringReader(configured);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    string trimmed = line.TrimStart();
                    if (trimmed.StartsWith("emu.log(", StringComparison.Ordinal))
                    {
                        int indentation = line.Length - trimmed.Length;
                        filtered.Append(' ', indentation)
                                .AppendLine("-- Mesen logging disabled by FamidashEditor");
                    }
                    else
                    {
                        filtered.AppendLine(line);
                    }
                }
                configured = filtered.ToString();
            }

            // Keep the hitbox script's many locals in their own Lua function
            // scope. Its registered callbacks retain those locals after setup.
            return "\n\n-- Famidash authoritative hitbox overlay ------------------------------\n" +
                   "local function __installFamidashHitboxOverlay()\n" +
                   configured +
                   "\nend\n__installFamidashHitboxOverlay()\n";
        }

        // -----------------------------------------------------------------------
        // Self-contained replay/trace Lua script passed to Mesen on startup.
        // -----------------------------------------------------------------------
        internal string BuildOverlayLuaScript()
        {
            return BuildOverlayLuaScript(includeReplay: true);
        }

        // The script is ALWAYS self-contained: replay data is embedded as a Lua
        // literal directly in the generated source.  When `includeReplay` is
        // false, an empty replay table is embedded and the script still loads.
        // No external file reads, ever.
        internal string BuildOverlayLuaScript(bool includeReplay)
        {
            return BuildOverlayLuaScript(includeReplay, drawPathlines: true);
        }

        internal string BuildOverlayLuaScript(bool includeReplay, bool drawPathlines)
        {
            return BuildOverlayLuaScript(includeReplay, drawPathlines, _mesenLuaLoggingEnabled);
        }

        internal string BuildOverlayLuaScript(bool includeReplay, bool drawPathlines, bool enableLogging)
        {
            // The Lua script resolves the temp directory at runtime via
            // os.getenv so the generated source contains NO machine-specific
            // paths.  Filenames are constants; only the directory varies.
            // Both the C# side (MesenTraceFile/MesenOrbDebugFile) and the Lua
            // resolver below produce the same path on any machine.
            string traceFileName  = System.IO.Path.GetFileName(MesenTraceFile);
            string orbDbgFileName = System.IO.Path.GetFileName(MesenOrbDebugFile);
            string physDbgFileName = System.IO.Path.GetFileName(MesenPhysicsDebugFile);

            // Read replay CSV at generation time and embed it as Lua literals
            // so the running script has zero inbound file dependencies.
            // Keeps trace/orb-debug writes (those are outputs, not dependencies).
            string embeddedReplayLiteral = "{}";
            string embeddedReplay2Literal = "{}";
            int    embeddedYOffset       = 0;
            if (includeReplay)
            {
                try
                {
                    if (System.IO.File.Exists(this.ReplayTempFile))
                    {
                        var sb  = new System.Text.StringBuilder();
                        var sb2 = new System.Text.StringBuilder();
                        sb.Append('{');
                        sb2.Append('{');
                        bool first = true;
                        bool first2 = true;
                        foreach (var raw in System.IO.File.ReadAllLines(this.ReplayTempFile))
                        {
                            if (string.IsNullOrEmpty(raw)) continue;
                            if (raw.StartsWith("nes_y_offset", System.StringComparison.Ordinal))
                            {
                                int comma = raw.IndexOf(',');
                                if (comma > 0 && int.TryParse(raw.AsSpan(comma + 1), out int yo))
                                    embeddedYOffset = yo;
                                continue;
                            }
                            if (raw.StartsWith("frame", System.StringComparison.Ordinal)) continue;
                            if (raw.StartsWith("p2_path", System.StringComparison.Ordinal)) continue;
                            if (raw.StartsWith("p2,", System.StringComparison.Ordinal))
                            {
                                var p2parts = raw.Split(',');
                                if (p2parts.Length < 3) continue;
                                if (!int.TryParse(p2parts[1], out int p2x)) continue;
                                if (!int.TryParse(p2parts[2], out int p2y)) continue;
                                if (!first2) sb2.Append(',');
                                first2 = false;
                                sb2.Append("{x=").Append(p2x)
                                   .Append(",y=").Append(p2y)
                                   .Append('}');
                                continue;
                            }
                            // Expected: frame,x,y,a
                            var parts = raw.Split(',');
                            if (parts.Length < 4) continue;
                            if (!int.TryParse(parts[1], out int x)) continue;
                            if (!int.TryParse(parts[2], out int y)) continue;
                            if (!int.TryParse(parts[3], out int a)) continue;
                            if (!first) sb.Append(',');
                            first = false;
                            sb.Append("{x=").Append(x)
                              .Append(",y=").Append(y)
                              .Append(",a=").Append(a)
                              .Append('}');
                        }
                        sb.Append('}');
                        sb2.Append('}');
                        embeddedReplayLiteral = sb.ToString();
                        embeddedReplay2Literal = sb2.ToString();
                    }
                }
                catch { /* fall back to empty replay */ }
            }

            // Lua reads NES _player_x ($043D, 16-bit lo|hi) and _player_y ($0441) every frame.
            // px = (read16(_player_x) >> 8) + 8 matches PathfinderEngine PathPoints (hitbox center).
            // The first replay X is used only to latch past the NES intro-freeze pre-step.
            // From then on the replay cursor is clocked by NES-local physics movement, never
            // by the predicted PF path; a real divergence therefore cannot retime the inputs.
            string replayPart =
$@"-- FamidashEditor Mesen replay script (auto-generated, do not edit)
-- ── Replay injector ────────────────────────────────────────────────────────
-- Output paths (written, never read).  Resolved at runtime from environment
-- so the script contains no machine-specific paths.  Falls back to the
-- current working directory if no temp env var is set.
local function _tempDir()
    local d = os.getenv(""TEMP"") or os.getenv(""TMP"") or os.getenv(""TMPDIR"") or """"
    if d == """" then return ""."" end
    -- Strip trailing slash/backslash for consistent joining.
    local last = d:sub(-1)
    if last == ""/"" or last == ""\\"" then d = d:sub(1, -2) end
    return d
end
local function _tempPath(name)
    local d = _tempDir()
    -- Prefer backslash on Windows (TEMP env), forward slash elsewhere.
    local sep = (os.getenv(""TEMP"") or os.getenv(""TMP"")) and ""\\"" or ""/""
    return d .. sep .. name
end
local traceFile  = _tempPath(""{traceFileName}"")
local orbDbgFile = _tempPath(""{orbDbgFileName}"")
local physDbgFile= _tempPath(""{physDbgFileName}"")
-- Replay table is embedded directly so this script has NO inbound file deps.
-- Regenerated by FamidashEditor every time a path is exported.
local replay     = {embeddedReplayLiteral}
local replay2    = {embeddedReplay2Literal}
local nesYOffset = {embeddedYOffset}
local SHOW_PATHLINES = {(drawPathlines ? "true" : "false")}
local ENABLE_LOGGING = {(enableLogging ? "true" : "false")}
local STATE_GAME = 0x02
local ADDR_GAMESTATE = 0x049C
local ADDR_JOYPAD1_HOLD = 0x0022
local PAD_A  = 0x80
local PAD_UP = 0x08
local cursor = 1
local armed = false
local prevPx = -1
local lastA = false
local frameIdx = 0
local replayClockStarted = false
local replayClockPrevPx = nil
local traceFp = nil
local orbDbgFp = nil
local physDbgFp = nil
-- Per-frame ring buffer of physics-routine events (cleared each NMI / endFrame).
-- Format: each entry is a preformatted string ""TAG pc=XXXX cpx=... cpy=... ...""
local physEvents = {{}}
local physEventCount = 0
local PHYS_EVENT_MAX = 64
-- Frame range filter for orb debug dump.  Set both to large values to log
-- the entire run, narrow them once you know the divergence frame band.
local ORB_DBG_LO = 0
local ORB_DBG_HI = 1000000
-- Ring buffer of recent ACTUAL NES (px,py) positions, for drawing a real
-- player trail to visualise divergence from the PF-projected path.
-- Each entry is {{ x = pf-coord X, y = pf-coord Y }}.  Drawn yellow.
local nesTrail = {{}}
local nesTrailMax = 600
local nesTrailHead = 1
local nesTrailCount = 0
local function clearPathOverlay()
    armed = false
    lastA = false
    prevPx = -1
    replayClockStarted = false
    replayClockPrevPx = nil
    nesTrail = {{}}
    nesTrailHead = 1
    nesTrailCount = 0
    physEventCount = 0
end
-- Linear-Y offset between NES world coords and pathfinder world coords:
--   PF_y = NES_y - nesYOffset    (set per-level by ExportReplayCsv)
-- Already baked in above from the C# generator.

-- ── cube_data[0] write tracker ─────────────────────────────────────────────
-- Capture the PC of every write to $007B (cube_data[0]).  When the byte
-- transitions 0->1 (death flag set), the most recent PC is the routine that
-- killed the player.  Stored in `lastCubeDataWritePC` and flushed into the
-- per-frame trace row's `cube_data` cell as ""val@PC"" once per kill.
local lastCubeDataWritePC = 0
local cubeDataDeathPC = nil      -- PC at the 0->1 transition (sticky until reset)
local cubeDataDeathCtx = nil     -- probe context snapshot at the 0->1 transition
local cubeDataPrev = 0
emu.addMemoryCallback(function(addr, value)
    local st = emu.getState()
    -- Mesen2 state is a flat table keyed by dotted-path strings, e.g.
    -- state[""cpu.pc""], state[""ppu.scanline""].  Nested-table access
    -- (st.cpu.pc) returns nil and gave PC=0000 in the previous trace.
    local pc = 0
    if st then
        pc = st[""cpu.pc""] or st[""cpu.PC""] or 0
    end
    lastCubeDataWritePC = pc
    -- Detect 0->1 transition (death).  Bit 0 is the kill flag.
    local prev = cubeDataPrev
    cubeDataPrev = value
    if (prev & 1) == 0 and (value & 1) == 1 then
        cubeDataDeathPC = pc
        -- Snapshot the probe context that bg_collision_sub left in zeropage
        -- when the kill routine fired.  Addresses come from
        -- sim-famidash/BUILD/main/famidash.dbg:
        --   _temp_x=$93 _temp_y=$94 _temp_room=$95 _collision=$81
        --   _Generic=$5C8 (x,y,width,height bytes)
        --   _currplayer_x=$68 (16-bit) _currplayer_y=$6A (16-bit)
        --   _currplayer_mini=$67 _scroll_x=$4A6 (32-bit)
        local M = emu.memType.nesMemory
        local tx  = emu.read(0x93, M) or 0
        local ty  = emu.read(0x94, M) or 0
        local tr  = emu.read(0x95, M) or 0
        local col = emu.read(0x81, M) or 0
        local gx  = emu.read(0x5C8, M) or 0
        local gy  = emu.read(0x5C9, M) or 0
        local gw  = emu.read(0x5CA, M) or 0
        local gh  = emu.read(0x5CB, M) or 0
        local cpx = emu.read16(0x68, M) or 0
        local cpy = emu.read16(0x6A, M) or 0
        local mini= emu.read(0x67, M) or 0
        local sx  = emu.read32(0x4A6, M) or 0
        cubeDataDeathCtx = string.format(
            ""tx=%02X ty=%02X tr=%02X col=%02X G=(%02X,%02X,%dx%d) cp=(%04X,%04X) mini=%d sx=%05X"",
            tx, ty, tr, col, gx, gy, gw, gh, cpx, cpy, mini, sx)
    end
end, emu.callbackType.write, 0x007B)

-- ── Watchpoint: activesprites_x_lo slot writes ─────────────────────────────
-- Log every write to activesprites_x_lo[0..15] (0x4DB..0x4EA) to catch
-- unexpected modifications (e.g. gravity portal worldX discrepancy).
-- Mesen addMemoryCallback does not accept an address range as two args;
-- register one callback per slot address.
local function _xloWriteSnap(addr, value)
    if orbDbgFp then
        local st = emu.getState()
        local pc = 0
        local aReg = 0
        local xReg = 0
        if st then
            pc = st[""cpu.pc""] or st[""cpu.PC""] or 0
            aReg = st[""cpu.a""] or st[""cpu.A""] or 0
            xReg = st[""cpu.x""] or st[""cpu.X""] or 0
        end
        local slot = addr - 0x4DB
        orbDbgFp:write(string.format(
            ""WRITE_XLO slot=%d addr=$%04X val=$%02X pc=$%04X A=$%02X X=$%02X cursor=%d\n"",
            slot, addr, value, pc, aReg, xReg, cursor))
        orbDbgFp:flush()
    end
end
for _sl = 0, 15 do
    emu.addMemoryCallback(_xloWriteSnap, emu.callbackType.write, 0x4DB + _sl)
end

-- ── Physics-routine entry hooks ────────────────────────────────────────────
-- For PF↔NES divergence diagnosis, capture full pre-call state at every
-- entry to the major movement / eject / collision routines.  PCs come from
-- BUILD/huge/famidash.dbg (rebuild ROM with shifted addrs ⇒ regenerate).
-- Each event is appended to physEvents[]; the per-frame trace callback
-- flushes them to physDbgFile and clears the buffer.
--
-- NOTE: cc65 banked PRG.  Multiple banks may map the same CPU address; the
-- callback fires on ANY exec at the PC regardless of which bank is paged in.
-- We tag each event with the current bank (read via emu.getState() PRG
-- register) so post-hoc filtering can distinguish wrong-bank false hits.
--
-- Routines (huge build):
--   $A183 _bg_coll_U   (size 313 -> last $A2BB)
--   $A2BC _bg_coll_D   (size 306 -> last $A3ED)
--   $ADDB _bg_coll_R   (size  35 -> last $ADFD)
--   $ADFE _bg_coll_L   (size  43 -> last $AE28)
--   $B03C _bg_coll_death (size 148 -> last $B0CF)
--   $B0D0 _ufo_ship_eject (size  42 -> last $B0F9)
--   $B0FA _common_gravity_routine (size 357 -> last $B25E)
--   $B25F _ufo_movement (size  97 -> last $B2BF)
--   $B2C0 _ball_eject (size 286 -> last $B3DD)
--   $B3DE _ball_movement (size 319 -> last $B51C ish)
--   $B542 _cube_eject (size 164 -> last $B5E5)
--   $B5E6 _cube_movement (size 1233 -> last $BAB6)
--   $BAD5 _ship_movement (size 222 -> last $BBB2)
--   $BBB3 _spider_eject (size  40 -> last $BBDA)
--   $BBDB _spider_movement (size 300 -> last $BD06)
--   $BD65 _wave_movement (size 291 -> last $BE87)
--   $87DA _x_movement_coll (size  77 -> last $8826)
--   $8827 _x_movement (size 441 -> last $89DF)
local function _physBank()
    local s = emu.getState()
    if not s then return -1 end
    -- Mesen2 NES state exposes 'mapper.X' and 'cartridge.X' fields.  The
    -- selected PRG bank for the $A000-$BFFF window is mapper-specific;
    -- best-effort dump of common keys.
    return s[""mapper.prgPageSize""] or s[""mapper.bank0""] or -1
end

local function _physSnap(tag, pc)
    if not ENABLE_LOGGING then return end
    if physEventCount >= PHYS_EVENT_MAX then return end
    local M = emu.memType.nesMemory
    local cpx  = emu.read16(0x68, M) or 0
    local cpy  = emu.read16(0x6A, M) or 0
    local cpvy = emu.read16(0x6E, M) or 0
    if cpvy >= 0x8000 then cpvy = cpvy - 0x10000 end
    local gx  = emu.read(0x5C8, M) or 0
    local gy  = emu.read(0x5C9, M) or 0
    local gw  = emu.read(0x5CA, M) or 0
    local gh  = emu.read(0x5CB, M) or 0
    local ej_U = emu.read(0x8D, M) or 0
    local ej_D = emu.read(0x8C, M) or 0
    local col  = emu.read(0x81, M) or 0
    local tx   = emu.read(0x93, M) or 0
    local ty   = emu.read(0x94, M) or 0
    local tr   = emu.read(0x95, M) or 0
    local mini = emu.read(0x67, M) or 0
    local grav = emu.read(0x70, M) or 0
    local tidx = emu.read(0x79, M) or 0
    local cd   = emu.read(0x7B, M) or 0
    local sx   = emu.read32(0x4A6, M) or 0
    local sy_r = emu.read16(0x4AA, M) or 0
    -- ── SLOPE STATE (zero page currplayer + zp-cached + globals) ─────────
    -- 0x74=currplayer_slope_frames, 0x75=currplayer_was_on_slope_counter,
    -- 0x76=currplayer_slope_type, 0x77=currplayer_last_slope_type.
    -- 0x97=slope_frames(zp), 0x99=slope_type(zp), 0x9B=was_on_slope_counter(zp).
    -- 0x4B5=_make_cube_jump_higher, 0x49A=_last_slope_type (player array).
    local cpsF   = emu.read(0x74, M) or 0
    local cpswOn = emu.read(0x75, M) or 0
    local cpsT   = emu.read(0x76, M) or 0
    local cplst  = emu.read(0x77, M) or 0
    local zsF    = emu.read(0x97, M) or 0
    local zsT    = emu.read(0x99, M) or 0
    local zswOn  = emu.read(0x9B, M) or 0
    local mcjh   = emu.read(0x4B5, M) or 0
    local glst   = emu.read(0x49A, M) or 0
    local cpvx = emu.read16(0x6C, M) or 0
    if cpvx >= 0x8000 then cpvx = cpvx - 0x10000 end
    -- A/X/Y registers at entry — useful to read return value when hooking
    -- the byte AFTER an RTS-bearing call site.
    local st = emu.getState()
    local A = (st and (st[""cpu.a""] or st[""cpu.A""])) or 0
    local X = (st and (st[""cpu.x""] or st[""cpu.X""])) or 0
    local Y = (st and (st[""cpu.y""] or st[""cpu.Y""])) or 0
    physEventCount = physEventCount + 1
    physEvents[physEventCount] = string.format(
        ""%s pc=%04X A=%02X X=%02X Y=%02X cpx=%04X cpy=%04X cpvx=%d cpvy=%d "" ..
        ""G=(%02X,%02X,%dx%d) ej_U=%02X ej_D=%02X col=%02X probe=(%02X,%02X,%02X) "" ..
        ""mini=%d grav=%02X tidx=%02X cd=%02X sx=%05X sy=%04X "" ..
        ""SLOPE[cpsF=%d cpswOn=%d cpsT=%02X cplst=%02X zsF=%d zsT=%02X zswOn=%d mcjh=%d glst=%02X]"",
        tag, pc, A, X, Y, cpx, cpy, cpvx, cpvy,
        gx, gy, gw, gh, ej_U, ej_D, col, tx, ty, tr,
        mini, grav, tidx, cd, sx, sy_r,
        cpsF, cpswOn, cpsT, cplst, zsF, zsT, zswOn, mcjh, glst)
end

local function _hookPhys(tag, pc)
    emu.addMemoryCallback(function() _physSnap(tag, pc) end,
        emu.callbackType.exec, pc)
end

-- Entry hooks
_hookPhys(""bg_coll_U.in"",      0xA183)
_hookPhys(""bg_coll_D.in"",      0xA2BC)
_hookPhys(""bg_coll_R.in"",      0xAE0C)
_hookPhys(""bg_coll_L.in"",      0xAE2F)
_hookPhys(""bg_coll_death.in"",  0xB07E)
_hookPhys(""ufo_ship_eject.in"", 0xB148)
_hookPhys(""common_gravity.in"", 0xB172)
_hookPhys(""common_gravity.out"", 0xB2D6) -- last byte of _common_gravity_routine ($B172+357-1)
_hookPhys(""ufo_movement.in"",   0xB2D7)
_hookPhys(""ball_eject.in"",     0xB338)
_hookPhys(""ball_movement.in"",  0xB456)
_hookPhys(""cube_eject.in"",     0xB5CA)
_hookPhys(""cube_movement.in"",  0xB66E)
_hookPhys(""ship_movement.in"",  0xBB5D)
_hookPhys(""spider_eject.in"",   0xBC3B)
_hookPhys(""spider_movement.in"",0xBC63)
_hookPhys(""wave_movement.in"",  0xBDED)
_hookPhys(""x_movement_coll.in"",0x882D)
_hookPhys(""x_movement.in"",     0x887A)

-- ── SLOPE routine hooks ─────────────────────────────────────────────────
-- Addresses from famidash/BUILD/huge/famidash.dbg.  Each in+out pair shows
-- the pre/post zp slope state so PF↔NES slope timing/state can be diffed.
--   _slope_vel          : $87C1 + 50 -> last $87F2
--   _apply_slope_vel    : $87F3 + 58 -> last $882C
--   _bg_coll_slope      : $AA0A + 823 -> last $AD40
--   _bg_coll_return_slope_D : $AEF1 + 82 -> last $AF42
--   _bg_coll_return_slope_U : $AF43 + 87 -> last $AF99
--   _slope_jump_check   : $B5A5 + 37 -> last $B5C9
_hookPhys(""slope_vel.in"",          0x87C1)
_hookPhys(""slope_vel.out"",         0x87F2)
_hookPhys(""apply_slope_vel.in"",    0x87F3)
_hookPhys(""apply_slope_vel.out"",   0x882C)
_hookPhys(""bg_coll_slope.in"",      0xAA0A)
_hookPhys(""bg_coll_slope.out"",     0xAD40)
_hookPhys(""bg_coll_ret_slp_D.in"",  0xAEF1)
_hookPhys(""bg_coll_ret_slp_D.out"", 0xAF42)
_hookPhys(""bg_coll_ret_slp_U.in"",  0xAF43)
_hookPhys(""bg_coll_ret_slp_U.out"", 0xAF99)
_hookPhys(""slope_jump_check.in"",   0xB5A5)
_hookPhys(""slope_jump_check.out"",  0xB5C9)

-- Exit hooks (last byte of each scope = presumed RTS).  Captures POST-call
-- state of currplayer_y / vel_y / eject_U/D / collision so a U/D eject's
-- effect on Y_high is directly visible in the log.  A register holds C
-- return value at function exit (if scope ends with RTS).
_hookPhys(""bg_coll_U.out"",      0xA2BB)
_hookPhys(""bg_coll_D.out"",      0xA3ED)
_hookPhys(""bg_coll_R.out"",      0xAE2E)
_hookPhys(""bg_coll_L.out"",      0xAE59)
_hookPhys(""bg_coll_death.out"",  0xB147)
_hookPhys(""ufo_ship_eject.out"", 0xB171)
_hookPhys(""ufo_movement.out"",   0xB337)
_hookPhys(""ball_eject.out"",     0xB455)
_hookPhys(""cube_eject.out"",     0xB66D)
_hookPhys(""ship_movement.out"",  0xBC3A)
_hookPhys(""spider_eject.out"",   0xBC62)
_hookPhys(""spider_movement.out"",0xBD8E)
_hookPhys(""x_movement_coll.out"",0x8879)
_hookPhys(""x_movement.out"",     0x8A32)

-- ── currplayer_y write watchers ($6A=low, $6B=high) ────────────────────────
-- Memory-write hooks log EVERY write to cpy so we can pinpoint which
-- instruction modifies it.  Diagnoses the sim=2211 +0x200 mystery where
-- ej_D=00 yet cpy_high increased by 2 between slope_vel.out and bg_coll_D.out.
local function _cpyWriteSnap(addr, value)
    if not ENABLE_LOGGING then return end
    if physEventCount >= PHYS_EVENT_MAX then return end
    local M = emu.memType.nesMemory
    local st = emu.getState()
    local pc = (st and (st[""cpu.pc""] or st[""cpu.PC""])) or 0
    local A  = (st and (st[""cpu.a""]  or st[""cpu.A""]))  or 0
    local X  = (st and (st[""cpu.x""]  or st[""cpu.X""]))  or 0
    local Y  = (st and (st[""cpu.y""]  or st[""cpu.Y""]))  or 0
    -- cpy is 16-bit at $6A; we get notified PER-byte so read both to show
    -- the COMPOSED post-write value (Mesen fires callback BEFORE the actual
    -- write completes — we synthesize the post-write value).
    local cpy_lo = emu.read(0x6A, M) or 0
    local cpy_hi = emu.read(0x6B, M) or 0
    if addr == 0x6A then cpy_lo = value end
    if addr == 0x6B then cpy_hi = value end
    local cpy_post = cpy_lo | (cpy_hi << 8)
    local ej_U = emu.read(0x8D, M) or 0
    local ej_D = emu.read(0x8C, M) or 0
    -- ROM file offset of the currently-paged code at PC: disambiguates which
    -- bank's $BDxx code is actually executing.  For MMC3 8KB swap at
    -- $A000-$BFFF, bank = offset / 0x2000 (when normalized to PRG ROM start).
    local romOff = -1
    if emu.getPrgRomOffset then romOff = emu.getPrgRomOffset(pc) or -1 end
    physEventCount = physEventCount + 1
    physEvents[physEventCount] = string.format(
        ""CPY_WRITE addr=%04X val=%02X -> cpy_post=%04X pc=%04X romOff=%X A=%02X X=%02X Y=%02X ej_U=%02X ej_D=%02X"",
        addr, value, cpy_post, pc, romOff, A, X, Y, ej_U, ej_D)
end
emu.addMemoryCallback(_cpyWriteSnap, emu.callbackType.write, 0x006A)
emu.addMemoryCallback(_cpyWriteSnap, emu.callbackType.write, 0x006B)

-- Per-frame: track player position, arm replay on level start (0 -> non-zero),
-- then advance the replay from NES-local physics movement.  The PF path is
-- deliberately not used as an ongoing clock.
emu.addEventCallback(function()
    if #replay == 0 then
        emu.drawString(8, 8, ""REPLAY: file empty or missing"", 0xFF6666, 0x000000)
        return
    end

    -- Only draw/advance overlay while actively in gameplay; clear paths
    -- immediately when the game transitions to level-complete/menu states.
    local gameState = emu.read(ADDR_GAMESTATE, emu.memType.nesMemory) or 0
    if gameState ~= STATE_GAME then
        if armed or nesTrailCount > 0 then
            clearPathOverlay()
        end
        return
    end

    -- _player_x / _player_y are screen-relative; add scroll to get NES world
    -- coords, then subtract nesYOffset to convert into pathfinder world coords
    -- (matches PathfinderEngine PathPoints which use the editor's row 0 as
    -- the origin, while the NES build skips top map rows).
    local scrollX0 = emu.read32(0x04A6, emu.memType.nesMemory) or 0
    local sy_raw0  = emu.read16(0x04AA, emu.memType.nesMemory) or 0
    local scrollX1 = emu.read32(0x053E, emu.memType.nesMemory) or 0
    local sy_raw1  = emu.read16(0x0542, emu.memType.nesMemory) or 0
    local scrollX = scrollX0
    local sy_raw = sy_raw0
    if scrollX0 == 0 and sy_raw0 == 0 and (scrollX1 ~= 0 or sy_raw1 ~= 0) then
        scrollX = scrollX1
        sy_raw = sy_raw1
    end
    local scrollY = (sy_raw & 0xFF) + ((sy_raw >> 8) & 0xFF) * 240
    local rawX = emu.read16(0x043D, emu.memType.nesMemory) or 0
    local rawY = emu.read16(0x0441, emu.memType.nesMemory) or 0
    local px = scrollX + (rawX >> 8) + 8
    local py = scrollY + (rawY >> 8) + 8 - nesYOffset
	-- player_x[0] is not committed until after some overloaded frames.  The
	-- live currplayer coordinates already contain the completed physics tick
	-- and therefore provide a stable replay clock through those frames.
	local clockRawX = emu.read16(0x0068, emu.memType.nesMemory) or 0
	local clockRawY = emu.read16(0x006A, emu.memType.nesMemory) or 0
	local clockPx = scrollX + (clockRawX >> 8) + 8
	local clockPy = scrollY + (clockRawY >> 8) + 8 - nesYOffset

	-- Detect level start / respawn from the same stable coordinate used by the
	-- replay clock.  player_x[0] can briefly lag by more than 16 pixels on an
	-- overloaded sprite/spider-orb frame even though currplayer_x is monotonic;
	-- treating that lag as a respawn resets the replay in the middle of a level.
	local resetPx = px
	local resetDual = emu.read(0x0096, emu.memType.nesMemory) or 0
	if resetDual == 0 and clockRawX ~= 0 then resetPx = clockPx end
	if (prevPx <= 0 or resetPx < prevPx - 16) and resetPx > 0 then
        cursor = 1
        armed = true
        frameIdx = 0
        replayClockStarted = false
        replayClockPrevPx = clockPx
        -- Reset the actual-NES trail on respawn so old trails don't linger.
        nesTrail = {{}}
        nesTrailHead = 1
        nesTrailCount = 0
        -- Trace file: APPEND on respawn so death-frames are preserved across
        -- attempts.  Open in ""a"" the first time, write a separator on each
        -- arm so attempts are visually grouped.  This way comparing the trace
        -- against PF can see the frames immediately preceding NES-death.
        if ENABLE_LOGGING then
            if not traceFp then
                traceFp = io.open(traceFile, ""w"")
                if traceFp then
                    traceFp:write(""nes_y_offset,"" .. tostring(nesYOffset) .. ""\n"")
                    traceFp:write(""rom_frame,sim_cursor,px,py,a_next,a_cur,raw_x,raw_y,scrollx,scrolly,vel_y,table_idx,gravity_mod,dashing,gamemode,scroll_y_subpx,framerate,tgt_scroll_y,cp_y,scroll_y_raw,cube_data,death_pc,death_ctx,collmap_r8,mini,cp_gravity,nocamlock,nocamlockforced,min_scroll_y,dual,orbed,jblocked,hblocked,fblocked,ninjajumps\n"")
                end
            else
                traceFp:write(""# --- respawn ---\n"")
                traceFp:flush()
            end
            if not orbDbgFp then
                orbDbgFp = io.open(orbDbgFile, ""w"")
                if orbDbgFp then
                    orbDbgFp:write(""# NES sprite/orb decision dump.  Per-frame: ENTER + per-slot SLOT lines.\n"")
                    orbDbgFp:write(""# Symbol RAM addrs: Generic=0x5C8 (x,y,w,h), Generic2=0x5CC (x,y,w,h)\n"")
                    orbDbgFp:write(""#   activesprites_x_lo=0x4DB[16], _x_hi=0x4EB[16] (world X 16-bit)\n"")
                    orbDbgFp:write(""#   activesprites_y_lo=0x4FB[16], _y_hi=0x50B[16] (world Y 16-bit)\n"")
                    orbDbgFp:write(""#   activesprites_type=0x51B[16]\n"")
                    orbDbgFp:write(""#   activesprites_realx=0x54B[16] (1 byte, screen-local draw x)\n"")
                    orbDbgFp:write(""#   activesprites_realy=0x55B[16] (1 byte, screen-local draw y)\n"")
                    orbDbgFp:write(""#   activesprites_active=0x56B[16], _activated=0x57B[16]\n"")
                    orbDbgFp:write(""#   currplayer_x=0x68 (16-bit), currplayer_y=0x6A (16-bit)\n"")
                    orbDbgFp:write(""#   currplayer_vel_y=0x6E (16-bit signed), currplayer_table_idx=0x79\n"")
                    orbDbgFp:write(""#   currplayer_gravity=0x70, currplayer_mini=0x67, cube_data=0x7B\n"")
                    orbDbgFp:write(""#   dashing=0x4D4, orbactive=0x4C6, orbhitonthisframe=0x451\n"")
                    orbDbgFp:write(""#   index=0x92 (last sprite slot tested), max_loaded_sprites=16\n"")
                    orbDbgFp:write(""#   sprite tables: widths=0xA811, heights=0xA711, x_offset=0xA911, y_offset=0xAA11\n"")
                end
            else
                orbDbgFp:write(""# --- respawn ---\n"")
                orbDbgFp:flush()
            end
            if not physDbgFp then
                physDbgFp = io.open(physDbgFile, ""w"")
                if physDbgFp then
                    physDbgFp:write(""# NES physics-routine entry/exit log.  Per-frame: F=<frame> sim=<cursor>\n"")
                    physDbgFp:write(""# followed by 0..N TAG lines (entry: <name>.in / exit: <name>.out).\n"")
                    physDbgFp:write(""# Fields: tag pc=PC A/X/Y cpx cpy cpvx cpvy G=(x,y,wxh) ej_U ej_D col probe=(tx,ty,tr) mini grav tidx cd sx sy SLOPE[cpsF cpswOn cpsT cplst zsF zsT zswOn mcjh glst]\n"")
                    physDbgFp:write(""# Probes: $93=temp_x $94=temp_y $95=temp_room $81=collision\n"")
                    physDbgFp:write(""# Eject:  $8D=eject_U $8C=eject_D (currplayer_y_high -= eject_U+1 / += eject_D+1)\n"")
                end
            else
                physDbgFp:write(""# --- respawn ---\n"")
                physDbgFp:flush()
            end
        end
    end
	prevPx = resetPx

	-- In single-player, trace/draw the live coordinate as well.  This avoids a
	-- false one-frame backward spike while player_x[0] is awaiting its commit.
	-- Dual keeps using player_x[0], since currplayer may be P2 at endFrame.
	local dualClock = emu.read(0x0096, emu.memType.nesMemory) or 0
	local samplePx = px
	local samplePy = py
	local sampleRawX = rawX
	local sampleRawY = rawY
	if armed and dualClock == 0 and clockRawX ~= 0 then
		samplePx = clockPx
		samplePy = clockPy
		sampleRawX = clockRawX
		sampleRawY = clockRawY
	end

    -- Append current NES position to the actual-trail ring buffer.
    if armed then
        local holdBits = emu.read(ADDR_JOYPAD1_HOLD, emu.memType.nesMemory) or 0
        local heldNow = ((holdBits & (PAD_A | PAD_UP)) ~= 0)
        nesTrail[nesTrailHead] = {{ x = samplePx, y = samplePy, a = heldNow and 1 or 0, ra = lastA and 1 or 0 }}
        nesTrailHead = nesTrailHead + 1
        if nesTrailHead > nesTrailMax then nesTrailHead = 1 end
        if nesTrailCount < nesTrailMax then nesTrailCount = nesTrailCount + 1 end
    end

    if armed then
		local plausiblePx = clockPx >= 0 and clockPx < 524288
		local movedThisFrame = plausiblePx and replayClockPrevPx ~= nil and clockPx ~= replayClockPrevPx

        -- Pathfinder applies the NES intro-freeze pre-step before recording
        -- PathPoints[0].  Latch when NES first reaches that same initial X,
        -- then use only actual NES movement as the replay clock.  This one-time
        -- gate handles startup/lag frames without letting a later PF position
        -- mismatch delay or accelerate the injected input stream.
        if not replayClockStarted then
            local firstEntry = replay[1]
			if movedThisFrame and firstEntry and clockPx >= firstEntry.x then
                replayClockStarted = true
                cursor = 1
            end
        elseif movedThisFrame and cursor < #replay then
            cursor = cursor + 1
        end
		if plausiblePx then replayClockPrevPx = clockPx end

        -- Frame alignment: PathPoints[cursor] is the post-physics position for
        -- sim frame `cursor`, produced by Inputs[cursor]. We've just observed
        -- that position in endFrame N. The next emulator inputPolled will be
        -- for frame N+1, so we must feed Inputs[cursor+1] -- otherwise the
        -- press lands one frame late (jumps fire after the spike).
        local curEntry = replay[cursor]
        local nextEntry
        if replayClockStarted then
            nextEntry = replay[cursor + 1] or replay[cursor]
        else
            -- Feed Inputs[0] through the hidden intro pre-step.  Once the first
            -- recorded PF/NES position is observed, the normal +1 phase applies.
            nextEntry = replay[1]
        end
        local curA = curEntry and (curEntry.a == 1) or false
        lastA = nextEntry and (nextEntry.a == 1) or false

        -- Append per-frame trace row.
        if traceFp then
            -- Per-frame dump of NES physics state for divergence analysis.
            -- Symbol locations from BUILD/main/famidash.dbg:
            --   _gamemode             abs (varies; read via debugger pref)
            --   _scroll_y_subpx       abs (8-bit)
            local vel_y_raw = emu.read16(0x006E, emu.memType.nesMemory) or 0
            local vel_y = vel_y_raw
            if vel_y >= 0x8000 then vel_y = vel_y - 0x10000 end
            local table_idx = emu.read(0x0079, emu.memType.nesMemory) or 0
            local gravity_mod = emu.read(0x05C2, emu.memType.nesMemory) or 0
            local dashing = emu.read(0x04D4, emu.memType.nesMemory) or 0
            local gamemode = emu.read(0x007A, emu.memType.nesMemory) or 0
            local scroll_y_subpx = emu.read(0x04AC, emu.memType.nesMemory) or 0
            local framerate_v = emu.read(0x002D, emu.memType.nesMemory) or 0
            local tgt_scroll_y = emu.read16(0x04AF, emu.memType.nesMemory) or 0
            local cp_y_raw = emu.read16(0x006A, emu.memType.nesMemory) or 0
            local cp_y = cp_y_raw
            if cp_y >= 0x8000 then cp_y = cp_y - 0x10000 end
            local scroll_y_raw = sy_raw
            local cube_data0 = emu.read(0x007B, emu.memType.nesMemory) or 0
            local mini_flag = emu.read(0x0067, emu.memType.nesMemory) or 0
            local cp_gravity = emu.read(0x0070, emu.memType.nesMemory) or 0
            local nocamlock = emu.read(0x0495, emu.memType.nesMemory) or 0
            local nocamlockforced = emu.read(0x0496, emu.memType.nesMemory) or 0
            local min_scroll_y = emu.read16(0x0363, emu.memType.nesMemory) or 0
            local dual_v = emu.read(0x0096, emu.memType.nesMemory) or 0
			local orbed_v = emu.read(0x0460, emu.memType.nesMemory) or 0
			local jblocked_v = emu.read(0x0477, emu.memType.nesMemory) or 0
			local hblocked_v = emu.read(0x0475, emu.memType.nesMemory) or 0
			local fblocked_v = emu.read(0x0479, emu.memType.nesMemory) or 0
			local ninjajumps_v = emu.read(0x047B, emu.memType.nesMemory) or 0
            -- Dump collMap row 8 of all 4 rooms (entries $80..$8F) where the
            -- ship-section spike-collision probes typically land at this scroll
            -- depth.  Format: ""r0:[hex16]|r1:[hex16]|r2:[hex16]|r3:[hex16]"".
            local cm_addrs = {{0x6080, 0x6180, 0x6280, 0x6380}}
            local cm_str = """"
            for ri, base in ipairs(cm_addrs) do
                if ri > 1 then cm_str = cm_str .. ""|"" end
                cm_str = cm_str .. ""r"" .. tostring(ri-1) .. "":""
                for k = 0, 15 do
                    cm_str = cm_str .. string.format(""%02X"", emu.read(base + k, emu.memType.nesMemory) or 0)
                end
            end
            traceFp:write(string.format(""%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%s,%s,%s,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d\n"",
				frameIdx, cursor, samplePx, samplePy, lastA and 1 or 0, curA and 1 or 0,
				sampleRawX, sampleRawY, scrollX, scrollY,
                vel_y, table_idx, gravity_mod, dashing, gamemode, scroll_y_subpx,
                framerate_v, tgt_scroll_y, cp_y, scroll_y_raw, cube_data0,
                cubeDataDeathPC and string.format(""%04X"", cubeDataDeathPC) or """",
                cubeDataDeathCtx or """",
                cm_str, mini_flag, cp_gravity,
                nocamlock, nocamlockforced, min_scroll_y, dual_v,
				orbed_v, jblocked_v, hblocked_v, fblocked_v, ninjajumps_v))
            traceFp:flush()
            -- Reset sticky death PC after we've logged it once with cube_data=1.
            if cube_data0 == 0 then
                cubeDataDeathPC = nil
                cubeDataDeathCtx = nil
            end
        end

        -- ── Physics-routine event flush ─────────────────────────────────
        -- Drain the per-frame ring of physics-routine entry/exit events
        -- captured by the addMemoryCallback hooks above into physDbgFile.
        -- Header line ties events to (rom_frame, sim_cursor) so the log can
        -- be cross-referenced 1:1 with the per-frame trace above.
        if physDbgFp and physEventCount > 0 then
            physDbgFp:write(string.format(""F=%d sim=%d events=%d\n"",
                frameIdx, cursor, physEventCount))
            for i = 1, physEventCount do
                physDbgFp:write(""  "")
                physDbgFp:write(physEvents[i])
                physDbgFp:write(""\n"")
            end
            physDbgFp:flush()
        end
        physEventCount = 0

        -- ── NES orb-debug dump ─────────────────────────────────────────
        -- Mirror of PF's famidash_pf_orb_debug.log.  For each frame within
        -- ORB_DBG_LO..ORB_DBG_HI, dump the full sprite ring buffer and the
        -- player/sprite hitboxes.  Match by ""f={{frame}}"" against PF.
        -- frame in this log == sim_cursor (NES emulator frame counts the
        -- replay-driven sim ticks).  Use ""sim={{cursor}}"" tag for explicit
        -- alignment with PF's f={{frameCounter}} (which IS sim_cursor).
        if orbDbgFp and cursor >= ORB_DBG_LO and cursor <= ORB_DBG_HI then
            local M = emu.memType.nesMemory
            local function rd8(a)  return emu.read(a, M)   or 0 end
            local function rd16(a) return emu.read16(a, M) or 0 end
            local function s16(v)  if v >= 0x8000 then return v - 0x10000 else return v end end

            local g_x  = rd8(0x5C8); local g_y  = rd8(0x5C9)
            local g_w  = rd8(0x5CA); local g_h  = rd8(0x5CB)
            local g2_x = rd8(0x5CC); local g2_y = rd8(0x5CD)
            local g2_w = rd8(0x5CE); local g2_h = rd8(0x5CF)
            local cpx = rd16(0x68); local cpy = rd16(0x6A)
            local cpvy = s16(rd16(0x6E))
            local cp_grav = rd8(0x70); local cp_mini = rd8(0x67)
            local cp_tidx = rd8(0x79); local cp_cube = rd8(0x7B)
            local dash = rd8(0x4D4); local orbAct = rd8(0x4C6)
            local orbHit = rd8(0x451); local lastIdx = rd8(0x92)
			local gm = rd8(0x7A)
			local orbed = rd8(0x460); local jblock = rd8(0x477)
			local hblock = rd8(0x475); local fblock = rd8(0x479)
			local ninja = rd8(0x47B)
			-- Log the controller bytes the ROM actually consumed as well as the
			-- replay entries above.  The latter describe scheduling; they are not
			-- proof of controllingplayer->hold/press inside movement().
			local joyHold = rd8(0x22); local joyPress = rd8(0x23)
			local controlling = rd8(0x25); local kandoHack = rd8(0x456)
			local retro = rd8(0x7204)

			orbDbgFp:write(string.format(
				""f=%d sim=%d ENTER mode=%d mini=%d grav=%d dash=%d orbAct=%d orbHit=%d lastSprIdx=%d "" ..
				""cpx=0x%04X cpy=0x%04X cpvy=%d cp_tidx=%d cube_data=0x%02X "" ..
				""Generic=(%d,%d,%dx%d) Generic2=(%d,%d,%dx%d) press=%d held=%d "" ..
				""joyHold=0x%02X joyPress=0x%02X controlling=0x%02X kando=%d retro=%d "" ..
				""orbed=%d j=%d h=%d f=%d ninja=%d\n"",
				frameIdx, cursor, gm, cp_mini, cp_grav, dash, orbAct, orbHit, lastIdx,
				cpx, cpy, cpvy, cp_tidx, cp_cube,
				g_x, g_y, g_w, g_h, g2_x, g2_y, g2_w, g2_h,
				lastA and 1 or 0, curA and 1 or 0,
				joyHold, joyPress, controlling, kandoHack, retro,
				orbed, jblock, hblock, fblock, ninja))

            -- Walk all 16 active-sprite slots.  Layout (from famidash.dbg,
            -- expanded by lohi_arr16_decl in arr_macros.h):
            --   _activesprites_x_lo = 0x4DB[16]   _activesprites_x_hi = 0x4EB[16]
            --   _activesprites_y_lo = 0x4FB[16]   _activesprites_y_hi = 0x50B[16]
            --   _activesprites_type = 0x51B[16]
            --   _activesprites_realx= 0x54B[16]   (1 byte, screen-local draw x)
            --   _activesprites_realy= 0x55B[16]   (1 byte, screen-local draw y)
            --   _activesprites_active   = 0x56B[16]
            --   _activesprites_activated= 0x57B[16]
            for sl = 0, 15 do
                local stype = rd8(0x51B + sl)
                local active = rd8(0x56B + sl)
                if active ~= 0 and stype ~= 0xFF then
                    local x_lo = rd8(0x4DB + sl); local x_hi = rd8(0x4EB + sl)
                    local y_lo = rd8(0x4FB + sl); local y_hi = rd8(0x50B + sl)
                    local worldX = x_hi * 256 + x_lo
                    local worldY = y_hi * 256 + y_lo
                    local rx = rd8(0x54B + sl)
                    local ry = rd8(0x55B + sl)
                    local act_flag = rd8(0x57B + sl)
                    -- Sprite-table geometry by type id.  These tables live
                    -- in a swappable bank, but during normal play the bank
                    -- is paged in for sprite_collide, so a best-effort read
                    -- via emu.read often works.  If values look bogus
                    -- (height==0) ignore them.
                    local sw  = rd8(0xA811 + stype)
                    local sh  = rd8(0xA711 + stype)
                    local sxo = rd8(0xA911 + stype)
                    local syo = rd8(0xAA11 + stype)
                    if sxo >= 0x80 then sxo = sxo - 0x100 end
                    if syo >= 0x80 then syo = syo - 0x100 end
                    orbDbgFp:write(string.format(
                        ""f=%d sim=%d SLOT %02d type=0x%02X worldX=%d worldY=%d "" ..
                        ""realx=%d realy=%d active=%d activated=%d "" ..
                        ""sw=%d sh=%d sxo=%d syo=%d\n"",
                        frameIdx, cursor, sl, stype, worldX, worldY,
                        rx, ry, active, act_flag,
                        sw, sh, sxo, syo))
                end
            end
            orbDbgFp:flush()
        end
        frameIdx = frameIdx + 1
    else
        lastA = false
    end

    if SHOW_PATHLINES then
    -- Live debug overlay (text removed; paths only)
    local color = armed and 0x00FF66 or 0xFFAA00

    -- Draw the predicted pathfinder path as a polyline directly on the NES
    -- framebuffer. PF coords -> NES screen coords:
    --   screenX = pf_x - scrollX
    --   screenY = pf_y - scrollY + nesYOffset
    -- (Both lines centered on the hitbox center, so no extra +/- 8 needed.)
    -- We draw a window of points around the current cursor to keep things fast.
    local function pf2sx(x) return x - scrollX end
    local function pf2sy(y) return y - scrollY + nesYOffset end
    local gamemodeOverlay = emu.read(0x007A, emu.memType.nesMemory) or 0
    local isWaveMode = (gamemodeOverlay == 6)
    local function drawPfSegment(ax, ay, bx, by, color, thickness)
        emu.drawLine(ax, ay, bx, by, color)
        local radius = math.floor((thickness - 1) / 2)
        if radius <= 0 then return end
        local dx = bx - ax
        local dy = by - ay
        for i = 1, radius do
            if math.abs(dx) >= math.abs(dy) then
                emu.drawLine(ax, ay - i, bx, by - i, color)
                emu.drawLine(ax, ay + i, bx, by + i, color)
            else
                emu.drawLine(ax - i, ay, bx - i, by, color)
                emu.drawLine(ax + i, ay, bx + i, by, color)
            end
        end
    end
    local function drawFilledCircle(cx, cy, radius, color)
        for dy = -radius, radius do
            local span = math.floor(math.sqrt(radius * radius - dy * dy) + 0.5)
            emu.drawLine(cx - span, cy + dy, cx + span, cy + dy, color)
        end
    end
    local first = math.max(1, cursor - 60)
    local last  = math.min(#replay, cursor + 240)
    local prev = replay[first]
    if prev then
        local pxA, pyA = pf2sx(prev.x), pf2sy(prev.y)
        for i = first + 1, last do
            local cur = replay[i]
            local pxB, pyB = pf2sx(cur.x), pf2sy(cur.y)
            -- Only draw segments where at least one endpoint is on screen.
            if (pxA >= -8 and pxA <= 264 and pyA >= -8 and pyA <= 248)
               or (pxB >= -8 and pxB <= 264 and pyB >= -8 and pyB <= 248) then
                local segColor = (i <= cursor) and 0xFF44CC or 0x66CCFF
                local held = ((prev.a or 0) ~= 0) or ((cur.a or 0) ~= 0)
                local thickness = (held and (not isWaveMode)) and 3 or 1
                drawPfSegment(pxA, pyA, pxB, pyB, segColor, thickness)
                local pressedFresh = ((prev.a or 0) == 0) and ((cur.a or 0) ~= 0)
                if (not isWaveMode) and pressedFresh then
                    drawFilledCircle(pxB, pyB, 4, 0xFFA500) -- orange click marker
                end
            end
            pxA, pyA = pxB, pyB
            prev = cur
        end
        -- Highlight the current cursor entry.
        local c = replay[cursor]
        if c then
            local cx, cy = pf2sx(c.x), pf2sy(c.y)
            emu.drawRectangle(cx - 2, cy - 2, 5, 5, 0xFFFF00, true)
        end
    end

    -- Draw projected P2 only while the ROM dual flag is live.  The exported
    -- P2 table contains only dual-active points, with (-1,-1) sentinels
    -- splitting segments after single portals.
    local dualOverlay = emu.read(0x0096, emu.memType.nesMemory) or 0
    if dualOverlay ~= 0 and #replay2 >= 2 then
        local currentX = replay[cursor] and replay[cursor].x or px
        local prev2 = nil
        for i = 1, #replay2 do
            local cur2 = replay2[i]
            if not cur2 or cur2.x < 0 or cur2.y < 0 then
                prev2 = nil
            else
                if prev2 then
                    local nearWindow = (cur2.x >= currentX - 256 and cur2.x <= currentX + 768)
                                    or (prev2.x >= currentX - 256 and prev2.x <= currentX + 768)
                    if nearWindow then
                        local ax2, ay2 = pf2sx(prev2.x), pf2sy(prev2.y)
                        local bx2, by2 = pf2sx(cur2.x), pf2sy(cur2.y)
                        if (ax2 >= -8 and ax2 <= 264 and ay2 >= -8 and ay2 <= 248)
                           or (bx2 >= -8 and bx2 <= 264 and by2 >= -8 and by2 <= 248) then
                            drawPfSegment(ax2, ay2, bx2, by2, 0x00FF99, 2)
                        end
                    end
                end
                prev2 = cur2
            end
        end
    end

    -- Draw the ACTUAL NES position trail in YELLOW (0xFFFF00) so divergence
    -- from the PF projection (purple/cyan) is visible.  Walk the ring buffer
    -- in chronological order from oldest to newest.
    if nesTrailCount >= 2 then
        local startIdx
        if nesTrailCount < nesTrailMax then
            startIdx = 1
        else
            startIdx = nesTrailHead  -- oldest entry slot (next to be overwritten)
        end
        local prevEntry = nesTrail[startIdx]
        local idx = startIdx
        for step = 2, nesTrailCount do
            idx = idx + 1
            if idx > nesTrailMax then idx = 1 end
            local curEntry = nesTrail[idx]
            if prevEntry and curEntry then
                local axS, ayS = pf2sx(prevEntry.x), pf2sy(prevEntry.y)
                local bxS, byS = pf2sx(curEntry.x), pf2sy(curEntry.y)
                if (axS >= -8 and axS <= 264 and ayS >= -8 and ayS <= 248)
                   or (bxS >= -8 and bxS <= 264 and byS >= -8 and byS <= 248) then
                    local thickHeld = ((prevEntry.a or 0) ~= 0) or ((curEntry.a or 0) ~= 0)
                    local thickReplay = ((prevEntry.ra or 0) ~= 0) or ((curEntry.ra or 0) ~= 0)
                    local thickness = ((thickHeld or thickReplay) and (not isWaveMode)) and 3 or 1
                    drawPfSegment(axS, ayS, bxS, byS, 0xFFFF00, thickness)
                    local pressedHeldFresh = ((prevEntry.a or 0) == 0) and ((curEntry.a or 0) ~= 0)
                    local pressedReplayFresh = ((prevEntry.ra or 0) == 0) and ((curEntry.ra or 0) ~= 0)
                    if (not isWaveMode) and (pressedHeldFresh or pressedReplayFresh) then
                        drawFilledCircle(bxS, byS, 4, 0xFFFF00)
                    end
                end
            end
            prevEntry = curEntry
        end
    end
    if armed and replay[cursor] then
        -- (debug text overlay removed)
    end
    end
end, emu.eventType.endFrame)

-- Counter so we can prove inputPolled is firing and our setInput was called.
local pollCount = 0
local pressCount = 0
emu.addEventCallback(function()
    pollCount = pollCount + 1
    if not armed then return end
    if lastA then pressCount = pressCount + 1 end
    -- Mesen2 setInput signature: setInput(inputTable, port, subport)
    -- (the public wiki incorrectly lists port first; the C++ source reads
    -- the table from stack slot 1 with port/subport popped from the top.)
    emu.setInput({{
        a = lastA, b = false,
        select = false, start = false,
        up = false, down = false, left = false, right = false
    }}, 0)
end, emu.eventType.inputPolled)

emu.addEventCallback(function()
    -- (debug text overlay removed)
end, emu.eventType.endFrame)
";
            return replayPart;
        }
    }
}
