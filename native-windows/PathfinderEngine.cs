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
        private const int CORRIDOR_LOOK_AHEAD_TILES = 15; // scan ahead for obstacles (240px ≈ 87 frames at 1x)
        private const int SHIP_LOOKAHEAD_HORIZON = 30;     // multi-frame lookahead for ship decisions
        private const int SHIP_TREE_DEPTH = 20;            // binary-tree search depth for ship
        private const int SHIP_TREE_MAX_NODES = 16000;      // cap on total nodes explored in tree search
        private const int MAX_FRAMES = 60 * 60 * 5; // 5 minutes at 60fps
        private const int MAX_BACKTRACK_ATTEMPTS = 600; // max backtrack retries PER death point (reset after each success)
        private const int MAX_TOTAL_BACKTRACK_ATTEMPTS = 3000; // absolute cap on total backtracks across all deaths
        private const int MAX_TOTAL_ITERATIONS = MAX_FRAMES * 10; // hard cap on total frame iterations (including replays)
        private const int MAX_CHECKPOINT_DEPTH = 120;   // max saved decision checkpoints (deep history for long levels)
        // No rewind limit — backtracker goes as far back as needed
        private const int MIN_CHECKPOINT_SPACING = 8;   // minimum frames between consecutive checkpoints

        /// <summary>
        /// Jump timing bias: 0.0 = earliest viable jump, 0.5 = middle (default), 1.0 = latest viable jump.
        /// </summary>
        public double JumpTimingBias { get; set; } = 0.5;

        /// <summary>
        /// Optional progress reporter. Reports 0-100 (percentage of MAX_FRAMES).
        /// </summary>
        public IProgress<int>? Progress { get; set; }

        public Action<List<(int x, int y)>?, int, int, bool>? OnSpeculativePath { get; set; }
        public int CurrentX_px => _currentX_px;
        private volatile int _currentX_px;

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
        /// <summary>
        /// Mode-aware hitbox Y offset.  Cube mini=9, Ball mini=4, others=0.
        /// </summary>
        private static int GetHitboxOffsetY(int gameMode, bool mini, bool gravFlipped)
        {
            if (!mini) return 0;
            if (gameMode == 2) // Ball: (0x10 - 7) >> 1 = 4
                return gravFlipped ? 0 : 4;
            return gravFlipped ? 0 : 9; // Cube/Robot/etc. mini offset
        }

        // Ball physics constants (from GameModePhysics.cs, 60fps values)
        private static int BallGravity(bool mini) => mini ? 0x57 : 0x47;
        private static int BallSwitchVel(bool mini) => mini ? 0x120 : 0x200;
        // Ball max fall speed is 0x600 — same as cube (uses the class maxFallSpeed field)

        // Ship physics constants (from GameModePhysics.cs, 60fps values)
        private static int ShipGravityBase(bool mini) => mini ? 0x31 : 0x2A;
        private static int ShipGravityAfterHold(bool mini) => mini ? 0x3B : 0x32;
        private static int ShipGravityHoldFall(bool mini) => mini ? 0x3E : 0x34;
        private static int ShipGravity(bool mini) => mini ? 0x27 : 0x22; // SHIP_GRAVITY at 60fps
        private static int ShipMaxFallSpeed(bool mini) => mini ? 0x0357 : 0x02D7;   // SHIP_MAX_FALLSPEED at 60fps
        private static int ShipMaxFallSpeedHold(bool mini) => mini ? 0x042D : 0x038D; // SHIP_MAX_FALLSPEED_HOLD at 60fps

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

        // ── Orb classification ─────────────────────────────────────────────
        private static bool IsYellowOrb(int sid) => sid == 0x0B;
        private static bool IsYellowOrbBigger(int sid) => sid == 0x1F;
        private static bool IsYellowOrbSmaller(int sid) => sid == 0x29;
        private static bool IsPinkOrb(int sid) => sid == 0x06;
        private static bool IsRedOrb(int sid) => sid == 0x28;
        private static bool IsBlueOrb(int sid) => sid == 0x05 || sid == 0x7B;  // includes multi
        private static bool IsGreenOrb(int sid) => sid == 0x27 || sid == 0x7C; // includes multi
        private static bool IsBlackOrb(int sid) => sid == 0x44;
        private static bool IsWhiteOrb(int sid) => sid == 0x7A;
        private static bool IsVelocityOrb(int sid) =>
            IsYellowOrb(sid) || IsYellowOrbBigger(sid) || IsYellowOrbSmaller(sid) ||
            IsPinkOrb(sid) || IsRedOrb(sid) || IsBlackOrb(sid);
        private static bool IsGravityOrb(int sid) => IsBlueOrb(sid) || IsGreenOrb(sid);
        private static bool IsOrbSprite(int sid) =>
            IsVelocityOrb(sid) || IsGravityOrb(sid) || IsWhiteOrb(sid);

        // ── Pad/Orb velocity tables (matching sim's PadOrbHeights) ────────
        // Columns: 0=Cube, 1=Ship, 2=Ball, 3=UFO, 4=Robot, 5=Spider, 6=Wave, 7=Swingcopter
        // Rows: 0=YellowOrb, 1=YellowPad, 2=PinkOrb, 3=PinkPad,
        //       4=RedOrb, 5=YellowBigger, 6=BlackOrb, 7=YellowSmaller, 8=RedPad
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

        /// <summary>
        /// Map game mode to PadOrbHeights column index, matching sim's mode remapping.
        /// </summary>
        private static int GetPadOrbModeCol(int gameMode)
        {
            if (gameMode == 8) return 0; // Ninja → Cube
            if (gameMode == 9) return 7; // Pogo → Swingcopter
            if (gameMode == 11) return 0; // Football → Cube
            return gameMode; // 0-7 map directly
        }

        private static int GetPadOrbVel(int row, bool mini, int gameMode)
        {
            int col = GetPadOrbModeCol(gameMode);
            return mini ? PadOrbHeights_Mini[row][col] : PadOrbHeights[row][col];
        }

        /// <summary>
        /// Scan allSprites for an orb that overlaps the player hitbox at
        /// the CURRENT position (pre-advance X).  Returns the orb's sprite
        /// ID, or -1 if no orb overlaps.  Uses current X to match the NES
        /// timing: sprite_collide() runs BEFORE x_movement, so orbs are
        /// detected at the pre-advance position.
        /// </summary>
        private int ScanForOrbOverlap(in SimState state, out int orbSpriteIndex)
        {
            orbSpriteIndex = -1;
            // Use current X (pre-advance) to match NES sprite_collide timing
            int currentX_px = state.X_fixed >> 8;
            int hbW = GetHitboxW(state.Mini);
            int hbH = GetHitboxH(state.Mini);
            int hbOffY = GetHitboxOffsetY(state.Mini, state.GravFlipped);
            int playerY_px = state.Y_fixed >> 8;
            int playerTop = playerY_px + hbOffY;
            int playerBottom = playerTop + hbH;
            int playerRight = currentX_px + hbW;

            foreach (var sp in allSprites)
            {
                if (state.ProcessedSprites.Contains(sp.Index)) continue;
                if (sp.HitRight <= currentX_px) continue;
                if (sp.AnchorX_px - TILE > playerRight + TILE) break;

                int sid = sp.SpriteId;
                if (!IsOrbSprite(sid)) continue;

                bool xOverlap = !(playerRight < sp.HitLeft || sp.HitRight < currentX_px);
                bool yOverlap = !(playerBottom < sp.HitTop || sp.HitBottom < playerTop);

                if (xOverlap && yOverlap)
                {
                    orbSpriteIndex = sp.Index;
                    return sid;
                }
            }
            return -1;
        }

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
            public int ShipBias;        // _shipCorridorBias at checkpoint creation
        }
        private List<BacktrackCheckpoint> _backtrackCheckpoints = new();
        private int _backtrackAttempts;
        private int _totalBacktrackAttempts;
        private int _btOverrideFrame = -1;  // frame at which to apply override
        private int _btOverrideStage = 0;   // which alternative to try
        private bool _backtrackActive;      // true while replaying from a checkpoint (suppress new checkpoints)
        private bool _btSuppressJumpUntilAirborne; // stage-1 no-jump persists until cube falls off edge
        private int _btDeathFrame;           // frame of the death that triggered backtracking
        private int _shipCorridorBias;       // sustained Y offset for ship corridor target during backtrack

        // ── Speculative depth / frame counter (needed in both debug and release) ───
        private int _speculativeDepth; // >0 means we're inside lookahead — suppress logging
        private int _frameCounter;     // current frame in the main Run() loop

        // ── Debug logging ───────────────────────────────────────────────────
