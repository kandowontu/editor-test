using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Media;

namespace FamidashEditor
{
    public partial class MainWindow
    {
        // -----------------------------------------------------------------------
        // Overlay options (persisted via editor settings)
        // -----------------------------------------------------------------------
        private bool _overlayAndFollow = true;
        private bool _camFollow        = true;

        // -----------------------------------------------------------------------
        // Runtime state
        // -----------------------------------------------------------------------
        private Process?        _mesenOverlayProcess;
        private nint            _mesenHwnd;
        private int             _latestNesScrollX;
        private int             _latestNesScrollY;
        private int             _rawNesScrollX = int.MinValue;
        private CancellationTokenSource? _overlayReadCts;
        private Task?           _overlayReadTask;
        private int             _lastMesenScreenX = int.MinValue;
        private int             _lastMesenScreenY = int.MinValue;
        private int             _lastMesenWinW = int.MinValue;
        private int             _lastMesenWinH = int.MinValue;
        private double          _lastScrollOffsetX = double.NaN;
        private double          _lastScrollOffsetY = double.NaN;
        private double          _fixedAnchorViewportX = double.NaN;
        private double          _fixedAnchorViewportY = double.NaN;
        private double          _initialCameraCanvasY = double.NaN;
        private double          _smoothScrollX = double.NaN;
        private double          _smoothScrollTargetX = double.NaN;
        private int             _smoothScrollCoarseXPx = int.MinValue;
        private int             _smoothScrollShiftXPx = int.MinValue;
        private TranslateTransform? _smoothScrollTransform;
        private bool            _overlayRenderHooked = false;
        // ── Per-level replay/trace paths ────────────────────────────────────
        // All Mesen-related files live in My Documents under a per-level folder.
        //   <Documents>/Famidash Editor/Replays/<level>/famidash_overlay.lua
        //   <Documents>/Famidash Editor/Replays/<level>/famidash_overlay_scroll.txt
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

        private string ScrollTempFile =>
            Path.Combine(CurrentReplayDir, "famidash_overlay_scroll.txt");

        internal string OverlayLuaPath =>
            Path.Combine(CurrentReplayDir, "famidash_overlay.lua");

        internal string ReplayTempFile =>
            Path.Combine(CurrentReplayDir, "famidash_replay.csv");

        // Mesen trace is an output written by the overlay Lua; it is not read
        // back by the overlay or the replay pipeline, so it lives in %TEMP%
        // alongside the other compare/debug artefacts instead of cluttering the
        // per-level Replays folder.
        internal string MesenTraceFile =>
            Path.Combine(Path.GetTempPath(), "famidash_mesen_trace.csv");

        // Comprehensive per-frame orb/sprite-state dump for PF↔NES divergence
        // diagnosis.  Written by the overlay Lua alongside the regular trace.
        // Includes Generic/Generic2 hitboxes (player + most-recent sprite tested),
        // every active sprite slot's type/realx/realy/active/activated, and key
        // physics scalars.  See AppendOrbDebugLuaSnippet() in the overlay Lua.
        internal string MesenOrbDebugFile =>
            Path.Combine(Path.GetTempPath(), "famidash_mesen_orb_debug.log");

        // Per-frame log of NES physics-routine entries (movement / eject /
        // collision).  Captures pre-call state of currplayer_y, currplayer_vel_y,
        // Generic.x/y/w/h, eject_U/D, collision, temp_x/y/room, scroll_x/y at
        // every entry to ufo_ship_eject / cube_eject / ball_eject / spider_eject /
        // bg_coll_U / bg_coll_D / bg_coll_R / bg_coll_L / bg_coll_death /
        // x_movement_coll / x_movement / common_gravity_routine / *_movement.
        // PCs are baked from BUILD/huge/famidash.dbg at script-generation time;
        // if the ROM is rebuilt with shifted addresses the C# side must regenerate.
        internal string MesenPhysicsDebugFile =>
            Path.Combine(Path.GetTempPath(), "famidash_mesen_physics_debug.log");

        // -----------------------------------------------------------------------
        // P/Invoke
        // -----------------------------------------------------------------------
        private delegate bool EnumWindowsProc(nint hWnd, nint lParam);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hWnd);

        private const uint SWP_NOACTIVATE  = 0x0010;
        private const uint SWP_NOZORDER    = 0x0004;
        private const uint SWP_SHOWWINDOW  = 0x0040;
        private static readonly nint HWND_TOPMOST = new nint(-1);

