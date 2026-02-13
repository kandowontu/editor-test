using System;
using System.Collections.Generic;
using System.Linq;

namespace FamidashEditor
{
    /// <summary>
    /// Standalone offline pathfinder engine that pre-calculates the optimal input sequence
    /// for a level using greedy lookahead. Runs in the editor (no real-time pressure).
    /// 
    /// Physics ordering matches the REAL simulator (SimulateNumericStep + ProcessCubePhysics_Fresh):
    ///   1. Advance X
    ///   2. Ground support verification (after X advance)
    ///   3. Sprite interaction checks (portals, pads, end trigger)
    ///   4. Physics dispatch (cube mode):
    ///      a. CommonGravityRoutine (gravity + Y integration; always applies)
    ///      b. CheckCenterPointDeath (single center pixel)
    ///      c. CubeEject (floor/ceiling collision + snap)
    ///      d. Jump input (ONLY when velY == 0 after ejection)
    ///   5. Forward collision (right-edge death for cube/robot/ninja)
    ///   6. CheckDeathCollision (5-point hitbox corners + right-center)
    /// </summary>
    public class PathfinderEngine
    {
        // ── Constants ──────────────────────────────────────────────────────
        private const int TILE = 16;
        private const int LOOKAHEAD_HORIZON = 90;
        private const int MAX_SPECULATIVE_DEPTH = 3; // max recursion depth for chained jump evaluation
        private const int CORRIDOR_LOOK_AHEAD_TILES = 10; // scan ahead for obstacles (160px ≈ 58 frames at 1x)
        private const int MAX_FRAMES = 60 * 60 * 5; // 5 minutes at 60fps
        private const int MAX_BACKTRACK_ATTEMPTS = 150; // max total backtrack retries
        private const int MAX_CHECKPOINT_DEPTH = 20;    // max saved decision checkpoints

        /// <summary>
        /// Jump timing bias: 0.0 = earliest viable jump, 0.5 = middle (default), 1.0 = latest viable jump.
        /// </summary>
        public double JumpTimingBias { get; set; } = 0.5;

        // Cube hitbox (from collision.h)
        private const int CUBE_HITBOX_W = 15;
        private const int CUBE_HITBOX_H = 15;
        private const int MINI_CUBE_HITBOX_W = 8;
        private const int MINI_CUBE_HITBOX_H = 7;

        // ── Speed lookup ───────────────────────────────────────────────────
        private static int SpeedUiIndexToFixed(int uiIndex)
        {
            return uiIndex switch
            {
                0 => 0x23B, // 0.5x
                1 => 0x2C4, // 1x
                2 => 0x371, // 2x
                3 => 0x429, // 3x
                4 => 0x51E, // 4x
                _ => 0x2C4
            };
        }

        private static int SpriteIdToSpeedFixed(int sid)
        {
            return sid switch
            {
                0x6D => 0x16E,
                0x14 => 0x23B,
                0x15 => 0x2C4,
                0x16 => 0x371,
                0x20 => 0x429,
                0x21 => 0x51E,
                _ => -1
            };
        }

        private static int SpriteIdToGameMode(int sid)
        {
            return sid switch
            {
                0x00 => 0, 0x01 => 1, 0x02 => 2, 0x03 => 3,
                0x04 => 4, 0x17 => 5, 0x24 => 6, 0x4B => 7,
                0x58 => 8, 0x6A => 9, 0x6B => 10, 0x6C => 11,
                _ => -1
            };
        }

        private static int GetGravity(bool mini) => mini ? 0x6F : 0x6B;
        private static int GetJumpVel(bool mini) => mini ? -0x4D0 : -0x590;
        private static int GetHitboxW(bool mini) => mini ? MINI_CUBE_HITBOX_W : CUBE_HITBOX_W;
        private static int GetHitboxH(bool mini) => mini ? MINI_CUBE_HITBOX_H : CUBE_HITBOX_H;
        private static int GetHitboxOffsetY(bool mini, bool gravFlipped) =>
            (mini && !gravFlipped) ? 9 : 0;

        // Ship physics constants (from GameModePhysics.cs, 60fps values)
        private static int ShipGravityBase(bool mini) => mini ? 0x31 : 0x2A;
        private static int ShipGravityAfterHold(bool mini) => mini ? 0x3B : 0x32;
        private static int ShipGravityHoldFall(bool mini) => mini ? 0x3E : 0x34;
        private static int ShipGravity(bool mini) => 0x22; // SHIP_GRAVITY at 60fps (same for mini and normal)
        private static int ShipMaxFallSpeed(bool mini) => 0x02D7;   // SHIP_MAX_FALLSPEED at 60fps
        private static int ShipMaxFallSpeedHold(bool mini) => 0x038D; // SHIP_MAX_FALLSPEED_HOLD at 60fps

        // ── Sprite classification ──────────────────────────────────────────
        private static bool IsSpeedPortal(int sid) => SpriteIdToSpeedFixed(sid) >= 0;
        private static bool IsGameModePortal(int sid) => SpriteIdToGameMode(sid) >= 0;
        private static bool IsGravityPortal(int sid) =>
            sid == 0x08 || sid == 0x09 || sid == 0x10 || sid == 0x11 ||
            sid == 0x12 || sid == 0x13 || sid == 0xFB || sid == 0xFC;
        private static bool IsReverseGravity(int sid) =>
            sid == 0x09 || sid == 0x12 || sid == 0x13 || sid == 0xFB;
        private static bool IsMiniGrowthPortal(int sid) => sid == 0x18 || sid == 0x19;
        private static bool IsEndLevel(int sid) => sid == 0x0F;
        private static bool IsYellowPad(int sid) => sid == 0x0A || sid == 0x0C;
        private static bool IsPinkPad(int sid) => sid == 0x25 || sid == 0x26;
        private static bool IsRedPad(int sid) => sid == 0x52 || sid == 0x53;
        private static bool IsBluePad(int sid) => sid == 0x0D || sid == 0x0E || sid == 0xFD || sid == 0xFE;
        private static bool IsGreenPad(int sid) => sid == 0x65;

        // ── Hold-jump state (persists across frames in the main loop) ────
        private bool _cubeHoldJump = false; // when true, keep jumping every landing
        private int _cubeHoldDelay = 0;    // frames to wait before first jump in hold mode
        private int _committedJumpDelay = -1; // when >= 0, counting down to a committed single-jump

        // ── Backtracking state ───────────────────────────────────────────
        // When the pathfinder dies, it can rewind to a previous grounded
        // decision point and try a different strategy (toggle hold/no-hold,
        // skip the jump entirely, etc.).
        private class BacktrackCheckpoint
        {
            public SimState State;
            public int Frame;
            public bool HoldJumpState;
            public int HoldDelayState;
            public int CommittedDelayState; // _committedJumpDelay before this frame
            public int PathPointCount;  // PathPoints.Count before this frame
            public int InputCount;      // Inputs.Count before this frame
            public int RetryStage;      // 0=untried, 1=no-jump, 2=toggle-hold, 3=opposite-bias, >3=exhausted
            public double UsedBias;     // JumpTimingBias active when checkpoint was created
        }
        private List<BacktrackCheckpoint> _backtrackCheckpoints = new();
        private int _backtrackAttempts;
        private int _btOverrideFrame = -1;  // frame at which to apply override
        private int _btOverrideStage = 0;   // which alternative to try
        private bool _backtrackActive;      // true while replaying from a checkpoint (suppress new checkpoints)
        private bool _btSuppressJumpUntilAirborne; // stage-1 no-jump persists until cube falls off edge
        private int _btDeathFrame;           // frame of the death that triggered backtracking

        // ── Debug logging ───────────────────────────────────────────────────
#if !DISABLE_DEBUG_LOGGING
        private readonly string pfDebugLogPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"famidash_pf_debug_{System.DateTime.UtcNow:yyyyMMdd_HHmmss}.txt");
        private int _speculativeDepth; // >0 means we're inside lookahead — suppress logging
        private int _frameCounter;     // current frame in the main Run() loop

        private void PfLog(string msg)
        {
            if (_speculativeDepth > 0) return; // don't log during lookahead
            try
            {
                string line = $"[PF f={_frameCounter}] {msg}";
                System.IO.File.AppendAllText(pfDebugLogPath, line + System.Environment.NewLine);
            }
            catch { }
        }

        /// <summary>Path to the pathfinder debug log (in %TEMP%). Empty if logging compiled out.</summary>
        public string DebugLogPath => pfDebugLogPath;
#else
        public string DebugLogPath => string.Empty;
#endif

        // ── Input data (immutable after construction) ──────────────────────
        private readonly int[] tiles;       // SANITIZED: negatives → 0, matching simulator
        private readonly int[] sprites;
        private readonly Dictionary<int, (int anchorTileX, int anchorTileY)> spriteAnchors;
        private readonly Dictionary<int, (int offsetX, int offsetY)> spritePixelOffsets;
        private readonly int mapWidth, mapHeight;
        private readonly int groundRowsToReserve;
        private readonly int maxFallSpeed;

        // Pre-sorted sprite list for efficient processing
        private readonly List<SpriteEntry> allSprites;

#if !DISABLE_DEBUG_LOGGING
        // Death reason tracking for speculative logging
        private string _lastDeathReason = "";
        private int _lastDeathX, _lastDeathY;
#endif

        private struct SpriteEntry
        {
            public int Index;
            public int SpriteId;
            public int AnchorX_px;
            public int AnchorY_px;
            // Pre-computed world-space hitbox (matches simulator's SpriteIntersectsPlayer)
            public int HitLeft;
            public int HitTop;
            public int HitRight;   // exclusive (NES-style)
            public int HitBottom;  // exclusive (NES-style)
        }

