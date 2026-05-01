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

        internal string MesenTraceFile =>
            Path.Combine(CurrentReplayDir, "famidash_mesen_trace.csv");

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
            return BuildOverlayLuaScript(includeReplay: false);
        }

        internal string BuildOverlayLuaScript(bool includeReplay)
        {
            string escaped = ScrollTempFile.Replace("\\", "\\\\");
            string replayEscaped = ReplayTempFile.Replace("\\", "\\\\");
            string traceEscaped  = MesenTraceFile.Replace("\\", "\\\\");

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

            if (!includeReplay) return scrollPart;

            // Replay table is keyed by the player's pixel X (and Y as a tiebreaker).
            // Lua reads NES _player_x ($043D, 16-bit lo|hi) and _player_y ($0441) every frame.
            // px = (read16(_player_x) >> 8) + 8 matches PathfinderEngine PathPoints (hitbox center).
            // A monotonic forward cursor advances while the next entry's X has been reached,
            // so backward motion (death respawn) re-arms via a 0->non-zero transition.
            string replayPart =
$@"
-- ── Replay injector ────────────────────────────────────────────────────────
local replayFile = ""{replayEscaped}""
local traceFile  = ""{traceEscaped}""
local replay = {{}}
local cursor = 1
local armed = false
local prevPx = -1
local lastA = false
local frameIdx = 0
local traceFp = nil
-- Ring buffer of recent ACTUAL NES (px,py) positions, for drawing a real
-- player trail to visualise divergence from the PF-projected path.
-- Each entry is {{ x = pf-coord X, y = pf-coord Y }}.  Drawn yellow.
local nesTrail = {{}}
local nesTrailMax = 600
local nesTrailHead = 1
local nesTrailCount = 0
-- Linear-Y offset between NES world coords and pathfinder world coords:
--   PF_y = NES_y - nesYOffset    (set per-level by ExportReplayCsv)
local nesYOffset = 0

local function loadReplay()
    replay = {{}}
    nesYOffset = 0
    local f = io.open(replayFile, ""r"")
    if not f then return end
    for line in f:lines() do
        local off = line:match(""^nes_y_offset,(%-?%d+)$"")
        if off then
            nesYOffset = tonumber(off)
        elseif line:find(""frame"", 1, true) then
            -- header, skip
        else
            local fr, x, y, a = line:match(""^(%-?%d+),(%-?%d+),(%-?%d+),(%-?%d+)$"")
            if fr then
                replay[#replay+1] = {{ x = tonumber(x), y = tonumber(y), a = tonumber(a) }}
            end
        end
    end
    f:close()
end

loadReplay()

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
                traceFp:write(""rom_frame,sim_cursor,px,py,a_next,a_cur,raw_x,raw_y,scrollx,scrolly,vel_y,table_idx,gravity_mod,dashing,gamemode,scroll_y_subpx,framerate,tgt_scroll_y,cp_y,scroll_y_raw,cube_data,collmap_r8\n"")
            end
        else
            traceFp:write(""# --- respawn ---\n"")
            traceFp:flush()
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
        -- Advance the cursor while the next entry's X has been reached.
        while cursor < #replay and replay[cursor + 1].x <= px do
            cursor = cursor + 1
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
            --   _currplayer_vel_y     zp $6E (16-bit signed)
            --   _currplayer_table_idx zp $79 (8-bit)
            --   _gravity_mod          abs $5C2 (8-bit)
            --   _dashing              abs $4D4 (8-bit, [currplayer])
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
            traceFp:write(string.format(""%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%d,%s\n"",
                frameIdx, cursor, px, py, lastA and 1 or 0, curA and 1 or 0,
                rawX, rawY, scrollX, scrollY,
                vel_y, table_idx, gravity_mod, dashing, gamemode, scroll_y_subpx,
                framerate_v, tgt_scroll_y, cp_y, scroll_y_raw, cube_data0, cm_str))
            traceFp:flush()
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