        // -----------------------------------------------------------------------
        // Public API called by OpenRomInMesen after the process is launched
        // -----------------------------------------------------------------------
        internal void StartMesenOverlay(Process proc)
        {
            _mesenOverlayProcess = proc;
            _mesenHwnd           = 0;
            _latestNesScrollX    = 0;
            _latestNesScrollY    = 0;
            _rawNesScrollX       = int.MinValue;
            _lastMesenWinW       = int.MinValue;
            _lastMesenWinH       = int.MinValue;
            _fixedAnchorViewportX = double.NaN;
            _fixedAnchorViewportY = double.NaN;
            _initialCameraCanvasY = double.NaN;
            _smoothScrollX = double.NaN;
            _smoothScrollTargetX = double.NaN;
            _smoothScrollCoarseXPx = int.MinValue;
            _smoothScrollShiftXPx = int.MinValue;
            ClearSmoothEditorHorizontalShift();

            _overlayReadCts = new CancellationTokenSource();
            _overlayReadTask = OverlayReadLoop(_overlayReadCts.Token);

            if (!_overlayRenderHooked)
            {
                CompositionTarget.Rendering += OverlayRendering_Tick;
                _overlayRenderHooked = true;
            }

            // Watch for process exit and clean up
            Task.Run(() =>
            {
                try { proc.WaitForExit(); } catch { }
                Dispatcher.BeginInvoke(StopMesenOverlay);
            });
        }

        internal void StopMesenOverlay()
        {
            string scrollFile = ScrollTempFile; // capture before state is torn down
            try { _overlayReadCts?.Cancel(); } catch { }
            _overlayReadCts      = null;
            _overlayReadTask     = null;
            if (_overlayRenderHooked)
            {
                try { CompositionTarget.Rendering -= OverlayRendering_Tick; } catch { }
                _overlayRenderHooked = false;
            }
            Process? proc = _mesenOverlayProcess;
            _mesenHwnd           = 0;
            _mesenOverlayProcess = null;
            _lastMesenScreenX    = int.MinValue;
            _lastMesenScreenY    = int.MinValue;
            _lastMesenWinW       = int.MinValue;
            _lastMesenWinH       = int.MinValue;
            _rawNesScrollX       = int.MinValue;
            _lastScrollOffsetX   = double.NaN;
            _lastScrollOffsetY   = double.NaN;
            _fixedAnchorViewportX = double.NaN;
            _fixedAnchorViewportY = double.NaN;
            _initialCameraCanvasY = double.NaN;
            _smoothScrollX = double.NaN;
            _smoothScrollTargetX = double.NaN;
            _smoothScrollCoarseXPx = int.MinValue;
            _smoothScrollShiftXPx = int.MinValue;
            ClearSmoothEditorHorizontalShift();
            try
            {
                if (proc != null && !proc.HasExited)
                {
                    proc.Kill(true);
                }
            }
            catch { }
            try { if (File.Exists(scrollFile)) File.Delete(scrollFile); } catch { }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            try { StopMesenOverlay(); } catch { }
            base.OnClosing(e);
        }

        // -----------------------------------------------------------------------
        // Background scroll reader loop (keeps file I/O off the UI thread)
        // -----------------------------------------------------------------------
        private async Task OverlayReadLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                if (_mesenOverlayProcess == null || _mesenOverlayProcess.HasExited)
                {
                    break;
                }