        // ── Sprite geometry tables (mirrored from SimulatorWindow) ─────────
        private static readonly int[] sprite_widths = {
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
        private static readonly int[] sprite_heights = {
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
        private static readonly int[] sprite_x_offset = {
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
        private static readonly int[] sprite_y_offset = {
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

        // ── Simulation state ───────────────────────────────────────────────
        private struct SimState
        {
            public int X_fixed;
            public int Y_fixed;
            public int VelY_fixed;
            public int VelX_fixed;
            public int GameMode;
            public bool GravFlipped;
            public bool Mini;
            public int GravMul;                 // +1 or -1
            public bool WasZeroedByCollision;   // set by eject when landing
            public bool OnGround;               // separate from wasZeroed (cleared by jump)
            public HashSet<int> ProcessedSprites;

            public SimState Clone()
            {
                var c = this;
                c.ProcessedSprites = new HashSet<int>(ProcessedSprites);
                return c;
            }
        }

        // ── Output ─────────────────────────────────────────────────────────
        public List<(int x, int y)> PathPoints { get; private set; }
        public List<bool> Inputs { get; private set; }
        public bool Success { get; private set; }
        public string ResultMessage { get; private set; } = "";

        // ── Constructor ────────────────────────────────────────────────────
        public PathfinderEngine(
            int[] tiles,
            int[] sprites,
            Dictionary<int, (int anchorTileX, int anchorTileY)> spriteAnchors,
            int mapWidth, int mapHeight,
            bool hasGroundLayer, int groundTileRows,
            int maxFallSpeed = 0x06,
            Dictionary<int, (int offsetX, int offsetY)>? spritePixelOffsets = null)
        {
            // CRITICAL: Sanitize tiles the same way SimulatorWindow does.
            // Negative tile IDs (sentinel -1 meaning "empty") must become 0.
            this.tiles = (tiles ?? Array.Empty<int>()).Select(t => t < 0 ? 0 : t).ToArray();
            this.sprites = sprites ?? Array.Empty<int>();
            this.spriteAnchors = spriteAnchors ?? new Dictionary<int, (int, int)>();
            this.spritePixelOffsets = spritePixelOffsets ?? new Dictionary<int, (int, int)>();
            this.mapWidth = mapWidth;
            this.mapHeight = mapHeight;
            this.groundRowsToReserve = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;

            // Convert maxFallSpeed the same way the simulator does:
            // small codes (< 0x100) are shifted left 8; values >= 0x100 are used directly.
            if (maxFallSpeed >= 0x100)
                this.maxFallSpeed = maxFallSpeed;
            else
                this.maxFallSpeed = maxFallSpeed << 8;

            // Build sorted sprite list
            allSprites = new List<SpriteEntry>();
            for (int idx = 0; idx < this.sprites.Length; idx++)
            {
                int sid = this.sprites[idx];
                if (sid == -1) continue;

                // Storage position (instance's own tile) — used for hitbox placement
                // matching simulator's SpriteIntersectsPlayer which uses idx % mapWidth / idx / mapWidth
                int storageTileX = idx % mapWidth;
                int storageTileY = idx / mapWidth;

                // Geometry ID: if anchored, use anchor tile's sprite ID for geometry lookup
                // (matching simulator: id_for_geom = anchored sprite id)
                int id_for_geom = sid & 0xFF;
                int anchorKey = -1;
                if (this.spriteAnchors.TryGetValue(idx, out var anchor))
                {
                    anchorKey = anchor.anchorTileY * mapWidth + anchor.anchorTileX;
                    if (anchorKey >= 0 && anchorKey < this.sprites.Length)
                    {
                        int anchoredId = this.sprites[anchorKey];
                        if (anchoredId >= 0 && anchoredId < 256) id_for_geom = anchoredId & 0xFF;
                    }
                }

                // Look up per-sprite hitbox geometry using geometry ID
                int hw = (id_for_geom >= 0 && id_for_geom < sprite_widths.Length) ? sprite_widths[id_for_geom] : TILE;
                int hh = (id_for_geom >= 0 && id_for_geom < sprite_heights.Length) ? sprite_heights[id_for_geom] : TILE;
                int hxoff = (id_for_geom >= 0 && id_for_geom < sprite_x_offset.Length) ? sprite_x_offset[id_for_geom] : 0;
                int hyoff = (id_for_geom >= 0 && id_for_geom < sprite_y_offset.Length) ? sprite_y_offset[id_for_geom] : 0;

                // Per-position pixel offset (matching simulator's spritePixelOffsets)
                int pxOff = 0, pyOff = 0;
                if (anchorKey >= 0 && this.spritePixelOffsets.TryGetValue(anchorKey, out var aoffs))
                {
                    pxOff = aoffs.offsetX; pyOff = aoffs.offsetY;
                }
                else if (this.spritePixelOffsets.TryGetValue(idx, out var offs))
                {
                    pxOff = offs.offsetX; pyOff = offs.offsetY;
                }

                // World-space hitbox: same formula as simulator's SpriteIntersectsPlayer
                // Position based on storage tile (NOT anchor tile)
                int hitLeft = storageTileX * TILE + hxoff + pxOff;
                int hitTop = (storageTileY - groundRowsToReserve) * TILE + hyoff + pyOff;
                int hitRight = hitLeft + Math.Max(1, hw);    // exclusive (NES-style)
                int hitBottom = hitTop + Math.Max(1, hh);    // exclusive (NES-style)

                // AnchorX for sorting/skip: use anchor tile if available, else storage tile
                int anchorTileXForSort = this.spriteAnchors.TryGetValue(idx, out var anch) ? anch.anchorTileX : storageTileX;

                allSprites.Add(new SpriteEntry
                {
                    Index = idx,
                    SpriteId = sid,
                    AnchorX_px = anchorTileXForSort * TILE + TILE / 2,
                    AnchorY_px = (storageTileY - groundRowsToReserve) * TILE + TILE / 2,
                    HitLeft = hitLeft,
                    HitTop = hitTop,
                    HitRight = hitRight,
                    HitBottom = hitBottom
                });
            }

            allSprites.Sort((a, b) => a.AnchorX_px.CompareTo(b.AnchorX_px));

            PathPoints = new List<(int, int)>();
            Inputs = new List<bool>();
        }

        // ═══════════════════════════════════════════════════════════════════
        //  PUBLIC API
        // ═══════════════════════════════════════════════════════════════════

        public void Run(int startX_px, int startY_px, int startSpeedUiIndex, int startGameMode,
                        bool startGravFlipped, bool startMini)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            double originalBias = JumpTimingBias;

            RunSingleAttempt(startX_px, startY_px, startSpeedUiIndex,
                             startGameMode, startGravFlipped, startMini);

            // If the primary bias failed, retry with intermediate biases.
            // This handles cases where the level geometry forces convergence
            // and the original bias+backtracking can't find a viable path.
            if (!Success)
            {
                double[] fallbacks;
                if (originalBias >= 0.5)
                    fallbacks = new[] { 0.75, 0.5, 0.25, 0.0 };
                else
                    fallbacks = new[] { 0.25, 0.5, 0.75, 1.0 };

                foreach (double fb in fallbacks)
                {
                    if (Math.Abs(fb - originalBias) < 0.01) continue;
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[RETRY] primary bias {originalBias:F2} failed, retrying with bias {fb:F2}");
#endif
                    JumpTimingBias = fb;
                    RunSingleAttempt(startX_px, startY_px, startSpeedUiIndex,
                                     startGameMode, startGravFlipped, startMini);
                    if (Success) break;
                }
            }

            JumpTimingBias = originalBias; // restore original bias
            sw.Stop();
            double elapsedSec = sw.Elapsed.TotalSeconds;
            ResultMessage += $" [{elapsedSec:F1}s]";
#if !DISABLE_DEBUG_LOGGING
            PfLog($"[TIMING] generation took {elapsedSec:F3}s");
#endif
        }

        private void RunSingleAttempt(int startX_px, int startY_px, int startSpeedUiIndex,
                                       int startGameMode, bool startGravFlipped, bool startMini)
        {
            var state = new SimState
            {
                X_fixed = startX_px << 8,
                Y_fixed = startY_px << 8,
                VelX_fixed = SpeedUiIndexToFixed(startSpeedUiIndex),
                VelY_fixed = 0,
                GameMode = startGameMode,
                GravFlipped = startGravFlipped,
                Mini = startMini,
                GravMul = startGravFlipped ? -1 : 1,
                WasZeroedByCollision = true,
                OnGround = true,
                ProcessedSprites = new HashSet<int>()
            };

            ApplyPortalsUpTo(ref state, startX_px);

            PathPoints.Clear();
            Inputs.Clear();
            _cubeHoldJump = false;
            _cubeHoldDelay = 0;
            _committedJumpDelay = -1;
            _backtrackCheckpoints = new List<BacktrackCheckpoint>();
            _backtrackAttempts = 0;
            _btOverrideFrame = -1;
            _backtrackActive = false;
            _btSuppressJumpUntilAirborne = false;
            _btDeathFrame = 0;

#if !DISABLE_DEBUG_LOGGING
            _frameCounter = 0;
            _speculativeDepth = 0;
            PfLog($"[RUN_START] startX={startX_px} startY={startY_px} speed={startSpeedUiIndex} mode={startGameMode} gravFlipped={startGravFlipped} mini={startMini}");
            PfLog($"[RUN_STATE] X_fixed=0x{state.X_fixed:X} Y_fixed=0x{state.Y_fixed:X} VelX=0x{state.VelX_fixed:X} VelY=0x{state.VelY_fixed:X} gravMul={state.GravMul} onGround={state.OnGround}");
            PfLog($"[RUN_MAP] mapWidth={mapWidth} mapHeight={mapHeight} groundRowsToReserve={groundRowsToReserve} maxFallSpeed=0x{maxFallSpeed:X} sprites={allSprites.Count}");
            foreach (var sp in allSprites)
            {
                int sid = sp.SpriteId;
                if (IsGravityPortal(sid) || IsGameModePortal(sid) || IsEndLevel(sid))
                {
                    PfLog($"[PORTAL_INV] sid=0x{sid:X2} idx={sp.Index} anchorX={sp.AnchorX_px} box=({sp.HitLeft},{sp.HitTop})-({sp.HitRight},{sp.HitBottom})");
                }
            }
#endif

            for (int frame = 0; frame < MAX_FRAMES; frame++)
            {
#if !DISABLE_DEBUG_LOGGING
                _frameCounter = frame;
#endif
                // Record path at hitbox center (matching simulator's recordedPlayerPath)
                int hbW = GetHitboxW(state.Mini);
                int hbH = GetHitboxH(state.Mini);
                int hbOffY = GetHitboxOffsetY(state.Mini, state.GravFlipped);
                PathPoints.Add(((state.X_fixed >> 8) + hbW / 2,
                                (state.Y_fixed >> 8) + hbH / 2 + hbOffY));

#if !DISABLE_DEBUG_LOGGING
                PfLog($"[STEP_START] X_fixed=0x{state.X_fixed:X} ({state.X_fixed >> 8}px) Y_fixed=0x{state.Y_fixed:X} ({state.Y_fixed >> 8}px) VelY=0x{state.VelY_fixed:X} mode={state.GameMode} gravFlipped={state.GravFlipped} gravMul={state.GravMul} onGround={state.OnGround} wasZeroed={state.WasZeroedByCollision} mini={state.Mini}");
#endif

                // Snapshot pre-decision state for potential checkpoint
                bool wasGrounded = state.VelY_fixed == 0 && state.OnGround;
                bool preHoldJump = _cubeHoldJump;
                int preHoldDelay = _cubeHoldDelay;
                int preCommittedDelay = _committedJumpDelay;
                int prePathCount = PathPoints.Count;
                int preInputCount = Inputs.Count;
                SimState preDecisionState = (wasGrounded || _cubeHoldJump) ? state.Clone() : default;
                bool isBacktrackFrame = (_btOverrideFrame == frame);

                bool input = DecideInput(state);
                Inputs.Add(input);

                // Once the replay path advances past the frame where the
                // original death occurred, the backtrack has "succeeded"
                // and we can resume creating new checkpoints for future
                // obstacles.
                if (_backtrackActive && frame > _btDeathFrame)
                {
                    _backtrackActive = false;
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[BACKTRACK_DONE] advanced past death frame {_btDeathFrame}, resuming normal checkpointing");
#endif
                }

                // Save checkpoint at grounded decisions where the cube jumps
                // OR where a committed delay is set (these are also key
                // decision points — choosing delay=17 vs delay=0 matters).
                // Don't create new checkpoints during backtrack replays — the
                // original checkpoints are the only ones we want to revisit.
                // Also save periodic checkpoints during hold-jump mode,
                // because hold-jump keeps the cube permanently airborne
                // (lands and immediately jumps in one frame), so the normal
                // wasGrounded && input condition never fires on stair landings.
                // Save roughly one checkpoint per jump arc (~24 frames).
                bool shouldCheckpoint = wasGrounded && (input || _committedJumpDelay > 0);
                if (!shouldCheckpoint && preHoldJump && input && !wasGrounded
                    && frame % 24 == 0)
                {
                    shouldCheckpoint = true;
                }
                if (shouldCheckpoint && !isBacktrackFrame && !_backtrackActive)
                {
                    if (_backtrackCheckpoints.Count >= MAX_CHECKPOINT_DEPTH)
                        _backtrackCheckpoints.RemoveAt(0);
                    // Use pre-decision state if captured, otherwise snapshot now
                    var cpState = preDecisionState.ProcessedSprites != null
                        ? preDecisionState : state.Clone();
                    _backtrackCheckpoints.Add(new BacktrackCheckpoint
                    {
                        State = cpState,
                        Frame = frame,
                        HoldJumpState = preHoldJump,
                        HoldDelayState = preHoldDelay,
                        CommittedDelayState = preCommittedDelay,
                        PathPointCount = prePathCount,
                        InputCount = preInputCount,
                        RetryStage = 0,
                        UsedBias = JumpTimingBias
                    });
                }

#if !DISABLE_DEBUG_LOGGING
                PfLog($"[DECIDE] input={input}");
#endif

                bool alive = StepFrame(ref state, input, out bool endLevel);
                if (!alive)
                {
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[DEATH] frame={frame} X={state.X_fixed >> 8}px Y={state.Y_fixed >> 8}px VelY=0x{state.VelY_fixed:X} gravFlipped={state.GravFlipped}");
#endif
                    // Try backtracking to a previous decision point
                    if (TryBacktrack(ref state, ref frame))
                        continue;

                    // All backtracks exhausted — die permanently
                    Success = false;
                    ResultMessage = $"Died at frame {frame}, X={state.X_fixed >> 8}px";
                    return;
                }
                if (endLevel)
                {
                    hbW = GetHitboxW(state.Mini);
                    hbH = GetHitboxH(state.Mini);
                    hbOffY = GetHitboxOffsetY(state.Mini, state.GravFlipped);
                    PathPoints.Add(((state.X_fixed >> 8) + hbW / 2,
                                    (state.Y_fixed >> 8) + hbH / 2 + hbOffY));
                    Success = true;
                    ResultMessage = $"Completed in {frame} frames ({PathPoints.Count} path points)";
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[END_LEVEL] frame={frame}");
#endif
                    return;
                }
            }

            Success = false;
            ResultMessage = $"Timeout after {MAX_FRAMES} frames";
#if !DISABLE_DEBUG_LOGGING
            PfLog($"[TIMEOUT]");
#endif
        }

        // ═══════════════════════════════════════════════════════════════════
        //  BACKTRACKING
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// On death, rewind to the most recent checkpoint that still has
        /// untried alternatives.  Each checkpoint offers up to 3 alternatives:
        ///   Stage 1 — "no jump": skip the jump entirely, suppress until airborne.
        ///   Stage 2 — "toggle hold": flip hold-jump on/off.
        ///   Stage 3 — "opposite bias": retry with the opposite timing bias
        ///             (earliest→latest or vice versa).
        /// Returns true if a checkpoint was restored (caller should continue
        /// the frame loop); false if all backtracks are exhausted.
        /// </summary>
        private bool TryBacktrack(ref SimState state, ref int frame)
        {
            if (!_backtrackActive)
            {
                // First backtrack from this death — record the death frame
                _btDeathFrame = frame;
            }
            _backtrackActive = true;

            while (_backtrackCheckpoints.Count > 0 &&
                   _backtrackAttempts < MAX_BACKTRACK_ATTEMPTS)
            {
                int last = _backtrackCheckpoints.Count - 1;
                var cp = _backtrackCheckpoints[last];
                cp.RetryStage++;

                if (cp.RetryStage > 3) // exhausted all alternatives
                {
                    _backtrackCheckpoints.RemoveAt(last);
                    continue; // try the previous checkpoint
                }

                // Restore state to before the decision at this checkpoint
                state = cp.State.Clone();
                _cubeHoldJump = cp.HoldJumpState;
                _cubeHoldDelay = cp.HoldDelayState;
                _committedJumpDelay = cp.CommittedDelayState; // restore committed delay state
                JumpTimingBias = cp.UsedBias; // restore bias so stage-3 flip doesn't permanently mutate it

                // Truncate outputs to before this frame
                if (PathPoints.Count > cp.PathPointCount)
                    PathPoints.RemoveRange(cp.PathPointCount,
                                           PathPoints.Count - cp.PathPointCount);
                if (Inputs.Count > cp.InputCount)
                    Inputs.RemoveRange(cp.InputCount,
                                       Inputs.Count - cp.InputCount);

                // Remove all checkpoints after the restored one — they
                // were created in the path that just died and will be
                // re-created (or not) in the new path.
                while (_backtrackCheckpoints.Count > last + 1)
                    _backtrackCheckpoints.RemoveAt(_backtrackCheckpoints.Count - 1);

                // Tell DecideInput to use the alternative at this frame
                _btOverrideFrame = cp.Frame;
                _btOverrideStage = cp.RetryStage;
                _btSuppressJumpUntilAirborne = false; // clear any prior suppression

                // Rewind: for-loop will increment to cp.Frame
                frame = cp.Frame - 1;
                _backtrackAttempts++;

#if !DISABLE_DEBUG_LOGGING
                _frameCounter = cp.Frame;
                PfLog($"[BACKTRACK] attempt={_backtrackAttempts}/{MAX_BACKTRACK_ATTEMPTS} " +
                      $"rewind to frame={cp.Frame} stage={cp.RetryStage} " +
                      $"remaining_checkpoints={_backtrackCheckpoints.Count} " +
                      $"holdWas={cp.HoldJumpState}");
#endif
                return true;
            }

            _backtrackActive = false;
            return false;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  DECISION LOGIC
        // ═══════════════════════════════════════════════════════════════════

        private bool DecideInput(SimState state)
        {
            if (state.GameMode == 1) return DecideShipInput(state);
            if (state.GameMode != 0) return false; // only cube/ship for now

            // ── Stage-2 "no-jump" suppression: persist until cube is airborne
            //    (walked off edge and is now falling). ──
            if (_btSuppressJumpUntilAirborne)
            {
                if (state.VelY_fixed != 0)
                {
                    _btSuppressJumpUntilAirborne = false; // cube is airborne, resume normal
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[BACKTRACK_OVERRIDE] no-jump suppression cleared (now airborne)");
#endif
                }
                else
                {
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[BACKTRACK_OVERRIDE] no-jump suppression active (grounded)");
#endif
                    return false; // don't jump — wait to fall off edge
                }
            }

            // ── Backtrack override: when rewinding to a checkpoint, force
            //    a different decision than the one that led to death. ──
#if !DISABLE_DEBUG_LOGGING
            if (_btOverrideFrame == _frameCounter && _speculativeDepth == 0)
#else
            if (_btOverrideFrame == _frameCounter)
#endif
            {
                int stage = _btOverrideStage;
                _btOverrideFrame = -1; // consume override

                if (stage == 1) // No jump: skip this decision, suppress until airborne
                {
                    _cubeHoldJump = false;
                    _cubeHoldDelay = 0;
                    _btSuppressJumpUntilAirborne = true;
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[BACKTRACK_OVERRIDE] stage=1: forcing no-jump (suppress until airborne)");
#endif
                    return false;
                }
                else if (stage == 2) // Toggle hold: flip hold-jump on/off
                {
                    if (_cubeHoldJump)
                    {
                        _cubeHoldJump = false;
                        _cubeHoldDelay = 0;
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[BACKTRACK_OVERRIDE] stage=2: released hold, falling through to normal logic");
#endif
                    }
                    else
                    {
                        _cubeHoldJump = true;
                        _cubeHoldDelay = 0;
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[BACKTRACK_OVERRIDE] stage=2: forcing hold-jump on");
#endif
                        return true;
                    }
                }
                else if (stage == 3) // Opposite bias: flip timing
                {
                    // If bias was < 0.5, try 1.0 (latest); if >= 0.5, try 0.0 (earliest)
                    double newBias = JumpTimingBias < 0.5 ? 1.0 : 0.0;
                    JumpTimingBias = newBias;
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[BACKTRACK_OVERRIDE] stage=3: switching to opposite bias {newBias:F2}");
#endif
                    // Fall through to normal decision logic with new bias
                }
            }

            // Can only jump when velY == 0 (matching real game's jump check)
            if (state.VelY_fixed != 0)
            {
                if (!_cubeHoldJump)
                {
                    // Airborne: if we have a committed delay, it's being consumed
                    // by not being grounded — the delay assumes grounded frames.
                    return false;
                }

                // In hold-jump mode while airborne: re-evaluate whether
                // releasing hold actually gives a BETTER outcome.  On stair
                // sections both paths may die at the same time because the
                // speculative horizon is finite — releasing there is pointless
                // and causes the cube to drift toward edges.  Only release
                // when the release path strictly out-survives the hold path.
                int holdSurv = SimulateForwardWithJumpAt(state, 0, holdAfterLanding: true);
                if (holdSurv >= LOOKAHEAD_HORIZON)
                {
                    // Hold path survives the full horizon — definitely keep
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[DECIDE_CUBE] hold-jump continuing (airborne): holdSurv={holdSurv}");
#endif
                    return true;
                }
                // Hold path dies within horizon — does releasing do better?
                int releaseSurv = SimulateForwardWithJumpAt(state, -1);
                if (releaseSurv > holdSurv)
                {
                    // Release genuinely survives longer — abandon hold
                    _cubeHoldJump = false;
                    _cubeHoldDelay = 0;
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[DECIDE_CUBE] hold-jump released (airborne): holdSurv={holdSurv}, releaseSurv={releaseSurv} (release better)");
#endif
                    return false;
                }
                // Release is no better (or worse) — keep holding; the
                // grounded re-evaluation will get another chance when we land.
#if !DISABLE_DEBUG_LOGGING
                PfLog($"[DECIDE_CUBE] hold-jump kept (airborne): holdSurv={holdSurv}, releaseSurv={releaseSurv} (release not better)");
#endif
                return true;
            }

            // If we're in hold-jump mode from a previous decision, jump immediately
            if (_cubeHoldJump && state.OnGround)
            {
                // If we had a countdown delay, decrement and wait
                if (_cubeHoldDelay > 0)
                {
                    _cubeHoldDelay--;
                    return false;
                }
                // Re-evaluate: does continuing to hold still survive?
                int holdSurvival = SimulateForwardWithJumpAt(state, 0, holdAfterLanding: true);
                // Also check: is NOT jumping safe?  If no-press survives
                // beyond the detection horizon, there's no immediate danger
                // and we should exit hold-jump to let normal logic decide.
                const int HOLD_EXIT_HORIZON = 20;
                int noPressHold = SimulateForwardWithJumpAt(state, -1);
                if (holdSurvival >= LOOKAHEAD_HORIZON && noPressHold < HOLD_EXIT_HORIZON)
                {
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[DECIDE_CUBE] hold-jump continuing: holdSurv={holdSurvival} noPressSurv={noPressHold}");
#endif
                    return true;
                }
                // Hold mode no longer beneficial (hold dies, OR no-press is
                // safe enough that we don't need to chain-jump) — exit and
                // fall through to normal decision logic
                _cubeHoldJump = false;
                _cubeHoldDelay = 0;
#if !DISABLE_DEBUG_LOGGING
                PfLog($"[DECIDE_CUBE] hold-jump released (grounded): holdSurv={holdSurvival} noPressSurv={noPressHold}");
#endif
            }

            // Committed jump delay: count down, fire when reaching 0.
            // Merging decrement+fire so that delay=N fires after exactly N
            // frames of waiting, matching SimulateForwardWithJumpAt(jumpFrame=N).
            if (_committedJumpDelay > 0)
            {
                _committedJumpDelay--;
                if (_committedJumpDelay == 0)
                {
                    _committedJumpDelay = -1; // consumed
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[DECIDE_CUBE] committed delay fired — jumping now");
#endif
                    return true;
                }
#if !DISABLE_DEBUG_LOGGING
                PfLog($"[DECIDE_CUBE] committed delay countdown: {_committedJumpDelay} remaining");
#endif
                return false;
            }

#if !DISABLE_DEBUG_LOGGING
            // Guard against deep recursion from chained jump evaluation
            if (_speculativeDepth >= MAX_SPECULATIVE_DEPTH) return false;
#endif

            // How long does the cube survive without ever jumping?
#if !DISABLE_DEBUG_LOGGING
            _speculativeDepth++;
            int noPressFrames = SimulateForwardWithJumpAt(state, -1, out string npDeath, out int npDX, out int npDY);
            _speculativeDepth--;
#else
            int noPressFrames = SimulateForwardWithJumpAt(state, -1);
#endif

            // ── Danger detection: only evaluate jumps when no-press
            //    dies within the lookahead horizon.  The bias does NOT
            //    change when we detect danger — it only changes which
            //    delay we PICK once we decide a jump is needed. ──
            const int DETECTION_HORIZON = 30; // evaluate jumps when obstacle within 30 frames
                                                // (~27 frame jump arc means after landing, only 3 frames left — no double-jump)

            // Always test jumps if a gravity portal is within the lookahead X range.
            // The portal bonus makes portal-hitting paths score higher, but only if
            // we actually evaluate them. Without this, the detection horizon skips
            // jump evaluation entirely, and portal-bonus paths are never discovered.
            bool gravPortalAhead = false;
            {
                int currentX = state.X_fixed >> 8;
                int lookaheadX = currentX + ((state.VelX_fixed >> 8) + 1) * LOOKAHEAD_HORIZON;
                foreach (var sp in allSprites)
                {
                    if (sp.AnchorX_px < currentX) continue;
                    if (sp.AnchorX_px > lookaheadX) break; // sorted by X
                    if (IsGravityPortal(sp.SpriteId) && !state.ProcessedSprites.Contains(sp.Index))
                    {
                        gravPortalAhead = true;
                        break;
                    }
                }
            }

            if (noPressFrames >= DETECTION_HORIZON && !gravPortalAhead)
            {
#if !DISABLE_DEBUG_LOGGING
                PfLog($"[DECIDE_CUBE] noPressFrames={noPressFrames} >= DETECTION_HORIZON={DETECTION_HORIZON}, no danger (noPressDeath={npDeath}@X={npDX},Y={npDY})");
#endif
                return false;
            }

#if !DISABLE_DEBUG_LOGGING
            PfLog($"[DECIDE_CUBE] noPressFrames={noPressFrames} < DETECTION_HORIZON={DETECTION_HORIZON}{(gravPortalAhead ? " (gravPortalAhead)" : "")}, testing jumps (noPressDeath={npDeath}@X={npDX},Y={npDY})");
#endif

            // Test different jump timings: jump at delay 0 (now), 1, 2, ...
            int bestSurvival = noPressFrames;
            var viableDelays = new List<(int delay, int survival)>();

            int maxDelay = Math.Min(noPressFrames, 35);
#if !DISABLE_DEBUG_LOGGING
            _speculativeDepth++;
            var specDeathDetails = new List<string>();
            // If reversed gravity and first speculative test, log the full trajectory
            bool logSpecTrajectory = state.GravFlipped;
#endif
            for (int delay = 0; delay < maxDelay; delay++)
            {
#if !DISABLE_DEBUG_LOGGING
                int survival = SimulateForwardWithJumpAt(state, delay, out string dr, out int dx, out int dy,
                    logSpecTrajectory && delay == 0);
                if (delay < 5 && survival < LOOKAHEAD_HORIZON)
                    specDeathDetails.Add($"d{delay}:s{survival}@{dr}(X={dx},Y={dy})");
#else
                int survival = SimulateForwardWithJumpAt(state, delay);
#endif
                if (survival > bestSurvival)
                    bestSurvival = survival;
                if (survival > noPressFrames)
                    viableDelays.Add((delay, survival));
            }
#if !DISABLE_DEBUG_LOGGING
            _speculativeDepth--;
#endif

            if (viableDelays.Count == 0)
            {
#if !DISABLE_DEBUG_LOGGING
                PfLog($"[DECIDE_CUBE] no viable delays found, no jump. bestSurvival={bestSurvival} details=[{string.Join(", ", specDeathDetails)}]");
                // Dump tile area when reversed gravity has no viable path
                if (state.GravFlipped)
                {
                    int startTileX = (state.X_fixed >> 8) / TILE;
                    int endTileX = startTileX + 10;
                    var sb = new System.Text.StringBuilder();
                    sb.Append($"[TILE_AREA] X_tiles={startTileX}-{endTileX} ");
                    for (int ay = 10; ay < mapHeight; ay++)
                    {
                        sb.Append($"row{ay}:");
                        for (int ax = startTileX; ax <= endTileX && ax < mapWidth; ax++)
                        {
                            int idx = ay * mapWidth + ax;
                            if (idx >= 0 && idx < tiles.Length)
                            {
                                int tid = tiles[idx];
                                if (tid != 0)
                                    sb.Append($"[{ax},{ay}]=0x{tid:X2}({MetatileCollisionTable.GetCollision((byte)tid)}) ");
                            }
                        }
                    }
                    PfLog(sb.ToString());
                }
#endif
                return false;
            }

            // ── Also test hold-jump variants ──
            // For each viable delay, test what happens if we hold jump
            // (always re-jump on landing) instead of re-evaluating each frame.
            // This can find paths that require continuous jumping without gaps.
            int holdBestSurvival = 0;
            int holdBestDelay = 0;
            {
                // Test hold for a few key delays (0, and a few others)
                int holdMaxDelay = Math.Min(maxDelay, 10);
#if !DISABLE_DEBUG_LOGGING
                _speculativeDepth++;
#endif
                for (int delay = 0; delay < holdMaxDelay; delay++)
                {
                    int holdSurv = SimulateForwardWithJumpAt(state, delay, holdAfterLanding: true);
                    if (holdSurv > holdBestSurvival)
                    {
                        holdBestSurvival = holdSurv;
                        holdBestDelay = delay;
                    }
                }
#if !DISABLE_DEBUG_LOGGING
                _speculativeDepth--;
#endif
            }

            int threshold = bestSurvival - 2;
            var bestDelays = viableDelays.Where(d => d.survival >= threshold).ToList();
            if (bestDelays.Count == 0) bestDelays = viableDelays;

            // When all best delays survive the full lookahead horizon,
            // the jump isn't urgent — any timing clears the obstacle.
            // Remove premature delays to avoid double-jump patterns where
            // jumping too early forces another jump right after landing.
            if (bestSurvival >= LOOKAHEAD_HORIZON && bestDelays.Count >= 2)
            {
                // 1) Remove d0: jumping immediately is always premature
                //    when all timings equally survive the full horizon.
                if (bestDelays[0].delay == 0)
                    bestDelays.RemoveAt(0);

                // 2) Remove isolated early outliers before a large gap.
                //    E.g. best=[d1, d16..d28]: d1 clears the spike but
                //    lands right before the next one (double-jump), while
                //    d16+ positions the arc to clear both obstacles.
                //    A gap > 5 between consecutive best delays signals
                //    two separate obstacle-clearing patterns.
                if (bestDelays.Count >= 2)
                {
                    for (int gi = 1; gi < bestDelays.Count; gi++)
                    {
                        if (bestDelays[gi].delay - bestDelays[gi - 1].delay > 5)
                        {
                            bestDelays.RemoveRange(0, gi);
                            break;
                        }
                    }
                }
            }

            // Use JumpTimingBias to pick from the best delays:
            // 0.0 = earliest (index 0), 0.5 = middle, 1.0 = latest (last index)
            int pickIndex = (int)(JumpTimingBias * (bestDelays.Count - 1));
            pickIndex = Math.Clamp(pickIndex, 0, bestDelays.Count - 1);

            int pickedDelay = bestDelays[pickIndex].delay;

            // Only enter hold-jump when single-jump can't survive the
            // full horizon but hold-jump can.  This prevents hold-jump from
            // activating on flat ground where single jumps already work.
            if (holdBestSurvival > bestSurvival && bestSurvival < LOOKAHEAD_HORIZON)
            {
#if !DISABLE_DEBUG_LOGGING
                PfLog($"[DECIDE_CUBE] HOLD-JUMP wins: holdSurv={holdBestSurvival} > bestSingleSurv={bestSurvival}, delay={holdBestDelay}");
#endif
                _cubeHoldJump = true;
                _cubeHoldDelay = holdBestDelay;
                return holdBestDelay == 0;
            }

#if !DISABLE_DEBUG_LOGGING
            PfLog($"[DECIDE_CUBE] viable={viableDelays.Count} best=[{string.Join(",", bestDelays.Select(d => $"d{d.delay}:s{d.survival}"))}] bestSurvival={bestSurvival} bias={JumpTimingBias:F2} pickIdx={pickIndex} picking delay={pickedDelay}{(holdBestSurvival > 0 ? $" (holdBest=d{holdBestDelay}:s{holdBestSurvival})" : "")}");
#endif

            // Commit to the picked delay: if delay > 0, store it and
            // return false until the countdown reaches 0.  This prevents
            // re-evaluation from eroding the "latest" timing back toward
            // delay=0 on subsequent frames.
            if (pickedDelay > 0)
            {
                _committedJumpDelay = pickedDelay;
                return false;
            }

            return true;  // delay == 0 → jump now
        }

        /// <summary>
        /// Ship decision: track the corridor center using simple proportional control.
        /// Hold (thrust up in normal gravity) if below the target, release if above.
        /// Uses 1-frame safety check: if the chosen action causes death, flip.
        /// This replaces the previous greedy lookahead which had subtle bugs causing
        /// the ship to stay at ground level and never climb.
        /// </summary>
        private bool DecideShipInput(SimState state)
        {
            // Detect corridor center at the ship's current position
            // (scans ahead CORRIDOR_LOOK_AHEAD_TILES for obstacles)
            int targetY = FindCorridorCenter(ref state);
            int currentY = state.Y_fixed >> 8;

            // Simple proportional control: fly toward corridor center
            // Normal gravity (GravMul>0): hold thrusts UP (negative Y)
            // Reversed gravity (GravMul<0): hold thrusts DOWN (positive Y)
            bool shouldHold = (state.GravMul > 0) ? (currentY > targetY) : (currentY < targetY);

            // Test 1-frame survival for both options
            var sH = state.Clone();
            var sR = state.Clone();
            bool aliveH = StepFrame(ref sH, true, out bool endH);
            bool aliveR = StepFrame(ref sR, false, out bool endR);

            // If one path reaches end-of-level, take it
            if (endH) return true;
            if (endR) return false;

            // If only one survives, pick it regardless of corridor target
            if (aliveH && !aliveR) return true;
            if (!aliveH && aliveR) return false;
            if (!aliveH && !aliveR) return false; // both die, doesn't matter

            // Both survive: if our chosen action (corridor tracking) survives, use it
            // Otherwise flip
            if (shouldHold && !aliveH) return false;
            if (!shouldHold && !aliveR) return true;
            return shouldHold;
        }

        /// <summary>
        /// Scan vertically from the ship's center to find the nearest solid ceiling above
        /// and nearest solid floor below. Also scans AHEAD horizontally (CORRIDOR_LOOK_AHEAD_TILES)
        /// to detect upcoming obstacles and proactively adjust the corridor target.
        /// Uses the tightest ceiling/floor constraint across all scanned columns, so the ship
        /// steers early to clear walls, blocks, and narrow passages ahead.
        /// </summary>
        private int FindCorridorCenter(ref SimState s)
        {
            int playerX_px = s.X_fixed >> 8;
            int playerY_px = s.Y_fixed >> 8;
            int hbW = GetHitboxW(s.Mini);
            int hbH = GetHitboxH(s.Mini);
            int centerX_px = playerX_px + hbW / 2;
            int centerY_tile = (playerY_px + hbH / 2) / TILE;

            int worldBottom = (mapHeight - groundRowsToReserve) * TILE;

            // Start with full world bounds
            int ceilingY = 0;       // default: top of world
            int floorY = worldBottom; // default: ground layer

            // Compute all tile rows that the ship's hitbox currently spans
            int topTile = playerY_px / TILE;
            int botTile = (playerY_px + hbH - 1) / TILE;

            // Scan vertically at current X AND ahead (CORRIDOR_LOOK_AHEAD_TILES columns)
            int startTileX = centerX_px / TILE;
            int endTileX = Math.Min(startTileX + CORRIDOR_LOOK_AHEAD_TILES, mapWidth - 1);

            for (int tx = startTileX; tx <= endTileX; tx++)
            {
                // Scan upward for ceiling at this column (from above the hitbox top)
                for (int ty = topTile - 1; ty >= 0; ty--)
                {
                    var col = GetTileCollision(tx, ty);
                    if (col != MetatileCollision.COL_NONE)
                    {
                        var (cL, cT, cR, cB) = GetCollisionBounds(col);
                        if (cR > cL)
                        {
                            int thisCeiling = ty * TILE + cB;
                            if (thisCeiling > ceilingY) ceilingY = thisCeiling;
                            break;
                        }
                    }
                }

                // Scan downward for floor at this column (from below the hitbox bottom)
                for (int ty = botTile + 1; ty < mapHeight - groundRowsToReserve; ty++)
                {
                    var col = GetTileCollision(tx, ty);
                    if (col != MetatileCollision.COL_NONE)
                    {
                        var (cL, cT, cR, cB) = GetCollisionBounds(col);
                        if (cR > cL)
                        {
                            int thisFloor = ty * TILE + cT;
                            if (thisFloor < floorY) floorY = thisFloor;
                            break;
                        }
                    }
                }

                // For forward columns: check ALL tile rows the hitbox spans for obstacles
                // This catches walls at any row the ship currently occupies
                if (tx > startTileX)
                {
                    for (int ty = topTile; ty <= botTile; ty++)
                    {
                        var col = GetTileCollision(tx, ty);
                        if (col != MetatileCollision.COL_NONE)
                        {
                            var (cL, cT, cR, cB) = GetCollisionBounds(col);
                            if (cR > cL) // solid block in the ship's path
                            {
                                int blockTop = ty * TILE + cT;
                                int blockBottom = ty * TILE + cB;

                                // Determine if the ship should go above or below this obstacle.
                                // Check available space above vs below the block.
                                int spaceAbove = blockTop - ceilingY;
                                int spaceBelow = floorY - blockBottom;

                                if (spaceAbove >= hbH && (spaceAbove >= spaceBelow || spaceBelow < hbH))
                                {
                                    // Fly over: treat block top as floor
                                    if (blockTop < floorY) floorY = blockTop;
                                }
                                else
                                {
                                    // Fly under: treat block bottom as ceiling
                                    if (blockBottom > ceilingY) ceilingY = blockBottom;
                                }
                            }
                        }
                    }

                    // Also check 1 tile above and below the hitbox span for obstacles
                    // that the ship would encounter if it adjusts vertically
                    int[] marginTiles = { topTile - 1, botTile + 1 };
                    foreach (int ty in marginTiles)
                    {
                        if (ty < 0 || ty >= mapHeight - groundRowsToReserve) continue;
                        if (ty >= topTile && ty <= botTile) continue; // already checked in main scan
                        var col = GetTileCollision(tx, ty);
                        if (col != MetatileCollision.COL_NONE)
                        {
                            var (cL, cT, cR, cB) = GetCollisionBounds(col);
                            if (cR > cL)
                            {
                                int blockTop = ty * TILE + cT;
                                int blockBottom = ty * TILE + cB;
                                // If block is above → tighten ceiling
                                if (ty < topTile && blockBottom > ceilingY) ceilingY = blockBottom;
                                // If block is below → tighten floor
                                if (ty > botTile && blockTop < floorY) floorY = blockTop;
                            }
                        }
                    }
                }
            }

            // Target: center of tightest corridor, offset so top-left Y puts center at midpoint
            return (ceilingY + floorY) / 2 - hbH / 2;
        }

        /// <summary>
        /// Greedy lookahead for ship: each frame, pick hold vs release based
        /// on which one-step result survives longer in a secondary lookahead.
        /// Returns number of frames survived (up to horizon).
        /// </summary>
        private int GreedyShipLookahead(SimState start, int horizon)
        {
            var s = start.Clone();

            for (int f = 0; f < horizon; f++)
            {
                // Detect corridor center at current position for tiebreaking
                int targetY = FindCorridorCenter(ref s);

                // Quick 1-step test for each option
                var sH = s.Clone();
                var sR = s.Clone();
                bool aliveH = StepFrame(ref sH, true, out bool endH);
                bool aliveR = StepFrame(ref sR, false, out bool endR);

                if (!aliveH && !aliveR) return f; // both die
                if (endH) return horizon; // hold reaches end
                if (endR) return horizon; // release reaches end

                bool pickHold;
                if (aliveH && !aliveR) pickHold = true;
                else if (!aliveH && aliveR) pickHold = false;
                else
                {
                    // Both survive — pick the one closer to corridor center
                    int hDist = Math.Abs((sH.Y_fixed >> 8) - targetY);
                    int rDist = Math.Abs((sR.Y_fixed >> 8) - targetY);
                    pickHold = hDist <= rDist;
                }

                // Advance with picked input
                bool alive = StepFrame(ref s, pickHold, out bool end);
                if (!alive) return f;
                if (end) return horizon;
            }
            return horizon;
        }

        private (int survival, int finalY) SimulateShipForward(SimState state, bool hold)
        {
            var s = state.Clone();
            for (int f = 0; f < LOOKAHEAD_HORIZON; f++)
            {
                bool alive = StepFrame(ref s, hold, out bool endLevel);
                if (!alive) return (f, s.Y_fixed);
                if (endLevel) return (LOOKAHEAD_HORIZON, s.Y_fixed);
            }
            return (LOOKAHEAD_HORIZON, s.Y_fixed);
        }

        /// <summary>
        /// Simulate forward, pressing jump on exactly one frame (jumpFrame).
        /// Pass jumpFrame = -1 to never jump (pure no-input simulation).
        /// After the initial jump lands, auto-jumps when danger is detected
        /// ahead (short lookahead) to evaluate multi-jump paths.
        /// </summary>
        private int SimulateForwardWithJumpAt(SimState state, int jumpFrame,
            bool holdAfterLanding = false)
        {
            return SimulateForwardWithJumpAt(state, jumpFrame, out _, out _, out _,
                false, holdAfterLanding);
        }

        private int SimulateForwardWithJumpAt(SimState state, int jumpFrame,
            out string deathReason, out int deathX, out int deathY,
            bool logTrajectory = false, bool holdAfterLanding = false)
        {
            deathReason = ""; deathX = 0; deathY = 0;
            var s = state.Clone();
            bool initialJumpDone = (jumpFrame < 0);
            bool chainJumps = (jumpFrame >= 0); // only chain jumps when an actual jump was requested
            bool startGravFlipped = s.GravFlipped; // track gravity portal hits
#if !DISABLE_DEBUG_LOGGING
            var trajLog = logTrajectory ? new System.Text.StringBuilder() : null;
#endif

            for (int f = 0; f < LOOKAHEAD_HORIZON; f++)
            {
                bool input = false;

                if (!initialJumpDone)
                {
                    // First jump at or after the specified delay frame.
                    // Using >= instead of == so that if the cube is airborne
                    // at the delay frame (e.g. jumpFrame=0 but cube is mid-air),
                    // the jump fires on the first subsequent landing (VelY==0)
                    // rather than being silently skipped forever.
                    input = (f >= jumpFrame && s.VelY_fixed == 0);
                    if (input) initialJumpDone = true;
                }
                else if (chainJumps && s.GameMode == 0 && s.VelY_fixed == 0 && s.OnGround)
                {
                    // After the initial jump has landed:
                    // holdAfterLanding = always re-jump (simulates holding the button)
                    // otherwise = check short-horizon danger to decide
                    input = holdAfterLanding || QuickDangerCheck(s);
                }

#if !DISABLE_DEBUG_LOGGING
                if (trajLog != null)
                    trajLog.Append($"f{f}:X={s.X_fixed >> 8},Y={s.Y_fixed >> 8},V=0x{s.VelY_fixed:X},G={s.OnGround},gf={s.GravFlipped},i={input} ");
#endif

                bool alive = StepFrame(ref s, input, out bool endLevel);
                if (!alive)
                {
#if !DISABLE_DEBUG_LOGGING
                    deathReason = _lastDeathReason;
                    deathX = _lastDeathX;
                    deathY = _lastDeathY;
                    if (trajLog != null)
                    {
                        trajLog.Append($"DEAD:X={s.X_fixed >> 8},Y={s.Y_fixed >> 8}");
                        PfLog($"[SPEC_TRAJ] {trajLog}");
                    }
#endif
                    // Bonus for paths that went through a gravity portal:
                    // these paths are structurally important for level progression
                    int portalBonus = (s.GravFlipped != startGravFlipped) ? LOOKAHEAD_HORIZON : 0;
                    return f + portalBonus;
                }
                if (endLevel) return LOOKAHEAD_HORIZON;
            }

            // Bonus for paths that went through a gravity portal
            int finalPortalBonus = (s.GravFlipped != startGravFlipped) ? LOOKAHEAD_HORIZON : 0;
            return LOOKAHEAD_HORIZON + finalPortalBonus;
        }

        /// <summary>
        /// Lightweight danger detection: simulate a few frames without jumping.
        /// Returns true if the cube dies within a short horizon, meaning it
        /// should jump now to survive.
        /// </summary>
        private bool QuickDangerCheck(SimState state)
        {
            var s = state.Clone();
            const int SHORT_HORIZON = 15;
            for (int f = 0; f < SHORT_HORIZON; f++)
            {
                bool alive = StepFrame(ref s, false, out _);
                if (!alive) return true; // danger ahead, jump now
            }
            return false;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  FRAME SIMULATION — matches SimulateNumericStep + CubePhysics_Fresh
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Simulate one frame with EXACT ordering matching the real simulator:
        /// Matching SimulatorWindow.SimulateNumericStep order:
        ///   1. sprite_collide()        — portals, pads at current X
        ///   2. x_movement()            — X advance (for ground support only)
        ///   3. (ground support check)  — verify floor at new X
        ///   4. REVERT to OLD X
        ///   5. movement()              — Y physics + eject at OLD X
        ///   6. x_movement_coll()       — forward wall check at OLD X, post-eject Y
        ///   7. RESTORE NEW X
        ///   8. bg_coll_death()         — death check at new X
        /// </summary>
        private bool StepFrame(ref SimState s, bool input, out bool endLevel)
        {
            endLevel = false;

            int oldX_fixed = s.X_fixed;
            int oldX_px = oldX_fixed >> 8;

            // ── STEP 1: PROCESS SPRITES at current X (sprite_collide) ──
            // Must use current X, not future X — matches simulator's sprite_collide
            endLevel = ProcessSprites(ref s, oldX_px);
            if (endLevel) return true;

            // ── STEP 2: X ADVANCE for ground support check ──
            int newX_fixed = s.X_fixed + s.VelX_fixed;
            s.X_fixed = newX_fixed;

            // ── STEP 3: GROUND SUPPORT CHECK at new X ──
            if (s.OnGround)
            {
                if (!VerifyGroundSupport(ref s))
                {
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[GROUND_LOST] newX={newX_fixed >> 8}px Y={s.Y_fixed >> 8}px");
#endif
                    s.OnGround = false;
                    s.WasZeroedByCollision = false;
                }
            }

            // ── STEP 4: REVERT to OLD X for physics ──
            s.X_fixed = oldX_fixed;

            // ── STEP 5: Y PHYSICS + EJECT at OLD X (movement) ──
            if (s.GameMode == 0) // Cube mode
            {
                CubeGravity(ref s);

                bool ejectDied = false;
                CubeEject(ref s, out ejectDied);
                if (ejectDied)
                {
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[EJECT_DEATH] X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
                    _lastDeathReason = "EJECT_DEATH"; _lastDeathX = s.X_fixed >> 8; _lastDeathY = s.Y_fixed >> 8;
#endif
                    return false;
                }

                if (CheckCenterPointDeath(ref s))
                {
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[CENTER_DEATH] X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
                    _lastDeathReason = "CENTER_DEATH"; _lastDeathX = s.X_fixed >> 8; _lastDeathY = s.Y_fixed >> 8;
#endif
                    return false;
                }

                // Jump input (ONLY when velY == 0, matching gamemode_cube.h)
                if (input && s.VelY_fixed == 0)
                {
                    s.VelY_fixed = GetJumpVel(s.Mini) * s.GravMul;
                    s.OnGround = false;
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[JUMP] VelY=0x{s.VelY_fixed:X} gravMul={s.GravMul} mini={s.Mini}");
#endif
                }
            }
            else if (s.GameMode == 1) // Ship mode
            {
                ShipGravityAndThrust(ref s, input);

                if (CheckCenterPointDeath(ref s))
                    return false;

                ShipEject(ref s, out bool shipDied);
                if (shipDied)
                    return false;
            }

            // ── STEP 6: POST-Y GRAVITY PORTAL CHECK at OLD X, new Y ──
            CheckGravityPortalsPostY(ref s, oldX_px);

            // ── STEP 7: FORWARD COLLISION at OLD X, post-eject Y ──
            if (s.GameMode == 0 || s.GameMode == 1 || s.GameMode == 4 || s.GameMode == 8 || s.GameMode == 10)
            {
                if (CheckForwardCollision(ref s))
                {
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[FWD_DEATH] X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
                    _lastDeathReason = "FWD_DEATH"; _lastDeathX = s.X_fixed >> 8; _lastDeathY = s.Y_fixed >> 8;
#endif
                    return false;
                }
            }

            // ── STEP 8: RESTORE NEW X ──
            s.X_fixed = newX_fixed;

            // ── STEP 9: DEATH CHECK at new X (bg_coll_death) ──
            if (CheckDeathCollision(ref s))
            {
#if !DISABLE_DEBUG_LOGGING
                PfLog($"[DEATH_COLL] X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
                _lastDeathReason = "DEATH_COLL"; _lastDeathX = s.X_fixed >> 8; _lastDeathY = s.Y_fixed >> 8;
#endif
                return false;
            }

            // ── STEP 10: MAP BOUNDS CHECK ──
            int playerY_px = s.Y_fixed >> 8;
            int worldBottom = (mapHeight - groundRowsToReserve) * TILE;
            if (playerY_px < 0 || playerY_px >= worldBottom + TILE)
            {
#if !DISABLE_DEBUG_LOGGING
                PfLog($"[BOUNDS_DEATH] Y={playerY_px}px worldBottom={worldBottom}");
                _lastDeathReason = "BOUNDS_DEATH"; _lastDeathX = s.X_fixed >> 8; _lastDeathY = playerY_px;
#endif
                return false;
            }

            return true;
        }

        // ═══════════════════════════════════════════════════════════════════
        //  CUBE PHYSICS (matching ProcessCubePhysics_Fresh)
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// CommonGravityRoutine_Fresh — apply gravity and integrate Y.
        /// Matches simulator's gravity skip: when velocity was zeroed by
        /// collision (player resting on ground), skip gravity entirely.
        /// </summary>
        private void CubeGravity(ref SimState s)
        {
            // GRAV_SKIP: match CommonGravityRoutine_Fresh — skip gravity when
            // velocity was zeroed by collision and is still 0.
            if (s.VelY_fixed == 0 && s.WasZeroedByCollision)
                return;

            // Clear wasZeroed flag when velocity is non-zero (player left ground)
            if (s.VelY_fixed != 0)
                s.WasZeroedByCollision = false;

            // Calculate gravity acceleration
            int gravity = GetGravity(s.Mini);
            int accel = gravity * s.GravMul;

            // Check past max fall speed → decelerate
            if (s.GravMul > 0)
            {
                if (s.VelY_fixed > maxFallSpeed)
                    accel = -accel;
            }
            else
            {
                if (s.VelY_fixed < -maxFallSpeed)
                    accel = -accel;
            }

#if !DISABLE_DEBUG_LOGGING
            int oldVelY = s.VelY_fixed;
            int oldY = s.Y_fixed;
#endif

            // Apply acceleration
            s.VelY_fixed += accel;

            // Integrate Y
            s.Y_fixed += s.VelY_fixed;

#if !DISABLE_DEBUG_LOGGING
            PfLog($"[CUBE_GRAV] velY: 0x{oldVelY:X} + 0x{accel:X} = 0x{s.VelY_fixed:X}, posY: 0x{oldY:X} ({oldY >> 8}px) -> 0x{s.Y_fixed:X} ({s.Y_fixed >> 8}px)");
#endif

            // Bottom: don't fall below map
            int maxY_fixed = Math.Max(0, (mapHeight * TILE - TILE)) << 8;
            if (s.Y_fixed > maxY_fixed) s.Y_fixed = maxY_fixed;
            // Top: do NOT clamp to 0 — let Y go negative so the bounds
            // check in StepFrame kills the player (matches NES off-screen death).
        }

        /// <summary>
        /// CubeEject_Fresh — collision detection and position/velocity correction.
        /// Normal gravity: check floor (landing). Ceiling headbonk only with hblocked/fblocked (not tracked).
        /// Reversed gravity: check ceiling (landing). Floor headbonk only with hblocked/fblocked (not tracked).
        /// 
        /// Matching CubeEject_Fresh in CubePhysics_Fresh.partial.cs:
        ///   Normal:   CheckCollisionDown = unconditional landing.
        ///             CheckCollisionUp   = only if hblocked||fblocked (alphabet blocks).
        ///   Reversed: CheckCollisionUp   = unconditional landing (velY <= 0).
        ///             CheckCollisionDown = only if hblocked||fblocked (alphabet blocks).
        /// The pathfinder has no alphabet block tracking, so the headbonk branches are omitted.
        /// </summary>
        private void CubeEject(ref SimState s, out bool died)
        {
            died = false;

            int playerX_px = s.X_fixed >> 8;
            int playerY_px = s.Y_fixed >> 8;
            int hbW = GetHitboxW(s.Mini);
            int hbH = GetHitboxH(s.Mini);
            int hbOffY = GetHitboxOffsetY(s.Mini, s.GravFlipped);
            int collX = playerX_px;
            int collY = playerY_px + hbOffY;

            if (!s.GravFlipped) // Normal gravity
            {
                // Floor collision (landing) — unconditional, matching CheckCollisionDown path
                var (floorHit, floorTopY, floorSpikeDeath) = CheckFloor(collX, collY, hbW, hbH);
                if (floorSpikeDeath)
                {
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[EJECT] floor spike death at X={playerX_px} Y={playerY_px}");
#endif
                    died = true;
                    return;
                }
                if (floorHit && s.VelY_fixed >= 0)
                {
                    int newY = floorTopY - hbH - hbOffY;
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[EJECT] floor land Y: {playerY_px} -> {newY} (floorTop={floorTopY})");
#endif
                    s.Y_fixed = newY << 8;
                    s.VelY_fixed = 0;
                    s.WasZeroedByCollision = true;
                    s.OnGround = true;
                }

                // NOTE: Ceiling headbonk (CheckCollisionUp) only fires in the simulator
                // when hblocked || fblocked (alphabet blocks). Pathfinder doesn't track
                // these flags, so headbonk is omitted — cube passes through ceilings from
                // below, matching actual NES behavior without alphabet blocks.
            }
            else // Reversed gravity
            {
                // Ceiling landing — unconditional when moving toward ceiling (velY <= 0)
                // Matching CheckCollisionUp path in simulator's reversed gravity branch
                if (s.VelY_fixed <= 0)
                {
                    var (ceilHit, ceilBotY) = CheckCeiling(collX, collY, hbW, hbH);
                    if (ceilHit)
                    {
                        int newY = ceilBotY - hbOffY;
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[EJECT] ceiling land (reversed) Y: {playerY_px} -> {newY} (ceilBot={ceilBotY})");
#endif
                        s.Y_fixed = newY << 8;
                        s.VelY_fixed = 0;
                        s.WasZeroedByCollision = true;
                        s.OnGround = true;
                    }
                }

                // NOTE: Floor headbonk (CheckCollisionDown) only fires in the simulator
                // when hblocked || fblocked (alphabet blocks). Pathfinder doesn't track
                // these flags, so headbonk is omitted — cube passes through floors from
                // above when reversed, matching actual NES behavior without alphabet blocks.
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  SHIP PHYSICS (matching ShipPhysics_Fresh + UfoShipEject_Fresh)
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Ship gravity: 4 gravity variants depending on hold state and fall direction.
        /// When holding, gravity is negated (thrust opposite to gravity direction).
        /// Matching ShipPhysics_Fresh in ShipPhysics_Fresh.partial.cs.
        /// </summary>
        private void ShipGravityAndThrust(ref SimState s, bool holding)
        {
            // GRAV_SKIP: match CommonGravityRoutine_Fresh — skip gravity when
            // velocity was zeroed by collision and is still 0.
            // Ship mode (1) is NOT excluded from this check (only modes 8,9 are).
            if (s.VelY_fixed == 0 && s.WasZeroedByCollision)
                return;

            // Clear wasZeroed when velocity is non-zero (player left ground)
            if (s.VelY_fixed != 0)
                s.WasZeroedByCollision = false;

            int gravMul = s.GravMul;
            bool falling = gravMul > 0 ? (s.VelY_fixed > 0) : (s.VelY_fixed < 0);

            int gravity;
            if (holding && falling)
                gravity = ShipGravityHoldFall(s.Mini) * gravMul;
            else if (holding)
                gravity = ShipGravityBase(s.Mini) * gravMul;
            else if (!falling)
                gravity = ShipGravityAfterHold(s.Mini) * gravMul;
            else
                gravity = ShipGravity(s.Mini) * gravMul;

            // Negate when holding (thrust opposite to gravity)
            if (holding)
                gravity = -gravity;

            s.VelY_fixed += gravity;
            s.Y_fixed += s.VelY_fixed;

            // Clamp velocity to ship max speeds
            int maxDown = ShipMaxFallSpeed(s.Mini) * gravMul;
            int maxUp = -ShipMaxFallSpeedHold(s.Mini) * gravMul;

            if (gravMul > 0)
            {
                if (s.VelY_fixed < maxUp) s.VelY_fixed = maxUp;
                if (s.VelY_fixed > maxDown) s.VelY_fixed = maxDown;
            }
            else
            {
                if (s.VelY_fixed > maxUp) s.VelY_fixed = maxUp;
                if (s.VelY_fixed < maxDown) s.VelY_fixed = maxDown;
            }

            // Clamp Y to world bounds (bottom only)
            int maxY_fixed = Math.Max(0, (mapHeight * TILE - TILE)) << 8;
            if (s.Y_fixed > maxY_fixed) s.Y_fixed = maxY_fixed;
            // Top: do NOT clamp to 0 — let Y go negative so the bounds
            // check in StepFrame kills the player (matches NES off-screen death).
        }

        /// <summary>
        /// Ship eject: ceiling + floor collision (matching UfoShipEject_Fresh).
        /// No slopes — just flat collision.
        /// </summary>
        private void ShipEject(ref SimState s, out bool died)
        {
            died = false;
            int playerX_px = s.X_fixed >> 8;
            int playerY_px = s.Y_fixed >> 8;
            int hbW = GetHitboxW(s.Mini);
            int hbH = GetHitboxH(s.Mini);
            int hbOffY = GetHitboxOffsetY(s.Mini, s.GravFlipped);
            int collX = playerX_px;
            int collY = playerY_px + hbOffY;

            // Ceiling check — NES ufo_ship_eject has NO velocity guard
            var (ceilHit, ceilBotY) = CheckCeiling(collX, collY, hbW, hbH);
            if (ceilHit)
            {
                int newY = ceilBotY - hbOffY;
                s.Y_fixed = newY << 8;
                s.VelY_fixed = 0;
                collY = newY + hbOffY;
            }

            // Floor check — NES ufo_ship_eject has NO velocity guard
            var (floorHit, floorTopY, spikeDeath) = CheckFloor(collX, collY, hbW, hbH);
            if (spikeDeath) { died = true; return; }
            if (floorHit)
            {
                int newY = floorTopY - hbH - hbOffY;
                s.Y_fixed = newY << 8;
                s.VelY_fixed = 0;
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  GROUND SUPPORT VERIFICATION
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Verify that the player still has floor support at the current position.
        /// Called after X advance to detect walking off ledges.
        /// Matches SimulateNumericStep's ground support check.
        /// </summary>
        private bool VerifyGroundSupport(ref SimState s)
        {
            int playerX_px = s.X_fixed >> 8;
            int playerY_px = s.Y_fixed >> 8;
            int hbW = GetHitboxW(s.Mini);
            int hbH = GetHitboxH(s.Mini);
            int hbOffY = GetHitboxOffsetY(s.Mini, s.GravFlipped);

            if (!s.GravFlipped)
            {
                // Normal gravity: check below feet
                int footY_px = playerY_px + hbOffY + hbH;
                int tileBelowY = footY_px / TILE;
                int tileArrayY = tileBelowY + groundRowsToReserve;

                // Ground layer always provides support
                if (tileArrayY >= mapHeight)
                    return true;

                if (tileArrayY < 0) return false;

                int tileLeftX = playerX_px / TILE;
                int tileRightX = (playerX_px + hbW - 1) / TILE;

                for (int tx = tileLeftX; tx <= tileRightX; tx++)
                {
                    if (tx < 0 || tx >= mapWidth) continue;
                    int idx = tileArrayY * mapWidth + tx;
                    if (idx < 0 || idx >= tiles.Length) continue;
                    int tid = tiles[idx];
                    var col = MetatileCollisionTable.GetCollision((byte)tid);
                    if (col == MetatileCollision.COL_NONE) continue;

                    // Check if this tile provides floor support
                    var (cLeft, cTop, cRight, cBottom) = GetCollisionBounds(col);
                    if (cRight <= cLeft) continue;

                    int tileWorldX = tx * TILE;
                    int colLeft_px = tileWorldX + cLeft;
                    int colRight_px = tileWorldX + cRight;

                    // Check horizontal overlap
                    if ((playerX_px + hbW - 1) >= colLeft_px && playerX_px < colRight_px)
                    {
                        // Check if foot is near the collision top
                        int tileWorldY = tileBelowY * TILE;
                        int collisionTop_px = tileWorldY + cTop;
                        if (footY_px >= collisionTop_px - 1 && footY_px <= tileWorldY + cBottom)
                            return true;
                    }
                }

                return false;
            }
            else
            {
                // Reversed gravity: check above head
                int headY_px = playerY_px + hbOffY;
                // Floor division for correct tile lookup at negative values
                int valGS = headY_px - 1;
                int tileAboveY = valGS >= 0 ? valGS / TILE : (valGS - TILE + 1) / TILE;
                if (tileAboveY < 0) return false;

                int tileArrayY = tileAboveY + groundRowsToReserve;
                if (tileArrayY < 0 || tileArrayY >= mapHeight) return false;

                int tileLeftX = playerX_px / TILE;
                int tileRightX = (playerX_px + hbW - 1) / TILE;

                for (int tx = tileLeftX; tx <= tileRightX; tx++)
                {
                    if (tx < 0 || tx >= mapWidth) continue;
                    int idx = tileArrayY * mapWidth + tx;
                    if (idx < 0 || idx >= tiles.Length) continue;
                    int tid = tiles[idx];
                    var col = MetatileCollisionTable.GetCollision((byte)tid);
                    if (col == MetatileCollision.COL_NONE) continue;

                    var (cLeft, cTop, cRight, cBottom) = GetCollisionBounds(col);
                    if (cRight <= cLeft) continue;
                    int tileWorldX = tx * TILE;
                    int colLeft_px = tileWorldX + cLeft;
                    int colRight_px = tileWorldX + cRight;
                    if ((playerX_px + hbW - 1) >= colLeft_px && playerX_px < colRight_px)
                        return true;
                }

                return false;
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  SPRITE PROCESSING
        // ═══════════════════════════════════════════════════════════════════

        private void ApplyPortalsUpTo(ref SimState s, int targetX_px)
        {
            int? lastGameModeSid = null, lastGameModeX = null;
            int? lastMiniSid = null, lastMiniX = null;
            int? lastGravSid = null, lastGravX = null;
            int? lastSpeedSid = null, lastSpeedX = null;

            foreach (var sp in allSprites)
            {
                if (sp.AnchorX_px > targetX_px) break;

                int sid = sp.SpriteId;

                if (IsGameModePortal(sid))
                {
                    if (!lastGameModeX.HasValue || sp.AnchorX_px > lastGameModeX.Value)
                    { lastGameModeSid = sid; lastGameModeX = sp.AnchorX_px; }
                }
                else if (IsMiniGrowthPortal(sid))
                {
                    if (!lastMiniX.HasValue || sp.AnchorX_px > lastMiniX.Value)
                    { lastMiniSid = sid; lastMiniX = sp.AnchorX_px; }
                }
                else if (IsGravityPortal(sid))
                {
                    if (!lastGravX.HasValue || sp.AnchorX_px > lastGravX.Value)
                    { lastGravSid = sid; lastGravX = sp.AnchorX_px; }
                }
                else if (IsSpeedPortal(sid))
                {
                    if (!lastSpeedX.HasValue || sp.AnchorX_px > lastSpeedX.Value)
                    { lastSpeedSid = sid; lastSpeedX = sp.AnchorX_px; }
                }

                s.ProcessedSprites.Add(sp.Index);
            }

            if (lastGameModeSid.HasValue)
            {
                int mode = SpriteIdToGameMode(lastGameModeSid.Value);
                if (mode >= 0) s.GameMode = mode;
            }
            if (lastMiniSid.HasValue)
                s.Mini = (lastMiniSid.Value == 0x18);
            if (lastGravSid.HasValue)
            {
                bool rev = IsReverseGravity(lastGravSid.Value);
                s.GravFlipped = rev;
                s.GravMul = rev ? -1 : 1;
            }
            if (lastSpeedSid.HasValue)
            {
                int spd = SpriteIdToSpeedFixed(lastSpeedSid.Value);
                if (spd > 0) s.VelX_fixed = spd;
            }
        }

        private bool ProcessSprites(ref SimState s, int currentX_px)
        {
            int playerY_px = s.Y_fixed >> 8;
            int hbW = GetHitboxW(s.Mini);
            int hbH = GetHitboxH(s.Mini);
            int hbOffY = GetHitboxOffsetY(s.Mini, s.GravFlipped);
            int playerTop = playerY_px + hbOffY;
            int playerBottom = playerTop + hbH;
            int playerRight = currentX_px + hbW;

            foreach (var sp in allSprites)
            {
                if (s.ProcessedSprites.Contains(sp.Index)) continue;
                if (sp.HitRight <= currentX_px) continue;
                if (sp.AnchorX_px - TILE > playerRight + TILE) break;

                int sid = sp.SpriteId;

                if (IsGameModePortal(sid) || IsSpeedPortal(sid) || IsGravityPortal(sid) ||
                    IsMiniGrowthPortal(sid) || IsEndLevel(sid))
                {
                    bool xOverlap = !((playerRight) < sp.HitLeft || sp.HitRight < currentX_px);
                    bool yOverlap = !((playerBottom) < sp.HitTop || sp.HitBottom < playerTop);

                    // End-level trigger (0x0F) fires on X overlap only (full-screen height),
                    // matching the simulator's X-position-only detection.
                    bool hit = IsEndLevel(sid) ? xOverlap : (xOverlap && yOverlap);

#if !DISABLE_DEBUG_LOGGING
                    if (IsGravityPortal(sid) || IsGameModePortal(sid) || IsEndLevel(sid))
                    {
                        PfLog($"[SPRITE_CHECK] sid=0x{sid:X2} idx={sp.Index} playerBox=({currentX_px},{playerTop})-({playerRight},{playerBottom}) spriteBox=({sp.HitLeft},{sp.HitTop})-({sp.HitRight},{sp.HitBottom}) xOvlp={xOverlap} yOvlp={yOverlap} hit={hit}");
                    }
#endif

                    if (hit)
                    {
                        bool applied = ApplyPortalSprite(ref s, sid);
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[SPRITE_HIT] sid=0x{sid:X2} idx={sp.Index} applied={applied} gravFlipped={s.GravFlipped} gravMul={s.GravMul}");
#endif
                        if (applied)
                            s.ProcessedSprites.Add(sp.Index);
                        if (IsEndLevel(sid)) return true;
                    }
                    continue;
                }

                // Pad detection (requires hitbox overlap)
                if (IsYellowPad(sid) || IsPinkPad(sid) || IsRedPad(sid) || IsBluePad(sid) || IsGreenPad(sid))
                {
                    bool xOverlap = !((playerRight) < sp.HitLeft || sp.HitRight < currentX_px);
                    bool yOverlap = !((playerBottom) < sp.HitTop || sp.HitBottom < playerTop);

                    if (xOverlap && yOverlap)
                    {
                        s.ProcessedSprites.Add(sp.Index);
                        ApplyPadSprite(ref s, sid);
                    }
                    continue;
                }
            }

            return false;
        }

        /// <summary>
        /// Post-Y-integration gravity portal check. Matching the simulator's inline
        /// gravity portal detection that runs AFTER playerY_fixed += playerVelY_fixed
        /// (SimulatorWindow line ~10821). Uses 14×14 center-based hitbox.
        /// This catches horizontal portals that the player falls/jumps into.
        /// </summary>
        private void CheckGravityPortalsPostY(ref SimState s, int prevX_px)
        {
            int playerY_px = s.Y_fixed >> 8;
            int hbW = GetHitboxW(s.Mini);
            int playerCenter = (s.X_fixed >> 8) + (hbW / 2);
            const int PORTAL_HIT_W = 14;
            const int PORTAL_HIT_H = 14;
            int playerLeft = playerCenter - (PORTAL_HIT_W / 2);
            int playerRight = playerLeft + PORTAL_HIT_W;
            int playerTop = playerY_px;
            if (s.Mini && !s.GravFlipped)
                playerTop += 9;
            int playerBottom = playerTop + PORTAL_HIT_H;

#if !DISABLE_DEBUG_LOGGING
            // Gravity portal checks are logged individually inside the loop
#endif

            foreach (var sp in allSprites)
            {
                if (s.ProcessedSprites.Contains(sp.Index)) continue;
                if (sp.HitRight <= prevX_px) continue;
                if (sp.AnchorX_px - TILE > playerRight + TILE) break;

                int sid = sp.SpriteId;
                if (!IsGravityPortal(sid)) continue;

                bool xOverlap = !((playerRight) < sp.HitLeft || sp.HitRight < playerLeft);
                bool yOverlap = !((playerBottom) < sp.HitTop || sp.HitBottom < playerTop);

#if !DISABLE_DEBUG_LOGGING
                PfLog($"[GRAV_POSTY_CHECK] sid=0x{sid:X2} idx={sp.Index} player14x14=({playerLeft},{playerTop})-({playerRight},{playerBottom}) spriteBox=({sp.HitLeft},{sp.HitTop})-({sp.HitRight},{sp.HitBottom}) xOvlp={xOverlap} yOvlp={yOverlap}");
#endif

                if (xOverlap && yOverlap)
                {
                    bool applied = ApplyPortalSprite(ref s, sid);
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[GRAV_POSTY_HIT] sid=0x{sid:X2} applied={applied} gravFlipped={s.GravFlipped} gravMul={s.GravMul} VelY=0x{s.VelY_fixed:X}");
#endif
                    if (applied)
                        s.ProcessedSprites.Add(sp.Index);
                }
            }
        }

        /// <summary>
        /// Apply a portal sprite effect. Returns true if the portal actually activated
        /// (gravity portals are conditional and may not activate).
        /// </summary>
        private bool ApplyPortalSprite(ref SimState s, int sid)
        {
            if (IsEndLevel(sid)) return true;

            if (IsGameModePortal(sid))
            {
                int mode = SpriteIdToGameMode(sid);
                if (mode >= 0 && mode != s.GameMode)
                {
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[PORTAL_GAMEMODE] sid=0x{sid:X2} mode {s.GameMode} -> {mode} VelY halved: 0x{s.VelY_fixed:X} -> 0x{s.VelY_fixed / 2:X}");
#endif
                    s.GameMode = mode;
                    s.VelY_fixed /= 2;
                }
                return true;
            }
            else if (IsSpeedPortal(sid))
            {
                int spd = SpriteIdToSpeedFixed(sid);
#if !DISABLE_DEBUG_LOGGING
                PfLog($"[PORTAL_SPEED] sid=0x{sid:X2} VelX: 0x{s.VelX_fixed:X} -> 0x{spd:X}");
#endif
                if (spd > 0) s.VelX_fixed = spd;
                return true;
            }
            else if (IsGravityPortal(sid))
            {
                bool isReverse = IsReverseGravity(sid);
#if !DISABLE_DEBUG_LOGGING
                PfLog($"[PORTAL_GRAV_EVAL] sid=0x{sid:X2} isReverse={isReverse} gravFlipped={s.GravFlipped}");
#endif
                if (isReverse && !s.GravFlipped)
                {
                    s.GravFlipped = true;
                    s.GravMul = -1;
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[PORTAL_GRAV_FLIP] REVERSED! VelY halved: 0x{s.VelY_fixed:X} -> 0x{s.VelY_fixed / 2:X}");
#endif
                    s.VelY_fixed /= 2;
                    s.WasZeroedByCollision = false;
                    return true;
                }
                else if (!isReverse && s.GravFlipped)
                {
                    s.GravFlipped = false;
                    s.GravMul = 1;
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[PORTAL_GRAV_FLIP] NORMAL! VelY halved: 0x{s.VelY_fixed:X} -> 0x{s.VelY_fixed / 2:X}");
#endif
                    s.VelY_fixed /= 2;
                    s.WasZeroedByCollision = false;
                    return true;
                }
                // Conditions not met — portal not consumed
                return false;
            }
            else if (IsMiniGrowthPortal(sid))
            {
                s.Mini = (sid == 0x18);
                return true;
            }
            return true;
        }

        // Pad velocities from simulator's PadOrbHeights matrix (cube column = 0)
        // These are positive magnitudes; gravity direction is applied via GravMul
        private static int GetYellowPadVel(bool mini) => mini ? -0x680 : -0x7C0;  // row 1
        private static int GetPinkPadVel(bool mini)   => mini ? -0x3F0 : -0x510;  // row 3
        private static int GetRedPadVel(bool mini)    => mini ? -0x650 : -0x9F0;  // row 8
        // Blue/green pads use yellow orb velocity (row 0) — same as GetJumpVel

        private void ApplyPadSprite(ref SimState s, int sid)
        {
#if !DISABLE_DEBUG_LOGGING
            PfLog($"[PAD] sid=0x{sid:X2} gravFlipped={s.GravFlipped}");
#endif
            if (IsYellowPad(sid))
            {
                s.VelY_fixed = GetYellowPadVel(s.Mini) * s.GravMul;
                s.WasZeroedByCollision = false;
                s.OnGround = false;
            }
            else if (IsPinkPad(sid))
            {
                s.VelY_fixed = GetPinkPadVel(s.Mini) * s.GravMul;
                s.WasZeroedByCollision = false;
                s.OnGround = false;
            }
            else if (IsRedPad(sid))
            {
                s.VelY_fixed = GetRedPadVel(s.Mini) * s.GravMul;
                s.WasZeroedByCollision = false;
                s.OnGround = false;
            }
            else if (IsBluePad(sid))
            {
                bool isBottomPad = (sid == 0x0D || sid == 0xFD);
                s.GravFlipped = isBottomPad;
                s.GravMul = isBottomPad ? -1 : 1;
                s.VelY_fixed = GetJumpVel(s.Mini) * s.GravMul;
                s.WasZeroedByCollision = false;
                s.OnGround = false;
            }
            else if (IsGreenPad(sid))
            {
                s.GravFlipped = !s.GravFlipped;
                s.GravMul = s.GravFlipped ? -1 : 1;
                s.VelY_fixed = GetJumpVel(s.Mini) * s.GravMul;
                s.WasZeroedByCollision = false;
                s.OnGround = false;
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  COLLISION DETECTION (matching CollisionDetection.partial.cs)
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Get tile collision type at world tile coordinates.
        /// Returns COL_ALL for implicit ground layer, COL_NONE for out of bounds.
        /// </summary>
        private MetatileCollision GetTileCollision(int tileX, int tileY)
        {
            int tileArrayY = tileY + groundRowsToReserve;
            if (tileX < 0 || tileX >= mapWidth) return MetatileCollision.COL_NONE;
            if (tileArrayY < 0) return MetatileCollision.COL_NONE;
            if (tileArrayY >= mapHeight) return MetatileCollision.COL_ALL; // ground layer = solid
            int idx = tileArrayY * mapWidth + tileX;
            if (idx < 0 || idx >= tiles.Length) return MetatileCollision.COL_NONE;
            int tid = tiles[idx];
            return MetatileCollisionTable.GetCollision((byte)tid);
        }

        private static (int left, int top, int right, int bottom) GetCollisionBounds(MetatileCollision col)
        {
            switch (col)
            {
                case MetatileCollision.COL_ALL:
                case MetatileCollision.COL_FLOOR_CEIL:
                case MetatileCollision.COL_NO_SIDE:
                    return (0, 0, 16, 16);
                case MetatileCollision.COL_TOP:
                case MetatileCollision.COL_TOP_CENTER_SPIKE:
                    return (0, 0, 16, 8);
                case MetatileCollision.COL_BOTTOM:
                case MetatileCollision.COL_BOTTOM_CENTER_SPIKE:
                case MetatileCollision.COL_BOTTOM_LEFT_SPIKE:
                case MetatileCollision.COL_BOTTOM_RIGHT_SPIKE:
                case MetatileCollision.COL_BOTTOM_SPIKES:
                    return (0, 8, 16, 16);
                case MetatileCollision.COL_LEFT:
                    return (0, 0, 8, 16);
                case MetatileCollision.COL_RIGHT:
                    return (8, 0, 16, 16);
                case MetatileCollision.COL_UP_LEFT:
                    return (0, 0, 8, 8);
                case MetatileCollision.COL_UP_RIGHT:
                    return (8, 0, 16, 8);
                case MetatileCollision.COL_DOWN_LEFT:
                case MetatileCollision.COL_LEFT_SPIKE_BLOCK:
                    return (0, 8, 8, 16);
                case MetatileCollision.COL_DOWN_RIGHT:
                case MetatileCollision.COL_RIGHT_SPIKE_BLOCK:
                    return (8, 8, 16, 16);
                case MetatileCollision.COL_UP_LEFT_SPIKE:
                    return (0, 0, 8, 8);
                case MetatileCollision.COL_UP_RIGHT_SPIKE:
                    return (8, 0, 16, 8);
                case MetatileCollision.COL_TOP_LEFT_BOTTOM_RIGHT:
                case MetatileCollision.COL_TOP_RIGHT_BOTTOM_LEFT:
                case MetatileCollision.COL_TOP_LEFT_STAIRS:
                case MetatileCollision.COL_TOP_RIGHT_STAIRS:
                case MetatileCollision.COL_BOTTOM_LEFT_STAIRS:
                case MetatileCollision.COL_BOTTOM_RIGHT_STAIRS:
                    return (0, 0, 16, 16); // complex shapes

                // Pure death / slopes / none — no solid collision
                default:
                    return (16, 16, 0, 0);
            }
        }

        /// <summary>
        /// Check floor collision (downward). Matching CheckCollisionDown in CollisionDetection.partial.cs.
        /// Returns (hit, surfaceY, spikeDeath).
        /// 
        /// The spike death pre-check uses 3 X-points at the player's bottom edge,
        /// only checking COL_DEATH_TOP and COL_DEATH_BOTTOM (matching NES bg_coll_D).
        /// </summary>
        private (bool hit, int surfaceY, bool spikeDeath) CheckFloor(int collX, int collY, int collW, int collH)
        {
            int playerBottom_px = collY + collH;
            int tileBelowY = playerBottom_px / TILE;
            int playerLeft_px = collX;
            int playerRight_px = collX + collW - 1;

            // ── SPIKE DEATH PRE-CHECK (3 points at bottom edge) ──
            // Matches CheckCollisionDown's spike detection
            {
                int checkY = playerBottom_px;
                for (int cpIdx = 0; cpIdx < 3; cpIdx++)
                {
                    int px = cpIdx == 0 ? playerLeft_px
                           : cpIdx == 1 ? playerLeft_px + collW / 2
                           : playerLeft_px + collW;
                    int tileX = px / TILE;
                    int tileY = checkY / TILE;

                    if (tileX < 0 || tileX >= mapWidth || tileY < 0 || tileY >= mapHeight) continue;

                    int tileArrayY = tileY + groundRowsToReserve;
                    if (tileArrayY < 0 || tileArrayY >= mapHeight) continue;

                    int tileIdx = tileArrayY * mapWidth + tileX;
                    if (tileIdx < 0 || tileIdx >= tiles.Length) continue;

                    int tileId = tiles[tileIdx];
                    var collision = MetatileCollisionTable.GetCollision((byte)tileId);

                    int localX = px % TILE;
                    int localY = checkY % TILE;

                    // Only COL_DEATH_TOP and COL_DEATH_BOTTOM are checked here
                    // (matching NES bg_coll_D → bg_coll_U_D_checks)
                    if ((collision == MetatileCollision.COL_DEATH_TOP || collision == MetatileCollision.COL_DEATH_BOTTOM) &&
                        MetatileCollisionTable.TileKillsAtPixel(collision, localX, localY))
                    {
                        return (false, 0, true); // spike death!
                    }
                }
            }

            // ── BOUNDS CHECK ──
            if (tileBelowY < 0 || tileBelowY >= mapHeight) return (false, 0, false);

            int tileArrayYFloor = tileBelowY + groundRowsToReserve;

            // Ground layer is always solid
            if (tileArrayYFloor >= mapHeight)
            {
                int groundTop = tileBelowY * TILE;
                return (true, groundTop, false);
            }

            // ── TILE SCAN (left to right) ──
            int tileLeftX = playerLeft_px / TILE;
            int tileRightX = playerRight_px / TILE;

            for (int tx = tileLeftX; tx <= tileRightX; tx++)
            {
                if (tx < 0 || tx >= mapWidth) continue;

                int tileIdx = tileArrayYFloor * mapWidth + tx;
                if (tileIdx < 0 || tileIdx >= tiles.Length) continue;

                int tileId = tiles[tileIdx];
                var collision = MetatileCollisionTable.GetCollision((byte)tileId);
                if (collision == MetatileCollision.COL_NONE) continue;

                var (cLeft, cTop, cRight, cBottom) = GetCollisionBounds(collision);
                if (cRight <= cLeft || cBottom <= cTop) continue; // no solid

                int tileWorldX = tx * TILE;
                int tileWorldY = tileBelowY * TILE;
                int collisionTop_px = tileWorldY + cTop;
                int collisionBottom_px = tileWorldY + cBottom;
                int colLeft_px = tileWorldX + cLeft;
                int colRight_px = tileWorldX + cRight;

                if (playerBottom_px >= collisionTop_px - 1 && playerBottom_px <= collisionBottom_px)
                {
                    if (playerRight_px >= colLeft_px && playerLeft_px < colRight_px)
                    {
                        return (true, collisionTop_px, false);
                    }
                }
            }

            // Also check implicit ground floor
            if (groundRowsToReserve > 0)
            {
                int groundTopWorld_px = (mapHeight - groundRowsToReserve) * TILE;
                if (playerBottom_px >= groundTopWorld_px - 1)
                    return (true, groundTopWorld_px, false);
            }

            return (false, 0, false);
        }

        /// <summary>
        /// Check ceiling collision (upward). Returns (hit, ceilingBottomY).
        /// Matching CheckCollisionUp in CollisionDetection.partial.cs.
        /// </summary>
        private (bool hit, int ceilingBottomY) CheckCeiling(int collX, int collY, int collW, int collH)
        {
            int topY = collY;
            // Floor division: C# truncates toward zero, so (-1)/16 = 0 instead of -1.
            // Use proper floor division for correct tile lookup at Y=0.
            int valC = topY - 1;
            int tileAboveY = valC >= 0 ? valC / TILE : (valC - TILE + 1) / TILE;
            if (tileAboveY < 0) return (false, 0);

            int tileLeftX = collX / TILE;
            int tileRightX = (collX + collW - 1) / TILE;
            int tileArrayY = tileAboveY + groundRowsToReserve;
            if (tileArrayY < 0 || tileArrayY >= mapHeight) return (false, 0);

            for (int tx = tileLeftX; tx <= tileRightX; tx++)
            {
                if (tx < 0 || tx >= mapWidth) continue;
                int tileIdx = tileArrayY * mapWidth + tx;
                if (tileIdx < 0 || tileIdx >= tiles.Length) continue;

                int tileId = tiles[tileIdx];
                var collision = MetatileCollisionTable.GetCollision((byte)tileId);
                if (collision == MetatileCollision.COL_NONE) continue;

                var (cLeft, cTop, cRight, cBottom) = GetCollisionBounds(collision);
                if (cRight <= cLeft) continue;

                int tileWorldX = tx * TILE;
                int tileWorldY = tileAboveY * TILE;
                int colBottom_px = tileWorldY + cBottom;
                int colLeft_px = tileWorldX + cLeft;
                int colRight_px = tileWorldX + cRight;

                if ((collX + collW - 1) >= colLeft_px && collX < colRight_px)
                {
                    if (topY >= tileWorldY + cTop && topY < colBottom_px)
                        return (true, colBottom_px);
                }
            }

            return (false, 0);
        }

        // ═══════════════════════════════════════════════════════════════════
        //  DEATH CHECKS
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// CheckCenterPointDeath_Fresh — single center pixel death check.
        /// Matches CubePhysics_Fresh.partial.cs bg_coll_death().
        /// Center point: (x + (w>>1) - 1, y + (h>>1) + hitboxOffsetY)
        /// </summary>
        private bool CheckCenterPointDeath(ref SimState s)
        {
            int playerX_px = s.X_fixed >> 8;
            int playerY_px = s.Y_fixed >> 8;
            int hbW = GetHitboxW(s.Mini);
            int hbH = GetHitboxH(s.Mini);
            int hbOffY = GetHitboxOffsetY(s.Mini, s.GravFlipped);

            int centerX_px = playerX_px + (hbW >> 1) - 1;
            int centerY_px = playerY_px + (hbH >> 1) + hbOffY;

            int tileX = centerX_px / TILE;
            int tileY = centerY_px / TILE;

            if (tileX < 0 || tileX >= mapWidth || tileY < 0 || tileY >= mapHeight) return false;

            int tileArrayY = tileY + groundRowsToReserve;
            if (tileArrayY < 0 || tileArrayY >= mapHeight) return false;

            int tileIdx = tileArrayY * mapWidth + tileX;
            if (tileIdx < 0 || tileIdx >= tiles.Length) return false;

            int tileId = tiles[tileIdx];
            var collision = MetatileCollisionTable.GetCollision((byte)tileId);

            int localX = centerX_px % TILE;
            int localY = centerY_px % TILE;

            return MetatileCollisionTable.TileKillsAtPixel(collision, localX, localY);
        }

        /// <summary>
        /// CheckDeathCollision — 5-point death check matching SimulatorWindow's CheckDeathCollision.
        /// Points: 4 hitbox corners (inset 3px sides, 2px top/bottom) + right-center (side spikes).
        /// </summary>
        private bool CheckDeathCollision(ref SimState s)
        {
            int playerX_px = s.X_fixed >> 8;
            int playerY_px = s.Y_fixed >> 8;
            int hbW = GetHitboxW(s.Mini);
            int hbH = GetHitboxH(s.Mini);
            int hbOffY = GetHitboxOffsetY(s.Mini, s.GravFlipped);

            // 5 check points matching CheckDeathCollision in SimulatorWindow:
            int leftX = playerX_px + 3;
            int rightX = playerX_px + hbW - 3;
            int rightEdgeX = playerX_px + hbW - 1;
            int topY = playerY_px + hbH - 2 + hbOffY;      // near bottom of hitbox
            int bottomY = playerY_px + (s.Mini ? 0 : 2) + hbOffY; // near top of hitbox
            int centerY = playerY_px + (hbH / 2) + hbOffY;

            // Point 0: (leftX, topY) — bottom-left
            // Point 1: (rightX, topY) — bottom-right
            // Point 2: (leftX, bottomY) — top-left
            // Point 3: (rightX, bottomY) — top-right
            // Point 4: (rightEdgeX, centerY) — right-center (side spikes)
            int px, py;
            for (int i = 0; i < 5; i++)
            {
                switch (i)
                {
                    case 0: px = leftX; py = topY; break;
                    case 1: px = rightX; py = topY; break;
                    case 2: px = leftX; py = bottomY; break;
                    case 3: px = rightX; py = bottomY; break;
                    default: px = rightEdgeX; py = centerY; break;
                }

                int tileX = px / TILE;
                int tileY = py / TILE;
                int tileArrayY = tileY + groundRowsToReserve;

                if (tileX < 0 || tileX >= mapWidth) continue;
                if (tileArrayY < 0 || tileArrayY >= mapHeight) continue;

                int tileIdx = tileArrayY * mapWidth + tileX;
                if (tileIdx < 0 || tileIdx >= tiles.Length) continue;

                int tid = tiles[tileIdx];
                var col = MetatileCollisionTable.GetCollision((byte)tid);

                int localX = px % TILE;
                int localY = py % TILE;

                if (MetatileCollisionTable.TileKillsAtPixel(col, localX, localY))
                {
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[DEATH_POINT] point{i} ({px},{py}) tile=({tileX},{tileArrayY}) tid=0x{tid:X2} col={col}");
                    _lastDeathReason = $"DEATH_COLL:pt{i}({px},{py})tile({tileX},{tileArrayY})tid=0x{tid:X2}col={col}";
                    _lastDeathX = s.X_fixed >> 8; _lastDeathY = s.Y_fixed >> 8;
#endif
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Forward collision check — right edge middle pixel for solid collision.
        /// Matches SimulateNumericStep step 24 (x_movement_coll).
        /// This detects running into a wall and triggers death.
        /// </summary>
        private bool CheckForwardCollision(ref SimState s)
        {
            int playerX_px = s.X_fixed >> 8;
            int playerY_px = s.Y_fixed >> 8;
            int hbW = GetHitboxW(s.Mini);
            int hbH = GetHitboxH(s.Mini);
            int hbOffY = GetHitboxOffsetY(s.Mini, s.GravFlipped);

            // NES bg_coll_R checks at Generic.x + Generic.width (one pixel PAST the hitbox right edge)
            int rightEdge_px = playerX_px + hbW;
            // NES bg_side_coll_common: Generic.y + (mini ? (0x10-height)>>1 : 0) + (height>>1)
            // then for mini cube/robot/ninja: += gravity ? 3 : -2
            int centerY_px;
            if (s.Mini)
            {
                int miniTopOffset = (0x10 - hbH) >> 1;  // (16-7)>>1 = 4
                centerY_px = playerY_px + miniTopOffset + (hbH >> 1);  // +4 +3 = +7
                // Mini cube/robot/ninja adjustment
                if (s.GameMode == 0 || s.GameMode == 4 || s.GameMode == 8)
                    centerY_px += s.GravFlipped ? 3 : -2;
            }
            else
            {
                centerY_px = playerY_px + (hbH >> 1);  // +7 for normal cube
            }

            int tileX = rightEdge_px / TILE;
            int tileY = centerY_px / TILE;
            int tileArrayY = tileY + groundRowsToReserve;

            if (tileX < 0 || tileX >= mapWidth) return false;
            if (tileArrayY < 0 || tileArrayY >= mapHeight) return false;

            int tileIdx = tileArrayY * mapWidth + tileX;
            if (tileIdx < 0 || tileIdx >= tiles.Length) return false;

            int tileId = tiles[tileIdx];
            var collision = MetatileCollisionTable.GetCollision((byte)tileId);

            // Check if the collision type is solid (provides a wall)
            var (cLeft, cTop, cRight, cBottom) = GetCollisionBounds(collision);
            if (cRight <= cLeft) return false; // not solid

            int tileWorldX = tileX * TILE;
            int tileWorldY = tileY * TILE;
            int localX = rightEdge_px - tileWorldX;
            int localY = centerY_px - tileWorldY;

            // Check if the right edge pixel is inside the solid region
            if (localX >= cLeft && localX < cRight && localY >= cTop && localY < cBottom)
            {
#if !DISABLE_DEBUG_LOGGING
                PfLog($"[FWD_COLL] probe=({rightEdge_px},{centerY_px}) tile=({tileX},{tileY}) tid=0x{tileId:X2} col={collision} oldX={playerX_px} newX={(s.X_fixed + s.VelX_fixed) >> 8}");
#endif
                return true; // blocked → death
            }

            return false;
        }
    }
}
