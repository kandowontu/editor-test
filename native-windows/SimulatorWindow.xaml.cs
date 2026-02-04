using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Threading;

namespace FamidashEditor
{
    public partial class SimulatorWindow : Window
    {
        // Window base title used for UI string formatting
        private string baseWindowTitle = "Simulator";
        // Option to hide trigger sprites in simulator rendering
        private bool hideTriggerSprites = false;

        public void SetHideTriggerSprites(bool v) { try { hideTriggerSprites = v; } catch { } }

        // Public toggle to show sprite hitboxes in the simulator viewport.
        public bool ShowSpriteHitboxes { get; set; } = false;

        // Public toggle used by the main window: show tile/hitbox overlays in the simulator.
        public bool ShowTileHitboxes { get; set; } = false;

        // Starting speed UI index (0=0.5x,1=1x,2=2x,3=3x,4=4x)
        private int startingSpeedUiIndex = 1;

        // Called by the editor to set the starting speed UI index so simulators match the editor.
        public void SetStartingSpeedUiIndex(int idx)
        {
            try
            {
                startingSpeedUiIndex = idx;
                try { Dispatcher.BeginInvoke(new Action(() => { try { RenderFrame(); } catch { } })); } catch { }
            }
            catch { }
        }

            private void UpdateSimTitle()
            {
                try
                {
                    string s = $"{baseWindowTitle} — Sim {(simTimeScale * 100.0):F0}%";
                        try { this.Title = s; } catch { }
                        orbHoldSuppressing = false;
                }
                catch { }
            }

        private bool IsHiddenTriggerSprite(int s)
        {
            try
            {
                // Minimal safe implementation: respect the `hideTriggerSprites` flag but
                // avoid hiding anything by default to prevent accidental visual regressions.
                // If needed, this can be expanded to check `s` against known trigger IDs.
                if (!hideTriggerSprites) return false;
                return false;
            }
            catch { return false; }
        }

        // In-memory debug buffer for simulator collision/pass-through decisions.
        // We avoid writing persistent log files by default; this buffer captures
        // recent messages and also forwards them to Debug.WriteLine so IDEs
        // can show them during runs. The buffer is bounded to avoid unbounded
        // memory growth.
        private readonly System.Collections.Generic.List<string> simDebugBuffer = new System.Collections.Generic.List<string>();
        private const int SIM_DEBUG_BUFFER_MAX = 4096;
        // By default we write debug output to a timestamped temp file so you can
        // open it after reproducing the issue. Change to false to disable file
        // writes.
        private readonly bool simDebugWriteToFile = true;
        private readonly string simDebugLogPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"famidash_sim_debug_{System.DateTime.UtcNow:yyyyMMdd_HHmmss}.txt");

        private void AppendSimDebug(string msg)
        {
            // Early return if debug is completely disabled
            if (!simDebugWriteToFile && simDebugBuffer.Count == 0)
            {
                return;
            }
            
            try
            {
                string t = DateTime.UtcNow.ToString("o") + " " + msg;
                lock (simDebugBuffer)
                {
                    simDebugBuffer.Add(t);
                    if (simDebugBuffer.Count > SIM_DEBUG_BUFFER_MAX)
                    {
                        // Trim oldest entries
                        int remove = simDebugBuffer.Count - SIM_DEBUG_BUFFER_MAX;
                        simDebugBuffer.RemoveRange(0, remove);
                    }
                }
                try { System.Diagnostics.Debug.WriteLine(t); } catch { }
                if (simDebugWriteToFile)
                {
                    try
                    {
                        System.IO.File.AppendAllText(simDebugLogPath, t + System.Environment.NewLine);
                    }
                    catch { }
                }
            }
            catch { }
        }

        // Return a snapshot of the current in-memory debug buffer (most-recent last).
        public string[] GetSimDebugSnapshot()
        {
            try
            {
                lock (simDebugBuffer)
                {
                    return simDebugBuffer.ToArray();
                }
            }
            catch { return new string[0]; }
        }

        // Return the path to the on-disk debug log (may not exist until a message is written).
        public string GetSimDebugLogPath()
        {
            try { return simDebugLogPath; } catch { return string.Empty; }
        }

        // Update effective gravity/jump/max-fall based on the current mode and flags
        private void UpdateEffectiveGravity()
        {
            try
            {
                // Choose base values depending on the current game mode.
                switch (currentGameMode)
                {
                    case 1: // ship
                        effectiveGravity_fixed = SHIP_GRAVITY;
                        effectiveJumpVel_fixed = 0; // ship doesn't use the generic jump impulse
                        effectiveMaxFall_fixed = SHIP_MAX_FALLSPEED;
                        break;
                    case 2: // ball
                        effectiveGravity_fixed = BALL_GRAVITY;
                        effectiveJumpVel_fixed = BALL_IMMEDIATE_VEL;
                        effectiveMaxFall_fixed = BALL_MAX_FALLSPEED;
                        break;
                    case 3: // UFO
                        effectiveGravity_fixed = UFO_GRAVITY;
                        effectiveJumpVel_fixed = UFO_JUMP_VEL;
                        effectiveMaxFall_fixed = UFO_MAX_FALLSPEED;
                        break;
                    case 6: // Wave (no gravity)
                        effectiveGravity_fixed = 0;
                        effectiveJumpVel_fixed = 0;
                        effectiveMaxFall_fixed = 0;
                        break;
                    case 9: // Pogo (uses ball physics)
                        effectiveGravity_fixed = BALL_GRAVITY;
                        effectiveJumpVel_fixed = BALL_IMMEDIATE_VEL;
                        effectiveMaxFall_fixed = BALL_MAX_FALLSPEED;
                        break;
                    case 10: // Snake (uses wave physics - no gravity)
                        effectiveGravity_fixed = 0;
                        effectiveJumpVel_fixed = 0;
                        effectiveMaxFall_fixed = 0;
                        break;
                    case 11: // Football (uses cube physics)
                        effectiveGravity_fixed = CUBE_GRAVITY;
                        effectiveJumpVel_fixed = CUBE_JUMP_VEL;
                        effectiveMaxFall_fixed = CUBE_MAX_FALLSPEED;
                        break;
                    case 0: // cube (default)
                    default:
                        effectiveGravity_fixed = CUBE_GRAVITY;
                        effectiveJumpVel_fixed = CUBE_JUMP_VEL;
                        effectiveMaxFall_fixed = CUBE_MAX_FALLSPEED;
                        break;
                }

                // If logical gravity is reversed for numeric purposes, flip numeric signs
                // so integration moves in the opposite direction. Numeric inversion is
                // determined by the canonical `gravityReversed` flag OR the compatibility
                // `effectiveInvertedByW` which allows numeric-only inversion when the
                // editor's No-Death option requests it.
                bool numericInvert = gravityReversed || effectiveInvertedByW;
                if (numericInvert)
                {
                    effectiveGravity_fixed = -effectiveGravity_fixed;
                    effectiveJumpVel_fixed = -effectiveJumpVel_fixed;
                    effectiveMaxFall_fixed = -effectiveMaxFall_fixed;
                }

                // Debug: log effective values and flags so we can verify toggles work
                try { System.Diagnostics.Debug.WriteLine($"UpdateEffectiveGravity: gravityReversed={gravityReversed} effectiveGravity={effectiveGravity_fixed} effectiveJump={effectiveJumpVel_fixed} effectiveMaxFall={effectiveMaxFall_fixed}"); } catch { }
            }
            catch { }
        }

        private void UpdatePlayerSpeed()
        {
            try
            {
                // Update horizontal velocity based on speed setting
                // Speed indices: 0=0.5x, 1=1x, 2=2x, 3=3x, 4=4x
                int[] speedValues = { CUBE_SPEED_X05, CUBE_SPEED_X1, CUBE_SPEED_X2, CUBE_SPEED_X3, CUBE_SPEED_X4 };
                if (speed >= 0 && speed < speedValues.Length)
                {
                    playerVelX_fixed = speedValues[speed];
                }
            }
            catch { }
        }

        // Map certain simulator tile indices to alternative indices for display.
        // This allows specific tile codes to render exactly like other tiles
        // (or be rendered as fully transparent by mapping to 0x00).
        private int ResolveSimulatorTileIndex(int idx)
        {
            try
            {
                if (idx >= 1000 || idx < 0) return idx;
                switch (idx)
                {
                    // Examples: render these special tiles as other tile graphics
                    case 0xD9:
                    case 0xDA:
                        return 0x11; // render like tile 0x11
                    case 0xDB:
                    case 0xDC:
                        return 0x1B; // render like tile 0x1B
                    case 0x8F:
                        return 0x2F; // render like tile 0x2F

                    // Render these indices as fully transparent (map to 0x00)
                    case 0xFC:
                    case 0xDF:
                    case 0xE3:
                    case 0xFE:
                    case 0xFF:
                        return 0x00;

                    // Add additional remaps here as needed.
                    default:
                        return idx;
                }
            }
            catch { return idx; }
        }

        // Sprite geometry tables (from user-provided data). Non-numeric placeholders use sensible defaults.
        private static readonly int[] sprite_heights = new int[] {
            0x34,0x34,0x34,0x34,0x34,0x12,0x12,0x10, // 00-07
            0x28,0x28,0x03,0x12,0x03,0x03,0x03,0x10, // 08-0F
            0x0e,0x0e,0x0e,0x0e,0x24,0x24,0x24,0x34, // 10-17
            0x34,0x34,0x10,0x10,0x10,0x10,0x10,0x12, // 18-1F
            0x24,0x24,0x34,0x34,0x34,0x03,0x03,0x12, // 20-27
            0x12,0x12,0x10,0x10,0x10,0x10,0x10,0x10, // 28-2F
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 30-37
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 38-3F
            0x10,0x10,0x10,0x10,0x12,0x12,0x12,0x28, // 40-47
            0x28,0x10,0x10,0x34,0x12,0x12,0x30,0x10, // 48-4F
            0x12,0x12,0x03,0x03,0x12,0x12,0x03,0x03, // 50-57
            0x34,0x10,0x10,0x12,0x12,0x12,0x12,0x34, // 58-5F
            0x34,0x34,0x34,0x34,0x34,0x02,0x10,0x10, // 60-67
            0x10,0x10,0x34,0x34,0x34,0x20,0x08,0x10, // 68-6F
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 70-77
            0x10,0x12,0x12,0x12,0x12,0x10,0x10,0x10, // 78-7F
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 80-87
            0x10,0x10,0x10,0x10,0x10,0x00,0x10,0x10, // 88-8F
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 90-97
            0x10,0x10,0x10,0x10,0x10,0x00,0x10,0x10, // 98-9F
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // A0-A7
            0x10,0x10,0x10,0x10,0x10,0x00,0x10,0x10, // A8-AF
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // B0-B7
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // B8-BF
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // C0-C7
            0x10,0x10,0x10,0x10,0x10,0x00,0x00,0x10, // C8-CF
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // D0-D7
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // D8-DF
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // E0-E7
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // E8-EF
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // F0-F7
            0x10,0x10,0x1F,0x10,0x10,0x03,0x03,0x00  // F8-FF
        };

        private static readonly int[] sprite_widths = new int[] {
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 00-07
            0x0e,0x0e,0x0F,0x10,0x0F,0x0F,0x0F,0x10, // 08-0F
            0x28,0x28,0x28,0x28,0x10,0x10,0x10,0x10, // 10-17
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 18-1F
            0x10,0x10,0x10,0x10,0x10,0x0F,0x0F,0x10, // 20-27
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 28-2F
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 30-37
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 38-3F
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x0e, // 40-47
            0x0e,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 48-4F
            0x10,0x10,0x0F,0x0F,0x10,0x10,0x0F,0x0F, // 50-57
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 58-5F
            0x10,0x10,0x10,0x10,0x10,0x0E,0x30,0x30, // 60-67
            0x30,0x30,0x10,0x10,0x10,0x10,0x08,0x10, // 68-6F
            0x10,0x10,0x10,0x10,0x10,0x30,0x30,0x30, // 70-77
            0x30,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 78-7F
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 80-87
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 88-8F
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 90-97
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 98-9F
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // A0-A7
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // A8-AF
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // B0-B7
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // B8-BF
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // C0-C7
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // C8-CF
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // D0-D7
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // D8-DF
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // E0-E7
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // E8-EF
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // F0-F7
            0x10,0x08,0x1B,0x10,0x10,0x0E,0x0E,0x10  // F8-FF
        };

        private static readonly int[] sprite_x_offset = new int[] {
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 00-07
            0x01,0x01,0x00,0x00,0x00,0x00,0x00,0x00, // 08-0F
            0x04,0x04,0x04,0x04,0x00,0x00,0x00,0x00, // 10-17
            0x08,0x08,0x00,0x00,0x00,0x00,0x00,0x00, // 18-1F
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 20-27
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 28-2F
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 30-37
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 38-3F
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x01, // 40-47
            0x01,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 48-4F
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 50-57
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 58-5F
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 60-67
            0x00,0x00,0x00,0x00,0x00,0x00,0x04,0x00, // 68-6F
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 70-77
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 78-7F
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 80-87
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 88-8F
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 90-97
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 98-9F
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // A0-A7
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // A8-AF
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // B0-B7
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // B8-BF
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // C0-C7
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // C8-CF
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // D0-D7
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // D8-DF
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // E0-E7
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // E8-EF
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // F0-F7
            0x00,0x08,-0x07,0x00,0x00,0x00,0x00,0x00  // F8-FF
        };

        private static readonly int[] sprite_y_offset = new int[] {
            -0x02,-0x02,-0x02,-0x02,-0x02,-0x01,-0x01,0x00, // 00-07
            0x04,0x04,0x05,-0x01,0x00,0x05,0x00,0x00, // 08-0F
            0x01,0x01,0x01,0x01,-0x02,-0x02,-0x02,-0x02, // 10-17
            -0x02,-0x02,0x00,0x00,0x00,0x00,0x00,-0x01, // 18-1F
            -0x02,-0x02,-0x02,-0x02,-0x02,0x05,0x00,-0x01, // 20-27
            -0x01,-0x01,0x00,0x00,0x00,0x00,0x00,0x00, // 28-2F
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 30-37
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 38-3F
            0x00,0x00,0x00,0x00,-0x01,-0x01,-0x01,0x04, // 40-47
            0x04,0x00,0x00,-0x02,0x00,-0x01,-0x01,0x00, // 48-4F
            -0x01,-0x01,0x05,0x00,-0x01,-0x01,0x0D,0x00, // 50-57
            -0x02,0x00,0x00,-0x01,-0x01,-0x01,-0x01,-0x02, // 58-5F
            -0x02,-0x02,-0x02,-0x02,-0x02,0x00,0x00,0x00, // 60-67
            0x00,0x00,-0x02,-0x02,-0x02,0x00,0x04,0x00, // 68-6F
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 70-77
            0x00,-0x01,-0x01,0x00,0x00,0x00,0x00,0x00, // 78-7F
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 80-87
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 88-8F
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 90-97
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 98-9F
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // A0-A7
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // A8-AF
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // B0-B7
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // B8-BF
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // C0-C7
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // C8-CF
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // D0-D7
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // D8-DF
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // E0-E7
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // E8-EF
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // F0-F7
            0x00,0x00,-0x07,0x00,0x00,0x05,0x02,0x00  // F8-FF
        };
        // Pad / Orb velocity matrix (rows = pad/orb kind, cols = game mode)
        // Columns: 0=cube,1=ship,2=ball,3=ufo,4=robot,5=spider,6=wave,7=swing
        // Rows (by author matrix):
        // 0: yellow orb
        // 1: yellow pad
        // 2: pink orb
        // 3: pink pad
        // 4: red orb
        // 5: yellow orb bigger
        // 6: black orb
        // 7: yellow orb smaller
        // 8: red pad
        private static readonly int[][] PadOrbHeights = new int[][] {
            new int[] { 0x590, 0x450, 0x410, 0x3B0, 0x590, 0x440, 0x000, 0x3A0 }, // 0 yellow orb
            new int[] { 0x7C0, 0x3C0, 0x4F0, 0x330, 0x8B0, 0x500, 0x000, 0x450 }, // 1 yellow pad
            new int[] { 0x3D0, 0x200, 0x330, 0x220, 0x450, 0x350, 0x000, 0x2D0 }, // 2 pink orb
            new int[] { 0x510, 0x270, 0x360, 0x250, 0x550, 0x350, 0x000, 0x360 }, // 3 pink pad
            new int[] { 0x750, 0x5D0, 0x550, 0x510, 0x750, 0x500, 0x000, 0x4D0 }, // 4 red orb
            new int[] { 0x590, 0x590, 0x5D0, 0x590, 0x590, 0x590, 0x000, 0x5D0 }, // 5 yellow orb bigger
            new int[] { -0x990, -0x990, -0x970, -0x990, -0x990, -0x990, 0x000, -0x970 }, // 6 black orb
            new int[] { 0x540, 0x540, 0x472, 0x4B0, 0x770, 0x4B0, 0x000, 0x472 }, // 7 yellow orb smaller
            new int[] { 0x9F0, 0x620, 0x630, 0x400, 0xA50, 0x690, 0x000, 0x660 }  // 8 red pad
        };

        private static readonly int[][] PadOrbHeights_Mini = new int[][] {
            new int[] { 0x4D0, 0x4A0, 0x450, 0x3D0, 0x470, 0x350, 0x000, 0x2A0 }, // 0 yellow orb
            new int[] { 0x680, 0x430, 0x4D0, 0x3A0, 0x730, 0x400, 0x000, 0x340 }, // 1 yellow pad
            new int[] { 0x350, 0x1E0, 0x350, 0x1B0, 0x370, 0x230, 0x000, 0x1F0 }, // 2 pink orb
            new int[] { 0x3F0, 0x1E0, 0x390, 0x150, 0x350, 0x350, 0x000, 0x220 }, // 3 pink pad
            new int[] { 0x650, 0x670, 0x500, 0x550, 0x650, 0x470, 0x000, 0x350 }, // 4 red orb
            new int[] { 0x590, 0x590, 0x560, 0x590, 0x590, 0x590, 0x000, 0x560 }, // 5 yellow orb bigger
            new int[] { -0x990, -0x990, -0x970, -0x990, -0x990, -0x990, 0x000, -0x970 }, // 6 black orb
            new int[] { 0x540, 0x540, 0x472, 0x4B0, 0x770, 0x4B0, 0x000, 0x472 }, // 7 yellow orb smaller
            new int[] { 0x830, 0x6C0, 0x5B0, 0x550, 0x8D0, 0x550, 0x000, 0x3A0 }  // 8 red pad
        };

        // Returns true when the sprite at storage index `idx` (with sprite id `sid`) overlaps
        // the player's axis-aligned hitbox in world pixel coordinates. This uses the
        // sprite geometry tables (`sprite_widths`, `sprite_heights`, `sprite_x_offset`, `sprite_y_offset`)
        // and any per-position `spritePixelOffsets`. If the sprite has an anchor, the anchor's
        // recorded offsets are used as a fallback so simulator rendering and collision match.
        private bool SpriteIntersectsPlayerTouching(int idx, int sid, int playerLeft_px, int playerRight_px, int playerTop_px, int playerBottom_px)
        {
            // For pads/orbs: trigger when edges TOUCH (adjacent pixels) instead of requiring overlap
            // This matches GD behavior where interactive objects activate on edge contact
            try
            {
                int storageTileX = idx % mapWidth;
                int storageTileY = idx / mapWidth;

                // If anchored, prefer the anchored sprite id for geometry lookup so preview matches editor
                int id_for_geom = sid & 0xFF;
                int anchorKey = -1;
                if (spriteAnchors != null && spriteAnchors.TryGetValue(idx, out var anchor))
                {
                    anchorKey = anchor.anchorTileY * mapWidth + anchor.anchorTileX;
                    // Prefer the anchor's sprite id for geometry lookup when anchored
                    if (anchorKey >= 0 && anchorKey < sprites.Length)
                    {
                        int anchoredId = sprites[anchorKey];
                        if (anchoredId >= 0 && anchoredId < 256) id_for_geom = anchoredId & 0xFF;
                    }
                }

                int hw = (id_for_geom >= 0 && id_for_geom < sprite_widths.Length) ? sprite_widths[id_for_geom] : TILE;
                int hh = (id_for_geom >= 0 && id_for_geom < sprite_heights.Length) ? sprite_heights[id_for_geom] : TILE;
                int hxoff = (id_for_geom >= 0 && id_for_geom < sprite_x_offset.Length) ? sprite_x_offset[id_for_geom] : 0;
                int hyoff = (id_for_geom >= 0 && id_for_geom < sprite_y_offset.Length) ? sprite_y_offset[id_for_geom] : 0;

                // Per-position pixel offset (visual shift).
                int pxOff = 0; int pyOff = 0;
                if (anchorKey >= 0 && spritePixelOffsets != null && spritePixelOffsets.TryGetValue(anchorKey, out var aoffs2))
                {
                    pxOff = aoffs2.offsetX; pyOff = aoffs2.offsetY;
                }
                else if (spritePixelOffsets != null && spritePixelOffsets.TryGetValue(idx, out var offs2))
                {
                    pxOff = offs2.offsetX; pyOff = offs2.offsetY;
                }

                // Compute world-space sprite rectangle (inclusive pixels)
                int groundRowsToReserve_local = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                int spriteLeft_world_px = storageTileX * TILE + hxoff + pxOff;
                int spriteTop_world_px = (storageTileY - groundRowsToReserve_local) * TILE + hyoff + pyOff;
                int spriteRight_world_px = spriteLeft_world_px + Math.Max(1, hw) - 1;
                int spriteBottom_world_px = spriteTop_world_px + Math.Max(1, hh) - 1;

                // If the hitbox table entry is the default TILE size but a larger sprite image
                // is available, prefer the image size for collision
                try
                {
                    if (hw == TILE && hh == TILE)
                    {
                        BitmapSource? bs = null;
                        int keyGeom = id_for_geom & 0xFF;
                        int keyInst = sid & 0xFF;
                        if (previewSpriteMap != null && previewSpriteMap.TryGetValue(keyGeom, out var pimgG) && pimgG is BitmapSource pbsG) bs = pbsG;
                        else if (previewSpriteMap != null && previewSpriteMap.TryGetValue(keyInst, out var pimgI) && pimgI is BitmapSource pbsI) bs = pbsI;
                        else if (spriteImages != null && keyGeom >= 0 && keyGeom < spriteImages.Length && spriteImages[keyGeom] is BitmapSource sbsG) bs = sbsG;
                        else if (spriteImages != null && keyInst >= 0 && keyInst < spriteImages.Length && spriteImages[keyInst] is BitmapSource sbsI) bs = sbsI;
                        if (bs != null)
                        {
                            hw = Math.Max(1, bs.PixelWidth);
                            hh = Math.Max(1, bs.PixelHeight);
                            spriteRight_world_px = spriteLeft_world_px + hw - 1;
                            spriteBottom_world_px = spriteTop_world_px + hh - 1;
                        }
                    }
                }
                catch { }

                // Check for "touching" - edges can be adjacent (1 pixel apart) without overlapping
                // Standard overlap: !(right < left || left > right)
                // Touching: !(right + 1 < left || left > right + 1)
                // Equivalently: !(right < left - 1 || left > right + 1)
                bool touching = !(playerRight_px < spriteLeft_world_px - 1 || 
                                 playerLeft_px > spriteRight_world_px + 1 || 
                                 playerBottom_px < spriteTop_world_px - 1 || 
                                 playerTop_px > spriteBottom_world_px + 1);
                return touching;
            }
            catch { return false; }
        }

        private bool SpriteIntersectsPlayer(int idx, int sid, int playerLeft_px, int playerRight_px, int playerTop_px, int playerBottom_px)
        {
            try
            {
                int storageTileX = idx % mapWidth;
                int storageTileY = idx / mapWidth;

                // If anchored, prefer the anchored sprite id for geometry lookup so preview matches editor
                int id_for_geom = sid & 0xFF;
                int anchorKey = -1;
                if (spriteAnchors != null && spriteAnchors.TryGetValue(idx, out var anchor))
                {
                    anchorKey = anchor.anchorTileY * mapWidth + anchor.anchorTileX;
                    // Prefer the anchor's sprite id for geometry lookup when anchored
                    if (anchorKey >= 0 && anchorKey < sprites.Length)
                    {
                        int anchoredId = sprites[anchorKey];
                        if (anchoredId >= 0 && anchoredId < 256) id_for_geom = anchoredId & 0xFF;
                    }

                    // Do NOT change storageTileX/Y -- keep the sprite instance's own tile position
                    // as the base. The displayed sprite position uses the anchor + tileDelta offsets,
                    // and the hitbox offsets in the tables are applied relative to the sprite's
                    // displayed origin (so using the instance tile + anchor pixel offsets yields
                    // the correct world rect).
                }

                int hw = (id_for_geom >= 0 && id_for_geom < sprite_widths.Length) ? sprite_widths[id_for_geom] : TILE;
                int hh = (id_for_geom >= 0 && id_for_geom < sprite_heights.Length) ? sprite_heights[id_for_geom] : TILE;
                int hxoff = (id_for_geom >= 0 && id_for_geom < sprite_x_offset.Length) ? sprite_x_offset[id_for_geom] : 0;
                int hyoff = (id_for_geom >= 0 && id_for_geom < sprite_y_offset.Length) ? sprite_y_offset[id_for_geom] : 0;

                // Per-position pixel offset (visual shift).
                // When anchored, prefer the anchor tile's pixel offset so collision and
                // overlay visuals match the anchored geometry base. Otherwise prefer
                // the sprite's own per-position offset.
                int pxOff = 0; int pyOff = 0;
                if (anchorKey >= 0 && spritePixelOffsets != null && spritePixelOffsets.TryGetValue(anchorKey, out var aoffs2))
                {
                    pxOff = aoffs2.offsetX; pyOff = aoffs2.offsetY;
                }
                else if (spritePixelOffsets != null && spritePixelOffsets.TryGetValue(idx, out var offs2))
                {
                    pxOff = offs2.offsetX; pyOff = offs2.offsetY;
                }

                // Compute world-space sprite rectangle (inclusive pixels)
                // Rendering subtracts `groundRowsToReserve` from the displayed anchor Y to
                // reserve bottom ground rows. Adjust collision to match displayed origin
                // by applying the same vertical shift when computing world sprite rect.
                int groundRowsToReserve_local = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                int spriteLeft_world_px = storageTileX * TILE + hxoff + pxOff;
                int spriteTop_world_px = (storageTileY - groundRowsToReserve_local) * TILE + hyoff + pyOff;
                int spriteRight_world_px = spriteLeft_world_px + Math.Max(1, hw) - 1;
                int spriteBottom_world_px = spriteTop_world_px + Math.Max(1, hh) - 1;

                // If the hitbox table entry is the default TILE size but a larger sprite image
                // is available (either in preview map or spriteImages), prefer the image size
                // for collision so portals rendered from larger bitmaps get correct functional area.
                try
                {
                    if (hw == TILE && hh == TILE)
                    {
                        BitmapSource? bs = null;
                        // Prefer preview image for the resolved geometry id; if unavailable,
                        // fall back to the instance sprite id so collision matches the renderer's
                        // final sprite selection (which sometimes uses the instance id).
                        int keyGeom = id_for_geom & 0xFF;
                        int keyInst = sid & 0xFF;
                        if (previewSpriteMap != null && previewSpriteMap.TryGetValue(keyGeom, out var pimgG) && pimgG is BitmapSource pbsG) bs = pbsG;
                        else if (previewSpriteMap != null && previewSpriteMap.TryGetValue(keyInst, out var pimgI) && pimgI is BitmapSource pbsI) bs = pbsI;
                        else if (spriteImages != null && keyGeom >= 0 && keyGeom < spriteImages.Length && spriteImages[keyGeom] is BitmapSource sbsG) bs = sbsG;
                        else if (spriteImages != null && keyInst >= 0 && keyInst < spriteImages.Length && spriteImages[keyInst] is BitmapSource sbsI) bs = sbsI;
                        if (bs != null)
                        {
                            hw = Math.Max(1, bs.PixelWidth);
                            hh = Math.Max(1, bs.PixelHeight);
                            spriteRight_world_px = spriteLeft_world_px + hw - 1;
                            spriteBottom_world_px = spriteTop_world_px + hh - 1;
                        }
                    }
                }
                catch { }

                // If a cached hitbox for this sprite was populated during rendering this frame,
                // prefer that rectangle (it exactly matches the overlay) to avoid subtle
                // geometry mismatches from duplicate math paths.
                try
                {
                    if (hitboxWorldCache != null && hitboxWorldCache.TryGetValue(idx, out var cached) && cached.frame == renderFrameCounter)
                    {
                        int cLeft = cached.left; int cTop = cached.top; int cRight = cached.right; int cBottom = cached.bottom;
                        bool cov = !(playerRight_px < cLeft || playerLeft_px > cRight || playerBottom_px < cTop || playerTop_px > cBottom);
                        return cov;
                    }
                }
                catch { }

                bool overlap = !(playerRight_px < spriteLeft_world_px || playerLeft_px > spriteRight_world_px || playerBottom_px < spriteTop_world_px || playerTop_px > spriteBottom_world_px);
                return overlap;
            }
            catch { return false; }
        }

        // Pool for hitbox rectangles
        private System.Collections.Generic.List<System.Windows.Shapes.Rectangle> hitboxPool = new System.Collections.Generic.List<System.Windows.Shapes.Rectangle>();
        private int hitboxesInUse = 0;
        // Pool of rectangle overlays used to draw per-tile hitboxes above the tile layer.
        private System.Collections.Generic.List<System.Windows.Shapes.Rectangle> tileHitboxPool = new System.Collections.Generic.List<System.Windows.Shapes.Rectangle>();
        private int tileHitboxesInUse = 0;
        // Cache of world-space hitbox rectangles populated during rendering so collision
        // can use the exact same geometry as the overlay (key = sprite storage idx).
        private System.Collections.Generic.Dictionary<int, (int left, int top, int right, int bottom, int frame)> hitboxWorldCache = new System.Collections.Generic.Dictionary<int, (int, int, int, int, int)>();
        private int renderFrameCounter = 0;
        // Experimental: record player world positions each rendered frame for editor overlay
        private System.Collections.Generic.List<(int x, int y)> recordedPlayerPath = new System.Collections.Generic.List<(int x, int y)>();
        // Interaction line: player's center (fixed-point) where scrolling begins
        private const int INTERACTION_LINE_FIXED = 0x5000;

        // Player world X (fixed-point, 8 fractional bits)
        // Start the player on the leftmost tile (x = 0)
        private int playerX_fixed = 0;
        private int playerY_fixed = 0; // fixed-point (8 frac bits) world Y for player
        // Player visual size in pixels (set during initialization)
        private int playerVisualWidth = TILE;
        private int playerVisualHeight = TILE;
        // Prevent multiple simultaneous requests to start playback (clicks/keys)
        private bool playbackStartPending = false;
        private volatile bool restartInProgress = false;
        private volatile bool windowClosed = false; // Prevent simulation during restart

        // Visual player controls used for the player: image preferred, rectangle fallback
        private System.Windows.Controls.Image? playerImage = null;
        private System.Windows.Shapes.Rectangle? playerRect = null;
        
        // When player crosses interaction line, remember the screen pixel offset where the crossing occurred
        // so the camera can follow the player while keeping them at that screen X.
        private int interactionScreenOffset_px = -1;

        private readonly int[] tiles;
        private readonly int[] sprites;
        private readonly int mapWidth;
        private readonly int mapHeight;
        private readonly ImageSource?[]? tileImages;
        private ImageSource?[]? tileTonedImages;
        private readonly ImageSource?[]? spriteImages;
        private readonly System.Collections.Generic.Dictionary<int, (int offsetX, int offsetY)> spritePixelOffsets;
        private readonly System.Collections.Generic.Dictionary<int, (int anchorTileX, int anchorTileY)> spriteAnchors;
        private Color backgroundTint;
        private Color groundTint;
        private bool backgroundForceSolidBlack = false; // when true, force background to solid black (0x8F)
        private readonly Color playerPlaceholderGreen = Color.FromArgb(255, 255, 50, 43); // #FF322B
        private Color tileTint;
        private readonly Color playerTint;
        private readonly bool playerTintEnabled;
        private readonly bool forcePreviewMode;
        private readonly bool hideColorTriggers;
        private readonly int gridRenderShiftYPx;
        private readonly System.Collections.Generic.Dictionary<int, ImageSource?>? previewSpriteMap;
        // Simulator-only cached upside-down chain image
        private ImageSource? chainUpsideSimImage = null;
        private readonly System.Collections.Generic.Dictionary<int, ImageSource?[]>? animationFrames;
        // Tile-level animated saw frames (tinted versions) passed from MainWindow
        private ImageSource?[]? sawFrame1TilesTinted;
        private ImageSource?[]? sawFrame2TilesTinted;
        private ImageSource?[]? smallSawFrame1TilesTinted;
        private ImageSource?[]? smallSawFrame2TilesTinted;
        private ImageSource?[]? largeSawFrame1TilesTinted;
        private ImageSource?[]? largeSawFrame2TilesTinted;
        // Keep originals so we can re-generate tinted versions when tile tint changes
        private ImageSource?[]? sawFrame1TilesOrig;
        private ImageSource?[]? sawFrame2TilesOrig;
        private ImageSource?[]? smallSawFrame1TilesOrig;
        private ImageSource?[]? smallSawFrame2TilesOrig;
        private ImageSource?[]? largeSawFrame1TilesOrig;
        private ImageSource?[]? largeSawFrame2TilesOrig;

        private const int NES_W = 16; // horizontal tiles (was 15)
        private const int NES_H = 15; // vertical tiles (was 16)
        private const int TILE = 16;

        // Centralized helper for determining whether a collision category provides
        // a floor at a given local tile column (0..15). Returns true and sets
        // `topOffsetPx` to the Y offset (0..15) of the floor within the tile
        // when a floor exists; otherwise returns false.
        private static bool ProvidesFloorAtColumnStatic(MetatileCollision col, int localX, out int topOffsetPx)
        {
            topOffsetPx = int.MaxValue;
            bool inLeft = (localX >= 0 && localX <= 7);
            bool inRight = (localX >= 8 && localX <= 15);

            switch (col)
            {
                case MetatileCollision.COL_ALL:
                case MetatileCollision.COL_FLOOR_CEIL:
                    topOffsetPx = 0; return true;
                case MetatileCollision.COL_TOP:
                case MetatileCollision.COL_TOP_LEFT_STAIRS:
                case MetatileCollision.COL_TOP_RIGHT_STAIRS:
                case MetatileCollision.COL_TOP_CENTER_SPIKE:
                    topOffsetPx = 0; return true;
                case MetatileCollision.COL_BOTTOM:
                case MetatileCollision.COL_BOTTOM_CENTER_SPIKE:
                case MetatileCollision.COL_BOTTOM_SPIKES:
                    topOffsetPx = 8; return true;
                case MetatileCollision.COL_LEFT:
                case MetatileCollision.COL_BOTTOM_LEFT_STAIRS:
                    if (inLeft) { topOffsetPx = 0; return true; }
                    break;
                case MetatileCollision.COL_BOTTOM_LEFT_SPIKE:
                    if (inLeft) { topOffsetPx = 8; return true; }
                    break;
                case MetatileCollision.COL_RIGHT:
                case MetatileCollision.COL_BOTTOM_RIGHT_STAIRS:
                    if (inRight) { topOffsetPx = 0; return true; }
                    break;
                case MetatileCollision.COL_BOTTOM_RIGHT_SPIKE:
                    if (inRight) { topOffsetPx = 8; return true; }
                    break;
                case MetatileCollision.COL_UP_LEFT:
                    if (inLeft) { topOffsetPx = 0; return true; }
                    break;
                case MetatileCollision.COL_UP_RIGHT:
                    if (inRight) { topOffsetPx = 0; return true; }
                    break;
                case MetatileCollision.COL_DOWN_LEFT:
                case MetatileCollision.COL_LEFT_SPIKE_BLOCK:
                    if (inLeft) { topOffsetPx = 8; return true; }
                    break;
                case MetatileCollision.COL_DOWN_RIGHT:
                case MetatileCollision.COL_RIGHT_SPIKE_BLOCK:
                    if (inRight) { topOffsetPx = 8; return true; }
                    break;
                case MetatileCollision.COL_TOP_LEFT_BOTTOM_RIGHT:
                    if (inLeft) { topOffsetPx = 0; return true; }
                    if (inRight) { topOffsetPx = 8; return true; }
                    break;
                case MetatileCollision.COL_TOP_RIGHT_BOTTOM_LEFT:
                    if (inRight) { topOffsetPx = 0; return true; }
                    if (inLeft) { topOffsetPx = 8; return true; }
                    break;
                default:
                    break;
            }

            // Additional combined-stair behavior for bottom-left / bottom-right stairs
            if (col == MetatileCollision.COL_BOTTOM_LEFT_STAIRS)
            {
                if (inLeft) { topOffsetPx = 0; return true; }
                if (inRight) { topOffsetPx = 8; return true; }
            }
            if (col == MetatileCollision.COL_BOTTOM_RIGHT_STAIRS)
            {
                if (inRight) { topOffsetPx = 0; return true; }
                if (inLeft) { topOffsetPx = 8; return true; }
            }

            return false;
        }

        // Conservative ceiling blocking test: returns true when this metatile should block
        // upward movement for the given local X within the tile. We treat `COL_TOP` as
        // pass-through from below (top slabs), but otherwise consider non-none tiles
        // as blocking to prevent the player from moving through ceilings.
        private static bool BlocksCeilingAtColumn(MetatileCollision col, int localX)
        {
            if (col == MetatileCollision.COL_NONE) return false;
            if (col == MetatileCollision.COL_TOP) return false; // top slabs are pass-through from below
            return true;
        }

        // Returns true when the tile's collision occupies the pixel at (localX, localY)
        // within the 16x16 metatile. `localX` and `localY` are 0..15 (local coords).
        // This helper understands death-categorized collisions (maps them to shape
        // components) so death-only tiles respect half-slab shapes instead of acting
        // as full 16x16 solids.
        private static bool TileOccupiesPixel(MetatileCollision col, int localX, int localY)
        {
            // Pure death tiles with no solid collision
            switch (col)
            {
                case MetatileCollision.COL_DEATH:
                case MetatileCollision.COL_DEATH_BOTTOM:
                case MetatileCollision.COL_DEATH_TOP:
                case MetatileCollision.COL_DEATH_LEFT:
                case MetatileCollision.COL_DEATH_RIGHT:
                case MetatileCollision.COL_DEATH_TOP_RIGHT:
                case MetatileCollision.COL_DEATH_TOP_LEFT:
                case MetatileCollision.COL_DEATH_BOTTOM_RIGHT:
                case MetatileCollision.COL_DEATH_BOTTOM_LEFT:
                case MetatileCollision.COL_DEATH_TOP_RIGHT_LEFT:
                case MetatileCollision.COL_DEATH_TOP_BOTTOM:
                case MetatileCollision.COL_DEATH_LEFT_RIGHT:
                case MetatileCollision.COL_DEATH_TOP_LEFT_BOTTOM:
                // Pure spike tiles (death only, no solid)
                case MetatileCollision.COL_UP_LEFT_SPIKE:
                case MetatileCollision.COL_UP_RIGHT_SPIKE:
                case MetatileCollision.COL_UP_BOTH_SPIKES:
                case MetatileCollision.COL_DOWN_LEFT_SPIKE:
                case MetatileCollision.COL_DOWN_RIGHT_SPIKE:
                case MetatileCollision.COL_DOWN_BOTH_SPIKES:
                    return false;
                default:
                    break;
            }

            // Handle top-oriented collision categories (top-half slabs and combos).
            // Top slabs occupy the top 8 pixels (localY 0..7). For combined tiles
            // that have top on one half and bottom on the other, choose behavior by
            // column.
            bool inLeft = (localX >= 0 && localX <= 7);
            bool inRight = (localX >= 8 && localX <= 15);

            switch (col)
            {
                case MetatileCollision.COL_TOP:
                case MetatileCollision.COL_TOP_CENTER_SPIKE:
                    return (localY <= 7);
                case MetatileCollision.COL_TOP_LEFT_STAIRS:
                    // Top 8 pixels + left 8 pixels (L-shape: top + left, bottom-right quadrant empty)
                    return (localY <= 7) || (localX <= 7);
                case MetatileCollision.COL_TOP_RIGHT_STAIRS:
                    // Top 8 pixels + right 8 pixels (L-shape: top + right, bottom-left quadrant empty)
                    return (localY <= 7) || (localX >= 8);
                case MetatileCollision.COL_BOTTOM_LEFT_STAIRS:
                    // Left 8 pixels (full height) + bottom-right quadrant (L-shape: left + bottom-right)
                    return (localX <= 7) || (localX >= 8 && localY >= 8);
                case MetatileCollision.COL_BOTTOM_RIGHT_STAIRS:
                    // Right 8 pixels (full height) + bottom-left quadrant (L-shape: right + bottom-left)
                    return (localX >= 8) || (localX <= 7 && localY >= 8);
                case MetatileCollision.COL_BOTTOM_LEFT_SPIKE:
                    // Bottom half full width for collision
                    return (localY >= 8);
                case MetatileCollision.COL_BOTTOM_RIGHT_SPIKE:
                    // Bottom half full width for collision
                    return (localY >= 8);
                case MetatileCollision.COL_LEFT_SPIKE_BLOCK:
                    // Bottom-left quadrant only
                    return (localX < 8 && localY >= 8);
                case MetatileCollision.COL_RIGHT_SPIKE_BLOCK:
                    // Bottom-right quadrant only
                    return (localX >= 8 && localY >= 8);
                case MetatileCollision.COL_UP_LEFT:
                    if (inLeft) return (localY <= 7);
                    break;
                case MetatileCollision.COL_UP_RIGHT:
                    if (inRight) return (localY <= 7);
                    break;
                case MetatileCollision.COL_TOP_LEFT_BOTTOM_RIGHT:
                    if (inLeft) return (localY <= 7);
                    if (inRight) return (localY >= 8);
                    break;
                case MetatileCollision.COL_TOP_RIGHT_BOTTOM_LEFT:
                    if (inRight) return (localY <= 7);
                    if (inLeft) return (localY >= 8);
                    break;
                default:
                    break;
            }

            // For other normal collision categories, attempt to derive a vertical
            // floor offset using the existing `ProvidesFloorAtColumnStatic` helper.
            // If that helper reports a floor offset for this column, treat any
            // localY >= topOffsetPx as occupied by solid (this captures bottom-half
            // slabs and floor-like shapes).
            if (ProvidesFloorAtColumnStatic(col, localX, out int topOffsetPx))
            {
                if (localY >= topOffsetPx) return true;
                return false;
            }

            // Handle slope tiles - check if pixel is below the slope's floor height at this X
            if (IsSlopeTile(col))
            {
                int slopeFloor = GetSlopeFloorAtX(col, localX);
                return (localY >= slopeFloor);
            }

            // Pure death tiles have no solid collision - player passes through
            if (col == MetatileCollision.COL_DEATH)
                return false;

            // Fallback: for ceiling-like categories that don't provide floor offsets
            // treat non-none as fully blocking at this stage.
            if (col != MetatileCollision.COL_NONE) return true;
            return false;
        }
        
        /// <summary>
        /// Check if collision type is a slope
        /// </summary>
        private static bool IsSlopeTile(MetatileCollision col)
        {
            return col >= MetatileCollision.COL_SLOPE_RD45 && col <= MetatileCollision.COL_SLOPE_LU66_BOT;
        }
        
        /// <summary>
        /// Get the floor Y position for a slope tile at a given local X coordinate
        /// Returns the Y pixel (0-15) where the slope's floor is at this X
        /// </summary>
        private static int GetSlopeFloorAtX(MetatileCollision col, int localX)
        {
            // Clamp localX to 0-15
            localX = Math.Max(0, Math.Min(15, localX));
            
            switch (col)
            {
                // 45-degree slopes (1:1 ratio, 16px rise over 16px run)
                case MetatileCollision.COL_SLOPE_RD45: // Right-down 45° (rising left-to-right, falling)
                    return localX; // Y increases with X
                case MetatileCollision.COL_SLOPE_LD45: // Left-down 45° (rising right-to-left, falling)
                    return 15 - localX; // Y increases as X decreases
                case MetatileCollision.COL_SLOPE_RU45: // Right-up 45° (ceiling, rising left-to-right)
                    return 15 - localX; // Y decreases with X (ceiling)
                case MetatileCollision.COL_SLOPE_LU45: // Left-up 45° (ceiling, rising right-to-left)
                    return localX; // Y decreases as X decreases (ceiling)
                
                // 22.5-degree slopes (1:2 ratio, 8px rise over 16px run)
                case MetatileCollision.COL_SLOPE_RD22_RIGHT: // Right half, gentle slope
                    return localX / 2; // 0-7 Y for 0-15 X
                case MetatileCollision.COL_SLOPE_RD22_LEFT: // Left half, gentle slope
                    return 8 + (localX / 2); // 8-15 Y for 0-15 X
                case MetatileCollision.COL_SLOPE_LD22_RIGHT: // Left-down gentle, right half
                    return (15 - localX) / 2;
                case MetatileCollision.COL_SLOPE_LD22_LEFT: // Left-down gentle, left half
                    return 8 + ((15 - localX) / 2);
                case MetatileCollision.COL_SLOPE_RU22_RIGHT: // Right-up gentle (ceiling)
                    return 15 - (localX / 2);
                case MetatileCollision.COL_SLOPE_RU22_LEFT: // Right-up gentle (ceiling)
                    return 7 - (localX / 2);
                case MetatileCollision.COL_SLOPE_LU22_RIGHT: // Left-up gentle (ceiling)
                    return 15 - ((15 - localX) / 2);
                case MetatileCollision.COL_SLOPE_LU22_LEFT: // Left-up gentle (ceiling)
                    return 7 - ((15 - localX) / 2);
                
                // 63.4-degree slopes (2:1 ratio, 16px rise over 8px run)
                case MetatileCollision.COL_SLOPE_RD66_TOP: // Top half, steep slope
                    return Math.Min(15, localX * 2); // 0-15 Y for 0-7 X
                case MetatileCollision.COL_SLOPE_RD66_BOT: // Bottom half, steep slope  
                    return Math.Max(0, (localX * 2) - 16); // Continue from top half
                case MetatileCollision.COL_SLOPE_LD66_TOP: // Left-down steep, top
                    return Math.Min(15, (15 - localX) * 2);
                case MetatileCollision.COL_SLOPE_LD66_BOT: // Left-down steep, bottom
                    return Math.Max(0, ((15 - localX) * 2) - 16);
                case MetatileCollision.COL_SLOPE_RU66_TOP: // Right-up steep (ceiling)
                    return Math.Max(0, 15 - (localX * 2));
                case MetatileCollision.COL_SLOPE_RU66_BOT: // Right-up steep (ceiling)
                    return Math.Min(15, 31 - (localX * 2));
                case MetatileCollision.COL_SLOPE_LU66_TOP: // Left-up steep (ceiling)
                    return Math.Max(0, 15 - ((15 - localX) * 2));
                case MetatileCollision.COL_SLOPE_LU66_BOT: // Left-up steep (ceiling)
                    return Math.Min(15, 31 - ((15 - localX) * 2));
                
                default:
                    return 0; // Fallback
            }
        }

        /// <summary>
        /// Check if a pixel at world coordinates collides with blocking tiles
        /// </summary>
        private bool CheckPixelCollision(int worldX_px, int worldY_px, int groundRowsToReserve)
        {
            int sampleTileX = worldX_px / TILE;
            int sampleTileY = worldY_px / TILE;
            int tileIndexY = sampleTileY + groundRowsToReserve;

            // Out of bounds horizontally = no collision
            if (sampleTileX < 0 || sampleTileX >= mapWidth)
                return false;
            
            // Above the map = solid ceiling (for spider scans)
            if (tileIndexY < 0)
                return true;
                
            // Ground layer is always solid - if tile index is beyond map height
            if (tileIndexY >= mapHeight)
                return true;

            int tid = tiles[tileIndexY * mapWidth + sampleTileX];
            int useTidForAnim = MapAnimatedTileIndex(tid);
            int collisionTid = useTidForAnim;
            
            if (useTidForAnim >= 1000)
            {
                if (useTidForAnim >= 1000 && useTidForAnim <= 1007)
                {
                    collisionTid = 0x08 + ((useTidForAnim - 1000) % 4);
                }
                else if (useTidForAnim >= 1010 && useTidForAnim <= 1015)
                {
                    int group = (useTidForAnim - 1010) % 3;
                    collisionTid = (group == 0) ? 0x04 : (group == 1) ? 0x7D : 0x7F;
                }
                else if (useTidForAnim >= 1020 && useTidForAnim <= 1037)
                {
                    collisionTid = 0x74 + ((useTidForAnim - 1020) % 9);
                }
                else
                {
                    collisionTid = tid;
                }
            }
            
            var collision = MetatileCollisionTable.GetCollision((byte)collisionTid);
            int tileStartX = sampleTileX * TILE;
            int localX = Math.Max(0, Math.Min(TILE - 1, worldX_px - tileStartX));
            int tileStartY = sampleTileY * TILE;
            int localY = Math.Max(0, Math.Min(TILE - 1, worldY_px - tileStartY));

            return TileOccupiesPixel(collision, localX, localY);
        }

        /// <summary>
        /// Check if the player's hitbox overlaps with any death tiles.
        /// Returns true if death collision is detected and NO DEATH mode is OFF.
        /// Returns false otherwise (safe or NO DEATH mode is ON).
        /// When death is detected, also triggers death state and returns the death location via out parameters.
        /// </summary>
        private bool CheckDeathCollision(out int deathX_px, out int deathY_px)
        {
            deathX_px = 0;
            deathY_px = 0;

            // If NO DEATH mode is on, never trigger death
            if (MainWindow.Option_NoDeath) return false;

            // If death already triggered, don't check again
            if (deathTriggered) return false;

            int playerX_px = playerX_fixed >> 8;
            int playerY_px = playerY_fixed >> 8;
            
            // Use player hitbox dimensions
            bool isMini = (currplayer_mini != 0);
            int width = isMini ? 8 : 15;
            int height = isMini ? 7 : 15;
            
            // Apply mini mode offset:
            // Mini cube is 8x7 pixels in a 16x16 space
            // Normal gravity: bottom-left aligned (offset = 16 - 7 = 9)
            // Inverted gravity: top-left aligned (offset = 0)
            if (miniMode)
            {
                if (!gravityFlipped)
                {
                    playerY_px += 9; // Bottom-left alignment
                }
                // else: top-left alignment, no offset needed
            }
            
            int groundRowsToReserve_local = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
            
            // Famidash checks 4 points with insets (bg_coll_floor_spikes):
            // - X: +3 pixels inset on left, +3 pixels inset on right (checks from X+3 to X+width-3)
            // - Y centering: mini mode applies (16 - height) / 2 offset
            // - Top Y: Generic.y + (mini ? (16-height)/2 : 0) + height - 2
            // - Bottom Y: Generic.y + (mini ? (16-height)/2 : 2)
            
            int leftX = playerX_px + 3;
            int rightX = playerX_px + width - 3;
            int rightEdgeX = playerX_px + width - 1;  // Absolute right edge (no inset) for side collision
            
            // NOTE: playerY_px already includes the mini mode offset (4 pixels for 8x7 hitbox)
            // Don't add yMiniOffset again - it would double-offset!
            int yMiniOffset = isMini ? ((16 - height) / 2) : 0;  // For reference: would be 4 for mini
            int topY = playerY_px + height - 2;          // commonly_used_store (no extra offset needed)
            int bottomY = playerY_px + (isMini ? 0 : 2); // commonly_stored_routine_2 (no yMiniOffset, already in playerY_px)
            int centerY = playerY_px + (height / 2);     // bg_side_coll_common middle (no extra offset)
            
            // Check the 4 corner points + right-center for side spikes
            var checkPoints = new[] {
                (leftX, topY),          // Top-left
                (rightX, topY),         // Top-right
                (leftX, bottomY),       // Bottom-left
                (rightX, bottomY),      // Bottom-right
                (rightEdgeX, centerY)   // Right-center (for right-facing spikes, no X inset)
            };
            
            foreach (var (px, py) in checkPoints)
            {
                int sampleTileX = px / TILE;
                int sampleTileY = py / TILE;
                int tileIndexY = sampleTileY + groundRowsToReserve_local;

                if (tileIndexY < 0 || tileIndexY >= mapHeight || sampleTileX < 0 || sampleTileX >= mapWidth)
                    continue;

                int tid = tiles[tileIndexY * mapWidth + sampleTileX];
                int useTidForAnim = MapAnimatedTileIndex(tid);
                int collisionTid = useTidForAnim;
                
                // Debug: log what we're checking
                try { AppendSimDebug($"[DEATH_CHECK] Point ({px},{py}) -> Tile({sampleTileX},{sampleTileY}) TID={tid}"); } catch { }

                if (useTidForAnim >= 1000)
                {
                    if (useTidForAnim >= 1000 && useTidForAnim <= 1007)
                    {
                        collisionTid = 0x08 + ((useTidForAnim - 1000) % 4);
                    }
                    else if (useTidForAnim >= 1010 && useTidForAnim <= 1015)
                    {
                        int group = (useTidForAnim - 1010) % 3;
                        collisionTid = (group == 0) ? 0x04 : (group == 1) ? 0x7D : 0x7F;
                    }
                    else if (useTidForAnim >= 1020 && useTidForAnim <= 1037)
                    {
                        collisionTid = 0x74 + ((useTidForAnim - 1020) % 9);
                    }
                    else
                    {
                        collisionTid = tid;
                    }
                }

                var collision = MetatileCollisionTable.GetCollision((byte)collisionTid);
                int tileStartX = sampleTileX * TILE;
                int localX = Math.Max(0, Math.Min(TILE - 1, px - tileStartX));
                int tileStartY = sampleTileY * TILE;
                int localY = Math.Max(0, Math.Min(TILE - 1, py - tileStartY));

                // Check if this pixel causes death
                if (MetatileCollisionTable.TileKillsAtPixel(collision, localX, localY))
                {
                    deathX_px = px;
                    deathY_px = py;
                    return true;
                }
            }

            return false;
        }
        
        /// <summary>
        /// Stop music playback
        /// </summary>
        private async System.Threading.Tasks.Task StopMusicAsync()
        {
            try
            {
                if (this.Owner is MainWindow mw)
                {
                    AppendSimDebug("[MUSIC] Stopping music");
                    await mw.StopSimulatorPlaybackAsync();
                }
            }
            catch { }
        }

        /// <summary>
        /// Check for gravity portal collision and activate if touched
        /// Portals: 0x08,0x10,0x11,0xFC = normal gravity
        ///          0x09,0x12,0x13,0xFB = reversed gravity
        /// </summary>
        private void CheckGravityPortals()
        {
            try
            {
                int playerX_px = playerX_fixed >> 8;
                int playerY_px = playerY_fixed >> 8;
                
                // Use actual collision hitbox size, not visual size
                int hitboxW = miniMode ? 8 : 15;
                int hitboxH = miniMode ? 7 : 15;
                
                // Apply mini mode offset: bottom-left for normal, top-left for inverted
                if (miniMode)
                {
                    if (!gravityFlipped)
                        playerY_px += 9;
                }
                
                // Player bounding box for collision using actual hitbox size
                int playerLeft_px = playerX_px;
                int playerRight_px = playerX_px + hitboxW - 1;
                int playerTop_px = playerY_px;
                int playerBottom_px = playerY_px + hitboxH - 1;
                
                AppendSimDebug($"[GRAV_PORTAL_CHECK] mini={miniMode} grav={gravityFlipped} playerBox=({playerLeft_px},{playerTop_px})-({playerRight_px},{playerBottom_px}) size={hitboxW}x{hitboxH}");
                
                // Iterate through ALL sprites and check for gravity portals
                for (int idx = 0; idx < sprites.Length; idx++)
                {
                    int sid = sprites[idx];
                    if (sid < 0) continue;
                    
                    // Check if this sprite is a gravity portal
                    bool isNormalGravityPortal = (sid == 0x08 || sid == 0x10 || sid == 0x11 || sid == 0xFC);
                    bool isReversedGravityPortal = (sid == 0x09 || sid == 0x12 || sid == 0x13 || sid == 0xFB);
                    
                    if (!isNormalGravityPortal && !isReversedGravityPortal) continue;
                    
                    // Check if already activated
                    if (processedGravityPortals.Contains(idx)) continue;
                    
                    // Use SpriteIntersectsPlayer to check sprite hitbox overlap
                    if (SpriteIntersectsPlayer(idx, sid, playerLeft_px, playerRight_px, playerTop_px, playerBottom_px))
                    {
                        // Activate portal with proper conditional logic
                        bool activated = false;
                        
                        // Reverse portals: only activate if gravity is currently normal
                        if (isReversedGravityPortal && !gravityReversed)
                        {
                            gravityReversed = true;
                            gravityFlipped = true;
                            currplayer_gravity = 0xFF;
                            activated = true;
                            
                            AppendSimDebug($"[GRAV_PORTAL] REVERSE ACTIVATED: mini={miniMode}/{currplayer_mini} grav={gravityFlipped}/{currplayer_gravity:X2} reversed={gravityReversed}");
                        }
                        // Normal portals: only activate if gravity is currently reversed
                        else if (isNormalGravityPortal && gravityReversed)
                        {
                            gravityReversed = false;
                            gravityFlipped = false;
                            currplayer_gravity = 0x00;
                            activated = true;
                            
                            AppendSimDebug($"[GRAV_PORTAL] NORMAL ACTIVATED: mini={miniMode}/{currplayer_mini} grav={gravityFlipped}/{currplayer_gravity:X2} reversed={gravityReversed}");
                        }
                        
                        if (activated)
                        {
                            // Update player icon flip on UI thread
                            try { Dispatcher?.BeginInvoke(new Action(() => UpdatePlayerIconFlip())); } catch { }
                            
                            // Halve Y velocity
                            playerVelY_fixed /= 2;
                            
                            AppendSimDebug($"[GRAV_PORTAL] POST-ACTIVATION: currplayer_gravity={currplayer_gravity:X2} gravityFlipped={gravityFlipped} gravityReversed={gravityReversed}");
                            
                            // Mark as activated
                            processedGravityPortals.Add(idx);
                            
                            // Only one portal per frame
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppendSimDebug($"[GRAVITY PORTAL] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Check for mini/growth portal collision and activate if touched
        /// Portals: 0x18 = mini portal, 0x19 = growth portal
        /// </summary>
        private void CheckMiniGrowthPortals()
        {
            try
            {
                int playerX_px = playerX_fixed >> 8;
                int playerY_px = playerY_fixed >> 8;
                
                // Use actual collision hitbox size (15x15 for normal, 8x7 for mini)
                bool isMini = (currplayer_mini != 0);
                int hitboxW = isMini ? 8 : 15;
                int hitboxH = isMini ? 7 : 15;
                
                // Apply Y offset for collision hitbox positioning within 16x16 player space
                // Mini mode: Y+9 (normal gravity) or Y+0 (inverted gravity)
                // Normal mode: Y+0 (normal gravity) or Y+1 (inverted gravity)
                if (isMini)
                {
                    if (currplayer_gravity == 0)
                    {
                        playerY_px += 9;  // Normal gravity: hitbox at bottom
                    }
                    // else: inverted gravity, hitbox at Y+0 (top)
                }
                else
                {
                    if (currplayer_gravity != 0)
                    {
                        playerY_px += 1;  // Inverted gravity: shift down 1px
                    }
                    // else: normal gravity, hitbox at Y+0
                }
                
                // Player bounding box for collision
                int playerLeft_px = playerX_px;
                int playerRight_px = playerX_px + hitboxW - 1;
                int playerTop_px = playerY_px;
                int playerBottom_px = playerY_px + hitboxH - 1;
                
                // Iterate through ALL sprites and check for mini/growth portals
                for (int idx = 0; idx < sprites.Length; idx++)
                {
                    int sid = sprites[idx];
                    if (sid < 0) continue;
                    
                    // Check if this sprite is a mini or growth portal
                    bool isMiniPortal = (sid == 0x18);
                    bool isGrowthPortal = (sid == 0x19);
                    
                    if (!isMiniPortal && !isGrowthPortal) continue;
                    
                    // Check if already activated
                    if (processedMiniPortals.Contains(idx)) continue;
                    
                    // Use SpriteIntersectsPlayer to check sprite hitbox overlap
                    if (SpriteIntersectsPlayer(idx, sid, playerLeft_px, playerRight_px, playerTop_px, playerBottom_px))
                    {
                        bool newMiniMode = isMiniPortal;
                        
                        if (miniMode != newMiniMode)
                        {
                            miniMode = newMiniMode;
                            currplayer_mini = (byte)(miniMode ? 1 : 0);
                            
                            // Update UI checkbox
                            try { Dispatcher.BeginInvoke(new Action(() => { if (MiniCheckBox != null) MiniCheckBox.IsChecked = miniMode; })); } catch { }
                            
                            // Update player visuals
                            try { UpdatePlayerImageForMode(); } catch { }
                            try { UpdatePlayerVisualSizeForMode(); } catch { }
                            
                            // Mark as activated
                            processedMiniPortals.Add(idx);
                            
                            // Only one portal per frame
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppendSimDebug($"[MINI/GROWTH PORTAL] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Check for alphabet block collisions (S, D, H, J, F blocks)
        /// S_BLOCK (0xF9): Stops dashing, sets orbed, sets velocityY to 0
        /// D_BLOCK (0xFA): Sets dblocked (prevents wave from moving in one direction)
        /// H_BLOCK (0xF8): Sets hblocked (headbonk - causes instant ceiling ejection)
        /// J_BLOCK (0xF7): Sets jblocked and orbed (requires press instead of hold for next jump)
        /// F_BLOCK (0xF6): Sets fblocked (forces press-to-jump and flips gravity on ceiling/floor hit)
        /// </summary>
        private void CheckAlphabetBlocks()
        {
            try
            {
                int playerX_px = playerX_fixed >> 8;
                int playerY_px = playerY_fixed >> 8;
                
                // Player bounding box for collision (no offset needed - sprite positions are already in world coords)
                int playerLeft_px = playerX_px;
                int playerRight_px = playerX_px + playerVisualWidth - 1;
                int playerTop_px = playerY_px;
                int playerBottom_px = playerY_px + playerVisualHeight - 1;
                
                for (int idx = 0; idx < sprites.Length; idx++)
                {
                    int sid = sprites[idx];
                    if (sid < 0) continue;
                    
                    // Check if this sprite is an alphabet block
                    bool isSBlock = (sid == 0xF9);
                    bool isDBlock = (sid == 0xFA);
                    bool isHBlock = (sid == 0xF8);
                    bool isJBlock = (sid == 0xF7);
                    bool isFBlock = (sid == 0xF6);
                    
                    if (!isSBlock && !isDBlock && !isHBlock && !isJBlock && !isFBlock) continue;
                    
                    // Check for collision
                    if (SpriteIntersectsPlayer(idx, sid, playerLeft_px, playerRight_px, playerTop_px, playerBottom_px))
                    {
                        // S_BLOCK: Stop dashing, set orbed, zero velocity (sprite_loading.h line 935)
                        if (isSBlock && dashing != 0)
                        {
                            dashing = 0;
                            orbed = true;
                            playerVelY_fixed = 0;
                            velocityY = 0;
                            AppendSimDebug($"[S_BLOCK] Stopped dash, orbed=true, velocityY=0");
                        }
                        // D_BLOCK: Set dblocked (sprite_loading.h line 940)
                        else if (isDBlock)
                        {
                            dblocked = true;
                            AppendSimDebug($"[D_BLOCK] dblocked=true");
                        }
                        // H_BLOCK: Set hblocked (headbonk) (sprite_loading.h line 938)
                        else if (isHBlock)
                        {
                            hblocked = true;
                            AppendSimDebug($"[H_BLOCK] hblocked=true (headbonk)");
                        }
                        // J_BLOCK: Set jblocked and orbed (sprite_loading.h line 939)
                        else if (isJBlock)
                        {
                            jblocked = true;
                            orbed = true;
                            AppendSimDebug($"[J_BLOCK] jblocked=true, orbed=true");
                        }
                        // F_BLOCK: Set fblocked (sprite_loading.h line 941)
                        else if (isFBlock)
                        {
                            fblocked = true;
                            AppendSimDebug($"[F_BLOCK] fblocked=true");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppendSimDebug($"[ALPHABET BLOCKS] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Check for pad collision and apply velocity change
        /// Pads: 0x0A/0x0C = yellow pad (down/up), 0x25/0x26 = pink pad (down/up), 0x52/0x53 = red pad (down/up)
        /// </summary>
        private void CheckPadCollision()
        {
            try
            {
                // In famidash sprite_collide(), Generic.x = high_byte(currplayer_x) + 1
                // This means player X is offset +1 pixel to the RIGHT for sprite collision!
                int playerX_px = (playerX_fixed >> 8) + 1;
                int playerY_px = playerY_fixed >> 8;
                
                // Use actual collision hitbox size (15x15 for normal, 8x7 for mini)
                bool miniMode = (currplayer_mini != 0);
                bool gravityFlipped = (currplayer_gravity != 0);
                int hitboxW = miniMode ? 8 : 15;
                int hitboxH = miniMode ? 7 : 15;
                
                // playerY_fixed is the visual sprite top-left position
                // Need to calculate actual hitbox position based on mode and gravity
                // From rendering code: normal hitbox is at Y+0, mini at Y+9 (normal gravity) or Y+0 (inverted)
                if (miniMode && !gravityFlipped)
                {
                    playerY_px += 9;  // Mini normal gravity: hitbox at bottom of 16x16 visual
                }
                // For all other cases (normal cube, or mini inverted), hitbox starts at visual Y
                
                // Player bounding box for collision
                int playerLeft_px = playerX_px;
                int playerRight_px = playerX_px + hitboxW - 1;
                int playerTop_px = playerY_px;
                int playerBottom_px = playerY_px + hitboxH - 1;
                
                // Iterate through ALL sprites and check for pads
                for (int idx = 0; idx < sprites.Length; idx++)
                {
                    int sid = sprites[idx];
                    if (sid < 0) continue;
                    
                    // Check if this sprite is a pad
                    // Yellow pads: 0x0A (down), 0x0C (up)
                    // Pink pads: 0x25 (down), 0x26 (up)
                    // Red pads: 0x52 (down), 0x53 (up)
                    int padRow = -1;
                    bool isDownPad = false;
                    
                    if (sid == 0x0A || sid == 0x0C)
                    {
                        padRow = 1; // yellow pad
                        isDownPad = (sid == 0x0A);
                    }
                    else if (sid == 0x25 || sid == 0x26)
                    {
                        padRow = 3; // pink pad
                        isDownPad = (sid == 0x25);
                    }
                    else if (sid == 0x52 || sid == 0x53)
                    {
                        padRow = 8; // red pad
                        isDownPad = (sid == 0x52);
                    }
                    else
                    {
                        continue; // Not a pad
                    }
                    
                    // Use standard AABB collision for pads
                    if (SpriteIntersectsPlayer(idx, sid, playerLeft_px, playerRight_px, playerTop_px, playerBottom_px))
                    {
                        // In famidash, pad activation tracking (idx8_inc(activesprites_activated, index)) is commented out
                        // This means pads activate EVERY frame while the player is touching them
                        // This is the correct behavior - don't add activation tracking for pads
                        
                        // Get pad position for debugging
                        int padTileX = idx % mapWidth;
                        int padTileY = idx / mapWidth;
                        int padWorldX = padTileX * TILE;
                        int padWorldY = padTileY * TILE;
                        
                        AppendSimDebug($"[PAD_PRE] Frame overlap detected! sid=0x{sid:X2}, padRow={padRow}, velY_before=0x{playerVelY_fixed:X4}");
                        AppendSimDebug($"[PAD_COLLISION] Player: L={playerLeft_px} R={playerRight_px} T={playerTop_px} B={playerBottom_px}, Pad: tileX={padTileX} tileY={padTileY} worldX={padWorldX} worldY={padWorldY}");
                        
                        // Get velocity from table based on game mode and mini state
                        int modeCol = currentGameMode;
                        if (modeCol == 8) modeCol = 0; // Ninja uses cube values
                        if (modeCol >= 0 && modeCol < 8 && padRow >= 0 && padRow < 9)
                        {
                            bool isMini = (currplayer_mini != 0);
                            int baseVel = isMini 
                                ? PadOrbHeights_Mini[padRow][modeCol] 
                                : PadOrbHeights[padRow][modeCol];
                            
                            // Apply gravity direction
                            bool gravityInverted = (currplayer_gravity != 0);
                            
                            // Yellow/pink/red pads: always launch in gravity-opposite direction
                            // Normal gravity: launch upward (negative velocity)
                            // Inverted gravity: launch downward (positive velocity)
                            // The sprite orientation (up/down) doesn't matter for these pads
                            int gravityMultiplier = gravityInverted ? 1 : -1;
                            int newVel = baseVel * gravityMultiplier;
                            
                            playerVelY_fixed = newVel;
                            orbhitonthisframe = true; // Signal that a pad/orb was activated this frame (prevents bounce in Pogo)
                            
                            AppendSimDebug($"[PAD_SET] sid=0x{sid:X2}, row={padRow}, mode={modeCol}, mini={isMini}, baseVel=0x{baseVel:X4}, newVel=0x{newVel:X4}, velY_after=0x{playerVelY_fixed:X4}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppendSimDebug($"[PAD COLLISION] Error: {ex.Message}");
            }
        }
        
        private void CheckSpiderOrbPadCollision()
        {
            try
            {
                // Spider orbs/pads work in ALL game modes (they switch you to spider)
                
                int playerX_px = playerX_fixed >> 8;
                int playerY_px = playerY_fixed >> 8;
                
                // Use actual collision hitbox size, not visual size
                int hitboxWidth = miniMode ? 8 : 15;
                int hitboxHeight = miniMode ? 7 : 15;
                
                // Apply mini mode offset: bottom-left for normal, top-left for inverted
                if (miniMode && !gravityFlipped)
                {
                    playerY_px += 9;
                }
                
                // Player bounding box for collision
                int playerLeft_px = playerX_px;
                int playerRight_px = playerX_px + hitboxWidth - 1;
                int playerTop_px = playerY_px;
                int playerBottom_px = playerY_px + hitboxHeight - 1;
                
                bool holdingJump = IsXDownAsync() || keyXHeld;
                int pressCount = Interlocked.CompareExchange(ref keyXPressedCount, 0, 0);
                bool pressedJump = pressCount > 0;
                
                int spiderOrbPadCount = 0;
                
                // Iterate through ALL sprites and check for spider orbs/pads
                for (int idx = 0; idx < sprites.Length; idx++)
                {
                    int sid = sprites[idx];
                    if (sid < 0) continue;
                    
                    // Spider orb up: 0x54, Spider orb down: 0x55
                    // Spider pad up: 0x56, Spider pad down: 0x57
                    bool isSpiderOrbUp = (sid == 0x54);
                    bool isSpiderOrbDown = (sid == 0x55);
                    bool isSpiderPadUp = (sid == 0x56);
                    bool isSpiderPadDown = (sid == 0x57);
                    
                    if (!isSpiderOrbUp && !isSpiderOrbDown && !isSpiderPadUp && !isSpiderPadDown)
                        continue;
                    
                    spiderOrbPadCount++;
                    
                    // Check if already activated (ONLY for orbs, not pads - pads can trigger multiple times)
                    bool isOrb = (isSpiderOrbUp || isSpiderOrbDown);
                    if (isOrb && orbActivated.ContainsKey(idx) && orbActivated[idx])
                        continue;
                    
                    // Use CheckOrbCollision for more reliable detection (same as regular orbs)
                    bool intersects = false;
                    try
                    {
                        intersects = CheckOrbCollision(idx, sid, playerLeft_px, playerRight_px, playerTop_px, playerBottom_px);
                    }
                    catch (Exception ex)
                    {
                        AppendSimDebug($"[SPIDER_ORB/PAD] CheckOrbCollision error for 0x{sid:X2} at idx {idx}: {ex.Message}");
                        continue;
                    }
                    
                    if (intersects)
                    {
                        AppendSimDebug($"[SPIDER_ORB/PAD] *** COLLISION DETECTED *** with 0x{sid:X2} at idx {idx}, isOrb={isOrb}, holdJump={holdingJump}, pressJump={pressedJump}");
                        
                        bool shouldActivate = false;
                        
                        // Orbs require X press/hold, pads activate immediately on collision
                        if (isOrb && (pressedJump || holdingJump))
                        {
                            shouldActivate = true;
                        }
                        else if (!isOrb) // Pads
                        {
                            shouldActivate = true;
                        }
                        
                        if (shouldActivate)
                        {
                            AppendSimDebug($"[SPIDER_ORB/PAD] ACTIVATING 0x{sid:X2}");
                            
                            int groundRowsToReserve = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                            bool isMini = (currplayer_mini != 0);
                            int hitboxW = isMini ? 8 : 15;
                            int hitboxH = isMini ? 7 : 15;
                            int hitboxOffsetY = isMini ? ((0x10 - hitboxH) >> 1) : 0;
                            
                            if (isSpiderOrbUp || isSpiderPadUp)
                            {
                                // Teleport upward (flip to ceiling)
                                AppendSimDebug($"[SPIDER_ORB/PAD] Teleporting UP");
                                
                                // Eject from current surface first (eject_D in famidash)
                                var (collided_pre, eject_pre) = BgCollD_Spider(playerX_px, playerY_px + hitboxOffsetY, hitboxW, hitboxH, groundRowsToReserve);
                                if (collided_pre)
                                {
                                    playerY_fixed -= (eject_pre << 8);
                                    AppendSimDebug($"[SPIDER_ORB/PAD] Ejected from floor by {eject_pre}px");
                                }
                                playerVelY_fixed = 0;
                                
                                // Flip gravity
                                currplayer_gravity = 0xFF; // GRAVITY_UP
                                gravityReversed = true;
                                gravityFlipped = true;
                                UpdateCurrplayerTableIdx_Fresh();
                                
                                // Scan upward for ceiling
                                SpiderUpWait_Fresh();
                                
                                // Apply final eject (eject_U in famidash)
                                int finalY = playerY_fixed >> 8;
                                var (collided_final, eject_final) = BgCollU_Spider(playerX_px, finalY, hitboxW, hitboxH, groundRowsToReserve);
                                if (collided_final)
                                {
                                    playerY_fixed -= (eject_final << 8);
                                    AppendSimDebug($"[SPIDER_ORB/PAD] Ejected from ceiling by {eject_final}px");
                                }
                                playerVelY_fixed = 0;
                                
                                // Set orbed flag
                                orbed = true;
                                
                                try { Dispatcher?.BeginInvoke(new Action(() => UpdatePlayerIconFlip())); } catch { }
                                AppendSimDebug($"[SPIDER_ORB/PAD] Teleported to ceiling Y={playerY_fixed >> 8}");
                            }
                            else // Down
                            {
                                // Teleport downward (flip to floor)
                                AppendSimDebug($"[SPIDER_ORB/PAD] Teleporting DOWN");
                                
                                // Eject from current surface first (eject_U + 1 in famidash)
                                var (collided_pre, eject_pre) = BgCollU_Spider(playerX_px, playerY_px, hitboxW, hitboxH, groundRowsToReserve);
                                if (collided_pre)
                                {
                                    playerY_fixed += ((eject_pre + 1) << 8);
                                    AppendSimDebug($"[SPIDER_ORB/PAD] Ejected from ceiling by {eject_pre + 1}px");
                                }
                                playerVelY_fixed = 0;
                                
                                // Flip gravity
                                currplayer_gravity = 0x00; // GRAVITY_DOWN
                                gravityReversed = false;
                                gravityFlipped = false;
                                UpdateCurrplayerTableIdx_Fresh();
                                
                                // Scan downward for floor
                                SpiderDownWait_Fresh();
                                
                                // Apply final eject (eject_D in famidash)
                                int finalY = playerY_fixed >> 8;
                                bool isMiniLocal = (currplayer_mini != 0);
                                int hitboxHLocal = isMiniLocal ? 7 : 15;
                                hitboxOffsetY = isMiniLocal ? ((0x10 - hitboxHLocal) >> 1) : 0;
                                var (collided_final, eject_final) = BgCollD_Spider(playerX_px, finalY + hitboxOffsetY, hitboxW, hitboxH, groundRowsToReserve);
                                if (collided_final)
                                {
                                    playerY_fixed -= (eject_final << 8);
                                    AppendSimDebug($"[SPIDER_ORB/PAD] Ejected from floor by {eject_final}px");
                                }
                                playerVelY_fixed = 0;
                                
                                // Set orbed flag
                                orbed = true;
                                
                                try { Dispatcher?.BeginInvoke(new Action(() => UpdatePlayerIconFlip())); } catch { }
                                AppendSimDebug($"[SPIDER_ORB/PAD] Teleported to floor Y={playerY_fixed >> 8}");
                            }
                            
                            // Mark orb as activated (pads don't get marked - they can trigger multiple times)
                            if (isOrb)
                            {
                                orbActivated[idx] = true;
                            }
                            
                            // Consume press if it was an orb activation
                            if (isOrb && pressedJump)
                            {
                                Interlocked.Exchange(ref keyXPressedCount, 0);
                            }
                            
                            return; // Only one per frame
                        }
                        else
                        {
                            AppendSimDebug($"[SPIDER_ORB/PAD] Did NOT activate - isOrb={isOrb}, pressJump={pressedJump}, holdJump={holdingJump}");
                        }
                    }
                }
                
                if (spiderOrbPadCount > 0)
                {
                    AppendSimDebug($"[SPIDER_ORB/PAD] Scanned {spiderOrbPadCount} spider orbs/pads in level");
                }
            }
            catch (Exception ex)
            {
                AppendSimDebug($"[SPIDER_ORB/PAD COLLISION] Error: {ex.Message}");
            }
        }

        // Helper: returns true when the player's head is overlapping a blocking ceiling
        // in the current world position. This mirrors the ceiling-collision check used
        // in the numeric integration path but does not modify player state.
        private bool IsTouchingCeiling()
        {
            try
            {
                const int HITBOX_W_LOCAL = 15;
                int playerCenter_px_local = (playerX_fixed >> 8) + (playerVisualWidth / 2);
                int playerLeft_px_local = playerCenter_px_local - (HITBOX_W_LOCAL / 2);
                int playerRight_px_local = playerLeft_px_local + (HITBOX_W_LOCAL - 1);
                int headWorldY_px_local = (playerY_fixed >> 8); // player's top

                int tileAboveY_world = headWorldY_px_local / TILE;
                int groundRowsToReserve_local = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                int tileIndexY = tileAboveY_world + groundRowsToReserve_local;

                if (tileIndexY < 0 || tileIndexY >= mapHeight) return false;

                for (int tx_local = playerLeft_px_local / TILE; tx_local <= playerRight_px_local / TILE; tx_local++)
                {
                    if (tx_local < 0 || tx_local >= mapWidth) continue;
                    int tid_local = tiles[tileIndexY * mapWidth + tx_local];
                    int useTidForAnim_local = MapAnimatedTileIndex(tid_local);
                    int collisionTid_local = useTidForAnim_local;
                    if (useTidForAnim_local >= 1000)
                    {
                        if (useTidForAnim_local >= 1000 && useTidForAnim_local <= 1007)
                        {
                            collisionTid_local = 0x08 + ((useTidForAnim_local - 1000) % 4);
                        }
                        else if (useTidForAnim_local >= 1010 && useTidForAnim_local <= 1015)
                        {
                            int group_local = (useTidForAnim_local - 1010) % 3;
                            collisionTid_local = (group_local == 0) ? 0x04 : (group_local == 1) ? 0x7D : 0x7F;
                        }
                        else if (useTidForAnim_local >= 1020 && useTidForAnim_local <= 1037)
                        {
                            collisionTid_local = 0x74 + ((useTidForAnim_local - 1020) % 9);
                        }
                        else
                        {
                            collisionTid_local = tid_local;
                        }
                    }
                    var col_local = MetatileCollisionTable.GetCollision((byte)collisionTid_local);

                    int tileStartX_local = tx_local * TILE;
                    int localLeft_local = Math.Max(0, playerLeft_px_local - tileStartX_local);
                    int localRight_local = Math.Min(TILE - 1, playerRight_px_local - tileStartX_local);

                    for (int lx_local = localLeft_local; lx_local <= localRight_local; lx_local++)
                    {
                        // Note: here we respect pass-through semantics elsewhere by default.
                        // This function is the permissive check used by most systems; for
                        // jump eligibility we also provide a strict variant below.
                        int worldPx = tileStartX_local + lx_local;
                        int playerCenter_px = (playerX_fixed >> 8) + (playerVisualWidth / 2);
                        if (!MainWindow.Option_NoDeath)
                        {
                            if (worldPx >= playerCenter_px) continue;
                        }
                        if (BlocksCeilingAtColumn(col_local, lx_local)) return true;
                    }
                }

                return false;
            }
            catch { return false; }
        }

        // Strict variant of ceiling check that ignores right-side pass-through.
        // Used for jump eligibility so a player who is overlapping a blocking
        // ceiling can still jump even when right-side pass-through is enabled.
        private bool IsTouchingCeilingStrict()
        {
            try
            {
                const int HITBOX_W_LOCAL = 15;
                int playerCenter_px_local = (playerX_fixed >> 8) + (playerVisualWidth / 2);
                int playerLeft_px_local = playerCenter_px_local - (HITBOX_W_LOCAL / 2);
                int playerRight_px_local = playerLeft_px_local + (HITBOX_W_LOCAL - 1);
                int headWorldY_px_local = (playerY_fixed >> 8); // player's top

                int tileAboveY_world = headWorldY_px_local / TILE;
                int groundRowsToReserve_local = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                int tileIndexY = tileAboveY_world + groundRowsToReserve_local;

                if (tileIndexY < 0 || tileIndexY >= mapHeight) return false;

                for (int tx_local = playerLeft_px_local / TILE; tx_local <= playerRight_px_local / TILE; tx_local++)
                {
                    if (tx_local < 0 || tx_local >= mapWidth) continue;
                    int tid_local = tiles[tileIndexY * mapWidth + tx_local];
                    int useTidForAnim_local = MapAnimatedTileIndex(tid_local);
                    int collisionTid_local = useTidForAnim_local;
                    if (useTidForAnim_local >= 1000)
                    {
                        if (useTidForAnim_local >= 1000 && useTidForAnim_local <= 1007)
                        {
                            collisionTid_local = 0x08 + ((useTidForAnim_local - 1000) % 4);
                        }
                        else if (useTidForAnim_local >= 1010 && useTidForAnim_local <= 1015)
                        {
                            int group_local = (useTidForAnim_local - 1010) % 3;
                            collisionTid_local = (group_local == 0) ? 0x04 : (group_local == 1) ? 0x7D : 0x7F;
                        }
                        else if (useTidForAnim_local >= 1020 && useTidForAnim_local <= 1037)
                        {
                            collisionTid_local = 0x74 + ((useTidForAnim_local - 1020) % 9);
                        }
                        else
                        {
                            collisionTid_local = tid_local;
                        }
                    }
                    var col_local = MetatileCollisionTable.GetCollision((byte)collisionTid_local);

                    int tileStartX_local = tx_local * TILE;
                    int localLeft_local = Math.Max(0, playerLeft_px_local - tileStartX_local);
                    int localRight_local = Math.Min(TILE - 1, playerRight_px_local - tileStartX_local);

                    // Compute the local Y within the tile corresponding to the player's head
                    int tileStartY_local = tileAboveY_world * TILE;
                    int localY_local = Math.Max(0, Math.Min(TILE - 1, headWorldY_px_local - tileStartY_local));

                    for (int lx_local = localLeft_local; lx_local <= localRight_local; lx_local++)
                    {
                        // Use per-pixel occupancy so top-half slabs (COL_TOP etc.) are
                        // considered blocking for the strict ceiling check used by jump
                        // eligibility. This treats any occupied pixel at the player's
                        // head Y as a blocking ceiling regardless of pass-through semantics.
                        try
                        {
                            if (TileOccupiesPixel(col_local, lx_local, localY_local)) return true;
                        }
                        catch { if (BlocksCeilingAtColumn(col_local, lx_local)) return true; }
                    }
                }

                return false;
            }
            catch { return false; }
        }

        // Parallax / ground data passed from the editor so simulator can mirror preview-mode
        private ImageSource?[]? parallaxImages;
        private ImageSource?[]? parallaxTonedImages;
        private ImageSource? parallaxBitmap = null;
        private ImageSource? parallaxBitmapToned = null;
        private double parallaxX = 1.0;
        private double parallaxY = 1.0;
        private bool parallaxRepeatX = true;
        private bool parallaxRepeatY = true;
        private bool hasParallaxLayer = false;

        private ImageSource?[]? groundImages;
        private ImageSource?[]? groundTonedImages;
        private double groundOffsetY = 0.0;
        private bool groundRepeatX = true;
        private bool hasGroundLayer = false;
        private int groundTileRows = 0;
        // Fixed-point camera X with 8 fractional bits
        private int cameraX_fixed = 0;
        // cameraY in fixed-point (8 fractional bits)
        private int cameraY_fixed = 0; // pixels * 256

        // speed per frame (0x02C4 fixed point with 8 fractional bits)
        private const int SPEED_FIXED = 0x02C4;
        // Dynamic current speed (fixed-point, 8 fractional bits). Starts at 1x.
        private int currentSpeed_fixed = 0x02C4;

        // Speed constants (fixed-point values, 8 fractional bits)
        private const int CUBE_SPEED_X05 = 0x23B;
        private const int CUBE_SPEED_X1  = 0x02C4;
        private const int CUBE_SPEED_X2  = 0x0371;
        private const int CUBE_SPEED_X3  = 0x0429;
        private const int CUBE_SPEED_X4  = 0x051E;
        private const int CUBE_SPEED_SLOW = 0x16E; // 0.1x special speed

        // Simple cube physics (fixed-point, 8 fractional bits)
        // Max downward velocity (fixed-point). Default 0x0600 (0x06 << 8).
        // This is now a runtime-configurable field so simulator can reflect level settings.
        private int CUBE_MAX_FALLSPEED = 0x600; // max downward velocity
        private const int CUBE_GRAVITY = 0x6B; // gravity added per frame
        private const int CUBE_JUMP_VEL = -0x590; // jump impulse (negative = upward)
        // Robot mode constants
        private const int ROBOT_JUMP_VEL = -0x2B0;
        // UFO mode constants (allow mid-air pulses / different gravity)
        private const int UFO_GRAVITY = 0x0032;
        private const int UFO_MAX_FALLSPEED = 0x0320;
        private const int UFO_JUMP_VEL = -0x0330;
        // Ship physics constants (fixed-point, 8 fractional bits)
        // Values provided by user in high-byte pixel / low-byte subpixel format
        private const int SHIP_MAX_FALLSPEED = 0x0369;
        private const int SHIP_MAX_FALLSPEED_HOLD = 0x0443;
        // Ball mode constants
        private const int BALL_GRAVITY = 0x0066;
        private const int BALL_MAX_FALLSPEED = 0x0733;
        private const int BALL_IMMEDIATE_VEL = 0x0266;
        private const int SHIP_GRAVITY_BASE = 0x003C;
        private const int SHIP_GRAVITY = 0x0030;
        private const int SHIP_GRAVITY_AFTER_HOLD = 0x0049;
        private const int SHIP_GRAVITY_HOLD_FALL = 0x004C;

        // Current player game mode: 0 = cube, 1 = ship, etc. Defaults to cube.
        private int currentGameMode = 0;
        
        // Unified physics state variables (shared across all modes)
        private int velocityY = 0;         // Vertical velocity (8.8 fixed point)
#pragma warning disable CS0414
        private int velocityX = 0x0300;    // Horizontal velocity (for wave mode)
#pragma warning restore CS0414
        private bool gravityFlipped = false; // True = up, False = down
        private bool miniMode = false;     // Mini mode flag
        private int speed = 1;             // Speed mode: 0=0.5x, 1=1x, 2=2x, 3=3x, 4=4x, 5=0.1x
#pragma warning disable CS0414
        private bool previousJumpState = false; // Track jump button for press detection
#pragma warning restore CS0414
        
        // Runtime-effective physics values (adjusted when gravity is reversed)
        private int effectiveGravity_fixed;
        private int effectiveJumpVel_fixed;
        private int effectiveMaxFall_fixed;
        private bool gravityReversed = false;
        // NOTE: `gravityReversed` is the canonical logical gravity direction used
        // by collision/landing logic. A separate flag `effectiveInvertedByW`
        // allows the editor's No-Death option to invert numeric physics (gravity,
        // jump, max-fall) without changing collision semantics. Numeric inversion
        // is computed as `gravityReversed || effectiveInvertedByW`.
        private bool effectiveInvertedByW = false;
        // Track gravity portals we've already activated this pass so each
        // portal activates only once per crossing.
        private System.Collections.Generic.HashSet<int> processedGravityPortals = new System.Collections.Generic.HashSet<int>();
        private System.Collections.Generic.HashSet<int> processedMiniPortals = new System.Collections.Generic.HashSet<int>();
        // Speed portals processed set: avoid re-applying speed change while portal remains near interaction line
        private System.Collections.Generic.HashSet<int> processedSpeedPortals = new System.Collections.Generic.HashSet<int>();
        // Track orbs that have been activated so they only fire once
        private System.Collections.Generic.HashSet<int> processedOrbs = new System.Collections.Generic.HashSet<int>();
        // Orb buffer: true when the player has pressed/held X in-air and is eligible
        // to activate orbs. This is cleared on ground, when X is released, when
        // the player jumps, or when an orb is activated.
        private bool orbBufferActive = false;
        // Prevent multiple orb activations from a single UI press: set when an orb
        // was activated in response to the current pressed state and cleared when
        // X is released or player lands.
#pragma warning disable CS0414
        private bool orbActivationConsumedThisPress = false;
#pragma warning restore CS0414
        // When a hold-based activation consumes the held X, set this so further
        // hold-based activations are suppressed until X is released and pressed again.
#pragma warning disable CS0414
        private bool orbHoldConsumed = false;
#pragma warning restore CS0414
        // True when a hold-based activation consumed the currently-held X and
        // the key is still down; used to prevent re-priming from sustained
        // hardware-held state until an explicit release occurs.
        private bool orbHoldConsumedKeyStillDown = false;
        // When true, suppress all orb-buffer priming and fresh-press activations
        // until an explicit KeyUp is observed. Set when a hold-based activation
        // consumes the currently-held X so further activations require release.
        private bool orbHoldSuppressing = false;
        // True when an orb or pad was activated on THIS frame (set by sprite collision, checked by physics)
        // Prevents bounce from occurring while an orb/pad is active
        private bool orbhitonthisframe = false;
        private int playerVelY_fixed = 0; // current vertical velocity (fixed-point)
        private bool physicsEnabled = false; // enable physics after first jump (for testing)
        // Landing epsilon in fixed-point (1 pixel)
        private const int LAND_EPS_FIXED = 1 << 8;
        // Whether the player is currently considered on the ground (true when snapped to ground)
    #pragma warning disable CS0414 // assigned but never used - keep for future use
        private bool onGround = true;
        // When landing, keep the player treated as grounded for a few physics frames
        // to avoid jitter between 0 and a small gravity increment.
        private int groundStabilizeCounter = 0;
        // When the cube is snapped to an inverted ceiling, hold grounded state
        // for a few frames to avoid rhythmic velocity oscillation while the
        // head overlaps blocking tiles. This is cleared only when no blocking
        // tile is detected for enough frames.
        private int invertedCeilingHoldCounter = 0;
    #pragma warning restore CS0414

        // Jump-buffer: when the player presses jump slightly before landing, store a small
        // frame window so the jump fires on landing. Timer_Tick sets this under `simLock`.
        private int jumpBufferCounter = 0;
        // Toggle to show player's Y velocity in top-left when Shift+F12 is pressed
        private bool showYVelocityOverlay = false;
        private System.Windows.Controls.TextBlock? yVelTextBlock = null;
        // Simulation time scale (1.0 = normal). Adjusting this slows/speeds the simulation
        // in even 10% increments when the user presses +/-.
        private double simTimeScale = 1.0;
        private const int JUMP_BUFFER_FRAMES = 6; // ~100ms @60Hz
        // Flag to enable refactored collision/physics system
        private bool useRefactoredPhysics = true;
        // Ball mode buffer: allow buffering an X press for ball gravity switch
        // Ball toggle tracking:
        // `ballToggleRequested` is set by UI KeyDown when X is pressed; it is consumed
        // by the physics code when landing or ball flip logic.
        private int ballToggleRequested = 0;
        private const int BALL_BUFFER_FRAMES = 6;
        private bool ballGoingDown = true; // true = downwards, false = upwards
        
        // Additional game mode state variables
        private bool[] ballSwitched = new bool[2];
        private int ballFlipCooldown = 0;
        private bool ballWasGroundedBeforeFlip = false;
        private bool ufoOrbed = false;
        private bool orbed = false; // Prevents jumps/teleports until X released (spider orbs/pads, teleport portals, S blocks, J blocks)
        private bool blackOrbed = false; // Spider black orb hold mechanic
        private int dashing = 0; // 0=not dashing, 1=horizontal, 2=45deg up, 3=45deg down, 4=upward, 5=downward
        private bool hblocked = false; // H block - headbonk block: causes instant ceiling ejection instead of passthrough
        private bool jblocked = false; // J block - requires press instead of hold for next jump
        private bool dblocked = false; // D block - allows wave to walk on surfaces instead of going through/colliding/dying
        private bool fblocked = false; // F block - forces press-to-jump in cube mode and flips gravity on ceiling/floor hit
        private int[] ninjajumps = new int[2] { 3, 3 };
        private int[] robotJumpTime = new int[2];
#pragma warning disable CS0414
        private int[] robotJumpFrame = new int[2];
        private int[] chargepower = new int[2];  // Football charge accumulation
#pragma warning restore CS0414
        private bool robotJumpPressed = false;
        private int ninjaJumps = 3;
        private bool ninjaJumpedThisFrame = false;
#pragma warning disable CS0414
        private bool swingSwitched = false;
#pragma warning restore CS0414

        // Update the player image based on `currentGameMode`.
        private void UpdatePlayerImageForMode()
        {
            try
            {
                if (playerImage == null) return;
                string exeDir = AppDomain.CurrentDomain.BaseDirectory ?? ".";
                string choice = "cube.png";
                
                // Use mini images if in mini mode
                if (miniMode && currentGameMode == 0) choice = "cube-mini.png";
                else if (miniMode && currentGameMode == 1) choice = "ship-mini.png";
                else if (miniMode && currentGameMode == 2) choice = "ball-mini.png";
                else if (miniMode && currentGameMode == 3) choice = "ufo-mini.png";
                else if (miniMode && currentGameMode == 4) choice = "robot-mini.png";
                else if (miniMode && currentGameMode == 5) choice = "spider-mini.png";
                else if (miniMode && currentGameMode == 6) choice = "wave-mini.png";
                else if (miniMode && currentGameMode == 7) choice = "swingcopter-mini.png";
                else if (miniMode && currentGameMode == 8) choice = "ninja-mini.png";
                else if (miniMode && currentGameMode == 9) choice = "pogo-mini.png";
                else if (miniMode && currentGameMode == 10) choice = "snake-mini.png";
                else if (miniMode && currentGameMode == 11) choice = "football-mini.png";
                else if (currentGameMode == 1) choice = "ship.png";
                else if (currentGameMode == 2) choice = "ball.png";
                else if (currentGameMode == 3) choice = "ufo.png";
                else if (currentGameMode == 4) choice = "robot.png";
                else if (currentGameMode == 5) choice = "spider.png";
                else if (currentGameMode == 6) choice = "wave.png";
                else if (currentGameMode == 7) choice = "swingcopter.png";
                else if (currentGameMode == 8) choice = "ninja.png";
                else if (currentGameMode == 9) choice = "pogo.png";
                else if (currentGameMode == 10) choice = "snake.png";
                else if (currentGameMode == 11) choice = "football.png";

                BitmapSource? bi = null;

                // 1) Prefer copy in output directory
                try
                {
                    string candidateOut = System.IO.Path.Combine(exeDir, choice);
                    if (System.IO.File.Exists(candidateOut))
                    {
                        var tmp = new BitmapImage();
                        tmp.BeginInit();
                        tmp.UriSource = new Uri(candidateOut);
                        tmp.CacheOption = BitmapCacheOption.OnLoad;
                        tmp.EndInit();
                        tmp.Freeze();
                        bi = tmp;
                    }
                }
                catch { bi = null; }

                // 2) Fallback: repo root relative (four levels up) for developer tree
                if (bi == null)
                {
                    try
                    {
                        string candidate = System.IO.Path.GetFullPath(System.IO.Path.Combine(exeDir, "..\\..\\..\\..\\" + choice));
                        if (System.IO.File.Exists(candidate))
                        {
                            var b2 = new BitmapImage();
                            b2.BeginInit();
                            b2.UriSource = new Uri(candidate);
                            b2.CacheOption = BitmapCacheOption.OnLoad;
                            b2.EndInit();
                            b2.Freeze();
                            bi = b2;
                        }
                    }
                    catch { }
                }

                // 3) Final fallback: embedded resource in the assembly
                if (bi == null)
                {
                    try
                    {
                        var asm = System.Reflection.Assembly.GetExecutingAssembly();
                        var names = asm.GetManifestResourceNames();
                        var found = names.FirstOrDefault(n => n.EndsWith(choice, StringComparison.OrdinalIgnoreCase));
                        if (!string.IsNullOrEmpty(found))
                        {
                            using (var s = asm.GetManifestResourceStream(found))
                            {
                                if (s != null)
                                {
                                    var b3 = new BitmapImage();
                                    b3.BeginInit();
                                    b3.CacheOption = BitmapCacheOption.OnLoad;
                                    b3.StreamSource = s;
                                    b3.EndInit();
                                    b3.Freeze();
                                    bi = App.EnsureUnfrozenForRender(b3) ?? b3;
                                }
                            }
                        }
                    }
                    catch { }
                }

                if (bi != null)
                {
                    playerImage.Source = App.EnsureUnfrozenForRender(bi) ?? bi;
                    playerImage.Width = bi.PixelWidth;
                    playerImage.Height = bi.PixelHeight;
                    playerImage.Visibility = Visibility.Visible;
                    if (playerRect != null) playerRect.Visibility = Visibility.Collapsed;
                    
                    // For mini mode, use the actual hitbox size (8x8) not the sprite image size
                    // Mini sprite images may be 16x16 with the icon in one quadrant
                    if (miniMode)
                    {
                        playerVisualWidth = 8;
                        playerVisualHeight = 8;
                    }
                    else
                    {
                        playerVisualWidth = (int)Math.Ceiling(playerImage.Width);
                        playerVisualHeight = (int)Math.Ceiling(playerImage.Height);
                    }
                    
                    try
                    {
                        // Flip all game mode icons vertically when gravity is reversed
                        if (gravityReversed)
                        {
                            playerImage.RenderTransformOrigin = new Point(0.5, 0.5);
                            playerImage.RenderTransform = new ScaleTransform(1, -1);
                        }
                        else
                        {
                            // Ensure no transform remains when gravity is normal
                            playerImage.RenderTransform = Transform.Identity;
                        }
                    }
                    catch { }
                    return;
                }

                // Could not load image: fall back to magenta rectangle
                if (playerRect == null)
                {
                    playerRect = new System.Windows.Shapes.Rectangle { Width = TILE, Height = TILE, Fill = new SolidColorBrush(Colors.Magenta) };
                    System.Windows.Controls.Canvas.SetZIndex(playerRect, 1000);
                    RenderCanvas.Children.Add(playerRect);
                }
                playerRect.Visibility = Visibility.Visible;
                if (playerImage != null) playerImage.Visibility = Visibility.Collapsed;
                playerVisualWidth = (int)Math.Ceiling(playerRect.Width);
                playerVisualHeight = (int)Math.Ceiling(playerRect.Height);
            }
            catch { }
        }

        private void UpdatePlayerVisualSizeForMode()
        {
            try
            {
                // Update visual size based on current player image/rect
                if (playerImage != null && playerImage.Source != null && playerImage.Visibility == Visibility.Visible)
                {
                    playerVisualWidth = (int)Math.Ceiling(playerImage.Width);
                    playerVisualHeight = (int)Math.Ceiling(playerImage.Height);
                }
                else if (playerRect != null && playerRect.Visibility == Visibility.Visible)
                {
                    playerVisualWidth = (int)Math.Ceiling(playerRect.Width);
                    playerVisualHeight = (int)Math.Ceiling(playerRect.Height);
                }
            }
            catch { }
        }

        private void UpdatePlayerIconFlip()
        {
            try
            {
                // Update checkbox to reflect current gravity state
                if (InvertedCheckBox != null)
                {
                    InvertedCheckBox.IsChecked = gravityReversed;
                }
                
                if (playerImage != null && playerImage.Source != null)
                {
                    if (gravityReversed)
                    {
                        playerImage.RenderTransformOrigin = new Point(0.5, 0.5);
                        playerImage.RenderTransform = new ScaleTransform(1, -1);
                    }
                    else
                    {
                        playerImage.RenderTransform = Transform.Identity;
                    }
                }
            }
            catch { }
        }

        // P/Invoke to check key state asynchronously from background threads
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
        private static bool IsXDownAsync() { 
            // Check X (0x58), UP (0x26), or SPACE (0x20)
            return (GetAsyncKeyState(0x58) & 0x8000) != 0 || 
                   (GetAsyncKeyState(0x26) & 0x8000) != 0 || 
                   (GetAsyncKeyState(0x20) & 0x8000) != 0; 
        }

        // Mapping from speed-portal sprite id -> speed value
        private readonly System.Collections.Generic.Dictionary<int, int> speedPortalMap = new System.Collections.Generic.Dictionary<int, int>
        {
            { 0x14, CUBE_SPEED_X05 },
            { 0x15, CUBE_SPEED_X1  },
            { 0x16, CUBE_SPEED_X2  },
            { 0x20, CUBE_SPEED_X3  },
            { 0x21, CUBE_SPEED_X4  },
            { 0x6D, CUBE_SPEED_SLOW } // 0.1x special speed
        };

        // Calculate music time (in seconds) to reach a given X position, accounting for speed portals
        private double CalculateMusicTimeToPosition(int targetX_px)
        {
            try
            {
                // Use player center position to match interaction line behavior
                int targetCenter_px = targetX_px + (playerVisualWidth / 2);
                
                // Default to 1x speed
                int currentSpeed_fixed = CUBE_SPEED_X1;
                int lastX_px = 0;
                double totalTime = 0.0;
                
                // Get all speed portals sorted by X position
                var speedPortals = new System.Collections.Generic.List<(int x, int speed)>();
                
                if (sprites != null && spriteAnchors != null)
                {
                    for (int idx = 0; idx < sprites.Length; idx++)
                    {
                        int sid = sprites[idx];
                        if (sid == -1) continue;
                        if (!speedPortalMap.ContainsKey(sid)) continue;
                        
                        // Get anchor position (center of sprite)
                        int anchorTileX = spriteAnchors.TryGetValue(idx, out var a) ? a.anchorTileX : idx % mapWidth;
                        int anchorX_px = anchorTileX * TILE + (TILE / 2);
                        
                        // Only consider portals before target center
                        if (anchorX_px <= targetCenter_px)
                        {
                            speedPortals.Add((anchorX_px, speedPortalMap[sid]));
                        }
                    }
                }
                
                // Sort by X position
                speedPortals.Sort((a, b) => a.x.CompareTo(b.x));
                
                // Calculate time for each segment
                foreach (var portal in speedPortals)
                {
                    int segmentDistance_px = portal.x - lastX_px;
                    if (segmentDistance_px > 0)
                    {
                        // Speed is in fixed-point (pixels per frame * 256)
                        // Convert to pixels per second: (speed_fixed / 256) * 60 fps
                        double speed_px_per_second = (currentSpeed_fixed / 256.0) * 60.0;
                        if (speed_px_per_second > 0)
                        {
                            totalTime += segmentDistance_px / speed_px_per_second;
                        }
                    }
                    
                    // Update speed for next segment
                    currentSpeed_fixed = portal.speed;
                    lastX_px = portal.x;
                }
                
                // Add final segment from last portal to target
                int finalDistance_px = targetCenter_px - lastX_px;
                if (finalDistance_px > 0)
                {
                    double speed_px_per_second = (currentSpeed_fixed / 256.0) * 60.0;
                    if (speed_px_per_second > 0)
                    {
                        totalTime += finalDistance_px / speed_px_per_second;
                    }
                }
                
                return totalTime;
            }
            catch
            {
                return 0.0;
            }
        }

        private void ApplyColorTriggersUpToPosition(int targetX_px)
        {
            try
            {
                if (sprites == null || spriteAnchors == null) return;
                
                int? lastBgIdx = null, lastTileIdx = null, lastGroundIdx = null;
                int? lastBgSid = null, lastTileSid = null, lastGroundSid = null;
                
                // Find the last trigger of each type before target position
                for (int idx = 0; idx < sprites.Length; idx++)
                {
                    int sid = sprites[idx];
                    if (sid == -1) continue;
                    
                    // Get anchor position
                    int anchorTileX = spriteAnchors.TryGetValue(idx, out var a) ? a.anchorTileX : idx % mapWidth;
                    int anchorX_px = anchorTileX * TILE + (TILE / 2);
                    
                    // Only consider triggers before target
                    if (anchorX_px <= targetX_px)
                    {
                        if (IsBackgroundTrigger(sid))
                        {
                            lastBgIdx = idx;
                            lastBgSid = sid;
                        }
                        else if (IsTileTrigger(sid))
                        {
                            lastTileIdx = idx;
                            lastTileSid = sid;
                        }
                        else if (IsGroundTrigger(sid))
                        {
                            lastGroundIdx = idx;
                            lastGroundSid = sid;
                        }
                    }
                }
                
                // Apply the triggers
                if (lastBgIdx.HasValue && lastBgSid.HasValue)
                {
                    var c = ColorFromTrigger(lastBgSid.Value);
                    backgroundTint = c;
                    backgroundForceSolidBlack = (lastBgSid.Value == 0x8F);
                    processedColorTriggers.Add(lastBgIdx.Value);
                }
                
                if (lastTileIdx.HasValue && lastTileSid.HasValue)
                {
                    var c = ColorFromTrigger(lastTileSid.Value);
                    tileTint = c;
                    processedColorTriggers.Add(lastTileIdx.Value);
                }
                
                if (lastGroundIdx.HasValue && lastGroundSid.HasValue)
                {
                    var c = ColorFromTrigger(lastGroundSid.Value);
                    if (lastGroundSid.Value == 0xCF)
                    {
                        groundTint = Color.FromArgb(255, 0, 0, 0);
                    }
                    else
                    {
                        groundTint = c;
                    }
                    processedColorTriggers.Add(lastGroundIdx.Value);
                }
            }
            catch { }
        }

        // Scan for last gamemode/mini/gravity/speed portals before target position and apply them
        private void ApplyPortalStatesUpToPosition(int targetX_px)
        {
            try
            {
                if (sprites == null || spriteAnchors == null) return;
                
                int? lastGamemodeIdx = null, lastGamemodeSid = null, lastGamemodeX = null;
                int? lastMiniIdx = null, lastMiniSid = null, lastMiniX = null;
                int? lastGravityIdx = null, lastGravitySid = null, lastGravityX = null;
                int? lastSpeedIdx = null, lastSpeedSid = null, lastSpeedX = null;
                int? lastBluePadIdx = null, lastBluePadSid = null, lastBluePadX = null;
                
                // Find the last portal of each type before target position (by X coordinate, not array order)
                for (int idx = 0; idx < sprites.Length; idx++)
                {
                    int sid = sprites[idx];
                    if (sid == -1) continue;
                    
                    // Get anchor position
                    int anchorTileX = spriteAnchors.TryGetValue(idx, out var a) ? a.anchorTileX : idx % mapWidth;
                    int anchorX_px = anchorTileX * TILE + (TILE / 2);
                    
                    // Only consider portals before target
                    if (anchorX_px <= targetX_px)
                    {
                        // Gamemode portals: 0x00=Cube, 0x01=Ship, 0x02=Ball, 0x03=UFO, 0x04=Robot, 0x17=Spider, 0x24=Wave, 0x4B=Swing, 0x58=Ninja, 0x6A=Pogo, 0x6B=Snake, 0x6C=Football
                        if (sid == 0x00 || sid == 0x01 || sid == 0x02 || sid == 0x03 || sid == 0x04 || sid == 0x17 || sid == 0x24 || sid == 0x4B || sid == 0x58 || sid == 0x6A || sid == 0x6B || sid == 0x6C)
                        {
                            if (!lastGamemodeX.HasValue || anchorX_px > lastGamemodeX.Value)
                            {
                                lastGamemodeIdx = idx;
                                lastGamemodeSid = sid;
                                lastGamemodeX = anchorX_px;
                            }
                        }
                        // Mini/Growth portals: 0x18=Mini, 0x19=Growth
                        else if (sid == 0x18 || sid == 0x19)
                        {
                            if (!lastMiniX.HasValue || anchorX_px > lastMiniX.Value)
                            {
                                lastMiniIdx = idx;
                                lastMiniSid = sid;
                                lastMiniX = anchorX_px;
                            }
                        }
                        // Gravity portals: 0x08=Down, 0x09=Up, 0x10-0x13=Directional, 0xFB=Down, 0xFC=Up
                        else if (sid == 0x08 || sid == 0x09 || sid == 0x10 || sid == 0x11 || sid == 0x12 || sid == 0x13 || sid == 0xFB || sid == 0xFC)
                        {
                            if (!lastGravityX.HasValue || anchorX_px > lastGravityX.Value)
                            {
                                lastGravityIdx = idx;
                                lastGravitySid = sid;
                                lastGravityX = anchorX_px;
                            }
                        }
                        // Blue pads: 0x0D=Bottom (flip to up), 0x0E=Top (flip to down), 0xFD=Bottom multi, 0xFE=Top multi
                        else if (sid == 0x0D || sid == 0x0E || sid == 0xFD || sid == 0xFE)
                        {
                            if (!lastBluePadX.HasValue || anchorX_px > lastBluePadX.Value)
                            {
                                lastBluePadIdx = idx;
                                lastBluePadSid = sid;
                                lastBluePadX = anchorX_px;
                            }
                        }
                        // Speed portals: 0x14=0.5x, 0x15=1x, 0x16=2x, 0x20=3x, 0x21=4x
                        else if (speedPortalMap.ContainsKey(sid))
                        {
                            if (!lastSpeedX.HasValue || anchorX_px > lastSpeedX.Value)
                            {
                                lastSpeedIdx = idx;
                                lastSpeedSid = sid;
                                lastSpeedX = anchorX_px;
                            }
                        }
                    }
                }
                
                // Apply gamemode portal
                if (lastGamemodeIdx.HasValue && lastGamemodeSid.HasValue)
                {
                    int newMode = lastGamemodeSid.Value switch {
                        0x00 => 0, // Cube
                        0x01 => 1, // Ship
                        0x02 => 2, // Ball
                        0x03 => 3, // UFO
                        0x04 => 4, // Robot
                        0x17 => 5, // Spider
                        0x24 => 6, // Wave
                        0x4B => 7, // Swing
                        0x58 => 8, // Ninja
                        _ => 0
                    };
                    currentGameMode = newMode;
                    try { UpdatePlayerImageForMode(); } catch { }
                    try { UpdatePlayerVisualSizeForMode(); } catch { }
                    try { UpdateGameModeDisplay(); } catch { }
                    // Don't add to processed set - let collision detection handle it during gameplay
                }
                
                // Apply mini/growth portal
                if (lastMiniIdx.HasValue && lastMiniSid.HasValue)
                {
                    bool newMini = (lastMiniSid.Value == 0x18);
                    miniMode = newMini;
                    currplayer_mini = (byte)(newMini ? 1 : 0);
                    try { Dispatcher.BeginInvoke(new Action(() => { if (MiniCheckBox != null) MiniCheckBox.IsChecked = miniMode; })); } catch { }
                    try { UpdatePlayerImageForMode(); } catch { }
                    try { UpdatePlayerVisualSizeForMode(); } catch { }
                    // Don't add to processed set - let collision detection handle it during gameplay
                }
                
                // Apply gravity portal
                if (lastGravityIdx.HasValue && lastGravitySid.HasValue)
                {
                    int sid = lastGravitySid.Value;
                    // 0x09, 0x12, 0x13, 0xFB = Reverse (up), 0x08, 0x10, 0x11, 0xFC = Normal (down)
                    bool newGravity = (sid == 0x09 || sid == 0x12 || sid == 0x13 || sid == 0xFB);
                    gravityReversed = newGravity;
                    gravityFlipped = newGravity;
                    currplayer_gravity = (byte)(newGravity ? 0xFF : 0x00);
                    try { UpdatePlayerIconFlip(); } catch { }
                    // Don't add to processed set - let collision detection handle it during gameplay
                }
                
                // Apply blue pad (blue pads flip gravity when landed on)
                // Blue pads take priority over gravity portals if they're at a later X position
                if (lastBluePadIdx.HasValue && lastBluePadSid.HasValue)
                {
                    int sid = lastBluePadSid.Value;
                    // 0x0D, 0xFD = Bottom blue pad (flip to up/inverted), 0x0E, 0xFE = Top blue pad (flip to down/normal)
                    bool isBottomPad = (sid == 0x0D || sid == 0xFD);
                    bool newGravity = isBottomPad; // Bottom pad = inverted gravity
                    gravityReversed = newGravity;
                    gravityFlipped = newGravity;
                    currplayer_gravity = (byte)(newGravity ? 0xFF : 0x00);
                    try { UpdatePlayerIconFlip(); } catch { }
                    try { Dispatcher.BeginInvoke(new Action(() => { if (InvertedCheckBox != null) InvertedCheckBox.IsChecked = gravityReversed; })); } catch { }
                    // Don't add to processed set - let collision detection handle it during gameplay
                }
                
                // Apply speed portal
                if (lastSpeedIdx.HasValue && lastSpeedSid.HasValue)
                {
                    if (speedPortalMap.TryGetValue(lastSpeedSid.Value, out int speedFixed))
                    {
                        playerVelX_fixed = speedFixed;
                        // Update speed UI index to match
                        int speedIndex = lastSpeedSid.Value switch {
                            0x14 => 0, // 0.5x
                            0x15 => 1, // 1x
                            0x16 => 2, // 2x
                            0x20 => 3, // 3x
                            0x21 => 4, // 4x
                            _ => 1
                        };
                        speed = speedIndex;
                        try { UpdateSpeedDisplay(); } catch { }
                    }
                    // Don't add to processed set - let collision detection handle it during gameplay
                }
            }
            catch { }
        }

        private readonly System.Windows.Threading.DispatcherTimer timer;
        // Dedicated background simulation timer to keep simulation at a steady 60Hz
        private System.Threading.Timer? simTimer;
        private readonly object simLock = new object();
        // High-resolution timing for fixed-step simulation at 60Hz
        private System.Diagnostics.Stopwatch simStopwatch = new System.Diagnostics.Stopwatch();
        private double simLastMs = 0.0;
        private double simAccumulatedMs = 0.0;
        private const double SIM_STEP_MS = 1000.0 / 60.0; // 16.666... ms per fixed-step
        // UI animation accumulator for cases where numeric sim is paused or not running
        private double uiAnimLastMs = 0.0;
        private double uiAnimAccumulatedMs = 0.0;
        // Pending color trigger info populated by simulation thread and applied on UI thread
        // Use -1 to indicate 'none' rather than nullable/volatile types.
        private int pendingBgIdx = -1;
        private int pendingBgSid = -1;
        private int pendingTileIdx = -1;
        private int pendingTileSid = -1;
        private int pendingGroundIdx = -1;
        private int pendingGroundSid = -1;
        private bool pendingTintChange = false;
        private bool pendingTintChangeIsStartup = false;
        // Set to true when we applied starting-per-level tints so subsequent image regeneration
        // avoids applying object/outline tints at startup.
        private bool startupTintApplied = false;
        private System.Diagnostics.Stopwatch renderStopwatch = new System.Diagnostics.Stopwatch();

        private int animationFrame = 0;
        // If the simulator has an owner MainWindow, prefer its animation frame so
        // two-frame decorations (dash-orbs, spider-orbs, deco) pulse in exact sync.
        private int GetEditorAnimationFrameValue()
        {
            try
            {
                // Only use the editor's animation frame when the editor's preview-mode animations are active.
                if (this.Owner is MainWindow mw && mw.EditorPreviewMode) return mw.EditorAnimationFrame;
            }
            catch { }
            return animationFrame;
        }
        // Sprite IDs that should animate at half speed (coins, pads, orbs)
        private static readonly System.Collections.Generic.HashSet<int> slowAnimatedSpriteIds = new System.Collections.Generic.HashSet<int> { 0x07, 0x1A, 0x1B, 0x6E };
        private System.Collections.Generic.Dictionary<int, int> spriteFrameOffsets = new System.Collections.Generic.Dictionary<int, int>();
        private Random spriteAnimationRandom = new Random();

        // Tile-layer cache and sprite pooling for performance
        private RenderTargetBitmap? tileLayerCache = null;
        private int cachedStartTileX = int.MinValue;
        private int cachedStartTileY = int.MinValue;
        private System.Windows.Controls.Image? tileLayerImage = null;
        private System.Windows.Shapes.Rectangle? bgRectPersistent = null;
        private System.Windows.Shapes.Rectangle? groundRectPersistent = null;
        private System.Collections.Generic.List<System.Windows.Controls.Image> spritePool = new System.Collections.Generic.List<System.Windows.Controls.Image>();
        private int spritesInUse = 0;
        private bool lastCacheHadAnimatedTiles = false;
        private int lastCacheAnimationFrame = -1;

        // Note: spider-orbs (0x54,0x55) are NOT decorations and should not receive player tint.
        private readonly System.Collections.Generic.HashSet<int> decorationSpriteIds = new System.Collections.Generic.HashSet<int> { 0x36, 0x32, 0x33, 0x34, 0x35, 0x37, 0x2C, 0x3C, 0x2D, 0x3D, 0x2E, 0x2F, 0x30, 0x31, 0x38, 0x39, 0x3E, 0x3F, 0x2B, 0x3B, 0x2A, 0x3A, 0x49, 0x4A };

        // Coin-like sprites should composite over exact tile pixels, but they must never
        // receive the player's tint. Keep a small set so we can exclude them from tinting.
        private readonly System.Collections.Generic.HashSet<int> nonPlayerTintSpriteIds = new System.Collections.Generic.HashSet<int> { 0x07, 0x1A, 0x1B, 0x6E };

        // Cache tinted decoration sprites keyed by (spriteId<<32)|ARGB
        private readonly System.Collections.Generic.Dictionary<long, ImageSource?> tintedSpriteCache = new System.Collections.Generic.Dictionary<long, ImageSource?>();

        // Cache to remember if a BitmapSource appears 'top-heavy' (non-transparent pixels concentrated near top)
        private readonly System.Collections.Generic.Dictionary<int, bool> imageTopHeavyCache = new System.Collections.Generic.Dictionary<int, bool>();
        // Cache composite of sprite over background/tile area: key = "srcHash:ARGB:destX:destY"
        private readonly System.Collections.Generic.Dictionary<string, ImageSource?> spriteBackgroundCompositeCache = new System.Collections.Generic.Dictionary<string, ImageSource?>();

        // Cache for ground-tinted tile images keyed by (tileIndex<<32)|ARGB
        private readonly System.Collections.Generic.Dictionary<long, ImageSource?> groundTintedTileCache = new System.Collections.Generic.Dictionary<long, ImageSource?>();

        // Cache for black-masked tile variants used when backgroundForceSolidBlack is true.
        private readonly System.Collections.Generic.Dictionary<long, ImageSource?> blackMaskedTileCache = new System.Collections.Generic.Dictionary<long, ImageSource?>();

        // Track color-trigger anchors that have already been processed (so we don't resample every frame)
        private System.Collections.Generic.HashSet<int> processedColorTriggers = new System.Collections.Generic.HashSet<int>();

        // Cache toned image arrays to avoid expensive regeneration on every color trigger
        private ImageSource?[]? cachedTileTonedImages = null;
        private ImageSource?[]? cachedParallaxTonedImages = null;
        private ImageSource?[]? cachedGroundTonedImages = null;
        private Color cachedBackgroundTint = Color.FromArgb(0, 0, 0, 0);
        private Color cachedTileTint = Color.FromArgb(0, 0, 0, 0);
        private Color cachedGroundTint = Color.FromArgb(0, 0, 0, 0);
        private bool cachedBackgroundForceSolidBlack = false;

        // Lightweight one-time debug logging sets to avoid spamming output repeatedly
        private System.Collections.Generic.HashSet<int> decoLogged = new System.Collections.Generic.HashSet<int>();
        private System.Collections.Generic.HashSet<int> triggerLogged = new System.Collections.Generic.HashSet<int>();
        private int tileSelectionLogCount = 0;
        private const int TILE_SELECTION_LOG_LIMIT = 64;
        // Track last selected decoration frame so we can log when it actually changes
        private System.Collections.Generic.Dictionary<int, int> decoLastSelectedFrame = new System.Collections.Generic.Dictionary<int, int>();
        private bool enableSimulatorDebugLogging = false; // set true to capture helpful messages during diagnosis

        // When true, print per-simulation-step diagnostics about the player's feet
        // including tile indices, animated remaps and resolved collision categories.
        // Disabled by default to avoid writing runtime logs.
        private bool runtimePerFrameLog = false;

        // Internal one-time simulator debug file (used only for local diagnosis when requested)
        // Use a deterministic, repo-root path so it's easy to find when running from VS/`dotnet run`.
        private readonly string simDebugFilePath = @"C:\Editor Test\native-windows\sim_debug.txt";
        private bool simDebugLoggedFirstFrame = false;

        // (Note: actual AppendSimDebug implementation exists earlier and writes to temp.)

        // Helper to append a temp log when `enableSimulatorDebugLogging` is enabled.
        // Disabled: do not write temporary logs to disk.
        private void WriteTempLog(string message)
        {
            // No-op
            return;
        }

        // Lightweight ball-mode event logging for diagnosis (disabled)
        private void LogBallEvent(string evt)
        {
            // No-op: ball event logging disabled.
            return;
        }

        // Start background simulation timer.
        public void StartSimulation()
        {
            try
            {
                if (simTimer != null) return; // Prevent starting multiple timers

                // Start high-resolution stopwatch and use an accumulator to run fixed 60Hz steps.
                simStopwatch.Restart();
                simLastMs = simStopwatch.Elapsed.TotalMilliseconds;
                simAccumulatedMs = 0.0;
                // Run timer at a small interval and accumulate elapsed time to drive fixed steps.
                simTimer = new System.Threading.Timer(_ => { try { TimerSimulationLoop(); } catch { } }, null, 0, 10);

                // NOTE: Do NOT reset playerY_fixed here - RestartButton_Click and constructor handle position initialization

                // Ensure the sim debug file exists and print its resolved path only when
                // `enableSimulatorDebugLogging` is set. Normal runs should not create the file.
                try
                {
                    if (enableSimulatorDebugLogging)
                    {
                        var header = DateTime.UtcNow.ToString("o") + " SIM_LOG_START\n";
                        System.Console.WriteLine("SIM_DEBUG_PATH: " + simDebugFilePath);
                        System.IO.File.AppendAllText(simDebugFilePath, header);
                        simDebugLoggedFirstFrame = true;
                    }
                }
                catch { }
                // When opening the simulator, clear any previous player-path overlay in the editor
                try
                {
                    if (this.Owner is MainWindow mw)
                    {
                        try { mw.ClearPlayerPathOverlay(); } catch { }
                    }
                }
                catch { }
                // Clear any previously recorded path for a fresh run
                try { recordedPlayerPath.Clear(); } catch { }
                // Don't clear processed portals here - RestartButton_Click handles that before ApplyPortalStatesUpToPosition
                // Respect the global Cam Mode option: when Cam Mode is enabled, disable physics
                try
                {
                    camModeActive = (this.Owner is MainWindow mw2) ? MainWindow.Option_CamMode : false;
                    if (camModeActive)
                    {
                        physicsEnabled = false;
                        // Keep jumpedOnce false so Up/Down act purely as camera pans
                        jumpedOnce = false;
                        // Hide settings panel in cam mode to prevent combo boxes from stealing keyboard focus
                        try { if (SettingsPanel != null) SettingsPanel.Visibility = System.Windows.Visibility.Collapsed; } catch { }
                    }
                    else
                    {
                        // Start physics immediately when Cam Mode is OFF
                        physicsEnabled = true;
                        // Prevent Up/Down from being camera-only
                        jumpedOnce = true;
                        // Show settings panel in normal mode
                        try { if (SettingsPanel != null) SettingsPanel.Visibility = System.Windows.Visibility.Visible; } catch { }
                    }
                }
                catch { }
                // Initialize per-simulator overlay flags from global editor options
                try { ShowTileHitboxes = (this.Owner is MainWindow mw3) ? MainWindow.Option_ShowTileHitboxes : false; } catch { }
                try { ShowSpriteHitboxes = (this.Owner is MainWindow mw4) ? MainWindow.Option_ShowSimulatorSpriteHitboxes : false; } catch { }
            }
            catch { }
        }

        // Stop the background simulation gracefully.
        public void StopSimulation()
        {
            try { simTimer?.Dispose(); } catch { }
            simTimer = null; // Clear the timer reference
            try { simStopwatch.Stop(); } catch { }
        }

        // When the simulator window closes, send the recorded player path back to the editor
        protected override void OnClosed(EventArgs e)
        {
            try
            {
                base.OnClosed(e);
                try
                {
                    if (this.Owner is MainWindow mw)
                    {
                        // Send a copy of the recorded path
                        var copy = new System.Collections.Generic.List<(int x, int y)>(recordedPlayerPath);
                        mw.ShowPlayerPathFromSimulator(copy);
                    }
                }
                catch { }
            }
            catch { base.OnClosed(e); }
        }

        // Timer loop invoked on threadpool; accumulates elapsed time and runs fixed-step simulation.
        private void TimerSimulationLoop()
        {
            // Skip if restart is in progress or window is closed
            if (restartInProgress || windowClosed) return;
            
            try
            {
                double now = simStopwatch.Elapsed.TotalMilliseconds;
                double delta = Math.Max(0.0, now - simLastMs);
                // First tick: simLastMs is zero, treat delta as 0 to avoid a large initial jump
                if (simLastMs <= 0.0) delta = 0.0;
                simLastMs = now;
                // Apply time scale to delta - this makes simulation run faster/slower
                simAccumulatedMs += delta * simTimeScale;

                // Run one or more fixed 60Hz steps as needed
                while (simAccumulatedMs >= SIM_STEP_MS)
                {
                    try { SimulateNumericStep(); } catch { }
                    simAccumulatedMs -= SIM_STEP_MS;
                // Record player path at fixed 60Hz (after physics step completes)
                try
                {
                    int worldX = (playerX_fixed >> 8) + (playerVisualWidth / 2);
                    int worldY;
                    if (miniMode)
                    {
                        if (gravityFlipped)
                        {
                            worldY = (playerY_fixed >> 8) + 4;  // Inverted mini: hitbox at Y+0, center at Y+3.5
                        }
                        else
                        {
                            worldY = (playerY_fixed >> 8) + 13; // Normal mini: hitbox at Y+9, center at Y+12.5
                        }
                    }
                    else
                    {
                        worldY = (playerY_fixed >> 8) + 8;  // Normal: center of 15x15 hitbox
                    }
                    recordedPlayerPath.Add((worldX, worldY));
                }
                catch { }
                
                // Automatic camera-follow while physics is active: ensure player stays within vertical thresholds
                try
                {
                    if (physicsEnabled && jumpedOnce)
                    {
                        int playerCenterScreenY_post = (playerY_fixed >> 8) + (playerVisualHeight / 2) - (cameraY_fixed >> 8);
                        int topThreshold_post = 5 * TILE;
                        int bottomThreshold_post = NES_H * TILE - 5 * TILE;

                        if (playerCenterScreenY_post <= topThreshold_post)
                        {
                            int need = topThreshold_post - playerCenterScreenY_post;
                            int camMove = Math.Min(need, (cameraY_fixed >> 8));
                            cameraY_fixed -= (camMove << 8);
                            if (cameraY_fixed < 0) cameraY_fixed = 0;
                        }
                        else if (playerCenterScreenY_post >= bottomThreshold_post)
                        {
                            int need = playerCenterScreenY_post - bottomThreshold_post;
                            int maxCameraY_fixed = Math.Max(0, (mapHeight - NES_H) * TILE) << 8;
                            int camAvail = (maxCameraY_fixed - cameraY_fixed) >> 8;
                            int camMove = Math.Min(need, camAvail);
                            cameraY_fixed += (camMove << 8);
                            if (cameraY_fixed > maxCameraY_fixed) cameraY_fixed = maxCameraY_fixed;
                        }
                    }
                }
                catch { }
                }
            }
            catch { }
        }

        private bool upHeld = false;
        private bool downHeld = false;
        // Has the player performed their first jump? When false, Up/Down act as camera-only.
        private bool jumpedOnce = false;
        // Per-frame input polling state (set on UI thread, consumed by numeric sim)
        private bool prevKeyXDown = false;
        private int keyXPressedCount = 0; // edge-detected press counter (atomic)
        private bool keyXHeld = false;    // current held state
        // Atomic flags to indicate whether a recorded press/hold started while the player
        // was on the ground. These are set by the UI/polling on the press edge and
        // consumed by the numeric thread when it processes pending presses.
        private int keyXPressStartedOnGroundInt = 0; // 0 = false, 1 = true
        private int keyXHeldStartedOnGroundInt = 0; // 0 = false, 1 = true
        // Jump buffer frames (atomic). When >0, a landing will consume this and trigger a jump.
        // tabHeld was used previously; use tabSpeedMultiplier instead.
        // (removed unused field to silence build warning)
        // Pause state controlled by ESC. Start paused so simulator opens paused.
        private bool paused = true;
        // If a death has been triggered by collision, suppress further triggers until reset
        private bool deathTriggered = false;
        // Death location (tile pixel coordinates)
        private int deathTileX = -1;
        private int deathTileY = -1;
        // Mouse hover ellipses for death debugging
        private System.Windows.Shapes.Ellipse? deathPlayerDot = null;
        private System.Windows.Shapes.Ellipse? deathTileDot = null;
        // Debug window instance
        private DebugInfoWindow? debugWindow = null;
        // When true, the simulator is in camera-only mode: Up/Down pan camera only and physics is disabled
        private bool camModeActive = false;
        // Multiplier applied while Tab (or Shift+Tab / Ctrl+Shift+Tab) is held.
        // Default 1 (no extra multiplier). While Tab is down this becomes 2/4/8 per modifiers.
        private int tabSpeedMultiplier = 1;

        // (debug overlay removed)

        public SimulatorWindow(
            int[] tiles,
            int[] sprites,
            int mapWidth,
            int mapHeight,
            ImageSource?[]? tileImages,
            ImageSource?[]? tileTonedImages,
            ImageSource?[]? spriteImages,
            System.Collections.Generic.Dictionary<int, (int offsetX, int offsetY)> spritePixelOffsets,
            System.Collections.Generic.Dictionary<int, (int anchorTileX, int anchorTileY)> spriteAnchors,
            Color backgroundTint,
            Color groundTint,
            Color tileTint,
            Color playerTint,
            bool playerTintEnabled,
            int gridRenderShiftYPx
            , bool forcePreviewMode = true,
            bool hideColorTriggers = false,
            System.Collections.Generic.Dictionary<int, ImageSource?>? previewSpriteMap = null,
            System.Collections.Generic.Dictionary<int, ImageSource?[]>? animationFrames = null,
            ImageSource?[]? sawFrame1TilesTinted = null,
            ImageSource?[]? sawFrame2TilesTinted = null,
            ImageSource?[]? smallSawFrame1TilesTinted = null,
            ImageSource?[]? smallSawFrame2TilesTinted = null,
            ImageSource?[]? largeSawFrame1TilesTinted = null,
            ImageSource?[]? largeSawFrame2TilesTinted = null
            ,
            ImageSource? parallaxBitmap = null,
            ImageSource?[]? parallaxImages = null,
            ImageSource?[]? parallaxTonedImages = null,
            double parallaxX = 1.0,
            double parallaxY = 1.0,
            bool parallaxRepeatX = true,
            bool parallaxRepeatY = true,
            bool hasParallaxLayer = false,
            ImageSource?[]? groundImages = null,
            ImageSource?[]? groundTonedImages = null,
            double groundOffsetY = 0.0,
            bool groundRepeatX = true,
            bool hasGroundLayer = false,
            int groundTileRows = 0,
            int? startingBackgroundColorCode = null,
            int? startingGroundColorCode = null,
            int simulatorScale = 1,
            int? maxFallSpeed = null,
            int startingGameMode = 0
            )
        {
            InitializeComponent();
            
            // Reset simulator speed to 100% on window open (don't carry over from previous session)
            simTimeScale = 1.0;
            
            // Clear ball toggle request on window creation
            Interlocked.Exchange(ref ballToggleRequested, 0);
            
            try { baseWindowTitle = this.Title ?? "Simulator"; } catch { baseWindowTitle = "Simulator"; }
            // Ensure pause overlay reflects initial paused state
            try { PauseOverlay.Visibility = paused ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed; } catch { }
            this.tiles = tiles.ToArray();
            this.sprites = sprites.ToArray();
            this.mapWidth = mapWidth;
            this.mapHeight = mapHeight;
            this.tileImages = tileImages;
            this.tileTonedImages = tileTonedImages;
            this.spriteImages = spriteImages;
            this.spritePixelOffsets = new System.Collections.Generic.Dictionary<int, (int, int)>(spritePixelOffsets);
            this.spriteAnchors = new System.Collections.Generic.Dictionary<int, (int, int)>(spriteAnchors);
            // Store saw frame originals and initial tinted copies
            this.sawFrame1TilesOrig = sawFrame1TilesTinted != null ? (ImageSource[])sawFrame1TilesTinted.Clone() : null;
            this.sawFrame2TilesOrig = sawFrame2TilesTinted != null ? (ImageSource[])sawFrame2TilesTinted.Clone() : null;
            this.smallSawFrame1TilesOrig = smallSawFrame1TilesTinted != null ? (ImageSource[])smallSawFrame1TilesTinted.Clone() : null;
            this.smallSawFrame2TilesOrig = smallSawFrame2TilesTinted != null ? (ImageSource[])smallSawFrame2TilesTinted.Clone() : null;
            this.largeSawFrame1TilesOrig = largeSawFrame1TilesTinted != null ? (ImageSource[])largeSawFrame1TilesTinted.Clone() : null;
            this.largeSawFrame2TilesOrig = largeSawFrame2TilesTinted != null ? (ImageSource[])largeSawFrame2TilesTinted.Clone() : null;
            // Initialize tinted copies based on provided tileTint
            // Saws should be tinted by the background color triggers, not the tile tint.
            if (backgroundTint.A == 255 && backgroundTint.R == 0 && backgroundTint.G == 0 && backgroundTint.B == 0)
            {
                this.sawFrame1TilesTinted = CreateSolidBlackImages(this.sawFrame1TilesOrig);
                this.sawFrame2TilesTinted = CreateSolidBlackImages(this.sawFrame2TilesOrig);
                this.smallSawFrame1TilesTinted = CreateSolidBlackImages(this.smallSawFrame1TilesOrig);
                this.smallSawFrame2TilesTinted = CreateSolidBlackImages(this.smallSawFrame2TilesOrig);
                this.largeSawFrame1TilesTinted = CreateSolidBlackImages(this.largeSawFrame1TilesOrig);
                this.largeSawFrame2TilesTinted = CreateSolidBlackImages(this.largeSawFrame2TilesOrig);
            }
            else
            {
                this.sawFrame1TilesTinted = CreateHslShiftedImages(this.sawFrame1TilesOrig, backgroundTint, tileTint);
                this.sawFrame2TilesTinted = CreateHslShiftedImages(this.sawFrame2TilesOrig, backgroundTint, tileTint);
                this.smallSawFrame1TilesTinted = CreateHslShiftedImages(this.smallSawFrame1TilesOrig, backgroundTint, tileTint);
                this.smallSawFrame2TilesTinted = CreateHslShiftedImages(this.smallSawFrame2TilesOrig, backgroundTint, tileTint);
                this.largeSawFrame1TilesTinted = CreateHslShiftedImages(this.largeSawFrame1TilesOrig, backgroundTint, tileTint);
                this.largeSawFrame2TilesTinted = CreateHslShiftedImages(this.largeSawFrame2TilesOrig, backgroundTint, tileTint);
            }
            // Simulator-specific tweak: shift sprite 0x2B and 0x2C up 8 pixels to match editor preview
            // Initialize starting game mode
            try { currentGameMode = startingGameMode; } catch { currentGameMode = 0; }
            // try { ModeDispatch_ApplyModeState(); } catch { } // REMOVED - fresh port
            try { UpdatePlayerImageForMode(); } catch { }
            try
            {
                const int SPRITE_ID_SHIFT_A = 0x2B;
                const int SPRITE_ID_SHIFT_B = 0x2C;
                if (this.spritePixelOffsets.ContainsKey(SPRITE_ID_SHIFT_A))
                {
                    var prev = this.spritePixelOffsets[SPRITE_ID_SHIFT_A];
                    this.spritePixelOffsets[SPRITE_ID_SHIFT_A] = (prev.Item1, prev.Item2 - 8);
                }
                else
                {
                    this.spritePixelOffsets[SPRITE_ID_SHIFT_A] = (0, -8);
                }

                if (this.spritePixelOffsets.ContainsKey(SPRITE_ID_SHIFT_B))
                {
                    var prev = this.spritePixelOffsets[SPRITE_ID_SHIFT_B];
                    this.spritePixelOffsets[SPRITE_ID_SHIFT_B] = (prev.Item1, prev.Item2 - 8);
                }
                else
                {
                    this.spritePixelOffsets[SPRITE_ID_SHIFT_B] = (0, -8);
                }
            }
            catch { }

            // Configure max fall speed from optional parameter. If the passed value is
            // a small numeric code (e.g. 0x06 or 0x07) convert to fixed-point by
            // shifting left 8; if the value already appears to be a fixed-point
            // value (>= 0x100) use it directly.
            try
            {
                if (maxFallSpeed.HasValue)
                {
                    int v = maxFallSpeed.Value;
                    if (v >= 0x100)
                    {
                        CUBE_MAX_FALLSPEED = v;
                    }
                    else
                    {
                        CUBE_MAX_FALLSPEED = (v << 8);
                    }
                }
                // Initialize effective physics values based on current reversal flag
                UpdateEffectiveGravity();
            }
            catch { }
            this.backgroundTint = backgroundTint;
            this.groundTint = groundTint;
            this.tileTint = tileTint;
            this.playerTint = playerTint;
            this.playerTintEnabled = playerTintEnabled;
            this.gridRenderShiftYPx = gridRenderShiftYPx;
            this.forcePreviewMode = forcePreviewMode;
            this.hideColorTriggers = hideColorTriggers;
            this.previewSpriteMap = previewSpriteMap ?? new System.Collections.Generic.Dictionary<int, ImageSource?>();
            this.animationFrames = animationFrames ?? new System.Collections.Generic.Dictionary<int, ImageSource?[]>();
            // Ensure blue-pad alias IDs 0xFD/0xFE animate the same as canonical 0x0D/0x0E when editor didn't provide aliases
            try
            {
                if (this.animationFrames != null)
                {
                    if (!this.animationFrames.ContainsKey(0xFD) && this.animationFrames.ContainsKey(0x0D))
                    {
                        this.animationFrames[0xFD] = this.animationFrames[0x0D];
                    }
                    if (!this.animationFrames.ContainsKey(0xFE) && this.animationFrames.ContainsKey(0x0E))
                    {
                        this.animationFrames[0xFE] = this.animationFrames[0x0E];
                    }
                }
            }
            catch { }
            this.sawFrame1TilesTinted = sawFrame1TilesTinted;
            this.sawFrame2TilesTinted = sawFrame2TilesTinted;
            this.smallSawFrame1TilesTinted = smallSawFrame1TilesTinted;
            this.smallSawFrame2TilesTinted = smallSawFrame2TilesTinted;
            this.largeSawFrame1TilesTinted = largeSawFrame1TilesTinted;
            this.largeSawFrame2TilesTinted = largeSawFrame2TilesTinted;

            // parallax / ground
            this.parallaxBitmap = parallaxBitmap;
            this.parallaxImages = parallaxImages;
            this.parallaxTonedImages = parallaxTonedImages;
            this.parallaxX = parallaxX;
            this.parallaxY = parallaxY;
            this.parallaxRepeatX = parallaxRepeatX;
            this.parallaxRepeatY = parallaxRepeatY;
            this.hasParallaxLayer = hasParallaxLayer;

            this.groundImages = groundImages;
            this.groundTonedImages = groundTonedImages;
            this.groundOffsetY = groundOffsetY;
            this.groundRepeatX = groundRepeatX;
            this.hasGroundLayer = hasGroundLayer;
            this.groundTileRows = groundTileRows;

            // If caller supplied per-level starting color codes, map them to trigger IDs and
            // apply the resulting tints immediately so the simulator starts with those colors.
            try
            {
                if (startingBackgroundColorCode.HasValue)
                {
                    int sc = startingBackgroundColorCode.Value;
                    int trigger = MapStartingCodeToTrigger(sc, false);
                    // Simulate activation of the background color trigger so the pending-tint
                    // path runs (same as when the player crosses a trigger in-game).
                    try {
                        // Workaround: queue the exact two-trigger runtime sequence on the UI Dispatcher
                        // instead of applying synchronously. This better reproduces the timing of
                        // an in-game crossing (first a different trigger, then the real one).
                        int firstTrigger = trigger + 1;
                        if ((trigger & 0x0F) == 0x0C) firstTrigger = trigger + 4;

                        // Queue first trigger at normal priority
                        try
                        {
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                try
                                {
                                    pendingBgIdx = 0; pendingBgSid = firstTrigger; pendingTintChange = true; pendingTintChangeIsStartup = false;
                                    ApplyPendingTints();
                                }
                                catch { }
                            }), System.Windows.Threading.DispatcherPriority.Normal);
                        }
                        catch { }

                        // Queue the real trigger at ApplicationIdle so it runs after other UI work
                        try
                        {
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                try
                                {
                                    pendingBgIdx = 0; pendingBgSid = trigger; pendingTintChange = true; pendingTintChangeIsStartup = false;
                                    ApplyPendingTints();
                                }
                                catch { }
                            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                        }
                        catch { }
                    } catch { }
                }
            }
            catch { }

            // Diagnostic: report whether spider orb animation frames were provided by the editor
            // (no-op) removed diagnostic logging

            try
            {
                if (startingGroundColorCode.HasValue)
                {
                    int sc = startingGroundColorCode.Value;
                    int trigger = MapStartingCodeToTrigger(sc, true);
                    // Simulate activation of the ground color trigger (special-casing is handled
                    // by ApplyPendingTints when it sees sid==0xCF). Use pending fields so the
                    // UI-thread path applies tints and regenerates toned images.
                    try { pendingGroundIdx = 0; pendingGroundSid = trigger; pendingTintChange = true; pendingTintChangeIsStartup = true; } catch { }
                }
            }
            catch { }

            // Starting tint values have been set above; we'll regenerate toned images later
            // after assets/parallax/ground images are loaded and persistent UI elements exist.

            // If we scheduled pending tint changes from starting codes, apply them now on the UI thread
            // so the toned images and caches are generated immediately.
            try
            {
                if (pendingTintChange)
                {
                    // We're on the UI thread in the constructor; apply immediately.
                    try { ApplyPendingTints(); } catch { }
                    try { EnsureInitialRender(); } catch { }
                    // Keep the simulator paused (PauseOverlay handled earlier via `paused`).
                }
            }
            catch { }

            // Robust fallback: if editor didn't provide parallax/ground data, try to load embedded project assets
            // or synthesize a transparent tile so the simulator always has something to render as a background.
            try
            {
                // (leave loading to the conditional fallbacks below so we don't overwrite editor-provided images)
                if ((!this.hasParallaxLayer) || this.parallaxImages == null || this.parallaxImages.Length == 0)
                {
                    var asm = System.Reflection.Assembly.GetExecutingAssembly();
                    var names = asm.GetManifestResourceNames();
                    var fullName = names.FirstOrDefault(r => r.IndexOf("Assets.parallax.bmp", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (fullName == null) fullName = names.FirstOrDefault(r => r.IndexOf("parallax", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (!string.IsNullOrEmpty(fullName))
                    {
                        using (var s = asm.GetManifestResourceStream(fullName))
                        {
                            if (s != null)
                            {
                                try
                                {
                                    var bi = new BitmapImage();
                                    bi.BeginInit();
                                    bi.CacheOption = BitmapCacheOption.OnLoad;
                                    bi.StreamSource = s;
                                    bi.EndInit();
                                    bi.Freeze();
                                    var imgs = new ImageSource[] { bi };
                                    this.parallaxImages = imgs;
                                    this.hasParallaxLayer = true;
                                }
                                catch { /* ignore - fallback below will provide a transparent tile */ }
                            }
                        }
                    }

                    if (this.groundImages == null || this.groundImages.Length == 0)
                    {
                        var wb = new WriteableBitmap(TILE, TILE, 96, 96, PixelFormats.Pbgra32, null);
                        try { wb.Lock(); wb.AddDirtyRect(new Int32Rect(0, 0, TILE, TILE)); } finally { try { wb.Unlock(); } catch { } }
                        // Do not freeze WriteableBitmap; it must remain writable for Lock/WritePixels.
                        this.groundImages = new ImageSource[] { wb };
                        this.groundTonedImages = null;
                        this.hasGroundLayer = true;
                    }
                }
            }
            catch { }

            // Ensure decoration sprites pulse even when editor didn't provide animation frames.
            try
            {
                // Use the simulator's effective animationFrames / previewSpriteMap so
                // this works even when the caller passed null and we created defaults.
                if (this.animationFrames != null && this.previewSpriteMap != null)
                {
                    foreach (var id in decorationSpriteIds)
                    {
                        if (!this.animationFrames.ContainsKey(id))
                        {
                            ImageSource? sourceImg = null;
                            if (this.previewSpriteMap.TryGetValue(id, out var pimg) && pimg != null) sourceImg = pimg;
                            else if (this.spriteImages != null && id >= 0 && id < this.spriteImages.Length && this.spriteImages[id] != null) sourceImg = this.spriteImages[id];
                            if (sourceImg != null)
                            {
                                var frames = CreateTwoFramePulse(sourceImg);
                                if (frames != null) this.animationFrames[id] = frames;
                            }
                        }
                    }
                    // Also ensure dash-orbs pulse even when the editor didn't provide two-frame images.
                    int[] dashOrbIds = new int[] { 0x45, 0x46, 0x4C, 0x4D, 0x50, 0x51, 0x5B, 0x5C, 0x5D, 0x5E };
                    foreach (var id in dashOrbIds)
                    {
                        if (!this.animationFrames.ContainsKey(id))
                        {
                            ImageSource? sourceImg = null;
                            if (this.previewSpriteMap.TryGetValue(id, out var pimg) && pimg != null) sourceImg = pimg;
                            else if (this.spriteImages != null && id >= 0 && id < this.spriteImages.Length && this.spriteImages[id] != null) sourceImg = this.spriteImages[id];
                            if (sourceImg != null)
                            {
                                var frames = CreateTwoFramePulse(sourceImg);
                                if (frames != null) this.animationFrames[id] = frames;
                            }
                        }
                    }
                }
            }
            catch { }

            // (debug reporting removed)

            // Clamp initial cameraY so visible region fits (fixed-point)
            int maxY_fixed = Math.Max(0, (mapHeight - NES_H) * TILE) << 8;
            // Start the simulator from the bottom of the map by default (unless START POS overrides it)
            if (!hasAppliedStartPos)
            {
                cameraY_fixed = maxY_fixed;
            }

            // Setup a high-precision render loop using CompositionTarget and a stopwatch
            timer = new System.Windows.Threading.DispatcherTimer(DispatcherPriority.Render);
            renderStopwatch.Start();
            uiAnimLastMs = renderStopwatch.Elapsed.TotalMilliseconds;
            System.Windows.Media.CompositionTarget.Rendering += CompositionTarget_Rendering;

            this.PreviewKeyDown += SimulatorWindow_KeyDown;
            this.KeyUp += SimulatorWindow_KeyUp;
            this.MouseMove += SimulatorWindow_MouseMove;
            this.Closing += SimulatorWindow_Closing;
            this.Closed += (s, e) =>
            {
                windowClosed = true;
                try { timer.Stop(); } catch { }
                try { if (simTimer != null) { simTimer.Dispose(); simTimer = null; } } catch { }
                try { System.Threading.Thread.Sleep(20); } catch { } // Give pending operations time to check windowClosed flag
                try { System.Windows.Media.CompositionTarget.Rendering -= CompositionTarget_Rendering; } catch { }
            };

            RenderCanvas.Width = NES_W * TILE;
            RenderCanvas.Height = NES_H * TILE;

            // Apply simulator scale: scale the RenderCanvas and overlay so the visible area is zoomed.
            try
            {
                simulatorScale = Math.Max(1, Math.Min(4, simulatorScale));
                var scaleTransform = new System.Windows.Media.ScaleTransform(simulatorScale, simulatorScale);
                RenderCanvas.LayoutTransform = scaleTransform;
                try { PauseOverlay.LayoutTransform = scaleTransform; } catch { }

                // Adjust window size so the scaled canvas fits comfortably (preserve original chrome padding)
                // Original XAML used Width=288 Height=320 for 256x240 canvas. Compute padding from that.
                double widthPadding = 288 - 256; // 32
                double heightPadding = 320 - 240; // 80
                try { this.Width = (NES_W * TILE) * simulatorScale + widthPadding; } catch { }
                try { this.Height = (NES_H * TILE) * simulatorScale + heightPadding; } catch { }
            }
            catch { }
            // Keep nearest-neighbor sampling for bitmaps, but avoid forcing layout rounding/snapping
            try
            {
                System.Windows.Media.RenderOptions.SetBitmapScalingMode(RenderCanvas, BitmapScalingMode.NearestNeighbor);
            }
            catch { }

            // Update window title to include sim time initially
            try { UpdateSimTitle(); } catch { }

            // Ensure the window receives keyboard input for panning

            // Create player visual: try to load `cube.png` from repo root, fall back to magenta rectangle
            try
            {
                // create image control and add to canvas
                playerImage = new System.Windows.Controls.Image { Stretch = Stretch.None };
                System.Windows.Media.RenderOptions.SetBitmapScalingMode(playerImage, BitmapScalingMode.NearestNeighbor);
                System.Windows.Controls.Canvas.SetZIndex(playerImage, 1000);
                RenderCanvas.Children.Add(playerImage);

                // Resolve cube.png relative to executable directory (project root is four levels up from bin)
                try
                {
                    bool loaded = false;
                    string exeDir = AppDomain.CurrentDomain.BaseDirectory ?? ".";
                    string candidate = System.IO.Path.GetFullPath(System.IO.Path.Combine(exeDir, "..\\..\\..\\..\\cube.png"));
                    if (System.IO.File.Exists(candidate))
                    {
                        var bi = new BitmapImage();
                        bi.BeginInit();
                        bi.UriSource = new Uri(candidate);
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.EndInit();
                        bi.Freeze();
                        playerImage.Source = App.EnsureUnfrozenForRender(bi) ?? bi;
                        playerImage.Width = bi.PixelWidth;
                        playerImage.Height = bi.PixelHeight;
                        loaded = true;
                    }

                    // If file path didn't work, attempt to load as an embedded resource from the executing assembly.
                    if (!loaded)
                    {
                        try
                        {
                            var asm = System.Reflection.Assembly.GetExecutingAssembly();
                            var names = asm.GetManifestResourceNames();
                            string? found = names.FirstOrDefault(n => n.EndsWith("cube.png", StringComparison.OrdinalIgnoreCase));
                            if (!string.IsNullOrEmpty(found))
                            {
                                using (var s = asm.GetManifestResourceStream(found))
                                {
                                    if (s != null)
                                    {
                                        var bi = new BitmapImage();
                                        bi.BeginInit();
                                        bi.CacheOption = BitmapCacheOption.OnLoad;
                                        bi.StreamSource = s;
                                        bi.EndInit();
                                        bi.Freeze();
                                        playerImage.Source = App.EnsureUnfrozenForRender(bi) ?? bi;
                                        playerImage.Width = bi.PixelWidth;
                                        playerImage.Height = bi.PixelHeight;
                                        loaded = true;
                                    }
                                }
                            }
                        }
                        catch { /* ignore embedded load errors */ }
                    }

                    if (!loaded)
                    {
                        // fallback rectangle if image not found
                        playerRect = new System.Windows.Shapes.Rectangle { Width = TILE, Height = TILE, Fill = new SolidColorBrush(Colors.Magenta) };
                        System.Windows.Controls.Canvas.SetZIndex(playerRect, 1000);
                        RenderCanvas.Children.Add(playerRect);
                        playerRect.Visibility = Visibility.Visible;
                        // hide the image control if it's unused
                        playerImage.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        playerImage.Visibility = Visibility.Visible;
                        if (playerRect != null) playerRect.Visibility = Visibility.Collapsed;
                    }
                }
                catch
                {
                    // image load failed; use rectangle fallback
                    playerRect = new System.Windows.Shapes.Rectangle { Width = TILE, Height = TILE, Fill = new SolidColorBrush(Colors.Magenta) };
                    System.Windows.Controls.Canvas.SetZIndex(playerRect, 1000);
                    RenderCanvas.Children.Add(playerRect);
                    playerRect.Visibility = Visibility.Visible;
                    if (playerImage != null) playerImage.Visibility = Visibility.Collapsed;
                }
            }
            catch { }
            // Ensure player image matches starting game mode
            try { UpdatePlayerImageForMode(); } catch { }
            // Ensure player image matches starting game mode
            try { UpdatePlayerImageForMode(); } catch { }
            // Ensure the player starts offscreen with just the first outline pixels visible.
            try
            {
                playerVisualWidth = TILE;
                playerVisualHeight = TILE;
                if (playerImage != null && playerImage.Source != null && playerImage.Width > 0)
                {
                    playerVisualWidth = (int)Math.Ceiling(playerImage.Width);
                    playerVisualHeight = (int)Math.Ceiling(playerImage.Height);
                }
                else if (playerRect != null)
                {
                    playerVisualWidth = (int)Math.Ceiling(playerRect.Width);
                    playerVisualHeight = (int)Math.Ceiling(playerRect.Height);
                }

                // Place the player so it starts on the leftmost visible tile (x=0)
                playerX_fixed = 0;
                interactionScreenOffset_px = -1;
                
                // Initialize Y position on ground (unless START POS overrides this later)
                try
                {
                    int groundRowsToReserve = 0;
                    try { if (hasGroundLayer && groundTileRows > 0) groundRowsToReserve = Math.Min(3, groundTileRows); } catch { groundRowsToReserve = 0; }
                    int playerRow = Math.Max(0, mapHeight - groundRowsToReserve - 1);
                    playerY_fixed = (playerRow * TILE) << 8;
                    int maxPlayerY_fixed = Math.Max(0, (mapHeight * TILE - playerVisualHeight)) << 8;
                    if (playerY_fixed > maxPlayerY_fixed) playerY_fixed = maxPlayerY_fixed;
                }
                catch { playerY_fixed = 0; }
                
                // Reset orb activation state at start
                try { ResetOrbSystem(); } catch { }
                
                // Reset blue pad activation state at start
                try { ResetBluePadSystem(); } catch { }
            }
            catch { }
            this.Loaded += (s, e) => { 
                try { this.Focus(); Keyboard.Focus(this); } catch { }
                // Update UI to reflect starting game mode
                try { UpdateGameModeDisplay(); } catch { }
                try { UpdateSpeedDisplay(); } catch { }
            };
            // Create persistent background / tile-layer / ground children to avoid re-allocating each frame
            try
            {
                bgRectPersistent = new System.Windows.Shapes.Rectangle
                {
                    Width = RenderCanvas.Width,
                    Height = RenderCanvas.Height,
                    Fill = new SolidColorBrush(backgroundTint)
                };
                System.Windows.Controls.Canvas.SetLeft(bgRectPersistent, 0);
                System.Windows.Controls.Canvas.SetTop(bgRectPersistent, 0);
                RenderCanvas.Children.Add(bgRectPersistent);

                // Tile layer image: cache full tile block into a RenderTargetBitmap and blit
                tileLayerImage = new System.Windows.Controls.Image
                {
                    Width = (NES_W + 1) * TILE,
                    Height = (NES_H + 1) * TILE,
                    Stretch = Stretch.None
                };
                System.Windows.Media.RenderOptions.SetBitmapScalingMode(tileLayerImage, BitmapScalingMode.NearestNeighbor);
                // Snap to device pixels and use aliased edge mode to avoid 1-px seams when translating
                tileLayerImage.SnapsToDevicePixels = true;
                System.Windows.Media.RenderOptions.SetEdgeMode(tileLayerImage, EdgeMode.Aliased);
                RenderCanvas.Children.Add(tileLayerImage);
                try { System.Windows.Controls.Canvas.SetZIndex(tileLayerImage, 0); } catch { }

                groundRectPersistent = new System.Windows.Shapes.Rectangle
                {
                    Width = RenderCanvas.Width,
                    Height = TILE * Math.Min(NES_H, 2),
                    Fill = new SolidColorBrush(groundTint) { Opacity = 0.25 },
                    Visibility = Visibility.Collapsed // hide ground overlay temporarily until ground rendering is fixed
                };
                System.Windows.Controls.Canvas.SetLeft(groundRectPersistent, 0);
                System.Windows.Controls.Canvas.SetTop(groundRectPersistent, (NES_H * TILE) - (TILE * Math.Min(NES_H, 2)));
                RenderCanvas.Children.Add(groundRectPersistent);
            }
            catch { }

            // Regenerate toned images now that parallax/ground images and persistent UI elements exist.
            try
            {
                // Emulate triggering object trigger 0xB0 before the initial render so
                // outlines default to white the same way an in-map trigger would.
                try
                {
                    // Use pending fields the same way the numeric sim does when it finds a trigger.
                    pendingTileIdx = 0;             // synthetic index used to mark we've set a trigger
                    pendingTileSid = 0xB0;         // sprite id to emulate
                    pendingTintChange = true;
                    pendingTintChangeIsStartup = true;
                    // Apply immediately (we're on the UI thread in the constructor) so toned images
                    // are generated with the triggered outline color before the renderer runs.
                    try { ApplyPendingTints(); } catch { }
                    try { EnsureInitialRender(); } catch { }
                }
                catch { }
                // Ensure UpdateTonedImagesForTileTint still runs to build any remaining caches.
                try { UpdateTonedImagesForTileTint(tileTint, startupTintApplied ? (Color?)Color.FromArgb(0, 0, 0, 0) : null); } catch { }

                try {
                    if (backgroundTint.A == 255 && backgroundTint.R == 0 && backgroundTint.G == 0 && backgroundTint.B == 0)
                    {
                        parallaxTonedImages = CreateBlackMaskedImages(parallaxImages);
                    }
                    else
                    {
                        parallaxTonedImages = CreateHueShiftedImages(parallaxImages, backgroundTint);
                    }
                } catch { parallaxTonedImages = parallaxImages; }

                try {
                    if (groundTint.A == 255 && groundTint.R == 0 && groundTint.G == 0 && groundTint.B == 0)
                    {
                        // For pure-black ground, produce black-masked images that only affect
                        // non-white/non-player-green pixels; do not recolor the thin seam.
                        try { groundTonedImages = CreateBlackMaskedExceptColorArray(groundImages, playerPlaceholderGreen, Color.FromArgb(0,0,0,0), false); }
                        catch { groundTonedImages = CreateTwoToneTileImages(groundImages, Color.FromArgb(255, 0, 0, 0), Color.FromArgb(255, 0, 0, 0), Color.FromArgb(0,0,0,0)); }
                    }
                    else
                    {
                        groundTonedImages = CreateHueShiftedImages(groundImages, groundTint, tileTint);
                    }
                } catch { groundTonedImages = groundImages; }

                // Forced-outline recolor fallback: if a tile/object tint is present at startup,
                // ensure the ground seam gets recolored to match other tiles.
                try
                {
                    if (tileTint.A > 0 && groundTonedImages != null)
                    {
                        var recol = CreateOutlineTintedTileImages(groundTonedImages, tileTint);
                        if (recol != null) groundTonedImages = recol;
                    }
                }
                catch { }

                // If ground tint is pure black, also create black-masked tile images so
                // tiles that visually represent ground (including common index 0) appear
                // black immediately when the simulator opens paused.
                try
                {
                    if (groundTint.A == 255 && groundTint.R == 0 && groundTint.G == 0 && groundTint.B == 0)
                    {
                        try { tileTonedImages = CreateTwoToneTileImages(tileImages, Color.FromArgb(255, 0, 0, 0), Color.FromArgb(255, 0, 0, 0), Color.FromArgb(0, 0, 0, 0)); } catch { }
                    }
                }
                catch { }

                // Invalidate cached tile layer so the initial render uses new toned images
                tileLayerCache = null;
                try { groundTintedTileCache.Clear(); } catch { }
                try { AppendSimDebug($"Constructor: parallaxToned={(parallaxTonedImages!=null?parallaxTonedImages.Length:0)} groundToned={(groundTonedImages!=null?groundTonedImages.Length:0)} tileToned={(tileTonedImages!=null?tileTonedImages.Length:0)}"); } catch { }
            }
            catch { }

            // Initial background override: if parallax images or bitmap are available, set the persistent
            // background rectangle to use an ImageBrush immediately so the UI shows a background even
            // before the render loop runs.
            try
            {
                ImageSource? src = null;
                if (parallaxBitmapToned != null) src = parallaxBitmapToned;
                else if (parallaxBitmap != null) src = parallaxBitmap;
                else if (parallaxTonedImages != null && parallaxTonedImages.Length > 0 && parallaxTonedImages[0] != null) src = parallaxTonedImages[0];
                else if (parallaxImages != null && parallaxImages.Length > 0 && parallaxImages[0] != null) src = parallaxImages[0];

                if (bgRectPersistent != null && src is BitmapSource pbs)
                {
                    var brushImg = App.EnsureUnfrozenForRender(src) ?? src;
                    var brush = new ImageBrush(brushImg)
                    {
                        TileMode = TileMode.Tile,
                        ViewportUnits = BrushMappingMode.Absolute,
                        Viewport = new Rect(0, 0, Math.Max(1.0, pbs.PixelWidth), Math.Max(1.0, pbs.PixelHeight)),
                        Stretch = Stretch.None
                    };
                    bgRectPersistent.Fill = brush;
                }
            }
            catch { }

            // Write an initial diagnostic snapshot of parallax/ground state for local debugging (Option A)
            try
            {
                AppendSimDebug($"Constructor: hasParallaxLayer={this.hasParallaxLayer}, parallaxBitmap={(this.parallaxBitmap!=null)}, parallaxImagesLen={(this.parallaxImages==null?0:this.parallaxImages.Length)}, parallaxTonedLen={(this.parallaxTonedImages==null?0:this.parallaxTonedImages.Length)}, hasGroundLayer={this.hasGroundLayer}, groundImagesLen={(this.groundImages==null?0:this.groundImages.Length)}, backgroundTint={this.backgroundTint}");
            }
            catch { }

            // (debug overlay removed)

            // (debug overlay removed)

            // Initialize cube physics state
            try { EnableCubePhysics_Fresh(); } catch { }

            // Background simulation timer will be started when the simulator is shown via StartSimulation().
        }

        // Map a tile index to its animated version based on current animation frame
        // Mirrors MainWindow.GetAnimatedTileIndex for saw tiles so simulator can animate tile-based saws
        private int MapAnimatedTileIndex(int originalIndex)
        {
            // Simulator forces preview-like behavior when requested
            int mapped = originalIndex;

            // Apply same preview remaps as editor (subset relevant to saws)
            switch (originalIndex)
            {
                case 0xFC:
                case 0xDF:
                case 0xE3:
                case 0xFE:
                case 0xFF: mapped = 0x00; break;
                case 0xFD: mapped = 0x26; break;
            }

            if (mapped >= 0x08 && mapped <= 0x0B)
            {
                // Use the same rhythm as sprite animations (frame math below)
                bool showFrame2 = (((GetEditorAnimationFrameValue() * 9) / 20) % 2) == 1;
                int tileOffset = mapped - 0x08;
                return showFrame2 ? 1004 + tileOffset : 1000 + tileOffset;
            }

            if (mapped == 0x04 || mapped == 0x7D || mapped == 0x7F)
            {
                bool showFrame2 = (((animationFrame * 9) / 20) % 2) == 1;
                int tileOffset = (mapped == 0x04) ? 0 : (mapped == 0x7D) ? 1 : 2;
                return showFrame2 ? 1013 + tileOffset : 1010 + tileOffset;
            }

            if (mapped >= 0x74 && mapped <= 0x7C)
            {
                bool showFrame2 = (((animationFrame * 9) / 20) % 2) == 1;
                int tileOffset = mapped - 0x74;
                return showFrame2 ? 1029 + tileOffset : 1020 + tileOffset;
            }

            return originalIndex;
        }

        private async void SimulatorWindow_KeyDown(object sender, KeyEventArgs e)
        {
            // If an unpause/start request is pending, ignore all additional input
            if (playbackStartPending)
            {
                try { e.Handled = true; } catch { }
                return;
            }
            if (e.Key == Key.Up) upHeld = true;
            if (e.Key == Key.Down) downHeld = true;
            if (e.Key == Key.X || (!MainWindow.Option_CamMode && (e.Key == Key.Up || e.Key == Key.Space)))
            {
                // Only trigger on the initial KeyDown (ignore OS key-repeat)
                try
                {
                    if (!e.IsRepeat)
                    {
                        lock (simLock)
                        {
                            // Increment press counter atomically for cube physics
                            // Physics will be ignored if physicsEnabled is false (cam mode)
                            try { Interlocked.Increment(ref keyXPressedCount); } catch { }
                            
                            // For ball mode, queue a toggle request
                            if (currentGameMode == 2)
                            {
                                try { Interlocked.Exchange(ref ballToggleRequested, 1); } catch { }
                                LogBallEvent($"KeyDown: queued toggle");
                            }
                        }
                    }
                }
                catch { }
            }
            
            // Q key: toggle mini/normal
            if (e.Key == Key.Q)
            {
                AppendSimDebug("[Q_HANDLER] Q key detected");
                try
                {
                    if (!e.IsRepeat)
                    {
                        AppendSimDebug("[Q_HANDLER] Not a repeat");
                        lock (simLock)
                        {
                            AppendSimDebug("[Q_HANDLER] In lock");
                            // Toggle miniMode and sync with currplayer_mini
                            miniMode = !miniMode;
                            currplayer_mini = (byte)(miniMode ? 1 : 0);
                            AppendSimDebug($"[Q_HANDLER] Set miniMode to {miniMode}, currplayer_mini to {currplayer_mini}");
                            currplayer_table_idx = (currplayer_gravity != 0 ? 1 : 0) | (currplayer_mini != 0 ? 4 : 0);
                            try { UpdatePlayerIconFlip(); } catch { AppendSimDebug("[Q_HANDLER] UpdatePlayerIconFlip exception"); }
                            try { UpdatePlayerImageForMode(); } catch { AppendSimDebug("[Q_HANDLER] UpdatePlayerImageForMode exception"); }
                            try { UpdatePlayerVisualSizeForMode(); } catch { AppendSimDebug("[Q_HANDLER] UpdatePlayerVisualSizeForMode exception"); }
                            // Sync checkbox state
                            try { MiniCheckBox.IsChecked = miniMode; } catch { AppendSimDebug("[Q_HANDLER] MiniCheckBox update exception"); }
                            AppendSimDebug($"[MINI] Mode now: {(miniMode ? "MINI" : "NORMAL")}");
                            e.Handled = true;
                        }
                    }
                }
                catch (Exception ex) { AppendSimDebug($"[Q_HANDLER] Exception: {ex.Message}"); }
            }
            
            // W key: toggle gravity
            if (e.Key == Key.W)
            {
                try
                {
                    if (!e.IsRepeat)
                    {
                        lock (simLock)
                        {
                            AppendSimDebug("[INPUT] W key pressed - toggling gravity!");
                            // Always toggle gravity regardless of No-Death mode
                            gravityReversed = !gravityReversed;
                            gravityFlipped = gravityReversed;
                            effectiveInvertedByW = gravityReversed;
                            currplayer_gravity = (byte)(gravityReversed ? 0xFF : 0x00);
                            currplayer_table_idx = (currplayer_gravity != 0 ? 1 : 0) | (currplayer_mini != 0 ? 4 : 0);
                            UpdatePlayerIconFlip();
                            try { UpdateEffectiveGravity(); } catch { }
                            onGround = false;
                            groundStabilizeCounter = 0;
                            try { if (currentGameMode == 2) ballGoingDown = !gravityReversed; } catch { }
                            AppendSimDebug($"[GRAVITY] Gravity now: {(gravityReversed ? "INVERTED" : "NORMAL")}");
                        }
                    }
                }
                catch { }
            }
            
            // Shift+F12: toggle Y-velocity overlay for debugging
            if (e.Key == Key.F12 && (Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            {
                try
                {
                    showYVelocityOverlay = !showYVelocityOverlay;
                    if (showYVelocityOverlay)
                    {
                        try
                        {
                            if (yVelTextBlock == null)
                            {
                                yVelTextBlock = new System.Windows.Controls.TextBlock();
                                yVelTextBlock.Foreground = new SolidColorBrush(Colors.Yellow);
                                yVelTextBlock.FontWeight = FontWeights.Bold;
                                yVelTextBlock.FontSize = 14;
                                yVelTextBlock.IsHitTestVisible = false;
                                RenderCanvas.Children.Add(yVelTextBlock);
                                try { System.Windows.Controls.Canvas.SetZIndex(yVelTextBlock, 2000); } catch { }
                            }
                        }
                        catch { }
                    }
                    else
                    {
                        try { if (yVelTextBlock != null) yVelTextBlock.Visibility = Visibility.Collapsed; } catch { }
                    }
                    try { RenderFrame(); } catch { }
                }
                catch { }
            }

            // Shift+F11: toggle debug info window
            if (e.Key == Key.F11 && (Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            {
                try
                {
                    if (debugWindow == null || !debugWindow.IsLoaded)
                    {
                        // Create and show debug window
                        debugWindow = new DebugInfoWindow();
                        
                        // Position to the right of the simulator window
                        debugWindow.Owner = this;
                        debugWindow.Left = this.Left + this.ActualWidth;
                        debugWindow.Top = this.Top;
                        
                        debugWindow.Closed += (s, args) => debugWindow = null;
                        debugWindow.Show();
                    }
                    else
                    {
                        // Close debug window
                        debugWindow.Close();
                        debugWindow = null;
                    }
                }
                catch { }
            }

            // Speed adjustment: '-' decrease sim time scale by 10%, '+' increase by 10% (even 10% steps)
            if (e.Key == Key.OemMinus || e.Key == Key.Subtract)
            {
                try
                {
                    simTimeScale = Math.Max(0.1, Math.Round((simTimeScale - 0.1) * 10.0) / 10.0);
                    try { UpdateSimTitle(); } catch { }
                    try { if (this.Owner is MainWindow mw) { mw.SetSimulatorPlaybackRate(simTimeScale); } } catch { }
                }
                catch { }
            }
            if (e.Key == Key.OemPlus || e.Key == Key.Add)
            {
                try
                {
                    simTimeScale = Math.Min(4.0, Math.Round((simTimeScale + 0.1) * 10.0) / 10.0);
                    try { UpdateSimTitle(); } catch { }
                    try { if (this.Owner is MainWindow mw) { mw.SetSimulatorPlaybackRate(simTimeScale); } } catch { }
                }
                catch { }
            }
            if (e.Key == Key.W)
            {
                try
                {
                    if (!e.IsRepeat)
                    {
                        lock (simLock)
                        {
                            // Already handled above in main key input section
                        }
                    }
                }
                catch { }
            }
            if (e.Key == Key.F2)
            {
                try
                {
                    if (!e.IsRepeat)
                    {
                        ShowSpriteHitboxes = !ShowSpriteHitboxes;
                        // Mirror the editor's canonical option so F2 in simulator updates visuals everywhere
                        try
                        {
                            MainWindow.Option_ShowSimulatorSpriteHitboxes = ShowSpriteHitboxes;
                            if (Application.Current != null)
                            {
                                // Update the main menu item's checked state if present
                                try { if (Application.Current.MainWindow is MainWindow mw && mw.MenuOptionShowSpriteHitboxes != null) mw.MenuOptionShowSpriteHitboxes.IsChecked = ShowSpriteHitboxes; } catch { }
                                // Propagate to any other open simulators
                                foreach (Window w2 in Application.Current.Windows)
                                {
                                    try { if (w2 is SimulatorWindow sw2) sw2.ShowSpriteHitboxes = ShowSpriteHitboxes; } catch { }
                                }
                            }
                        }
                        catch { }
                        try { MainWindow.ShowTransientInfo($"Show Sprite Hitboxes: {(ShowSpriteHitboxes ? "ON" : "OFF")}", this, 1500); } catch { }
                    }
                }
                catch { }
            }
            if (e.Key == Key.Tab)
            {
                // compute tab multiplier based on modifiers: Tab=2x, Shift+Tab=4x, Ctrl+Shift+Tab=8x
                bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
                if (shift && ctrl) tabSpeedMultiplier = 8;
                else if (shift) tabSpeedMultiplier = 4;
                else tabSpeedMultiplier = 2;
                // Tab speed multiplier previously notified the owner to change audio playback rate.
                // Reverted: do not affect music from simulator key presses.
            }
            if (e.Key == Key.Escape)
            {
                // toggle pause. When unpausing, request the owner to start music so music
                // and gameplay begin on the same frame.
                bool wasPaused = paused;
                bool willBePaused = !paused;

                if (wasPaused && !willBePaused)
                {
                    // Unpausing: request the owner to start playback and wait briefly for audio
                    // to begin so audio and gameplay are (more) in sync, then advance one
                    // numeric step and render a frame so the simulator visibly starts.
                    if (!playbackStartPending)
                    {
                        playbackStartPending = true;
                        try
                        {
                            try { if (this.Owner is MainWindow mw) { var t = mw.StartSimulatorPlaybackAsync(); if (t != null) await t; } } catch { }
                            try { SimulateNumericStep(); } catch { }
                            try { RenderFrame(); } catch { }
                        }
                        finally
                        {
                            playbackStartPending = false;
                        }
                    }
                }

                // Pausing: request the owner to pause music so audio stops when simulator pauses.
                if (!wasPaused && willBePaused)
                {
                    try { if (this.Owner is MainWindow mw) { mw.PauseSimulatorPlayback(); } } catch { }
                }

                paused = willBePaused;

                // Update overlay visibility
                try { PauseOverlay.Visibility = paused ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed; } catch { }
            }
        }

        private void SimulatorWindow_MouseMove(object sender, MouseEventArgs e)
        {
            try
            {
                // Show red death dots when hovering over player after death
                if (!deathTriggered || deathTileX < 0 || deathTileY < 0)
                {
                    // Hide dots if not in death state
                    if (deathPlayerDot != null) deathPlayerDot.Visibility = Visibility.Collapsed;
                    if (deathTileDot != null) deathTileDot.Visibility = Visibility.Collapsed;
                    return;
                }

                // Get mouse position relative to RenderCanvas
                Point mousePos = e.GetPosition(RenderCanvas);
                
                // Get player screen position
                int playerScreenX = (playerX_fixed >> 8) - (cameraX_fixed >> 8);
                int playerScreenY = (playerY_fixed >> 8) - (cameraY_fixed >> 8);
                
                // Check if hovering over player (within player visual bounds)
                bool hoveringPlayer = mousePos.X >= playerScreenX && mousePos.X < playerScreenX + playerVisualWidth &&
                                     mousePos.Y >= playerScreenY && mousePos.Y < playerScreenY + playerVisualHeight;
                
                if (hoveringPlayer)
                {
                    // Create death dots if they don't exist
                    if (deathPlayerDot == null)
                    {
                        deathPlayerDot = new System.Windows.Shapes.Ellipse
                        {
                            Width = 8,
                            Height = 8,
                            Fill = new SolidColorBrush(Colors.Red),
                            IsHitTestVisible = false
                        };
                        RenderCanvas.Children.Add(deathPlayerDot);
                        System.Windows.Controls.Canvas.SetZIndex(deathPlayerDot, 3000);
                    }

                    if (deathTileDot == null)
                    {
                        deathTileDot = new System.Windows.Shapes.Ellipse
                        {
                            Width = 8,
                            Height = 8,
                            Fill = new SolidColorBrush(Colors.Red),
                            IsHitTestVisible = false
                        };
                        RenderCanvas.Children.Add(deathTileDot);
                        System.Windows.Controls.Canvas.SetZIndex(deathTileDot, 3000);
                    }
                    
                    // Position player dot at center of player
                    double playerDotX = playerScreenX + (playerVisualWidth / 2.0) - 4;
                    double playerDotY = playerScreenY + (playerVisualHeight / 2.0) - 4;
                    System.Windows.Controls.Canvas.SetLeft(deathPlayerDot, playerDotX);
                    System.Windows.Controls.Canvas.SetTop(deathPlayerDot, playerDotY);
                    deathPlayerDot.Visibility = Visibility.Visible;
                    
                    // Position tile dot at death tile location (screen coordinates)
                    int deathScreenX = deathTileX - (cameraX_fixed >> 8);
                    int deathScreenY = deathTileY - (cameraY_fixed >> 8);
                    System.Windows.Controls.Canvas.SetLeft(deathTileDot, deathScreenX - 4);
                    System.Windows.Controls.Canvas.SetTop(deathTileDot, deathScreenY - 4);
                    deathTileDot.Visibility = Visibility.Visible;
                }
                else
                {
                    // Hide dots when not hovering
                    if (deathPlayerDot != null) deathPlayerDot.Visibility = Visibility.Collapsed;
                    if (deathTileDot != null) deathTileDot.Visibility = Visibility.Collapsed;
                }
            }
            catch { }
        }

        private void SimulatorWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                // Stop simulation to prevent render thread errors
                StopSimulation();
                
                // Cleanup RenderCanvas on UI thread
                Dispatcher.Invoke(() =>
                {
                    try { RenderCanvas.Children.Clear(); } catch { }
                }, System.Windows.Threading.DispatcherPriority.Normal);
            }
            catch { }
        }

        private void SimulatorWindow_KeyUp(object sender, KeyEventArgs e)
        {
            // Ignore key-up events while start playback is pending to avoid changing held state.
            if (playbackStartPending)
            {
                try { e.Handled = true; } catch { }
                return;
            }
            if (e.Key == Key.Up) upHeld = false;
            if (e.Key == Key.Down) downHeld = false;
            if (e.Key == Key.X || (!MainWindow.Option_CamMode && (e.Key == Key.Up || e.Key == Key.Space)))
            {
                // Clear any buffered X presses when key is released
                try
                {
                    lock (simLock)
                    {
                        try { Interlocked.Exchange(ref keyXPressedCount, 0); } catch { }
                    }
                }
                catch { }
                // If the player released X, cancel any queued ball toggle request.
                try
                {
                    Interlocked.Exchange(ref ballToggleRequested, 0);
                }
                catch { }
                try { Interlocked.Exchange(ref keyXHeldStartedOnGroundInt, 0); } catch { }
                try { Interlocked.Exchange(ref keyXPressStartedOnGroundInt, 0); } catch { }
                // Clear orb buffer immediately on UI release so holds cannot persist.
                try { orbBufferActive = false; } catch { }
                try { orbHoldConsumed = false; } catch { }
                try { orbHoldConsumedKeyStillDown = false; } catch { }
                try { orbHoldSuppressing = false; } catch { }
            }
            if (e.Key == Key.Tab)
            {
                tabSpeedMultiplier = 1;
            }
        }

        private async void PauseOverlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            try
            {
                // Only respond when currently paused.
                if (!paused)
                    return;

                // If another start is already pending, ignore repeated clicks.
                if (!playbackStartPending)
                {
                    playbackStartPending = true;
                    try
                    {
                        // Start/resume music at correct position
                        if (this.Owner is MainWindow mw)
                        {
                            bool isPlaying = mw.IsMusicPlaying();
                            bool isPaused = mw.IsMusicPaused();
                            AppendSimDebug($"[UNPAUSE] Music state: playing={isPlaying}, paused={isPaused}, hasAppliedStartPos={hasAppliedStartPos}");
                            
                            // If music is not playing (either paused or stopped), start it with proper seeking
                            if (!isPlaying)
                            {
                                double musicTime = 0.0;
                                if (hasAppliedStartPos)
                                {
                                    musicTime = CalculateMusicTimeToPosition(startPosX_forMusicSeek + (playerVisualWidth / 2));
                                    AppendSimDebug($"[UNPAUSE] Starting music with seek to START POS musicTime={musicTime:F3}s");
                                }
                                else
                                {
                                    AppendSimDebug($"[UNPAUSE] Starting music with seek to beginning");
                                }
                                
                                // Stop any existing playback first
                                await mw.StopSimulatorPlaybackAsync();
                                
                                // Start fresh with proper seeking
                                var startTask = mw.ForceStartAndSeekSimulatorPlaybackAsync(musicTime);
                                if (startTask != null) await startTask;
                            }
                            else
                            {
                                AppendSimDebug($"[UNPAUSE] Music already playing, no action needed");
                            }
                        }
                        try { SimulateNumericStep(); } catch { }
                        try { RenderFrame(); } catch { }
                    }
                    finally
                    {
                        playbackStartPending = false;
                    }
                }

                paused = false;
                try { PauseOverlay.Visibility = System.Windows.Visibility.Collapsed; } catch { }
                
                // Grab focus to enable keyboard input (W, up/down) after unpausing
                try { this.Focus(); } catch { }

                // Mark event handled so underlying canvas doesn't also receive it.
                e.Handled = true;
            }
            catch { }
        }

        private void RenderCanvas_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Grab focus on the window to enable keyboard input (W, up/down)
            // Focus the window itself, not the canvas, so keyboard events reach the window handlers
            this.Focus();
            e.Handled = true;
        }

        private void GameModeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            try
            {
                if (GameModeComboBox.SelectedIndex >= 0)
                {
                    currentGameMode = GameModeComboBox.SelectedIndex;
                    try { UpdatePlayerSpeed(); } catch { }
                    try { UpdateEffectiveGravity(); } catch { }
                    try { UpdatePlayerImageForMode(); } catch { }
                }
            }
            catch { }
        }

        private void MiniCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            try
            {
                miniMode = MiniCheckBox.IsChecked == true;
                currplayer_mini = (byte)(miniMode ? 1 : 0);
                try { UpdateEffectiveGravity(); } catch { }
                try { UpdatePlayerImageForMode(); } catch { }
                try { UpdatePlayerVisualSizeForMode(); } catch { }
            }
            catch { }
        }

        private void InvertedCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            try
            {
                bool wasInverted = gravityReversed;
                gravityReversed = InvertedCheckBox.IsChecked == true;
                gravityFlipped = gravityReversed;
                currplayer_gravity = (byte)(gravityReversed ? 0xFF : 0x00);
                
                if (wasInverted != gravityReversed)
                {
                    try { UpdateEffectiveGravity(); } catch { }
                    try { UpdatePlayerIconFlip(); } catch { }
                }
            }
            catch { }
        }

        private void SpeedComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            try
            {
                if (SpeedComboBox.SelectedItem is ComboBoxItem item && item.Tag != null)
                {
                    if (int.TryParse(item.Tag.ToString(), out int speedIndex))
                    {
                        // Speed indices: 0=0.5x, 1=1x, 2=2x, 3=3x, 4=4x, 5=0.1x
                        speed = speedIndex;
                        // Update horizontal movement speed to match speed portal values
                        int[] speedValues = { CUBE_SPEED_X05, CUBE_SPEED_X1, CUBE_SPEED_X2, CUBE_SPEED_X3, CUBE_SPEED_X4, CUBE_SPEED_SLOW };
                        if (speedIndex >= 0 && speedIndex < speedValues.Length)
                        {
                            currentSpeed_fixed = speedValues[speedIndex];
                            playerVelX_fixed = speedValues[speedIndex];
                        }
                        // Update visual display immediately
                        UpdateSpeedDisplay();
                    }
                }
            }
            catch { }
        }
        
        private void UpdateSpeedDisplay()
        {
            try
            {
                int speedIndex = speed;
                if (SpeedComboBox != null && speedIndex >= 0 && speedIndex < SpeedComboBox.Items.Count)
                {
                    if (SpeedComboBox.SelectedIndex != speedIndex)
                    {
                        SpeedComboBox.SelectedIndex = speedIndex;
                    }
                }
            }
            catch { }
        }
        
        private void UpdateGameModeDisplay()
        {
            try
            {
                if (GameModeComboBox != null && currentGameMode >= 0 && currentGameMode < GameModeComboBox.Items.Count)
                {
                    if (GameModeComboBox.SelectedIndex != currentGameMode)
                    {
                        GameModeComboBox.SelectedIndex = currentGameMode;
                    }
                }
            }
            catch { }
        }

        private async void RestartButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Stop current simulation
                StopSimulation();

                // Check for START POS marker
                int startX_px = 0;
                int startY_px = 0;
                bool hasStartPos = false;

                try
                {
                    if (this.Owner is MainWindow mw)
                    {
                        if (mw.StartPosMarkerX.HasValue && mw.StartPosMarkerY.HasValue)
                        {
                            startX_px = mw.StartPosMarkerX.Value;
                            startY_px = mw.StartPosMarkerY.Value;
                            hasStartPos = true;
                            startPosX_forMusicSeek = startX_px;
                            hasAppliedStartPos = true; // Update flag for music seeking
                        }
                        else
                        {
                            hasAppliedStartPos = false; // No START POS
                        }
                    }
                }
                catch { }

                // Reset player position to start or START POS marker
                playerX_fixed = startX_px << 8;

                // Calculate interaction screen offset based on START POS
                if (hasStartPos)
                {
                    int playerCenter_fixed = playerX_fixed + ((playerVisualWidth / 2) << 8);
                    // If player is past interaction line, calculate the proper screen offset
                    if (playerCenter_fixed >= INTERACTION_LINE_FIXED)
                    {
                        // In standard GD, the interaction line appears at screen pixel 80
                        // when camera is at X=0. The offset is where interaction line appears
                        // on screen when it's crossed.
                        interactionScreenOffset_px = 80; // Standard GD interaction line screen position
                    }
                    else
                    {
                        // Player is before interaction line - camera stays at 0
                        interactionScreenOffset_px = -1;
                    }
                }
                else
                {
                    interactionScreenOffset_px = -1;
                }

                // Reset player to starting Y position (one tile above ground rows) or marker Y
                if (hasStartPos)
                {
                    playerY_fixed = startY_px << 8;
                    
                    // Clamp to map bounds
                    int maxPlayerY_fixed = Math.Max(0, (mapHeight * TILE - playerVisualHeight)) << 8;
                    if (playerY_fixed > maxPlayerY_fixed) playerY_fixed = maxPlayerY_fixed;
                }
                else
                {
                    try
                    {
                        int groundRowsToReserve = 0;
                        try { if (hasGroundLayer && groundTileRows > 0) groundRowsToReserve = Math.Min(3, groundTileRows); } catch { groundRowsToReserve = 0; }
                        int playerRow = Math.Max(0, mapHeight - groundRowsToReserve - 1);
                        playerY_fixed = (playerRow * TILE) << 8;
                        int maxPlayerY_fixed = Math.Max(0, (mapHeight * TILE - playerVisualHeight)) << 8;
                        if (playerY_fixed > maxPlayerY_fixed) playerY_fixed = maxPlayerY_fixed;
                    }
                    catch { playerY_fixed = 0; }
                }

                // Reset velocity and physics state
                playerVelY_fixed = 0;

                // Reset gamemode/mini/gravity/speed to defaults
                currentGameMode = 0; // Cube
                miniMode = false;
                currplayer_mini = 0;
                currplayer_gravity = 0;
                gravityReversed = false;
                gravityFlipped = false;
                speed = 1; // 1x speed
                playerVelX_fixed = CUBE_SPEED_X1;

                // Update UI to match defaults
                try { UpdateGameModeDisplay(); } catch { }
                try { UpdateSpeedDisplay(); } catch { }
#pragma warning disable CS4014
                try { Dispatcher.BeginInvoke(new Action(() => { if (MiniCheckBox != null) MiniCheckBox.IsChecked = false; })); } catch { }
#pragma warning restore CS4014
                try { UpdatePlayerImageForMode(); } catch { }
                try { UpdatePlayerVisualSizeForMode(); } catch { }
                try { UpdatePlayerIconFlip(); } catch { }

                // Reset camera to starting position or START POS marker
                if (hasStartPos)
                {
                    // Position camera based on interaction offset
                    int playerCenter_fixed_restart = playerX_fixed + ((playerVisualWidth / 2) << 8);
                    if (playerCenter_fixed_restart >= INTERACTION_LINE_FIXED && interactionScreenOffset_px >= 0)
                    {
                        cameraX_fixed = playerX_fixed - (interactionScreenOffset_px << 8);
                    }
                    else
                    {
                        cameraX_fixed = 0;
                    }
                    int maxCameraY_fixed = Math.Max(0, (mapHeight - NES_H) * TILE) << 8;
                    cameraY_fixed = Math.Max(0, Math.Min(maxCameraY_fixed, playerY_fixed - ((NES_H * TILE / 2) << 8)));
                }
                else
                {
                    cameraX_fixed = 0;
                    int maxY_fixed = Math.Max(0, (mapHeight - NES_H) * TILE) << 8;
                    cameraY_fixed = maxY_fixed;
                }

                // Clear paths and processed portals
                try { recordedPlayerPath.Clear(); } catch { }
                try { processedGravityPortals.Clear(); } catch { }
                try { processedSpeedPortals.Clear(); } catch { }

                // Apply portal states and color triggers if using START POS
                if (hasStartPos)
                {
                    // Scan for last gamemode/mini/gravity/speed portals before START POS
                    try { ApplyPortalStatesUpToPosition(startX_px); } catch { }
                    // Apply color triggers (already done earlier, but clear processed list first)
                    try { processedColorTriggers.Clear(); } catch { }
                    try { ApplyColorTriggersUpToPosition(startX_px); } catch { }
                }
                else
                {
                    // No START POS - clear color triggers to use defaults
                    try { processedColorTriggers.Clear(); } catch { }
                }

                try { ResetOrbSystem(); } catch { }
                try { ResetBluePadSystem(); } catch { }

                // Clear input buffers
                try { Interlocked.Exchange(ref keyXPressedCount, 0); } catch { }
                keyXHeld = false;
                upHeld = false;
                downHeld = false;

                // Reset ball/swing state
                ballSwitched[0] = false;
                ballFlipCooldown = 0;
                ufoOrbed = false;

                // Stop music first (same as death) before restarting simulation
                try
                {
                    AppendSimDebug($"[RESTART] Stopping music (isPlaying={this.Owner is MainWindow mw2 && mw2.IsMusicPlaying()})");
                    await StopMusicAsync();
                    AppendSimDebug($"[RESTART] Music stopped (isPlaying={this.Owner is MainWindow mw3 && mw3.IsMusicPlaying()})");
                }
                catch { }

                // Restart simulation timer
                StopSimulation();
                await System.Threading.Tasks.Task.Delay(20).ConfigureAwait(false);
                StartSimulation();

                // Reset to initial paused state
                paused = true;
                try { PauseOverlay.Visibility = System.Windows.Visibility.Visible; } catch { }

                // Clear death state if any
                deathTriggered = false;
            }
            catch { }
        }

        public async System.Threading.Tasks.Task StartAndSeekMusicAsync()
        {
            try
            {
                if (this.Owner is MainWindow mw && hasAppliedStartPos)
                {
                    // Start playback
                    var t = mw.StartSimulatorPlaybackAsync();
                    if (t != null)
                    {
                        await t;
                        // Wait for audio to be fully initialized
                        await System.Threading.Tasks.Task.Delay(200).ConfigureAwait(false);
                        // Seek to calculated position
                        double musicTimeSeconds = CalculateMusicTimeToPosition(startPosX_forMusicSeek);
                        mw.SeekSimulatorPlayback(musicTimeSeconds);
                        // Small delay to ensure seek completes
                        await System.Threading.Tasks.Task.Delay(100).ConfigureAwait(false);
                    }
                }
            }
            catch { }
        }

        // Public API: start the simulator running and request playback (used by MainWindow to auto-start)
        public async System.Threading.Tasks.Task StartRunningAsync()
        {
            try
            {
                // Only act when currently paused.
                if (!paused) return;

                // If another start is already pending, avoid duplicate starts.
                if (!playbackStartPending)
                {
                    playbackStartPending = true;
                    try
                    {
                        if (this.Owner is MainWindow mw)
                        {
                            // Calculate seek position
                            double musicTime = 0.0;
                            if (hasAppliedStartPos)
                            {
                                musicTime = CalculateMusicTimeToPosition(startPosX_forMusicSeek + (playerVisualWidth / 2));
                                AppendSimDebug($"[START] Starting and seeking to START POS musicTime={musicTime:F3}s");
                            }
                            else
                            {
                                AppendSimDebug($"[START] Starting and seeking to beginning");
                            }
                            
                            // Force start playback with immediate seek (before music plays)
                            var startTask = mw.ForceStartAndSeekSimulatorPlaybackAsync(musicTime);
                            if (startTask != null) await startTask;
                            
                            // Now unpause gameplay (music already playing)
                            paused = false;
                        }
                        else
                        {
                            paused = false;
                            try { if (this.Owner is MainWindow mw2) { var t = mw2.ResumeSimulatorPlaybackAsync(); if (t != null) await t; } } catch { }
                        }
                        
                        try { SimulateNumericStep(); } catch { }
                        try { RenderFrame(); } catch { }
                    }
                    finally
                    {
                        playbackStartPending = false;
                    }
                }
                try { PauseOverlay.Visibility = System.Windows.Visibility.Collapsed; } catch { }
            }
            catch { }
        }

        // Apply START POS marker position (called after Owner is set)
        private bool hasAppliedStartPos = false;
        private int startPosX_forMusicSeek = 0;

        public bool HasStartPosApplied() => hasAppliedStartPos;

        public void ApplyStartPosMarker()
        {
            try
            {
                if (this.Owner is MainWindow mw)
                {
                    if (mw.StartPosMarkerX.HasValue && mw.StartPosMarkerY.HasValue)
                    {
                        int startX_px = mw.StartPosMarkerX.Value;
                        int startY_px = mw.StartPosMarkerY.Value;
                        
                        playerX_fixed = startX_px << 8;
                        playerY_fixed = startY_px << 8;
                        
                        // Clamp to map bounds
                        int maxPlayerY_fixed = Math.Max(0, (mapHeight * TILE - playerVisualHeight)) << 8;
                        if (playerY_fixed > maxPlayerY_fixed) playerY_fixed = maxPlayerY_fixed;
                        
                        // Calculate proper camera and interaction offset for START POS
                        int playerCenter_fixed = playerX_fixed + ((playerVisualWidth / 2) << 8);
                        if (playerCenter_fixed >= INTERACTION_LINE_FIXED)
                        {
                            // Player is past interaction line - use standard GD offset (80 pixels)
                            interactionScreenOffset_px = 80;
                            // Position camera so player appears at screen X = 80
                            cameraX_fixed = playerX_fixed - (interactionScreenOffset_px << 8);
                        }
                        else
                        {
                            // Player is before interaction line - camera at 0
                            cameraX_fixed = 0;
                            interactionScreenOffset_px = -1;
                        }
                        
                        // Position camera Y centered on player
                        int maxCameraY_fixed = Math.Max(0, (mapHeight - NES_H) * TILE) << 8;
                        cameraY_fixed = Math.Max(0, Math.Min(maxCameraY_fixed, playerY_fixed - ((NES_H * TILE / 2) << 8)));
                        
                        hasAppliedStartPos = true;
                        startPosX_forMusicSeek = startX_px;
                        
                        // Apply starting speed from level config before scanning portals
                        try
                        {
                            int speedFixed = startingSpeedUiIndex switch
                            {
                                0 => CUBE_SPEED_X05, // 0.5x
                                1 => CUBE_SPEED_X1,   // 1x
                                2 => CUBE_SPEED_X2,   // 2x
                                3 => CUBE_SPEED_X3,   // 3x
                                4 => CUBE_SPEED_X4,   // 4x
                                _ => CUBE_SPEED_X1
                            };
                            playerVelX_fixed = speedFixed;
                            speed = startingSpeedUiIndex;
                            try { UpdateSpeedDisplay(); } catch { }
                        }
                        catch { }
                        
                        // Apply portals (gamemode, mini, gravity, speed) up to START POS
                        // Speed portals will override the starting speed if present
                        try { ApplyPortalStatesUpToPosition(startX_px); } catch { }
                        
                        // Apply color triggers up to this position
                        ApplyColorTriggersUpToPosition(startX_px);
                        
                        // Cache the music time for when playback starts
                        // Don't seek here - will seek when playback actually starts
                        try
                        {
                            double musicTime = CalculateMusicTimeToPosition(startX_px + (playerVisualWidth / 2));
                            AppendSimDebug($"[START POS] Applied: X={startX_px}, musicTime={musicTime:F3}s (will seek on unpause)");
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            // Poll input on UI thread to generate stable per-frame pressed/held flags for numeric sim
            try
            {
                // Use IsXDownAsync() to get global keyboard state (works even when window is unfocused)
                bool curX = IsXDownAsync() || 
                            (!MainWindow.Option_CamMode && (System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.Up) || 
                                                             System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.Space)));
                bool curUp = System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.Up);
                bool curDown = System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.Down);
                lock (simLock)
                {
                    // Only register an X press edge if the player is currently on the ground (no queued mid-air presses)
                    int maxPlayerY_fixed_poll = Math.Max(0, (mapHeight * TILE - playerVisualHeight)) << 8;
                    // Edge detect X regardless of ground so mid-air jumps (for testing) are possible.
                    // Make the edge sticky until the numeric/UI physics code consumes it so
                    // the background fixed-step sim cannot miss a short UI-frame edge.
                    if (curX && !prevKeyXDown)
                    {
                        // record an edge atomically so numeric sim cannot miss it
                        // ProcessModeXEdge(); // REMOVED - fresh port
                    }
                    prevKeyXDown = curX;
                    keyXHeld = curX;

                    // Also poll Up/Down to avoid missing key events; these set the held flags used by movement logic
                    upHeld = curUp;
                    downHeld = curDown;
                }
            }
            catch { }

            // Keep previous camera center for later anchor detection
            int prevCameraCenter_fixed = cameraX_fixed + ((NES_W * TILE / 2) << 8);

            // Advance player X by current dynamic speed (fixed-point)
            // Holding TAB or modifiers change horizontal movement speed via tabSpeedMultiplier.
            int speedMultiplier = tabSpeedMultiplier;
            int centerOffset_fixed = (TILE / 2) << 8;
            int prevPlayerCenter_fixed = playerX_fixed + centerOffset_fixed;
            int attemptedPlayerX_fixed = playerX_fixed + (int)Math.Round((currentSpeed_fixed * speedMultiplier) * simTimeScale);
            int attemptedPlayerCenter_fixed = attemptedPlayerX_fixed + centerOffset_fixed;

            // Move the player forward in world coordinates first
            playerX_fixed = attemptedPlayerX_fixed;
            // When player moves horizontally while considered grounded, clear the
            // stabilization counter so walking off platforms causes immediate fall.
            if (onGround)
            {
                groundStabilizeCounter = 0;
                // Immediately verify the player still has supporting surface underfoot
                // (or overhead when gravity is reversed). If not, clear `onGround`
                // so gravity resumes on the same frame instead of persisting.
                try
                {
                    bool stillSupported = false;
                    if (gravityReversed)
                    {
                        // When gravity is reversed, the ceiling acts like the ground.
                        stillSupported = IsTouchingCeiling();
                    }
                    else
                    {
                        const int HITBOX_W_LOCAL = 15;
                        int playerCenter_px = (playerX_fixed >> 8) + (playerVisualWidth / 2);
                        int playerLeft_px = playerCenter_px - (HITBOX_W_LOCAL / 2);
                        int playerRight_px = playerLeft_px + (HITBOX_W_LOCAL - 1);
                        int footWorldY_px = (playerY_fixed >> 8) + playerVisualHeight - 1;
                        int tileBelowY_world = footWorldY_px / TILE;
                        int groundRowsToReserve_local = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                        int tileIndexY = tileBelowY_world + groundRowsToReserve_local;
                        if (tileIndexY >= 0 && tileIndexY < mapHeight)
                        {
                            for (int tx = playerLeft_px / TILE; tx <= playerRight_px / TILE; tx++)
                            {
                                if (tx < 0 || tx >= mapWidth) continue;
                                int tid = tiles[tileIndexY * mapWidth + tx];
                                int useTidForAnim = MapAnimatedTileIndex(tid);
                                int collisionTid = useTidForAnim;
                                if (useTidForAnim >= 1000)
                                {
                                    if (useTidForAnim >= 1000 && useTidForAnim <= 1007)
                                        collisionTid = 0x08 + ((useTidForAnim - 1000) % 4);
                                    else if (useTidForAnim >= 1010 && useTidForAnim <= 1015)
                                    {
                                        int group = (useTidForAnim - 1010) % 3;
                                        collisionTid = (group == 0) ? 0x04 : (group == 1) ? 0x7D : 0x7F;
                                    }
                                    else if (useTidForAnim >= 1020 && useTidForAnim <= 1037)
                                        collisionTid = 0x74 + ((useTidForAnim - 1020) % 9);
                                    else
                                        collisionTid = tid;
                                }
                                var col = MetatileCollisionTable.GetCollision((byte)collisionTid);
                                int tileStartX = tx * TILE;
                                int localX = Math.Max(0, Math.Min(TILE - 1, playerCenter_px - tileStartX));
                                if (ProvidesFloorAtColumnStatic(col, localX, out int _)) { stillSupported = true; break; }
                            }
                        }
                    }

                    if (!stillSupported)
                    {
                        onGround = false;
                    }
                }
                catch { onGround = false; }
            }

            // If the player just crossed the interaction line this step, capture the
            // screen X (in pixels) where the interaction line appeared so the camera
            // can keep the player anchored there while the player continues moving.
            bool crossedInteraction = prevPlayerCenter_fixed < INTERACTION_LINE_FIXED && attemptedPlayerCenter_fixed >= INTERACTION_LINE_FIXED;
            if (crossedInteraction)
            {
                interactionScreenOffset_px = (INTERACTION_LINE_FIXED >> 8) - (cameraX_fixed >> 8);
            }

            // If the player's center is at/after the interaction line, make the camera
            // follow the player's world movement such that the player's screen X stays
            // at the recorded interaction offset. If no offset recorded, fall back to
            // keeping the player at the interaction line world X.
            int playerCenter_fixed_now = playerX_fixed + centerOffset_fixed;
            if (playerCenter_fixed_now >= INTERACTION_LINE_FIXED)
            {
                if (interactionScreenOffset_px >= 0)
                {
                    // camera = playerX - interactionScreenOffset
                    cameraX_fixed = playerX_fixed - (interactionScreenOffset_px << 8);
                }
                else
                {
                    // No recorded offset (edge case): keep player's center at interaction line
                    cameraX_fixed += attemptedPlayerCenter_fixed - INTERACTION_LINE_FIXED;
                }

                // clamp cameraX to map bounds
                int maxCamera_fixed = Math.Max(0, (mapWidth - NES_W) * TILE) << 8;
                if (cameraX_fixed < 0) cameraX_fixed = 0;
                if (cameraX_fixed > maxCamera_fixed) cameraX_fixed = maxCamera_fixed;
            }
            else
            {
                // player moved left of interaction line: clear recorded offset so crossing will re-capture
                interactionScreenOffset_px = -1;
            }

            // Advance animation frame counter - handled by UI render loop now

            // Vertical player movement & camera-follow behavior
            // Vertical step (2 pixels/frame) in fixed-point (8 fractional bits)
            const int vStep_fixed = 512; // 2 px/frame

            int maxCameraY_fixed = Math.Max(0, (mapHeight - NES_H) * TILE) << 8;
            int maxPlayerY_fixed = Math.Max(0, (mapHeight * TILE - playerVisualHeight)) << 8; // player cannot go below last tile row

            // Compute player's screen Y before movement (pixels)
            int playerScreenY_before = (playerY_fixed >> 8) - (cameraY_fixed >> 8);

            if (upHeld)
            {
                // In cam mode, only pan camera - no player movement or physics
                if (camModeActive)
                {
                    if (cameraY_fixed > 0)
                    {
                        cameraY_fixed -= vStep_fixed;
                        if (cameraY_fixed < 0) cameraY_fixed = 0;
                    }
                }
                else
                {
                    // Normal mode: player movement and camera following
                    // Use player's screen center Y for scrolling decisions to match camera panning behavior
                    int playerCenterScreenY = (playerY_fixed >> 8) + (playerVisualHeight / 2) - (cameraY_fixed >> 8);
                    int topThreshold = 5 * TILE; // 5 tiles from top
                    if (!jumpedOnce && camModeActive)
                    {
                        // Before first jump: Up is camera-only (do not move player)
                        if (cameraY_fixed > 0)
                        {
                            cameraY_fixed -= vStep_fixed;
                            if (cameraY_fixed < 0) cameraY_fixed = 0;
                        }
                    }
                    else
                    {
                        if (!physicsEnabled)
                        {
                            // Try move player up (only if cam mode active)
                            if (camModeActive)
                            {
                                playerY_fixed -= vStep_fixed;
                                if (playerY_fixed < 0) playerY_fixed = 0;

                                // Recompute center after moving the player
                                playerCenterScreenY = (playerY_fixed >> 8) + (playerVisualHeight / 2) - (cameraY_fixed >> 8);
                                // If player's center is at or above the threshold, scroll camera up to follow
                                if (playerCenterScreenY <= topThreshold)
                                {
                                    int need = topThreshold - playerCenterScreenY; // pixels camera should move up
                                    int camMove = Math.Min(need, (cameraY_fixed >> 8));
                                    cameraY_fixed -= (camMove << 8);
                                    if (cameraY_fixed < 0) cameraY_fixed = 0;
                                }
                            }
                        }
                        else
                        {
                            // Physics active: do not move player Y directly, but allow camera to scroll up (only if cam mode active)
                            if (camModeActive && playerCenterScreenY <= topThreshold)
                            {
                                int need = topThreshold - playerCenterScreenY;
                                int camMove = Math.Min(need, (cameraY_fixed >> 8));
                                cameraY_fixed -= (camMove << 8);
                                if (cameraY_fixed < 0) cameraY_fixed = 0;
                            }
                            else
                            {
                                // Manual camera pan while physics is enabled: allow small step when holding Up
                                if (camModeActive && cameraY_fixed > 0)
                                {
                                    cameraY_fixed -= vStep_fixed;
                                    if (cameraY_fixed < 0) cameraY_fixed = 0;
                                }
                            }
                        }
                    }
                }
            }

            if (downHeld)
            {
                // In cam mode, only pan camera - no player movement or physics
                if (camModeActive)
                {
                    if (cameraY_fixed < maxCameraY_fixed)
                    {
                        cameraY_fixed += vStep_fixed;
                        if (cameraY_fixed > maxCameraY_fixed) cameraY_fixed = maxCameraY_fixed;
                    }
                }
                else
                {
                    // Normal mode: player movement and camera following
                    // Attempt to move player down, but if within bottom threshold, prefer to scroll camera first
                    int bottomThreshold = NES_H * TILE - 5 * TILE; // 5 tiles from bottom measured against player center
                    int playerCenterScreenY_down = (playerY_fixed >> 8) + (playerVisualHeight / 2) - (cameraY_fixed >> 8);
                    // If before first jump, Down should pan camera only
                    if (!jumpedOnce && camModeActive)
                    {
                        if (cameraY_fixed < maxCameraY_fixed)
                        {
                            cameraY_fixed += vStep_fixed;
                            if (cameraY_fixed > maxCameraY_fixed) cameraY_fixed = maxCameraY_fixed;
                        }
                    }
                    else
                    {
                        // If player's center is above the bottom threshold, allow moving player down.
                        // If player's center has reached or passed the threshold, scroll the camera instead.
                        if (playerCenterScreenY_down < bottomThreshold)
                        {
                            if (!physicsEnabled && camModeActive)
                            {
                                // Safe to move player down without scrolling
                                // Advance player, then clamp to ground. Keep ordering consistent
                                playerY_fixed += vStep_fixed;
                                if (playerY_fixed > maxPlayerY_fixed) playerY_fixed = maxPlayerY_fixed;
                            }
                            else
                            {
                                // Physics active: do not move player Y directly when using down; let physics control position
                            }
                        }
                        else
                        {
                            // Player within 5 tiles of bottom: scroll camera down first until bottom reached
                            if (camModeActive && cameraY_fixed < maxCameraY_fixed)
                            {
                                // Scroll camera down and advance player world Y by same amount so
                                // Mark that we've jumped at least once
                                jumpedOnce = true;
                                // visually stationary while camera scrolls.
                                cameraY_fixed += vStep_fixed;
                                if (cameraY_fixed > maxCameraY_fixed) cameraY_fixed = maxCameraY_fixed;
                                // If physics not active, advance player so perceived speed matches upward movement
                                if (!physicsEnabled)
                                {
                                    playerY_fixed += vStep_fixed;
                                    if (playerY_fixed > maxPlayerY_fixed) playerY_fixed = maxPlayerY_fixed;
                                }
                            }
                            else
                            {
                                // Camera at bottom: allow player to move down to ground and clamp
                                if (!physicsEnabled)
                                {
                                    playerY_fixed += vStep_fixed;
                                    if (playerY_fixed > maxPlayerY_fixed) playerY_fixed = maxPlayerY_fixed;
                                }
                            }
                        }
                    }
                }
            }

                // Automatic camera-follow while physics is active in numeric path
                try
                {
                    if (physicsEnabled && jumpedOnce)
                    {
                        int playerCenterScreenY_post = (playerY_fixed >> 8) + (playerVisualHeight / 2) - (cameraY_fixed >> 8);
                        int topThreshold_post = 5 * TILE;
                        int bottomThreshold_post = NES_H * TILE - 5 * TILE;

                        if (playerCenterScreenY_post <= topThreshold_post)
                        {
                            int need = topThreshold_post - playerCenterScreenY_post;
                            int camMove = Math.Min(need, (cameraY_fixed >> 8));
                            cameraY_fixed -= (camMove << 8);
                            if (cameraY_fixed < 0) cameraY_fixed = 0;
                        }
                        else if (playerCenterScreenY_post >= bottomThreshold_post)
                        {
                            int need = playerCenterScreenY_post - bottomThreshold_post;
                            int maxCameraY_fixed_local = Math.Max(0, (mapHeight - NES_H) * TILE) << 8;
                            int camAvail = (maxCameraY_fixed_local - cameraY_fixed) >> 8;
                            int camMove = Math.Min(need, camAvail);
                            cameraY_fixed += (camMove << 8);
                            if (cameraY_fixed > maxCameraY_fixed_local) cameraY_fixed = maxCameraY_fixed_local;
                        }
                    }
                }
                catch { }

            // Apply simple physics if enabled: jump -> gravity -> cap -> integrate -> ground collision
            // Only run physics here when the numeric sim timer is not running to avoid double-applying
            if (physicsEnabled && simTimer == null)
            {
                try
                {
                    // Decrement grounded-stabilization counter each numeric step
                    if (groundStabilizeCounter > 0) groundStabilizeCounter--;
                    // Consume UI-frame jump press if present and on-ground (do this before gravity)
                    bool effectiveOnGround_local = onGround || groundStabilizeCounter > 0 || invertedCeilingHoldCounter > 0;
                    bool jumpAppliedThisFrame = false;
                    // Atomically grab and clear any pending UI edges
                    int pendingPress = Interlocked.Exchange(ref keyXPressedCount, 0);
                    if (pendingPress > 0)
                    {
                        // try { ProcessModeSimPendingPress(pendingPress, effectiveOnGround_local, ref jumpAppliedThisFrame); } catch { } // REMOVED - fresh port
                    }

                    // Do not age the jump-buffer here (would race with numeric sim).
                    // We'll atomically consume the buffer at the moment of landing below.

                    // Apply gravity only if we did not just apply a jump this frame and if moving vertically
                    // or sufficiently above ground (use epsilon). Also skip gravity while on a grounded surface
                    // to prevent small oscillations between 0 and a gravity increment.
                    if (!jumpAppliedThisFrame && !effectiveOnGround_local && (playerVelY_fixed != 0 || playerY_fixed < maxPlayerY_fixed - LAND_EPS_FIXED))
                    {
                        if (currentGameMode == 1)
                        {
                            try
                            {
                                // Determine whether gravity is "downwards" (positive Y) for numeric sign
                                int gravitySign = gravityReversed ? -1 : 1;
                                // canonical gravity flag already applied via gravityReversed

                                bool movingUpRelative = gravitySign > 0 ? (playerVelY_fixed < 0) : (playerVelY_fixed > 0);
                                bool xheld = IsXDownAsync() || keyXHeld;

                                int tmpMag = movingUpRelative ? (xheld ? SHIP_GRAVITY_HOLD_FALL : SHIP_GRAVITY_BASE)
                                                               : (xheld ? SHIP_GRAVITY_AFTER_HOLD : SHIP_GRAVITY);

                                int tmpgravity = tmpMag * gravitySign;
                                if (xheld) tmpgravity = -tmpgravity; // X = thrust opposite to gravity

                                try { playerVelY_fixed += (int)Math.Round(tmpgravity * simTimeScale * simTimeScale); } catch { playerVelY_fixed += tmpgravity; }

                                try
                                {
                                    // Enforce frame clamps per design: choose clamping bounds depending on gravity direction
                                    if (gravitySign < 0)
                                    {
                                        if (playerVelY_fixed < -SHIP_MAX_FALLSPEED) playerVelY_fixed = -SHIP_MAX_FALLSPEED;
                                        if (playerVelY_fixed > SHIP_MAX_FALLSPEED_HOLD) playerVelY_fixed = SHIP_MAX_FALLSPEED_HOLD;
                                    }
                                    else
                                    {
                                        if (playerVelY_fixed < -SHIP_MAX_FALLSPEED_HOLD) playerVelY_fixed = -SHIP_MAX_FALLSPEED_HOLD;
                                        if (playerVelY_fixed > SHIP_MAX_FALLSPEED) playerVelY_fixed = SHIP_MAX_FALLSPEED;
                                    }
                                }
                                catch { }
                            }
                            catch { try { playerVelY_fixed += (int)Math.Round(effectiveGravity_fixed * simTimeScale * simTimeScale); } catch { playerVelY_fixed += effectiveGravity_fixed; } }
                        }
                        else if (currentGameMode == 2)
                        {
                            try
                            {
                                // For ball mode, gravity direction is controlled by `ballGoingDown`.
                                int gravitySign = ballGoingDown ? 1 : -1;
                                if (gravityReversed) gravitySign = -gravitySign;
                                // canonical gravity flag already applied via gravityReversed

                                int tmpMag = BALL_GRAVITY;
                                int tmpgravity = tmpMag * gravitySign;

                                try { playerVelY_fixed += (int)Math.Round(tmpgravity * simTimeScale * simTimeScale); } catch { playerVelY_fixed += tmpgravity; }

                                try
                                {
                                    // Enforce sign-aware max-fall similar to other modes:
                                    // compute an effectiveMaxFall (positive when gravity pushes down, negative when up)
                                    int effectiveBallMaxFall = gravitySign >= 0 ? BALL_MAX_FALLSPEED : -BALL_MAX_FALLSPEED;
                                    if (effectiveBallMaxFall >= 0)
                                    {
                                        if (playerVelY_fixed > effectiveBallMaxFall) playerVelY_fixed = effectiveBallMaxFall;
                                    }
                                    else
                                    {
                                        if (playerVelY_fixed < effectiveBallMaxFall) playerVelY_fixed = effectiveBallMaxFall;
                                    }
                                }
                                catch { }
                            }
                            catch { try { playerVelY_fixed += (int)Math.Round(effectiveGravity_fixed * simTimeScale * simTimeScale); } catch { playerVelY_fixed += effectiveGravity_fixed; } }
                        }
                        else
                        {
                            try { playerVelY_fixed += (int)Math.Round(effectiveGravity_fixed * simTimeScale * simTimeScale); } catch { playerVelY_fixed += effectiveGravity_fixed; }
                            // cap velocity according to the sign of effectiveMaxFall_fixed
                            try
                            {
                                if (effectiveMaxFall_fixed >= 0)
                                {
                                    if (playerVelY_fixed > effectiveMaxFall_fixed) playerVelY_fixed = effectiveMaxFall_fixed;
                                }
                                else
                                {
                                    if (playerVelY_fixed < effectiveMaxFall_fixed) playerVelY_fixed = effectiveMaxFall_fixed;
                                }
                            }
                            catch { }
                        }
                    }

                    // integrate velocity
                    playerY_fixed += playerVelY_fixed;
                    // Prevent the player's world Y from going negative (above map top).
                    // The simulation uses pixel Y coordinates where 0 is the top of the world
                    // and increasing values go downwards; negative fixed-point Y can cause
                    // out-of-bounds tile lookups and incorrect floor detection.
                    if (playerY_fixed < 0) playerY_fixed = 0;

                    // Ceiling collision (UI-path): if moving up, check for tiles above player's head that should block upward movement.
                    try
                    {
                        if (playerVelY_fixed < 0)
                        {
                            const int HITBOX_W = 15;
                            int playerCenter_px = (playerX_fixed >> 8) + (playerVisualWidth / 2);
                            int playerLeft_px = playerCenter_px - (HITBOX_W / 2);
                            int playerRight_px = playerLeft_px + (HITBOX_W - 1);
                            int headWorldY_px = (playerY_fixed >> 8);

                            int tileAboveY_world = headWorldY_px / TILE;
                            int groundRowsToReserve_calc = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                            int tileIndexY = tileAboveY_world + groundRowsToReserve_calc;
                            if (tileIndexY >= 0 && tileIndexY < mapHeight)
                            {
                                int leftTileX = playerLeft_px / TILE;
                                int rightTileX = playerRight_px / TILE;
                                bool blocked = false;
                                int blockingTileWorldBottom_px = int.MaxValue;
                                for (int tx = leftTileX; tx <= rightTileX; tx++)
                                {
                                    if (tx < 0 || tx >= mapWidth) continue;
                                    int tid = tiles[tileIndexY * mapWidth + tx];
                                    int useTidForAnim = MapAnimatedTileIndex(tid);
                                    int collisionTid = useTidForAnim;
                                    if (useTidForAnim >= 1000)
                                    {
                                        if (useTidForAnim >= 1000 && useTidForAnim <= 1007)
                                        {
                                            collisionTid = 0x08 + ((useTidForAnim - 1000) % 4);
                                        }
                                        else if (useTidForAnim >= 1010 && useTidForAnim <= 1015)
                                        {
                                            int group = (useTidForAnim - 1010) % 3;
                                            collisionTid = (group == 0) ? 0x04 : (group == 1) ? 0x7D : 0x7F;
                                        }
                                        else if (useTidForAnim >= 1020 && useTidForAnim <= 1037)
                                        {
                                            collisionTid = 0x74 + ((useTidForAnim - 1020) % 9);
                                        }
                                        else
                                        {
                                            collisionTid = tid;
                                        }
                                    }
                                    var col = MetatileCollisionTable.GetCollision((byte)collisionTid);
                                    int tileStartX = tx * TILE;
                                    int localLeft = Math.Max(0, playerLeft_px - tileStartX);
                                    int localRight = Math.Min(TILE - 1, playerRight_px - tileStartX);
                                    for (int lx = localLeft; lx <= localRight; lx++)
                                    {
                                        // When NoDeath is OFF, allow the player's right side to pass through tiles.
                                        // Skip checking any columns that are on or to the right of the player's visual center.
                                        if (!MainWindow.Option_NoDeath)
                                        {
                                            int worldPx = tileStartX + lx;
                                            if (worldPx >= playerCenter_px) continue;
                                        }
                                        if (BlocksCeilingAtColumn(col, lx))
                                        {
                                            blocked = true;
                                            int tileWorldBottom_px = (tileAboveY_world + 1) * TILE;
                                            if (tileWorldBottom_px < blockingTileWorldBottom_px) blockingTileWorldBottom_px = tileWorldBottom_px;
                                            break;
                                        }
                                    }
                                    if (blocked) break;
                                }

                                            if (blocked && blockingTileWorldBottom_px < int.MaxValue)
                                            {
                                                // try { Cube_HandleCeilingCollision_NoLocals(); } catch { } // REMOVED - fresh port
                                            }
                            }
                        }
                    }
                    catch { }

                    // ground collision: try tile-based floor collision first, otherwise fall back to map bottom
                    try
                    {
                        // Hitbox dimensions based on mini mode
                        int HITBOX_W = miniMode ? 8 : 15;
                        int HITBOX_H = miniMode ? 7 : 15;

                        int playerCenter_px = (playerX_fixed >> 8) + (playerVisualWidth / 2);
                        int playerLeft_px = playerCenter_px - (HITBOX_W / 2);
                        int playerRight_px = playerLeft_px + (HITBOX_W - 1);
                        
                        // Calculate foot position accounting for mini mode offset
                        int footWorldY_px = (playerY_fixed >> 8);
                        if (miniMode && !gravityFlipped)
                        {
                            footWorldY_px += 9; // Mini hitbox starts at +9 in normal gravity
                        }
                        footWorldY_px += HITBOX_H; // Add height to get bottom edge

                        int tileBelowY = footWorldY_px / TILE;
                        // If a ground image layer is present we reserve a few bottom rows for ground
                        // rendering. The visible map Y is offset by those reserved rows, so when
                        // converting a world tile Y to the tiles[] map index we must add the
                        // reserved ground rows. Compute the same reservation used by the renderer.
                        try
                        {
                            int groundRowsToReserve_calc = 0;
                            if (hasGroundLayer && groundTileRows > 0) groundRowsToReserve_calc = Math.Min(3, groundTileRows);
                            tileBelowY = tileBelowY + groundRowsToReserve_calc;
                        }
                        catch { }
                        int floorDetected = 0; // 0=false, 1=true
                        int floorTopWorldY_px = (mapHeight * TILE); // default to map bottom

                        // Delegate to centralized helper for floor checks.
                        bool ProvidesFloorAtColumn(MetatileCollision col, int localX, out int topOffsetPx)
                        {
                            return ProvidesFloorAtColumnStatic(col, localX, out topOffsetPx);
                        }

                            if (tileBelowY >= 0 && tileBelowY < mapHeight)
                        {
                            // For all tiles under the player's horizontal span, check a small vertical
                            // window of tiles (the tile under the feet and the one above) so slabs
                            // that occupy the top half of a tile are detected correctly.
                            int leftTileX = playerLeft_px / TILE;
                            int rightTileX = playerRight_px / TILE;

                            int groundRowsToReserve_calc = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                            int startRow = Math.Max(0, tileBelowY - 1);
                            int endRow = Math.Min(mapHeight - 1, tileBelowY);

                            for (int ty = startRow; ty <= endRow; ty++)
                            {
                                for (int tx = leftTileX; tx <= rightTileX; tx++)
                                {
                                    if (tx < 0 || tx >= mapWidth) continue;
                                    int tid = tiles[ty * mapWidth + tx];

                                    int useTidForAnim = MapAnimatedTileIndex(tid);
                                    int collisionTid = useTidForAnim;
                                    if (useTidForAnim >= 1000)
                                    {
                                        if (useTidForAnim >= 1000 && useTidForAnim <= 1007)
                                        {
                                            collisionTid = 0x08 + ((useTidForAnim - 1000) % 4);
                                        }
                                        else if (useTidForAnim >= 1010 && useTidForAnim <= 1015)
                                        {
                                            int group = (useTidForAnim - 1010) % 3; // 0 -> 0x04, 1 -> 0x7D, 2 -> 0x7F
                                            collisionTid = (group == 0) ? 0x04 : (group == 1) ? 0x7D : 0x7F;
                                        }
                                        else if (useTidForAnim >= 1020 && useTidForAnim <= 1037)
                                        {
                                            collisionTid = 0x74 + ((useTidForAnim - 1020) % 9);
                                        }
                                        else
                                        {
                                            collisionTid = tid;
                                        }
                                    }
                                    var col = MetatileCollisionTable.GetCollision((byte)collisionTid);

                                    int tileStartX = tx * TILE;
                                    int localLeft = Math.Max(0, playerLeft_px - tileStartX);
                                    int localRight = Math.Min(TILE - 1, playerRight_px - tileStartX);

                                    int bestTopOffset = int.MaxValue;
                                    bool any = false;
                                    for (int lx = localLeft; lx <= localRight; lx++)
                                    {
                                        // Prefer detecting a local floor first (so bottom-half slabs are honored).
                                        if (ProvidesFloorAtColumn(col, lx, out int off))
                                        {
                                            any = true;
                                            if (off < bestTopOffset) bestTopOffset = off;
                                            continue;
                                        }

                                        // If no local floor found at this column and deaths are enabled,
                                        // skip columns on/after the player's visual center to allow
                                        // right-edge passthrough. This avoids ignoring actual floors.
                                        if (!MainWindow.Option_NoDeath)
                                        {
                                            int worldPx = tileStartX + lx;
                                            if (worldPx >= playerCenter_px) continue;
                                        }
                                    }
                                    if (any)
                                    {
                                        int candidateTop = (ty - groundRowsToReserve_calc) * TILE + bestTopOffset;
                                        if (candidateTop < floorTopWorldY_px) floorTopWorldY_px = candidateTop;
                                        floorDetected = 1;
                                    }
                                }
                            }
                        }

                        // If no tile-based floor found but a ground layer exists, treat the top
                        // of the reserved ground rows as a solid floor so landing on ground counts.
                        if (floorDetected == 0)
                        {
                            int groundRowsToReserve_calc2 = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                            if (groundRowsToReserve_calc2 > 0)
                            {
                                int topOfGround_px = (mapHeight - groundRowsToReserve_calc2) * TILE;
                                if (topOfGround_px < floorTopWorldY_px) floorTopWorldY_px = topOfGround_px;
                                floorDetected = 1;
                            }
                        }

                        int floorTop_fixed = (floorDetected == 1) ? ((floorTopWorldY_px - HITBOX_H) << 8) : maxPlayerY_fixed;

                        // Per-frame runtime diagnostics: print the tiles under the player's feet
                        if (runtimePerFrameLog)
                        {
                            try
                            {
                                var sb = new System.Text.StringBuilder();
                                sb.AppendFormat("SIM_FRAME: playerX_px={0} playerY_px={1} footY_px={2} tileBelowY={3}; ", (playerX_fixed >> 8), (playerY_fixed >> 8), footWorldY_px, tileBelowY);
                                int dbgLeft = playerLeft_px / TILE;
                                int dbgRight = playerRight_px / TILE;
                                if (tileBelowY < 0 || tileBelowY >= mapHeight)
                                {
                                    sb.AppendFormat("tileBelowY={0}:out_of_bounds; ", tileBelowY);
                                }
                                else
                                {
                                    for (int tx_dbg = dbgLeft; tx_dbg <= dbgRight; tx_dbg++)
                                    {
                                        if (tx_dbg < 0 || tx_dbg >= mapWidth) { sb.AppendFormat("tx={0}:out_of_bounds; ", tx_dbg); continue; }
                                        int tid_dbg = tiles[tileBelowY * mapWidth + tx_dbg];
                                        int useTid_dbg = MapAnimatedTileIndex(tid_dbg);
                                        int collisionTid_dbg = useTid_dbg;
                                        if (useTid_dbg >= 1000)
                                        {
                                            if (useTid_dbg >= 1000 && useTid_dbg <= 1007)
                                            {
                                                collisionTid_dbg = 0x08 + ((useTid_dbg - 1000) % 4);
                                            }
                                            else if (useTid_dbg >= 1010 && useTid_dbg <= 1015)
                                            {
                                                int group_dbg = (useTid_dbg - 1010) % 3;
                                                collisionTid_dbg = (group_dbg == 0) ? 0x04 : (group_dbg == 1) ? 0x7D : 0x7F;
                                            }
                                            else if (useTid_dbg >= 1020 && useTid_dbg <= 1037)
                                            {
                                                collisionTid_dbg = 0x74 + ((useTid_dbg - 1020) % 9);
                                            }
                                            else
                                            {
                                                collisionTid_dbg = tid_dbg;
                                            }
                                        }
                                        var col_dbg = MetatileCollisionTable.GetCollision((byte)collisionTid_dbg);
                                        sb.AppendFormat("tx={0} tid={1} animRemap={2} collTid=0x{3:X2} coll={4}; ", tx_dbg, tid_dbg, useTid_dbg, collisionTid_dbg, col_dbg);
                                    }
                                }
                                var line = sb.ToString();
                                System.Console.WriteLine(line);
                                try
                                {
                                    System.IO.File.AppendAllText(simDebugFilePath, DateTime.UtcNow.ToString("o") + " " + line + Environment.NewLine);
                                }
                                catch { }
                            }
                            catch { }
                        }
                        else
                        {
                            // If not running runtime diagnostics, perform landing resolution here
                            // by atomically consuming any jump buffer and snapping the player to floor.
                            int buffered_ui = Interlocked.Exchange(ref jumpBufferCounter, 0);
                            // Also read the raw edge-press counter without clearing it so held/edge presses
                            // recorded by the UI poll are considered when landing.
                            int pendingPressesNow = Interlocked.CompareExchange(ref keyXPressedCount, 0, 0);
                            if (!gravityReversed)
                            {
                                if (playerY_fixed >= floorTop_fixed - LAND_EPS_FIXED && playerVelY_fixed >= 0)
                                {
                                    int nudged = floorTop_fixed - (1 << 8);
                                    if (nudged < 0) nudged = 0;
                                    playerY_fixed = nudged;
                                    playerVelY_fixed = 0;
                                    onGround = true;
                                    
                                    groundStabilizeCounter = 2;

                                            // try { Cube_HandleUILanding_NoLocals(); } catch { } // REMOVED - fresh port

                                            // Ball handling (non-cube modes)
                                            if (currentGameMode != 0)
                                            {
                                                int buffered_ball_ui = Interlocked.Exchange(ref ballToggleRequested, 0);
                                                if (currentGameMode == 2 && buffered_ball_ui > 0)
                                                {
                                                    ballGoingDown = !ballGoingDown;
                                                    try { playerVelY_fixed = (int)Math.Round((ballGoingDown ? BALL_IMMEDIATE_VEL : -BALL_IMMEDIATE_VEL) * simTimeScale); } catch { playerVelY_fixed = ballGoingDown ? BALL_IMMEDIATE_VEL : -BALL_IMMEDIATE_VEL; }
                                                    onGround = false;
                                                    jumpedOnce = true;
                                                    LogBallEvent($"UI-Landing: consumed queued toggle; ballGoingDown={ballGoingDown}");
                                                }
                                            }
                                            
                                }
                                else
                                {
                                    onGround = false;
                                    groundStabilizeCounter = 0;
                                }
                            }
                            else
                            {
                                // Reversed gravity: landing occurs when moving upward and reaching the floor from below
                                if (playerY_fixed <= floorTop_fixed + LAND_EPS_FIXED && playerVelY_fixed <= 0)
                                {
                                    int nudged = floorTop_fixed + (1 << 8);
                                    playerY_fixed = nudged;
                                    playerVelY_fixed = 0;
                                    onGround = true;

                                    // try { Cube_HandleUILanding_NoLocals(); } catch { } // REMOVED - fresh port

                                    if (currentGameMode != 0)
                                    {
                                        int buffered_ball_ui = Interlocked.Exchange(ref ballToggleRequested, 0);
                                        if (currentGameMode == 2 && buffered_ball_ui > 0)
                                        {
                                            ballGoingDown = !ballGoingDown;
                                            try { playerVelY_fixed = (int)Math.Round((ballGoingDown ? BALL_IMMEDIATE_VEL : -BALL_IMMEDIATE_VEL) * simTimeScale); } catch { playerVelY_fixed = ballGoingDown ? BALL_IMMEDIATE_VEL : -BALL_IMMEDIATE_VEL; }
                                            onGround = false;
                                            jumpedOnce = true;
                                            LogBallEvent($"UI-ReversedLanding: consumed queued toggle; ballGoingDown={ballGoingDown}");
                                        }
                                    }
                                }
                                else
                                {
                                    onGround = false;
                                }
                            }
                        }
                    }
                    catch { /* keep previous behavior on error */ }
                }
                catch { }
            }

            // Clamp cameraY to valid range after adjustments
            if (cameraY_fixed < 0) cameraY_fixed = 0;
            if (cameraY_fixed > maxCameraY_fixed) cameraY_fixed = maxCameraY_fixed;

            // Final safety clamp: ensure player remains above ground after camera moves
            if (playerY_fixed > maxPlayerY_fixed) { playerY_fixed = maxPlayerY_fixed; playerVelY_fixed = 0; }

            // Additional screen-space enforcement: ensure at least 3 rows of ground remain visible
            // Skip this enforcement when gravity is inverted (allow jumping into ground rows from ceiling)
            try
            {
                if (currplayer_gravity == 0) // Only enforce for normal gravity
                {
                    int playerScreenY_now = (playerY_fixed >> 8) - (cameraY_fixed >> 8) + gridRenderShiftYPx;
                    int allowedBottom_px = (NES_H * TILE) - (3 * TILE); // require 3 rows visible
                    int playerScreenBottom = playerScreenY_now + playerVisualHeight;
                    if (playerScreenBottom > allowedBottom_px)
                    {
                        int desiredPlayerScreenY = allowedBottom_px - playerVisualHeight;
                        int desiredPlayerWorldY = desiredPlayerScreenY + (cameraY_fixed >> 8) - gridRenderShiftYPx;
                        if (desiredPlayerWorldY < 0) desiredPlayerWorldY = 0;
                        int desiredPlayerY_fixed = desiredPlayerWorldY << 8;
                        if (desiredPlayerY_fixed > maxPlayerY_fixed) desiredPlayerY_fixed = maxPlayerY_fixed;
                        playerY_fixed = desiredPlayerY_fixed;
                        // When the UI enforces a screen-space clamp we should treat the player as effectively grounded
                        // (prevent further gravity) and zero vertical velocity so the player doesn't sink while camera constraints apply.
                        playerVelY_fixed = 0;
                        onGround = true;
                    }
                }
            }
            catch { }

            // After moving camera X, check for speed-portal anchor crossings
            try
            {
                // remember previous tints so we can detect changes
                var prevBackgroundTint = backgroundTint;
                var prevTileTint = tileTint;
                var prevGroundTint = groundTint;
                // remember previous forced-black flag so we don't regenerate parallax
                // on unrelated ground-only changes unless the forced-black state toggles
                var prevBackgroundForceSolidBlack = backgroundForceSolidBlack;
                int center_fixed = cameraX_fixed + ((NES_W * TILE / 2) << 8);
                // We moved right relative to world; detect triggers either from a player crossing
                // the fixed interaction line, or from camera movement when the player is already past it.
                int bestAnchor_fixed = int.MaxValue;
                int? newSpeed_fixed = null;

                if (crossedInteraction)
                {
                    // Player crossed the interaction line this step: consider anchors between the
                    // previous player center and the fixed interaction line.
                    for (int idx = 0; idx < sprites.Length; idx++)
                    {
                        int sid = sprites[idx];
                        if (sid < 0) continue;
                        // Portal handling: All 9 gamemode portals
                        try
                        {
                            if (sid == 0x00 || sid == 0x01 || sid == 0x02 || sid == 0x03 || sid == 0x04 || sid == 0x17 || sid == 0x24 || sid == 0x4B || sid == 0x58 || sid == 0x6A || sid == 0x6B || sid == 0x6C)
                            {
                                // Require actual 2D AABB overlap between player hitbox and sprite hitbox
                                const int HITBOX_W = 15; const int HITBOX_H = 15;
                                int playerCenter_px_now = (playerX_fixed >> 8) + (playerVisualWidth / 2);
                                int playerLeft_px_now = playerCenter_px_now - (HITBOX_W / 2);
                                int playerRight_px_now = playerLeft_px_now + (HITBOX_W - 1);
                                int playerTop_px_now = (playerY_fixed >> 8);
                                // Apply mini mode offset: bottom-left for normal, top-left for inverted
                                if (miniMode)
                                {
                                    if (!gravityFlipped)
                                        playerTop_px_now += 9;
                                }
                                int playerBottom_px_now = playerTop_px_now + (HITBOX_H - 1);

                                if (SpriteIntersectsPlayer(idx, sid, playerLeft_px_now, playerRight_px_now, playerTop_px_now, playerBottom_px_now))
                                {
                                    try
                                    {
                                        int oldMode = currentGameMode;
                                        int newMode = sid switch {
                                            0x00 => 0, // Cube
                                            0x01 => 1, // Ship
                                            0x02 => 2, // Ball
                                            0x03 => 3, // UFO
                                            0x04 => 4, // Robot
                                            0x17 => 5, // Spider
                                            0x24 => 6, // Wave
                                            0x4B => 7, // Swing
                                            0x58 => 8, // Ninja
                                            0x6A => 9, // Pogo
                                            0x6B => 10, // Snake
                                            0x6C => 11, // Football
                                            _ => currentGameMode
                                        };
                                        if (newMode != oldMode)
                                        {
                                            currentGameMode = newMode;
                                            try { UpdateGameModeDisplay(); } catch { }
                                            try { UpdateEffectiveGravity(); } catch { }
                                            try { playerVelY_fixed = playerVelY_fixed / 2; } catch { }
                                        }
                                        try { UpdatePlayerImageForMode(); } catch { }
                                    }
                                    catch { }
                                    break;
                                }
                            }
                        }
                        catch { }
                        
                        // Speed portal handling
                        if (!speedPortalMap.ContainsKey(sid)) continue;

                        int anchorTileX = (spriteAnchors != null && spriteAnchors.TryGetValue(idx, out var a)) ? a.anchorTileX : idx % mapWidth;
                        int anchorX_center_fixed = ((anchorTileX * TILE) + (TILE / 2)) << 8;

                        // When cam mode is OFF, use collision-based activation like gamemode/gravity portals
                        if (!camModeActive)
                        {
                            // Require actual 2D AABB overlap between player hitbox and sprite hitbox
                            const int HITBOX_W_SPEED = 15; const int HITBOX_H_SPEED = 15;
                            int playerCenter_px_speed = (playerX_fixed >> 8) + (playerVisualWidth / 2);
                            int playerLeft_px_speed = playerCenter_px_speed - (HITBOX_W_SPEED / 2);
                            int playerRight_px_speed = playerLeft_px_speed + (HITBOX_W_SPEED - 1);
                            int playerTop_px_speed = (playerY_fixed >> 8);
                            int playerBottom_px_speed = playerTop_px_speed + (HITBOX_H_SPEED - 1);
                            
                            if (SpriteIntersectsPlayer(idx, sid, playerLeft_px_speed, playerRight_px_speed, playerTop_px_speed, playerBottom_px_speed))
                            {
                                if (!processedSpeedPortals.Contains(idx))
                                {
                                    if (anchorX_center_fixed < bestAnchor_fixed)
                                    {
                                        bestAnchor_fixed = anchorX_center_fixed;
                                        newSpeed_fixed = speedPortalMap[sid];
                                        processedSpeedPortals.Add(idx);
                                    }
                                }
                            }
                            else
                            {
                                if (processedSpeedPortals.Contains(idx)) processedSpeedPortals.Remove(idx);
                            }
                        }
                        // When cam mode is ON, use screen threshold logic
                        else if (crossedInteraction)
                        {
                            if (anchorX_center_fixed > prevPlayerCenter_fixed && anchorX_center_fixed <= INTERACTION_LINE_FIXED)
                            {
                                if (!processedSpeedPortals.Contains(idx))
                                {
                                    if (anchorX_center_fixed < bestAnchor_fixed)
                                    {
                                        bestAnchor_fixed = anchorX_center_fixed;
                                        newSpeed_fixed = speedPortalMap[sid];
                                        processedSpeedPortals.Add(idx);
                                    }
                                }
                            }
                            else
                            {
                                if (processedSpeedPortals.Contains(idx) && anchorX_center_fixed > INTERACTION_LINE_FIXED) processedSpeedPortals.Remove(idx);
                            }
                        }
                        else
                        {
                            if (anchorX_center_fixed <= center_fixed)
                            {
                                if (!processedSpeedPortals.Contains(idx))
                                {
                                    if (anchorX_center_fixed < bestAnchor_fixed)
                                    {
                                        bestAnchor_fixed = anchorX_center_fixed;
                                        newSpeed_fixed = speedPortalMap[sid];
                                        processedSpeedPortals.Add(idx);
                                    }
                                }
                            }
                            else
                            {
                                if (processedSpeedPortals.Contains(idx)) processedSpeedPortals.Remove(idx);
                            }
                        }
                    }
                }
                else
                {
                    // Player already past interaction line
                    // When cam mode is ON: use camera-centered detection (screen threshold)
                    // When cam mode is OFF: still use collision detection
                    if (camModeActive)
                    {
                        for (int idx = 0; idx < sprites.Length; idx++)
                        {
                            int sid = sprites[idx];
                            if (sid < 0) continue;
                            if (!speedPortalMap.ContainsKey(sid)) continue;

                            int anchorTileX = (spriteAnchors != null && spriteAnchors.TryGetValue(idx, out var a)) ? a.anchorTileX : idx % mapWidth;
                            int anchorX_center_fixed = ((anchorTileX * TILE) + (TILE / 2)) << 8;

                            // Require actual 2D overlap with player before selecting this portal
                            const int HITBOX_W_LOCAL = 15; const int HITBOX_H_LOCAL = 15;
                            int playerCenter_px_local = (playerX_fixed >> 8) + (playerVisualWidth / 2);
                            int playerLeft_px_local = playerCenter_px_local - (HITBOX_W_LOCAL / 2);
                            int playerRight_px_local = playerLeft_px_local + (HITBOX_W_LOCAL - 1);
                            int playerTop_px_local = (playerY_fixed >> 8);
                            int playerBottom_px_local = playerTop_px_local + (HITBOX_H_LOCAL - 1);

                            if (SpriteIntersectsPlayer(idx, sid, playerLeft_px_local, playerRight_px_local, playerTop_px_local, playerBottom_px_local))
                            {
                                if (anchorX_center_fixed < bestAnchor_fixed)
                                {
                                    bestAnchor_fixed = anchorX_center_fixed;
                                    newSpeed_fixed = speedPortalMap[sid];
                                }
                            }
                        }
                    }
                }

                // Also detect color-trigger crossings. To reduce sampling cost, only sample a trigger
                // once when it first moves past the interaction/center line. Keep a set of processed anchors so
                // we don't resample every frame while the trigger remains past center.
                int bestBg_fixed = int.MaxValue; int? bgIdx = null; int? bgSid = null;
                int bestTile_fixed = int.MaxValue; int? tileIdx = null; int? tileSid = null;
                int bestGround_fixed = int.MaxValue; int? groundIdx = null; int? groundSid = null;

                for (int idx = 0; idx < sprites.Length; idx++)
                {
                    int sid = sprites[idx];
                    if (sid < 0) continue;

                    // Check if this sprite is a color trigger we care about
                    if (!IsColorTriggerSprite(sid)) continue;

                    // Determine anchor tile X for this sprite: prefer explicit anchor, otherwise use tile position
                    int anchorTileX;
                    if (spriteAnchors != null && spriteAnchors.TryGetValue(idx, out var a2))
                        anchorTileX = a2.anchorTileX;
                    else
                        anchorTileX = idx % mapWidth;

                    int anchorX_center_fixed = ((anchorTileX * TILE) + (TILE / 2)) << 8;

                    if (crossedInteraction)
                    {
                        // During a player crossing, only consider anchors between previous player center and the fixed interaction line.
                        if (anchorX_center_fixed > prevPlayerCenter_fixed && anchorX_center_fixed <= INTERACTION_LINE_FIXED)
                        {
                            if (IsBackgroundTrigger(sid))
                            {
                                if (anchorX_center_fixed < bestBg_fixed) { bestBg_fixed = anchorX_center_fixed; bgIdx = idx; bgSid = sid; }
                            }
                            else if (IsTileTrigger(sid))
                            {
                                if (anchorX_center_fixed < bestTile_fixed) { bestTile_fixed = anchorX_center_fixed; tileIdx = idx; tileSid = sid; }
                            }
                            else if (IsGroundTrigger(sid))
                            {
                                if (anchorX_center_fixed < bestGround_fixed) { bestGround_fixed = anchorX_center_fixed; groundIdx = idx; groundSid = sid; }
                            }
                        }
                        else
                        {
                            // Anchor is to the right of the interaction line; clear processed flags so they can trigger again when recrossed
                            if (processedColorTriggers.Contains(idx) && anchorX_center_fixed > INTERACTION_LINE_FIXED) processedColorTriggers.Remove(idx);
                            if (processedGravityPortals.Contains(idx) && anchorX_center_fixed > INTERACTION_LINE_FIXED) processedGravityPortals.Remove(idx);
                            if (processedOrbs.Contains(idx) && anchorX_center_fixed > INTERACTION_LINE_FIXED) processedOrbs.Remove(idx);
                            if (processedSpeedPortals.Contains(idx) && anchorX_center_fixed > INTERACTION_LINE_FIXED) processedSpeedPortals.Remove(idx);
                            if (processedSpeedPortals.Contains(idx) && anchorX_center_fixed > INTERACTION_LINE_FIXED) processedSpeedPortals.Remove(idx);
                        }
                    }
                    else
                    {
                        // Camera-centered detection (player already past interaction line)
                        if (anchorX_center_fixed <= center_fixed)
                        {
                            if (processedColorTriggers.Contains(idx))
                            {
                                // already handled previously
                            }
                            else
                            {
                                if (IsBackgroundTrigger(sid))
                                {
                                    if (anchorX_center_fixed < bestBg_fixed) { bestBg_fixed = anchorX_center_fixed; bgIdx = idx; bgSid = sid; }
                                }
                                else if (IsTileTrigger(sid))
                                {
                                    if (anchorX_center_fixed < bestTile_fixed) { bestTile_fixed = anchorX_center_fixed; tileIdx = idx; tileSid = sid; }
                                }
                                else if (IsGroundTrigger(sid))
                                {
                                    if (anchorX_center_fixed < bestGround_fixed) { bestGround_fixed = anchorX_center_fixed; groundIdx = idx; groundSid = sid; }
                                }
                            }
                        }
                        else
                        {
                            // Anchor is left-of-center; clear any processed flag so it can be processed again if recrossed
                            if (processedColorTriggers.Contains(idx)) processedColorTriggers.Remove(idx);
                        }
                    }
                }

                // Apply detected color triggers: compute color and set tints accordingly. Mark anchors processed
                try
                {
                        if (bgIdx.HasValue && bgSid.HasValue)
                    {
                        var c = ColorFromTrigger(bgSid.Value);
                            backgroundTint = c; // update field used for drawing background
                            // Force solid-black background for 0x8F (explicit black background trigger)
                            backgroundForceSolidBlack = (bgSid.Value == 0x8F);
                        processedColorTriggers.Add(bgIdx.Value);
                        if (enableSimulatorDebugLogging && !triggerLogged.Contains(bgIdx.Value))
                        {
                            WriteTempLog($"Simulator: Applied background trigger at idx={bgIdx.Value} sid=0x{bgSid.Value:X} color={c}");
                            triggerLogged.Add(bgIdx.Value);
                        }
                    }
                    if (tileIdx.HasValue && tileSid.HasValue)
                    {
                        var c = ColorFromTrigger(tileSid.Value);
                        tileTint = c;
                        processedColorTriggers.Add(tileIdx.Value);
                        if (enableSimulatorDebugLogging && !triggerLogged.Contains(tileIdx.Value))
                        {
                            WriteTempLog($"Simulator: Applied tile trigger at idx={tileIdx.Value} sid=0x{tileSid.Value:X} color={c}");
                            triggerLogged.Add(tileIdx.Value);
                        }
                    }
                    if (groundIdx.HasValue && groundSid.HasValue)
                    {
                        // Compute ground tint from trigger. Special-case: when the trigger
                        // is the black-ground trigger (0xCF) we want the ground to become
                        // solid black (preserving the top white seam). We avoid forcing
                        // 0xCF globally in ColorFromTrigger so sprites/icons remain correct;
                        // here only the ground tint is made pure black to produce the
                        // expected visual (CreateBlackMaskedImages will be used below).
                        var c = ColorFromTrigger(groundSid.Value);
                        if (groundSid.Value == 0xCF)
                        {
                            // Special-case 0xCF: force the ground tint to pure black so the
                            // two-tone/ground mapping produces solid black bodies while
                            // allowing near-white seams to be recolored by object tints.
                            groundTint = Color.FromArgb(255, 0, 0, 0);
                        }
                        else
                        {
                            groundTint = c;
                        }
                        processedColorTriggers.Add(groundIdx.Value);
                        if (enableSimulatorDebugLogging && !triggerLogged.Contains(groundIdx.Value))
                        {
                            WriteTempLog($"Simulator: Applied ground trigger at idx={groundIdx.Value} sid=0x{groundSid.Value:X} color={c}");
                            triggerLogged.Add(groundIdx.Value);
                        }
                    }
                }
                catch { }

                // If any tint changed, regenerate toned tile images and invalidate tile-layer cache
                try
                {
                    // Compute an outline tint parameter for this regeneration. If a startup-origin
                    // tint was applied, keep outline tint transparent so outlines are not recolored.
                    var outlineTintParam = startupTintApplied ? Color.FromArgb(0, 0, 0, 0) : tileTint;

                    bool bgChanged = !AreColorsEqual(prevBackgroundTint, backgroundTint);
                    bool tileChanged = !AreColorsEqual(prevTileTint, tileTint);
                    bool grdChanged = !AreColorsEqual(prevGroundTint, groundTint);

                    if (bgChanged || tileChanged || grdChanged)
                    {
                        // Compute an effective outline tint for this regeneration so startup-applied
                        // tints do not recolor object outlines.
                        var outlineTintLocal = startupTintApplied ? Color.FromArgb(0, 0, 0, 0) : tileTint;
                        
                        // OPTIMIZATION: Only regenerate tile tint if tile actually changed
                        // This prevents expensive tile regeneration when only background or ground changed
                        if (tileChanged)
                        {
                            UpdateTonedImagesForTileTint(tileTint, outlineTintLocal);
                        }

                        // Only regenerate parallax when the background actually changed or when
                        // the forced-black flag toggled. This prevents ground-only triggers from
                        // accidentally replacing two-tone/parallax backgrounds.
                        try
                        {
                            if (bgChanged || prevBackgroundForceSolidBlack != backgroundForceSolidBlack)
                            {
                                if (backgroundForceSolidBlack)
                                {
                                    // When explicitly forced by 0x8F, do not use image-derived parallax; render solid black instead
                                    parallaxTonedImages = null;
                                }
                                else if (backgroundTint.A == 255 && backgroundTint.R == 0 && backgroundTint.G == 0 && backgroundTint.B == 0)
                                {
                                    parallaxTonedImages = CreateBlackMaskedImages(parallaxImages);
                                }
                                else
                                {
                                    parallaxTonedImages = CreateHueShiftedImages(parallaxImages, backgroundTint);
                                }
                            }
                        }
                        catch { parallaxTonedImages = parallaxImages; }

                        // Regenerate ground visuals when the ground tint changed OR when
                        // an object/tile tint changed (object triggers recolor the seam outline).
                        try
                        {
                            if (grdChanged || tileChanged)
                            {
                                if (groundTint.A == 255 && groundTint.R == 0 && groundTint.G == 0 && groundTint.B == 0)
                                {
                                    // For 0xCF (black-ground) produce black-masked images so
                                    // non-white/non-player-green pixels become black while
                                    // leaving the seam unaffected (outline recolor is disabled).
                                    try { groundTonedImages = CreateBlackMaskedExceptColorArray(groundImages, playerPlaceholderGreen, outlineTintLocal, false); }
                                    catch { groundTonedImages = CreateTwoToneTileImages(groundImages, Color.FromArgb(255, 0, 0, 0), Color.FromArgb(255, 0, 0, 0), outlineTintLocal); }
                                }
                                else
                                {
                                    groundTonedImages = CreateHueShiftedImages(groundImages, groundTint, outlineTintLocal);
                                }
                            }
                        }
                        catch { groundTonedImages = groundImages; }

                        // Forced-outline recolor fallback: if an outline tint is active for this regeneration,
                        // ensure thin/anti-aliased ground seams are recolored to match other tiles.
                        try
                        {
                            if (outlineTintParam.A > 0 && groundTonedImages != null)
                            {
                                var recol = CreateOutlineTintedTileImages(groundTonedImages, outlineTintParam);
                                if (recol != null) groundTonedImages = recol;
                            }
                        }
                        catch { }

                        // Also create/update a tinted full-parallax bitmap if a full parallax bitmap was provided
                        // Only update the full parallax bitmap when the background actually changed or when
                        // there is an explicit palette mapping for background, or when forced-black toggled.
                        try
                        {
                            if (parallaxBitmap != null && (bgChanged || prevBackgroundForceSolidBlack != backgroundForceSolidBlack))
                            {
                                ImageSource?[]? arr = null;
                                if (backgroundTint.A == 255 && backgroundTint.R == 0 && backgroundTint.G == 0 && backgroundTint.B == 0)
                                    arr = CreateBlackMaskedImages(new ImageSource?[] { parallaxBitmap! });
                                else
                                    arr = CreateHueShiftedImages(new ImageSource?[] { parallaxBitmap! }, backgroundTint);

                                if (arr != null && arr.Length > 0 && arr[0] != null) parallaxBitmapToned = arr[0];
                                else parallaxBitmapToned = parallaxBitmap;
                            }
                            else
                            {
                                // If we are not updating the parallax bitmap due to an unrelated ground change,
                                // leave the previous toned bitmap as-is so background visuals remain stable.
                            }
                        }
                        catch { parallaxBitmapToned = parallaxBitmap; }

                        // OPTIMIZATION: Only regenerate saw frames when background tint actually changed
                        // Saws are tinted by background color, not tile/ground color
                        if (bgChanged)
                        {
                            try
                            {
                                if (backgroundTint.A == 255 && backgroundTint.R == 0 && backgroundTint.G == 0 && backgroundTint.B == 0)
                                {
                                    this.sawFrame1TilesTinted = CreateSolidBlackImages(this.sawFrame1TilesOrig);
                                    this.sawFrame2TilesTinted = CreateSolidBlackImages(this.sawFrame2TilesOrig);
                                    this.smallSawFrame1TilesTinted = CreateSolidBlackImages(this.smallSawFrame1TilesOrig);
                                    this.smallSawFrame2TilesTinted = CreateSolidBlackImages(this.smallSawFrame2TilesOrig);
                                    this.largeSawFrame1TilesTinted = CreateSolidBlackImages(this.largeSawFrame1TilesOrig);
                                    this.largeSawFrame2TilesTinted = CreateSolidBlackImages(this.largeSawFrame2TilesOrig);
                                }
                                else
                                {
                                    this.sawFrame1TilesTinted = CreateHslShiftedImages(this.sawFrame1TilesOrig, backgroundTint, outlineTintLocal);
                                    this.sawFrame2TilesTinted = CreateHslShiftedImages(this.sawFrame2TilesOrig, backgroundTint, outlineTintLocal);
                                    this.smallSawFrame1TilesTinted = CreateHslShiftedImages(this.smallSawFrame1TilesOrig, backgroundTint, outlineTintLocal);
                                    this.smallSawFrame2TilesTinted = CreateHslShiftedImages(this.smallSawFrame2TilesOrig, backgroundTint, outlineTintLocal);
                                    this.largeSawFrame1TilesTinted = CreateHslShiftedImages(this.largeSawFrame1TilesOrig, backgroundTint, outlineTintLocal);
                                    this.largeSawFrame2TilesTinted = CreateHslShiftedImages(this.largeSawFrame2TilesOrig, backgroundTint, outlineTintLocal);
                                }
                            }
                            catch { }
                        }

                        tileLayerCache = null;
                        try { groundTintedTileCache.Clear(); } catch { }
                        try { spriteBackgroundCompositeCache.Clear(); } catch { }
                    }
                }
                catch { }
                if (newSpeed_fixed.HasValue)
                {
                    currentSpeed_fixed = newSpeed_fixed.Value;
                    playerVelX_fixed = newSpeed_fixed.Value; // Update Wave horizontal velocity for current speed
                    
                    // Update speed variable for debug display
                    if (newSpeed_fixed.Value == CUBE_SPEED_X05) speed = 0;
                    else if (newSpeed_fixed.Value == CUBE_SPEED_X1) speed = 1;
                    else if (newSpeed_fixed.Value == CUBE_SPEED_X2) speed = 2;
                    else if (newSpeed_fixed.Value == CUBE_SPEED_X3) speed = 3;
                    else if (newSpeed_fixed.Value == CUBE_SPEED_X4) speed = 4;
                    
                    // Update combo box selection to match
                    try
                    {
                        Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            if (SpeedComboBox != null && speed >= 0 && speed < SpeedComboBox.Items.Count)
                            {
                                SpeedComboBox.SelectedIndex = speed;
                            }
                        }));
                    }
                    catch { }
                }
            }
            catch { }

            RenderFrame();
        }

        private void RenderFrame()
        {
            // Prevent rendering if window is closed
            if (windowClosed) return;
            
            // Advance per-frame counter used for caching overlay-computed hitboxes
            try { renderFrameCounter++; } catch { renderFrameCounter = 1; }
            // One-time first-frame diagnostic snapshot (Option A)
            try
            {
                if (!simDebugLoggedFirstFrame)
                {
                    simDebugLoggedFirstFrame = true;
                    // Determine which ImageSource would be used as the brush source
                    string chosenSrc = "none";
                    int srcW = 0, srcH = 0;
                    try
                    {
                        ImageSource? src = null;
                        if (parallaxBitmapToned != null) src = parallaxBitmapToned;
                        else if (parallaxBitmap != null) src = parallaxBitmap;
                        else if (parallaxTonedImages != null && parallaxTonedImages.Length == (parallaxImages==null?0:parallaxImages.Length) && parallaxTonedImages.Length>0) src = parallaxTonedImages[0];
                        else if (parallaxImages != null && parallaxImages.Length > 0) src = parallaxImages[0];
                        if (src is BitmapSource bs)
                        {
                            chosenSrc = bs.GetType().Name;
                            srcW = bs.PixelWidth;
                            srcH = bs.PixelHeight;
                        }
                        else if (src != null) chosenSrc = src.GetType().Name;
                    }
                    catch { }

                    // Compute parallax offsets as used when creating the brush
                    double logParallaxOffsetX = -(cameraX_fixed >> 8) * (1.0 - parallaxX);
                    double logParallaxOffsetY = -(cameraY_fixed >> 8) * (1.0 - parallaxY);

                    string tileLayerSrc = (tileLayerImage?.Source == null) ? "null" : tileLayerImage.Source.GetType().Name;

                    AppendSimDebug($"RenderFrame: hasParallaxLayer={hasParallaxLayer}, chosenSrc={chosenSrc}, srcW={srcW}, srcH={srcH}, parallaxImagesLen={(parallaxImages==null?0:parallaxImages.Length)}, parallaxTonedLen={(parallaxTonedImages==null?0:parallaxTonedImages.Length)}, bgRectFill={(bgRectPersistent?.Fill==null?"null":bgRectPersistent.Fill.GetType().Name)}, backgroundTint={backgroundTint}, parallaxOffsetX={logParallaxOffsetX}, parallaxOffsetY={logParallaxOffsetY}, tileLayerImageSource={tileLayerSrc}");

                    try
                    {
                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine("RenderCanvas children:");
                        for (int i = 0; i < RenderCanvas.Children.Count; i++)
                        {
                            var child = RenderCanvas.Children[i];
                            int z = 0;
                            try { z = System.Windows.Controls.Canvas.GetZIndex(child); } catch { }
                            string tname = child?.GetType().Name ?? "null";
                            double w = 0, h = 0;
                            try { w = (child as System.Windows.FrameworkElement)?.Width ?? Double.NaN; h = (child as System.Windows.FrameworkElement)?.Height ?? Double.NaN; } catch { }
                            sb.AppendLine($"  [{i}] Type={tname} Z={z} Width={w} Height={h} Visible={(child is System.Windows.FrameworkElement fe? (fe.Visibility==Visibility.Visible):true)}");
                        }
                        AppendSimDebug(sb.ToString());
                    }
                    catch { }
                }
            }
            catch { }

            // Compute pixel offset and starting tile index
            int pixelX = cameraX_fixed >> 8; // full pixels
            int subPixelX = cameraX_fixed & 0xFF; // fractional
            int startTileX = pixelX / TILE;
            int offsetX = pixelX % TILE;
            double subPixelOffsetX = subPixelX / 256.0; // Convert to fractional pixels
            int pixelY = cameraY_fixed >> 8;
            int subPixelY = cameraY_fixed & 0xFF; // fractional Y
            double subPixelOffsetY = subPixelY / 256.0; // Convert to fractional pixels

            // If ground is present in the preview, reserve up to three ground rows at the bottom
            int groundRowsToReserve = 0;
            if (hasGroundLayer && groundTileRows > 0)
            {
                // Reserve exactly 3 rows when a ground layer exists (or fewer if ground bitmap has <3 rows)
                groundRowsToReserve = Math.Min(3, groundTileRows);
            }
            int groundPixels = groundRowsToReserve * TILE;

            // Keep camera-aligned start/offset based on actual camera Y so sub-pixel translation
            // remains consistent with the editor. We'll subtract ground rows when sampling map tiles.
            int startTileY = pixelY / TILE;
            int offsetY = pixelY % TILE;

            // Update persistent background: draw parallax tiled image when available
            try
            {
                if (bgRectPersistent != null && hasParallaxLayer && backgroundForceSolidBlack)
                {
                    // Force full solid-black background when 0x8F activated (don't use parallax images)
                    try { bgRectPersistent.Fill = new SolidColorBrush(Color.FromArgb(255, 0, 0, 0)); }
                    catch { bgRectPersistent.Fill = Brushes.Black; }
                }
                else if (bgRectPersistent != null && hasParallaxLayer && parallaxImages != null && parallaxImages.Length > 0)
                {
                    // Prefer using the full parallax bitmap (if provided) so the entire image repeats.
                    ImageSource? src = null;
                    if (parallaxBitmapToned != null) src = parallaxBitmapToned;
                    else if (parallaxBitmap != null) src = parallaxBitmap;
                    else if (parallaxTonedImages != null && parallaxTonedImages.Length == parallaxImages.Length && parallaxTonedImages[0] != null) src = parallaxTonedImages[0];
                    else if (parallaxImages != null && parallaxImages.Length > 0) src = parallaxImages[0];

                    if (src is BitmapSource pbs)
                    {
                        double imgW = Math.Max(1.0, pbs.PixelWidth);
                        double imgH = Math.Max(1.0, pbs.PixelHeight);
                        var brushImg = App.EnsureUnfrozenForRender(src) ?? src;
                        var brush = new ImageBrush(brushImg)
                        {
                            TileMode = TileMode.Tile,
                            ViewportUnits = BrushMappingMode.Absolute,
                            Viewport = new Rect(0, 0, imgW, imgH),
                            Stretch = Stretch.None
                        };

                        // Parallax translation: background moves slower than camera based on parallaxX/Y.
                        double parallaxOffsetX = -(pixelX) * (1.0 - parallaxX);
                        double parallaxOffsetY = -(cameraY_fixed >> 8) * (1.0 - parallaxY);

                        // Align horizontal scroll to pixel coordinates to avoid shimmering
                        brush.Transform = new TranslateTransform(parallaxOffsetX, parallaxOffsetY);
                        bgRectPersistent.Fill = brush;
                    }
                    else
                    {
                        // If no src was selectable, attempt an on-the-spot load of the embedded project parallax
                        try
                        {
                            var asm = System.Reflection.Assembly.GetExecutingAssembly();
                            var names = asm.GetManifestResourceNames();
                            var fullName = names.FirstOrDefault(r => r.IndexOf("Assets.parallax.bmp", StringComparison.OrdinalIgnoreCase) >= 0)
                                ?? names.FirstOrDefault(r => r.IndexOf("parallax Blue.bmp", StringComparison.OrdinalIgnoreCase) >= 0)
                                ?? names.FirstOrDefault(r => r.IndexOf("parallax.bmp", StringComparison.OrdinalIgnoreCase) >= 0)
                                ?? names.FirstOrDefault(r => r.IndexOf("parallax", StringComparison.OrdinalIgnoreCase) >= 0);
                            if (!string.IsNullOrEmpty(fullName))
                            {
                                using (var s = asm.GetManifestResourceStream(fullName))
                                {
                                    if (s != null)
                                    {
                                        var bi = new BitmapImage();
                                        bi.BeginInit();
                                        bi.CacheOption = BitmapCacheOption.OnLoad;
                                        bi.StreamSource = s;
                                        bi.EndInit();
                                        bi.Freeze();
                                        // set as runtime parallax bitmap and create a brush
                                        this.parallaxBitmap = bi;
                                        this.parallaxImages = new ImageSource[] { bi };
                                        this.parallaxTonedImages = null;
                                        this.hasParallaxLayer = true;

                                        var brush2Img = App.EnsureUnfrozenForRender(bi) ?? bi;
                                        var brush2 = new ImageBrush(brush2Img)
                                        {
                                            TileMode = TileMode.Tile,
                                            ViewportUnits = BrushMappingMode.Absolute,
                                            Viewport = new Rect(0, 0, Math.Max(1.0, bi.PixelWidth), Math.Max(1.0, bi.PixelHeight)),
                                            Stretch = Stretch.None
                                        };
                                        double parallaxOffsetX2 = -(pixelX) * (1.0 - parallaxX);
                                        double parallaxOffsetY2 = -(cameraY_fixed >> 8) * (1.0 - parallaxY);
                                        brush2.Transform = new TranslateTransform(parallaxOffsetX2, parallaxOffsetY2);
                                        if (bgRectPersistent != null) bgRectPersistent.Fill = brush2;
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                }
                else
                {
                    if (bgRectPersistent != null) bgRectPersistent.Fill = new SolidColorBrush(backgroundTint);
                }
            }
            catch { }

            // Rebuild tile-layer cache when integer tile origin changes
            try
            {
                if (tileLayerImage != null && (tileLayerCache == null || cachedStartTileX != startTileX || cachedStartTileY != startTileY || (lastCacheHadAnimatedTiles && lastCacheAnimationFrame != animationFrame)))
                {
                    int cacheTilesX = NES_W + 1;
                    int cacheTilesY = NES_H + 1;
                    int pxW = cacheTilesX * TILE;
                    int pxH = cacheTilesY * TILE;

                    var dv = new DrawingVisual();
                    bool hadAnimated = false;
                    using (var dc = dv.RenderOpen())
                    {
                        // Pre-create a semi-transparent red brush for tile hitbox overlay
                        Brush? tileHitBrush = null;
                        try
                        {
                            if (ShowTileHitboxes)
                            {
                                tileHitBrush = new SolidColorBrush(Color.FromArgb(160, 0xFF, 0x00, 0x00));
                                try { tileHitBrush.Freeze(); } catch { }
                            }
                        }
                        catch { tileHitBrush = null; }
                        int cacheTilesY_local = NES_H + 1;
                        int groundRowsToReserve_local = groundRowsToReserve;
                        int groundStartRow_local = cacheTilesY_local - groundRowsToReserve_local;

                        // Determine ground layout columns if ground images are available
                        int groundCols = 1;
                        if (groundImages != null && groundTileRows > 0)
                        {
                            groundCols = Math.Max(1, groundImages.Length / groundTileRows);
                        }

                        for (int vx = 0; vx <= NES_W; vx++)
                        {
                            int mapX = startTileX + vx;
                            for (int vy = 0; vy <= NES_H; vy++)
                            {
                                Rect dest = new Rect(vx * TILE, vy * TILE, TILE, TILE);

                                // Compute the map Y corresponding to this dest row, where we treat the
                                // visible rows as starting from startTileY - groundRowsToReserve
                                int mapY = startTileY + groundRowsToReserve_local + vy;

                                // If mapY is beyond the bottom of the map, and we have a ground layer,
                                // draw the appropriate ground slice row instead of map tiles.
                                if (mapY >= mapHeight)
                                {
                                    if (hasGroundLayer && groundImages != null && groundImages.Length > 0)
                                    {
                                        int pyGround = mapY - mapHeight; // 0..groundRowsToReserve-1
                                        int pxGround = ((mapX % groundCols) + groundCols) % groundCols; // wrap
                                        int rowIndex = Math.Max(0, Math.Min(groundTileRows - 1, pyGround));
                                        int arrIdx = rowIndex * groundCols + pxGround;
                                        ImageSource? gimg = null;
                                        if (groundTonedImages != null && arrIdx >= 0 && arrIdx < groundTonedImages.Length) gimg = groundTonedImages[arrIdx];
                                        if (gimg == null && groundImages != null && arrIdx >= 0 && arrIdx < groundImages.Length) gimg = groundImages[arrIdx];
                                        if (gimg != null)
                                        {
                                            var gdraw = App.EnsureUnfrozenForRender(gimg) ?? gimg;
                                            if (gimg is BitmapSource gbs)
                                            {
                                                double imgW = Math.Max(1.0, gbs.PixelWidth);
                                                double imgH = Math.Max(1.0, gbs.PixelHeight);
                                                if (imgW <= TILE && imgH <= TILE)
                                                {
                                                    double x = dest.X + (TILE - imgW) / 2.0;
                                                    double y = dest.Y + (TILE - imgH);
                                                    dc.DrawImage(gdraw, new Rect(x, y, imgW, imgH));
                                                }
                                                else
                                                {
                                                    dc.DrawImage(gdraw, dest);
                                                }
                                            }
                                            else
                                            {
                                                dc.DrawImage(gdraw, dest);
                                            }
                                        }
                                        else
                                        {
                                            dc.DrawRectangle(Brushes.Black, null, dest);
                                        }
                                        continue;
                                    }
                                    else
                                    {
                                        dc.DrawRectangle(Brushes.Black, null, dest);
                                        continue;
                                    }
                                }

                                // Normal map tile sampling
                                if (mapX < 0 || mapX >= mapWidth || mapY < 0 || mapY >= mapHeight)
                                {
                                    dc.DrawRectangle(Brushes.Black, null, dest);
                                    continue;
                                }
                                int idx = mapY * mapWidth + mapX;
                                int t = tiles[idx];
                                int animatedTileIndex = MapAnimatedTileIndex(t);
                                int useTileIndex = animatedTileIndex;
                                // Apply simulator-specific remapping so certain tile codes
                                // render exactly like other tile indices (or transparent).
                                useTileIndex = ResolveSimulatorTileIndex(useTileIndex);
                                if (animatedTileIndex != t || useTileIndex >= 1000) hadAnimated = true;
                                ImageSource? chosenTile = null;
                                try
                                {
                                    if (useTileIndex >= 1000)
                                    {
                                        // Use the same animation cadence as sprite frames so saws flip in sync
                                        bool frame2 = (((animationFrame * 9) / 20) % 2) != 0;
                                        int ut = useTileIndex;
                                        // Prefer explicit tileImages/toned images for animated tile indices when available
                                        if (tileTonedImages != null && useTileIndex >= 0 && useTileIndex < tileTonedImages.Length && tileTonedImages[useTileIndex] != null)
                                        {
                                            chosenTile = tileTonedImages[useTileIndex];
                                        }
                                        else if (tileImages != null && useTileIndex >= 0 && useTileIndex < tileImages.Length && tileImages[useTileIndex] != null)
                                        {
                                            chosenTile = tileImages[useTileIndex];
                                        }
                                        else if (ut >= 1000 && ut <= 1007)
                                        {
                                            int off = (ut - 1000) % 4;
                                            int len1 = sawFrame1TilesTinted != null ? sawFrame1TilesTinted.Length : 0;
                                            int len2 = sawFrame2TilesTinted != null ? sawFrame2TilesTinted.Length : 0;
                                            int tileCount = 4;
                                            int nFrames1 = len1 >= tileCount && len1 % tileCount == 0 ? len1 / tileCount : 1;
                                            int nFrames2 = len2 >= tileCount && len2 % tileCount == 0 ? len2 / tileCount : 1;
                                            int frameCount = Math.Max(1, Math.Max(nFrames1, nFrames2));
                                            int frameIdx = (((animationFrame * 9) / 20)) % frameCount;
                                            bool useFrame2 = (((animationFrame * 9) / 20) % 2) == 1;

                                            if (useFrame2 && sawFrame2TilesTinted != null)
                                            {
                                                int arrIdx = (nFrames2 > 1 ? (frameIdx % nFrames2) * tileCount + off : off);
                                                if (arrIdx >= 0 && arrIdx < len2) chosenTile = sawFrame2TilesTinted[arrIdx];
                                            }
                                            if (chosenTile == null && sawFrame1TilesTinted != null)
                                            {
                                                int arrIdx = (nFrames1 > 1 ? (frameIdx % nFrames1) * tileCount + off : off);
                                                if (arrIdx >= 0 && arrIdx < len1) chosenTile = sawFrame1TilesTinted[arrIdx];
                                            }
                                        }
                                        else if (ut >= 1010 && ut <= 1015)
                                        {
                                            int off = (ut - 1010) % 3;
                                            int len1 = smallSawFrame1TilesTinted != null ? smallSawFrame1TilesTinted.Length : 0;
                                            int len2 = smallSawFrame2TilesTinted != null ? smallSawFrame2TilesTinted.Length : 0;
                                            int tileCount = 3;
                                            int nFrames1 = len1 >= tileCount && len1 % tileCount == 0 ? len1 / tileCount : 1;
                                            int nFrames2 = len2 >= tileCount && len2 % tileCount == 0 ? len2 / tileCount : 1;
                                            int frameCount = Math.Max(1, Math.Max(nFrames1, nFrames2));
                                            int frameIdx = (((animationFrame * 9) / 20)) % frameCount;
                                            bool useFrame2 = (((animationFrame * 9) / 20) % 2) == 1;

                                            if (useFrame2 && smallSawFrame2TilesTinted != null)
                                            {
                                                int arrIdx = (nFrames2 > 1 ? (frameIdx % nFrames2) * tileCount + off : off);
                                                if (arrIdx >= 0 && arrIdx < len2) chosenTile = smallSawFrame2TilesTinted[arrIdx];
                                            }
                                            if (chosenTile == null && smallSawFrame1TilesTinted != null)
                                            {
                                                int arrIdx = (nFrames1 > 1 ? (frameIdx % nFrames1) * tileCount + off : off);
                                                if (arrIdx >= 0 && arrIdx < len1) chosenTile = smallSawFrame1TilesTinted[arrIdx];
                                            }
                                        }
                                        else if (ut >= 1020 && ut <= 1037)
                                        {
                                            int off = (ut - 1020) % 9;
                                            int len1 = largeSawFrame1TilesTinted != null ? largeSawFrame1TilesTinted.Length : 0;
                                            int len2 = largeSawFrame2TilesTinted != null ? largeSawFrame2TilesTinted.Length : 0;
                                            int tileCount = 9;
                                            int nFrames1 = len1 >= tileCount && len1 % tileCount == 0 ? len1 / tileCount : 1;
                                            int nFrames2 = len2 >= tileCount && len2 % tileCount == 0 ? len2 / tileCount : 1;
                                            int frameCount = Math.Max(1, Math.Max(nFrames1, nFrames2));
                                            int frameIdx = (((animationFrame * 9) / 20)) % frameCount;
                                            bool useFrame2 = (((animationFrame * 9) / 20) % 2) == 1;

                                            if (useFrame2 && largeSawFrame2TilesTinted != null)
                                            {
                                                int arrIdx = (nFrames2 > 1 ? (frameIdx % nFrames2) * tileCount + off : off);
                                                if (arrIdx >= 0 && arrIdx < len2) chosenTile = largeSawFrame2TilesTinted[arrIdx];
                                            }
                                            if (chosenTile == null && largeSawFrame1TilesTinted != null)
                                            {
                                                int arrIdx = (nFrames1 > 1 ? (frameIdx % nFrames1) * tileCount + off : off);
                                                if (arrIdx >= 0 && arrIdx < len1) chosenTile = largeSawFrame1TilesTinted[arrIdx];
                                            }
                                        }

                                        // (no diagnostics) intentionally left blank
                                    }
                                }
                                catch { }

                                if (chosenTile == null && useTileIndex >= 0)
                                {
                                    // Use the animated tile index (useTileIndex) consistently when selecting the image.
                                    // For a few ground-related tile indices, prefer applying the ground tint
                                    // (these tiles should respond to ground tint, not tile tint).
                                    // These specific tiles should behave exactly like the ground image.
                                    var groundAffected = (useTileIndex == 0x01 || useTileIndex == 0x02 || useTileIndex == 0x05 || useTileIndex == 0x06 || useTileIndex == 0x88 || useTileIndex == 0x89);

                                    if (groundAffected)
                                    {
                                        try
                                        {
                                            ImageSource? gt = null;
                                            // Include both groundTint and current tileTint in the cache key so
                                            // per-tile ground-tinted copies respond to later tile/object tints.
                                            long key = (((long)useTileIndex) << 48)
                                                       | (((long)groundTint.A & 0xFF) << 40) | (((long)groundTint.R & 0xFF) << 32) | (((long)groundTint.G & 0xFF) << 24) | (((long)groundTint.B & 0xFF) << 16)
                                                       | (((long)tileTint.A & 0xFF) << 8) | (((long)tileTint.R & 0xFF));
                                            if (!groundTintedTileCache.TryGetValue(key, out gt))
                                            {
                                                // create a ground-tinted copy from original tileImages when available
                                                if (tileImages != null && useTileIndex >= 0 && useTileIndex < tileImages.Length && tileImages[useTileIndex] != null)
                                                {
                                                    try
                                                    {
                                                            // If background was forced to solid black (0x8F), ensure
                                                            // ground-affected tiles also observe the black-mask so
                                                            // non-white/non-green pixels become black. Preserve seams.
                                                            if (backgroundForceSolidBlack)
                                                            {
                                                                try { gt = CreateBlackMaskedExceptColor(tileImages[useTileIndex], playerPlaceholderGreen, Color.FromArgb(0,0,0,0), false); }
                                                                catch { /* fallthrough to other logic below */ }
                                                            }
                                                            if (gt == null)
                                                            {
                                                        if (groundTint.A == 255 && groundTint.R == 0 && groundTint.G == 0 && groundTint.B == 0)
                                                        {
                                                            // Pure black ground tint: make a black-masked copy (preserve/recolor white lines using tileTint)
                                                            // Use two-tone mapping for this single tile so non-white pixels
                                                            // become pure black while near-white outlines remain for
                                                            // object tinting control.
                                                            try
                                                            {
                                                                // Preserve white seam outlines rather than recoloring them to tileTint
                                                                var arrGt = CreateTwoToneTileImages(new ImageSource?[] { tileImages[useTileIndex]! }, Color.FromArgb(255, 0, 0, 0), Color.FromArgb(255, 0, 0, 0), Color.FromArgb(0, 0, 0, 0));
                                                                if (arrGt != null && arrGt.Length > 0) gt = arrGt[0];
                                                            }
                                                            catch { gt = CreateBlackMaskedImage(tileImages[useTileIndex], tileTint, false); }
                                                        }
                                                        else
                                                        {
                                                            // Use two-tone mapping like ground: lighter = selected ground tint,
                                                            // darker = palette row-up color. If an object/tile outline tint is active
                                                            // for this regeneration, pass it so seam pixels recolor to the object
                                                            // color. Otherwise preserve seams by passing transparent outline.
                                                            try
                                                            {
                                                                var darker = PaletteHelper.RowUpColor(Color.FromArgb(groundTint.A, groundTint.R, groundTint.G, groundTint.B));
                                                                var outlineForThisParam = startupTintApplied ? Color.FromArgb(0, 0, 0, 0) : tileTint;
                                                                var outlineForThis = (outlineForThisParam.A > 0) ? outlineForThisParam : Color.FromArgb(0, 0, 0, 0);
                                                                var arr = CreateTwoToneTileImages(new ImageSource?[] { tileImages[useTileIndex]! }, groundTint, darker, outlineForThis);
                                                                if (arr != null && arr.Length > 0) gt = arr[0];
                                                            }
                                                            catch
                                                            {
                                                                var arr = CreateHslShiftedImages(new ImageSource?[] { tileImages[useTileIndex]! }, groundTint, tileTint);
                                                                if (arr != null && arr.Length > 0) gt = arr[0];
                                                            }
                                                        }
                                                            }
                                                    }
                                                    catch { gt = tileImages[useTileIndex]; }
                                                }
                                                groundTintedTileCache[key] = gt;
                                            }
                                            if (gt != null) chosenTile = gt;

                                            // Ensure object (outline) tint recolors the seam for these ground-affected
                                            // tiles exactly like normal tiles: when an object/tile outline tint is
                                            // active, regenerate from the ORIGINAL tile image using a two-step
                                            // process: (1) two-tone ground mapping that PRESERVES seams (transparent
                                            // outline param), then (2) apply the outline recolor onto that result.
                                            // This bypasses any cached ground-only images and guarantees parity.
                                            try
                                            {
                                                if (tileImages != null && useTileIndex >= 0 && useTileIndex < tileImages.Length && tileImages[useTileIndex] != null)
                                                {
                                                    var darker2 = PaletteHelper.RowUpColor(Color.FromArgb(groundTint.A, groundTint.R, groundTint.G, groundTint.B));
                                                    if (tileTint.A > 0)
                                                    {
                                                        // Active object tint: preserve seams during two-tone, then recolor outlines to object tint
                                                        var twoToneArr = CreateTwoToneTileImages(new ImageSource?[] { tileImages[useTileIndex]! }, groundTint, darker2, Color.FromArgb(0, 0, 0, 0));
                                                        ImageSource? twoToneBase = (twoToneArr != null && twoToneArr.Length > 0) ? twoToneArr[0] : tileImages[useTileIndex];
                                                        var finalArr = CreateOutlineTintedTileImages(new ImageSource?[] { twoToneBase }, tileTint);
                                                        if (finalArr != null && finalArr.Length > 0 && finalArr[0] != null)
                                                        {
                                                            chosenTile = finalArr[0];
                                                        }
                                                    }
                                                    else
                                                    {
                                                        // No object tint yet: force seams to opaque white in the two-tone pass
                                                        var twoToneArr = CreateTwoToneTileImages(new ImageSource?[] { tileImages[useTileIndex]! }, groundTint, darker2, Color.FromArgb(255, 255, 255, 255));
                                                        if (twoToneArr != null && twoToneArr.Length > 0 && twoToneArr[0] != null)
                                                        {
                                                            chosenTile = twoToneArr[0];
                                                        }
                                                    }
                                                }
                                            }
                                            catch { }
                                        }
                                        catch { }
                                    }

                                    if (chosenTile == null)
                                    {
                                        if (useTileIndex >= 1000)
                                        {
                                            if (tileTonedImages != null && useTileIndex >= 0 && useTileIndex < tileTonedImages.Length && tileTonedImages[useTileIndex] != null)
                                                chosenTile = tileTonedImages[useTileIndex];
                                            else if (tileImages != null && useTileIndex >= 0 && useTileIndex < tileImages.Length && tileImages[useTileIndex] != null)
                                                chosenTile = tileImages[useTileIndex];
                                        }
                                        else
                                        {
                                            if (tileTonedImages != null && useTileIndex >= 0 && useTileIndex < tileTonedImages.Length && tileTonedImages[useTileIndex] != null)
                                                chosenTile = tileTonedImages[useTileIndex];
                                            else if (tileImages != null && useTileIndex >= 0 && useTileIndex < tileImages.Length && tileImages[useTileIndex] != null)
                                                chosenTile = tileImages[useTileIndex];
                                        }
                                    }
                                }

                                // Safety: ensure non-ground tiles receive outline recolor when an object tint is active.
                                try
                                {
                                    if ((useTileIndex != 0x01 && useTileIndex != 0x02 && useTileIndex != 0x05 && useTileIndex != 0x06 && useTileIndex != 0x88 && useTileIndex != 0x89) && tileTint.A > 0 && useTileIndex >= 0)
                                    {
                                        ImageSource? baseSrc = null;
                                        if (tileTonedImages != null && useTileIndex >= 0 && useTileIndex < tileTonedImages.Length)
                                            baseSrc = tileTonedImages[useTileIndex];
                                        if (baseSrc == null && tileImages != null && useTileIndex >= 0 && useTileIndex < tileImages.Length)
                                            baseSrc = tileImages[useTileIndex];

                                        if (baseSrc != null)
                                        {
                                            try
                                            {
                                                var trecol = CreateOutlineTintedTileImages(new ImageSource?[] { baseSrc }, tileTint);
                                                if (trecol != null && trecol.Length > 0 && trecol[0] != null)
                                                    chosenTile = trecol[0];
                                            }
                                            catch { }
                                        }
                                    }
                                }
                                catch { }

                                if (chosenTile != null)
                                {
                                    try
                                    {
                                        if (tileSelectionLogCount < TILE_SELECTION_LOG_LIMIT)
                                        {
                                            string src = "unknown";
                                            try
                                            {
                                                if (tileTonedImages != null && useTileIndex >= 0 && useTileIndex < tileTonedImages.Length && tileTonedImages[useTileIndex] == chosenTile)
                                                    src = "tileTonedImages";
                                                else if (tileImages != null && useTileIndex >= 0 && useTileIndex < tileImages.Length && tileImages[useTileIndex] == chosenTile)
                                                    src = "tileImages";
                                                else
                                                {
                                                    foreach (var kv in groundTintedTileCache)
                                                    {
                                                        if (kv.Value == chosenTile) { src = "groundTintedTileCache"; break; }
                                                    }
                                                }
                                            }
                                            catch { }
                                            AppendSimDebug($"GetOrRenderCachedTile idx={useTileIndex} src={src} mapIdx={idx} tileVal=0x{t:X}");
                                            tileSelectionLogCount++;
                                        }
                                    }
                                    catch { }
                                    // If background is forced to solid black, ensure the chosen tile
                                    // used for drawing is the black-masked variant (preserving exact
                                    // player-green and white seams). Cache masked variants to avoid
                                    // repeated pixel processing.
                                    try
                                    {
                                        // If this tile contains the special-deco green, ensure those
                                        // pixels are recolored to `playerTint` now so masking uses
                                        // the player-colored pixels and doesn't accidentally black them.
                                        try
                                        {
                                            int[] special = new int[] { 0x0C, 0x0D, 0x0E, 0x0F, 0x13, 0x14, 0x80, 0x81, 0x84, 0x85, 0x86, 0x87 };
                                            if (chosenTile != null && tileImages != null && useTileIndex >= 0 && System.Array.IndexOf(special, useTileIndex) >= 0)
                                            {
                                                try { chosenTile = ApplyPlayerTintToGreenPixels(chosenTile, tileImages[useTileIndex], playerTint); } catch { }
                                            }
                                        }
                                        catch { }

                                        if (backgroundForceSolidBlack && chosenTile != null)
                                        {
                                            // Choose an exclude color so the black-mask preserves important pixels:
                                            // - If this tile contains player-green deco, preserve the player-tinted color for those
                                            //   pixels. Otherwise preserve outline-colored pixels (tileTint) or placeholder green.
                                            Color excludeColor;
                                            int[] special2 = new int[] { 0x0C, 0x0D, 0x0E, 0x0F, 0x13, 0x14, 0x80, 0x81, 0x84, 0x85, 0x86, 0x87 };
                                            if (tileImages != null && useTileIndex >= 0 && System.Array.IndexOf(special2, useTileIndex) >= 0)
                                            {
                                                excludeColor = (playerTint.A > 0) ? playerTint : Color.FromArgb(255, 0x5A, 0xCE, 0x52);
                                            }
                                            else
                                            {
                                                excludeColor = (tileTint.A > 0) ? Color.FromArgb(255, tileTint.R, tileTint.G, tileTint.B) : playerPlaceholderGreen;
                                            }

                                            // Include the excludeColor in the cache key so different exclude colors
                                            // produce different masked variants.
                                            // Use tile index + exclude color as the cache key to avoid sharing
                                            // masked ImageSource instances between different tile indices.
                                            long compositeKey = (((long)useTileIndex & 0xFFFFL) << 32)
                                                                | (((long)excludeColor.A & 0xFFL) << 24)
                                                                | (((long)excludeColor.R & 0xFFL) << 16)
                                                                | (((long)excludeColor.G & 0xFFL) << 8)
                                                                | (((long)excludeColor.B & 0xFFL));

                                            if (!blackMaskedTileCache.TryGetValue(compositeKey, out var masked))
                                            {
                                                try { masked = CreateBlackMaskedExceptColor(chosenTile, excludeColor, Color.FromArgb(0, 0, 0, 0), false); }
                                                catch { masked = chosenTile; }
                                                try { blackMaskedTileCache[compositeKey] = masked; } catch { }
                                            }
                                            if (masked != null) chosenTile = masked;
                                        }
                                    }
                                    catch { }
                                    // If the tile image is smaller than the canonical TILE size (e.g.
                                    // saw halves that are half-height PNGs), draw it at its natural
                                    // pixel size instead of scaling to fill the full tile. Align
                                    // smaller images to the bottom of the tile so transparent
                                    // padding sits above as expected.
                                    if (chosenTile is BitmapSource bs)
                                    {
                                        // Use the bitmap's pixel dimensions to decide if it should be
                                        // drawn at natural size. Device-independent units in this
                                        // renderer correspond to pixels (RTB created at 96 DPI), so
                                        // bs.PixelWidth/Height work directly.
                                        double imgW = Math.Max(1.0, bs.PixelWidth);
                                        double imgH = Math.Max(1.0, bs.PixelHeight);

                                        if (imgW <= TILE && imgH <= TILE)
                                        {
                                            // bottom-align within the tile cell
                                            double x = dest.X + (TILE - imgW) / 2.0;
                                            double y = dest.Y + (TILE - imgH);

                                            // Nudge small-saw bottom halves up slightly so they align with the editor preview.
                                            // Small saw animated tile indices live in the 1010..1015 range. Only apply
                                            // for very short images (half-height) to avoid disturbing other tiles.
                                                try
                                                {
                                                    if (useTileIndex >= 1010 && useTileIndex <= 1015 && imgH <= (TILE / 2.0))
                                                    {
                                                        try
                                                        {
                                                            if (bs != null && IsImageTopHeavy(bs))
                                                                y -= 8; // move up 8 pixels for upside-down half bottoms only
                                                        }
                                                        catch { }
                                                    }
                                                }
                                                catch { }

                                            var drawTile = App.EnsureUnfrozenForRender(chosenTile) ?? chosenTile;
                                            dc.DrawImage(drawTile, new Rect(x, y, imgW, imgH));
                                        }
                                        else
                                        {
                                            // image larger than tile: fall back to scaling to tile
                                            var drawTile = App.EnsureUnfrozenForRender(chosenTile) ?? chosenTile;
                                            dc.DrawImage(drawTile, dest);
                                        }
                                    }
                                    else
                                    {
                                        var drawTile = App.EnsureUnfrozenForRender(chosenTile) ?? chosenTile;
                                        dc.DrawImage(drawTile, dest);
                                    }
                                }
                                else
                                {
                                    dc.DrawRectangle(Brushes.Black, null, dest);
                                }
                            }
                        }
                    }

                    var rtb = new RenderTargetBitmap(pxW, pxH, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(dv);
                    try { rtb.Freeze(); } catch { }
                    tileLayerCache = rtb;
                    try { spriteBackgroundCompositeCache.Clear(); } catch { }
                    try
                    {
                        AppendSimDebug($"Built tileLayerCache pxW={pxW} pxH={pxH} hadAnimated={hadAnimated} startTileX={startTileX} startTileY={startTileY} tilesLen={(tiles==null?0:tiles.Length)} mapWidth={mapWidth} mapHeight={mapHeight}");
                    }
                    catch { }
                    lastCacheHadAnimatedTiles = hadAnimated;
                    lastCacheAnimationFrame = animationFrame;
                    cachedStartTileX = startTileX;
                    cachedStartTileY = startTileY;
                    tileLayerImage!.Source = App.EnsureUnfrozenForRender(tileLayerCache) ?? tileLayerCache;
                    try { AppendSimDebug($"Assigned tileLayerImage.Source={(tileLayerImage.Source==null?"null":tileLayerImage.Source.GetType().Name)}"); } catch { }
                    tileLayerImage!.Width = pxW;
                    tileLayerImage!.Height = pxH;
                }
            }
            catch { }

                // Position the tile layer to account for fractional pixel offset (including sub-pixel precision)
            try { if (tileLayerImage != null) { System.Windows.Controls.Canvas.SetLeft(tileLayerImage, -offsetX - subPixelOffsetX); System.Windows.Controls.Canvas.SetTop(tileLayerImage, -offsetY - subPixelOffsetY); } } catch { }

            // Update per-tile hitbox overlays so they render above the tile layer but below sprites.
            try
            {
                tileHitboxesInUse = 0;
                if (ShowTileHitboxes)
                {
                    // Reuse visible tile rects: iterate the same visible tile grid used for cache
                    for (int vx = 0; vx <= NES_W; vx++)
                    {
                        int mapX = startTileX + vx;
                        for (int vy = 0; vy <= NES_H; vy++)
                        {
                            int mapY = startTileY + groundRowsToReserve + vy;
                            if (mapX < 0 || mapX >= mapWidth || mapY < 0 || mapY >= mapHeight) continue;

                            // Determine collision type for this visible tile and skip COL_NONE tiles
                            MetatileCollision col = MetatileCollision.COL_NONE;
                            bool hasDeath = false;
                            try
                            {
                                if (tiles == null) continue;
                                int tid = tiles[mapY * mapWidth + mapX];
                                int useTidForAnim = MapAnimatedTileIndex(tid);
                                int collisionTid = useTidForAnim;
                                if (useTidForAnim >= 1000)
                                {
                                    if (useTidForAnim >= 1000 && useTidForAnim <= 1007)
                                    {
                                        collisionTid = 0x08 + ((useTidForAnim - 1000) % 4);
                                    }
                                    else if (useTidForAnim >= 1010 && useTidForAnim <= 1015)
                                    {
                                        int group = (useTidForAnim - 1010) % 3;
                                        collisionTid = (group == 0) ? 0x04 : (group == 1) ? 0x7D : 0x7F;
                                    }
                                    else if (useTidForAnim >= 1020 && useTidForAnim <= 1037)
                                    {
                                        collisionTid = 0x74 + ((useTidForAnim - 1020) % 9);
                                    }
                                    else
                                    {
                                        collisionTid = tid;
                                    }
                                }
                                col = MetatileCollisionTable.GetCollision((byte)collisionTid);
                                if (col == MetatileCollision.COL_NONE) continue; // skip transparent/no-collision tiles
                                
                                // Check if this tile has death collision
                                for (int ly = 0; ly < TILE && !hasDeath; ly++)
                                {
                                    for (int lx = 0; lx < TILE && !hasDeath; lx++)
                                    {
                                        if (MetatileCollisionTable.TileKillsAtPixel(col, lx, ly))
                                        {
                                            hasDeath = true;
                                        }
                                    }
                                }
                            }
                            catch { }

                            // Determine if this requires pixel-by-pixel rendering:
                            // - Complex collision shapes (L-shapes, diagonals)
                            // - Tiles with death collision (to show death area distinctly)
                            bool isComplex = col == MetatileCollision.COL_TOP_LEFT_STAIRS ||
                                           col == MetatileCollision.COL_TOP_RIGHT_STAIRS ||
                                           col == MetatileCollision.COL_BOTTOM_LEFT_STAIRS ||
                                           col == MetatileCollision.COL_BOTTOM_RIGHT_STAIRS ||
                                           col == MetatileCollision.COL_TOP_LEFT_BOTTOM_RIGHT ||
                                           col == MetatileCollision.COL_TOP_RIGHT_BOTTOM_LEFT ||
                                           col == MetatileCollision.COL_DEATH_TOP_RIGHT ||
                                           col == MetatileCollision.COL_DEATH_TOP_LEFT ||
                                           col == MetatileCollision.COL_DEATH_BOTTOM_RIGHT ||
                                           col == MetatileCollision.COL_DEATH_BOTTOM_LEFT ||
                                           col == MetatileCollision.COL_DEATH_TOP_RIGHT_LEFT ||
                                           col == MetatileCollision.COL_DEATH_TOP_BOTTOM ||
                                           col == MetatileCollision.COL_DEATH_LEFT_RIGHT ||
                                           col == MetatileCollision.COL_DEATH_TOP_LEFT_BOTTOM ||
                                           col == MetatileCollision.COL_TOP_CENTER_SPIKE ||
                                           col == MetatileCollision.COL_BOTTOM_CENTER_SPIKE ||
                                           col == MetatileCollision.COL_LEFT_SPIKE_BLOCK ||
                                           col == MetatileCollision.COL_RIGHT_SPIKE_BLOCK ||
                                           col == MetatileCollision.COL_BOTTOM_LEFT_SPIKE ||
                                           col == MetatileCollision.COL_BOTTOM_RIGHT_SPIKE ||
                                           col == MetatileCollision.COL_BOTTOM_SPIKES;

                            // Also do pixel-by-pixel for any tile with death collision
                            bool needsPixelByPixel = isComplex || hasDeath;

                            if (needsPixelByPixel)
                            {
                                // Determine if this is a pure death tile (no solid collision) vs mixed
                                bool isPureDeathTile = col == MetatileCollision.COL_UP_LEFT_SPIKE ||
                                                      col == MetatileCollision.COL_UP_RIGHT_SPIKE ||
                                                      col == MetatileCollision.COL_UP_BOTH_SPIKES ||
                                                      col == MetatileCollision.COL_DOWN_LEFT_SPIKE ||
                                                      col == MetatileCollision.COL_DOWN_RIGHT_SPIKE ||
                                                      col == MetatileCollision.COL_DOWN_BOTH_SPIKES ||
                                                      col == MetatileCollision.COL_DEATH ||
                                                      col == MetatileCollision.COL_DEATH_TOP ||
                                                      col == MetatileCollision.COL_DEATH_BOTTOM ||
                                                      col == MetatileCollision.COL_DEATH_LEFT ||
                                                      col == MetatileCollision.COL_DEATH_RIGHT ||
                                                      col == MetatileCollision.COL_DEATH_BOTTOM_LEFT ||
                                                      col == MetatileCollision.COL_DEATH_BOTTOM_RIGHT ||
                                                      col == MetatileCollision.COL_DEATH_TOP_LEFT ||
                                                      col == MetatileCollision.COL_DEATH_TOP_RIGHT ||
                                                      col == MetatileCollision.COL_DEATH_TOP_RIGHT_LEFT ||
                                                      col == MetatileCollision.COL_DEATH_TOP_BOTTOM ||
                                                      col == MetatileCollision.COL_DEATH_LEFT_RIGHT ||
                                                      col == MetatileCollision.COL_DEATH_TOP_LEFT_BOTTOM;

                                // For complex shapes, render individual pixels that have collision
                                for (int ly = 0; ly < TILE; ly++)
                                {
                                    for (int lx = 0; lx < TILE; lx++)
                                    {
                                        bool hasCollision = TileOccupiesPixel(col, lx, ly);
                                        bool isDeathPixel = MetatileCollisionTable.TileKillsAtPixel(col, lx, ly);
                                        
                                        // Skip pixels that have neither collision nor death
                                        if (!hasCollision && !isDeathPixel) continue;
                                        
                                        // For pure death tiles, only render death pixels
                                        // For mixed tiles, render both collision (red) and death (pink)
                                        if (isPureDeathTile && !isDeathPixel) continue;

                                        System.Windows.Shapes.Rectangle r;
                                        if (tileHitboxesInUse < tileHitboxPool.Count)
                                        {
                                            r = tileHitboxPool[tileHitboxesInUse];
                                            r.Visibility = Visibility.Visible;
                                        }
                                        else
                                        {
                                            r = new System.Windows.Shapes.Rectangle();
                                            r.IsHitTestVisible = false;
                                            r.Stroke = null;
                                            tileHitboxPool.Add(r);
                                            RenderCanvas.Children.Add(r);
                                            try { System.Windows.Controls.Canvas.SetZIndex(r, 100); } catch { }
                                        }

                                        r.Fill = isDeathPixel
                                            ? new SolidColorBrush(Color.FromArgb(160, 0xFF, 0x50, 0xC8)) // Magenta/pink for death
                                            : new SolidColorBrush(Color.FromArgb(160, 0xFF, 0x00, 0x00)); // Red for collision

                                        r.Width = 1;
                                        r.Height = 1;
                                        System.Windows.Controls.Canvas.SetLeft(r, vx * TILE + lx - offsetX);
                                        System.Windows.Controls.Canvas.SetTop(r, vy * TILE + ly - offsetY + gridRenderShiftYPx);
                                        tileHitboxesInUse++;
                                    }
                                }
                            }
                            else
                            {
                                // For simple shapes, get collision bounds and draw a single rectangle
                                var (colLeft, colTop, colRight, colBottom) = GetCollisionBounds(col);
                                if (colRight <= colLeft || colBottom <= colTop) continue; // Invalid bounds

                                int rectWidth = colRight - colLeft;
                                int rectHeight = colBottom - colTop;

                                System.Windows.Shapes.Rectangle r;
                                if (tileHitboxesInUse < tileHitboxPool.Count)
                                {
                                    r = tileHitboxPool[tileHitboxesInUse];
                                    r.Visibility = Visibility.Visible;
                                }
                                else
                                {
                                    r = new System.Windows.Shapes.Rectangle();
                                    r.IsHitTestVisible = false;
                                    r.Stroke = null;
                                    tileHitboxPool.Add(r);
                                    RenderCanvas.Children.Add(r);
                                    try { System.Windows.Controls.Canvas.SetZIndex(r, 100); } catch { }
                                }

                                // Set color based on whether it has death collision
                                r.Fill = hasDeath
                                    ? new SolidColorBrush(Color.FromArgb(160, 0x80, 0x00, 0x80)) // Purple for death
                                    : new SolidColorBrush(Color.FromArgb(160, 0xFF, 0x00, 0x00)); // Red for collision

                                r.Width = rectWidth;
                                r.Height = rectHeight;
                                System.Windows.Controls.Canvas.SetLeft(r, vx * TILE + colLeft - offsetX);
                                System.Windows.Controls.Canvas.SetTop(r, vy * TILE + colTop - offsetY + gridRenderShiftYPx);
                                tileHitboxesInUse++;
                            }
                        }
                    }
                }

                // Hide unused pooled rectangles
                for (int i = tileHitboxesInUse; i < tileHitboxPool.Count; i++)
                {
                    try { tileHitboxPool[i].Visibility = Visibility.Collapsed; } catch { }
                }
            }
            catch { }

            // Update ground rectangle (render tiled ground image if available)
            try
            {
                if (groundRectPersistent != null)
                {
                    if (hasGroundLayer && groundImages != null && groundImages.Length > 0)
                    {
                        ImageSource? src = groundTonedImages != null && groundTonedImages.Length == groundImages.Length && groundTonedImages[0] != null ? groundTonedImages[0] : groundImages[0];
                        if (src is BitmapSource gbs)
                        {
                            double tileW = Math.Max(1.0, gbs.PixelWidth);
                            double tileH = Math.Max(1.0, gbs.PixelHeight);
                            var brushImg = App.EnsureUnfrozenForRender(src) ?? src;
                            var brush = new ImageBrush(brushImg)
                            {
                                TileMode = TileMode.Tile,
                                ViewportUnits = BrushMappingMode.Absolute,
                                Viewport = new Rect(0, 0, tileW, tileH),
                                Stretch = Stretch.Fill
                            };
                            // Sync horizontal scroll with tiles
                            brush.Transform = new TranslateTransform(-offsetX, 0);
                            groundRectPersistent.Fill = brush;
                        }
                        else
                        {
                            groundRectPersistent.Fill = new SolidColorBrush(groundTint) { Opacity = 0.25 };
                        }
                        int groundHeight = TILE * Math.Min(NES_H, Math.Max(groundTileRows, 2));
                        // Use reserved rows (up to 2) as visual ground height
                        groundHeight = TILE * Math.Min(NES_H, Math.Min(groundTileRows, 2));
                        groundRectPersistent.Height = groundHeight;
                        System.Windows.Controls.Canvas.SetTop(groundRectPersistent, (NES_H * TILE) - groundHeight);
                        // We draw ground cells directly into the tile-layer cache, so keep the persistent
                        // ground rect collapsed to avoid covering the tile layer with a single-tile brush.
                        groundRectPersistent.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        groundRectPersistent.Fill = new SolidColorBrush(groundTint) { Opacity = 0.25 };
                        int groundHeight = TILE * Math.Min(NES_H, 2);
                        groundRectPersistent.Height = groundHeight;
                        System.Windows.Controls.Canvas.SetTop(groundRectPersistent, (NES_H * TILE) - groundHeight);
                        groundRectPersistent.Visibility = Visibility.Collapsed;
                    }
                }
            }
            catch { }

            // Render sprites using pooled Image controls
            spritesInUse = 0;
            for (int vx = 0; vx < NES_W; vx++)
            {
                int mapX = startTileX + vx;
                for (int vy = 0; vy < NES_H; vy++)
                {
                    int mapY = startTileY + groundRowsToReserve + vy;
                    if (mapX < 0 || mapX >= mapWidth || mapY < 0 || mapY >= mapHeight) continue;
                    int idx = mapY * mapWidth + mapX;
                    int s = sprites[idx];
                    if (s < 0) continue;

                    // Visual alias: make sprite 0x7B render identically to 0x05
                    int s_vis = (s == 0x7B) ? 0x05 : s;

                    // If the global 'hide trigger sprites' option is enabled, skip drawing
                    // these specific trigger sprite images while still allowing them to
                    // function (triggers remain active in the simulation logic).
                    try { if (hideTriggerSprites && IsHiddenTriggerSprite(s)) continue; } catch { }

                    ImageSource? chosenSprite = null;
                    // Simulator-only special-case: prefer an embedded upside-down chain for sprite 0x3D
                    if (s == 0x3D)
                    {
                        if (forcePreviewMode && previewSpriteMap != null && previewSpriteMap.TryGetValue(s, out var pimg) && pimg != null)
                        {
                            chosenSprite = pimg;
                        }
                        else
                        {
                            if (chainUpsideSimImage == null)
                            {
                                try
                                {
                                    var asm = System.Reflection.Assembly.GetExecutingAssembly();
                                    var names = asm.GetManifestResourceNames();
                                    var fullName = names.FirstOrDefault(r => r.EndsWith("chain-upsidedown.png", StringComparison.OrdinalIgnoreCase) || r.IndexOf("chain-upsidedown.png", StringComparison.OrdinalIgnoreCase) >= 0);
                                    BitmapImage? bi = null;
                                    if (fullName != null)
                                    {
                                        using (var st = asm.GetManifestResourceStream(fullName))
                                        {
                                            if (st != null)
                                            {
                                                bi = new BitmapImage();
                                                bi.BeginInit();
                                                bi.CacheOption = BitmapCacheOption.OnLoad;
                                                bi.StreamSource = st;
                                                bi.EndInit();
                                                bi.Freeze();
                                            }
                                        }
                                    }
                                    if (bi == null)
                                    {
                                        var p = System.IO.Path.Combine(AppContext.BaseDirectory ?? ".", "chain-upsidedown.png");
                                        if (System.IO.File.Exists(p))
                                        {
                                            bi = new BitmapImage();
                                            bi.BeginInit();
                                            bi.CacheOption = BitmapCacheOption.OnLoad;
                                            bi.UriSource = new Uri(p);
                                            bi.EndInit();
                                            bi.Freeze();
                                        }
                                    }
                                    if (bi != null)
                                    {
                                        chainUpsideSimImage = new FormatConvertedBitmap(bi, PixelFormats.Pbgra32, null, 0);
                                    }
                                }
                                catch { }
                            }
                            if (chainUpsideSimImage != null) chosenSprite = chainUpsideSimImage;
                        }
                    }
                    if (animationFrames != null && animationFrames.TryGetValue(s_vis, out var frames) && frames != null && frames.Length > 0)
                    {
                        // For 2-frame decoration sprites we want a uniform cadence across all anchors
                        // so do not apply a per-anchor random offset. For other sprites/lengths, preserve
                        // per-anchor randomness so placements don't always animate in lockstep.
                        int offset = 0;
                        if (!(decorationSpriteIds.Contains(s) && frames.Length == 2))
                        {
                            if (!spriteFrameOffsets.ContainsKey(idx)) spriteFrameOffsets[idx] = spriteAnimationRandom.Next(0, Math.Max(1, frames.Length));
                            offset = spriteFrameOffsets[idx];
                        }
                        int frame = 0;
                        if (frames.Length == 2 && (decorationSpriteIds.Contains(s)
                                                     || s == 0x54 || s == 0x55
                                                     // Dash-orbs: sync them to the same two-frame decoration cadence
                                                     || s == 0x45 || s == 0x46 || s == 0x4C || s == 0x4D
                                                     || s == 0x50 || s == 0x51 || s == 0x5B || s == 0x5C
                                                     || s == 0x5D || s == 0x5E))
                        {
                            // Match editor preview two-frame cadence used by dash-orbs and decorations
                            // Make spider-orbs (0x54/0x55) pulse exactly like the dash-orb routine,
                            // but do NOT treat them as decorations (they are excluded from tinting).
                            frame = (((GetEditorAnimationFrameValue() * 3) / 40) % 2 + 2) % 2;
                        }
                        else
                        {
                            // Orbs include: 0x08-0x0B (yellow/pink/red), plus dash-orbs 0x45,0x46,0x4C,0x4D,0x50,0x51,0x5B,0x5C,0x5D,0x5E
                            bool isOrb = (s >= 0x08 && s <= 0x0B) 
                                      || s == 0x45 || s == 0x46 || s == 0x4C || s == 0x4D
                                      || s == 0x50 || s == 0x51 || s == 0x5B || s == 0x5C
                                      || s == 0x5D || s == 0x5E;
                            
                            // Slow down coins/pads/orbs and decorations to half speed
                            if (slowAnimatedSpriteIds.Contains(s) || decorationSpriteIds.Contains(s))
                            {
                                frame = (((GetEditorAnimationFrameValue() * 9) / 40) + offset) % Math.Max(1, frames.Length);
                            }
                            else if (isOrb)
                            {
                                // Orbs use slowest animation speed
                                frame = (((GetEditorAnimationFrameValue() * 9) / 60) + offset) % Math.Max(1, frames.Length);
                            }
                            else
                            {
                                // Pads and other sprites - slowed down from /20 to /40 for half speed
                                frame = (((GetEditorAnimationFrameValue() * 9) / 40) + offset) % Math.Max(1, frames.Length);
                            }
                        }
                        chosenSprite = frames[frame];
                        // Debug: log decoration frames presence/selection when the selected frame changes
                        try
                        {
                            if (enableSimulatorDebugLogging && decorationSpriteIds.Contains(s) && frames.Length == 2)
                            {
                                int prevFrame = -1;
                                decoLastSelectedFrame.TryGetValue(idx, out prevFrame);
                                if (prevFrame != frame)
                                {
                                    string h0 = frames[0] != null ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(frames[0]!).ToString("X8") : "null";
                                    string h1 = frames[1] != null ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(frames[1]!).ToString("X8") : "null";
                                    WriteTempLog($"Simulator: Decoration sprite idx={idx} id=0x{s:X} frames=[{(frames[0]!=null?"ok":"null")},{(frames[1]!=null?"ok":"null")}] selectedFrame={frame} hashes=[{h0},{h1}]");
                                    decoLastSelectedFrame[idx] = frame;
                                }
                            }
                        }
                        catch { }
                            // (no-op) removed per-request debug logging
                        if (chosenSprite == null)
                        {
                            if (forcePreviewMode && previewSpriteMap != null && previewSpriteMap.TryGetValue(s_vis, out var pimg) && pimg != null)
                                chosenSprite = pimg;
                            else if (spriteImages != null && s_vis < spriteImages.Length && spriteImages[s_vis] != null)
                                chosenSprite = spriteImages[s_vis];
                        }
                    }
                    else
                    {
                        // Special-case rainbow portal (0x64): cycle through the ordered portal
                        // preview images deterministically per-position and advance by animation frame.
                        if (s == 0x64)
                        {
                            try
                            {
                                if (previewSpriteMap != null)
                                {
                                    int[] orderIds = new int[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x24, 0x17, 0x4B, 0x58, 0x6A, 0x6B, 0x6C };
                                    var list = new System.Collections.Generic.List<ImageSource?>();
                                    foreach (var id in orderIds)
                                    {
                                        if (previewSpriteMap.TryGetValue(id, out var img) && img != null) list.Add(img);
                                    }
                                    if (list.Count > 0)
                                    {
                                        int len = list.Count;
                                        int frameAdvance = 0;
                                        try { frameAdvance = (animationFrame / 8) % Math.Max(1, len); } catch { frameAdvance = 0; }
                                        uint seed = (uint)idx; // deterministic per-position offset (matches editor behavior)
                                        uint offset = (uint)((seed * 2654435761u) % (uint)len);
                                        int sel = (int)((offset + (uint)frameAdvance) % (uint)len);
                                        chosenSprite = list[sel];
                                    }
                                }
                            }
                            catch { }
                        }

                        if (chosenSprite == null)
                        {
                            if (forcePreviewMode && previewSpriteMap != null && previewSpriteMap.TryGetValue(s_vis, out var previewImg) && previewImg != null)
                            {
                                chosenSprite = previewImg;
                            }
                            else if (spriteImages != null && s_vis < spriteImages.Length && spriteImages[s_vis] != null)
                            {
                                chosenSprite = spriteImages[s_vis];
                            }
                        }
                    }

                    if (chosenSprite == null) continue;
                    // Always hide black color-trigger sprites (they should activate but not be visible in simulator)
                    if (s == 0x8F || s == 0xCF) continue;
                    if (hideColorTriggers && IsColorTriggerSprite(s)) continue;

                    // Apply player tint to decoration sprites when enabled
                    try
                    {
                            // No special-case preview override for 0x3D: let the normal selection/fallbacks apply.

                        if (playerTintEnabled && decorationSpriteIds.Contains(s) && !nonPlayerTintSpriteIds.Contains(s) && chosenSprite != null)
                        {
                            chosenSprite = GetPlayerTintedSprite(chosenSprite, s_vis);
                        }
                    }
                    catch { }

                    double px = (mapX - startTileX) * TILE - offsetX;
                    double py = (vy * TILE) - offsetY + gridRenderShiftYPx;
                    if (spriteAnchors != null && spriteAnchors.TryGetValue(idx, out var anchor))
                    {
                        int storageTileX = idx % mapWidth;
                        int storageTileY = idx / mapWidth;
                        int tileDeltaX = storageTileX - anchor.anchorTileX;
                        int tileDeltaY = storageTileY - anchor.anchorTileY;
                        double anchorDisplayX = (anchor.anchorTileX - startTileX) * TILE - offsetX;
                        double anchorDisplayY = (anchor.anchorTileY - (startTileY + groundRowsToReserve)) * TILE - offsetY;
                        px = anchorDisplayX + tileDeltaX * TILE;
                        py = anchorDisplayY + tileDeltaY * TILE + gridRenderShiftYPx;
                    }
                    if (spritePixelOffsets != null && spritePixelOffsets.TryGetValue(idx, out var offs))
                    {
                        px += offs.offsetX;
                        py += offs.offsetY; // match editor convention: positive offsetY moves sprite down
                    }
                    else if (spriteAnchors != null && spriteAnchors.TryGetValue(idx, out var anc))
                    {
                        int anchorKey = anc.anchorTileY * mapWidth + anc.anchorTileX;
                        if (spritePixelOffsets != null && spritePixelOffsets.TryGetValue(anchorKey, out var aoffs))
                        {
                            px += aoffs.offsetX;
                            py += aoffs.offsetY; // match editor convention
                        }
                    }

                    // Simulator tweak: medium poles (sprite id 0x2B and 0x2C) render 8px higher in preview
                    try
                    {
                        if (s == 0x2B || s == 0x2C) py -= 8;
                        // Shift long poles (sprite 0x2A/0x3A) upwards by 1.5 tiles in the simulator
                        if (s == 0x2A || s == 0x3A)
                        {
                            try
                            {
                                // Move up 1.5 tiles, then correct by moving down 8px per latest request
                                int shift = (int)Math.Round(TILE * 1.5) - 8; // net 1 tile (24-8=16)
                                py = Math.Max(0, py - shift);
                            }
                            catch { }
                        }
                        // Upright chains (0x2D) are nudged up to match preview; do not special-case 0x3D.
                        if (s == 0x2D) py -= 8;
                        // If this decoration sprite appears upside-down (content at top), nudge it up as well.
                        try
                        {
                            if (decorationSpriteIds.Contains(s) && s != 0x2D && chosenSprite is BitmapSource cbs && cbs.PixelHeight <= (TILE / 2.0))
                            {
                                if (IsImageTopHeavy(cbs)) py -= 8;
                            }
                        }
                        catch { }
                    }
                    catch { }

                    // get pooled image
                    System.Windows.Controls.Image simg;
                    if (spritesInUse < spritePool.Count)
                    {
                        simg = spritePool[spritesInUse];
                        simg.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        simg = new System.Windows.Controls.Image { Stretch = Stretch.None };
                        System.Windows.Media.RenderOptions.SetBitmapScalingMode(simg, BitmapScalingMode.NearestNeighbor);
                        spritePool.Add(simg);
                        RenderCanvas.Children.Add(simg);
                    }
                    // Force-refresh the Image control to ensure WPF updates when the source changes
                    try { simg.Source = null; } catch { }
                    ImageSource? finalSprite = chosenSprite;
                    try
                    {
                        // For decoration sprites, composite the sprite over the rendered tile layer
                        // (or fallback to the flat background tint) so semi-transparent edges blend
                        // seamlessly with the exact underlying pixels instead of a flat color.
                        if (chosenSprite is BitmapSource cbs && decorationSpriteIds.Contains(s) && backgroundTint.A > 0)
                        {
                            int ix = (int)Math.Round(px);
                            int iy = (int)Math.Round(py);
                            var comp = CompositeSpriteOverBackgroundAt(chosenSprite, backgroundTint, ix, iy);
                            if (comp != null) finalSprite = comp;
                        }
                    }
                    catch { }

                    simg.Source = App.EnsureUnfrozenForRender(finalSprite) ?? finalSprite;
                    if (finalSprite is BitmapSource fbs) { simg.Width = fbs.PixelWidth; simg.Height = fbs.PixelHeight; }
                    System.Windows.Controls.Canvas.SetLeft(simg, px);
                    System.Windows.Controls.Canvas.SetTop(simg, py);
                    spritesInUse++;

                    // Cache the world-space hitbox rect derived from the same values the renderer
                    // used so collisions can match the visible sprite even when overlays are off.
                    try
                    {
                        int id_for_overlay = s & 0xFF;
                        if (spriteAnchors != null && spriteAnchors.TryGetValue(idx, out var anch2))
                        {
                            int anchorKey2 = anch2.anchorTileY * mapWidth + anch2.anchorTileX;
                            if (anchorKey2 >= 0 && anchorKey2 < sprites.Length)
                            {
                                int anchoredId2 = sprites[anchorKey2];
                                if (anchoredId2 >= 0 && anchoredId2 < 256) id_for_overlay = anchoredId2 & 0xFF;
                            }
                        }

                        int hw_o = 0x10; int hh_o = 0x10; int hxoff_o = 0; int hyoff_o = 0;
                        if (id_for_overlay >= 0 && id_for_overlay < sprite_widths.Length) hw_o = sprite_widths[id_for_overlay];
                        if (id_for_overlay >= 0 && id_for_overlay < sprite_heights.Length) hh_o = sprite_heights[id_for_overlay];
                        if (id_for_overlay >= 0 && id_for_overlay < sprite_x_offset.Length) hxoff_o = sprite_x_offset[id_for_overlay];
                        if (id_for_overlay >= 0 && id_for_overlay < sprite_y_offset.Length) hyoff_o = sprite_y_offset[id_for_overlay];

                        try
                        {
                            if (hw_o == TILE && hh_o == TILE && finalSprite is BitmapSource fbs3)
                            {
                                hw_o = Math.Max(1, fbs3.PixelWidth);
                                hh_o = Math.Max(1, fbs3.PixelHeight);
                            }
                        }
                        catch { }

                        double hx_screen = (int)Math.Round(px) + hxoff_o;
                        double hy_screen = (int)Math.Round(py) + hyoff_o;
                        var padDownIds2 = new System.Collections.Generic.HashSet<int> { 0x52, 0x0A, 0x0D, 0x25, 0xFD };
                        if (padDownIds2.Contains(id_for_overlay)) hy_screen += 8;

                        int pixelX_now2 = cameraX_fixed >> 8;
                        int pixelY_now2 = cameraY_fixed >> 8;
                        int worldLeft2 = (int)Math.Round(hx_screen) + pixelX_now2;
                        int worldTop2 = (int)Math.Round(hy_screen) + pixelY_now2 - gridRenderShiftYPx;
                        int worldRight2 = worldLeft2 + Math.Max(1, hw_o) - 1;
                        int worldBottom2 = worldTop2 + Math.Max(1, hh_o) - 1;
                        try { hitboxWorldCache[idx] = (worldLeft2, worldTop2, worldRight2, worldBottom2, renderFrameCounter); } catch { }
                    }
                    catch { }

                    // Draw hitbox overlay if requested and this is not a color-trigger sprite
                    try
                    {
                        // Only render overlays when the main editor option is enabled and
                        // this simulator's instance toggle is set.
                        if (MainWindow.Option_ShowSimulatorSpriteHitboxes && ShowSpriteHitboxes && !IsColorTriggerSprite(s) && !decorationSpriteIds.Contains(s))
                        {
                            System.Windows.Shapes.Rectangle hrect;
                            if (hitboxesInUse < hitboxPool.Count)
                            {
                                hrect = hitboxPool[hitboxesInUse];
                                hrect.Visibility = Visibility.Visible;
                            }
                            else
                            {
                                hrect = new System.Windows.Shapes.Rectangle();
                                hrect.Fill = new SolidColorBrush(Color.FromArgb(96, 255, 255, 0)); // translucent yellow
                                hrect.Stroke = new SolidColorBrush(Color.FromArgb(160, 255, 200, 0));
                                hrect.StrokeThickness = 1;
                                hrect.IsHitTestVisible = false;
                                hitboxPool.Add(hrect);
                                RenderCanvas.Children.Add(hrect);
                            }

                            // Determine hitbox from sprite tables; prefer anchor's sprite id for geometry when anchored
                            int id = s & 0xFF;
                            // Use the rendered `px`/`py` (which already include anchor tileDelta adjustments)
                            // as the hitbox base so the overlay aligns with the visible sprite instance.
                            int hitbase_px_x = (int)Math.Round(px);
                            int hitbase_px_y = (int)Math.Round(py);
                            if (spriteAnchors != null && spriteAnchors.TryGetValue(idx, out var anch))
                            {
                                int anchorKey = anch.anchorTileY * mapWidth + anch.anchorTileX;
                                if (anchorKey >= 0 && anchorKey < sprites.Length)
                                {
                                    int anchoredId = sprites[anchorKey];
                                    if (anchoredId >= 0 && anchoredId < 256) id = anchoredId & 0xFF;
                                }

                                // Do not re-apply anchor pixel offsets here: `px`/`py` already include
                                // any per-position or anchor pixel offsets earlier in the renderer.
                            }

                            int hw = 0x10; int hh = 0x10; int hxoff = 0; int hyoff = 0;
                            if (id >= 0 && id < sprite_widths.Length) hw = sprite_widths[id];
                            if (id >= 0 && id < sprite_heights.Length) hh = sprite_heights[id];
                            if (id >= 0 && id < sprite_x_offset.Length) hxoff = sprite_x_offset[id];
                            if (id >= 0 && id < sprite_y_offset.Length) hyoff = sprite_y_offset[id];

                            // If the hitbox table entry is the default TILE size but the final sprite
                            // image used for rendering is larger, prefer the image size for the overlay
                            // so the drawn rectangle reflects the actual graphic.
                            try
                            {
                                if (hw == TILE && hh == TILE && finalSprite is BitmapSource fbs2)
                                {
                                    hw = Math.Max(1, fbs2.PixelWidth);
                                    hh = Math.Max(1, fbs2.PixelHeight);
                                }
                            }
                            catch { }

                            double hx = hitbase_px_x + hxoff; // screen-space base + offsets
                            double hy = hitbase_px_y + hyoff;
                            // If this sprite is a non-upside-down pad, shift hitbox down an extra 8 px on top of specified offsets
                            // Known non-upside-down pad IDs include typical down variants; extend set as needed.
                            var padDownIds = new System.Collections.Generic.HashSet<int> { 0x52, 0x0A, 0x0D, 0x25, 0xFD };
                            if (padDownIds.Contains(id)) hy += 8;
                            hrect.Width = Math.Max(1, hw);
                            hrect.Height = Math.Max(1, hh);
                            System.Windows.Controls.Canvas.SetLeft(hrect, hx);
                            System.Windows.Controls.Canvas.SetTop(hrect, hy);
                            
                            hitboxesInUse++;
                        }
                    }
                    catch { }

                    
                }
            }

            // Hide remaining pooled images
            for (int i = spritesInUse; i < spritePool.Count; i++) spritePool[i].Visibility = Visibility.Collapsed;

            // Hide remaining hitboxes
            for (int i = hitboxesInUse; i < hitboxPool.Count; i++) hitboxPool[i].Visibility = Visibility.Collapsed;
            // reset hitbox counter for next frame
            hitboxesInUse = 0;

            // Position the player visual based on world Y (`playerY_fixed`) and camera Y
            try
            {
                int playerPixelX, playerPixelY;
                int worldX, worldY;
                lock (simLock)
                {
                    playerPixelX = (playerX_fixed >> 8) - (cameraX_fixed >> 8);
                    playerPixelY = (playerY_fixed >> 8) - (cameraY_fixed >> 8) + gridRenderShiftYPx;
                    
                    // Record center of player for path (use actual physics position)
                    worldX = (playerX_fixed >> 8) + (playerVisualWidth / 2);
                    
                    // For path, use center of collision hitbox
                    // Mini mode: 8x7 hitbox, positioned at Y+9 (normal) or Y+0 (inverted) in 16x16 space
                    // Normal mode: 15x15 hitbox at Y+0
                    if (miniMode)
                    {
                        if (gravityFlipped)
                        {
                            // Inverted: hitbox at Y+0, center at Y+3.5 → round to Y+4
                            worldY = (playerY_fixed >> 8) + 4;
                        }
                        else
                        {
                            // Normal: hitbox at Y+9, center at Y+9+3.5 → round to Y+13
                            worldY = (playerY_fixed >> 8) + 13;
                        }
                    }
                    else
                    {
                        // Center of 15x15 normal hitbox (at Y+0)
                        worldY = (playerY_fixed >> 8) + 8;
                    }
                }

                // For mini mode, adjust visual position based on gravity
                // Mini sprites are 8x8 pixels, normal sprites are 16x16 pixels
                // Normal gravity: align bottom-left (shift down 8 pixels for visual centering)
                // Reversed gravity: align top-left (no shift)
                // NOTE: This is visual offset (8px), collision uses (0x10-0x07)>>1 = 4px
                if (miniMode && !gravityFlipped)
                {
                    // Shift down by (16 - 8) = 8 pixels to align bottom visually
                    playerPixelY += (TILE - 8);
                    
                    // Additional -1 pixel adjustment for ship/ball/UFO/wave/swingcopter to prevent visual sinking
                    // Cube/robot/ninja use -1 collision offset which works, but ship/ball/UFO/wave need visual adjustment
                    if (currentGameMode == 1 || currentGameMode == 2 || currentGameMode == 3 || currentGameMode == 6 || currentGameMode == 7)
                    {
                        playerPixelY -= 1;
                    }
                }

                // Path recording moved to SimulateNumericStep for better performance (60Hz instead of 144Hz+)

                // Hide player completely in cam mode
                if (camModeActive)
                {
                    if (playerImage != null) playerImage.Visibility = Visibility.Collapsed;
                    if (playerRect != null) playerRect.Visibility = Visibility.Collapsed;
                }
                else if (playerImage != null && playerImage.Source != null)
                {
                    System.Windows.Controls.Canvas.SetLeft(playerImage, playerPixelX);
                    System.Windows.Controls.Canvas.SetTop(playerImage, playerPixelY);
                    playerImage.Visibility = Visibility.Visible;
                    if (playerRect != null) playerRect.Visibility = Visibility.Collapsed;
                }
                else if (playerRect != null)
                {
                    System.Windows.Controls.Canvas.SetLeft(playerRect, playerPixelX);
                    System.Windows.Controls.Canvas.SetTop(playerRect, playerPixelY);
                    playerRect.Visibility = Visibility.Visible;
                }
            }
            catch { }

            // Apply sub-pixel smoothing with a translate transform for X and Y
            // Stabilize X fractional translation when the player is anchored at the interaction line
            int centerOffset_fixed_local = (TILE / 2) << 8;
            int playerCenter_fixed_now_local = playerX_fixed + centerOffset_fixed_local;
            bool isAnchoredNow = interactionScreenOffset_px >= 0 && playerCenter_fixed_now_local >= INTERACTION_LINE_FIXED;

            double fracX = (cameraX_fixed & 0xFF) / 256.0;
            double fracY = (cameraY_fixed & 0xFF) / 256.0;

            if (isAnchoredNow)
            {
                // Force fractional X to zero while anchored to avoid 1-px jitter when camera follows
                fracX = 0.0;
            }

            RenderCanvas.RenderTransform = new TranslateTransform(-fracX, -fracY);

            // Update Y-velocity overlay if enabled
            try
            {
                if (showYVelocityOverlay)
                {
                    if (yVelTextBlock == null)
                    {
                        yVelTextBlock = new System.Windows.Controls.TextBlock();
                        yVelTextBlock.Foreground = new SolidColorBrush(Colors.Yellow);
                        yVelTextBlock.FontWeight = FontWeights.Bold;
                        yVelTextBlock.FontSize = 14;
                        yVelTextBlock.IsHitTestVisible = false;
                        RenderCanvas.Children.Add(yVelTextBlock);
                        try { System.Windows.Controls.Canvas.SetZIndex(yVelTextBlock, 2000); } catch { }
                    }
                    try
                    {
                        // Show fixed-point Y velocity as hex (and decimal px/frame for convenience)
                        int v_fixed = playerVelY_fixed; // fixed-point (8 frac bits)
                        double vel_px = v_fixed / 256.0;
                        string hex;
                        if (v_fixed < 0) hex = "-0x" + ((-v_fixed) & 0xFFFF).ToString("X4");
                        else hex = "0x" + (v_fixed & 0xFFFF).ToString("X4");
                        yVelTextBlock.Text = $"Y vel: {hex}  ({vel_px:F2} px/frame)\nInvertedGravity: {gravityReversed}";
                        yVelTextBlock.Visibility = Visibility.Visible;
                        System.Windows.Controls.Canvas.SetLeft(yVelTextBlock, 4);
                        System.Windows.Controls.Canvas.SetTop(yVelTextBlock, 4);
                    }
                    catch { }
                }
                else
                {
                    try { if (yVelTextBlock != null) yVelTextBlock.Visibility = Visibility.Collapsed; } catch { }
                }
            }
            catch { }

            // Update debug info window if visible
            try
            {
                if (debugWindow != null && debugWindow.IsLoaded)
                {
                    // Determine speed string
                    string speedStr = speed switch
                    {
                        0 => "0.5x",
                        1 => "1x",
                        2 => "2x",
                        3 => "3x",
                        4 => "4x",
                        5 => "0.1x",
                        _ => $"{speed}x"
                    };
                    
                    debugWindow.UpdateDebugInfo(
                        currentGameMode,
                        currplayer_mini != 0,
                        gravityFlipped,
                        playerX_fixed >> 8,
                        playerY_fixed >> 8,
                        speedStr,
                        ninjaJumps,
                        dblocked,
                        hblocked,
                        fblocked,
                        jblocked,
                        orbed,
                        blackOrbed,
                        dashing,
                        robotJumpTime[0],
                        playerVelY_fixed,
                        orbBufferActive
                    );
                }
            }
            catch { }
        }

        // Ensure initial render is performed on the UI thread so toned images and
        // tile-layer caches are built before the window is shown. Call from owner
        // before showing the simulator to guarantee the paused snapshot reflects
        // starting tints.
        public void EnsureInitialRender()
        {
            try
            {
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.Invoke(new Action(() => {
                        try { RenderFrame(); } catch { }
                        try { if (pendingTintChange || pendingTintChangeIsStartup) { try { ApplyPendingTints(); } catch { } try { RenderFrame(); } catch { } } } catch { }
                    }));
                }
                else
                {
                    try { RenderFrame(); } catch { }
                    try { if (pendingTintChange || pendingTintChangeIsStartup) { try { ApplyPendingTints(); } catch { } try { RenderFrame(); } catch { } } } catch { }
                }
            }
            catch { }
        }

        // Use CompositionTarget.Rendering as the main loop to maintain consistent timing. We implement
        // a simple fixed-step simulation so animation and camera advance at 60Hz even if rendering
        // intermittently lags.
        private void CompositionTarget_Rendering(object? sender, EventArgs e)
        {
            if (windowClosed) return;
            
            try
            {
                // Advance a UI-driven animation counter when the numeric sim isn't running
                // or when the sim is paused so decorative/preview animations remain active.
                try
                {
                    double now = renderStopwatch.Elapsed.TotalMilliseconds;
                    double delta = Math.Max(0.0, now - uiAnimLastMs);
                    uiAnimLastMs = now;
                    uiAnimAccumulatedMs += delta;
                    // Advance UI-driven animation counter regardless of pause state so
                    // decorative/preview animations (eg. rainbow portal) continue animating
                    // even when numeric simulation is paused.
                    while (uiAnimAccumulatedMs >= SIM_STEP_MS)
                    {
                        animationFrame++;
                        uiAnimAccumulatedMs -= SIM_STEP_MS;
                    }
                }
                catch { }

                // If paused, still render the current frame and show the pause overlay.
                if (paused)
                {
                    RenderFrame();
                    try { if (!deathTriggered) PauseOverlay.Visibility = System.Windows.Visibility.Visible; else PauseOverlay.Visibility = System.Windows.Visibility.Collapsed; } catch { }
                    return;
                }

                // If the simulation flagged a pending tint change, apply it here on the UI thread.
                if (pendingTintChange)
                {
                    try { ApplyPendingTints(); } catch { }
                }

                // Ensure overlay is not visible while running
                try { PauseOverlay.Visibility = System.Windows.Visibility.Collapsed; } catch { }
                RenderFrame();
            }
            catch { }
        }

        // Perform numeric-only simulation step on a background thread at ~60Hz.
        private void SimulateNumericStep()
        {
            // Keep previous camera center for later anchor detection
            int prevCameraCenter_fixed;
            int prevPlayerCenter_fixed;
            int attemptedPlayerX_fixed;
            int attemptedPlayerCenter_fixed;
            int speedMultiplierLocal;
            int centerOffset_fixed = (TILE / 2) << 8;

            lock (simLock)
            {
                // CRITICAL: Check if restart is in progress or window closed
                // If so, exit immediately to allow restart to acquire the lock and avoid deadlock
                if (restartInProgress || windowClosed) return;
                
                // Respect pause: do not advance numeric simulation when paused.
                if (paused) return;
                AppendSimDebug($"[STEP_START] playerY_fixed=0x{playerY_fixed:X4} ({playerY_fixed >> 8}px), playerVelY_fixed=0x{playerVelY_fixed:X4}");
                prevCameraCenter_fixed = cameraX_fixed + ((NES_W * TILE / 2) << 8);
                prevPlayerCenter_fixed = playerX_fixed + centerOffset_fixed;

                speedMultiplierLocal = tabSpeedMultiplier; // atomic read of volatile-like field
                attemptedPlayerX_fixed = playerX_fixed + (int)Math.Round((currentSpeed_fixed * speedMultiplierLocal) * simTimeScale);
                attemptedPlayerCenter_fixed = attemptedPlayerX_fixed + centerOffset_fixed;

                // === SPRITE INTERACTIONS (BEFORE MOVEMENT) ===
                // Check sprite interactions with the player's CURRENT position before moving
                // This ensures portals/pads/orbs are detected before the player moves past them
                if (physicsEnabled)
                {
                    try
                    {
                        // Reset orb/pad activation flag for this frame (will be set if collision occurs)
                        orbhitonthisframe = false;
                        
                        // Check for gravity portal activation
                        CheckGravityPortals();
                        AppendSimDebug($"[GRAV_PRE_MOVEMENT] currplayer_gravity={currplayer_gravity:X2} gravityFlipped={gravityFlipped} gravityReversed={gravityReversed}");
                        
                        // Check for mini/growth portal activation
                        CheckMiniGrowthPortals();
                        
                        // Check for pad collision
                        CheckPadCollision();
                        CheckSpiderOrbPadCollision();
                        CheckDashOrbCollision();
                        CheckAlphabetBlocks();
                        
                        // Check for blue pad collision
                        CheckBluePadCollision();
                    }
                    catch { }
                }

                // Move the player forward in world coordinates AFTER sprite interactions
                playerX_fixed = attemptedPlayerX_fixed;
                
                // === COLLISION/GROUNDING DISABLED ===
                /*
                // When player moves horizontally while considered grounded in the numeric
                // simulation, clear the stabilization counter and verify support so
                // walking off surfaces resumes gravity on the same frame.
                if (onGround)
                {
                    groundStabilizeCounter = 0;
                    try
                    {
                        bool stillSupported = false;
                        if (gravityReversed)
                        {
                            stillSupported = IsTouchingCeiling();
                        }
                        else
                        {
                            const int HITBOX_W_LOCAL = 15;
                            int playerCenter_px = (playerX_fixed >> 8) + (playerVisualWidth / 2);
                            int playerLeft_px = playerCenter_px - (HITBOX_W_LOCAL / 2);
                            int playerRight_px = playerLeft_px + (HITBOX_W_LOCAL - 1);
                            int footWorldY_px = (playerY_fixed >> 8) + playerVisualHeight - 1;
                            int tileBelowY_world = footWorldY_px / TILE;
                            int groundRowsToReserve_local = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                            int tileIndexY = tileBelowY_world + groundRowsToReserve_local;
                            if (tileIndexY >= 0 && tileIndexY < mapHeight)
                            {
                                for (int tx = playerLeft_px / TILE; tx <= playerRight_px / TILE; tx++)
                                {
                                    if (tx < 0 || tx >= mapWidth) continue;
                                    int tid = tiles[tileIndexY * mapWidth + tx];
                                    int useTidForAnim = MapAnimatedTileIndex(tid);
                                    int collisionTid = useTidForAnim;
                                    if (useTidForAnim >= 1000)
                                    {
                                        if (useTidForAnim >= 1000 && useTidForAnim <= 1007)
                                            collisionTid = 0x08 + ((useTidForAnim - 1000) % 4);
                                        else if (useTidForAnim >= 1010 && useTidForAnim <= 1015)
                                        {
                                            int group = (useTidForAnim - 1010) % 3;
                                            collisionTid = (group == 0) ? 0x04 : (group == 1) ? 0x7D : 0x7F;
                                        }
                                        else if (useTidForAnim >= 1020 && useTidForAnim <= 1037)
                                            collisionTid = 0x74 + ((useTidForAnim - 1020) % 9);
                                        else
                                            collisionTid = tid;
                                    }
                                    var col = MetatileCollisionTable.GetCollision((byte)collisionTid);
                                    int tileStartX = tx * TILE;
                                    int localX = Math.Max(0, Math.Min(TILE - 1, playerCenter_px - tileStartX));
                                    if (ProvidesFloorAtColumnStatic(col, localX, out int _)) { stillSupported = true; break; }
                                }
                            }
                        }

                        if (!stillSupported) onGround = false;
                    }
                    catch { onGround = false; }
                }
                */
                // === END COLLISION/GROUNDING DISABLED ===

                    // Atomically consume any jump-buffer frames at the start of the physics step
                    // so landing code can check a stable value. We clear the buffer here and
                    // test the captured value when resolving landing to avoid races with UI thread.
                            int jumpBuffered_local = Interlocked.Exchange(ref jumpBufferCounter, 0);
                            // Read keyX pressed count without clearing so we can respect held/edge presses
                            int pendingKeyX_local = Interlocked.CompareExchange(ref keyXPressedCount, 0, 0);

                // Interaction crossing detection
                bool crossedInteraction = prevPlayerCenter_fixed < INTERACTION_LINE_FIXED && attemptedPlayerCenter_fixed >= INTERACTION_LINE_FIXED;
                if (crossedInteraction)
                {
                    interactionScreenOffset_px = (INTERACTION_LINE_FIXED >> 8) - (cameraX_fixed >> 8);
                }

                int playerCenter_fixed_now = playerX_fixed + centerOffset_fixed;
                if (playerCenter_fixed_now >= INTERACTION_LINE_FIXED)
                {
                    if (interactionScreenOffset_px >= 0)
                    {
                        cameraX_fixed = playerX_fixed - (interactionScreenOffset_px << 8);
                    }
                    else
                    {
                        cameraX_fixed += attemptedPlayerCenter_fixed - INTERACTION_LINE_FIXED;
                    }

                    int maxCamera_fixed = Math.Max(0, (mapWidth - NES_W) * TILE) << 8;
                    if (cameraX_fixed < 0) cameraX_fixed = 0;
                    if (cameraX_fixed > maxCamera_fixed) cameraX_fixed = maxCamera_fixed;
                }
                else
                {
                    interactionScreenOffset_px = -1;
                }

                // Advance animation frame (handled by fixed-step simulation loop)

                // Vertical player movement & camera-follow behavior (numeric sim path)
                const int vStep_fixed_local = 512; // 2 px/frame
                int maxCameraY_fixed_local = Math.Max(0, (mapHeight - NES_H) * TILE) << 8;
                int maxPlayerY_fixed_local = Math.Max(0, (mapHeight * TILE - playerVisualHeight)) << 8;

                if (upHeld)
                {
                    // Use player's screen center Y for scrolling decisions to match camera panning behavior
                    int playerCenterScreenY_local = (playerY_fixed >> 8) + (playerVisualHeight / 2) - (cameraY_fixed >> 8);
                    int topThreshold_local = 5 * TILE; // 5 tiles from top

                    if (!jumpedOnce && camModeActive)
                    {
                        // Before first jump: Up acts as camera-only
                        if (cameraY_fixed > 0)
                        {
                            cameraY_fixed -= vStep_fixed_local;
                            if (cameraY_fixed < 0) cameraY_fixed = 0;
                        }
                    }
                    else
                    {
                        if (!physicsEnabled && camModeActive)
                        {
                            // Move player up
                            playerY_fixed -= vStep_fixed_local;
                            if (playerY_fixed < 0) playerY_fixed = 0;

                            // Recompute center after moving the player
                            playerCenterScreenY_local = (playerY_fixed >> 8) + (playerVisualHeight / 2) - (cameraY_fixed >> 8);
                            // If player's center is at or above the threshold, scroll camera up to follow
                            if (playerCenterScreenY_local <= topThreshold_local)
                            {
                                int need = topThreshold_local - playerCenterScreenY_local;
                                int camMove = Math.Min(need, (cameraY_fixed >> 8));
                                cameraY_fixed -= (camMove << 8);
                                if (cameraY_fixed < 0) cameraY_fixed = 0;
                            }
                        }
                        else
                        {
                            // Physics active: don't move player Y directly. Allow camera to scroll up if needed (only if cam mode active).
                            if (camModeActive)
                            {
                                if (playerCenterScreenY_local <= topThreshold_local)
                                {
                                    int need = topThreshold_local - playerCenterScreenY_local;
                                    int camMove = Math.Min(need, (cameraY_fixed >> 8));
                                    cameraY_fixed -= (camMove << 8);
                                    if (cameraY_fixed < 0) cameraY_fixed = 0;
                                }
                                else
                                {
                                    // Manual camera pan while physics is enabled: allow small step when holding Up
                                    if (cameraY_fixed > 0)
                                    {
                                        cameraY_fixed -= vStep_fixed_local;
                                        if (cameraY_fixed < 0) cameraY_fixed = 0;
                                    }
                                }
                            }
                        }
                    }
                }

                        

                if (downHeld)
                {
                    int bottomThresholdBottom_local = NES_H * TILE - 5 * TILE; // 5 tiles from bottom (measured from bottom edge)
                    int playerScreenY = (playerY_fixed >> 8) - (cameraY_fixed >> 8);
                    int playerScreenBottom_local = playerScreenY + playerVisualHeight;

                    if (!jumpedOnce && camModeActive)
                    {
                        // Before first jump: Down pans camera only
                        if (cameraY_fixed < maxCameraY_fixed_local)
                        {
                            cameraY_fixed += vStep_fixed_local;
                            if (cameraY_fixed > maxCameraY_fixed_local) cameraY_fixed = maxCameraY_fixed_local;
                        }
                    }
                    else
                    {
                        // If player's bottom is above the bottom threshold, allow moving player down.
                        // If player's bottom has reached or passed the threshold, scroll the camera instead.
                        if (playerScreenBottom_local < bottomThresholdBottom_local)
                        {
                            if (!physicsEnabled && camModeActive)
                            {
                                // Move player down
                                playerY_fixed += vStep_fixed_local;
                                if (playerY_fixed > maxPlayerY_fixed_local) playerY_fixed = maxPlayerY_fixed_local;
                            }
                            else
                            {
                                // Physics active: don't move player Y directly here; camera remains stationary unless thresholds
                            }
                        }
                        else
                        {
                            // Scroll camera down first until it reaches bottom (only if cam mode active)
                            if (camModeActive && cameraY_fixed < maxCameraY_fixed_local)
                            {
                                // Scroll camera down
                                cameraY_fixed += vStep_fixed_local;
                                if (cameraY_fixed > maxCameraY_fixed_local) cameraY_fixed = maxCameraY_fixed_local;
                                // Only advance player when physics is not enabled
                                if (!physicsEnabled)
                                {
                                    playerY_fixed += vStep_fixed_local;
                                    if (playerY_fixed > maxPlayerY_fixed_local) playerY_fixed = maxPlayerY_fixed_local;
                                }
                            }
                            else if (camModeActive)
                            {
                                // Manual camera pan while physics is enabled: allow small step when holding Down
                                if (cameraY_fixed < maxCameraY_fixed_local)
                                {
                                    cameraY_fixed += vStep_fixed_local;
                                    if (cameraY_fixed > maxCameraY_fixed_local) cameraY_fixed = maxCameraY_fixed_local;
                                }
                                else
                                {
                                    if (!physicsEnabled)
                                    {
                                        playerY_fixed += vStep_fixed_local;
                                        if (playerY_fixed > maxPlayerY_fixed_local) playerY_fixed = maxPlayerY_fixed_local;
                                    }
                                }
                            }
                        }
                    }
                }

                // Call game mode physics when enabled (disabled in cam mode for complete passthrough)
                if (physicsEnabled && !camModeActive)
                {
                    try
                    {
                        // Initialize state for all modes (sync from gravity system)
                        currplayer_mini = (byte)(miniMode ? 1 : 0);
                        currplayer_gravity = (byte)(gravityFlipped ? 0xFF : 0);
                        currplayer_table_idx = (currplayer_gravity != 0 ? 1 : 0) | (currplayer_mini != 0 ? 4 : 0);
                        if (gravityFlipped) AppendSimDebug($"[PHYSICS] FLIPPED! Mode={currentGameMode}, gravity={currplayer_gravity:X2}, mini={currplayer_mini}, table_idx={currplayer_table_idx}, gravityFlipped={gravityFlipped}, gravityReversed={gravityReversed}");
                        else AppendSimDebug($"[PHYSICS] Mode={currentGameMode}, gravity={currplayer_gravity:X2}, mini={currplayer_mini}, table_idx={currplayer_table_idx}");
                        
                        // All modes handle their own input internally now
                        switch (currentGameMode)
                        {
                            case 0: // Cube
                                ProcessCubePhysics_Fresh();
                                break;
                            case 1: // Ship
                                ShipPhysics_Fresh();
                                break;
                            case 2: // Ball
                                BallPhysics_Fresh();
                                break;
                            case 3: // UFO
                                UfoPhysics_Fresh();
                                break;
                            case 4: // Robot (uses cube physics with hold-to-jump)
                                RobotPhysics_Fresh();
                                break;
                            case 5: // Spider
                                SpiderPhysics_Fresh();
                                break;
                            case 6: // Wave
                                WavePhysics_Fresh();
                                break;
                            case 7: // Swing
                                BallPhysics_Fresh(); // Swing uses ball physics
                                break;
                            case 8: // Ninja (uses cube physics with triple jump)
                                NinjaPhysics_Fresh();
                                break;
                            case 9: // Pogo (uses ball physics with bounce mechanic)
                                BallPhysics_Fresh();
                                break;
                            case 10: // Snake (uses wave movement with gravity dash)
                                SnakePhysics_Fresh();
                                break;
                            case 11: // Football (uses cube gravity with charge mechanic)
                                ProcessCubePhysics_Fresh();
                                break;
                        }
                        
                        // Sync state back (gravity might have flipped)
                        gravityFlipped = (currplayer_gravity != 0);
                        AppendSimDebug($"[GRAV_POST_PHYSICS] currplayer_gravity={currplayer_gravity:X2} gravityFlipped={gravityFlipped} gravityReversed={gravityReversed} mini={miniMode}");
                        
                        // Clear dblocked every frame (matches state_game.h line 636)
                        dblocked = false;
                        
                        // Clear dashing state when X is released (user requested Y velocity = 0)
                        if (dashing != 0)
                        {
                            if (!(IsXDownAsync() || keyXHeld))
                            {
                                // Zero Y velocity when stopping dash
                                velocityY = 0;
                                playerVelY_fixed = 0;
                                dashing = 0;
                            }
                        }
                        
                        // Clear orbed flag when X is released (matches state_game.h lines 142-143)
                        if (orbed)
                        {
                            if (!(IsXDownAsync() || keyXHeld))
                                orbed = false;
                        }
                        
                        // Check for death collision (skip in cam mode)
                        if (!camModeActive && CheckDeathCollision(out int deathX_px, out int deathY_px))
                        {
                            AppendSimDebug($"[DEATH] Death tile collision at ({deathX_px},{deathY_px})");
                            deathTriggered = true;
                            deathTileX = deathX_px;
                            deathTileY = deathY_px;
                            paused = true;
                            _ = StopMusicAsync();
                            
                            try
                            {
                                Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    try { PauseOverlay.Visibility = System.Windows.Visibility.Collapsed; } catch { }
                                    if (this.Owner is MainWindow mw)
                                    {
                                        try { mw.PauseSimulatorPlayback(); } catch { }
                                        try { mw.AddDeathMarker(deathX_px, deathY_px); } catch { }
                                    }
                                }));
                            }
                            catch { }
                        }
                    }
                    catch (Exception ex)
                    {
                        AppendSimDebug($"Game mode physics error (mode {currentGameMode}): {ex.Message}");
                    }
                }

                if (false && physicsEnabled)
                {
                    try
                        {
                            // Decrement grounded-stabilization counter each numeric step
                            if (groundStabilizeCounter > 0) groundStabilizeCounter--;
                            // Per-frame ceiling collision stabilization: if the cube's head
                            // is overlapping a blocking ceiling while numeric gravity is
                            // inverted (either canonical or numeric-only), treat the ceiling
                            // as a solid support for this frame. Snap vertical velocity to
                            // zero and mark `onGround` so gravity is suppressed this frame.
                            // This is checked every numeric frame (deterministic) rather
                            // than relying on a time window to avoid jitter.
                            // try { Cube_HandleCeilingStabilization(); } catch { } // REMOVED - fresh port
                            // Read input flags atomically so numeric sim doesn't race with UI poll.
                            bool keyXHeld_local;
                            int keyXHeldStartedOnGround_local_int = 0;
                            lock (simLock)
                            {
                                keyXHeld_local = keyXHeld;
                                try { keyXHeldStartedOnGround_local_int = Interlocked.CompareExchange(ref keyXHeldStartedOnGroundInt, 0, 0); } catch { keyXHeldStartedOnGround_local_int = 0; }
                            }

                            // Detailed trace for diagnosis: record world Y, maxY, vel, and flags (use local copies)
                            // Read OS-level held state for logic where needed; avoid expensive logging here

                            // Track if a jump was applied this numeric step so we skip immediate gravity application
                            bool jumpAppliedThisStep_local = false;

                            bool effectiveOnGround_local = onGround || groundStabilizeCounter > 0 || invertedCeilingHoldCounter > 0;

                            // Atomically consume any pending UI-edge presses recorded by the UI poll
                            int pendingPresses_num = Interlocked.Exchange(ref keyXPressedCount, 0);
                            // Also consume whether the pending press was recorded as starting on-ground
                            int pendingPressStartedOnGround = Interlocked.Exchange(ref keyXPressStartedOnGroundInt, 0);
                            // For Cube mode we want to defer applying the jump until after gravity+integration
                            // so the first frame applies gravity/integration before jump velocity is set.
                            int pendingPresses_forLater = 0;
                            if (pendingPresses_num > 0)
                            {
                                // try { Cube_HandlePendingPresses_NoLocals(pendingPresses_num, pendingPressStartedOnGround, ref pendingPresses_forLater, ref jumpAppliedThisStep_local); } catch { } // REMOVED - fresh port
                            }

                            // Update orb buffer: set/clear according to strict rules
                            try
                            {
                                // Clear buffer and hold-consumption when landing
                                if (effectiveOnGround_local)
                                {
                                    orbBufferActive = false;
                                    orbHoldConsumed = false;
                                    orbHoldConsumedKeyStillDown = false;
                                    orbActivationConsumedThisPress = false;
                                }
                                else if (jumpAppliedThisStep_local)
                                {
                                    // A jump used this frame should not also prime the orb buffer
                                    orbBufferActive = false;
                                    orbHoldConsumed = false;
                                    orbHoldConsumedKeyStillDown = false;
                                    orbActivationConsumedThisPress = false;
                                }
                                else
                                {
                                    // Do not consider deferred pending presses as a fresh press for buffering
                                    bool freshPressEdge = (pendingPresses_num > 0) && (pendingPressStartedOnGround == 0) && !(pendingPresses_forLater > 0);

                                    // Set buffer when a fresh press occurs while airborne
                                    // Do not allow fresh presses to prime if a previous
                                    // hold-activation consumed the held X and suppression
                                    // is active; a release is required to reset.
                                    if (freshPressEdge && !effectiveOnGround_local && !orbHoldSuppressing)
                                    {
                                        orbBufferActive = true;
                                        orbHoldConsumed = false;
                                    }

                                    // Also allow holding X in-air to prime the buffer (if not already active).
                                    // This covers the case where X was pressed earlier and the player became
                                    // airborne before we could set the buffer on the press frame.
                                    if (!orbBufferActive && (keyXHeld_local || IsXDownAsync()) && !effectiveOnGround_local && keyXHeldStartedOnGround_local_int == 0 && !orbHoldConsumedKeyStillDown && !orbHoldSuppressing)
                                    {
                                        orbBufferActive = true;
                                        // Do not mark orbHoldConsumed here; consumption happens when a hold-based
                                        // activation actually fires.
                                    }

                                    // NOTE: clearing the orb buffer when X is released is handled
                                    // after orb activation checks below to avoid races where the
                                    // UI thread primes the buffer and the numeric thread clears
                                    // it before activations can consume it.

                                    // A fresh press edge should reset hold-consumption so a new hold can be used
                                    if (freshPressEdge) { orbHoldConsumed = false; orbHoldConsumedKeyStillDown = false; }
                                }
                            }
                            catch { }

                            // === REFACTORED PHYSICS INTEGRATION ===
                            // Use the new refactored physics system for all game modes
                            if (useRefactoredPhysics)
                            {
                                try
                                {
                                    // ProcessAllGameModesRefactored(); // REMOVED - fresh port
                                }
                                catch (Exception ex)
                                {
                                    AppendSimDebug($"Refactored physics error: {ex.Message}");
                                    // Fall back to old physics on error
                                    useRefactoredPhysics = false;
                                }
                            }
                            // === END REFACTORED PHYSICS ===
                            else if (!jumpAppliedThisStep_local && !effectiveOnGround_local && (playerVelY_fixed != 0 || playerY_fixed < maxPlayerY_fixed_local - LAND_EPS_FIXED))
                            // Apply gravity only if we did not just apply a jump, are not grounded,
                            // and if moving vertically or not at the bottom clamp. Prevents gravity
                            // from kicking in while standing on a surface which caused jitter.
                            {
                                if (currentGameMode == 1)
                                {
                                    // Ship-mode numeric sim gravity selection (use local key state)
                                    try
                                    {
                                        bool normalGravity = !gravityReversed;
                                        bool movingUpRelative = normalGravity ? (playerVelY_fixed < 0) : (playerVelY_fixed > 0);
                                        bool xheld_local = IsXDownAsync() || keyXHeld_local;

                                                int gravitySign_local = gravityReversed ? -1 : 1;
                                                // canonical gravity flag already applied via gravityReversed

                                                bool movingUpRelative_local = gravitySign_local > 0 ? (playerVelY_fixed < 0) : (playerVelY_fixed > 0);
                                                int tmpMag_local = movingUpRelative_local ? (xheld_local ? SHIP_GRAVITY_HOLD_FALL : SHIP_GRAVITY_BASE)
                                                                                      : (xheld_local ? SHIP_GRAVITY_AFTER_HOLD : SHIP_GRAVITY);

                                                int tmpgravity_local = tmpMag_local * gravitySign_local;
                                                if (xheld_local) tmpgravity_local = -tmpgravity_local; // X = thrust opposite to gravity

                                                try { playerVelY_fixed += (int)Math.Round(tmpgravity_local * simTimeScale * simTimeScale); } catch { playerVelY_fixed += tmpgravity_local; }

                                                try
                                                {
                                                    if (gravitySign_local < 0)
                                                    {
                                                        if (playerVelY_fixed < -SHIP_MAX_FALLSPEED) playerVelY_fixed = -SHIP_MAX_FALLSPEED;
                                                        if (playerVelY_fixed > SHIP_MAX_FALLSPEED_HOLD) playerVelY_fixed = SHIP_MAX_FALLSPEED_HOLD;
                                                    }
                                                    else
                                                    {
                                                        if (playerVelY_fixed < -SHIP_MAX_FALLSPEED_HOLD) playerVelY_fixed = -SHIP_MAX_FALLSPEED_HOLD;
                                                        if (playerVelY_fixed > SHIP_MAX_FALLSPEED) playerVelY_fixed = SHIP_MAX_FALLSPEED;
                                                    }
                                                }
                                                catch { }
                                            }
                                            catch { try { playerVelY_fixed += (int)Math.Round(effectiveGravity_fixed * simTimeScale * simTimeScale); } catch { playerVelY_fixed += effectiveGravity_fixed; } }
                                }
                                        else if (currentGameMode == 2)
                                        {
                                            try
                                            {
                                                int gravitySign_local = gravityReversed ? -1 : 1;
                                                // canonical gravity flag already applied via gravityReversed
                                                bool xheld_local = IsXDownAsync() || keyXHeld_local;

                                                int tmpMag_local = BALL_GRAVITY;
                                                // Use ballGoingDown as the authoritative gravity direction for ball mode
                                                int gravityDir_local = ballGoingDown ? 1 : -1;
                                                if (gravityReversed) gravityDir_local = -gravityDir_local;
                                                // canonical gravity flag already applied via gravityReversed

                                                int tmpgravity_local = tmpMag_local * gravityDir_local;

                                                try { playerVelY_fixed += (int)Math.Round(tmpgravity_local * simTimeScale * simTimeScale); } catch { playerVelY_fixed += tmpgravity_local; }

                                                try
                                                {
                                                    int effectiveBallMaxFall_local = gravityDir_local >= 0 ? BALL_MAX_FALLSPEED : -BALL_MAX_FALLSPEED;
                                                    if (effectiveBallMaxFall_local >= 0)
                                                    {
                                                        if (playerVelY_fixed > effectiveBallMaxFall_local) playerVelY_fixed = effectiveBallMaxFall_local;
                                                    }
                                                    else
                                                    {
                                                        if (playerVelY_fixed < effectiveBallMaxFall_local) playerVelY_fixed = effectiveBallMaxFall_local;
                                                    }
                                                }
                                                catch { }
                                            }
                                            catch { try { playerVelY_fixed += (int)Math.Round(effectiveGravity_fixed * simTimeScale * simTimeScale); } catch { playerVelY_fixed += effectiveGravity_fixed; } }
                                        }
                                        else
                                        {
                                            try { playerVelY_fixed += (int)Math.Round(effectiveGravity_fixed * simTimeScale * simTimeScale); } catch { playerVelY_fixed += effectiveGravity_fixed; }
                                            try
                                            {
                                                if (effectiveMaxFall_fixed >= 0)
                                                {
                                                    if (playerVelY_fixed > effectiveMaxFall_fixed) playerVelY_fixed = effectiveMaxFall_fixed;
                                                }
                                                else
                                                {
                                                    if (playerVelY_fixed < effectiveMaxFall_fixed) playerVelY_fixed = effectiveMaxFall_fixed;
                                                }
                                            }
                                            catch { }
                                        }
                            }

                            // integrate
                            playerY_fixed += playerVelY_fixed;

                            // Gravity portal numeric activation: detect sprite overlap in numeric path
                            try
                            {
                                // Use the same hitbox as other portal checks
                                const int PORTAL_HIT_W_NUM = 14; const int PORTAL_HIT_H_NUM = 14;
                                int playerCenter_px_num = (playerX_fixed >> 8) + (playerVisualWidth / 2);
                                int playerLeft_px_num = playerCenter_px_num - (PORTAL_HIT_W_NUM / 2);
                                int playerRight_px_num = playerLeft_px_num + (PORTAL_HIT_W_NUM - 1);
                                int playerTop_px_num = (playerY_fixed >> 8);
                                // Apply mini mode offset: bottom-left for normal, top-left for inverted
                                if (miniMode)
                                {
                                    if (!gravityFlipped)
                                        playerTop_px_num += 9;
                                }
                                int playerBottom_px_num = playerTop_px_num + (PORTAL_HIT_H_NUM - 1);

                                for (int idx = 0; idx < sprites.Length; idx++)
                                {
                                    int sid = sprites[idx];
                                    if (sid < 0) continue;
                                    // Gravity portals: normal (0x08,0x10,0x11,0xFC) and reverse (0x09,0x12,0x13,0xFB)
                                    if (!(sid == 0x08 || sid == 0x10 || sid == 0x11 || sid == 0xFC || sid == 0x09 || sid == 0x12 || sid == 0x13 || sid == 0xFB)) continue;

                                    // Only activate once per crossing
                                    if (processedGravityPortals.Contains(idx)) continue;

                                    if (SpriteIntersectsPlayer(idx, sid, playerLeft_px_num, playerRight_px_num, playerTop_px_num, playerBottom_px_num))
                                    {
                                        bool isReverse = (sid == 0x09 || sid == 0x12 || sid == 0x13 || sid == 0xFB);
                                        // Reverse portal: only activate if gravity currently normal
                                        if (isReverse && !gravityReversed)
                                        {
                                            AppendSimDebug($"[GRAV_7895] ACTIVATED mini={miniMode}/{currplayer_mini} grav before={gravityReversed}");
                                            try { playerVelY_fixed = playerVelY_fixed / 2; } catch { }
                                            try { gravityReversed = true; gravityFlipped = true; currplayer_gravity = 0xFF; effectiveInvertedByW = gravityReversed; } catch { }
                                            try { UpdateEffectiveGravity(); } catch { }
                                            try { Dispatcher?.BeginInvoke(new Action(() => UpdatePlayerIconFlip())); } catch { }
                                            try { UpdatePlayerImageForMode(); } catch { }
                                            processedGravityPortals.Add(idx);
                                            break; // only one portal per frame
                                        }
                                        // Normal portal: only activate if gravity currently reversed
                                        else if (!isReverse && gravityReversed)
                                        {
                                            try { playerVelY_fixed = playerVelY_fixed / 2; } catch { }
                                            try { gravityReversed = false; gravityFlipped = false; currplayer_gravity = 0x00; effectiveInvertedByW = gravityReversed; } catch { }
                                            try { UpdateEffectiveGravity(); } catch { }
                                            try { Dispatcher?.BeginInvoke(new Action(() => UpdatePlayerIconFlip())); } catch { }
                                            try { UpdatePlayerImageForMode(); } catch { }
                                            processedGravityPortals.Add(idx);
                                            break; // only one portal per frame
                                        }
                                    }
                                }
                            }
                            catch { }

                            // try { OrbPad_HandleNumericActivations(pendingPresses_num, pendingPressStartedOnGround, keyXHeld_local, ref jumpAppliedThisStep_local, pendingPresses_forLater); } catch { } // REMOVED - fresh port
                            try
                            {
                                // Cube_HandleDeferredJump(pendingPresses_forLater, ref jumpAppliedThisStep_local); // REMOVED - fresh port
                            }
                            catch { }

                            try
                            {
                                // Cube_HandleHeldJump(jumpBuffered_local, pendingPresses_num, keyXHeld_local, ref jumpAppliedThisStep_local); // REMOVED - fresh port
                            }
                            catch { }

                            // Ceiling collision handled by cube-mode specific handler
                            try
                            {
                                // Cube_HandleCeilingCollision(jumpBuffered_local, pendingKeyX_local, keyXHeld_local, pendingPresses_num, pendingPressStartedOnGround, pendingPresses_forLater, ref jumpAppliedThisStep_local); // REMOVED - fresh port
                            }
                            catch { }

                        // Landing detection replaced by cube-mode handler
                        try
                        {
                            // Cube_HandleLandingAndReversed(jumpBuffered_local, pendingKeyX_local, keyXHeld_local, pendingPresses_num, pendingPressStartedOnGround, pendingPresses_forLater, ref jumpAppliedThisStep_local); // REMOVED - fresh port
                        }
                        catch { }
                    }
                    catch { }
                }

                // Clamp cameraY
                if (cameraY_fixed < 0) cameraY_fixed = 0;
                if (cameraY_fixed > maxCameraY_fixed_local) cameraY_fixed = maxCameraY_fixed_local;

                // Final safety clamp: ensure player remains above ground after camera moves
                if (playerY_fixed > maxPlayerY_fixed_local) { playerY_fixed = maxPlayerY_fixed_local; playerVelY_fixed = 0; }

                // === RIGHT SIDE DEATH/SNAP CHECK ===
                // Right-edge collision with death check:
                // - Middle pixel collision → DEATH (when NO DEATH is disabled)
                // - Bottom half collision (but not middle) → snap up
                // - NO DEATH enabled → always snap (no death)
                // Disabled entirely for ball mode (mode 2) and cam mode
                try
                {
                    bool skipSnap = currentGameMode == 2 || camModeActive; // Skip all snap logic for ball mode and cam mode
                    
                    if (!deathTriggered && !skipSnap)
                    {
                        int playerRightEdge_px = (playerX_fixed >> 8) + playerVisualWidth - 1;
                        int playerTop_px = (playerY_fixed >> 8);
                        int playerCenterY_px = playerTop_px + (playerVisualHeight / 2);
                        int playerBottomHalfStart_px = playerCenterY_px;
                        int playerBottom_px = playerTop_px + playerVisualHeight - 1;

                        int groundRowsToReserve_local = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;

                        // Check middle pixel on right edge - only death, not solid collision
                        bool middlePixelBlocked = CheckPixelCollision(playerRightEdge_px, playerCenterY_px, groundRowsToReserve_local);
                        
                        // Debug: Log tile info at collision point
                        int checkTileX = playerRightEdge_px / TILE;
                        int checkTileY = playerCenterY_px / TILE;
                        int checkTileIndexY = checkTileY + groundRowsToReserve_local;
                        int checkTid = (checkTileIndexY >= 0 && checkTileIndexY < mapHeight && checkTileX >= 0 && checkTileX < mapWidth) 
                            ? tiles[checkTileIndexY * mapWidth + checkTileX] : -1;
                        var checkCol = checkTid >= 0 ? MetatileCollisionTable.GetCollision((byte)checkTid) : MetatileCollision.COL_NONE;
                        AppendSimDebug($"[RIGHT-CHECK] Pos=({playerX_fixed >> 8},{playerY_fixed >> 8}) CheckPx=({playerRightEdge_px},{playerCenterY_px}) Tile=({checkTileX},{checkTileY}) TID={checkTid} Col={checkCol} Result={middlePixelBlocked}");
                        
                        if (middlePixelBlocked && !MainWindow.Option_NoDeath)
                        {
                            // DEATH - middle pixel hit (matches bg_coll_R in auto-scroll mode)
                            // In famidash: x_movement_coll() calls bg_coll_R() which checks ONLY middle pixel
                            // If blocked in auto-scroll: cube_data[currplayer] | 0x01 (death flag)
                            AppendSimDebug($"[DEATH] Right middle pixel collision at ({playerRightEdge_px},{playerCenterY_px})");
                            deathTriggered = true;
                            paused = true;
                            _ = StopMusicAsync();
                            
                            try
                            {
                                Dispatcher?.BeginInvoke(new Action(() =>
                                {
                                    try { PauseOverlay.Visibility = System.Windows.Visibility.Collapsed; } catch { }
                                    if (this.Owner is MainWindow mw)
                                    {
                                        try { mw.PauseSimulatorPlayback(); } catch { }
                                        try { mw.AddDeathMarker(playerRightEdge_px, playerCenterY_px); } catch { }
                                    }
                                }));
                            }
                            catch { }
                        }
                        // NOTE: Bottom-half collision and snapping happens separately in cube_eject() phase
                        // (the general downward collision check that runs after x_movement_coll)
                        // NOT part of right-side collision logic in famidash source
                    }
                }
                catch (Exception ex)
                {
                    AppendSimDebug($"Right collision death check error: {ex.Message}");
                }
                // === END RIGHT SIDE DEATH/SNAP CHECK ===
                
                // Additional screen-space enforcement: ensure at least 3 rows of ground remain visible
                // Skip this enforcement when gravity is inverted (allow jumping into ground rows from ceiling)
                try
                {
                    if (currplayer_gravity == 0) // Only enforce for normal gravity
                    {
                        int playerScreenY_now_local = (playerY_fixed >> 8) - (cameraY_fixed >> 8) + gridRenderShiftYPx;
                        int allowedBottom_px_local = (NES_H * TILE) - (3 * TILE);
                        int playerScreenBottom_local = playerScreenY_now_local + playerVisualHeight;
                        if (playerScreenBottom_local > allowedBottom_px_local)
                        {
                            int desiredPlayerScreenY_local = allowedBottom_px_local - playerVisualHeight;
                            int desiredPlayerWorldY_local = desiredPlayerScreenY_local + (cameraY_fixed >> 8) - gridRenderShiftYPx;
                            if (desiredPlayerWorldY_local < 0) desiredPlayerWorldY_local = 0;
                            int desiredPlayerY_fixed_local = desiredPlayerWorldY_local << 8;
                            if (desiredPlayerY_fixed_local > maxPlayerY_fixed_local) desiredPlayerY_fixed_local = maxPlayerY_fixed_local;
                            playerY_fixed = desiredPlayerY_fixed_local;
                            // When numeric sim enforces a screen-space clamp, treat the player as grounded so gravity stops.
                            playerVelY_fixed = 0;
                            onGround = true;
                        }
                    }
                }
                catch { }

                // Detect speed portals between prevCameraCenter_fixed and current center
                int center_fixed = cameraX_fixed + ((NES_W * TILE / 2) << 8);
                int? newSpeed_fixed = null;
                for (int idx = 0; idx < sprites.Length; idx++)
                {
                    int sid = sprites[idx];
                    if (sid < 0) continue;

                    // Gravity portals (UI/sprite-intersection path): handle independently
                    // of mode portals so invisible gravity tiles still work.
                    try
                    {
                        if (sid == 0x08 || sid == 0x10 || sid == 0x11 || sid == 0xFB || sid == 0x09 || sid == 0x12 || sid == 0x13 || sid == 0xFC)
                        {
                            const int HITBOX_W_UI = 15; const int HITBOX_H_UI = 15;
                            int playerCenter_px_ui = (playerX_fixed >> 8) + (playerVisualWidth / 2);
                            int playerLeft_px_ui = playerCenter_px_ui - (HITBOX_W_UI / 2);
                            int playerRight_px_ui = playerLeft_px_ui + (HITBOX_W_UI - 1);
                            int playerTop_px_ui = (playerY_fixed >> 8);
                            int playerBottom_px_ui = playerTop_px_ui + (HITBOX_H_UI - 1);

                            if (processedGravityPortals.Contains(idx)) { /* wait until portal moves past interaction line */ }
                            else if (SpriteIntersectsPlayer(idx, sid, playerLeft_px_ui, playerRight_px_ui, playerTop_px_ui, playerBottom_px_ui))
                            {
                                bool isReverse = (sid == 0x09 || sid == 0x12 || sid == 0x13 || sid == 0xFC);
                                if (isReverse && !gravityReversed)
                                {
                                    gravityReversed = true;
                                    gravityFlipped = true;
                                    currplayer_gravity = 0xFF;
                                    try { playerVelY_fixed = playerVelY_fixed / 2; } catch { }
                                    try { UpdateEffectiveGravity(); } catch { }
                                    try { Dispatcher?.BeginInvoke(new Action(() => { try { UpdatePlayerImageForMode(); } catch { } })); } catch { }
                                    processedGravityPortals.Add(idx);
                                    break;
                                }
                                else if (!isReverse && gravityReversed)
                                {
                                    gravityReversed = false;
                                    gravityFlipped = false;
                                    currplayer_gravity = 0x00;
                                    try { playerVelY_fixed = playerVelY_fixed / 2; } catch { }
                                    try { UpdateEffectiveGravity(); } catch { }
                                    try { Dispatcher?.BeginInvoke(new Action(() => { try { UpdatePlayerImageForMode(); } catch { } })); } catch { }
                                    processedGravityPortals.Add(idx);
                                    break;
                                }
                            }
                        }
                    }
                    catch { }

                    // Portal handling: All 9 gamemode portals
                    try
                    {
                        if (sid == 0x00 || sid == 0x01 || sid == 0x02 || sid == 0x03 || sid == 0x04 || sid == 0x17 || sid == 0x24 || sid == 0x4B || sid == 0x58 || sid == 0x6A || sid == 0x6B || sid == 0x6C)
                        {
                            // require 2D overlap with player's hitbox for portal activation
                            const int HITBOX_W_LOCAL = 15; const int HITBOX_H_LOCAL = 15;
                            int playerCenter_px_local = (playerX_fixed >> 8) + (playerVisualWidth / 2);
                            int playerLeft_px_local = playerCenter_px_local - (HITBOX_W_LOCAL / 2);
                            int playerRight_px_local = playerLeft_px_local + (HITBOX_W_LOCAL - 1);
                            int playerTop_px_local = (playerY_fixed >> 8);
                            int playerBottom_px_local = playerTop_px_local + (HITBOX_H_LOCAL - 1);

                            if (SpriteIntersectsPlayer(idx, sid, playerLeft_px_local, playerRight_px_local, playerTop_px_local, playerBottom_px_local))
                            {
                                int oldMode = currentGameMode;
                                int newMode = sid switch {
                                    0x00 => 0, // Cube
                                    0x01 => 1, // Ship
                                    0x02 => 2, // Ball
                                    0x03 => 3, // UFO
                                    0x04 => 4, // Robot
                                    0x17 => 5, // Spider
                                    0x24 => 6, // Wave
                                    0x4B => 7, // Swing
                                    0x58 => 8, // Ninja
                                    0x6A => 9, // Pogo
                                    0x6B => 10, // Snake
                                    0x6C => 11, // Football
                                    _ => currentGameMode
                                };
                                if (newMode != oldMode)
                                {
                                    currentGameMode = newMode;
                                    try { UpdateGameModeDisplay(); } catch { }
                                    try { UpdateEffectiveGravity(); } catch { }
                                    try { playerVelY_fixed = playerVelY_fixed / 2; } catch { }
                                }
                                try { Dispatcher?.BeginInvoke(new Action(() => { try { UpdatePlayerImageForMode(); } catch { } })); } catch { }
                                break;
                            }
                        }
                    }
                    catch { }
                    if (!speedPortalMap.ContainsKey(sid)) continue;
                    int anchorTileX = (spriteAnchors != null && spriteAnchors.TryGetValue(idx, out var a)) ? a.anchorTileX : idx % mapWidth;
                    int anchorX_center_fixed = ((anchorTileX * TILE) + (TILE / 2)) << 8;

                    // Use interaction-line crossing logic so speed portals activate when anchors cross
                    // the player's interaction line (consistent with color triggers behavior).
                    if (crossedInteraction)
                    {
                        if (anchorX_center_fixed > prevPlayerCenter_fixed && anchorX_center_fixed <= INTERACTION_LINE_FIXED)
                        {
                            if (!processedSpeedPortals.Contains(idx))
                            {
                                newSpeed_fixed = speedPortalMap[sid];
                                processedSpeedPortals.Add(idx);
                                break;
                            }
                        }
                        else
                        {
                            if (processedSpeedPortals.Contains(idx) && anchorX_center_fixed > INTERACTION_LINE_FIXED) processedSpeedPortals.Remove(idx);
                        }
                    }
                    else
                    {
                        if (anchorX_center_fixed <= center_fixed)
                        {
                            if (!processedSpeedPortals.Contains(idx))
                            {
                                newSpeed_fixed = speedPortalMap[sid];
                                processedSpeedPortals.Add(idx);
                                break;
                            }
                        }
                        else
                        {
                            if (processedSpeedPortals.Contains(idx)) processedSpeedPortals.Remove(idx);
                        }
                    }
                }
                if (newSpeed_fixed.HasValue)
                {
                    currentSpeed_fixed = newSpeed_fixed.Value;
                    playerVelX_fixed = newSpeed_fixed.Value; // Update Wave horizontal velocity for current speed
                    
                    // Update speed variable for debug display
                    if (newSpeed_fixed.Value == CUBE_SPEED_X05) speed = 0;
                    else if (newSpeed_fixed.Value == CUBE_SPEED_X1) speed = 1;
                    else if (newSpeed_fixed.Value == CUBE_SPEED_X2) speed = 2;
                    else if (newSpeed_fixed.Value == CUBE_SPEED_X3) speed = 3;
                    else if (newSpeed_fixed.Value == CUBE_SPEED_X4) speed = 4;
                    else if (newSpeed_fixed.Value == CUBE_SPEED_SLOW) speed = 5;
                    
                    // Update combo box selection to match
                    try
                    {
                        Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            if (SpeedComboBox != null && speed >= 0 && speed < SpeedComboBox.Items.Count)
                            {
                                SpeedComboBox.SelectedIndex = speed;
                            }
                        }));
                    }
                    catch { }
                }

                // Detect color triggers; instead of sampling/pixel work here, record pending triggers
                int bestBg_fixed = int.MaxValue; int? bgIdxLocal = null; int? bgSidLocal = null;
                int bestTile_fixed = int.MaxValue; int? tileIdxLocal = null; int? tileSidLocal = null;
                int bestGround_fixed = int.MaxValue; int? groundIdxLocal = null; int? groundSidLocal = null;

                for (int idx = 0; idx < sprites.Length; idx++)
                {
                    int sid = sprites[idx];
                    if (sid < 0) continue;
                    if (!IsColorTriggerSprite(sid)) continue;
                    int anchorTileX = (spriteAnchors != null && spriteAnchors.TryGetValue(idx, out var a2)) ? a2.anchorTileX : idx % mapWidth;
                    int anchorX_center_fixed = ((anchorTileX * TILE) + (TILE / 2)) << 8;
                    if (crossedInteraction)
                    {
                        if (anchorX_center_fixed > prevPlayerCenter_fixed && anchorX_center_fixed <= INTERACTION_LINE_FIXED)
                        {
                            if (IsBackgroundTrigger(sid)) { if (anchorX_center_fixed < bestBg_fixed) { bestBg_fixed = anchorX_center_fixed; bgIdxLocal = idx; bgSidLocal = sid; } }
                            else if (IsTileTrigger(sid)) { if (anchorX_center_fixed < bestTile_fixed) { bestTile_fixed = anchorX_center_fixed; tileIdxLocal = idx; tileSidLocal = sid; } }
                            else if (IsGroundTrigger(sid)) { if (anchorX_center_fixed < bestGround_fixed) { bestGround_fixed = anchorX_center_fixed; groundIdxLocal = idx; groundSidLocal = sid; } }
                        }
                        else
                        {
                            if (processedColorTriggers.Contains(idx) && anchorX_center_fixed > INTERACTION_LINE_FIXED) processedColorTriggers.Remove(idx);
                            if (processedGravityPortals.Contains(idx) && anchorX_center_fixed > INTERACTION_LINE_FIXED) processedGravityPortals.Remove(idx);
                        }
                    }
                    else
                    {
                        if (anchorX_center_fixed <= center_fixed)
                        {
                            if (!processedColorTriggers.Contains(idx))
                            {
                                if (IsBackgroundTrigger(sid)) { if (anchorX_center_fixed < bestBg_fixed) { bestBg_fixed = anchorX_center_fixed; bgIdxLocal = idx; bgSidLocal = sid; } }
                                else if (IsTileTrigger(sid)) { if (anchorX_center_fixed < bestTile_fixed) { bestTile_fixed = anchorX_center_fixed; tileIdxLocal = idx; tileSidLocal = sid; } }
                                else if (IsGroundTrigger(sid)) { if (anchorX_center_fixed < bestGround_fixed) { bestGround_fixed = anchorX_center_fixed; groundIdxLocal = idx; groundSidLocal = sid; } }
                            }
                        }
                        else { if (processedColorTriggers.Contains(idx)) processedColorTriggers.Remove(idx); if (processedGravityPortals.Contains(idx)) processedGravityPortals.Remove(idx); if (processedOrbs.Contains(idx)) processedOrbs.Remove(idx); }
                    }
                }

                // If any triggers detected, set pending fields so UI thread will sample and apply tints
                if (bgIdxLocal.HasValue) { pendingBgIdx = bgIdxLocal ?? -1; pendingBgSid = bgSidLocal ?? -1; pendingTintChange = true; }
                if (tileIdxLocal.HasValue) { pendingTileIdx = tileIdxLocal ?? -1; pendingTileSid = tileSidLocal ?? -1; pendingTintChange = true; }
                if (groundIdxLocal.HasValue) { pendingGroundIdx = groundIdxLocal ?? -1; pendingGroundSid = groundSidLocal ?? -1; pendingTintChange = true; }
            }

            // If we have pending tints, schedule application on UI thread for heavier image work
            if (pendingTintChange)
            {
                try { Dispatcher?.BeginInvoke((Action)(() => { try { ApplyPendingTints(); } catch { } })); } catch { }
            }
        }

        // Apply pending trigger tints on the UI thread and regenerate toned images as necessary
        private void ApplyPendingTints()
        {
            try
            {
                bool wasStartup = pendingTintChangeIsStartup;
                int bgIdxLocal = pendingBgIdx; int bgSidLocal = pendingBgSid;
                int tileIdxLocal = pendingTileIdx; int tileSidLocal = pendingTileSid;
                int groundIdxLocal = pendingGroundIdx; int groundSidLocal = pendingGroundSid;

                pendingBgIdx = -1; pendingBgSid = -1; pendingTileIdx = -1; pendingTileSid = -1; pendingGroundIdx = -1; pendingGroundSid = -1; pendingTintChange = false; pendingTintChangeIsStartup = false;
                // Remember that we applied a startup-origin tint so future regenerations avoid object tinting.
                if (wasStartup) startupTintApplied = true;

                var prevBackgroundTint = backgroundTint; var prevTileTint = tileTint; var prevGroundTint = groundTint;
                var prevBackgroundForceSolidBlack = backgroundForceSolidBlack;
                // Determine the candidate tile/object tint for this pending change. If a tile/object
                // trigger is pending (tileIdxLocal present) prefer its new tint so outline recoloring
                // uses the up-to-date color even before we assign `tileTint` below.
                Color candidateTileTint = tileTint;
                try { if (tileIdxLocal >= 0 && tileSidLocal >= 0) candidateTileTint = ColorFromTrigger(tileSidLocal); } catch { }

                if (bgIdxLocal >= 0 && bgSidLocal >= 0)
                {
                    var c = ColorFromTrigger(bgSidLocal);
                    backgroundTint = c; processedColorTriggers.Add(bgIdxLocal);
                    // mark special-case solid-black background trigger (0x8F)
                    backgroundForceSolidBlack = (bgSidLocal == 0x8F);
                    if (enableSimulatorDebugLogging && !triggerLogged.Contains(bgIdxLocal)) { WriteTempLog($"Simulator: Applied background trigger at idx={bgIdxLocal} sid=0x{bgSidLocal:X} color={c}"); triggerLogged.Add(bgIdxLocal); }
                }
                if (tileIdxLocal >= 0 && tileSidLocal >= 0)
                {
                    var c = ColorFromTrigger(tileSidLocal);
                    tileTint = c; processedColorTriggers.Add(tileIdxLocal);
                    if (enableSimulatorDebugLogging && !triggerLogged.Contains(tileIdxLocal)) { WriteTempLog($"Simulator: Applied tile trigger at idx={tileIdxLocal} sid=0x{tileSidLocal:X} color={c}"); triggerLogged.Add(tileIdxLocal); }
                }
                if (groundIdxLocal >= 0 && groundSidLocal >= 0)
                {
                    var c = ColorFromTrigger(groundSidLocal);
                    // If this pending ground trigger is the special black-ground id (0xCF),
                    // force the ground tint to pure black so the ground images become
                    // black-masked (preserving the thin white seam). This keeps the
                    // behavior consistent regardless of whether tints are applied
                    // immediately or via the pending-tint path.
                    if (groundSidLocal == 0xCF)
                    {
                        // Special-case 0xCF in the pending-path as well: force pure black
                        // so the ground images are generated as two-tone black and the
                        // seams remain available for object tinting.
                        groundTint = Color.FromArgb(255, 0, 0, 0);
                    }
                    else
                    {
                        groundTint = c;
                    }
                    processedColorTriggers.Add(groundIdxLocal);
                    if (enableSimulatorDebugLogging && !triggerLogged.Contains(groundIdxLocal)) { WriteTempLog($"Simulator: Applied ground trigger at idx={groundIdxLocal} sid=0x{groundSidLocal:X} color={c}"); triggerLogged.Add(groundIdxLocal); }
                }

                bool bgChanged = !AreColorsEqual(prevBackgroundTint, backgroundTint);
                bool tileChanged = !AreColorsEqual(prevTileTint, tileTint);
                bool grdChanged = !AreColorsEqual(prevGroundTint, groundTint);
                // If this pending tint application originated from startup, force a regeneration
                // so two-tone/outline images exist before the first render even when the
                // starting tint equals the current tint values.
                bool forceRegenerateOnStartup = wasStartup;

                // Compute outline tint to use for this regeneration. Preserve object outline
                // recoloring on ground-only changes so object tints are not cleared by a ground trigger.
                var outlineTintParam = Color.FromArgb(0, 0, 0, 0);
                if (!wasStartup)
                {
                    if (tileIdxLocal >= 0 || !AreColorsEqual(prevTileTint, candidateTileTint))
                    {
                        // Explicit tile/object trigger or tile tint changed: use the candidate/new tint
                        outlineTintParam = candidateTileTint;
                    }
                    else if (grdChanged)
                    {
                        // Ground-only change: preserve existing tile tint as outline so object
                        // recolors survive ground updates.
                        outlineTintParam = tileTint;
                    }
                }

                // Check if we can use cached toned images instead of regenerating
                bool canUseCache = !forceRegenerateOnStartup && 
                    cachedTileTonedImages != null && 
                    cachedParallaxTonedImages != null && 
                    cachedGroundTonedImages != null &&
                    AreColorsEqual(cachedBackgroundTint, backgroundTint) &&
                    AreColorsEqual(cachedTileTint, tileTint) &&
                    AreColorsEqual(cachedGroundTint, groundTint) &&
                    cachedBackgroundForceSolidBlack == backgroundForceSolidBlack;

                if (canUseCache)
                {
                    // Use cached images - no regeneration needed
                    tileTonedImages = cachedTileTonedImages;
                    parallaxTonedImages = cachedParallaxTonedImages;
                    groundTonedImages = cachedGroundTonedImages;
                }
                else if (tileChanged || bgChanged || grdChanged || forceRegenerateOnStartup)
                {
                    // Recompute tile-toned images.
                    // Compute separate palette-driven primary/secondary colors for background and ground
                    // so background triggers do not override ground visuals.
                    Color? bgPrimary = null; Color? bgSecondary = null;
                    Color? grdPrimary = null; Color? grdSecondary = null;
                    try
                    {
                        var palette = PaletteProvider.GetPalette();

                        // Background palette mapping (only from explicit background trigger)
                        if (bgSidLocal >= 0)
                        {
                            // Lighter shade must be the exact trigger color; darker shade comes from the
                            // trigger one row up (sid - 0x10). If the trigger is in the first row
                            // (0x80..0x8C) then the darker shade must be pure black.
                            try
                            {
                                var prim = ColorFromTrigger(bgSidLocal);
                                bgPrimary = prim;
                                if (bgSidLocal >= 0x80 && bgSidLocal <= 0x8C)
                                {
                                    bgSecondary = Color.FromArgb(255, 0, 0, 0);
                                }
                                else
                                {
                                    int darkerSid = bgSidLocal - 0x10;
                                    var darker = ColorFromTrigger(darkerSid);
                                    bgSecondary = darker;
                                }
                            }
                            catch { bgPrimary = null; bgSecondary = null; }
                        }

                        // Ground palette mapping (only from explicit ground trigger)
                        if (groundSidLocal >= 0)
                        {
                            int? pidx = GetPaletteIndexForBackgroundTrigger(groundSidLocal);
                            if (pidx.HasValue && pidx.Value >= 0 && pidx.Value < palette.Length)
                            {
                                grdPrimary = palette[pidx.Value];
                                int col = pidx.Value % 14; if (col > 12) col = 12;
                                int row = pidx.Value / 14;
                                // Special-case: triggers in 0xC0..0xCF must have darker secondary = pure black
                                if (groundSidLocal >= 0xC0 && groundSidLocal <= 0xCF)
                                {
                                    grdSecondary = Color.FromArgb(255, 0, 0, 0);
                                }
                                else
                                {
                                    // Ground two-tone: derive a darker secondary from primary (avoid row-shifting)
                                    try
                                    {
                                        RgbToHsl(grdPrimary.Value.R, grdPrimary.Value.G, grdPrimary.Value.B, out double gh, out double gs, out double gl);
                                        double darkerL = Math.Max(0.0, gl - 0.12);
                                        RgbFromHsl(gh, gs, darkerL, out byte sgr, out byte sgg, out byte sgb);
                                        grdSecondary = Color.FromArgb(255, sgr, sgg, sgb);
                                    }
                                    catch { grdSecondary = Color.FromArgb(255, 0, 0, 0); }
                                }
                            }
                        }
                    }
                    catch { bgPrimary = null; bgSecondary = null; grdPrimary = null; grdSecondary = null; }

                    try
                    {
                        // Regenerate tile toned images using background two-tone mapping when available,
                        // and use tileTint to recolor white/outline pixels only (object triggers affect outlines).
                        if (tileImages != null)
                        {
                            // Use two-tone mapping for all tiles by default (so slopes and other tiles
                            // get the same background/non-white recoloring behavior). White/near-white
                            // outlines are handled by `outlineTint` (tileTint) so object color triggers
                            // still recolor outlines as intended.
                            // Only pass the outline tint when this regeneration is a result of an
                            // explicit object/tile trigger (tileIdxLocal >= 0). Background or
                            // ground-triggered regenerations should not recolor white seams.
                            // NOTE: `outlineTintParam` is computed earlier to preserve object
                            // outline recolors on ground-only updates; do not overwrite it here.
                            if (bgPrimary.HasValue)
                                tileTonedImages = CreateTwoToneTileImages(tileImages, bgPrimary.Value, bgSecondary ?? Color.FromArgb(255, 0, 0, 0), outlineTintParam);
                            else
                            {
                                if (backgroundForceSolidBlack)
                                {
                                    // For 0x8F, make non-white, non-green pixels black for tiles
                                    try { tileTonedImages = CreateBlackMaskedExceptColorArray(tileImages, playerPlaceholderGreen, Color.FromArgb(0,0,0,0), false); }
                                    catch { tileTonedImages = CreateTwoToneTileImages(tileImages, backgroundTint, Color.FromArgb(255, 0, 0, 0), outlineTintParam); }
                                }
                                else
                                {
                                    tileTonedImages = CreateTwoToneTileImages(tileImages, backgroundTint, Color.FromArgb(255, 0, 0, 0), outlineTintParam);
                                }
                            }

                            // Per-tile overrides: ensure specific tiles use background or ground tinting
                            try
                            {
                                if (tileTonedImages != null && tileImages != null)
                                {
                                    // Tiles that should get the background tint like other tiles: 0x90-0xA3, 0xD7, 0xD8
                                    // Process 0x90-0xA3 range
                                    for (int i = 0x90; i <= 0xA3; i++)
                                    {
                                        if (i >= 0 && i < tileImages.Length)
                                        {
                                            ImageSource? rep = null;
                                            // For slope tiles, ensure white/outline parts are recolored by object triggers (tileTint)
                                            // while non-white areas get background two-tone mapping. Prefer palette-driven two-tone
                                            // mapping when available; otherwise fall back to using backgroundTint as primary.
                                            if (bgPrimary.HasValue)
                                            {
                                                var arr = CreateTwoToneTileImages(new ImageSource?[] { tileImages[i]! }, bgPrimary.Value, bgSecondary ?? Color.FromArgb(255, 0, 0, 0), outlineTintParam);
                                                if (arr != null && arr.Length > 0) rep = arr[0];
                                            }
                                            else
                                            {
                                                // Use backgroundTint for non-white areas and outlineTintParam for outlines.
                                                // If solid-black background is forced, prefer the black-masked variant
                                                // so non-white/non-green pixels become black and are not overwritten
                                                // by two-tone fallbacks.
                                                if (backgroundForceSolidBlack)
                                                {
                                                    try { rep = CreateBlackMaskedExceptColor(tileImages[i], playerPlaceholderGreen, outlineTintParam, false); }
                                                    catch { rep = tileImages[i]; }
                                                }
                                                else
                                                {
                                                    var arr = CreateTwoToneTileImages(new ImageSource?[] { tileImages[i]! }, backgroundTint, Color.FromArgb(255, 0, 0, 0), outlineTintParam);
                                                    if (arr != null && arr.Length > 0) rep = arr[0];
                                                }
                                            }
                                            if (rep != null) tileTonedImages[i] = rep;
                                        }
                                    }

                                    // Process 0xD7 and 0xD8 separately
                                    foreach (int i in new[] { 0xD7, 0xD8 })
                                    {
                                        if (i >= 0 && i < tileImages.Length)
                                        {
                                            ImageSource? rep = null;
                                            if (bgPrimary.HasValue)
                                            {
                                                var arr = CreateTwoToneTileImages(new ImageSource?[] { tileImages[i]! }, bgPrimary.Value, bgSecondary ?? Color.FromArgb(255, 0, 0, 0), outlineTintParam);
                                                if (arr != null && arr.Length > 0) rep = arr[0];
                                            }
                                            else
                                            {
                                                if (backgroundForceSolidBlack)
                                                {
                                                    try { rep = CreateBlackMaskedExceptColor(tileImages[i], playerPlaceholderGreen, outlineTintParam, false); }
                                                    catch { rep = tileImages[i]; }
                                                }
                                                else
                                                {
                                                    var arr = CreateTwoToneTileImages(new ImageSource?[] { tileImages[i]! }, backgroundTint, Color.FromArgb(255, 0, 0, 0), outlineTintParam);
                                                    if (arr != null && arr.Length > 0) rep = arr[0];
                                                }
                                            }
                                            if (rep != null) tileTonedImages[i] = rep;
                                        }
                                    }

                                    // Tiles that should get ground tint (exactly like ground): 0x01/02, 0x05/06, 0x88/89
                                    int[] groundTiles = new[] { 0x01, 0x02, 0x05, 0x06, 0x88, 0x89 };
                                    foreach (var i in groundTiles)
                                    {
                                        if (i >= 0 && i < tileImages.Length)
                                        {
                                            // Ground tiles should only respond to ground color triggers and should not
                                            // have their near-white outline pixels recolored here — those outlines
                                            // must remain untouched so object triggers can recolor them later.
                                            // Use ground palette mapping (grdPrimary/grdSecondary) when present; otherwise
                                            // fall back to a two-tone mapping using `groundTint`. Pass a transparent
                                            // outline tint so white/seam pixels are preserved now.
                                            // Determine outline behavior: if an outline tint (object/tile) is active
                                            // for this regeneration, allow seam pixels to be recolored to that color.
                                            // Otherwise preserve seams by passing a transparent outline tint.
                                            var outlineForThisTile = (outlineTintParam.A > 0) ? outlineTintParam : Color.FromArgb(0, 0, 0, 0);
                                            if (grdPrimary.HasValue)
                                            {
                                                var arr = CreateTwoToneTileImages(new ImageSource?[] { tileImages[i]! }, grdSecondary ?? Color.FromArgb(255, 0, 0, 0), grdPrimary.Value, outlineForThisTile);
                                                if (arr != null && arr.Length > 0 && arr[0] != null) tileTonedImages[i] = arr[0];
                                            }
                                            else
                                            {
                                                if (backgroundForceSolidBlack || (groundTint.A == 255 && groundTint.R == 0 && groundTint.G == 0 && groundTint.B == 0))
                                                {
                                                    try { tileTonedImages[i] = CreateBlackMaskedExceptColor(tileImages[i], playerPlaceholderGreen, outlineForThisTile, outlineForThisTile.A > 0); }
                                                    catch { tileTonedImages[i] = tileImages[i]; }
                                                }
                                                else
                                                {
                                                    // Use two-tone mapping based on groundTint and a palette-derived darker color.
                                                    var darker = PaletteHelper.RowUpColor(Color.FromArgb(groundTint.A, groundTint.R, groundTint.G, groundTint.B));
                                                    var arr = CreateTwoToneTileImages(new ImageSource?[] { tileImages[i]! }, groundTint, darker, outlineForThisTile);
                                                    if (arr != null && arr.Length > 0 && arr[0] != null) tileTonedImages[i] = arr[0];
                                                }
                                            }
                                        }
                                    }

                                    // Remap visuals: 0x8F should look like 0x2F; 0xFC should look like 0x00
                                    if (0x2F >= 0 && 0x2F < tileTonedImages.Length && 0x8F >= 0 && 0x8F < tileTonedImages.Length)
                                    {
                                        tileTonedImages[0x8F] = tileTonedImages[0x2F];
                                    }
                                    if (0x00 >= 0 && 0x00 < tileTonedImages.Length && 0xFC >= 0 && 0xFC < tileTonedImages.Length)
                                    {
                                        tileTonedImages[0xFC] = tileTonedImages[0x00];
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                    catch { tileTonedImages = CreateHslShiftedImages(tileImages, tileTint, tileTint); }

                    try {
                        // Only regenerate parallax when background actually changed or when a palette mapping
                        // is explicitly provided via bgPrimary, or when forced-black flag changed.
                        // Also regenerate when this was a startup-origin application so initial
                        // render sees fresh toned images.
                        if (!AreColorsEqual(prevBackgroundTint, backgroundTint) || prevBackgroundForceSolidBlack != backgroundForceSolidBlack || bgPrimary.HasValue || forceRegenerateOnStartup)
                        {
                            if (parallaxImages != null)
                            {
                                // Prefer palette-driven two-tone for the background/parallax images.
                                if (bgPrimary.HasValue)
                                {
                                    // Ensure we have a sensible secondary color: prefer palette-derived, otherwise
                                    // synthesize a darker secondary from the primary to avoid falling back to pure black.
                                    Color synthesizedBgSecondary;
                                    if (bgSecondary.HasValue) synthesizedBgSecondary = bgSecondary.Value;
                                    else { RgbToHsl(bgPrimary.Value.R, bgPrimary.Value.G, bgPrimary.Value.B, out double _bh, out double _bs, out double _bl); double _darker = Math.Max(0.0, _bl - 0.12); RgbFromHsl(_bh, _bs, _darker, out byte _dbr, out byte _dbg, out byte _dbb); synthesizedBgSecondary = Color.FromArgb(255, _dbr, _dbg, _dbb); }
                                    // Use primary as the lighter mapping and secondary as darker so the
                                    // background two-tone matches how ground visuals are derived.
                                    parallaxTonedImages = CreateTwoToneTileImages(parallaxImages, bgPrimary.Value, synthesizedBgSecondary, outlineTintParam);
                                }
                                else if (backgroundForceSolidBlack)
                                {
                                    // forced solid black: don't generate from images
                                    parallaxTonedImages = null;
                                }
                                else if (backgroundTint.A == 255 && backgroundTint.R == 0 && backgroundTint.G == 0 && backgroundTint.B == 0)
                                {
                                    parallaxTonedImages = CreateTwoToneTileImages(parallaxImages, Color.FromArgb(255,0,0,0), Color.FromArgb(255,0,0,0), outlineTintParam);
                                }
                                else
                                {
                                    parallaxTonedImages = CreateHueShiftedImages(parallaxImages, backgroundTint, outlineTintParam);
                                }
                            }
                            else parallaxTonedImages = parallaxImages;
                        }
                    } catch { parallaxTonedImages = parallaxImages; }
                    try
                    {
                        // Only regenerate ground visuals when the ground tint actually changed
                        // or when an explicit ground palette mapping is available.
                        // Also regenerate on startup-origin so ground visuals are ready
                        // for the first render.
                        if (!AreColorsEqual(prevGroundTint, groundTint) || grdPrimary.HasValue || forceRegenerateOnStartup)
                        {
                            if (grdPrimary.HasValue)
                            {
                                // For ground visuals, flip primary/secondary so ground uses swapped two-tone
                                Color synthesizedGrdSecondary;
                                if (grdSecondary.HasValue)
                                    synthesizedGrdSecondary = grdSecondary.Value;
                                else
                                {
                                    RgbToHsl(grdPrimary.Value.R, grdPrimary.Value.G, grdPrimary.Value.B, out double _gh, out double _gs, out double _gl);
                                    double _gdarker = Math.Max(0.0, _gl - 0.12);
                                    RgbFromHsl(_gh, _gs, _gdarker, out byte _sr, out byte _sg, out byte _sb);
                                    synthesizedGrdSecondary = Color.FromArgb(255, _sr, _sg, _sb);
                                }
                                groundTonedImages = CreateTwoToneTileImages(groundImages, synthesizedGrdSecondary, grdPrimary.Value, outlineTintParam);
                            }
                            else
                            {
                                // Preserve previous special-case behavior for pure-black ground tint
                                if (groundTint.A == 255 && groundTint.R == 0 && groundTint.G == 0 && groundTint.B == 0)
                                {
                                    groundTonedImages = CreateTwoToneTileImages(groundImages, Color.FromArgb(255, 0, 0, 0), Color.FromArgb(255, 0, 0, 0), outlineTintParam);
                                }
                                else
                                {
                                    // If ground tint is fully-opaque but no palette mapping is available,
                                    // generate a two-tone pair from the ground tint by making a darker
                                    // secondary color instead of performing a full replacement which
                                    // would make the ground a solid color.
                                    if (groundTint.A == 255)
                                    {
                                        // compute a darker variant for secondary
                                        RgbToHsl(groundTint.R, groundTint.G, groundTint.B, out double gh, out double gs, out double gl);
                                        double secL = Math.Max(0.0, gl - 0.12);
                                        RgbFromHsl(gh, gs, secL, out byte sr, out byte sg, out byte sb);
                                        var secondaryFromGround = Color.FromArgb(255, sr, sg, sb);
                                        // flip primary/secondary for ground visuals
                                        groundTonedImages = CreateTwoToneTileImages(groundImages, secondaryFromGround, groundTint, outlineTintParam);
                                    }
                                    else
                                    {
                                        groundTonedImages = CreateHueShiftedImages(groundImages, groundTint, outlineTintParam);
                                    }
                                }
                            }
                        }
                    }
                    catch { groundTonedImages = groundImages; }

                    // Cache the generated images for future use
                    cachedTileTonedImages = tileTonedImages;
                    cachedParallaxTonedImages = parallaxTonedImages;
                    cachedGroundTonedImages = groundTonedImages;
                    cachedBackgroundTint = backgroundTint;
                    cachedTileTint = tileTint;
                    cachedGroundTint = groundTint;
                    cachedBackgroundForceSolidBlack = backgroundForceSolidBlack;

                        // If an outline tint is active for this regeneration, force-apply an outline-only
                        // recolor to the ground images so thin/anti-aliased white seams are recolored
                        // the same way other tiles are when object triggers fire.
                        try
                        {
                            if (outlineTintParam.A > 0 && groundTonedImages != null)
                            {
                                var recol = CreateOutlineTintedTileImages(groundTonedImages, outlineTintParam);
                                if (recol != null) groundTonedImages = recol;
                            }
                        }
                        catch { }

                    // Also create/update a tinted full-parallax bitmap if a full parallax bitmap was provided
                    // Only update the full parallax bitmap when the background actually changed or when
                    // there is an explicit palette mapping for background, or when forced-black toggled.
                    try
                    {
                        if (parallaxBitmap != null && (bgChanged || prevBackgroundForceSolidBlack != backgroundForceSolidBlack || bgPrimary.HasValue))
                        {
                            ImageSource?[]? arr = null;
                            // Prefer palette-driven two-tone for full parallax bitmap when available
                            if (bgPrimary.HasValue)
                            {
                                arr = CreateTwoToneTileImages(new ImageSource?[] { parallaxBitmap! }, bgPrimary.Value, bgSecondary ?? Color.FromArgb(255,0,0,0), outlineTintParam);
                            }
                            else if (backgroundTint.A == 255 && backgroundTint.R == 0 && backgroundTint.G == 0 && backgroundTint.B == 0)
                                arr = CreateBlackMaskedImages(new ImageSource?[] { parallaxBitmap! });
                            else
                                arr = CreateHueShiftedImages(new ImageSource?[] { parallaxBitmap! }, backgroundTint);

                            if (arr != null && arr.Length > 0 && arr[0] != null) parallaxBitmapToned = arr[0];
                            else parallaxBitmapToned = parallaxBitmap;
                        }
                        else
                        {
                            // If we are not updating the parallax bitmap due to an unrelated ground change,
                            // leave the previous toned bitmap as-is so background visuals remain stable.
                        }
                    }
                    catch { parallaxBitmapToned = parallaxBitmap; }
                    // If the background tint changed, regenerate saw-frame tinted images
                    // Also regenerate saw-frame images when this is startup-origin so they
                    // appear correctly on the very first render.
                    if (!AreColorsEqual(prevBackgroundTint, backgroundTint) || forceRegenerateOnStartup)
                    {
                        try
                        {
                            // When background is pure opaque black, make saw frames fully black
                            // (no white lines or outlines). Otherwise preserve existing HSL shifted
                            // behavior and pass `tileTint` as outline tint so object recolors work.
                            if (backgroundTint.A == 255 && backgroundTint.R == 0 && backgroundTint.G == 0 && backgroundTint.B == 0)
                            {
                                this.sawFrame1TilesTinted = CreateSolidBlackImages(this.sawFrame1TilesOrig);
                                this.sawFrame2TilesTinted = CreateSolidBlackImages(this.sawFrame2TilesOrig);
                                this.smallSawFrame1TilesTinted = CreateSolidBlackImages(this.smallSawFrame1TilesOrig);
                                this.smallSawFrame2TilesTinted = CreateSolidBlackImages(this.smallSawFrame2TilesOrig);
                                this.largeSawFrame1TilesTinted = CreateSolidBlackImages(this.largeSawFrame1TilesOrig);
                                this.largeSawFrame2TilesTinted = CreateSolidBlackImages(this.largeSawFrame2TilesOrig);
                            }
                            else
                            {
                                // Use outlineTintParam so startup-origin changes don't recolor outlines/objects.
                                this.sawFrame1TilesTinted = CreateHslShiftedImages(this.sawFrame1TilesOrig, backgroundTint, outlineTintParam);
                                this.sawFrame2TilesTinted = CreateHslShiftedImages(this.sawFrame2TilesOrig, backgroundTint, outlineTintParam);
                                this.smallSawFrame1TilesTinted = CreateHslShiftedImages(this.smallSawFrame1TilesOrig, backgroundTint, outlineTintParam);
                                this.smallSawFrame2TilesTinted = CreateHslShiftedImages(this.smallSawFrame2TilesOrig, backgroundTint, outlineTintParam);
                                this.largeSawFrame1TilesTinted = CreateHslShiftedImages(this.largeSawFrame1TilesOrig, backgroundTint, outlineTintParam);
                                this.largeSawFrame2TilesTinted = CreateHslShiftedImages(this.largeSawFrame2TilesOrig, backgroundTint, outlineTintParam);
                            }
                        }
                        catch { }
                    }

                    tileLayerCache = null;
                    try { groundTintedTileCache.Clear(); } catch { }
                    try { spriteBackgroundCompositeCache.Clear(); } catch { }
                }
            }
            catch { }
        }

        // Determine if a sprite id is a color trigger we should consider
        private bool IsColorTriggerSprite(int spriteIdx)
        {
            if (spriteIdx < 0) return false;
            // Accept ranges like the editor, but exclude certain low-nibble values per user request
            // Background triggers: 0x80-0xAC
            // Tile triggers: 0xB0-0xBF
            // Ground triggers: 0xC0-0xEC
            bool inRanges = (spriteIdx >= 0x80 && spriteIdx <= 0x8C) || spriteIdx == 0x8F ||
                            (spriteIdx >= 0x90 && spriteIdx <= 0x9C) || spriteIdx == 0x9F ||
                            (spriteIdx >= 0xA0 && spriteIdx <= 0xAC) || (spriteIdx >= 0xAE && spriteIdx <= 0xAF) ||
                            (spriteIdx >= 0xB0 && spriteIdx <= 0xBF) ||
                            (spriteIdx >= 0xC0 && spriteIdx <= 0xCC) || spriteIdx == 0xCF ||
                            (spriteIdx >= 0xD0 && spriteIdx <= 0xDC) ||
                            (spriteIdx >= 0xE0 && spriteIdx <= 0xEC);
            if (!inRanges) return false;

            // Disregard specific combinations where low nibble is D/E/F for certain high nibbles
            int low = spriteIdx & 0x0F;
            int high = spriteIdx & 0xF0;
            if (low >= 0xD)
            {
                // Most high-nibble groups with low >= 0xD are excluded, but allow explicit
                // single-value exceptions such as 0x8F and 0xCF which should act as triggers.
                if (high == 0x80 || high == 0x90 || high == 0xA0 || high == 0xC0 || high == 0xD0 || high == 0xE0)
                {
                    if (spriteIdx == 0x8F || spriteIdx == 0xCF)
                    {
                        // explicit exceptions: keep as trigger
                    }
                    else
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private bool IsBackgroundTrigger(int spriteIdx)
        {
            // Include 0x8F as valid background trigger per request
            return (spriteIdx >= 0x80 && spriteIdx <= 0xAC || spriteIdx == 0x8F) && IsColorTriggerSprite(spriteIdx);
        }

        private bool IsTileTrigger(int spriteIdx)
        {
            return spriteIdx >= 0xB0 && spriteIdx <= 0xBF && IsColorTriggerSprite(spriteIdx);
        }

        private bool IsGroundTrigger(int spriteIdx)
        {
            // Include 0xCF as a valid ground trigger per request
            return ((spriteIdx >= 0xC0 && spriteIdx <= 0xEC) || spriteIdx == 0xCF) && IsColorTriggerSprite(spriteIdx);
        }

        private Color ColorFromTrigger(int spriteIdx)
        {
            // Special-case: certain trigger sprites explicitly mean "black" regardless of sampling.
            // Keep sprite-only black triggers here (0x8F and 0xBF). Do NOT special-case 0xCF
            // so ground triggers can be sampled or palette-mapped like other ground tints.
            if (spriteIdx == 0x8F || spriteIdx == 0xBF)
            {
                return Color.FromArgb(255, 0, 0, 0);
            }
            // If this sprite corresponds to a palette cell in the Color Picker, prefer that exact color.
            try
            {
                var palette = PaletteProvider.GetPalette(); // expects 14*4 (or similar)
                try
                {
                    // For background triggers, use the shifted palette index (one row darker) so
                    // the background tint and tile two-tone mapping agree.
                    int? pidx = GetPaletteIndexForBackgroundTrigger(spriteIdx);
                    if (pidx.HasValue && pidx.Value >= 0 && pidx.Value < palette.Length)
                    {
                        var p = palette[pidx.Value];
                        return Color.FromArgb(255, p.R, p.G, p.B);
                    }
                }
                catch { }
            }
            catch { }

            // Prefer sampling the actual sprite/preview image color so the tint matches icon color
            try
            {
                var c = SampleRepresentativeColorFromSprite(spriteIdx);
                if (c.HasValue) return c.Value;
            }
            catch { }

            // Fallback: map low nibble to HSL hue
            int colorIndex = spriteIdx & 0x0F; // 0-15
            double hue = (colorIndex / 16.0) * 360.0;
            return HslToColor(hue, 0.7, 0.45);
        }

        private Color? SampleRepresentativeColorFromSprite(int spriteIdx)
        {
            ImageSource? src = null;
            if (previewSpriteMap != null && previewSpriteMap.TryGetValue(spriteIdx, out var p) && p != null)
                src = p;
            else if (spriteImages != null && spriteIdx >= 0 && spriteIdx < spriteImages.Length)
                src = spriteImages[spriteIdx];

            if (src is BitmapSource bs)
            {
                try
                {
                    var conv = new FormatConvertedBitmap(bs, PixelFormats.Pbgra32, null, 0);
                    int w = Math.Max(1, conv.PixelWidth);
                    int h = Math.Max(1, conv.PixelHeight);
                    int stride = w * 4;
                    var pixels = new byte[h * stride];
                    conv.CopyPixels(pixels, stride, 0);

                    // Search for a non-transparent, non-black pixel. Prefer most frequent color isn't necessary; choose first non-black.
                    for (int i = 0; i < pixels.Length; i += 4)
                    {
                        byte b = pixels[i + 0];
                        byte g = pixels[i + 1];
                        byte r = pixels[i + 2];
                        byte a = pixels[i + 3];
                        if (a > 32)
                        {
                            // ignore fully black pixels (icons often have black outlines)
                            if (!(r == 0 && g == 0 && b == 0))
                            {
                                return Color.FromArgb(255, r, g, b);
                            }
                        }
                    }
                }
                catch { }
            }
            return null;
        }

        // Determine whether a bitmap's visible pixels are concentrated near the top of the image.
        // This heuristic allows detecting upside-down halves (content at top) vs normal bottom-aligned halves.
        private bool IsImageTopHeavy(BitmapSource bs)
        {
            try
            {
                if (bs == null) return false;
                int key = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(bs);
                if (imageTopHeavyCache.TryGetValue(key, out var val)) return val;

                var conv = new FormatConvertedBitmap(bs, PixelFormats.Pbgra32, null, 0);
                int w = Math.Max(1, conv.PixelWidth);
                int h = Math.Max(1, conv.PixelHeight);
                int stride = w * 4;
                var pixels = new byte[h * stride];
                conv.CopyPixels(pixels, stride, 0);

                long sumY = 0;
                int count = 0;
                // Sample at most a limited number of pixels for performance.
                int maxSamples = 4000;
                for (int y = 0; y < h && count < maxSamples; y++)
                {
                    int rowStart = y * stride;
                    for (int x = 0; x < w && count < maxSamples; x++)
                    {
                        int i = rowStart + x * 4;
                        byte a = pixels[i + 3];
                        if (a > 32)
                        {
                            // count this pixel
                            sumY += y;
                            count++;
                        }
                    }
                }

                bool topHeavy = false;
                if (count > 0)
                {
                    double avgY = (double)sumY / (double)count;
                    // If the average non-transparent pixel Y is in the top ~45% of the image,
                    // consider the image top-heavy (likely upside-down bottom half).
                    topHeavy = avgY < (h * 0.45);
                }

                imageTopHeavyCache[key] = topHeavy;
                return topHeavy;
            }
            catch { return false; }
        }

        private Color HslToColor(double h, double s, double l)
        {
            // h in [0,360), s,l in [0,1]
            double c = (1 - Math.Abs(2 * l - 1)) * s;
            double hh = h / 60.0;
            double x = c * (1 - Math.Abs((hh % 2) - 1));
            double r1 = 0, g1 = 0, b1 = 0;
            if (0 <= hh && hh < 1) { r1 = c; g1 = x; b1 = 0; }
            else if (1 <= hh && hh < 2) { r1 = x; g1 = c; b1 = 0; }
            else if (2 <= hh && hh < 3) { r1 = 0; g1 = c; b1 = x; }
            else if (3 <= hh && hh < 4) { r1 = 0; g1 = x; b1 = c; }
            else if (4 <= hh && hh < 5) { r1 = x; g1 = 0; b1 = c; }
            else { r1 = c; g1 = 0; b1 = x; }
            double m = l - c / 2.0;
            byte r = (byte)Math.Round((r1 + m) * 255.0);
            byte g = (byte)Math.Round((g1 + m) * 255.0);
            byte b = (byte)Math.Round((b1 + m) * 255.0);
            return Color.FromArgb(255, r, g, b);
        }

        // Map a starting color code (0x00-0x2C or 0x0F) to the corresponding trigger sprite id.
        // Background mapping: 0x00-0x0C -> 0x80-0x8C, 0x10-0x1C -> 0x90-0x9C, 0x20-0x2C -> 0xA0-0xAC
        // Ground mapping: same ranges but mapped to 0xC0/0xD0/0xE0 groups respectively
        // Special-case: 0x0F -> 0x8F for background, 0xCF for ground.
        private int MapStartingCodeToTrigger(int code, bool isGround)
        {
            try
            {
                if (code == 0x0F)
                {
                    return isGround ? 0xCF : 0x8F;
                }
                int low = code & 0x0F;
                int high = code & 0xF0;
                if (high == 0x00)
                {
                    return (isGround ? 0xC0 : 0x80) + low;
                }
                if (high == 0x10)
                {
                    return (isGround ? 0xD0 : 0x90) + low;
                }
                if (high == 0x20)
                {
                    return (isGround ? 0xE0 : 0xA0) + low;
                }
            }
            catch { }
            return isGround ? 0xC0 : 0x80; // fallback
        }

        // Given a background trigger sprite id, return the corresponding palette index (or null).
        // Mirrors the mapping used by ColorFromTrigger but returns the numeric palette index instead of a color.
        // Given a background trigger sprite id, return the corresponding palette index (or null).
        // This mapping returns the exact palette index that corresponds to the trigger sprite
        // (no automatic up-row shifting). Callers may choose to compute a darker/lighter
        // variant explicitly if desired.
        private int? GetPaletteIndexForBackgroundTrigger(int spriteIdx)
        {
            try
            {
                int idx = -1;
                if (spriteIdx >= 0x80 && spriteIdx <= 0x8C) idx = (spriteIdx - 0x80) + (0 * 14);
                else if (spriteIdx >= 0x90 && spriteIdx <= 0x9C) idx = (spriteIdx - 0x90) + (1 * 14);
                else if (spriteIdx >= 0xA0 && spriteIdx <= 0xAC) idx = (spriteIdx - 0xA0) + (2 * 14);
                else if (spriteIdx >= 0xC0 && spriteIdx <= 0xCC) idx = (spriteIdx - 0xC0) + (0 * 14);
                else if (spriteIdx >= 0xD0 && spriteIdx <= 0xDC) idx = (spriteIdx - 0xD0) + (1 * 14);
                else if (spriteIdx >= 0xE0 && spriteIdx <= 0xEC) idx = (spriteIdx - 0xE0) + (2 * 14);

                if (idx >= 0) return idx;
            }
            catch { }
            return null;
        }

        // (Deprecated: previous implementation shifted selection to the row above.)

        // Create two-tone tile images: non-white pixels are classified into lighter/darker groups and
        // mapped to bgPrimary/bgSecondary respectively; white/near-white outlines are mapped to outlineTint.
        private ImageSource?[]? CreateTwoToneTileImages(ImageSource?[]? originals, Color bgPrimary, Color bgSecondary, Color outlineTint)
        {
            if (originals == null) return null;
            var outList = new System.Collections.Generic.List<ImageSource>(originals.Length);
            foreach (var src in originals)
            {
                if (src == null)
                {
                    var pf = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Pbgra32, null);
                    var pxf = new byte[4];
                    try { pf.WritePixels(new Int32Rect(0, 0, 1, 1), pxf, 4, 0); } catch { }
                    try { pf.Freeze(); } catch { }
                    outList.Add(pf);
                    continue;
                }
                if (src is BitmapSource bs)
                {
                    try
                    {
                        var conv = new FormatConvertedBitmap(bs, PixelFormats.Pbgra32, null, 0);
                        int w = conv.PixelWidth; int h = conv.PixelHeight; int stride = w * 4;
                        var pixels = new byte[h * stride];
                        conv.CopyPixels(pixels, stride, 0);

                        // Compute min/max luminance of non-white, non-transparent pixels to determine threshold
                        double minL = 1.0, maxL = 0.0; int count = 0;
                        for (int i = 0; i < pixels.Length; i += 4)
                        {
                            byte b = pixels[i + 0]; byte g = pixels[i + 1]; byte r = pixels[i + 2]; byte a = pixels[i + 3];
                            if (a == 0) continue;
                            double lum = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;
                            // Skip near-white when computing min/max (use 0.82 to catch anti-aliased outlines)
                            if (lum >= 0.82) continue;
                            if (lum < minL) minL = lum;
                            if (lum > maxL) maxL = lum;
                            count++;
                        }
                        double threshold = (count > 0) ? ((minL + maxL) / 2.0) : 0.5;

                        // Determine a white-detection threshold. If the outline tint is pure opaque black
                        // (object trigger like 0xBF), be more aggressive in treating light pixels as "white"
                        // so object-black triggers recolor thin/anti-aliased whites properly.
                        double whiteThreshold = 0.82;
                        if (outlineTint.A == 255 && outlineTint.R == 0 && outlineTint.G == 0 && outlineTint.B == 0)
                        {
                            whiteThreshold = 0.70; // treat more pixels as white when applying black outline tint
                        }

                        for (int i = 0; i < pixels.Length; i += 4)
                        {
                            byte ob = pixels[i + 0]; byte og = pixels[i + 1]; byte orr = pixels[i + 2]; byte a = pixels[i + 3];
                            if (a == 0) continue;
                            double lum = (0.2126 * orr + 0.7152 * og + 0.0722 * ob) / 255.0;
                            bool isWhite = lum >= whiteThreshold;
                            bool isBlack = (orr <= 12 && og <= 12 && ob <= 12);

                            if (isBlack)
                            {
                                // Preserve true black pixels unchanged (do not tint blacks with background)
                                continue;
                            }

                            if (isWhite)
                            {
                                // Only recolor white outlines if an explicit outline tint is provided (alpha>0).
                                if (outlineTint.A > 0)
                                {
                                    pixels[i + 3] = 255; // ensure opaque when recoloring
                                    pixels[i + 2] = outlineTint.R;
                                    pixels[i + 1] = outlineTint.G;
                                    pixels[i + 0] = outlineTint.B;
                                }
                                else
                                {
                                    // leave white as-is
                                    continue;
                                }
                            }
                            else
                            {
                                // Map into primary/secondary based on luminance threshold
                                Color target = (lum >= threshold) ? bgPrimary : bgSecondary;
                                pixels[i + 3] = 255;
                                pixels[i + 2] = target.R;
                                pixels[i + 1] = target.G;
                                pixels[i + 0] = target.B;
                            }
                        }

                        var wb = new WriteableBitmap(w, h, conv.DpiX, conv.DpiY, PixelFormats.Pbgra32, null);
                        wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
                        // Do not freeze WriteableBitmap; it must remain writable for Lock/WritePixels.
                        outList.Add(wb);
                    }
                    catch
                    {
                            if (src != null) outList.Add(src);
                            else outList.Add(new WriteableBitmap(1, 1, 96, 96, PixelFormats.Pbgra32, null));
                    }
                }
                else
                {
                        if (src != null) outList.Add(src);
                        else outList.Add(new WriteableBitmap(1, 1, 96, 96, PixelFormats.Pbgra32, null));
                }
            }
            return outList.ToArray();
        }

        // Create tile images where only near-white outline pixels are replaced by outlineTint while
        // keeping other pixels unchanged.
        private ImageSource?[]? CreateOutlineTintedTileImages(ImageSource?[]? originals, Color outlineTint)
        {
            if (originals == null) return null;
            var outList = new System.Collections.Generic.List<ImageSource>(originals.Length);
            foreach (var src in originals)
            {
                if (src == null)
                {
                    var pf = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Pbgra32, null);
                    var pxf = new byte[4];
                    try { pf.WritePixels(new Int32Rect(0, 0, 1, 1), pxf, 4, 0); } catch { }
                    try { pf.Freeze(); } catch { }
                    outList.Add(pf);
                    continue;
                }
                if (src is BitmapSource bs)
                {
                    try
                    {
                        var conv = new FormatConvertedBitmap(bs, PixelFormats.Pbgra32, null, 0);
                        int w = conv.PixelWidth; int h = conv.PixelHeight; int stride = w * 4;
                        var pixels = new byte[h * stride];
                        conv.CopyPixels(pixels, stride, 0);

                        // Choose a white-detection threshold. Be more aggressive when outline tint is
                        // pure opaque black so object-black triggers recolor a wider set of light pixels.
                        double outlineWhiteThreshold = 0.82;
                        if (outlineTint.A == 255 && outlineTint.R == 0 && outlineTint.G == 0 && outlineTint.B == 0) outlineWhiteThreshold = 0.70;
                        for (int i = 0; i < pixels.Length; i += 4)
                        {
                            byte b = pixels[i + 0]; byte g = pixels[i + 1]; byte r = pixels[i + 2]; byte a = pixels[i + 3];
                            if (a == 0) continue;
                            double lum = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;
                            bool isWhite = lum >= outlineWhiteThreshold;
                            if (isWhite)
                            {
                                pixels[i + 3] = 255;
                                pixels[i + 2] = outlineTint.R;
                                pixels[i + 1] = outlineTint.G;
                                pixels[i + 0] = outlineTint.B;
                            }
                        }

                        var wb = new WriteableBitmap(w, h, conv.DpiX, conv.DpiY, PixelFormats.Pbgra32, null);
                        wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
                        // Do not freeze WriteableBitmap; it must remain writable for Lock/WritePixels.
                        outList.Add(wb);
                    }
                    catch { outList.Add(src); }
                }
                else outList.Add(src);
            }
            return outList.ToArray();
        }

        // Helper: compare colors (treat nullability not applicable here)
        private static bool AreColorsEqual(Color a, Color b)
        {
            return a.A == b.A && a.R == b.R && a.G == b.G && a.B == b.B;
        }

        // Return a player-tinted copy of the given sprite image (cached).
        private ImageSource? GetPlayerTintedSprite(ImageSource src, int spriteId)
        {
            if (!playerTintEnabled) return src;
            try
            {
                // Include the source image identity in the cache key so different frames
                // of the same sprite id don't collapse to the same cached tinted image.
                int srcHash = src != null ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(src) : 0;
                long key = (((long)spriteId) << 48) | (((long)srcHash & 0xFFFF) << 32) | ((long)playerTint.A << 24) | ((long)playerTint.R << 16) | ((long)playerTint.G << 8) | playerTint.B;
                if (tintedSpriteCache.TryGetValue(key, out var cached)) return cached;

                if (src is BitmapSource bs)
                {
                    var conv = new FormatConvertedBitmap(bs, PixelFormats.Pbgra32, null, 0);
                    int w = Math.Max(1, conv.PixelWidth);
                    int h = Math.Max(1, conv.PixelHeight);
                    int stride = w * 4;
                    var pixels = new byte[h * stride];
                    conv.CopyPixels(pixels, stride, 0);

                    // compute tint hue
                    RgbToHsl(playerTint.R, playerTint.G, playerTint.B, out double tintH, out double tintS, out double tintL);

                    for (int i = 0; i < pixels.Length; i += 4)
                    {
                        byte b = pixels[i + 0];
                        byte g = pixels[i + 1];
                        byte r = pixels[i + 2];
                        byte a = pixels[i + 3];
                        if (a == 0) continue; // preserve fully transparent pixels

                        // Replace hue with tint hue while preserving original saturation/lightness
                        RgbToHsl(r, g, b, out double ph, out double ps, out double pl);
                        double nh = tintH; double ns = ps; double nl = pl;
                        RgbFromHsl(nh, ns, nl, out byte nr, out byte ng, out byte nb);
                        pixels[i + 0] = nb;
                        pixels[i + 1] = ng;
                        pixels[i + 2] = nr;
                        // alpha unchanged
                    }

                    var wb = new WriteableBitmap(w, h, conv.DpiX, conv.DpiY, PixelFormats.Pbgra32, null);
                    wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
                    // Do not freeze WriteableBitmap; it must remain writable for Lock/WritePixels.
                    tintedSpriteCache[key] = wb;
                    return wb;
                }
                else
                {
                    tintedSpriteCache[key] = src;
                    return src;
                }
            }
            catch
            {
                return src;
            }
        }

        // Composite a sprite image over the rendered tile layer (if available) at the given
        // destination coordinates so transparent areas show the exact underlying pixels.
        // Falls back to compositing over a flat `bg` color when the tile layer is unavailable.
        private ImageSource? CompositeSpriteOverBackgroundAt(ImageSource src, Color bg, int destX, int destY)
        {
            if (src == null) return null;
            if (!(src is BitmapSource bs)) return src;
            try
            {
                int srcHash = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(bs);
                // Include animationFrame and tile-layer canvas offsets so cached composites
                // update when animated tiles/frame or fractional camera offsets change.
                int layerLeft = 0, layerTop = 0;
                try { if (tileLayerImage != null) { var v = System.Windows.Controls.Canvas.GetLeft(tileLayerImage); if (!double.IsNaN(v)) layerLeft = (int)Math.Round(v); var t = System.Windows.Controls.Canvas.GetTop(tileLayerImage); if (!double.IsNaN(t)) layerTop = (int)Math.Round(t); } } catch { }
                string key = string.Format("{0}:{1:X2}{2:X2}{3:X2}{4:X2}:{5}:{6}:{7}:{8}:{9}", srcHash, bg.A, bg.R, bg.G, bg.B, destX, destY, animationFrame, layerLeft, layerTop);
                if (spriteBackgroundCompositeCache.TryGetValue(key, out var cached)) return cached;

                // Use WPF rendering to compose the sprite over the background region. This
                // ensures correct alpha blending and avoids subtle premultiplication issues
                // that can cause semi-transparent pixels to render the wrong color.
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    // Draw background: either the cropped tile-layer region or a flat color
                    if (tileLayerCache is BitmapSource layer)
                    {
                        try
                        {
                            int layerW = layer.PixelWidth;
                            int layerH = layer.PixelHeight;
                            int srcLeft = destX - layerLeft;
                            int srcTop = destY - layerTop;
                            int ovLeft = Math.Max(0, srcLeft);
                            int ovTop = Math.Max(0, srcTop);
                            int ovRight = Math.Min(layerW, srcLeft + Math.Max(1, (int)bs.PixelWidth));
                            int ovBottom = Math.Min(layerH, srcTop + Math.Max(1, (int)bs.PixelHeight));
                            int ovW = Math.Max(0, ovRight - ovLeft);
                            int ovH = Math.Max(0, ovBottom - ovTop);
                            if (ovW > 0 && ovH > 0)
                            {
                                try
                                {
                                    var cropped = new CroppedBitmap(layer, new Int32Rect(ovLeft, ovTop, ovW, ovH));
                                    var brushImg = App.EnsureUnfrozenForRender(cropped) ?? cropped;
                                    var brush = new ImageBrush(brushImg) { Stretch = Stretch.None, TileMode = TileMode.None };
                                    // Draw the full visible tile layer translated into sprite-local coordinates
                                    // so transparent sprite pixels reveal the exact rendered pixels beneath them.
                                    // If the full visible layer isn't available, fall back to the persistent
                                    // background brush (parallax ImageBrush) or a flat color as before.
                                    bool drewFullLayer = false;
                                    try
                                    {
                                        if (tileLayerImage != null && tileLayerImage.Source is BitmapSource fullLayer)
                                        {
                                            double tx = layerLeft - destX;
                                            double ty = layerTop - destY;
                                            dc.PushTransform(new TranslateTransform(tx, ty));
                                            var drawFull = App.EnsureUnfrozenForRender(fullLayer) ?? fullLayer;
                                            dc.DrawImage(drawFull, new Rect(0, 0, fullLayer.PixelWidth, fullLayer.PixelHeight));
                                            dc.Pop();
                                            drewFullLayer = true;
                                        }
                                    }
                                    catch { drewFullLayer = false; }

                                    if (!drewFullLayer)
                                    {
                                        // If we couldn't draw the full tile layer, fall back to the persistent background
                                        // brush or a flat color. Prefer the persistent background so parallax/ground
                                        // visuals still show through transparent sprite pixels.
                                        Brush? bgBrush = null;
                                        try { if (bgRectPersistent != null && bgRectPersistent.Fill != null) bgBrush = bgRectPersistent.Fill; } catch { bgBrush = null; }
                                        if (bgBrush == null)
                                        {
                                            bgBrush = new SolidColorBrush(Color.FromArgb(bg.A, bg.R, bg.G, bg.B));
                                            dc.DrawRectangle(bgBrush ?? new SolidColorBrush(Color.FromArgb(bg.A, bg.R, bg.G, bg.B)), null, new Rect(0, 0, bs.PixelWidth, bs.PixelHeight));
                                        }
                                        else
                                        {
                                            try
                                            {
                                                if (bgBrush is ImageBrush ib)
                                                {
                                                    var srcImg = ib.ImageSource;
                                                    // compute palette-accurate darker version for consistency with tile tinting
                                                    Color darkerBg = bg;
                                                    try
                                                    {
                                                        var palette = PaletteProvider.GetPalette();
                                                        int? nearest = GetNearestPaletteIndexForColor(bg, palette);
                                                        if (nearest.HasValue)
                                                        {
                                                            int col = nearest.Value % 14;
                                                            int row = nearest.Value / 14;
                                                            int finalIdx = row * 14 + (col > 12 ? 12 : col);
                                                            if (finalIdx >= 0 && finalIdx < palette.Length) darkerBg = Color.FromArgb(bg.A, palette[finalIdx].R, palette[finalIdx].G, palette[finalIdx].B);
                                                        }
                                                        else
                                                        {
                                                            RgbToHsl(bg.R, bg.G, bg.B, out double hh, out double ss, out double ll);
                                                            ll = Math.Max(0.0, ll - 0.12);
                                                            RgbFromHsl(hh, ss, ll, out byte dr, out byte dg, out byte db);
                                                            darkerBg = Color.FromArgb(bg.A, dr, dg, db);
                                                        }
                                                    }
                                                    catch { }
                                                    ImageSource useImg = srcImg ?? new WriteableBitmap(1, 1, 96, 96, PixelFormats.Pbgra32, null);
                                                    try { if (srcImg != null) { var arr = CreateHslShiftedImages(new ImageSource?[] { srcImg! }, darkerBg); if (arr != null && arr.Length > 0 && arr[0] != null) useImg = arr[0]!; } } catch { }
                                                    var newIbImg = App.EnsureUnfrozenForRender(useImg) ?? useImg;
                                                    var newIb = new ImageBrush(newIbImg)
                                                    {
                                                        Stretch = ib.Stretch,
                                                        TileMode = ib.TileMode,
                                                        Viewport = ib.Viewport,
                                                        ViewportUnits = ib.ViewportUnits,
                                                    };
                                                    double ox = 0, oy = 0;
                                                    try { if (ib.Transform is TranslateTransform tt) { ox = tt.X; oy = tt.Y; } else if (ib.Transform is MatrixTransform mt) { ox = mt.Matrix.OffsetX; oy = mt.Matrix.OffsetY; } } catch { }
                                                    newIb.Transform = new TranslateTransform(ox - destX, oy - destY);
                                                    dc.DrawRectangle(newIb, null, new Rect(0, 0, bs.PixelWidth, bs.PixelHeight));
                                                }
                                                else
                                                {
                                                    dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(bg.A, bg.R, bg.G, bg.B)), null, new Rect(0, 0, bs.PixelWidth, bs.PixelHeight));
                                                }
                                            }
                                            catch
                                            {
                                                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(bg.A, bg.R, bg.G, bg.B)), null, new Rect(0, 0, bs.PixelWidth, bs.PixelHeight));
                                            }
                                        }
                                    }

                                    // Draw the cropped content on top at the appropriate offset (if present)
                                    int dx = ovLeft - srcLeft;
                                    int dy = ovTop - srcTop;
                                    dc.PushTransform(new TranslateTransform(-dx, -dy));
                                    dc.DrawRectangle(brush, null, new Rect(dx, dy, ovW, ovH));
                                    dc.Pop();
                                }
                                catch
                                {
                                    Brush? bgBrush2 = null;
                                    try { if (bgRectPersistent != null && bgRectPersistent.Fill != null) bgBrush2 = bgRectPersistent.Fill; } catch { bgBrush2 = null; }
                                    if (bgBrush2 == null)
                                    {
                                        bgBrush2 = new SolidColorBrush(Color.FromArgb(bg.A, bg.R, bg.G, bg.B));
                                    }
                                    else
                                    {
                                        try
                                        {
                                            if (bgBrush2 is ImageBrush ib2)
                                            {
                                                var srcImg2 = ib2.ImageSource;
                                                // compute darker version for consistency with tile tinting
                                                    Color darkerBg2 = bg;
                                                    try
                                                    {
                                                        var palette2 = PaletteProvider.GetPalette();
                                                        int? nearest2 = GetNearestPaletteIndexForColor(bg, palette2);
                                                        if (nearest2.HasValue)
                                                        {
                                                            int col2 = nearest2.Value % 14;
                                                            int row2 = nearest2.Value / 14;
                                                            int finalIdx2 = row2 * 14 + (col2 > 12 ? 12 : col2);
                                                            if (finalIdx2 >= 0 && finalIdx2 < palette2.Length) darkerBg2 = Color.FromArgb(bg.A, palette2[finalIdx2].R, palette2[finalIdx2].G, palette2[finalIdx2].B);
                                                        }
                                                        else
                                                        {
                                                            RgbToHsl(bg.R, bg.G, bg.B, out double hh2, out double ss2, out double ll2);
                                                            ll2 = Math.Max(0.0, ll2 - 0.12);
                                                            RgbFromHsl(hh2, ss2, ll2, out byte dr2, out byte dg2, out byte db2);
                                                            darkerBg2 = Color.FromArgb(bg.A, dr2, dg2, db2);
                                                        }
                                                    }
                                                    catch { }
                                                    ImageSource useImg2 = srcImg2 ?? new WriteableBitmap(1, 1, 96, 96, PixelFormats.Pbgra32, null);
                                                    try { if (srcImg2 != null) { var arr2 = CreateHslShiftedImages(new ImageSource?[] { srcImg2! }, darkerBg2); if (arr2 != null && arr2.Length > 0 && arr2[0] != null) useImg2 = arr2[0]!; } } catch { }
                                                var newIb2Img = App.EnsureUnfrozenForRender(useImg2) ?? useImg2;
                                                var newIb2 = new ImageBrush(newIb2Img)
                                                {
                                                    Stretch = ib2.Stretch,
                                                    TileMode = ib2.TileMode,
                                                    Viewport = ib2.Viewport,
                                                    ViewportUnits = ib2.ViewportUnits,
                                                };
                                                double ox2 = 0, oy2 = 0;
                                                try { if (ib2.Transform is TranslateTransform tt2) { ox2 = tt2.X; oy2 = tt2.Y; } else if (ib2.Transform is MatrixTransform mt2) { ox2 = mt2.Matrix.OffsetX; oy2 = mt2.Matrix.OffsetY; } } catch { }
                                                newIb2.Transform = new TranslateTransform(ox2 - destX, oy2 - destY);
                                                bgBrush2 = newIb2;
                                            }
                                        }
                                        catch { }
                                    }
                                        dc.DrawRectangle(bgBrush2 ?? new SolidColorBrush(Color.FromArgb(bg.A, bg.R, bg.G, bg.B)), null, new Rect(0, 0, bs.PixelWidth, bs.PixelHeight));
                                }
                            }
                            else
                            {
                                // Cropping the local tile layer cache returned nothing. Attempt to draw from the
                                // visible tileLayerImage.Source (full layer) so decorations still composite correctly
                                // even when the cached region doesn't cover the sprite.
                                try
                                {
                                    if (tileLayerImage != null && tileLayerImage.Source is BitmapSource fullLayer)
                                    {
                                        // Translate so that the sprite-local (destX,destY) region of the full layer
                                        // maps to (0,0) in the drawing visual.
                                        double tx = layerLeft - destX;
                                        double ty = layerTop - destY;
                                        dc.PushTransform(new TranslateTransform(tx, ty));
                                        var drawFull2 = App.EnsureUnfrozenForRender(fullLayer) ?? fullLayer;
                                        dc.DrawImage(drawFull2, new Rect(0, 0, fullLayer.PixelWidth, fullLayer.PixelHeight));
                                        dc.Pop();
                                    }
                                    else
                                    {
                                        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(bg.A, bg.R, bg.G, bg.B)), null, new Rect(0, 0, bs.PixelWidth, bs.PixelHeight));
                                    }
                                }
                                catch
                                {
                                    Brush? bgBrush3 = null;
                                    try { if (bgRectPersistent != null && bgRectPersistent.Fill != null) bgBrush3 = bgRectPersistent.Fill; } catch { bgBrush3 = null; }
                                    if (bgBrush3 == null)
                                    {
                                        bgBrush3 = new SolidColorBrush(Color.FromArgb(bg.A, bg.R, bg.G, bg.B));
                                    }
                                    else
                                    {
                                        try
                                        {
                                            if (bgBrush3 is ImageBrush ib3)
                                            {
                                                var srcImg3 = ib3.ImageSource;
                                                Color darkerBg3 = bg;
                                                try { RgbToHsl(bg.R, bg.G, bg.B, out double hh3, out double ss3, out double ll3); ll3 = Math.Max(0.0, ll3 - 0.12); RgbFromHsl(hh3, ss3, ll3, out byte dr3, out byte dg3, out byte db3); darkerBg3 = Color.FromArgb(bg.A, dr3, dg3, db3); } catch { }
                                                ImageSource useImg3 = srcImg3 ?? new WriteableBitmap(1, 1, 96, 96, PixelFormats.Pbgra32, null);
                                                try { if (srcImg3 != null) { var arr3 = CreateHslShiftedImages(new ImageSource?[] { srcImg3! }, darkerBg3); if (arr3 != null && arr3.Length > 0 && arr3[0] != null) useImg3 = arr3[0]!; } } catch { }
                                                var newIb3Img = App.EnsureUnfrozenForRender(useImg3) ?? useImg3;
                                                var newIb3 = new ImageBrush(newIb3Img)
                                                {
                                                    Stretch = ib3.Stretch,
                                                    TileMode = ib3.TileMode,
                                                    Viewport = ib3.Viewport,
                                                    ViewportUnits = ib3.ViewportUnits,
                                                };
                                                double ox3 = 0, oy3 = 0;
                                                try { if (ib3.Transform is TranslateTransform tt3) { ox3 = tt3.X; oy3 = tt3.Y; } else if (ib3.Transform is MatrixTransform mt3) { ox3 = mt3.Matrix.OffsetX; oy3 = mt3.Matrix.OffsetY; } } catch { }
                                                newIb3.Transform = new TranslateTransform(ox3 - destX, oy3 - destY);
                                                bgBrush3 = newIb3;
                                            }
                                        }
                                        catch { }
                                    }
                                    dc.DrawRectangle(bgBrush3 ?? new SolidColorBrush(Color.FromArgb(bg.A, bg.R, bg.G, bg.B)), null, new Rect(0, 0, bs.PixelWidth, bs.PixelHeight));
                                }
                            }
                        }
                        catch
                        {
                            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(bg.A, bg.R, bg.G, bg.B)), null, new Rect(0, 0, bs.PixelWidth, bs.PixelHeight));
                        }
                    }
                    else
                    {
                        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(bg.A, bg.R, bg.G, bg.B)), null, new Rect(0, 0, bs.PixelWidth, bs.PixelHeight));
                    }

                    // Draw the sprite on top; WPF will handle alpha blending correctly
                    var drawBs = App.EnsureUnfrozenForRender(bs) ?? bs;
                    dc.DrawImage(drawBs, new Rect(0, 0, bs.PixelWidth, bs.PixelHeight));
                }

                var rtb = new RenderTargetBitmap((int)bs.PixelWidth, (int)bs.PixelHeight, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);
                rtb.Freeze();
                spriteBackgroundCompositeCache[key] = rtb;
                return rtb;
            }
            catch { return src; }
        }

        // Regenerate toned images for tiles and saw frames using HSL hue shifting.
        // This mirrors MainWindow.UpdateTileTint's behavior so simulator can apply tints locally.
        private void UpdateTonedImagesForTileTint(Color newTileTint, Color? outlineOverride = null)
        {
            try
            {
                // Create HSL-shifted copies for tiles and saw frames
                // Allow caller to override the outline tint (used to keep outlines uncolored at startup).
                // If outlineOverride is null, fall back to the current `tileTint` member.
                Color outline = outlineOverride.HasValue ? outlineOverride.Value : tileTint;
                if (tileImages != null)
                {
                    if (backgroundForceSolidBlack)
                    {
                        try { tileTonedImages = CreateBlackMaskedExceptColorArray(tileImages, playerPlaceholderGreen, outline, false); }
                        catch { tileTonedImages = CreateHslShiftedImages(tileImages, newTileTint, outline); }
                    }
                    else
                    {
                        tileTonedImages = CreateHslShiftedImages(tileImages, newTileTint, outline);
                    }
                }

                // For a small set of tiles that contain decorative green pixels (#5ACE52),
                // ensure those pixels are not affected by tile/bg/obj tints and instead
                // receive only the player tint. Post-process the generated `tileTonedImages`
                // so the green pixels from the original tile images are replaced by `playerTint`.
                try
                {
                    if (tileTonedImages != null && tileImages != null)
                    {
                        int[] special = new int[] { 0x0C, 0x0D, 0x0E, 0x0F, 0x13, 0x14, 0x80, 0x81, 0x84, 0x85, 0x86, 0x87 };
                        foreach (var si in special)
                        {
                            if (si >= 0 && si < tileTonedImages.Length && si >= 0 && si < tileImages.Length && tileTonedImages[si] != null && tileImages[si] != null)
                            {
                                try { tileTonedImages[si] = ApplyPlayerTintToGreenPixels(tileTonedImages[si], tileImages[si], playerTint); } catch { }
                            }
                        }
                    }
                }
                catch { }

                if (sawFrame1TilesTinted != null && sawFrame1TilesTinted.Length > 0)
                {
                    // If we already had tinted saw frames passed in, re-tint the original saw frames
                    // Fallback: if original arrays are null, do nothing
                }

                // NOTE: saw frames are intentionally NOT retinted here from tile tint.
                // Saws should respond only to background color triggers (handled elsewhere).
            }
            catch { }
        }

        // Create a black-masked copy of a single image: non-white pixels become black.
        // If `outlineTint` is provided (alpha>0) and `recolorOutline` is true, near-white
        // pixels will be recolored to that tint so object-outline recoloring continues to
        // work when appropriate. When `recolorOutline` is false the top seam/near-white
        // pixels are left untouched so another system (object tinting) can control them.
        private ImageSource? CreateBlackMaskedImage(ImageSource? src, Color outlineTint = default, bool recolorOutline = true)
        {
            if (src == null) return null;
            if (!(src is BitmapSource bs)) return src;
            try
            {
                var conv = new FormatConvertedBitmap(bs, PixelFormats.Pbgra32, null, 0);
                int w = Math.Max(1, conv.PixelWidth);
                int h = Math.Max(1, conv.PixelHeight);
                int stride = w * 4;
                var pixels = new byte[h * stride];
                conv.CopyPixels(pixels, stride, 0);
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    byte b = pixels[i + 0];
                    byte g = pixels[i + 1];
                    byte r = pixels[i + 2];
                    byte a = pixels[i + 3];
                    if (a == 0) continue;
                    // Use perceptual luminance to detect near-white (preserve thin white lines
                    // and anti-aliased edge pixels).
                    double lum = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;
                    bool isWhite = lum >= 0.82;
                    if (isWhite)
                    {
                        if (recolorOutline && outlineTint.A > 0)
                        {
                            // Recolor near-white pixels to the outline tint so object tints still apply.
                            pixels[i + 3] = 255;
                            pixels[i + 2] = outlineTint.R;
                            pixels[i + 1] = outlineTint.G;
                            pixels[i + 0] = outlineTint.B;
                        }
                        else
                        {
                            // Preserve original near-white pixel color to avoid overwriting downstream tinting.
                            continue;
                        }
                    }
                    else
                    {
                        // Make pixel opaque black
                        pixels[i + 0] = 0;
                        pixels[i + 1] = 0;
                        pixels[i + 2] = 0;
                        pixels[i + 3] = 255;
                    }
                }
                var wb = new WriteableBitmap(w, h, conv.DpiX, conv.DpiY, PixelFormats.Pbgra32, null);
                wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
                // Do not freeze WriteableBitmap; it must remain writable for Lock/WritePixels.
                return wb;
            }
            catch { return src; }
        }

        // Create a black-masked copy but preserve pixels matching `excludeColor` (exact RGB match)
        // and preserve near-white outline pixels unless recolorOutline==true.
        private ImageSource? CreateBlackMaskedExceptColor(ImageSource? src, Color excludeColor, Color outlineTint = default, bool recolorOutline = true)
        {
            if (src == null) return null;
            if (!(src is BitmapSource bs)) return src;
            try
            {
                var conv = new FormatConvertedBitmap(bs, PixelFormats.Pbgra32, null, 0);
                int w = Math.Max(1, conv.PixelWidth);
                int h = Math.Max(1, conv.PixelHeight);
                int stride = w * 4;
                var pixels = new byte[h * stride];
                conv.CopyPixels(pixels, stride, 0);
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    byte b = pixels[i + 0];
                    byte g = pixels[i + 1];
                    byte r = pixels[i + 2];
                    byte a = pixels[i + 3];
                    if (a == 0) continue;
                    // Preserve exact exclude color (e.g., player placeholder green #FF322B)
                    if (r == excludeColor.R && g == excludeColor.G && b == excludeColor.B) continue;
                    double lum = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;
                    bool isWhite = (lum >= 0.82);
                    if (isWhite)
                    {
                        if (recolorOutline && outlineTint.A > 0)
                        {
                            pixels[i + 3] = 255;
                            pixels[i + 2] = outlineTint.R;
                            pixels[i + 1] = outlineTint.G;
                            pixels[i + 0] = outlineTint.B;
                        }
                        else
                        {
                            continue;
                        }
                    }
                    else
                    {
                        // Make pixel opaque black
                        pixels[i + 0] = 0; pixels[i + 1] = 0; pixels[i + 2] = 0; pixels[i + 3] = 255;
                    }
                }
                var wb = new WriteableBitmap(w, h, conv.DpiX, conv.DpiY, PixelFormats.Pbgra32, null);
                wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
                // Do not freeze WriteableBitmap; it must remain writable for Lock/WritePixels.
                return wb;
            }
            catch { return src; }
        }

        // Recolor exact-match green pixels (#5ACE52) from the original image to the given playerTint
        // in the provided source image. This preserves the pixel layout from `src` while ensuring
        // only the special green areas receive the player tint color.
        private ImageSource? ApplyPlayerTintToGreenPixels(ImageSource? src, ImageSource? original, Color playerTint)
        {
            if (src == null || original == null) return src;
            if (!(src is BitmapSource sb) || !(original is BitmapSource ob)) return src;

            try
            {
                var convSrc = new FormatConvertedBitmap(sb, PixelFormats.Pbgra32, null, 0);
                var convOrig = new FormatConvertedBitmap(ob, PixelFormats.Pbgra32, null, 0);
                int w = Math.Max(1, convSrc.PixelWidth);
                int h = Math.Max(1, convSrc.PixelHeight);
                if (convOrig.PixelWidth != w || convOrig.PixelHeight != h) return src;
                int stride = w * 4;
                var pixelsSrc = new byte[h * stride];
                var pixelsOrig = new byte[h * stride];
                convSrc.CopyPixels(pixelsSrc, stride, 0);
                convOrig.CopyPixels(pixelsOrig, stride, 0);

                // special green RGB
                byte gR = 0x5A; byte gG = 0xCE; byte gB = 0x52; // #5ACE52

                for (int i = 0; i < pixelsSrc.Length; i += 4)
                {
                    byte ob_b = pixelsOrig[i + 0];
                    byte ob_g = pixelsOrig[i + 1];
                    byte ob_r = pixelsOrig[i + 2];
                    byte ob_a = pixelsOrig[i + 3];
                    if (ob_a == 0) continue;
                    if (ob_r == gR && ob_g == gG && ob_b == gB)
                    {
                        // apply playerTint (opaque)
                        pixelsSrc[i + 3] = playerTint.A;
                        pixelsSrc[i + 2] = playerTint.R;
                        pixelsSrc[i + 1] = playerTint.G;
                        pixelsSrc[i + 0] = playerTint.B;
                    }
                }

                var wb = new WriteableBitmap(w, h, convSrc.DpiX, convSrc.DpiY, PixelFormats.Pbgra32, null);
                wb.WritePixels(new Int32Rect(0, 0, w, h), pixelsSrc, stride, 0);
                // Do not freeze WriteableBitmap; it must remain writable for Lock/WritePixels.
                return wb;
            }
            catch { return src; }
        }

        // Create HSL-hue shifted copies of images. For each visible, non-black/non-white pixel
        // we replace the hue with the tint's hue while preserving the original saturation and lightness.
        // Returns originals if tint.A == 0.
        private ImageSource?[]? CreateHslShiftedImages(ImageSource?[]? originals, Color tint, Color outlineTint = default)
        {
            if (originals == null) return null;
            if (tint.A == 0) return originals; // no change requested
            // If the tint is fully opaque (A==255) and not the special-cased black/white handled above,
            // perform a full replacement: set non-transparent, non-black pixels to the exact tint RGB.
            // Preserve near-white outline pixels according to `outlineTint` so seam recoloring still works.
            var outList = new System.Collections.Generic.List<ImageSource>(originals.Length);
            if (tint.A == 255)
            {
                foreach (var src in originals)
                {
                    if (src == null)
                    {
                        var pf = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Pbgra32, null);
                        var pxf = new byte[4];
                        try { pf.WritePixels(new Int32Rect(0, 0, 1, 1), pxf, 4, 0); } catch { }
                        try { pf.Freeze(); } catch { }
                        outList.Add(pf);
                        continue;
                    }
                    if (src is BitmapSource bs)
                    {
                        try
                        {
                            var conv = new FormatConvertedBitmap(bs, PixelFormats.Pbgra32, null, 0);
                            int w = conv.PixelWidth; int h = conv.PixelHeight; int stride = w * 4;
                            var pixels = new byte[h * stride];
                            conv.CopyPixels(pixels, stride, 0);

                            double nearWhiteThreshold = 0.82;
                            if (outlineTint.A == 255 && outlineTint.R == 0 && outlineTint.G == 0 && outlineTint.B == 0) nearWhiteThreshold = 0.70;

                            for (int i = 0; i < pixels.Length; i += 4)
                            {
                                byte b = pixels[i + 0];
                                byte g = pixels[i + 1];
                                byte r = pixels[i + 2];
                                byte a = pixels[i + 3];
                                if (a == 0) continue;
                                double lum = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;
                                bool isBlack = (r <= 12 && g <= 12 && b <= 12);
                                bool isNearWhite = (lum >= nearWhiteThreshold);
                                if (isBlack) continue;
                                if (isNearWhite)
                                {
                                    if (outlineTint.A > 0)
                                    {
                                        pixels[i + 3] = outlineTint.A;
                                        pixels[i + 2] = outlineTint.R;
                                        pixels[i + 1] = outlineTint.G;
                                        pixels[i + 0] = outlineTint.B;
                                    }
                                    continue;
                                }
                                // Full replacement with tint color
                                pixels[i + 3] = 255;
                                pixels[i + 2] = tint.R;
                                pixels[i + 1] = tint.G;
                                pixels[i + 0] = tint.B;
                            }

                            var wb = new WriteableBitmap(w, h, conv.DpiX, conv.DpiY, PixelFormats.Pbgra32, null);
                            wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
                            // Do not freeze WriteableBitmap; it must remain writable for Lock/WritePixels.
                            outList.Add(wb);
                        }
                        catch
                        {
                            if (src == null)
                            {
                                var pf = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Pbgra32, null);
                                try { pf.WritePixels(new Int32Rect(0, 0, 1, 1), new byte[4], 4, 0); } catch { }
                                try { pf.Freeze(); } catch { }
                                outList.Add(pf);
                            }
                            else outList.Add(src);
                        }
                    }
                    else
                    {
                        if (src == null)
                        {
                            var pf = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Bgra32, null);
                            try { pf.WritePixels(new Int32Rect(0, 0, 1, 1), new byte[4], 4, 0); } catch { }
                            try { pf.Freeze(); } catch { }
                            outList.Add(pf);
                        }
                        else outList.Add(src);
                    }
                }
                return outList.ToArray();
            }

            // Precompute tint hue
            RgbToHsl(tint.R, tint.G, tint.B, out double tintH, out double tintS, out double tintL);

            
            foreach (var src in originals)
            {
                if (src is BitmapSource bs)
                {
                    try
                    {
                        var conv = new FormatConvertedBitmap(bs, PixelFormats.Bgra32, null, 0);
                        int w = conv.PixelWidth; int h = conv.PixelHeight; int stride = w * 4;
                        var pixels = new byte[h * stride];
                        conv.CopyPixels(pixels, stride, 0);

                        for (int i = 0; i < pixels.Length; i += 4)
                        {
                            byte b = pixels[i + 0];
                            byte g = pixels[i + 1];
                            byte r = pixels[i + 2];
                            byte a = pixels[i + 3];
                            // Skip fully transparent and near-black pixels. Handle near-white outlines
                            // via perceptual luminance so anti-aliased white lines are detected reliably.
                            if (a == 0) continue;
                            double lum = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;
                            bool isBlack = (r <= 12 && g <= 12 && b <= 12);
                            double nearWhiteThreshold = 0.82;
                            if (outlineTint.A == 255 && outlineTint.R == 0 && outlineTint.G == 0 && outlineTint.B == 0) nearWhiteThreshold = 0.70;
                            bool isNearWhite = (lum >= nearWhiteThreshold);
                            if (isBlack) continue;
                            if (isNearWhite)
                            {
                                if (outlineTint.A > 0)
                                {
                                    pixels[i + 3] = outlineTint.A;
                                    pixels[i + 2] = outlineTint.R;
                                    pixels[i + 1] = outlineTint.G;
                                    pixels[i + 0] = outlineTint.B;
                                }
                                continue;
                            }

                            // Convert pixel to HSL, replace hue with tint hue, keep S/L
                            RgbToHsl(r, g, b, out double ph, out double ps, out double pl);
                            double nh = tintH; // replace hue
                            double ns = ps;
                            double nl = pl;
                            RgbFromHsl(nh, ns, nl, out byte nr, out byte ng, out byte nb);

                            pixels[i + 0] = nb;
                            pixels[i + 1] = ng;
                            pixels[i + 2] = nr;
                            // alpha unchanged
                        }

                        var wb = new WriteableBitmap(w, h, conv.DpiX, conv.DpiY, PixelFormats.Bgra32, null);
                        wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
                        // Do not freeze WriteableBitmap; it must remain writable for Lock/WritePixels.
                        outList.Add(wb);
                    }
                    catch
                    {
                        if (src == null)
                        {
                            var pf = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Bgra32, null);
                            try { pf.WritePixels(new Int32Rect(0, 0, 1, 1), new byte[4], 4, 0); } catch { }
                            try { pf.Freeze(); } catch { }
                            outList.Add(pf);
                        }
                        else outList.Add(src);
                    }
                }
                else
                {
                    if (src == null)
                    {
                        var pf = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Bgra32, null);
                        try { pf.WritePixels(new Int32Rect(0, 0, 1, 1), new byte[4], 4, 0); } catch { }
                        try { pf.Freeze(); } catch { }
                        outList.Add(pf);
                    }
                    else outList.Add(src);
                }
            }
            return outList.ToArray();
        }

        // Create a simple 2-frame pulsing animation from a single sprite image by producing
        // a slightly brightened second frame. Returns null on failure.
        private ImageSource?[]? CreateTwoFramePulse(ImageSource src)
        {
            try
            {
                if (src is BitmapSource bs)
                {
                    // Convert to BGRA32
                    var conv = new FormatConvertedBitmap(bs, PixelFormats.Bgra32, null, 0);
                    int w = Math.Max(1, conv.PixelWidth);
                    int h = Math.Max(1, conv.PixelHeight);
                    int stride = w * 4;
                    var pixels = new byte[h * stride];
                    conv.CopyPixels(pixels, stride, 0);

                    // Create brightened copy by blending each color toward white (alpha preserved)
                    var bright = new byte[h * stride];
                    const double factor = 0.35; // how strongly to brighten
                    for (int i = 0; i < pixels.Length; i += 4)
                    {
                        byte b = pixels[i + 0];
                        byte g = pixels[i + 1];
                        byte r = pixels[i + 2];
                        byte a = pixels[i + 3];
                        if (a == 0) { bright[i + 0] = b; bright[i + 1] = g; bright[i + 2] = r; bright[i + 3] = a; continue; }
                        bright[i + 0] = (byte)Math.Min(255, (int)Math.Round(b + (255 - b) * factor));
                        bright[i + 1] = (byte)Math.Min(255, (int)Math.Round(g + (255 - g) * factor));
                        bright[i + 2] = (byte)Math.Min(255, (int)Math.Round(r + (255 - r) * factor));
                        bright[i + 3] = a;
                    }

                    var wb1 = new WriteableBitmap(conv.PixelWidth, conv.PixelHeight, conv.DpiX, conv.DpiY, PixelFormats.Bgra32, null);
                    wb1.WritePixels(new Int32Rect(0, 0, conv.PixelWidth, conv.PixelHeight), pixels, stride, 0);
                    wb1.Freeze();
                    var wb2 = new WriteableBitmap(conv.PixelWidth, conv.PixelHeight, conv.DpiX, conv.DpiY, PixelFormats.Bgra32, null);
                    wb2.WritePixels(new Int32Rect(0, 0, conv.PixelWidth, conv.PixelHeight), bright, stride, 0);
                    wb2.Freeze();
                    return new ImageSource?[] { wb1, wb2 };
                }
            }
            catch { }
            return null;
        }

        // Find the nearest palette index for a given color. Returns null on failure.
        private int? GetNearestPaletteIndexForColor(Color c, Color[]? palette)
        {
            try
            {
                if (palette == null || palette.Length == 0) return null;
                int best = -1; double bestDist = double.MaxValue;
                for (int i = 0; i < palette.Length; i++)
                {
                    var p = palette[i];
                    double dr = p.R - c.R; double dg = p.G - c.G; double db = p.B - c.B;
                    double d = dr * dr + dg * dg + db * db;
                    if (d < bestDist) { bestDist = d; best = i; }
                }
                if (best >= 0) return best;
            }
            catch { }
            return null;
        }

        // Create hue-shifted images with interpolation towards tint hue (used for ground in MainWindow)
        private ImageSource?[]? CreateHueShiftedImages(ImageSource?[]? originals, Color tint, Color outlineTint = default)
        {
            if (originals == null) return null;
            if (tint.A == 0) return originals; // strength 0 => no change
            // If the tint is pure opaque black, produce two-tone images that map
            // non-white pixels to pure black while preserving near-white outlines
            // so object-outline tints can recolor the seam. Use CreateTwoToneTileImages
            // for consistent behavior with other ground/tiles code paths.
            if (tint.A == 255 && tint.R == 0 && tint.G == 0 && tint.B == 0)
            {
                return CreateTwoToneTileImages(originals, Color.FromArgb(255, 0, 0, 0), Color.FromArgb(255, 0, 0, 0), outlineTint);
            }

            // If the tint is pure opaque white, produce white-masked images where
            // non-white pixels become opaque white while preserving near-white
            // seam pixels (so object-outline tinting can control them). Use the
            // outlineTint only for cases where callers want seam recoloring; by
            // default we avoid recoloring the seam here.
            if (tint.A == 255 && tint.R == 255 && tint.G == 255 && tint.B == 255)
            {
                return CreateWhiteMaskedImages(originals, outlineTint, false);
            }

            // If the tint is fully opaque (but not pure-black/white handled above),
            // perform a full replacement of pixel colors with the tint while preserving
            // near-white outlines according to `outlineTint`.
            if (tint.A == 255)
            {
                var outListFull = new System.Collections.Generic.List<ImageSource>(originals.Length);
                foreach (var src in originals)
                {
                    if (src == null)
                    {
                        var pf = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Bgra32, null);
                        var pxf = new byte[4];
                        try { pf.WritePixels(new Int32Rect(0, 0, 1, 1), pxf, 4, 0); } catch { }
                        try { pf.Freeze(); } catch { }
                        outListFull.Add(pf);
                        continue;
                    }
                    if (src is BitmapSource bs)
                    {
                        try
                        {
                            var conv = new FormatConvertedBitmap(bs, PixelFormats.Bgra32, null, 0);
                            int w = conv.PixelWidth; int h = conv.PixelHeight; int stride = w * 4;
                            var pixels = new byte[h * stride];
                            conv.CopyPixels(pixels, stride, 0);

                            double nearWhiteThreshold = 0.82;
                            if (outlineTint.A == 255 && outlineTint.R == 0 && outlineTint.G == 0 && outlineTint.B == 0) nearWhiteThreshold = 0.70;

                            for (int i = 0; i < pixels.Length; i += 4)
                            {
                                byte b = pixels[i + 0];
                                byte g = pixels[i + 1];
                                byte r = pixels[i + 2];
                                byte a = pixels[i + 3];
                                if (a == 0) continue;
                                double lum = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;
                                bool isBlack = (r <= 12 && g <= 12 && b <= 12);
                                bool isNearWhite = (lum >= nearWhiteThreshold);
                                if (isBlack) continue;
                                if (isNearWhite)
                                {
                                    if (outlineTint.A > 0)
                                    {
                                        pixels[i + 3] = outlineTint.A;
                                        pixels[i + 2] = outlineTint.R;
                                        pixels[i + 1] = outlineTint.G;
                                        pixels[i + 0] = outlineTint.B;
                                    }
                                    continue;
                                }
                                // Full replacement with tint color
                                pixels[i + 3] = 255;
                                pixels[i + 2] = tint.R;
                                pixels[i + 1] = tint.G;
                                pixels[i + 0] = tint.B;
                            }

                            var wb = new WriteableBitmap(w, h, conv.DpiX, conv.DpiY, PixelFormats.Bgra32, null);
                            wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
                            // Do not freeze WriteableBitmap; it must remain writable for Lock/WritePixels.
                            outListFull.Add(wb);
                        }
                        catch
                        {
                            if (src == null)
                            {
                                var pf = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Bgra32, null);
                                try { pf.WritePixels(new Int32Rect(0, 0, 1, 1), new byte[4], 4, 0); } catch { }
                                try { pf.Freeze(); } catch { }
                                outListFull.Add(pf);
                            }
                            else outListFull.Add(src);
                        }
                    }
                    else outListFull.Add(src);
                }
                return outListFull.ToArray();
            }

            double strength = tint.A / 255.0;
            // convert tint color to HSL once
            RgbToHsl(tint.R, tint.G, tint.B, out double tintH, out double tintS, out double tintL);
            var outList = new System.Collections.Generic.List<ImageSource>(originals.Length);

            foreach (var src in originals)
            {
                if (src == null)
                {
                    var pf = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Bgra32, null);
                    var pxf = new byte[4];
                    try { pf.WritePixels(new Int32Rect(0, 0, 1, 1), pxf, 4, 0); } catch { }
                    try { pf.Freeze(); } catch { }
                    outList.Add(pf);
                    continue;
                }
                if (src is BitmapSource bs)
                {
                    try
                    {
                        var conv = new FormatConvertedBitmap(bs, PixelFormats.Bgra32, null, 0);
                        int w = conv.PixelWidth; int h = conv.PixelHeight; int stride = w * 4;
                        var pixels = new byte[h * stride];
                        conv.CopyPixels(pixels, stride, 0);

                        for (int i = 0; i < pixels.Length; i += 4)
                        {
                            int b = pixels[i + 0];
                            int g = pixels[i + 1];
                            int r = pixels[i + 2];
                            int a = pixels[i + 3];

                            // Preserve fully transparent pixels
                            if (a == 0) continue;

                            // Use perceptual luminance to identify near-white outline pixels.
                            double lum = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;
                            bool isNearWhite = lum >= 0.82;

                            if (isNearWhite && outlineTint.A > 0)
                            {
                                // Apply outline tint exactly for near-white pixels when requested
                                pixels[i + 3] = outlineTint.A;
                                pixels[i + 2] = outlineTint.R;
                                pixels[i + 1] = outlineTint.G;
                                pixels[i + 0] = outlineTint.B;
                                continue;
                            }

                            RgbToHsl((byte)r, (byte)g, (byte)b, out double h0, out double s0, out double l0);

                            // interpolate hue towards tint hue, and optionally scale/lerp saturation
                            double newH = LerpAngle(h0, tintH, strength);
                            double newS = s0 * (1.0 - strength) + tintS * strength;
                            double newL = l0; // preserve original lightness to keep details

                            RgbFromHsl(newH, newS, newL, out byte r2, out byte g2, out byte b2);

                            pixels[i + 0] = b2;
                            pixels[i + 1] = g2;
                            pixels[i + 2] = r2;
                            pixels[i + 3] = (byte)a; // keep original alpha
                        }

                        var wb = new WriteableBitmap(w, h, conv.DpiX, conv.DpiY, PixelFormats.Bgra32, null);
                        wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
                        // Do not freeze WriteableBitmap; it must remain writable for Lock/WritePixels.
                        outList.Add(wb);
                    }
                    catch
                    {
                        outList.Add(src ?? new WriteableBitmap(1, 1, 96, 96, PixelFormats.Bgra32, null));
                    }
                }
                else
                {
                    outList.Add(src ?? new WriteableBitmap(1, 1, 96, 96, PixelFormats.Bgra32, null));
                }
            }
            return outList.ToArray();
        }

        // Create images where non-white pixels become solid black (alpha preserved for transparent pixels),
        // and near-white pixels are preserved as white. This produces a 'black with white line' effect
        // useful for pure-black triggers.
        private ImageSource?[]? CreateBlackMaskedImages(ImageSource?[]? originals, Color outlineTint = default, bool recolorOutline = true)
        {
            if (originals == null) return null;
            var outList = new System.Collections.Generic.List<ImageSource>(originals.Length);
            foreach (var src in originals)
            {
                if (src == null)
                {
                    var pf = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Bgra32, null);
                    var pxf = new byte[4];
                    try { pf.WritePixels(new Int32Rect(0, 0, 1, 1), pxf, 4, 0); } catch { }
                    try { pf.Freeze(); } catch { }
                    outList.Add(pf);
                    continue;
                }
                if (src is BitmapSource bs)
                {
                    try
                    {
                        var conv = new FormatConvertedBitmap(bs, PixelFormats.Bgra32, null, 0);
                        int w = conv.PixelWidth; int h = conv.PixelHeight; int stride = w * 4;
                        var pixels = new byte[h * stride];
                        conv.CopyPixels(pixels, stride, 0);

                        for (int i = 0; i < pixels.Length; i += 4)
                        {
                            byte b = pixels[i + 0];
                            byte g = pixels[i + 1];
                            byte r = pixels[i + 2];
                            byte a = pixels[i + 3];
                            if (a == 0) continue; // preserve transparency
                            double lum = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;
                            bool isWhite = (lum >= 0.82);
                            if (isWhite)
                            {
                                if (recolorOutline && outlineTint.A > 0)
                                {
                                    pixels[i + 3] = 255;
                                    pixels[i + 2] = outlineTint.R;
                                    pixels[i + 1] = outlineTint.G;
                                    pixels[i + 0] = outlineTint.B;
                                }
                                else
                                {
                                    // preserve original near-white color
                                    continue;
                                }
                            }
                            else
                            {
                                pixels[i + 0] = 0; pixels[i + 1] = 0; pixels[i + 2] = 0;
                                pixels[i + 3] = 255;
                            }
                        }

                        var wb = new WriteableBitmap(w, h, conv.DpiX, conv.DpiY, PixelFormats.Bgra32, null);
                        wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
                        // Do not freeze WriteableBitmap; it must remain writable for Lock/WritePixels.
                        outList.Add(wb);
                    }
                    catch
                    {
                        outList.Add(src);
                    }
                }
                else
                {
                    outList.Add(src);
                }
            }
            return outList.ToArray();
        }

        private ImageSource?[]? CreateBlackMaskedExceptColorArray(ImageSource?[]? originals, Color excludeColor, Color outlineTint = default, bool recolorOutline = true)
        {
            if (originals == null) return null;
            var outList = new System.Collections.Generic.List<ImageSource>(originals.Length);
            foreach (var src in originals)
            {
                try
                {
                    var v = CreateBlackMaskedExceptColor(src, excludeColor, outlineTint, recolorOutline);
                    outList.Add(v ?? src ?? new WriteableBitmap(1,1,96,96,PixelFormats.Bgra32,null));
                }
                catch { outList.Add(src ?? new WriteableBitmap(1,1,96,96,PixelFormats.Bgra32,null)); }
            }
            return outList.ToArray();
        }

        // Create white-masked images: non-white pixels become opaque white, and
        // near-white pixels are left untouched unless `recolorOutline` is true.
        // This mirrors CreateBlackMaskedImages but produces white instead, used for
        // representing a pure-white ground tint so the seam can remain editable by
        // object-outline tints.
        private ImageSource?[]? CreateWhiteMaskedImages(ImageSource?[]? originals, Color outlineTint = default, bool recolorOutline = false)
        {
            if (originals == null) return null;
            var outList = new System.Collections.Generic.List<ImageSource>(originals.Length);
            foreach (var src in originals)
            {
                if (src == null)
                {
                    var pf = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Bgra32, null);
                    var pxf = new byte[4];
                    try { pf.WritePixels(new Int32Rect(0, 0, 1, 1), pxf, 4, 0); } catch { }
                    try { pf.Freeze(); } catch { }
                    outList.Add(pf);
                    continue;
                }
                if (src is BitmapSource bs)
                {
                    try
                    {
                        var conv = new FormatConvertedBitmap(bs, PixelFormats.Bgra32, null, 0);
                        int w = conv.PixelWidth; int h = conv.PixelHeight; int stride = w * 4;
                        var pixels = new byte[h * stride];
                        conv.CopyPixels(pixels, stride, 0);

                        for (int i = 0; i < pixels.Length; i += 4)
                        {
                            byte b = pixels[i + 0];
                            byte g = pixels[i + 1];
                            byte r = pixels[i + 2];
                            byte a = pixels[i + 3];
                            if (a == 0) continue; // preserve transparency
                            double lum = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;
                            bool isNearWhite = (lum >= 0.82);
                            if (isNearWhite)
                            {
                                if (recolorOutline && outlineTint.A > 0)
                                {
                                    pixels[i + 3] = 255;
                                    pixels[i + 2] = outlineTint.R;
                                    pixels[i + 1] = outlineTint.G;
                                    pixels[i + 0] = outlineTint.B;
                                }
                                else
                                {
                                    // leave near-white seam pixels untouched
                                    continue;
                                }
                            }
                            else
                            {
                                // make pixel opaque white
                                pixels[i + 2] = 255;
                                pixels[i + 1] = 255;
                                pixels[i + 0] = 255;
                                pixels[i + 3] = 255;
                            }
                        }

                        var wb = new WriteableBitmap(w, h, conv.DpiX, conv.DpiY, PixelFormats.Bgra32, null);
                        wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
                        // Do not freeze WriteableBitmap; it must remain writable for Lock/WritePixels.
                        outList.Add(wb);
                    }
                    catch { outList.Add(src); }
                }
                else outList.Add(src);
            }
            return outList.ToArray();
        }

        // Create fully black images: non-transparent pixels become opaque black.
        // Use this for saws when background is pure black so no white lines remain.
        private ImageSource?[]? CreateSolidBlackImages(ImageSource?[]? originals)
        {
            if (originals == null) return null;
            var outList = new System.Collections.Generic.List<ImageSource>(originals.Length);
            foreach (var src in originals)
            {
                if (src == null)
                {
                    var pf = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Bgra32, null);
                    var pxf = new byte[4];
                    try { pf.WritePixels(new Int32Rect(0, 0, 1, 1), pxf, 4, 0); } catch { }
                    try { pf.Freeze(); } catch { }
                    outList.Add(pf);
                    continue;
                }
                if (src is BitmapSource bs)
                {
                    try
                    {
                        var conv = new FormatConvertedBitmap(bs, PixelFormats.Bgra32, null, 0);
                        int w = Math.Max(1, conv.PixelWidth);
                        int h = Math.Max(1, conv.PixelHeight);
                        int stride = w * 4;
                        var pixels = new byte[h * stride];
                        conv.CopyPixels(pixels, stride, 0);
                        for (int i = 0; i < pixels.Length; i += 4)
                        {
                            byte a = pixels[i + 3];
                            if (a == 0) continue;
                            pixels[i + 0] = 0; // b
                            pixels[i + 1] = 0; // g
                            pixels[i + 2] = 0; // r
                            pixels[i + 3] = 255; // make opaque
                        }
                        var wb = new WriteableBitmap(w, h, conv.DpiX, conv.DpiY, PixelFormats.Bgra32, null);
                        wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
                        // Do not freeze WriteableBitmap; it must remain writable for Lock/WritePixels.
                        outList.Add(wb);
                    }
                    catch { outList.Add(src); }
                }
                else outList.Add(src);
            }
            return outList.ToArray();
        }

        // Helper: convert RGB byte values to HSL (H in degrees 0..360, S/L 0..1)
        private static void RgbToHsl(byte r8, byte g8, byte b8, out double h, out double s, out double l)
        {
            double r = r8 / 255.0, g = g8 / 255.0, b = b8 / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            l = (max + min) / 2.0;
            if (max == min)
            {
                h = 0.0; s = 0.0; return;
            }
            double d = max - min;
            s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
            if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h *= 60.0;
        }

        // Helper: convert HSL to RGB bytes. H in degrees 0..360, S/L 0..1
        private static void RgbFromHsl(double h, double s, double l, out byte r8, out byte g8, out byte b8)
        {
            double r, g, b;
            if (s == 0)
            {
                r = g = b = l; // achromatic
            }
            else
            {
                double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
                double p = 2 * l - q;
                double hk = (h % 360.0) / 360.0;
                double[] t = new double[3] { hk + 1.0 / 3.0, hk, hk - 1.0 / 3.0 };
                double[] rgb = new double[3];
                for (int i = 0; i < 3; i++)
                {
                    double tc = t[i];
                    if (tc < 0) tc += 1.0; if (tc > 1) tc -= 1.0;
                    if (tc < 1.0 / 6.0) rgb[i] = p + (q - p) * 6.0 * tc;
                    else if (tc < 1.0 / 2.0) rgb[i] = q;
                    else if (tc < 2.0 / 3.0) rgb[i] = p + (q - p) * (2.0 / 3.0 - tc) * 6.0;
                    else rgb[i] = p;
                }
                r = rgb[0]; g = rgb[1]; b = rgb[2];
            }
            r8 = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(r * 255.0)));
            g8 = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(g * 255.0)));
            b8 = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(b * 255.0)));
        }

        // Linear interpolation for circular hue (degrees). t in 0..1
        private static double LerpAngle(double a, double b, double t)
        {
            // convert to radians for shortest path
            double diff = (b - a + 540.0) % 360.0 - 180.0;
            return (a + diff * t + 360.0) % 360.0;
        }
    }
}