                try
                {
                    if (File.Exists(ScrollTempFile))
                    {
                        string txt;
                        using (var fs = new FileStream(ScrollTempFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                        using (var sr = new StreamReader(fs))
                        {
                            txt = sr.ReadToEnd().Trim();
                        }
                        string[] parts = txt.Split(',');
                        if (parts.Length >= 2
                            && int.TryParse(parts[0].Trim(), out int vx)
                            && int.TryParse(parts[1].Trim(), out int vy))
                        {
                            int candidateX = Math.Max(0, vx);
                            // Geometry Dash-style camera X should be monotonic during play.
                            // Treat large backward jumps as restart/death (rebase), and ignore
                            // small backward deltas as sampling jitter.
                            const int resetBackJumpPx = 64;
                            if (_rawNesScrollX == int.MinValue)
                            {
                                _rawNesScrollX = candidateX;
                                _latestNesScrollX = candidateX;
                            }
                            else if (candidateX + resetBackJumpPx < _rawNesScrollX)
                            {
                                // Restart/death or hard seek: accept backward jump and rebase.
                                _rawNesScrollX = candidateX;
                                _latestNesScrollX = candidateX;
                            }
                            else
                            {
                                _rawNesScrollX = Math.Max(_rawNesScrollX, candidateX);
                                _latestNesScrollX = _rawNesScrollX;
                            }
                            _latestNesScrollY = vy;
                        }
                    }
                }
                catch { }

                try
                {
                    await Task.Delay(1, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        private void OverlayRendering_Tick(object? sender, EventArgs e)
        {
            if (!_overlayAndFollow) return;
            if (_mesenOverlayProcess == null || _mesenOverlayProcess.HasExited) return;

            if (_mesenHwnd == 0)
            {
                _mesenHwnd = FindWindowForProcess(_mesenOverlayProcess.Id);
                if (_mesenHwnd == 0) return;
            }

            RepositionMesenWindow();
        }

        // -----------------------------------------------------------------------
        // Reposition & resize the Mesen window to overlay the editor canvas
        // -----------------------------------------------------------------------
        private void RepositionMesenWindow()
        {
            if (_mesenHwnd == 0 || CanvasHost == null) return;

            try
            {
                var    dpi     = VisualTreeHelper.GetDpi(this);
                double zoom    = ZoomSlider?.Value ?? 1.0;
                double scrollX = _latestNesScrollX;
                double scrollY = _latestNesScrollY;

                const int NesHeightPixels = 240;
                const int NesWidthPixels  = 256;
                const int YCalibrationTiles = -33;
                const int XLeadTiles = 2;
                const int WindowSizeNudgePixels = 0;
                double cameraCanvasX = mapViewportPadding + scrollX * zoom;
                double cameraCanvasY = mapViewportPadding + (scrollY + (3 * TileSize)) * zoom + gridRenderShiftY;

                // Emulator size in editor logical units / physical pixels.
                double emuLogicalW = (NesWidthPixels + WindowSizeNudgePixels) * zoom;
                double emuLogicalH = (NesHeightPixels + WindowSizeNudgePixels) * zoom;
                int winW = (int)(emuLogicalW * dpi.DpiScaleX);
                int winH = (int)(emuLogicalH * dpi.DpiScaleY);

                double vpW = SafeViewportWidth();
                double vpH = SafeViewportHeight();
                double centeredAnchorX = Math.Max(0.0, (vpW - emuLogicalW) / 2.0);
                double centeredAnchorY = Math.Max(0.0, (vpH - emuLogicalH) / 2.0);

                if (double.IsNaN(_fixedAnchorViewportX) || double.IsNaN(_fixedAnchorViewportY))
                {
                    _fixedAnchorViewportX = Math.Min(centeredAnchorX, mapViewportPadding);
                    _fixedAnchorViewportY = Math.Min(centeredAnchorY, mapViewportPadding + (3 * TileSize) * zoom + gridRenderShiftY);
                    // Rebase vertical follow to the current rendered camera position.
                    _initialCameraCanvasY = cameraCanvasY;
                }

                double anchorViewportX = _fixedAnchorViewportX;
                double yCalibrationPx = YCalibrationTiles * TileSize * zoom;
                double baseAnchorViewportY = _fixedAnchorViewportY;
                double anchorViewportY = baseAnchorViewportY;

                if (_camFollow && MapScrollViewer != null)
                {
                    double xLeadPx = XLeadTiles * TileSize * zoom;
                    double targetOffsetX = Math.Max(0.0, cameraCanvasX - anchorViewportX + xLeadPx);
                    // Stable follow: apply direct pixel-aligned offset (no spring/easing).
                    UpdateSmoothEditorHorizontalScroll(targetOffsetX);
                    _lastScrollOffsetX = targetOffsetX;
                    _lastScrollOffsetY = MapScrollViewer.VerticalOffset;

                    // Vertical behavior: move window by rendered camera delta from its startup baseline.
                    if (double.IsNaN(_initialCameraCanvasY))
                    {
                        _initialCameraCanvasY = cameraCanvasY;
                    }
                    double yDelta = cameraCanvasY - _initialCameraCanvasY;
                    anchorViewportY = baseAnchorViewportY + yDelta;
                }
                else
                {
                    ClearSmoothEditorHorizontalShift();
                }

                // Apply explicit user calibration after camera-delta follow.
                anchorViewportY = anchorViewportY + yCalibrationPx;

                // (Tall-level Y compensation removed; Mesen window position
                // is correct for all level heights without extra offset.)

                // Pixel-align the anchor to device pixels to avoid 1-2px drift from
                // sub-pixel accumulation during vertical camera movement.
                double alignedAnchorX = Math.Round(anchorViewportX * dpi.DpiScaleX) / dpi.DpiScaleX;
                double alignedAnchorY = Math.Round(anchorViewportY * dpi.DpiScaleY) / dpi.DpiScaleY;

                // Place Mesen at the computed viewport anchor.
                Point screenTopLeft = MapScrollViewer != null
                    ? MapScrollViewer.PointToScreen(new Point(alignedAnchorX, alignedAnchorY))
                    : CanvasHost.PointToScreen(new Point(alignedAnchorX, alignedAnchorY));

                int newX = (int)Math.Round(screenTopLeft.X);
                int newY = (int)Math.Round(screenTopLeft.Y);
                // Enforce position and size every tick in case Mesen reapplies its own bounds.
                SetWindowPos(_mesenHwnd, HWND_TOPMOST,
                    newX, newY, winW, winH, SWP_NOACTIVATE | SWP_SHOWWINDOW);
                _lastMesenScreenX = newX;
                _lastMesenScreenY = newY;
                _lastMesenWinW = winW;
                _lastMesenWinH = winH;
            }
            catch { }
        }

        private void UpdateSmoothEditorHorizontalScroll(double targetOffsetX)
        {
            var scrollViewer = MapScrollViewer;
            var canvasHost = CanvasHost;
            var mapContentRoot = MapContentRoot;
            if (scrollViewer == null || canvasHost == null || mapContentRoot == null) return;

            var dpi = VisualTreeHelper.GetDpi(this);
            double maxH = Math.Max(0.0, canvasHost.ActualWidth - SafeViewportWidth());
            double clampedTarget = Math.Max(0.0, Math.Min(maxH, targetOffsetX));

            int targetPx = (int)Math.Round(clampedTarget * dpi.DpiScaleX);
            int maxPx = (int)Math.Round(maxH * dpi.DpiScaleX);
            targetPx = Math.Max(0, Math.Min(maxPx, targetPx));

            // Keep ScrollViewer updates infrequent; visible motion comes from
            // integer-pixel transform updates on the full map content each frame.
            const int rebaseStepPx = 512;
            int coarsePx = (targetPx / rebaseStepPx) * rebaseStepPx;
            int shiftPx = targetPx - coarsePx;

            if (_smoothScrollCoarseXPx != coarsePx)
            {
                scrollViewer.ScrollToHorizontalOffset(coarsePx / dpi.DpiScaleX);
                _smoothScrollCoarseXPx = coarsePx;
            }

            if (_smoothScrollShiftXPx != shiftPx)
            {
                var smoothTransform = _smoothScrollTransform ??= new TranslateTransform();
                smoothTransform.X = -(shiftPx / dpi.DpiScaleX);
                smoothTransform.Y = 0.0;
                mapContentRoot.RenderTransform = smoothTransform;
                _smoothScrollShiftXPx = shiftPx;
            }

            _smoothScrollX = targetPx / dpi.DpiScaleX;
            _smoothScrollTargetX = _smoothScrollX;
        }

        private void ClearSmoothEditorHorizontalShift()
        {
            try
            {
                if (MapContentRoot != null)
                {
                    MapContentRoot.RenderTransform = Transform.Identity;
                }
            }
            catch { }
            _smoothScrollCoarseXPx = int.MinValue;
            _smoothScrollShiftXPx = int.MinValue;
            _smoothScrollTransform = null;
        }

        // -----------------------------------------------------------------------
        // Find the first visible top-level window owned by a given process
        // -----------------------------------------------------------------------
        private static nint FindWindowForProcess(int processId)
        {
            nint found = 0;
            EnumWindows((hWnd, _) =>
            {
                GetWindowThreadProcessId(hWnd, out uint pid);
                if ((int)pid == processId && IsWindowVisible(hWnd))
                {
                    found = hWnd;
                    return false; // stop enumeration
                }
                return true;
            }, 0);
            return found;
        }

        // -----------------------------------------------------------------------
        // Lua script embedded as a string — written to a temp file and passed
        // to Mesen as a command-line argument so it loads on startup.
        //
        // The script reads Famidash's live camera variables directly from RAM
        // every 3 frames (~20 Hz) and writes them to a temp file that the editor
        // overlay timer reads.
        //
        // From Famidash.dbg (current build):
        //   _scroll_x = $04A6 (uint32 world pixel camera X)
        //   _scroll_y = $04AA (uint16 encoded camera Y)
        // -----------------------------------------------------------------------
        internal string BuildOverlayLuaScript()
        {
            return BuildOverlayLuaScript(includeReplay: true);
        }

        // The script is ALWAYS self-contained: replay data is embedded as a Lua
        // literal directly in the generated source.  When `includeReplay` is
        // false, an empty replay table is embedded — the script still runs
        // (scroll capture works) but displays "REPLAY: file empty or missing"
        // until a real path is exported.  No external file reads, ever.
        internal string BuildOverlayLuaScript(bool includeReplay)
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
            // Keeps trace/orbDbg/scroll WRITES (those are outputs, not deps).
            string embeddedReplayLiteral = "{}";
            int    embeddedYOffset       = 0;
            if (includeReplay)
            {
                try
                {
                    if (System.IO.File.Exists(this.ReplayTempFile))
                    {
                        var sb  = new System.Text.StringBuilder();
                        sb.Append('{');
                        bool first = true;
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
                        embeddedReplayLiteral = sb.ToString();
                    }
                }
                catch { /* fall back to empty replay */ }
            }

            string scrollPart =
$@"-- FamidashEditor overlay script (auto-generated, do not edit)
local scrollFile = ""famidash_overlay_scroll.txt""

emu.addEventCallback(function()
    local scrollX = emu.read32(0x04A6, emu.memType.nesMemory) or 0
    local sy_raw  = emu.read16(0x04AA, emu.memType.nesMemory) or 0
    -- calculate_linear_scroll_y: linear = lo + hi * 240 (matches nesdash.s implementation)
    local sy_lo   = sy_raw & 0xFF
    local sy_hi   = (sy_raw >> 8) & 0xFF
    local scrollY = sy_lo + sy_hi * 240
    local tempFile = scrollFile .. "".tmp""
    local f = io.open(tempFile, ""w"")
    if f then
        f:write(tostring(scrollX) .. "","" .. tostring(scrollY))
        f:close()
        pcall(function() os.remove(scrollFile) end)
        pcall(function() os.rename(tempFile, scrollFile) end)
    end
end, emu.eventType.endFrame)
";

            // Replay table is keyed by the player's pixel X (and Y as a tiebreaker).
            // Lua reads NES _player_x ($043D, 16-bit lo|hi) and _player_y ($0441) every frame.
            // px = (read16(_player_x) >> 8) + 8 matches PathfinderEngine PathPoints (hitbox center).
            // A monotonic forward cursor advances while the next entry's X has been reached,
            // so backward motion (death respawn) re-arms via a 0->non-zero transition.
            string replayPart =
$@"
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
local nesYOffset = {embeddedYOffset}
local cursor = 1
local armed = false
local prevPx = -1
local lastA = false
local frameIdx = 0
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
    -- A/X/Y registers at entry — useful to read return value when hooking
    -- the byte AFTER an RTS-bearing call site.
    local st = emu.getState()
    local A = (st and (st[""cpu.a""] or st[""cpu.A""])) or 0
    local X = (st and (st[""cpu.x""] or st[""cpu.X""])) or 0
    local Y = (st and (st[""cpu.y""] or st[""cpu.Y""])) or 0
    physEventCount = physEventCount + 1
    physEvents[physEventCount] = string.format(
        ""%s pc=%04X A=%02X X=%02X Y=%02X cpx=%04X cpy=%04X cpvy=%d "" ..
        ""G=(%02X,%02X,%dx%d) ej_U=%02X ej_D=%02X col=%02X probe=(%02X,%02X,%02X) "" ..
        ""mini=%d grav=%02X tidx=%02X cd=%02X sx=%05X sy=%04X"",
        tag, pc, A, X, Y, cpx, cpy, cpvy,
        gx, gy, gw, gh, ej_U, ej_D, col, tx, ty, tr,
        mini, grav, tidx, cd, sx, sy_r)
end

local function _hookPhys(tag, pc)
    emu.addMemoryCallback(function() _physSnap(tag, pc) end,
        emu.callbackType.exec, pc)
end

-- Entry hooks
_hookPhys(""bg_coll_U.in"",      0xA183)
_hookPhys(""bg_coll_D.in"",      0xA2BC)
_hookPhys(""bg_coll_R.in"",      0xADDB)
_hookPhys(""bg_coll_L.in"",      0xADFE)
_hookPhys(""bg_coll_death.in"",  0xB03C)
_hookPhys(""ufo_ship_eject.in"", 0xB0D0)
_hookPhys(""common_gravity.in"", 0xB0FA)
_hookPhys(""ufo_movement.in"",   0xB25F)
_hookPhys(""ball_eject.in"",     0xB2C0)
_hookPhys(""ball_movement.in"",  0xB3DE)
_hookPhys(""cube_eject.in"",     0xB542)
_hookPhys(""cube_movement.in"",  0xB5E6)
_hookPhys(""ship_movement.in"",  0xBAD5)
_hookPhys(""spider_eject.in"",   0xBBB3)
_hookPhys(""spider_movement.in"",0xBBDB)
_hookPhys(""wave_movement.in"",  0xBD65)
_hookPhys(""x_movement_coll.in"",0x87DA)
_hookPhys(""x_movement.in"",     0x8827)

-- Exit hooks (last byte of each scope = presumed RTS).  Captures POST-call
-- state of currplayer_y / vel_y / eject_U/D / collision so a U/D eject's
-- effect on Y_high is directly visible in the log.  A register holds C
-- return value at function exit (if scope ends with RTS).
_hookPhys(""bg_coll_U.out"",      0xA2BB)
_hookPhys(""bg_coll_D.out"",      0xA3ED)
_hookPhys(""bg_coll_R.out"",      0xADFD)
_hookPhys(""bg_coll_L.out"",      0xAE28)
_hookPhys(""bg_coll_death.out"",  0xB0CF)
_hookPhys(""ufo_ship_eject.out"", 0xB0F9)
_hookPhys(""ufo_movement.out"",   0xB2BF)
_hookPhys(""ball_eject.out"",     0xB3DD)
_hookPhys(""cube_eject.out"",     0xB5E5)
_hookPhys(""ship_movement.out"",  0xBBB2)
_hookPhys(""spider_eject.out"",   0xBBDA)
_hookPhys(""spider_movement.out"",0xBD06)
_hookPhys(""x_movement_coll.out"",0x8826)
_hookPhys(""x_movement.out"",     0x89DF)

-- Per-frame: track player position, arm replay on level start (0 -> non-zero),
-- advance cursor monotonically, capture the desired A state for the next poll.
emu.addEventCallback(function()
    if #replay == 0 then
        emu.drawString(8, 8, ""REPLAY: file empty or missing"", 0xFF6666, 0x000000)
        return
    end

    -- _player_x / _player_y are screen-relative; add scroll to get NES world
    -- coords, then subtract nesYOffset to convert into pathfinder world coords
    -- (matches PathfinderEngine PathPoints which use the editor's row 0 as
    -- the origin, while the NES build skips top map rows).
    local scrollX = emu.read32(0x04A6, emu.memType.nesMemory) or 0
    local sy_raw  = emu.read16(0x04AA, emu.memType.nesMemory) or 0
    local scrollY = (sy_raw & 0xFF) + ((sy_raw >> 8) & 0xFF) * 240
    local rawX = emu.read16(0x043D, emu.memType.nesMemory) or 0
    local rawY = emu.read16(0x0441, emu.memType.nesMemory) or 0
    local px = scrollX + (rawX >> 8) + 8
    local py = scrollY + (rawY >> 8) + 8 - nesYOffset

    -- Detect level start / respawn: prev was 0/uninitialized, now in-game.
    if (prevPx <= 0 or px < prevPx - 16) and px > 0 then
        cursor = 1
        armed = true
        frameIdx = 0
        -- Reset the actual-NES trail on respawn so old trails don't linger.
        nesTrail = {{}}
        nesTrailHead = 1
        nesTrailCount = 0
        -- Trace file: APPEND on respawn so death-frames are preserved across
        -- attempts.  Open in ""a"" the first time, write a separator on each
        -- arm so attempts are visually grouped.  This way comparing the trace
        -- against PF can see the frames immediately preceding NES-death.
        if not traceFp then
            traceFp = io.open(traceFile, ""w"")
            if traceFp then
                traceFp:write(""nes_y_offset,"" .. tostring(nesYOffset) .. ""\n"")
                traceFp:write(""rom_frame,sim_cursor,px,py,a_next,a_cur,raw_x,raw_y,scrollx,scrolly,vel_y,table_idx,gravity_mod,dashing,gamemode,scroll_y_subpx,framerate,tgt_scroll_y,cp_y,scroll_y_raw,cube_data,death_pc,death_ctx,collmap_r8,mini,cp_gravity\n"")
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
                physDbgFp:write(""# Fields: tag pc=PC A/X/Y cpx cpy cpvy G=(x,y,wxh) ej_U ej_D col probe=(tx,ty,tr) mini grav tidx cd sx sy\n"")
                physDbgFp:write(""# Probes: $93=temp_x $94=temp_y $95=temp_room $81=collision\n"")
                physDbgFp:write(""# Eject:  $8D=eject_U $8C=eject_D (currplayer_y_high -= eject_U+1 / += eject_D+1)\n"")
            end
        else
            physDbgFp:write(""# --- respawn ---\n"")
            physDbgFp:flush()
        end
    end
    prevPx = px

    -- Append current NES position to the actual-trail ring buffer.
    if armed then
        nesTrail[nesTrailHead] = {{ x = px, y = py }}
        nesTrailHead = nesTrailHead + 1
        if nesTrailHead > nesTrailMax then nesTrailHead = 1 end
        if nesTrailCount < nesTrailMax then nesTrailCount = nesTrailCount + 1 end
    end

    if armed then
        -- Guard: at game-start scrollX is garbage (~0xFFFFFF82), making px ~4
        -- billion and blowing the cursor to #replay in one step.  Only advance
        -- when px is in a plausible in-level range (NES level width << 500000px).
        if px >= 0 and px < 524288 then
            while cursor < #replay and replay[cursor + 1].x <= px do
                cursor = cursor + 1
            end
        end
        -- Frame alignment: PathPoints[cursor] is the post-physics position for
        -- sim frame `cursor`, produced by Inputs[cursor]. We've just observed
        -- that position in endFrame N. The next emulator inputPolled will be
        -- for frame N+1, so we must feed Inputs[cursor+1] -- otherwise the
        -- press lands one frame late (jumps fire after the spike).
        local curEntry = replay[cursor]
        local nextEntry = replay[cursor + 1] or replay[cursor]
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
            traceFp:write(string.format(""%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%s,%s,%s,%d,%d\n"",
                frameIdx, cursor, px, py, lastA and 1 or 0, curA and 1 or 0,
                rawX, rawY, scrollX, scrollY,
                vel_y, table_idx, gravity_mod, dashing, gamemode, scroll_y_subpx,
                framerate_v, tgt_scroll_y, cp_y, scroll_y_raw, cube_data0,
                cubeDataDeathPC and string.format(""%04X"", cubeDataDeathPC) or """",
                cubeDataDeathCtx or """",
                cm_str, mini_flag, cp_gravity))
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

            orbDbgFp:write(string.format(
                ""f=%d sim=%d ENTER mode=%d mini=%d grav=%d dash=%d orbAct=%d orbHit=%d lastSprIdx=%d "" ..
                ""cpx=0x%04X cpy=0x%04X cpvy=%d cp_tidx=%d cube_data=0x%02X "" ..
                ""Generic=(%d,%d,%dx%d) Generic2=(%d,%d,%dx%d) press=%d held=%d\n"",
                frameIdx, cursor, gm, cp_mini, cp_grav, dash, orbAct, orbHit, lastIdx,
                cpx, cpy, cpvy, cp_tidx, cp_cube,
                g_x, g_y, g_w, g_h, g2_x, g2_y, g2_w, g2_h,
                lastA and 1 or 0, curA and 1 or 0))

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
                emu.drawLine(pxA, pyA, pxB, pyB, segColor)
            end
            pxA, pyA = pxB, pyB
        end
        -- Highlight the current cursor entry.
        local c = replay[cursor]
        if c then
            local cx, cy = pf2sx(c.x), pf2sy(c.y)
            emu.drawRectangle(cx - 2, cy - 2, 5, 5, 0xFFFF00, true)
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
                    emu.drawLine(axS, ayS, bxS, byS, 0xFFFF00)
                end
            end
            prevEntry = curEntry
        end
    end
    if armed and replay[cursor] then
        -- (debug text overlay removed)
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
            return scrollPart + replayPart;
        }
    }
}