#if !DISABLE_DEBUG_LOGGING
        private readonly string pfDebugLogPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"famidash_pf_debug_{System.DateTime.UtcNow:yyyyMMdd_HHmmss}.txt");

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
            0x04,0x04,0x0D,-0x01,0x00,0x0D,0x00,0x00, // 08-0F (0x0A,0x0D: +8 from NES globalObjectOffset for bottom pads)
            0x01,0x01,0x01,0x01,-0x02,-0x02,-0x02,-0x02, // 10-17
            -0x02,-0x02,0x00,0x00,0x00,0x00,0x00,-0x01, // 18-1F
            -0x02,-0x02,-0x02,-0x02,-0x02,0x0D,0x00,-0x01, // 20-27 (0x25: +8 from NES globalObjectOffset for bottom pads)
            -0x01,-0x01,0x00,0x00,0x00,0x00,0x00,0x00, // 28-2F
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 30-37
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 38-3F
            0x00,0x00,0x00,0x00,-0x01,-0x01,-0x01,0x04, // 40-47
            0x04,0x00,0x00,-0x02,0x00,-0x01,-0x01,0x00, // 48-4F
            -0x01,-0x01,0x0D,0x00,-0x01,-0x01,0x0D,0x00, // 50-57 (0x52: +8 from NES globalObjectOffset for bottom pads)
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
            0x00,0x00,-0x07,0x00,0x00,0x0D,0x02,0x00  // F8-FF (0xFD: +8 from NES globalObjectOffset for bottom pads)
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
            public int BallFlipCooldown;         // frames to skip eject after ball gravity flip
            public HashSet<int> ProcessedSprites;

            // Orb system: pending orb that overlaps the player (activation requires input)
            public int PendingOrbIndex;         // -1 = no pending orb
            public int PendingOrbSpriteId;      // sprite ID of pending orb

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

        /// <summary>
        /// All paths attempted during backtracking (failed attempts).
        /// Each entry is a snapshot of the path segment that was explored
        /// before the backtracker rewound to try a different decision.
        /// </summary>
        public List<List<(int x, int y)>> AttemptedPaths { get; private set; } = new();

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
                // NES check_spr_objects() applies -1 to sprite Y (clc;sbc intentionally subtracts 1 extra)
                int hitTop = (storageTileY - groundRowsToReserve) * TILE + hyoff + pyOff - 1;
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
                ProcessedSprites = new HashSet<int>(),
                PendingOrbIndex = -1,
                PendingOrbSpriteId = -1
            };

            ApplyPortalsUpTo(ref state, startX_px);

            PathPoints.Clear();
            Inputs.Clear();
            _cubeHoldJump = false;
            _cubeHoldDelay = 0;
            _committedJumpDelay = -1;
            _backtrackCheckpoints = new List<BacktrackCheckpoint>();
            _backtrackAttempts = 0;
            _totalBacktrackAttempts = 0;
            _btOverrideFrame = -1;
            _backtrackActive = false;
            _btSuppressJumpUntilAirborne = false;
            _btDeathFrame = 0;
            _shipCorridorBias = 0;

            _frameCounter = 0;
            _speculativeDepth = 0;
#if !DISABLE_DEBUG_LOGGING
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

            // Report progress interval: every 1% of MAX_FRAMES
            int progressInterval = Math.Max(1, MAX_FRAMES / 100);
            int highWaterFrame = 0; // track furthest frame reached for progress
            int totalIterations = 0; // counts every frame step including replays

            for (int frame = 0; frame < MAX_FRAMES; frame++)
            {
                totalIterations++;
                if (totalIterations > MAX_TOTAL_ITERATIONS)
                {
                    Success = false;
                    ResultMessage = $"Aborted: exceeded {MAX_TOTAL_ITERATIONS} total iterations (frame={frame}, attempts={_backtrackAttempts})";
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[ABORT] total iterations {totalIterations} exceeded cap");
#endif
                    return;
                }
                if (frame > highWaterFrame) highWaterFrame = frame;
                if (frame % progressInterval == 0)
                    Progress?.Report(highWaterFrame * 100 / MAX_FRAMES);
                _currentX_px = state.X_fixed >> 8;
                _frameCounter = frame;
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
                bool isShipMode = (state.GameMode == 1);
                SimState preDecisionState = (wasGrounded || _cubeHoldJump || isShipMode) ? state.Clone() : default;
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
                    _shipCorridorBias = 0; // clear ship bias once past the obstacle
                    // Reset attempt budget after each successful backtrack.
                    // Each death point gets a fresh budget — solving early
                    // obstacles shouldn't deplete the budget for harder
                    // sections ahead.
                    _backtrackAttempts = 0;
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[BACKTRACK_DONE] advanced past death frame {_btDeathFrame}, resuming normal checkpointing (attempts={_backtrackAttempts}/{MAX_BACKTRACK_ATTEMPTS})");
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

                // Periodic "approach" checkpoints: when the cube is grounded and
                // NOT jumping (decided "no danger"), save a checkpoint every 30
                // frames.  This lets the backtracker try forcing a jump at
                // positions the cube originally walked past — essential for
                // multi-platform sections where the cube must jump ONTO blocks
                // long before danger is detected.
                if (!shouldCheckpoint && wasGrounded && !input && !preHoldJump
                    && _committedJumpDelay < 0 && frame % 30 == 0)
                {
                    shouldCheckpoint = true;
                }

                // Ship mode periodic checkpoints: ship is always airborne,
                // so the grounded-based checkpoint logic never fires.
                // Save a checkpoint every 24 frames (roughly one corridor
                // adjustment cycle) so the backtracker can try different
                // hold/release decisions within the ship section.
                if (!shouldCheckpoint && isShipMode && frame % 24 == 0)
                {
                    shouldCheckpoint = true;
                }
                // Enforce minimum spacing between checkpoints to prevent
                // wasting slots on consecutive frames (e.g., hold-jump landing
                // every frame).  This ensures the checkpoint buffer covers a
                // wider time window for deeper backtracking.
                if (shouldCheckpoint && _backtrackCheckpoints.Count > 0)
                {
                    int lastCpFrame = _backtrackCheckpoints[_backtrackCheckpoints.Count - 1].Frame;
                    if (frame - lastCpFrame < MIN_CHECKPOINT_SPACING)
                        shouldCheckpoint = false;
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
                        UsedBias = JumpTimingBias,
                        ShipBias = _shipCorridorBias
                    });
                }

#if !DISABLE_DEBUG_LOGGING
                PfLog($"[DECIDE] input={input}");
#endif

                bool alive = StepFrame(ref state, input, out bool endLevel);

                // Record path at visual center AFTER physics+eject (matching sim's
                // recording inside UfoShipEject_Fresh / CubePhysics_Fresh which uses
                //   X: (playerX_fixed >> 8) + playerVisualWidth / 2  (= X + 8)
                //   Y: (playerY_fixed >> 8) + [4 if mini && !inverted] + playerVisualHeight / 2
                {
                    int pathMiniOffY = (state.Mini && !state.GravFlipped) ? 4 : 0;
                    PathPoints.Add(((state.X_fixed >> 8) + 8,
                                    (state.Y_fixed >> 8) + pathMiniOffY + 8));
                }

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
                    // Path point already recorded above after StepFrame
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
                   _backtrackAttempts < MAX_BACKTRACK_ATTEMPTS &&
                   _totalBacktrackAttempts < MAX_TOTAL_BACKTRACK_ATTEMPTS)
            {
                int last = _backtrackCheckpoints.Count - 1;
                var cp = _backtrackCheckpoints[last];
                cp.RetryStage++;

                // Ship mode gets 3 stages (bias adjustments to give tree search
                // different starting conditions).
                // Other modes get 3 stages (no-jump, toggle-hold, flip-bias).
                int maxStages = 3;
                if (cp.RetryStage > maxStages)
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
                _shipCorridorBias = cp.ShipBias; // restore ship bias

                // Snapshot the failed path segment before truncating
                if (PathPoints.Count > cp.PathPointCount)
                {
                    var failedSegment = PathPoints.GetRange(cp.PathPointCount,
                        PathPoints.Count - cp.PathPointCount);
                    AttemptedPaths.Add(failedSegment);
                }

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
                _totalBacktrackAttempts++;

                _frameCounter = cp.Frame;
#if !DISABLE_DEBUG_LOGGING
                PfLog($"[BACKTRACK] attempt={_backtrackAttempts}/{MAX_BACKTRACK_ATTEMPTS} " +
                      $"total={_totalBacktrackAttempts}/{MAX_TOTAL_BACKTRACK_ATTEMPTS} " +
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
            // ── Backtrack override: handle for ALL game modes before
            //    mode-specific logic.  Without this, ship mode bypasses
            //    all override handling and the backtracker can't alter
            //    ship decisions. ──
            bool isOverrideFrame = (_btOverrideFrame == _frameCounter && _speculativeDepth == 0);
            if (isOverrideFrame)
            {
                int stage = _btOverrideStage;
                _btOverrideFrame = -1; // consume override

                if (state.GameMode == 1) // Ship mode overrides
                {
                    // Ship backtrack: set sustained corridor Y-bias to give the
                    // tree search a different starting trajectory.
                    int bias = 0;
                    switch (stage)
                    {
                        case 1: bias = -32; break;
                        case 2: bias = 32; break;
                        default: bias = (_shipCorridorBias <= 0) ? 64 : -64; break;
                    }
                    _shipCorridorBias = bias;
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[BACKTRACK_OVERRIDE_SHIP] stage={stage}: setting corridor bias={_shipCorridorBias}");
#endif
                    // Fall through to normal DecideShipInput with new bias active
                    return DecideShipInput(state);
                }

                // Cube/other mode overrides (original logic)
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
                        PfLog($"[BACKTRACK_OVERRIDE] stage=2: activated hold-jump");
#endif
                        return true;
                    }
                }
                else // stage 3 — opposite bias
                {
                    JumpTimingBias = 1.0 - JumpTimingBias;
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[BACKTRACK_OVERRIDE] stage=3: flipped bias to {JumpTimingBias:F2}");
#endif
                    // Fall through to normal decision with flipped bias
                }
            }

            if (state.GameMode == 1) return DecideShipInput(state);
            if (state.GameMode == 2) return DecideBallInput(state, isOverrideFrame);
            if (state.GameMode != 0) return false; // only cube/ship/ball for now

            // ── Orb decision: orbs can be activated while airborne (no
            //    VelY==0 requirement), so this check must come BEFORE the
            //    airborne early-return.  Skip on backtrack override frames
            //    (let the override run instead) and during jump-suppression
            //    (backtrack wants the cube to walk past, not activate). ──
            if (!isOverrideFrame && !_btSuppressJumpUntilAirborne)
            {
                int orbSid = ScanForOrbOverlap(state, out int orbIndex);
                if (orbSid >= 0)
                {
                    // Speculatively test: activate orb (input=true) vs skip (input=false)
                    var orbState = state.Clone();
                    bool orbAlive = StepFrame(ref orbState, true, out bool orbEnd);
                    if (orbEnd) return true; // orb activation reaches level end

                    int orbSurv = 0;
                    if (orbAlive)
                    {
                        // Test multiple jump timings after orb activation,
                        // not just 0/-1.  The cube may need to ride the orb
                        // boost for a few frames before jumping.
                        int orbMaxDelay = Math.Min(15, LOOKAHEAD_HORIZON);
                        for (int od = -1; od < orbMaxDelay; od++)
                        {
                            int os = 1 + SimulateForwardWithJumpAt(orbState, od);
                            if (os > orbSurv) orbSurv = os;
                        }
                    }

                    // Skip evaluation: step one frame with input=false to
                    // actually skip the pending orb, then test what the cube
                    // can do from the post-skip state.  We can't just call
                    // SimulateForwardWithJumpAt(state, 0) because that still
                    // activates the pending orb in the initial-jump phase.
                    var skipState = state.Clone();
                    bool skipAlive = StepFrame(ref skipState, false, out bool skipEnd);
                    // Mark the orb as processed in the skip clone so the
                    // forward simulation can't secretly re-activate it.
                    skipState.ProcessedSprites.Add(orbIndex);
                    int skipSurv = 0;
                    if (skipEnd) skipSurv = LOOKAHEAD_HORIZON;
                    else if (skipAlive)
                    {
                        int skipNoJump = 1 + SimulateForwardWithJumpAt(skipState, -1);
                        int skipJump   = 1 + SimulateForwardWithJumpAt(skipState, 0);
                        skipSurv = Math.Max(skipNoJump, skipJump);
                    }

#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[DECIDE_ORB] sid=0x{orbSid:X2} orbSurv={orbSurv} skipSurv={skipSurv}");
#endif
                    if (orbSurv >= skipSurv) return true;  // activate orb
                    // Decided to skip: mark the orb as processed so it won't
                    // be re-evaluated on subsequent overlapping frames.
                    state.ProcessedSprites.Add(orbIndex);
                    return false; // skip orb
                }
            }

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

            // ── Backtrack override was already handled at the top of
            //    DecideInput (before the ship early-return).  If we reach
            //    here, the override was consumed and we fall through to
            //    normal decision logic. ──

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
                // If we had a countdown delay, decrement and fire on reaching 0
                // (matches SimulateForwardWithJumpAt where jumpFrame=N fires at f==N)
                if (_cubeHoldDelay > 0)
                {
                    _cubeHoldDelay--;
                    if (_cubeHoldDelay > 0)
                        return false;
                    // Delay just hit 0 — fall through to jump
                    return true;
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
            // Safety check: if the cube would die before the delay fires,
            // abort the delay and re-evaluate from scratch instead of
            // blindly jumping (which often arcs into hazards).
            if (_committedJumpDelay > 0)
            {
                // Abort check: simulate no-press for the remaining delay
                // frames.  If the cube dies within that window, the delay
                // is stale — discard it and fall through to fresh evaluation.
                int remainingDelay = _committedJumpDelay;
                int noPressSurvival = SimulateForwardWithJumpAt(state, -1);
                if (noPressSurvival < remainingDelay)
                {
                    _committedJumpDelay = -1; // abort stale delay
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[DECIDE_CUBE] committed delay ABORTED — no-press dies in {noPressSurvival} frames but delay has {remainingDelay} remaining, re-evaluating");
#endif
                    // Fall through to normal decision logic below
                }
                else
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
            }

            // Guard against deep recursion from chained jump evaluation
            if (_speculativeDepth >= MAX_SPECULATIVE_DEPTH) return false;

            // How long does the cube survive without ever jumping?
            _speculativeDepth++;
            List<(int x, int y)>? noPressPath = (_speculativeDepth == 1 && OnSpeculativePath != null)
                ? new List<(int x, int y)>() : null;
#if !DISABLE_DEBUG_LOGGING
            int noPressFrames = SimulateForwardWithJumpAt(state, -1, out string npDeath, out int npDX, out int npDY,
                pathPoints: noPressPath);
#else
            int noPressFrames = SimulateForwardWithJumpAt(state, -1, pathPoints: noPressPath);
#endif
            OnSpeculativePath?.Invoke(noPressPath, -1, noPressFrames, false);
            _speculativeDepth--;

            // ── Danger detection: only evaluate jumps when no-press
            //    dies within the lookahead horizon.  The bias does NOT
            //    change when we detect danger — it only changes which
            //    delay we PICK once we decide a jump is needed. ──
            // 50 frames gives room for 2 full jump arcs (~24f each)
            // before danger — essential for multi-platform sections where
            // the cube must chain-jump onto higher blocks.
            const int DETECTION_HORIZON = 50;

            // Always test jumps if a gravity portal is within the lookahead X range.
            // The portal bonus makes portal-hitting paths score higher, but only if
            // we actually evaluate them. Without this, the detection horizon skips
            // jump evaluation entirely, and portal-bonus paths are never discovered.
            bool gravPortalAhead = false;
            // Also force jump evaluation when unprocessed pads are ahead.
            // Pads provide velocity boosts needed for progression — the cube may
            // need to jump to reach an elevated pad that no-press would miss.
            bool padAhead = false;
            {
                int currentX = state.X_fixed >> 8;
                int lookaheadX = currentX + ((state.VelX_fixed >> 8) + 1) * LOOKAHEAD_HORIZON;
                foreach (var sp in allSprites)
                {
                    if (sp.AnchorX_px < currentX) continue;
                    if (sp.AnchorX_px > lookaheadX) break; // sorted by X
                    if (!state.ProcessedSprites.Contains(sp.Index))
                    {
                        int sid = sp.SpriteId;
                        if (IsGravityPortal(sid))
                        {
                            gravPortalAhead = true;
                            if (padAhead) break; // found both, stop scanning
                        }
                        else if (IsYellowPad(sid) || IsPinkPad(sid) || IsRedPad(sid) ||
                                 IsBluePad(sid) || IsGreenPad(sid))
                        {
                            padAhead = true;
                            if (gravPortalAhead) break; // found both, stop scanning
                        }
                    }
                }
            }

            if (noPressFrames >= DETECTION_HORIZON && !gravPortalAhead && !padAhead)
            {
#if !DISABLE_DEBUG_LOGGING
                PfLog($"[DECIDE_CUBE] noPressFrames={noPressFrames} >= DETECTION_HORIZON={DETECTION_HORIZON}, no danger (noPressDeath={npDeath}@X={npDX},Y={npDY})");
#endif
                return false;
            }

#if !DISABLE_DEBUG_LOGGING
            PfLog($"[DECIDE_CUBE] noPressFrames={noPressFrames} < DETECTION_HORIZON={DETECTION_HORIZON}{(gravPortalAhead ? " (gravPortalAhead)" : "")}{(padAhead ? " (padAhead)" : "")}, testing jumps (noPressDeath={npDeath}@X={npDX},Y={npDY})");
#endif

            // Test different jump timings: jump at delay 0 (now), 1, 2, ...
            int bestSurvival = noPressFrames;
            var viableDelays = new List<(int delay, int survival)>();

            int maxDelay = Math.Min(noPressFrames, 35);
            _speculativeDepth++;
#if !DISABLE_DEBUG_LOGGING
            var specDeathDetails = new List<string>();
            // If reversed gravity and first speculative test, log the full trajectory
            bool logSpecTrajectory = state.GravFlipped;
#endif
            for (int delay = 0; delay < maxDelay; delay++)
            {
                List<(int x, int y)>? specPath = (_speculativeDepth == 1 && OnSpeculativePath != null)
                    ? new List<(int x, int y)>() : null;
#if !DISABLE_DEBUG_LOGGING
                int survival = SimulateForwardWithJumpAt(state, delay, out string dr, out int dx, out int dy,
                    logSpecTrajectory && delay == 0, pathPoints: specPath);
                if (delay < 5 && survival < LOOKAHEAD_HORIZON)
                    specDeathDetails.Add($"d{delay}:s{survival}@{dr}(X={dx},Y={dy})");
#else
                int survival = SimulateForwardWithJumpAt(state, delay, pathPoints: specPath);
#endif
                OnSpeculativePath?.Invoke(specPath, delay, survival, false);
                if (survival > bestSurvival)
                    bestSurvival = survival;
                if (survival > noPressFrames)
                    viableDelays.Add((delay, survival));
            }
            _speculativeDepth--;

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
                _speculativeDepth++;
                for (int delay = 0; delay < holdMaxDelay; delay++)
                {
                    List<(int x, int y)>? specPath = (_speculativeDepth == 1 && OnSpeculativePath != null)
                        ? new List<(int x, int y)>() : null;
                    int holdSurv = SimulateForwardWithJumpAt(state, delay, holdAfterLanding: true, pathPoints: specPath);
                    OnSpeculativePath?.Invoke(specPath, delay, holdSurv, true);
                    if (holdSurv > holdBestSurvival)
                    {
                        holdBestSurvival = holdSurv;
                        holdBestDelay = delay;
                    }
                }
                _speculativeDepth--;
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
                //    BUT: only remove the early group if they DON'T survive
                //    the full horizon.  If they do, the gap just means
                //    middle delays die during the arc — early delays are
                //    still excellent paths.
                if (bestDelays.Count >= 2)
                {
                    for (int gi = 1; gi < bestDelays.Count; gi++)
                    {
                        if (bestDelays[gi].delay - bestDelays[gi - 1].delay > 5)
                        {
                            bool earlyGroupSurvivedFull = true;
                            for (int ei = 0; ei < gi; ei++)
                            {
                                if (bestDelays[ei].survival < LOOKAHEAD_HORIZON)
                                {
                                    earlyGroupSurvivedFull = false;
                                    break;
                                }
                            }
                            if (!earlyGroupSurvivedFull)
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
            // Test 1-frame survival for both options first
            _speculativeDepth++;
            var sH = state.Clone();
            var sR = state.Clone();
            bool aliveH = StepFrame(ref sH, true, out bool endH);
            bool aliveR = StepFrame(ref sR, false, out bool endR);
            _speculativeDepth--;

            // If one path reaches end-of-level, take it
            if (endH) return true;
            if (endR) return false;

            // If only one survives, pick it regardless
            if (aliveH && !aliveR) return true;
            if (!aliveH && aliveR) return false;
            if (!aliveH && !aliveR) return false; // both die

            // Both survive 1 frame — use binary tree search.
            // Explore both hold and release branches recursively at each future frame.
            // Pick whichever initial choice leads to the longest max-survival path.
            _shipTreeNodesExplored = 0;
            int survH = 1 + ShipTreeSearch(sH, SHIP_TREE_DEPTH - 1);
            // Reset node budget so release gets equal exploration opportunity
            _shipTreeNodesExplored = 0;
            int survR = 1 + ShipTreeSearch(sR, SHIP_TREE_DEPTH - 1);

            if (survH != survR)
                return survH > survR;

            // Equal survival — use velocity-aware corridor tracking as tiebreaker
            int targetY = FindCorridorCenter(ref state) + _shipCorridorBias;
            int currentY = state.Y_fixed >> 8;
            int velY = state.VelY_fixed;

            int posError = (state.GravMul > 0) ? (currentY - targetY) : (targetY - currentY);
            int velComponent = -(velY * state.GravMul);
            int pdSignal = posError - (velComponent * 2);

            return pdSignal > 0;
        }

        /// <summary>
        /// Binary tree search for ship: at each depth, try both hold and release,
        /// recurse, and return the max survival depth achievable. This replaces the
        /// greedy lookahead which used the PD controller internally and could not
        /// find paths through complex obstacle sequences.
        /// </summary>
        private int _shipTreeNodesExplored;
        private int ShipTreeSearch(SimState state, int depthRemaining)
        {
            if (depthRemaining <= 0 || _shipTreeNodesExplored >= SHIP_TREE_MAX_NODES)
                return 0;

            _shipTreeNodesExplored++;
            _speculativeDepth++; // suppress logging during speculative search

            // Try hold
            var sH = state.Clone();
            bool aliveH = StepFrame(ref sH, true, out bool endH);
            int bestH = 0;
            if (endH) bestH = depthRemaining; // reached end
            else if (aliveH) bestH = 1 + ShipTreeSearch(sH, depthRemaining - 1);

            // Early exit: if hold already achieves max depth, no need to try release
            if (bestH >= depthRemaining)
            {
                _speculativeDepth--;
                return bestH;
            }

            // Try release
            _shipTreeNodesExplored++;
            var sR = state.Clone();
            bool aliveR = StepFrame(ref sR, false, out bool endR);
            int bestR = 0;
            if (endR) bestR = depthRemaining;
            else if (aliveR) bestR = 1 + ShipTreeSearch(sR, depthRemaining - 1);

            _speculativeDepth--;
            return Math.Max(bestH, bestR);
        }

        /// <summary>
        /// Scan vertically from the ship's center to find the nearest solid ceiling above
        /// and nearest solid floor below. Also scans AHEAD horizontally (CORRIDOR_LOOK_AHEAD_TILES)
        /// to detect upcoming obstacles and proactively adjust the corridor target.
        /// Uses the tightest ceiling/floor constraint across all scanned columns, so the ship
        /// steers early to clear walls, blocks, and narrow passages ahead.
        /// Death tiles (spikes) are treated as obstacles that tighten the corridor,
        /// preventing the ship from drifting into spike rows.
        /// </summary>
        private int FindCorridorCenter(ref SimState s)
        {
            int playerX_px = s.X_fixed >> 8;
            int playerY_px = s.Y_fixed >> 8;
            int hbW = GetHitboxW(s.Mini);
            int hbH = GetHitboxH(s.Mini);
            int centerX_px = playerX_px + hbW / 2;

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
                        // Check solid first
                        var (cL, cT, cR, cB) = GetCollisionBounds(col);
                        if (cR > cL)
                        {
                            int thisCeiling = ty * TILE + cB;
                            if (thisCeiling > ceilingY) ceilingY = thisCeiling;
                            break;
                        }
                        // Death tiles act as obstacles too — tile bottom is obstacle boundary
                        if (IsDeathCollision(col))
                        {
                            int thisCeiling = ty * TILE + TILE;
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
                        // Death tiles act as obstacles — tile top is obstacle boundary
                        if (IsDeathCollision(col))
                        {
                            int thisFloor = ty * TILE;
                            if (thisFloor < floorY) floorY = thisFloor;
                            break;
                        }
                    }
                }

                // For forward columns: check ALL tile rows the hitbox spans
                // PLUS a 2-tile margin above/below for obstacles and spikes
                if (tx > startTileX)
                {
                    int scanTop = Math.Max(0, topTile - 2);
                    int scanBot = Math.Min(mapHeight - groundRowsToReserve - 1, botTile + 2);

                    for (int ty = scanTop; ty <= scanBot; ty++)
                    {
                        var col = GetTileCollision(tx, ty);
                        if (col == MetatileCollision.COL_NONE) continue;

                        bool isSolid = false;
                        bool isDeath = false;
                        int blockTop, blockBottom;

                        var (cL, cT, cR, cB) = GetCollisionBounds(col);
                        if (cR > cL)
                        {
                            isSolid = true;
                            blockTop = ty * TILE + cT;
                            blockBottom = ty * TILE + cB;
                        }
                        else if (IsDeathCollision(col))
                        {
                            isDeath = true;
                            blockTop = ty * TILE;
                            blockBottom = ty * TILE + TILE;
                        }
                        else continue;

                        // Tiles within the hitbox span → route above or below
                        if (ty >= topTile && ty <= botTile)
                        {
                            int spaceAbove = blockTop - ceilingY;
                            int spaceBelow = floorY - blockBottom;

                            if (spaceAbove >= hbH && (spaceAbove >= spaceBelow || spaceBelow < hbH))
                            {
                                if (blockTop < floorY) floorY = blockTop;
                            }
                            else
                            {
                                if (blockBottom > ceilingY) ceilingY = blockBottom;
                            }
                        }
                        // Tiles above the hitbox → tighten ceiling
                        else if (ty < topTile)
                        {
                            if (isSolid && blockBottom > ceilingY) ceilingY = blockBottom;
                            if (isDeath && blockBottom > ceilingY) ceilingY = blockBottom;
                        }
                        // Tiles below the hitbox → tighten floor
                        else if (ty > botTile)
                        {
                            if (isSolid && blockTop < floorY) floorY = blockTop;
                            if (isDeath && blockTop < floorY) floorY = blockTop;
                        }
                    }
                }
            }

            // Target: center of tightest corridor, offset so top-left Y puts center at midpoint
            return (ceilingY + floorY) / 2 - hbH / 2;
        }

        /// <summary>
        /// Returns true if the collision type is any death/spike type.
        /// </summary>
        private static bool IsDeathCollision(MetatileCollision col)
        {
            return col == MetatileCollision.COL_DEATH ||
                   col == MetatileCollision.COL_DEATH_TOP ||
                   col == MetatileCollision.COL_DEATH_BOTTOM ||
                   col == MetatileCollision.COL_DEATH_LEFT ||
                   col == MetatileCollision.COL_DEATH_RIGHT ||
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
                   col == MetatileCollision.COL_UP_LEFT_SPIKE ||
                   col == MetatileCollision.COL_UP_RIGHT_SPIKE ||
                   col == MetatileCollision.COL_UP_BOTH_SPIKES ||
                   col == MetatileCollision.COL_DOWN_LEFT_SPIKE ||
                   col == MetatileCollision.COL_DOWN_RIGHT_SPIKE ||
                   col == MetatileCollision.COL_DOWN_BOTH_SPIKES ||
                   col == MetatileCollision.COL_LEFT_SPIKE_BLOCK ||
                   col == MetatileCollision.COL_RIGHT_SPIKE_BLOCK ||
                   col == MetatileCollision.COL_BOTTOM_LEFT_SPIKE ||
                   col == MetatileCollision.COL_BOTTOM_RIGHT_SPIKE ||
                   col == MetatileCollision.COL_BOTTOM_SPIKES;
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
                // Detect corridor center at current position
                int targetY = FindCorridorCenter(ref s) + _shipCorridorBias;

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
                    // Both survive — PD controller: position + velocity damping
                    int currentY = s.Y_fixed >> 8;
                    int velY = s.VelY_fixed;
                    int posError = (s.GravMul > 0) ? (currentY - targetY) : (targetY - currentY);
                    int velComponent = -(velY * s.GravMul);
                    int pdSignal = posError - (velComponent * 2);
                    pickHold = pdSignal > 0;
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
            bool holdAfterLanding = false, List<(int x, int y)>? pathPoints = null)
        {
            return SimulateForwardWithJumpAt(state, jumpFrame, out _, out _, out _,
                false, holdAfterLanding, pathPoints);
        }

        private int SimulateForwardWithJumpAt(SimState state, int jumpFrame,
            out string deathReason, out int deathX, out int deathY,
            bool logTrajectory = false, bool holdAfterLanding = false,
            List<(int x, int y)>? pathPoints = null)
        {
            deathReason = ""; deathX = 0; deathY = 0;
            var s = state.Clone();
            pathPoints?.Add(((s.X_fixed >> 8) + 8, (s.Y_fixed >> 8) + 8));
            bool initialJumpDone = (jumpFrame < 0);
            bool chainJumps = (jumpFrame >= 0); // only chain jumps when an actual jump was requested
            bool startGravFlipped = s.GravFlipped; // track gravity portal hits
            bool isBallMode = (s.GameMode == 2); // ball flips change GravFlipped — don't confuse with portal hits
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

                    // Orbs can be activated while airborne (no VelY==0 requirement).
                    // If there's a pending orb from the previous StepFrame and we've
                    // reached/passed the jump frame, force input to activate the orb.
                    if (!input && f >= jumpFrame && jumpFrame >= 0 && s.PendingOrbIndex >= 0)
                        input = true;

                    if (input) initialJumpDone = true;
                }
                else if (chainJumps && s.GameMode == 0 && s.VelY_fixed == 0 && s.OnGround)
                {
                    // After the initial jump has landed:
                    // holdAfterLanding = always re-jump (simulates holding the button)
                    // otherwise = check short-horizon danger to decide
                    input = holdAfterLanding || QuickDangerCheck(s);
                }
                else if (chainJumps && s.GameMode == 2 && s.VelY_fixed == 0 && s.OnGround)
                {
                    // Ball mode: after the initial flip has landed, evaluate
                    // whether to flip again (danger check).
                    input = holdAfterLanding || QuickDangerCheck(s);
                }

                // Auto-activate orbs encountered after the initial action.
                // Orbs are typically required for level progression; speculative
                // paths should handle them the same way a real player would.
                // Only for action paths (jumpFrame >= 0); no-press (-1) stays passive.
                if (jumpFrame >= 0 && initialJumpDone && !input && s.PendingOrbIndex >= 0)
                    input = true;

#if !DISABLE_DEBUG_LOGGING
                if (trajLog != null)
                    trajLog.Append($"f{f}:X={s.X_fixed >> 8},Y={s.Y_fixed >> 8},V=0x{s.VelY_fixed:X},G={s.OnGround},gf={s.GravFlipped},i={input} ");
#endif

                bool alive = StepFrame(ref s, input, out bool endLevel);
                pathPoints?.Add(((s.X_fixed >> 8) + 8, (s.Y_fixed >> 8) + 8));
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
                    // these paths are structurally important for level progression.
                    // Ball mode: flips change GravFlipped every time — not a portal event.
                    int portalBonus = (!isBallMode && s.GravFlipped != startGravFlipped) ? LOOKAHEAD_HORIZON : 0;
                    return f + portalBonus;
                }
                if (endLevel) return LOOKAHEAD_HORIZON;
            }

            // Bonus for paths that went through a gravity portal
            int finalPortalBonus = (!isBallMode && s.GravFlipped != startGravFlipped) ? LOOKAHEAD_HORIZON : 0;
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

            // ── Clear pending orb from previous frame ──
            s.PendingOrbIndex = -1;
            s.PendingOrbSpriteId = -1;

            int oldX_fixed = s.X_fixed;
            int oldX_px = oldX_fixed >> 8;

            // ── STEP 1: PROCESS SPRITES at current X (sprite_collide) ──
            // NES order: sprite_collide() at OLD X → cube_movement() (Y physics)
            // → x_movement() (X advance).  Orbs, pads, gravity/speed/mini portals
            // detected at OLD X.  Game mode portals are detected AFTER Y physics
            // at NEW X (matching sim's post-physics portal loop).
            endLevel = ProcessSprites(ref s, oldX_px);
            if (endLevel) return true;

            // ── STEP 1b: ORB ACTIVATION at OLD X ──
            // NES sprite_collide detects orbs at OLD X; cube_movement's Step 0
            // (orb_check) then activates them before gravity.  This must happen
            // BEFORE X advance to match NES/simulator timing.
            if (s.PendingOrbIndex >= 0 && input)
            {
#if !DISABLE_DEBUG_LOGGING
                PfLog($"[ORB_ACTIVATE] sid=0x{s.PendingOrbSpriteId:X2} gravFlipped={s.GravFlipped} mini={s.Mini}");
#endif
                ApplyOrbSprite(ref s, s.PendingOrbSpriteId);
                bool isMultiOrb = (s.PendingOrbSpriteId == 0x7B || s.PendingOrbSpriteId == 0x7C);
                if (!isMultiOrb)
                    s.ProcessedSprites.Add(s.PendingOrbIndex);
                s.PendingOrbIndex = -1;
                s.PendingOrbSpriteId = -1;
            }

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

                // Ceiling proximity check (matches CubePhysics_Fresh.partial.cs lines 110-128):
                // After gravity, if gravity is flipped and player is moving toward ceiling,
                // check collision at Y-1 to detect boundary hits that CubeEject would miss
                // (because CheckCeiling uses strict < on the boundary: topY < colBottom fails
                // when topY == colBottom, but testing at Y-1 succeeds).
                if (s.GravFlipped)
                {
                    int hbW_chk = GetHitboxW(s.Mini);
                    int hbH_chk = GetHitboxH(s.Mini);
                    int hbOffY_chk = s.Mini ? ((0x10 - hbH_chk) >> 1) : 0;
                    int collX_chk = s.X_fixed >> 8;
                    int testY_chk = (s.Y_fixed >> 8) + hbOffY_chk - 1;
                    var (ceilHit, _) = CheckCeiling(collX_chk, testY_chk, hbW_chk, hbH_chk);
                    if (ceilHit && s.VelY_fixed < 0)
                    {
                        s.VelY_fixed = 0;
                    }
                }

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

                ShipEject(ref s, out bool shipDied);
                if (shipDied)
                    return false;

                if (CheckCenterPointDeath(ref s))
                    return false;
            }
            else if (s.GameMode == 2) // Ball mode
            {
                // Ball input: flip gravity when grounded
                // Must happen BEFORE gravity application (matching sim)
                if (input && s.VelY_fixed == 0 && s.OnGround)
                {
                    // Flip gravity
                    s.GravFlipped = !s.GravFlipped;
                    s.GravMul = s.GravFlipped ? -1 : 1;
                    // Apply switch velocity (direction based on NEW gravity)
                    // NES table_idx bit 0 = 1 means normal (down) gravity post-flip
                    // normalGravity (bit0=1) → positive vel (launch down)
                    // invertedGravity (bit0=0) → negative vel (launch up)
                    s.VelY_fixed = BallSwitchVel(s.Mini) * s.GravMul;
                    s.OnGround = false;
                    s.WasZeroedByCollision = false;
                    s.BallFlipCooldown = 2;
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[BALL_FLIP] gravFlipped={s.GravFlipped} gravMul={s.GravMul} VelY=0x{s.VelY_fixed:X} mini={s.Mini}");
#endif
                }

                // Ball gravity (same structure as cube but different constants)
                BallGravityStep(ref s);

                // Skip eject during flip cooldown (prevents oscillation)
                if (s.BallFlipCooldown > 0)
                {
                    s.BallFlipCooldown--;
                }
                else
                {
                    // Ball velocity zeroing when grounded (prevents accumulation)
                    BallVelocityZeroing(ref s);

                    // Ball eject (with 1px Y offset)
                    bool ballEjectDied = false;
                    BallEject(ref s, out ballEjectDied);
                    if (ballEjectDied)
                    {
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[BALL_EJECT_DEATH] X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
                        _lastDeathReason = "BALL_EJECT_DEATH"; _lastDeathX = s.X_fixed >> 8; _lastDeathY = s.Y_fixed >> 8;
#endif
                        return false;
                    }
                }

                if (CheckCenterPointDeath(ref s))
                {
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[CENTER_DEATH] X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
                    _lastDeathReason = "CENTER_DEATH"; _lastDeathX = s.X_fixed >> 8; _lastDeathY = s.Y_fixed >> 8;
#endif
                    return false;
                }
            }

            // ── STEP 6: POST-Y GRAVITY PORTAL CHECK at OLD X, new Y ──
            CheckGravityPortalsPostY(ref s, oldX_px);

            // ── STEP 7: FORWARD COLLISION at OLD X, post-eject Y ──
            if (s.GameMode == 0 || s.GameMode == 1 || s.GameMode == 2 || s.GameMode == 4 || s.GameMode == 8 || s.GameMode == 10)
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

            // ── STEP 8b: GAME MODE PORTAL CHECK at NEW X ──
            // The sim detects game mode portals AFTER physics + forward collision,
            // using NEW X (post-advance).  Matching sim's post-physics portal loop
            // (SimulatorWindow line ~10983).  Uses centered 15×15 hitbox and NES-style
            // (+1) overlap conversion, exactly as SpriteIntersectsPlayer does.
            CheckGameModePortalsAtNewX(ref s);

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
        //  BALL PHYSICS (matching BallPhysics_Fresh + BallEject_Fresh)
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Ball gravity: same structure as CubeGravity but with ball-specific constants.
        /// Ball gravity is lighter than cube (0x47 vs 0x6B normal, 0x57 vs 0x6F mini).
        /// </summary>
        private void BallGravityStep(ref SimState s)
        {
            // GRAV_SKIP: match CommonGravityRoutine_Fresh
            if (s.VelY_fixed == 0 && s.WasZeroedByCollision)
                return;

            if (s.VelY_fixed != 0)
                s.WasZeroedByCollision = false;

            int gravity = BallGravity(s.Mini);
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

            s.VelY_fixed += accel;
            s.Y_fixed += s.VelY_fixed;

#if !DISABLE_DEBUG_LOGGING
            PfLog($"[BALL_GRAV] velY: 0x{oldVelY:X} + 0x{accel:X} = 0x{s.VelY_fixed:X}, posY: 0x{oldY:X} ({oldY >> 8}px) -> 0x{s.Y_fixed:X} ({s.Y_fixed >> 8}px)");
#endif

            int maxY_fixed = Math.Max(0, (mapHeight * TILE - TILE)) << 8;
            if (s.Y_fixed > maxY_fixed) s.Y_fixed = maxY_fixed;
        }

        /// <summary>
        /// Ball velocity zeroing: prevents velocity accumulation when grounded.
        /// Normal gravity: if touching ceiling and moving up → zero vel.
        /// Inverted gravity (gravFlipped=true, currplayer_gravity=0xFF → NES check
        /// is on gravity==0 which is "normal" i.e. gravFlipped=false in pathfinder
        /// terms): if touching floor and moving down → zero vel.
        ///
        /// Wait — the sim code is confusing. Let me re-read the sim:
        ///   if (currplayer_gravity == 0) { // Normal gravity in NES terms
        ///       // "Inverted gravity - prevent velocity from pulling into ceiling"
        ///       CheckCollisionUp; if collided && velY < 0 → zero
        ///   } else {
        ///       // "Normal gravity - prevent velocity from pulling into ground"
        ///       CheckCollisionDown; if collided && velY > 0 → zero
        ///   }
        ///
        /// In the sim, currplayer_gravity==0 means GravFlipped=false.  But the
        /// comment says "Inverted gravity."  The sim checks ceiling for normal grav
        /// and floor for flipped grav — this catches headbonk scenarios.
        /// </summary>
        private void BallVelocityZeroing(ref SimState s)
        {
            int hbW = GetHitboxW(s.Mini);
            int hbH = GetHitboxH(s.Mini);
            int hbOffY = GetHitboxOffsetY(2, s.Mini, s.GravFlipped);
            int collX = s.X_fixed >> 8;

            if (!s.GravFlipped) // currplayer_gravity == 0
            {
                // Check ceiling collision for velocity zeroing
                int testY = (s.Y_fixed >> 8) + hbOffY - 1;
                var (collided, _) = CheckCeiling(collX, testY, hbW, hbH);
                if (collided && s.VelY_fixed < 0)
                {
                    s.VelY_fixed = 0;
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[BALL_VEL_ZERO] ceiling zeroed upward velocity");
#endif
                }
            }
            else // currplayer_gravity != 0
            {
                // Check floor collision for velocity zeroing
                int playerBottom = (s.Y_fixed >> 8) + hbOffY + hbH;
                int testHeight = 2;
                var (collided, _, _) = CheckFloor(collX, playerBottom - testHeight, hbW, testHeight);
                if (collided && s.VelY_fixed > 0)
                {
                    s.VelY_fixed = 0;
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[BALL_VEL_ZERO] floor zeroed downward velocity");
#endif
                }
            }
        }

        /// <summary>
        /// Ball eject: same idea as CubeEject but with 1px Y offset to prevent
        /// every-other-frame oscillation (matching NES ball_eject).
        /// </summary>
        private void BallEject(ref SimState s, out bool died)
        {
            died = false;

            int playerX_px = s.X_fixed >> 8;
            int playerY_px = s.Y_fixed >> 8;
            int hbW = GetHitboxW(s.Mini);
            int hbH = GetHitboxH(s.Mini);
            int hbOffY = GetHitboxOffsetY(2, s.Mini, s.GravFlipped);
            int collX = playerX_px;

            // Ball-specific: 1px Y offset prevents oscillation
            int ballYOffset = s.GravFlipped ? -1 : 1;
            int collY = playerY_px + hbOffY + ballYOffset;

            if (!s.GravFlipped) // Normal gravity
            {
                // Floor collision (landing) — only when moving down
                if (s.VelY_fixed >= 0)
                {
                    var (floorHit, floorTopY, floorSpikeDeath) = CheckFloor(collX, collY, hbW, hbH);
                    if (floorSpikeDeath)
                    {
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[BALL_EJECT] floor spike death at X={playerX_px} Y={playerY_px}");
#endif
                        died = true;
                        return;
                    }
                    if (floorHit)
                    {
                        int newY = floorTopY - hbH - hbOffY - ballYOffset;
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[BALL_EJECT] floor land Y: {playerY_px} -> {newY} (floorTop={floorTopY})");
#endif
                        s.Y_fixed = newY << 8;
                        s.VelY_fixed = 0;
                        s.WasZeroedByCollision = true;
                        s.OnGround = true;
                    }
                }
            }
            else // Inverted gravity
            {
                // Ceiling collision (landing) — only when moving up
                if (s.VelY_fixed <= 0)
                {
                    var (ceilHit, ceilBotY) = CheckCeiling(collX, collY, hbW, hbH);
                    if (ceilHit)
                    {
                        int newY = ceilBotY - hbOffY;
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[BALL_EJECT] ceiling land (inverted) Y: {playerY_px} -> {newY} (ceilBot={ceilBotY})");
#endif
                        s.Y_fixed = newY << 8;
                        s.VelY_fixed = 0;
                        s.WasZeroedByCollision = true;
                        s.OnGround = true;
                    }
                }
            }
        }

        /// <summary>
        /// Ball decision logic: decide when to flip gravity.
        /// Ball flips gravity on input when grounded.  Uses the same
        /// speculative forward-simulation approach as cube to evaluate
        /// whether flipping now vs waiting produces better survival.
        /// </summary>
        private bool DecideBallInput(SimState state, bool isOverrideFrame)
        {
            // ── Orb decision (same as cube — orbs apply to all modes) ──
            if (!isOverrideFrame && !_btSuppressJumpUntilAirborne)
            {
                int orbSid = ScanForOrbOverlap(state, out int orbIndex);
                if (orbSid >= 0)
                {
                    var orbState = state.Clone();
                    bool orbAlive = StepFrame(ref orbState, true, out bool orbEnd);
                    if (orbEnd) return true;

                    int orbSurv = 0;
                    if (orbAlive)
                    {
                        int orbMaxDelay = Math.Min(15, LOOKAHEAD_HORIZON);
                        for (int od = -1; od < orbMaxDelay; od++)
                        {
                            int os = 1 + SimulateForwardWithJumpAt(orbState, od);
                            if (os > orbSurv) orbSurv = os;
                        }
                    }

                    var skipState = state.Clone();
                    bool skipAlive = StepFrame(ref skipState, false, out bool skipEnd);
                    skipState.ProcessedSprites.Add(orbIndex);
                    int skipSurv = 0;
                    if (skipEnd) skipSurv = LOOKAHEAD_HORIZON;
                    else if (skipAlive)
                    {
                        int skipNoJump = 1 + SimulateForwardWithJumpAt(skipState, -1);
                        int skipJump   = 1 + SimulateForwardWithJumpAt(skipState, 0);
                        skipSurv = Math.Max(skipNoJump, skipJump);
                    }

#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[DECIDE_ORB_BALL] sid=0x{orbSid:X2} orbSurv={orbSurv} skipSurv={skipSurv}");
#endif
                    if (orbSurv >= skipSurv) return true;
                    state.ProcessedSprites.Add(orbIndex);
                    return false;
                }
            }

            // Committed flip delay: count down, fire when reaching 0.
            if (_committedJumpDelay > 0)
            {
                int remainingDelay = _committedJumpDelay;
                int noPressSurvival = SimulateForwardWithJumpAt(state, -1);
                if (noPressSurvival < remainingDelay)
                {
                    _committedJumpDelay = -1; // abort stale delay
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[DECIDE_BALL] committed delay ABORTED — no-press dies in {noPressSurvival} frames but delay has {remainingDelay} remaining, re-evaluating");
#endif
                    // Fall through to normal decision logic below
                }
                else
                {
                    _committedJumpDelay--;
                    if (_committedJumpDelay == 0)
                    {
                        _committedJumpDelay = -1; // consumed
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[DECIDE_BALL] committed delay fired — flipping now");
#endif
                        return true;
                    }
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[DECIDE_BALL] committed delay countdown: {_committedJumpDelay} remaining");
#endif
                    return false;
                }
            }

            // Ball can only flip when grounded (VelY == 0 and OnGround)
            if (state.VelY_fixed != 0 || !state.OnGround) return false;

            // Guard against deep recursion
            if (_speculativeDepth >= MAX_SPECULATIVE_DEPTH) return false;

            // How long does the ball survive without flipping?
            _speculativeDepth++;
            List<(int x, int y)>? noPressPath = (_speculativeDepth == 1 && OnSpeculativePath != null)
                ? new List<(int x, int y)>() : null;
            int noPressFrames = SimulateForwardWithJumpAt(state, -1, pathPoints: noPressPath);
            _speculativeDepth--;

            OnSpeculativePath?.Invoke(noPressPath, -1, noPressFrames, false);

            const int DETECTION_HORIZON = 50;

            if (noPressFrames >= DETECTION_HORIZON)
            {
#if !DISABLE_DEBUG_LOGGING
                PfLog($"[DECIDE_BALL] noPressFrames={noPressFrames} >= {DETECTION_HORIZON}, no flip needed");
#endif
                return false;
            }

#if !DISABLE_DEBUG_LOGGING
            PfLog($"[DECIDE_BALL] noPressFrames={noPressFrames} < {DETECTION_HORIZON}, testing flips");
#endif

            // Test different flip timings
            int bestSurvival = noPressFrames;
            var viableDelays = new List<(int delay, int survival)>();

            int maxDelay = Math.Min(noPressFrames, 35);
            _speculativeDepth++;
            for (int delay = 0; delay < maxDelay; delay++)
            {
                List<(int x, int y)>? specPath = (_speculativeDepth == 1 && OnSpeculativePath != null)
                    ? new List<(int x, int y)>() : null;
                int survival = SimulateForwardWithJumpAt(state, delay, pathPoints: specPath);

                OnSpeculativePath?.Invoke(specPath, delay, survival, false);

                if (survival > bestSurvival)
                    bestSurvival = survival;
                if (survival > noPressFrames)
                    viableDelays.Add((delay, survival));
            }
            _speculativeDepth--;

            if (viableDelays.Count == 0)
            {
#if !DISABLE_DEBUG_LOGGING
                PfLog($"[DECIDE_BALL] no viable flip delays found");
#endif
                return false;
            }

            // Pick timing using bias (same as cube)
            int threshold = bestSurvival - 2;
            var bestDelays = viableDelays.Where(d => d.survival >= threshold).ToList();
            if (bestDelays.Count == 0) bestDelays = viableDelays;

            int pickIndex = (int)(JumpTimingBias * (bestDelays.Count - 1));
            pickIndex = Math.Clamp(pickIndex, 0, bestDelays.Count - 1);
            int pickedDelay = bestDelays[pickIndex].delay;

#if !DISABLE_DEBUG_LOGGING
            PfLog($"[DECIDE_BALL] viable={viableDelays.Count} best=[{string.Join(",", bestDelays.Select(d => $"d{d.delay}:s{d.survival}"))}] bestSurvival={bestSurvival} picking delay={pickedDelay}");
#endif

            if (pickedDelay > 0)
            {
                _committedJumpDelay = pickedDelay;
                return false;
            }

            return true; // flip now
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
                // Ball mode: BallEject positions the ball 1px higher (ballYOffset=1)
                // so feet are 1px above the floor tile.  Without compensation,
                // the tile lookup checks the wrong row and falsely clears OnGround.
                if (s.GameMode == 2) footY_px += 1;
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

                // Game mode portals are detected AFTER Y physics at NEW X
                // (matching sim's post-physics portal loop at line ~10983).
                // Skip them here; they are handled by CheckGameModePortalsAtNewX.
                if (IsGameModePortal(sid)) continue;

                if (IsSpeedPortal(sid) || IsGravityPortal(sid) ||
                    IsMiniGrowthPortal(sid) || IsEndLevel(sid))
                {
                    bool xOverlap = !((playerRight) < sp.HitLeft || sp.HitRight < currentX_px);
                    bool yOverlap = !((playerBottom) < sp.HitTop || sp.HitBottom < playerTop);

                    // End-level trigger (0x0F) fires on X overlap only (full-screen height),
                    // matching the simulator's X-position-only detection.
                    bool hit = IsEndLevel(sid) ? xOverlap : (xOverlap && yOverlap);

#if !DISABLE_DEBUG_LOGGING
                    if (IsGravityPortal(sid) || IsEndLevel(sid))
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
                // Pads fire EVERY overlapping frame (famidash has activation tracking commented out).
                // Do NOT add to ProcessedSprites — must match sim behavior.
                if (IsYellowPad(sid) || IsPinkPad(sid) || IsRedPad(sid) || IsBluePad(sid) || IsGreenPad(sid))
                {
                    // Sim's CheckPadCollision uses (playerX_fixed >> 8) + 1 for yellow/pink/red/green pads,
                    // matching NES sprite_collide() where Generic.x = high_byte(currplayer_x) + 1.
                    // Blue pads are in a separate CheckBluePadCollision that uses plain (>> 8).
                    int padXOffset = IsBluePad(sid) ? 0 : 1;
                    int padLeft = currentX_px + padXOffset;
                    int padRight = padLeft + hbW;  // exclusive right
                    bool xOverlap = !((padRight) < sp.HitLeft || sp.HitRight < padLeft);
                    bool yOverlap = !((playerBottom) < sp.HitTop || sp.HitBottom < playerTop);

                    if (xOverlap && yOverlap)
                    {
                        ApplyPadSprite(ref s, sid);
                    }
                    continue;
                }

                // Orb detection: orbs require player input to activate.
                // When overlapping, store as pending — the decision logic or
                // StepFrame will decide whether to activate.
                if (IsOrbSprite(sid))
                {
                    bool xOverlap = !((playerRight) < sp.HitLeft || sp.HitRight < currentX_px);
                    bool yOverlap = !((playerBottom) < sp.HitTop || sp.HitBottom < playerTop);

                    if (xOverlap && yOverlap)
                    {
                        s.PendingOrbIndex = sp.Index;
                        s.PendingOrbSpriteId = sid;
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[ORB_PENDING] sid=0x{sid:X2} idx={sp.Index} playerBox=({currentX_px},{playerTop})-({playerRight},{playerBottom}) spriteBox=({sp.HitLeft},{sp.HitTop})-({sp.HitRight},{sp.HitBottom})");
#endif
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
        /// Post-physics game mode portal check at NEW X.
        /// Matches the simulator's post-physics portal loop (SimulatorWindow ~line 10983)
        /// which detects game mode portals AFTER Y physics, at the post-advance X position.
        /// Uses a centered 15×15 hitbox and NES-style (+1) overlap conversion matching
        /// SpriteIntersectsPlayer (line 1136): (playerRight+1) &lt; spriteLeft.
        /// </summary>
        private void CheckGameModePortalsAtNewX(ref SimState s)
        {
            int playerX_px = s.X_fixed >> 8;
            const int HITBOX_W = 15;
            const int HITBOX_H = 15;
            // Centered hitbox matching sim: center = playerX + visualWidth/2 (8),
            // left = center - 7, right = left + 14
            int playerCenter = playerX_px + 8;       // playerVisualWidth / 2 = 8
            int playerLeft = playerCenter - (HITBOX_W / 2);  // center - 7
            int playerRight = playerLeft + (HITBOX_W - 1);   // inclusive right (left + 14)
            int playerTop = s.Y_fixed >> 8;
            int playerBottom = playerTop + (HITBOX_H - 1);   // inclusive bottom

            foreach (var sp in allSprites)
            {
                if (s.ProcessedSprites.Contains(sp.Index)) continue;
                if (sp.HitRight <= playerLeft) continue;
                if (sp.AnchorX_px - TILE > playerRight + TILE) break;

                int sid = sp.SpriteId;
                if (!IsGameModePortal(sid)) continue;

                // NES-style overlap: convert inclusive player bounds to exclusive with +1,
                // matching SpriteIntersectsPlayer's check:
                //   !((playerRight+1) < spriteLeft || spriteRight < playerLeft ||
                //     (playerBottom+1) < spriteTop || spriteBottom < playerTop)
                // Sprite HitRight/HitBottom are already exclusive (left + width).
                bool xOverlap = !((playerRight + 1) < sp.HitLeft || sp.HitRight < playerLeft);
                bool yOverlap = !((playerBottom + 1) < sp.HitTop || sp.HitBottom < playerTop);
                bool hit = xOverlap && yOverlap;

#if !DISABLE_DEBUG_LOGGING
                PfLog($"[GAMEMODE_NEWX_CHECK] sid=0x{sid:X2} idx={sp.Index} playerBox=({playerLeft},{playerTop})-({playerRight},{playerBottom}) spriteBox=({sp.HitLeft},{sp.HitTop})-({sp.HitRight},{sp.HitBottom}) xOvlp={xOverlap} yOvlp={yOverlap} hit={hit}");
#endif

                if (hit)
                {
                    bool applied = ApplyPortalSprite(ref s, sid);
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[GAMEMODE_NEWX_HIT] sid=0x{sid:X2} applied={applied} mode={s.GameMode} VelY=0x{s.VelY_fixed:X}");
#endif
                    if (applied)
                        s.ProcessedSprites.Add(sp.Index);
                    break; // only one game mode portal per frame (matching sim's break)
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

        private void ApplyPadSprite(ref SimState s, int sid)
        {
#if !DISABLE_DEBUG_LOGGING
            PfLog($"[PAD] sid=0x{sid:X2} gravFlipped={s.GravFlipped} mode={s.GameMode}");
#endif
            // Sim applies: baseVel * (gravInverted ? 1 : -1)  →  launch against gravity
            int gravSign = s.GravFlipped ? 1 : -1;

            if (IsYellowPad(sid))
            {
                s.VelY_fixed = GetPadOrbVel(1, s.Mini, s.GameMode) * gravSign;
                s.WasZeroedByCollision = false;
                s.OnGround = false;
            }
            else if (IsPinkPad(sid))
            {
                s.VelY_fixed = GetPadOrbVel(3, s.Mini, s.GameMode) * gravSign;
                s.WasZeroedByCollision = false;
                s.OnGround = false;
            }
            else if (IsRedPad(sid))
            {
                s.VelY_fixed = GetPadOrbVel(8, s.Mini, s.GameMode) * gravSign;
                s.WasZeroedByCollision = false;
                s.OnGround = false;
            }
            else if (IsBluePad(sid))
            {
                bool isBottomPad = (sid == 0x0D || sid == 0xFD);
                s.GravFlipped = isBottomPad;
                s.GravMul = isBottomPad ? -1 : 1;
                int blueGravSign = s.GravFlipped ? 1 : -1;
                s.VelY_fixed = GetPadOrbVel(0, s.Mini, s.GameMode) * blueGravSign;
                s.WasZeroedByCollision = false;
                s.OnGround = false;
            }
            else if (IsGreenPad(sid))
            {
                s.GravFlipped = !s.GravFlipped;
                s.GravMul = s.GravFlipped ? -1 : 1;
                int greenGravSign = s.GravFlipped ? 1 : -1;
                s.VelY_fixed = GetPadOrbVel(0, s.Mini, s.GameMode) * greenGravSign;
                s.WasZeroedByCollision = false;
                s.OnGround = false;
            }
        }

        /// <summary>
        /// Apply orb physics when the player activates an orb (input while overlapping).
        /// Orb velocities use positive magnitudes; the sign is determined by GravMul.
        ///   "Against gravity" = -vel * GravMul  (yellow, pink, red, yellowBigger, yellowSmaller)
        ///   "With gravity"    =  vel * GravMul  (black orb)
        ///   Gravity flip orbs flip first, then apply velocity in new frame.
        /// </summary>
        private void ApplyOrbSprite(ref SimState s, int sid)
        {
#if !DISABLE_DEBUG_LOGGING
            PfLog($"[ORB_ACTIVATE] sid=0x{sid:X2} gravFlipped={s.GravFlipped} mini={s.Mini}");
#endif
            // Sim applies: baseVel * (gravInverted ? 1 : -1)  →  launch against gravity
            int orbGravSign = s.GravFlipped ? 1 : -1;

            if (IsYellowOrb(sid))
            {
                s.VelY_fixed = GetPadOrbVel(0, s.Mini, s.GameMode) * orbGravSign;
                s.WasZeroedByCollision = false;
                s.OnGround = false;
            }
            else if (IsYellowOrbBigger(sid))
            {
                s.VelY_fixed = GetPadOrbVel(5, s.Mini, s.GameMode) * orbGravSign;
                s.WasZeroedByCollision = false;
                s.OnGround = false;
            }
            else if (IsYellowOrbSmaller(sid))
            {
                s.VelY_fixed = GetPadOrbVel(7, s.Mini, s.GameMode) * orbGravSign;
                s.WasZeroedByCollision = false;
                s.OnGround = false;
            }
            else if (IsPinkOrb(sid))
            {
                s.VelY_fixed = GetPadOrbVel(2, s.Mini, s.GameMode) * orbGravSign;
                s.WasZeroedByCollision = false;
                s.OnGround = false;
            }
            else if (IsRedOrb(sid))
            {
                s.VelY_fixed = GetPadOrbVel(4, s.Mini, s.GameMode) * orbGravSign;
                s.WasZeroedByCollision = false;
                s.OnGround = false;
            }
            else if (IsBlackOrb(sid))
            {
                // Black orb launches WITH gravity (downward when normal)
                // Sim: baseVel is already negative in table, * (gravInverted ? 1 : -1)
                // For normal gravity: negative baseVel * -1 = positive (downward) ✓
                s.VelY_fixed = GetPadOrbVel(6, s.Mini, s.GameMode) * orbGravSign;
                s.WasZeroedByCollision = false;
                s.OnGround = false;
            }
            else if (IsBlueOrb(sid))
            {
                // Flip gravity first, then launch TOWARD new ground
                s.GravFlipped = !s.GravFlipped;
                s.GravMul = s.GravFlipped ? -1 : 1;
                // Sim uses hardcoded constants (NOT PadOrbHeights):
                //   PAD_HEIGHT_BLUE_normal = -0x3A0, PAD_HEIGHT_BLUE_mini = -0x160
                // Sign: if (!gravInverted) negate → launches toward new ground
                int blueVel = s.Mini ? -0x160 : -0x3A0;
                if (!s.GravFlipped)
                    blueVel = -blueVel;
                s.VelY_fixed = blueVel;
                s.WasZeroedByCollision = false;
                s.OnGround = false;
            }
            else if (IsGreenOrb(sid))
            {
                // Flip gravity, then bounce AGAINST new gravity (yellow-orb-strength)
                s.GravFlipped = !s.GravFlipped;
                s.GravMul = s.GravFlipped ? -1 : 1;
                int greenOrbGravSign = s.GravFlipped ? 1 : -1;
                s.VelY_fixed = GetPadOrbVel(0, s.Mini, s.GameMode) * greenOrbGravSign;
                s.WasZeroedByCollision = false;
                s.OnGround = false;
            }
            else if (IsWhiteOrb(sid))
            {
                // White orb: zero Y velocity
                s.VelY_fixed = 0;
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
            // NES bg_coll_D checks at playerX+width (one pixel past inclusive right edge),
            // which can reach the next tile column. Match NES by using collW not collW-1.
            int playerRight_px = collX + collW;

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
            // NES bg_coll_U also checks at playerX+width; match with collW not collW-1.
            int tileRightX = (collX + collW) / TILE;
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

                if ((collX + collW) >= colLeft_px && collX < colRight_px)
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
