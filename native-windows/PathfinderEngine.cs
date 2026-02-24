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
    ///   6. Restore NEW X
    ///   7. CheckDeathCollision at NEW X (4 hitbox corners + right-center, matching sim)
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
        private const int MAX_FRAMES = 60 * 60 * 8; // 8 minutes at 60fps (enough for ~4600-tile levels at 1x speed)
        private const int MAX_BACKTRACK_ATTEMPTS = 200; // max backtrack retries PER death point (reset after each success)
        private const int MAX_TOTAL_BACKTRACK_ATTEMPTS = 3000; // absolute cap on total backtracks across all deaths
        private const int MAX_TOTAL_ITERATIONS = MAX_FRAMES * 10; // hard cap on total frame iterations (including replays)
        private const int MAX_CHECKPOINT_DEPTH = 120;   // max saved decision checkpoints (deep history for long levels)
        // No rewind limit — backtracker goes as far back as needed
        private const int MIN_CHECKPOINT_SPACING = 8;   // minimum frames between consecutive checkpoints
        private const int ELEV_THRESHOLD = 12;              // <1 tile — elevation difference to trigger exploration

        /// <summary>
        /// Jump timing bias: 0.0 = earliest viable jump, 0.5 = middle (default), 1.0 = latest viable jump.
        /// </summary>
        public double JumpTimingBias { get; set; } = 0.5;

        /// <summary>
        /// Optional progress reporter. Reports 0-100 (percentage of level length explored).
        /// </summary>
        public IProgress<int>? Progress { get; set; }

        /// <summary>
        /// When true, the pathfinder attempts to collect coins along the path.
        /// </summary>
        public bool PreferCoins { get; set; } = false;

        /// <summary>
        /// Number of coins collected by the final path.
        /// </summary>
        public int CoinsCollected { get; private set; } = 0;

        /// <summary>
        /// Indices of coins collected in the final path.
        /// </summary>
        public HashSet<int>? FinalCollectedCoinIndices { get; private set; }

        /// <summary>
        /// Set to true to request cancellation from UI thread.
        /// </summary>
        public volatile bool CancelRequested;

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
        private static int BallMaxFallSpeed(bool mini) => 0x600; // BALL_MAX_FALLSPEED — always 0x600 regardless of level maxFallSpeed

        // Ship physics constants (from GameModePhysics.cs, 60fps values)
        private static int ShipGravityBase(bool mini) => mini ? 0x31 : 0x2A;
        private static int ShipGravityAfterHold(bool mini) => mini ? 0x3B : 0x32;
        private static int ShipGravityHoldFall(bool mini) => mini ? 0x3E : 0x34;
        private static int ShipGravity(bool mini) => mini ? 0x27 : 0x22; // SHIP_GRAVITY at 60fps
        private static int ShipMaxFallSpeed(bool mini) => mini ? 0x0357 : 0x02D7;   // SHIP_MAX_FALLSPEED at 60fps
        private static int ShipMaxFallSpeedHold(bool mini) => mini ? 0x042D : 0x038D; // SHIP_MAX_FALLSPEED_HOLD at 60fps

        // UFO physics constants (from GameModePhysics.cs)
        private static int UfoGravity(bool mini) => 0x32; // UFO_GRAVITY — same for normal and mini
        private static int UfoJumpVel(bool mini) => mini ? 0x2D0 : 0x330; // UFO_JUMP_VEL magnitude (applied * gravMul)
        private static int UfoMaxFallSpeed(bool mini) => mini ? 0x350 : 0x320; // UFO_MAX_FALLSPEED

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
            public int GameMode;        // game mode at checkpoint (prevents cross-mode backtracking)
            public int ShipForceHold;   // _shipForceHoldFrames at checkpoint
            public int ShipForceRelease; // _shipForceReleaseFrames at checkpoint
            public int ShipCommitFrames; // _shipCommitFrames at checkpoint
            public bool ShipCommitHold;  // _shipCommitHold at checkpoint
            public int ForceJumpRemaining; // _btForceJumpFramesRemaining at checkpoint
        }
        private List<BacktrackCheckpoint> _backtrackCheckpoints = new();
        private int _backtrackAttempts;
        private int _totalBacktrackAttempts;
        private int _btOverrideFrame = -1;  // frame at which to apply override
        private int _btOverrideStage = 0;   // which alternative to try
        private int _btOverrideDistFromDeath; // frames between override checkpoint and death (for adaptive forcing)
        private bool _backtrackActive;      // true while replaying from a checkpoint (suppress new checkpoints)
        private bool _btSuppressJumpUntilAirborne; // stage-1 no-jump persists until cube falls off edge
        private int _btForceJumpFramesRemaining;  // when >0, force jump at every grounded frame (decrements per landing)
        private int _btDeathFrame;           // frame of the death that triggered backtracking
        private int _shipCorridorBias;       // sustained Y offset for ship corridor target during backtrack
        private int _shipForceHoldFrames;    // when >0, force hold input for this many frames
        private int _shipForceReleaseFrames; // when >0, force release input for this many frames
        private int _shipCommitFrames;       // remaining frames of current hold/release commitment
        private bool _shipCommitHold;        // true = committed to hold, false = committed to release

        // ── Grounded walk counters (for periodic eval triggers) ───────
        private int _cubeGroundedWalkFrames;  // consecutive grounded frames without jumping (cube)
        private int _ballGroundedWalkFrames;  // consecutive grounded frames without flipping (ball)
        private int _lastSpecMinLandY;        // min Y where VelY==0 during last speculative sim (surface elevation)

        // ── Best-so-far path tracking (for partial results on fail/cancel) ──
        private int _bestPathHighWaterX;      // highest X (pixels) reached by any attempted path
        private List<(int x, int y)> _bestPathPoints = new();  // snapshot of PathPoints at best X
        private List<bool> _bestInputs = new();                 // snapshot of Inputs at best X

        // ── Backtrack timing ──
        private System.Diagnostics.Stopwatch? _backtrackTimer;
        private const int MAX_BACKTRACK_SECONDS = 60; // hard time limit on backtracking per death point

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

        // ── Frame trace for diagnostics ─────────────────────────────────
        // Writes a CSV to %TEMP%\famidash_pf_trace.csv on every frame of the
        // main run to enable comparing with NES emulator / simulator output.
#if !DISABLE_DEBUG_LOGGING
        private readonly string _frameTracePath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "famidash_pf_trace.csv");
        private System.IO.StreamWriter? _traceWriter;

        private void TraceFrameOpen()
        {
            try
            {
                _traceWriter = new System.IO.StreamWriter(_frameTracePath, false);
                _traceWriter.WriteLine("frame,X_fixed,Y_fixed,VelY_fixed,input,alive,X_px,Y_px,onGround");
            }
            catch { _traceWriter = null; }
        }

        private void TraceFrame(int frame, ref SimState s, bool input, bool alive)
        {
            if (_traceWriter == null || _speculativeDepth > 0) return;
            try
            {
                _traceWriter.WriteLine($"{frame},0x{s.X_fixed:X},0x{s.Y_fixed:X},0x{s.VelY_fixed:X},{(input ? 1 : 0)},{(alive ? 1 : 0)},{s.X_fixed >> 8},{s.Y_fixed >> 8},{(s.OnGround ? 1 : 0)}");
            }
            catch { }
        }

        private void TraceFrameClose()
        {
            try { _traceWriter?.Flush(); _traceWriter?.Dispose(); _traceWriter = null; }
            catch { }
        }

        /// <summary>Path to the per-frame trace CSV.</summary>
        public string FrameTracePath => _frameTracePath;
#else
        // Trace disabled in production builds — no-op stubs
        private void TraceFrameOpen() { }
        private void TraceFrame(int frame, ref SimState s, bool input, bool alive) { }
        private void TraceFrameClose() { }
        public string FrameTracePath => string.Empty;
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

        // Death reason tracking (used by backtracker and result messages)
        private string _lastDeathReason = "";
        private int _lastDeathX, _lastDeathY;

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

        /// <summary>
        /// Replay the level with a given input sequence and return whether it completes.
        /// Used for click optimization — verify modified inputs still work.
        /// </summary>
        private bool ReplayInputs(List<bool> inputs, int startX_px, int startY_px,
            int startSpeedUiIndex, int startGameMode, bool startGravFlipped, bool startMini,
            out List<(int x, int y)> pathPoints)
        {
            pathPoints = new List<(int x, int y)>();
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

            for (int f = 0; f < inputs.Count; f++)
            {
                bool alive = StepFrame(ref state, inputs[f], out bool endLevel);
                int pathMiniOffY = (state.Mini && !state.GravFlipped) ? 4 : 0;
                pathPoints.Add(((state.X_fixed >> 8) + 8, (state.Y_fixed >> 8) + pathMiniOffY + 8));
                if (!alive) return false;
                if (endLevel) return true;
            }
            return false; // ran out of inputs without ending level
        }

        /// <summary>
        /// Optimize the input sequence after a successful pathfinder run.
        /// mode: 1 = least clicks, 2 = most clicks, 3 = just the clicks needed.
        /// </summary>
        public void OptimizeClicks(int mode, int startX_px, int startY_px,
            int startSpeedUiIndex, int startGameMode, bool startGravFlipped, bool startMini)
        {
            if (!Success || Inputs == null || Inputs.Count == 0) return;
            var originalInputs = new List<bool>(Inputs);
            int originalClicks = CountClicks(originalInputs);

            if (mode == 1) // Least clicks — remove unnecessary presses
            {
                // Try removing each press-on event and see if the path still completes.
                // Work backwards so removing early inputs doesn't shift indices.
                var candidate = new List<bool>(originalInputs);
                bool improved = true;
                while (improved)
                {
                    improved = false;
                    for (int i = candidate.Count - 1; i >= 0; i--)
                    {
                        if (!candidate[i]) continue; // already false
                        candidate[i] = false;
                        if (ReplayInputs(candidate, startX_px, startY_px, startSpeedUiIndex,
                            startGameMode, startGravFlipped, startMini, out var newPath))
                        {
                            improved = true; // successfully removed a press
                        }
                        else
                        {
                            candidate[i] = true; // restore — this press was needed
                        }
                    }
                }
                int newClicks = CountClicks(candidate);
                if (newClicks < originalClicks)
                {
                    Inputs = candidate;
                    ReplayInputs(Inputs, startX_px, startY_px, startSpeedUiIndex,
                        startGameMode, startGravFlipped, startMini, out var finalPath);
                    PathPoints = finalPath;
                    ResultMessage += $" [optimized: {originalClicks}→{newClicks} clicks]";
                }
            }
            else if (mode == 2) // Most clicks — add presses where safe
            {
                var candidate = new List<bool>(originalInputs);
                for (int i = 0; i < candidate.Count; i++)
                {
                    if (candidate[i]) continue; // already pressing
                    candidate[i] = true;
                    if (!ReplayInputs(candidate, startX_px, startY_px, startSpeedUiIndex,
                        startGameMode, startGravFlipped, startMini, out _))
                    {
                        candidate[i] = false; // revert — this press broke the path
                    }
                }
                int newClicks = CountClicks(candidate);
                if (newClicks > originalClicks)
                {
                    Inputs = candidate;
                    ReplayInputs(Inputs, startX_px, startY_px, startSpeedUiIndex,
                        startGameMode, startGravFlipped, startMini, out var finalPath);
                    PathPoints = finalPath;
                    ResultMessage += $" [swag: {originalClicks}→{newClicks} clicks]";
                }
            }
            else if (mode == 3) // Just the clicks needed — remove unnecessary, keep essential
            {
                // Same as least clicks but also try converting held presses to single taps.
                // First pass: remove unnecessary presses (same as mode 1)
                var candidate = new List<bool>(originalInputs);
                bool improved = true;
                while (improved)
                {
                    improved = false;
                    for (int i = candidate.Count - 1; i >= 0; i--)
                    {
                        if (!candidate[i]) continue;
                        candidate[i] = false;
                        if (ReplayInputs(candidate, startX_px, startY_px, startSpeedUiIndex,
                            startGameMode, startGravFlipped, startMini, out _))
                        {
                            improved = true;
                        }
                        else
                        {
                            candidate[i] = true;
                        }
                    }
                }
                // Second pass: for sequences of consecutive true frames,
                // try keeping only the first frame true (single tap).
                int idx = 0;
                while (idx < candidate.Count)
                {
                    if (!candidate[idx]) { idx++; continue; }
                    int runStart = idx;
                    while (idx < candidate.Count && candidate[idx]) idx++;
                    int runEnd = idx; // exclusive
                    if (runEnd - runStart <= 1) continue; // already a single tap
                    // Try converting to single tap
                    var backup = new List<bool>();
                    for (int j = runStart + 1; j < runEnd; j++)
                    {
                        backup.Add(candidate[j]);
                        candidate[j] = false;
                    }
                    if (!ReplayInputs(candidate, startX_px, startY_px, startSpeedUiIndex,
                        startGameMode, startGravFlipped, startMini, out _))
                    {
                        // Revert
                        for (int j = runStart + 1; j < runEnd; j++)
                            candidate[j] = backup[j - runStart - 1];
                    }
                }
                int newClicks = CountClicks(candidate);
                Inputs = candidate;
                ReplayInputs(Inputs, startX_px, startY_px, startSpeedUiIndex,
                    startGameMode, startGravFlipped, startMini, out var finalPath);
                PathPoints = finalPath;
                ResultMessage += $" [essential: {originalClicks}→{newClicks} clicks]";
            }
        }

        /// <summary>Count the number of press-on transitions (false→true).</summary>
        private static int CountClicks(List<bool> inputs)
        {
            int count = 0;
            bool prev = false;
            foreach (bool b in inputs)
            {
                if (b && !prev) count++;
                prev = b;
            }
            return count;
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
            _btOverrideDistFromDeath = 0;
            _backtrackActive = false;
            _btSuppressJumpUntilAirborne = false;
            _btDeathFrame = 0;
            _shipCorridorBias = 0;
            _shipForceHoldFrames = 0;
            _shipForceReleaseFrames = 0;
            _shipCommitFrames = 0;
            _shipCommitHold = false;
            _cubeGroundedWalkFrames = 0;
            _ballGroundedWalkFrames = 0;
            _bestPathHighWaterX = startX_px;
            _bestPathPoints.Clear();
            _bestInputs.Clear();

            _frameCounter = 0;
            _speculativeDepth = 0;
            TraceFrameOpen();
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

            // ── DIAGNOSTIC: collision map for the stuck region ── (disabled for perf)
            // {
            //     ... collision map diagnostic removed ...
            // }

            // Report progress based on X position relative to level length
            int levelLengthPx = mapWidth * TILE;
            int highWaterX_px = startX_px; // track furthest X reached for progress
            int progressInterval = Math.Max(1, MAX_FRAMES / 200); // check every 0.5% of frame budget
            int totalIterations = 0; // counts every frame step including replays

            for (int frame = 0; frame < MAX_FRAMES; frame++)
            {
                totalIterations++;
                if (CancelRequested)
                {
                    SnapshotBestPath();
                    UseBestPathIfBetter();
                    Success = false;
                    int bestX = PathPoints.Count > 0 ? PathPoints[PathPoints.Count - 1].x : 0;
                    int bestPct = levelLengthPx > 0 ? bestX * 100 / levelLengthPx : 0;
                    ResultMessage = $"Cancelled — partial path to X={bestX}px ({bestPct}%, {PathPoints.Count} points)";
                    TraceFrameClose();
                    return;
                }
                if (totalIterations > MAX_TOTAL_ITERATIONS)
                {
                    SnapshotBestPath();
                    UseBestPathIfBetter();
                    Success = false;
                    int bestXA = PathPoints.Count > 0 ? PathPoints[PathPoints.Count - 1].x : 0;
                    int bestPctA = levelLengthPx > 0 ? bestXA * 100 / levelLengthPx : 0;
                    ResultMessage = $"Aborted — partial path to X={bestXA}px ({bestPctA}%) exceeded {MAX_TOTAL_ITERATIONS} iterations";
                    TraceFrameClose();
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[ABORT] total iterations {totalIterations} exceeded cap");
#endif
                    return;
                }
                int curX = state.X_fixed >> 8;
                if (curX > highWaterX_px) highWaterX_px = curX;
                if (frame % progressInterval == 0 && levelLengthPx > 0)
                    Progress?.Report(Math.Min(99, highWaterX_px * 100 / levelLengthPx));
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
                SimState preDecisionState = (wasGrounded || isShipMode) ? state.Clone() : default;
                bool isBacktrackFrame = (_btOverrideFrame == frame);

                bool input = DecideInput(state);
                Inputs.Add(input);

                // Track consecutive grounded walk frames (for periodic eval)
                if (state.GameMode == 0 && wasGrounded && !input && _committedJumpDelay < 0)
                    _cubeGroundedWalkFrames++;
                else if (state.GameMode == 0)
                    _cubeGroundedWalkFrames = 0;

                if (state.GameMode == 2 && wasGrounded && !input && _committedJumpDelay < 0)
                    _ballGroundedWalkFrames++;
                else if (state.GameMode == 2)
                    _ballGroundedWalkFrames = 0;

                // Once the replay path advances past the frame where the
                // original death occurred, the backtrack has "succeeded"
                // and we can resume creating new checkpoints for future
                // obstacles.
                if (_backtrackActive && frame > _btDeathFrame)
                {
                    _backtrackActive = false;
                    _shipCorridorBias = 0; // clear ship bias once past the obstacle
                    _shipForceHoldFrames = 0;
                    _shipForceReleaseFrames = 0;
                    _shipCommitFrames = 0;
                    // Reset attempt budget after each successful backtrack.
                    // Each death point gets a fresh budget — solving early
                    // obstacles shouldn't deplete the budget for harder
                    // sections ahead.
                    _backtrackAttempts = 0;
                    _backtrackTimer = null; // reset timer for next death point
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[BACKTRACK_DONE] advanced past death frame {_btDeathFrame}, resuming normal checkpointing (attempts={_backtrackAttempts}/{MAX_BACKTRACK_ATTEMPTS})");
#endif
                }

                // Save checkpoint at grounded decisions where the cube jumps
                // OR where a committed delay is set (these are also key
                // decision points — choosing delay=17 vs delay=0 matters).
                // Don't create new checkpoints during backtrack replays — the
                // original checkpoints are the only ones we want to revisit.
                bool shouldCheckpoint = wasGrounded && (input || _committedJumpDelay > 0);

                // Ship/UFO mode periodic checkpoints: always airborne,
                // so the grounded-based checkpoint logic never fires.
                // Save a checkpoint every 12 frames so the backtracker
                // has fine-grained control points for maneuvers.
                bool isUfoMode = state.GameMode == 3;
                if (!shouldCheckpoint && (isShipMode || isUfoMode) && frame % 12 == 0)
                {
                    shouldCheckpoint = true;
                }
                // Cube mode: periodic checkpoints during long grounded walks.
                // When the cube walks without jumping, no checkpoints are created
                // (shouldCheckpoint requires input=true or committedDelay>0).
                // Without these, the backtracker has no rewind points when a
                // long walk leads to a dead end. Save a checkpoint every 24
                // walk frames so the backtracker can try jumping from mid-walk.
                if (!shouldCheckpoint && state.GameMode == 0
                    && _cubeGroundedWalkFrames > 0 && _cubeGroundedWalkFrames % 24 == 0)
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
                // Cube mode: skip checkpoint creation.  In the working
                // 8ff7eb4 release build, _frameCounter was debug-only so the
                // backtrack override check (_btOverrideFrame == _frameCounter)
                // always compared against 0 — effectively disabling all cube
                // backtracking.  The cube's forward lookahead decisions were
                // sufficient; forced backtrack alternatives (no-jump, toggle-
                // hold, opposite-bias) consistently produced WORSE paths.
                // Ship/ball/UFO still benefit from backtracking.
                // UPDATE: Cube backtracking re-enabled — the forward-only
                // approach fails at obstacles like triple spikes where the
                // cube needs to try different approaches from nearby checkpoints.
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
                        ShipBias = _shipCorridorBias,
                        GameMode = cpState.GameMode,
                        ShipForceHold = _shipForceHoldFrames,
                        ShipForceRelease = _shipForceReleaseFrames,
                        ShipCommitFrames = _shipCommitFrames,
                        ShipCommitHold = _shipCommitHold,
                        ForceJumpRemaining = _btForceJumpFramesRemaining
                    });
                }

#if !DISABLE_DEBUG_LOGGING
                PfLog($"[DECIDE] input={input}");
#endif

                bool alive = StepFrame(ref state, input, out bool endLevel);
                TraceFrame(frame, ref state, input, alive);

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
                    int deathPct = levelLengthPx > 0 ? (state.X_fixed >> 8) * 100 / levelLengthPx : 0;
                    PfLog($"[DEATH] frame={frame} X={state.X_fixed >> 8}px Y={state.Y_fixed >> 8}px pct={deathPct}% reason={_lastDeathReason} dX={_lastDeathX} dY={_lastDeathY} VelY=0x{state.VelY_fixed:X} gravFlipped={state.GravFlipped}");
#endif
                    // Snapshot the current full path if it reached further than any previous attempt
                    SnapshotBestPath();

                    // Try backtracking to a previous decision point
                    if (TryBacktrack(ref state, ref frame))
                        continue;

                    // All backtracks exhausted — use the best path we found
                    UseBestPathIfBetter();
                    Success = false;
                    int bestX = PathPoints.Count > 0 ? PathPoints[PathPoints.Count - 1].x : 0;
                    int bestPct = levelLengthPx > 0 ? bestX * 100 / levelLengthPx : 0;
                    ResultMessage = $"Died — partial path to X={bestX}px ({bestPct}%) reason={_lastDeathReason} dX={_lastDeathX} dY={_lastDeathY}";
                    System.Console.Error.WriteLine($"[PERM_DEATH] frame={frame} X={state.X_fixed >> 8} Y={state.Y_fixed >> 8} reason={_lastDeathReason} dX={_lastDeathX} dY={_lastDeathY}");
                    TraceFrameClose();
                    return;
                }
                if (endLevel)
                {
                    // Path point already recorded above after StepFrame
                    Success = true;
                    ResultMessage = $"Completed in {frame} frames ({PathPoints.Count} path points)";
                    TraceFrameClose();
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[END_LEVEL] frame={frame}");
#endif
                    return;
                }
            }

            SnapshotBestPath();
            UseBestPathIfBetter();
            Success = false;
            {
                int bestXT = PathPoints.Count > 0 ? PathPoints[PathPoints.Count - 1].x : 0;
                int bestPctT = levelLengthPx > 0 ? bestXT * 100 / levelLengthPx : 0;
                ResultMessage = $"Timeout — partial path to X={bestXT}px ({bestPctT}%)";
            }
            TraceFrameClose();
#if !DISABLE_DEBUG_LOGGING
            PfLog($"[TIMEOUT]");
#endif
        }

        // ═══════════════════════════════════════════════════════════════════
        //  BEST-PATH TRACKING (for partial results on fail/cancel/timeout)
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// If the current PathPoints extends further (higher X) than the
        /// previous best, save a copy. Called before every backtrack rewind
        /// and before giving up.
        /// </summary>
        private void SnapshotBestPath()
        {
            if (_speculativeDepth > 0) return;
            if (PathPoints.Count == 0) return;
            var last = PathPoints[PathPoints.Count - 1];
            if (last.x > _bestPathHighWaterX)
            {
                _bestPathHighWaterX = last.x;
                _bestPathPoints = new List<(int x, int y)>(PathPoints);
                _bestInputs = new List<bool>(Inputs);
            }
        }

        /// <summary>
        /// Replace PathPoints/Inputs with the best-so-far snapshot if it
        /// reached further than the current (post-backtrack / post-death)
        /// path. This ensures that fail/cancel/timeout always returns the
        /// path that got the furthest.
        /// </summary>
        private void UseBestPathIfBetter()
        {
            if (_bestPathPoints.Count == 0) return;
            int currentMaxX = PathPoints.Count > 0 ? PathPoints[PathPoints.Count - 1].x : 0;
            if (_bestPathHighWaterX > currentMaxX)
            {
                PathPoints.Clear();
                PathPoints.AddRange(_bestPathPoints);
                Inputs.Clear();
                Inputs.AddRange(_bestInputs);
            }
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
                _backtrackTimer = System.Diagnostics.Stopwatch.StartNew();
            }
            _backtrackActive = true;
            int deathGameMode = state.GameMode; // capture death mode before any restoration

            while (_backtrackCheckpoints.Count > 0 &&
                   _backtrackAttempts < MAX_BACKTRACK_ATTEMPTS &&
                   _totalBacktrackAttempts < MAX_TOTAL_BACKTRACK_ATTEMPTS &&
                   (_backtrackTimer == null || _backtrackTimer.Elapsed.TotalSeconds < MAX_BACKTRACK_SECONDS))
            {
                int last = _backtrackCheckpoints.Count - 1;
                var cp = _backtrackCheckpoints[last];

                // Allow cross-mode backtracking when the checkpoint is
                // close to the death (within 500 frames) — mode transitions
                // often need the player to undo the last few decisions from
                // the prior mode.  Far-away cross-mode checkpoints are still
                // discarded to avoid replaying already-cleared sections.
                if (cp.GameMode != deathGameMode)
                {
                    int frameDist = _btDeathFrame - cp.Frame;
                    if (frameDist > 500)
                    {
                        _backtrackCheckpoints.RemoveAt(last);
                        continue;
                    }
                }

                cp.RetryStage++;

                // Ship/UFO get 4 stages (bias ±, force hold, force release).
                // Cube gets 10 stages (no-jump, toggle-hold, opposite-bias, force-jump, delays).
                // Ball gets 10 stages for deeper exploration.
                //
                // ADAPTIVE: For distant checkpoints (far from death), use fewer
                // stages so the backtracker reaches earlier checkpoints faster.
                // Only the most impactful stages (suppress, force-jump, bias-flip)
                // are tried for distant checkpoints; delays are skipped.
                //
                // ELEVATION-AWARE: When the death was a forward wall (FWD_DEATH)
                // and the checkpoint is at a similar Y to the death Y (both at
                // floor level), cap stages to 1.  Changing the input at floor
                // level won't help clear a wall above — the backtracker must
                // reach elevated checkpoints where height matters.
                int maxStages;
                if (cp.GameMode == 1 || cp.GameMode == 3)
                    maxStages = 4;
                else if (cp.GameMode == 2)
                    maxStages = 10;
                else
                {
                    int frameDist2 = _btDeathFrame - cp.Frame;
                    int cpY = cp.State.Y_fixed >> 8;
                    bool floorLevel = _lastDeathReason == "FWD_DEATH"
                        && Math.Abs(cpY - _lastDeathY) < 48; // within 3 tiles of death Y = same floor

                    if (floorLevel)
                        maxStages = 1;   // floor-level near wall: skip fast
                    else
                        maxStages = 12;  // elevated: full exploration including force-jump windows
                }
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
                _shipForceHoldFrames = cp.ShipForceHold;
                _shipForceReleaseFrames = cp.ShipForceRelease;
                _shipCommitFrames = cp.ShipCommitFrames;
                _shipCommitHold = cp.ShipCommitHold;
                _btForceJumpFramesRemaining = cp.ForceJumpRemaining;

                // Snapshot the failed path segment before truncating (cap to prevent memory bloat)
                if (PathPoints.Count > cp.PathPointCount && AttemptedPaths.Count < 200)
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
                _btOverrideDistFromDeath = _btDeathFrame - cp.Frame;
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
                    // Ship backtrack: 4 stages per checkpoint, cycling quickly
                    // to earlier checkpoints so the ship tries preemptive
                    // trajectory changes, not just near-obstacle adjustments.
                    //   1: bias down (aim lower corridor center)
                    //   2: bias up (aim higher corridor center)
                    //   3: force hold (adaptive duration — longer at earlier checkpoints)
                    //   4: force release (adaptive duration)
                    // Bias magnitude and force duration scale with distance
                    // from death: earlier interventions need bigger adjustments.
                    int dist = _btOverrideDistFromDeath;
                    int forceDuration = Math.Max(4, dist / 3);
                    int biasAmount = Math.Max(24, Math.Min(72, dist));
                    _shipForceHoldFrames = 0;
                    _shipForceReleaseFrames = 0;
                    switch (stage)
                    {
                        case 1: _shipCorridorBias = -biasAmount; break;
                        case 2: _shipCorridorBias = biasAmount; break;
                        case 3: _shipForceHoldFrames = forceDuration; break;
                        case 4: _shipForceReleaseFrames = forceDuration; break;
                    }
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[BACKTRACK_OVERRIDE_SHIP] stage={stage} dist={dist}: bias={_shipCorridorBias} forceHold={_shipForceHoldFrames} forceRel={_shipForceReleaseFrames}");
#endif
                    // Fall through to normal DecideShipInput with new bias/force active
                    return DecideShipInput(state);
                }

                // Ball mode overrides (10 stages)
                // Ball alternates between flipping and not flipping, with various biases.
                //   1: suppress flip (walk past, no gravity flip)
                //   2: force flip immediately
                //   3: flip bias to opposite
                //   4: suppress flip + opposite bias
                //   5: force flip + opposite bias
                //   6: force flip after delay=5
                //   7: force flip after delay=10
                //   8: force flip after delay=15
                //   9: force flip after delay=20
                //  10: force flip after delay=25
                if (state.GameMode == 2)
                {
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[BACKTRACK_OVERRIDE_BALL] stage={stage}");
#endif
                    switch (stage)
                    {
                        case 1: // suppress flip
                            _btSuppressJumpUntilAirborne = true;
                            return false;
                        case 2: // force flip now
                            return true;
                        case 3: // opposite bias, fall through
                            JumpTimingBias = 1.0 - JumpTimingBias;
                            break;
                        case 4: // suppress + opposite bias
                            JumpTimingBias = 1.0 - JumpTimingBias;
                            _btSuppressJumpUntilAirborne = true;
                            return false;
                        case 5: // force flip + opposite bias
                            JumpTimingBias = 1.0 - JumpTimingBias;
                            return true;
                        case 6: // force flip after delay 5
                            _committedJumpDelay = 5;
                            return false;
                        case 7: // force flip after delay 10
                            _committedJumpDelay = 10;
                            return false;
                        case 8: // force flip after delay 15
                            _committedJumpDelay = 15;
                            return false;
                        case 9: // force flip after delay 20
                            _committedJumpDelay = 20;
                            return false;
                        case 10: // force flip after delay 25
                            _committedJumpDelay = 25;
                            return false;
                    }
                    // Fall through to normal DecideBallInput
                    return DecideBallInput(state, true);
                }

                // Cube mode overrides (12 stages)
                //   1: suppress jump (walk past)
                //   2: toggle hold-jump
                //   3: opposite bias
                //   4: force jump now
                //   5: force jump + opposite bias
                //   6: suppress + opposite bias
                //   7: force jump after delay=5
                //   8: force jump after delay=10
                //   9: force jump after delay=15
                //  10: force jump after delay=20
                //  11: force-jump window (jump at every safe landing for next 30 landings)
                //  12: force-jump window (jump at every safe landing for next 50 landings)
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
                else if (stage == 2) // Force jump now (alternate)
                {
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[BACKTRACK_OVERRIDE] stage=2: force jump now (alternate)");
#endif
                    return true;
                }
                else if (stage == 3) // Opposite bias
                {
                    double newBias = JumpTimingBias < 0.5 ? 1.0 : 0.0;
                    JumpTimingBias = newBias;
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[BACKTRACK_OVERRIDE] stage=3: flipped bias to {JumpTimingBias:F2}");
#endif
                    // Fall through to normal decision with flipped bias
                }
                else if (stage == 4) // Force jump now
                {
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[BACKTRACK_OVERRIDE] stage=4: force jump now");
#endif
                    return true;
                }
                else if (stage == 5) // Force jump + opposite bias
                {
                    JumpTimingBias = 1.0 - JumpTimingBias;
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[BACKTRACK_OVERRIDE] stage=5: force jump + flipped bias to {JumpTimingBias:F2}");
#endif
                    return true;
                }
                else if (stage == 6) // Suppress + opposite bias
                {
                    JumpTimingBias = 1.0 - JumpTimingBias;
                    _btSuppressJumpUntilAirborne = true;
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[BACKTRACK_OVERRIDE] stage=6: suppress + flipped bias to {JumpTimingBias:F2}");
#endif
                    return false;
                }
                else if (stage == 7) // Force jump after delay=5
                {
                    _committedJumpDelay = 5;
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[BACKTRACK_OVERRIDE] stage=7: committed delay=5");
#endif
                    return false;
                }
                else if (stage == 8) // Force jump after delay=10
                {
                    _committedJumpDelay = 10;
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[BACKTRACK_OVERRIDE] stage=8: committed delay=10");
#endif
                    return false;
                }
                else if (stage == 9) // Force jump after delay=15
                {
                    _committedJumpDelay = 15;
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[BACKTRACK_OVERRIDE] stage=9: committed delay=15");
#endif
                    return false;
                }
                else if (stage == 10) // Force jump after delay=20
                {
                    _committedJumpDelay = 20;
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[BACKTRACK_OVERRIDE] stage=10: committed delay=20");
#endif
                    return false;
                }
                else if (stage == 11) // Force-jump window: 30 grounded jumps
                {
                    _btForceJumpFramesRemaining = 30;
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[BACKTRACK_OVERRIDE] stage=11: force-jump window=8 landings");
#endif
                    return true; // also jump NOW
                }
                else if (stage == 12) // Force-jump window: 50 grounded jumps
                {
                    _btForceJumpFramesRemaining = 50;
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[BACKTRACK_OVERRIDE] stage=12: force-jump window=16 landings");
#endif
                    return true; // also jump NOW
                }
            }

            if (state.GameMode == 1) return DecideShipInput(state);
            if (state.GameMode == 2) return DecideBallInput(state, isOverrideFrame);
            if (state.GameMode == 3) return DecideUfoInput(state, isOverrideFrame);
            if (state.GameMode != 0) return false; // only cube/ship/ball/ufo for now

            // NOTE: Orb handling for cube is intentionally omitted here.
            // In 8ff7eb4, the cube had no orb decision logic — orbs were
            // handled purely through the speculative simulation's
            // PendingOrbIndex auto-activation in SimulateForwardWithJumpAt.
            // Adding explicit orb decision pre-empts normal jump evaluation
            // and can cause worse outcomes.  Ball/UFO orb handling is in
            // their respective DecideXxxInput methods.

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
                    // Safety check: if walking forward without jumping leads to
                    // death within a few frames, abandon the suppression and let
                    // the cube jump to survive.
                    if (QuickDangerCheck(state))
                    {
                        _btSuppressJumpUntilAirborne = false;
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[BACKTRACK_OVERRIDE] no-jump suppression OVERRIDDEN — danger ahead, allowing jump");
#endif
                        // Fall through to normal decision logic instead of returning false
                    }
                    else
                    {
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[BACKTRACK_OVERRIDE] no-jump suppression active (grounded)");
#endif
                        return false; // don't jump — wait to fall off edge
                    }
                }
            }

            // ── Backtrack force-jump window: when active, jump at every
            //    grounded opportunity UNLESS jumping would immediately die.
            //    This overrides the BFS to chain-jump through maze-like
            //    platforming sections where the BFS can't see far enough
            //    to plan a global path. ──
            if (_btForceJumpFramesRemaining > 0 && _speculativeDepth == 0)
            {
                bool isGrounded = state.VelY_fixed == 0 && state.OnGround;
                if (isGrounded)
                {
                    // Safety check: will the jump survive at least a few frames?
                    var testState = state.Clone();
                    _speculativeDepth++;
                    bool jumpOk = StepFrame(ref testState, true, out _);
                    if (jumpOk) jumpOk = StepFrame(ref testState, false, out _);
                    if (jumpOk) jumpOk = StepFrame(ref testState, false, out _);
                    _speculativeDepth--;

                    _btForceJumpFramesRemaining--;
                    if (jumpOk)
                    {
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[BACKTRACK_FORCE_JUMP] forcing grounded jump, remaining={_btForceJumpFramesRemaining}");
#endif
                        return true;
                    }
                    else
                    {
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[BACKTRACK_FORCE_JUMP] jump unsafe, walking instead, remaining={_btForceJumpFramesRemaining}");
#endif
                        return false; // walk — jump would die
                    }
                }
                // If airborne, don't decrement — just fall through to normal logic
            }

            // ── Backtrack override was already handled at the top of
            //    DecideInput (before the ship early-return).  If we reach
            //    here, the override was consumed and we fall through to
            //    normal decision logic. ──

            // Can only jump when velY == 0 (matching real game's jump check)
            if (state.VelY_fixed != 0)
            {
                // Airborne: decrement committed delay (backtrack overrides only)
                if (_committedJumpDelay > 0)
                    _committedJumpDelay--;

                // ── LANDING-FRAME JUMP (NES MATCH) ──
                // On the NES, holding A provides input=true EVERY frame,
                // including the frame the player lands.  In cube_movement:
                //   common_gravity_routine → cube_eject (VelY→0) → cube_do_jump.
                // If A is held and VelY==0 after eject, cube_do_jump fires
                // on the SAME frame as landing — zero wasted frames.
                //
                // The PF decides input BEFORE StepFrame runs, so when VelY>0
                // (falling, pre-eject), it used to return false — causing the
                // jump to fire 1 frame LATE.  Over many jumps, this ~3px/jump
                // drift shifts the pixel alignment at tight spike sections.
                //
                // Fix: predict whether landing will occur this frame.  If so,
                // fall through to the hold-jump fast path / BFS to decide
                // whether an immediate landing-frame jump is beneficial.
                if (state.GameMode == 0 && CubeWillLandThisFrame(state))
                {
                    // Fall through to BFS / hold-jump evaluation
                }
                else
                {
                    return false;
                }
            }

            // Committed jump delay from backtrack overrides: count down, fire when reaching 0.
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
            if (_committedJumpDelay == 0)
            {
                _committedJumpDelay = -1; // consumed (reached 0 while airborne)
#if !DISABLE_DEBUG_LOGGING
                PfLog($"[DECIDE_CUBE] committed delay fired (was pending from airborne) — jumping now");
#endif
                return true;
            }

            // Hold-jump continuity removed: the BFS runs at every
            // grounded frame and makes optimal jump/walk decisions.
            // Locking into hold-jump prevents the BFS from adjusting
            // the bounce phase when obstacles come within view.
            _cubeHoldJump = false;

            // Guard against deep recursion from chained jump evaluation
            if (_speculativeDepth >= MAX_SPECULATIVE_DEPTH) return false;

            // ═══════════════════════════════════════════════════════════════
            //  BFS frame-by-frame pathfinding
            //  Instead of testing 35 pre-set delays and ranking them,
            //  explore every possible jump/nojump choice at every grounded
            //  frame.  This naturally finds hold-jump patterns, delayed
            //  jumps, and any combination thereof without heuristic ranking.
            // ═══════════════════════════════════════════════════════════════

            const int BFS_HORIZON_NORMAL = 120;
            const int BFS_HORIZON_ELEVATED = 600; // extended horizon to see distant walls when on platforms
            const int MAX_ALIVE_NORMAL = 256;
            const int MAX_ALIVE_ELEVATED = 128;   // needs to be large enough to find complex maze paths

            int currentYpx = state.Y_fixed >> 8;
            int floorYpx = (mapHeight - groundRowsToReserve) * 16; // world bottom in pixels
            // Elevated when player is above the floor level — whether currently
            // grounded OR about to land (landing-frame prediction fell through).
            // Threshold is 128px (8 tiles) above floor to avoid triggering
            // on normal ground-level landings (~Y=840) while still covering
            // maze platform landings (~Y=689).
            bool isElevated = currentYpx < floorYpx - 128;
            int BFS_HORIZON = isElevated ? BFS_HORIZON_ELEVATED : BFS_HORIZON_NORMAL;
            int MAX_ALIVE = isElevated ? MAX_ALIVE_ELEVATED : MAX_ALIVE_NORMAL;

            // ── HOLD-JUMP FAST PATH ──
            // Before running the full BFS, quickly simulate a "hold-jump"
            // path (always jump at every landing).  If this path survives
            // the full BFS horizon, immediately return jump.
            //
            // Rationale: precision spike sections often require the player
            // to jump at every possible frame.  The BFS re-evaluates from
            // scratch at each landing and may choose "walk" when it locally
            // survives longer within the horizon, breaking the always-jump
            // rhythm needed for frame-perfect sections.  By pre-checking
            // hold-jump, we skip the BFS entirely when it's safe, matching
            // NES behavior (player holds jump button continuously).
            {
                _speculativeDepth++;
                var holdState = state.Clone();
                int holdSurv = 0;
                bool holdAlive = true;
                string holdDeathReason = null;
                for (int f = 0; f < BFS_HORIZON && holdAlive; f++)
                {
                    // Simulate holding A continuously (NES behavior).
                    // The jump check in StepFrame requires VelY==0, so
                    // true is harmless while airborne but enables
                    // landing-frame jumps (matching NES cube_do_jump
                    // firing on the same frame as cube_eject landing).
                    bool holdInput = true;
                    holdAlive = StepFrame(ref holdState, holdInput, out bool holdEnd);
                    if (holdEnd) { holdSurv = BFS_HORIZON; break; }
                    if (holdAlive) holdSurv = f + 1;
                    else holdDeathReason = $"f={f} X={holdState.X_fixed >> 8} Y={holdState.Y_fixed >> 8}";
                }
                if (holdAlive) holdSurv = BFS_HORIZON;
                _speculativeDepth--;

                if (holdSurv >= BFS_HORIZON)
                {
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[DECIDE_CUBE] hold-jump fast path: survived full horizon, jumping immediately");
#endif
                    return true;
                }
#if !DISABLE_DEBUG_LOGGING
                PfLog($"[DECIDE_CUBE] hold-jump fast path: survived {holdSurv} frames (< {BFS_HORIZON}), falling through to BFS");
#endif
            }

#if !DISABLE_DEBUG_LOGGING
            if (isElevated)
                PfLog($"[DECIDE_CUBE] ELEVATED BFS: horizon={BFS_HORIZON} maxAlive={MAX_ALIVE} Y={currentYpx} floorY={floorYpx}");
#endif

            // Each BFS node tracks:
            //   state:          physics snapshot
            //   jumpedFrame0:   whether this lineage jumped on frame 0 (the output we need)
            //   landingsJumped: how many landings this path jumped on (for hold-jump detection)
            //   totalLandings:  how many landing opportunities this path had
            //   my:             current Y (pixels) at this state — used for terminal Y tracking
            var alive = new List<(SimState s, bool j0, int lj, int tl, int my)>();

            // Best results per frame-0 choice (survival frames, then X as tiebreaker)
            int bestF0JumpSurv = -1, bestF0JumpX = int.MinValue;
            bool bestF0JumpHoldPattern = false;
            int bestF0JumpMinY = int.MaxValue; // Y of best-scoring terminal in jump lineage
            int bestF0WalkSurv = -1, bestF0WalkX = int.MinValue;
            int bestF0WalkMinY = int.MaxValue; // Y of best-scoring terminal in walk lineage

            void RecordTerminal(bool j0, int survFrames, int xPx, int lj, int tl, int minY)
            {
                if (j0)
                {
                    if (survFrames > bestF0JumpSurv || (survFrames == bestF0JumpSurv && xPx > bestF0JumpX))
                    {
                        bestF0JumpSurv = survFrames;
                        bestF0JumpX = xPx;
                        bestF0JumpHoldPattern = (tl > 0 && lj == tl);
                        bestF0JumpMinY = minY;
                    }
                }
                else
                {
                    if (survFrames > bestF0WalkSurv || (survFrames == bestF0WalkSurv && xPx > bestF0WalkX))
                    {
                        bestF0WalkSurv = survFrames;
                        bestF0WalkX = xPx;
                        bestF0WalkMinY = minY;
                    }
                }
            }

            // Seed: two branches if on ground (jump now vs wait)
            alive.Add((state.Clone(), true, 0, 0, state.Y_fixed >> 8));
            alive.Add((state.Clone(), false, 0, 0, state.Y_fixed >> 8));

            _speculativeDepth++;

            for (int f = 0; f < BFS_HORIZON && alive.Count > 0; f++)
            {
                var next = new List<(SimState s, bool j0, int lj, int tl, int my)>();

                foreach (var node in alive)
                {
                    bool isGrounded = node.s.VelY_fixed == 0 && node.s.OnGround
                        && (node.s.GameMode == 0 || node.s.GameMode == 2);

                    if (isGrounded)
                    {
                        // Landing frame: branch into "jump" and "don't jump"
                        int newTl = node.tl + 1;

                        // ── Jump branch ──
                        {
                            var sj = node.s.Clone();
                            bool orbForce = sj.PendingOrbIndex >= 0;
                            bool aliveJ = StepFrame(ref sj, true, out bool endJ);
                            bool j0 = (f == 0) ? true : node.j0;
                            int newLj = node.lj + 1;
                            int jMinY = sj.Y_fixed >> 8; // terminal Y = current state Y
                            if (endJ) RecordTerminal(j0, BFS_HORIZON, sj.X_fixed >> 8, newLj, newTl, jMinY);
                            else if (aliveJ) next.Add((sj, j0, newLj, newTl, jMinY));
                            else RecordTerminal(j0, f, sj.X_fixed >> 8, newLj, newTl, jMinY);
                        }

                        // ── Walk branch ──
                        {
                            var sw = node.s.Clone();
                            bool orbForce = sw.PendingOrbIndex >= 0;
                            bool aliveW = StepFrame(ref sw, orbForce, out bool endW);
                            bool j0 = (f == 0) ? false : node.j0;
                            int wMinY = sw.Y_fixed >> 8; // terminal Y = current state Y
                            if (endW) RecordTerminal(j0, BFS_HORIZON, sw.X_fixed >> 8, node.lj, newTl, wMinY);
                            else if (aliveW) next.Add((sw, j0, node.lj, newTl, wMinY));
                            else RecordTerminal(j0, f, sw.X_fixed >> 8, node.lj, newTl, wMinY);
                        }
                    }
                    else
                    {
                        // Airborne: predict whether landing occurs this frame.
                        // Only dual-expand (jump vs walk on landing) when landing
                        // is predicted; otherwise single-expand since input has
                        // no effect on pure airborne physics.  This avoids wasting
                        // state budget on duplicate states at non-landing frames.
                        bool predictLanding = (node.s.GameMode == 0) && CubeWillLandThisFrame(node.s);
                        if (predictLanding)
                        {
                            // Landing predicted: branch into "jump on landing" and "walk"
                            int newTl = node.tl + 1;
                            for (int bi = 0; bi < 2; bi++)
                            {
                                bool bfsInput = (bi == 0); // true = jump on landing
                                if (node.s.PendingOrbIndex >= 0) bfsInput = true;
                                var sa = node.s.Clone();
                                bool aliveA = StepFrame(ref sa, bfsInput, out bool endA);
                                bool j0 = (f == 0) ? bfsInput : node.j0;
                                int newLj = (bi == 0) ? node.lj + 1 : node.lj;
                                int aMinY = sa.Y_fixed >> 8; // terminal Y = current state Y
                                if (endA) RecordTerminal(j0, BFS_HORIZON, sa.X_fixed >> 8, newLj, newTl, aMinY);
                                else if (aliveA) next.Add((sa, j0, newLj, newTl, aMinY));
                                else RecordTerminal(j0, f, sa.X_fixed >> 8, newLj, newTl, aMinY);
                            }
                        }
                        else
                        {
                            // Pure airborne: single successor, no choice to make
                            var sa = node.s.Clone();
                            bool orbInput = sa.PendingOrbIndex >= 0;
                            bool aliveA = StepFrame(ref sa, orbInput, out bool endA);
                            int aMinY = sa.Y_fixed >> 8; // terminal Y = current state Y
                            if (endA) RecordTerminal(node.j0, BFS_HORIZON, sa.X_fixed >> 8, node.lj, node.tl, aMinY);
                            else if (aliveA) next.Add((sa, node.j0, node.lj, node.tl, aMinY));
                            else RecordTerminal(node.j0, f, sa.X_fixed >> 8, node.lj, node.tl, aMinY);
                        }
                    }
                }

                alive = next;

                // Cap alive states: keep a balanced mix of both lineages
                if (alive.Count > MAX_ALIVE)
                {
                    // Deduplicate: states with same physics within a lineage are redundant.
                    // Key: (Y_fixed, VelY_fixed, OnGround, GravFlipped, GameMode, jumpedFrame0)
                    // Keep the one with highest landingsJumped/totalLandings (most "jump-like")
                    var deduped = new Dictionary<(int, int, bool, bool, int, bool), (SimState s, bool j0, int lj, int tl, int my)>();
                    foreach (var n in alive)
                    {
                        var key = (n.s.Y_fixed, n.s.VelY_fixed, n.s.OnGround, n.s.GravFlipped, n.s.GameMode, n.j0);
                        if (!deduped.ContainsKey(key) || n.lj > deduped[key].lj)
                            deduped[key] = n;
                    }
                    alive = deduped.Values.ToList();

                    // If still over cap after dedup, Y-diverse sampling.
                    // Sort each lineage by Y_fixed and sample evenly so the
                    // BFS explores states at different altitudes rather than
                    // concentrating on similar trajectories.
                    if (alive.Count > MAX_ALIVE)
                    {
                        var jumpL = alive.Where(n => n.j0).OrderBy(n => n.s.Y_fixed).ToList();
                        var walkL = alive.Where(n => !n.j0).OrderBy(n => n.s.Y_fixed).ToList();
                        int half = MAX_ALIVE / 2;
                        if (jumpL.Count > half)
                        {
                            var sampled = new List<(SimState s, bool j0, int lj, int tl, int my)>(half);
                            for (int i = 0; i < half; i++)
                                sampled.Add(jumpL[(int)((long)i * jumpL.Count / half)]);
                            jumpL = sampled;
                        }
                        if (walkL.Count > half)
                        {
                            var sampled = new List<(SimState s, bool j0, int lj, int tl, int my)>(half);
                            for (int i = 0; i < half; i++)
                                sampled.Add(walkL[(int)((long)i * walkL.Count / half)]);
                            walkL = sampled;
                        }
                        alive = jumpL.Concat(walkL).ToList();
                    }
                }
            }

            _speculativeDepth--;

            // Any states still alive survived the full BFS horizon
            foreach (var node in alive)
            {
                RecordTerminal(node.j0, BFS_HORIZON, node.s.X_fixed >> 8, node.lj, node.tl, node.my);
            }

            // ── Emit speculative paths for visualization ──
            // BFS doesn't trace individual paths, so reconstruct representative
            // paths using SimulateForwardWithJumpAt for the two frame-0 choices.
            if (_speculativeDepth == 0 && OnSpeculativePath != null)
            {
                _speculativeDepth++;

                // d=0 jump with QDC chain (best single-delay approximation of "jump now")
                var jumpPath = new List<(int x, int y)>();
                int jpSurv = SimulateForwardWithJumpAt(state, 0, out _, pathPoints: jumpPath);
                OnSpeculativePath.Invoke(jumpPath, 0, jpSurv, false);

                // d=0 hold-jump chain
                var holdPath = new List<(int x, int y)>();
                int hjSurv = SimulateForwardWithJumpAt(state, 0, out _,
                    holdAfterLanding: true, pathPoints: holdPath);
                OnSpeculativePath.Invoke(holdPath, 0, hjSurv, true);

                // no-press (walk)
                var walkPath = new List<(int x, int y)>();
                int wpSurv = SimulateForwardWithJumpAt(state, -1, pathPoints: walkPath);
                OnSpeculativePath.Invoke(walkPath, -1, wpSurv, false);

                // d=1 chain (walk one frame then jump)
                var d1Path = new List<(int x, int y)>();
                int d1Surv = SimulateForwardWithJumpAt(state, 1, out _, pathPoints: d1Path);
                OnSpeculativePath.Invoke(d1Path, 1, d1Surv, false);

                _speculativeDepth--;
            }

            // Decision: prefer frame-0 choice with best survival, then X as tiebreaker.
            // When the jump-at-frame-0 lineage's best path jumped at every landing
            // (holdPattern = true), give it a tiebreaker bonus — this means the BFS found
            // that a continuous jump rhythm works, and we should prefer it even when
            // walking has a marginally better survival count.  Precision spike sections
            // rely on maintaining the jump rhythm; the BFS re-evaluation at each
            // landing can break this rhythm when walk locally survives a few frames longer.
            //
            // ELEVATION BONUS: Convert height advantage to equivalent survival frames.
            // Each tile (16px) of extra altitude is worth ELEV_FRAMES_PER_TILE bonus
            // frames.  This causes the BFS to prefer climbing through platforming
            // sections even when walking survives a few frames longer locally.
            // Without this, the BFS is blind to distant walls and never climbs.
            const int ELEV_FRAMES_PER_TILE = 8;
            int elevBonus = 0;
            // Only apply elevation bonus when jump lineage SURVIVES the full
            // BFS horizon.  A terminal that dies at high altitude (e.g. hitting
            // spikes at Y=497) must NOT receive an elevation bonus — dying at
            // a high position is not beneficial.
            if (bestF0JumpSurv >= BFS_HORIZON && bestF0WalkSurv >= 0
                && bestF0JumpMinY != int.MaxValue && bestF0WalkMinY != int.MaxValue)
            {
                // positive elevDiff = jump lineage ends higher (lower Y) than walk
                int elevDiff = bestF0WalkMinY - bestF0JumpMinY;
                elevBonus = (elevDiff / 16) * ELEV_FRAMES_PER_TILE;
                if (elevBonus < 0) elevBonus = 0; // only reward climbing, never penalize
            }

            bool shouldJump;
            int holdBonus = bestF0JumpHoldPattern ? 5 : 0;
            int jumpScore = bestF0JumpSurv + holdBonus + elevBonus;
            int walkScore = bestF0WalkSurv;
            if (jumpScore > walkScore)
                shouldJump = true;
            else if (walkScore > jumpScore)
                shouldJump = false;
            else if (isElevated && bestF0JumpSurv >= BFS_HORIZON && bestF0WalkSurv >= BFS_HORIZON)
                shouldJump = false; // elevated tiebreak: stay on platform (walk) to preserve altitude
            else if (bestF0JumpX >= bestF0WalkX)
                shouldJump = true;  // tied: prefer jump (maintains momentum)
            else
                shouldJump = false;

#if !DISABLE_DEBUG_LOGGING
            PfLog($"[DECIDE_CUBE] BFS: jumpSurv={bestF0JumpSurv} jumpX={bestF0JumpX} holdPat={bestF0JumpHoldPattern} jumpMinY={bestF0JumpMinY}, walkSurv={bestF0WalkSurv} walkX={bestF0WalkX} walkMinY={bestF0WalkMinY} elevBonus={elevBonus} → {(shouldJump?"JUMP":"WALK")}");
#endif

            if (!shouldJump)
            {
                // BFS says don't jump this frame. But don't commit a delay —
                // next frame BFS re-evaluates from scratch.
                return false;
            }

            return true;
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
            // Forced input overrides from backtrack (hold/release for N frames)
            if (_shipForceHoldFrames > 0)
            {
                _shipForceHoldFrames--;
                return true;
            }
            if (_shipForceReleaseFrames > 0)
            {
                _shipForceReleaseFrames--;
                return false;
            }

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

            // Emit speculative paths for visualization (hold and release)
            if (_speculativeDepth == 0 && OnSpeculativePath != null)
            {
                // Simulate forward with hold-only and release-only for visualization
                var vizH = state.Clone();
                var vizR = state.Clone();
                var holdPath = new List<(int x, int y)>();
                var relPath = new List<(int x, int y)>();
                holdPath.Add(((vizH.X_fixed >> 8) + 8, (vizH.Y_fixed >> 8) + 8));
                relPath.Add(((vizR.X_fixed >> 8) + 8, (vizR.Y_fixed >> 8) + 8));
                _speculativeDepth++;
                for (int vf = 0; vf < SHIP_TREE_DEPTH; vf++)
                {
                    bool aH = StepFrame(ref vizH, true, out bool eH);
                    holdPath.Add(((vizH.X_fixed >> 8) + 8, (vizH.Y_fixed >> 8) + 8));
                    if (!aH || eH) break;
                }
                for (int vf = 0; vf < SHIP_TREE_DEPTH; vf++)
                {
                    bool aR = StepFrame(ref vizR, false, out bool eR);
                    relPath.Add(((vizR.X_fixed >> 8) + 8, (vizR.Y_fixed >> 8) + 8));
                    if (!aR || eR) break;
                }
                _speculativeDepth--;
                OnSpeculativePath.Invoke(holdPath, 0, survH, true);
                OnSpeculativePath.Invoke(relPath, 1, survR, false);
            }

            if (survH != survR)
                return survH > survR;

            // Equal survival — use velocity-aware corridor tracking as tiebreaker
            int biasPixels = (int)((JumpTimingBias - 0.5) * 16.0);
            int targetY = FindCorridorCenter(ref state) + _shipCorridorBias + biasPixels;
            int currentY = state.Y_fixed >> 8;
            int velY = state.VelY_fixed;

            int posError = (state.GravMul > 0) ? (currentY - targetY) : (targetY - currentY);
            int velComponent = -(velY * state.GravMul);
            int pdSignal = posError - (velComponent * 2);

            return pdSignal > 0;
        }

        /// <summary>
        /// Binary tree search for ship: at each depth, try both hold and release,
        /// recurse, and return the max survival depth achievable.
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



        /// <summary>
        /// Simulate forward, pressing jump on exactly one frame (jumpFrame).
        /// Pass jumpFrame = -1 to never jump (pure no-input simulation).
        /// After the initial jump lands, auto-jumps when danger is detected
        /// ahead (short lookahead) to evaluate multi-jump paths.
        /// </summary>
        private int SimulateForwardWithJumpAt(SimState state, int jumpFrame,
            bool holdAfterLanding = false, List<(int x, int y)>? pathPoints = null,
            bool singleJumpOnly = false)
        {
            return SimulateForwardWithJumpAt(state, jumpFrame, out _, out _, out _,
                false, holdAfterLanding, pathPoints, singleJumpOnly);
        }

        /// <summary>
        /// X-progress aware overload: simulates forward and returns both
        /// survival frames AND the final X position (in pixels) as an out
        /// parameter. Allows callers to score paths by X-progress (distance
        /// covered) in addition to survival time. Supports a horizonOverride
        /// to extend the simulation beyond the default LOOKAHEAD_HORIZON.
        /// </summary>
        private int SimulateForwardWithJumpAt(SimState state, int jumpFrame,
            out int finalX_px, bool holdAfterLanding = false,
            int horizonOverride = -1, List<(int x, int y)>? pathPoints = null,
            bool singleJumpOnly = false)
        {
            finalX_px = state.X_fixed >> 8;
            var s = state.Clone();
            pathPoints?.Add(((s.X_fixed >> 8) + 8, (s.Y_fixed >> 8) + 8));
            bool initialJumpDone = (jumpFrame < 0);
            bool chainJumps = (jumpFrame >= 0) && !singleJumpOnly;
            int horizon = horizonOverride > 0 ? horizonOverride : LOOKAHEAD_HORIZON;
            int minLandY = int.MaxValue; // track min Y when on a surface (for elevation exploration)

            // Capture starting surface Y
            if (s.VelY_fixed == 0)
                minLandY = s.Y_fixed >> 8;

            for (int f = 0; f < horizon; f++)
            {
                bool input = false;

                if (!initialJumpDone)
                {
                    input = (f >= jumpFrame && s.VelY_fixed == 0);
                    if (!input && f >= jumpFrame && jumpFrame >= 0 && s.PendingOrbIndex >= 0)
                        input = true;
                    if (input) initialJumpDone = true;
                }
                else if (chainJumps && (s.GameMode == 0 || s.GameMode == 2) && s.VelY_fixed == 0 && s.OnGround)
                {
                    input = holdAfterLanding || QuickDangerCheck(s);
                }

                if (jumpFrame >= 0 && initialJumpDone && !input && s.PendingOrbIndex >= 0)
                    input = true;

                bool alive = StepFrame(ref s, input, out bool endLevel);
                pathPoints?.Add(((s.X_fixed >> 8) + 8, (s.Y_fixed >> 8) + 8));

                // Track min Y when on a surface (VelY==0) for elevation comparison
                if (s.VelY_fixed == 0)
                {
                    int surfY = s.Y_fixed >> 8;
                    if (surfY < minLandY) minLandY = surfY;
                }

                if (!alive) { finalX_px = s.X_fixed >> 8; _lastSpecMinLandY = minLandY; return f; }
                if (endLevel) { finalX_px = s.X_fixed >> 8; _lastSpecMinLandY = minLandY; return horizon; }
            }
            finalX_px = s.X_fixed >> 8;
            _lastSpecMinLandY = minLandY;
            return horizon;
        }

        private int SimulateForwardWithJumpAt(SimState state, int jumpFrame,
            out string deathReason, out int deathX, out int deathY,
            bool logTrajectory = false, bool holdAfterLanding = false,
            List<(int x, int y)>? pathPoints = null, bool singleJumpOnly = false)
        {
            deathReason = ""; deathX = 0; deathY = 0;
            var s = state.Clone();
            pathPoints?.Add(((s.X_fixed >> 8) + 8, (s.Y_fixed >> 8) + 8));
            bool initialJumpDone = (jumpFrame < 0);
            bool chainJumps = (jumpFrame >= 0) && !singleJumpOnly;
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
            const int ELEV_DROP_THRESHOLD = 16; // 1 tile — detect walking off platform edges
            int startY = s.Y_fixed >> 8;
            for (int f = 0; f < SHORT_HORIZON; f++)
            {
                bool alive = StepFrame(ref s, false, out _);
                if (!alive) return true; // danger ahead, jump now
            }
            // Elevation guard: if the cube would drop significantly in the
            // gravity direction (walking off a pillar edge into a gap/pit),
            // treat as danger even if it wouldn't die within SHORT_HORIZON.
            // This forces re-jump in the chained pass to maintain elevation
            // through checkerboard/gap terrain.
            int endY = s.Y_fixed >> 8;
            int drop = s.GravFlipped ? (startY - endY) : (endY - startY);
            if (drop > ELEV_DROP_THRESHOLD) return true;
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
        ///   6. x_movement_coll()       — forward wall check at OLD X, post-eject Y (slope skip)
        ///   7. RESTORE NEW X
        ///   8. death collision          — 5-point death check at NEW X (matching sim)
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

            // ── STEP 2: Compute new X (applied at the end, matching NES
            //    where x_movement() runs AFTER all collision checks) ──
            int newX_fixed = s.X_fixed + s.VelX_fixed;

            // NOTE: The old VerifyGroundSupport check at new-X has been removed.
            // The NES has no such look-ahead — ground loss is detected naturally
            // by gravity pulling the player down and CubeEject finding no floor.
            // With no GRAV_SKIP (matching NES), grounded frames apply gravity
            // (+0x6B), and CubeEject snaps the player back each frame.

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
                    var (ceilHit, _, ceilSpikeDeath) = CheckCeiling(collX_chk, testY_chk, hbW_chk, hbH_chk);
                    if (ceilSpikeDeath)
                    {
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[CEIL_SPIKE_DEATH] cube prox check X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
                        _lastDeathReason = "CEIL_SPIKE_DEATH"; _lastDeathX = s.X_fixed >> 8; _lastDeathY = s.Y_fixed >> 8;
#endif
                        return false;
                    }
                    if (ceilHit && s.VelY_fixed < 0)
                    {
                        s.VelY_fixed = 0;
                    }
                }

                // Eject first (matching NES: cube_eject runs inside cube_movement,
                // bg_coll_death runs later in runthecolls using post-eject Generic.y)
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

                // Center death check at post-eject Y (NES: bg_coll_death in runthecolls
                // reads Generic.y which was set from currplayer_y after cube_eject)
                if (CheckCenterPointDeath(ref s))
                {
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[CENTER_DEATH] X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px (post-eject)");
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

                if (CheckDeathCollision(ref s))
                    return false;
            }
            else if (s.GameMode == 2) // Ball mode
            {
                // ── BALL GROUNDED PROBE SPIKE CHECK (before gravity) ──
                // Sim's ball grounded check calls CheckCollisionDown/Up which have
                // spike-death side-effects (COL_DEATH_TOP / COL_DEATH_BOTTOM).
                // This fires EVERY frame in ball mode, BEFORE gravity, but only
                // outside flip cooldown (sim uses cached grounded state during cooldown).
                if (s.BallFlipCooldown == 0)
                {
                    if (BallGroundedProbeSpikeDeath(ref s))
                    {
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[BALL_GROUND_PROBE_DEATH] X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
                        _lastDeathReason = "BALL_GROUND_PROBE_DEATH"; _lastDeathX = s.X_fixed >> 8; _lastDeathY = s.Y_fixed >> 8;
#endif
                        return false;
                    }
                }

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
                    bool ballVelZeroDied = false;
                    BallVelocityZeroing(ref s, out ballVelZeroDied);
                    if (ballVelZeroDied)
                    {
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[BALL_VELZERO_DEATH] X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
                        _lastDeathReason = "BALL_VELZERO_DEATH"; _lastDeathX = s.X_fixed >> 8; _lastDeathY = s.Y_fixed >> 8;
#endif
                        return false;
                    }

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

                if (CheckDeathCollision(ref s))
                {
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[CENTER_DEATH] X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
                    _lastDeathReason = "CENTER_DEATH"; _lastDeathX = s.X_fixed >> 8; _lastDeathY = s.Y_fixed >> 8;
#endif
                    return false;
                }
            }
            else if (s.GameMode == 3) // UFO mode
            {
                // ── UFO GRAVITY ──
                // Same as CommonGravityRoutine_Fresh with UFO constants.
                // UFO uses tap-to-jump (not hold-to-fly like ship).
                UfoGravityStep(ref s);

                // ── CEILING PROXIMITY CHECK (non-flipped gravity only) ──
                // Matches UfoPhysics_Fresh.partial.cs lines 61-73
                if (!s.GravFlipped)
                {
                    int hbW_chk = GetHitboxW(s.Mini);
                    int hbH_chk = GetHitboxH(s.Mini);
                    int hbOffY_chk = GetHitboxOffsetY(s.Mini, s.GravFlipped);
                    int collX_chk = s.X_fixed >> 8;
                    int testY_chk = (s.Y_fixed >> 8) + hbOffY_chk - 1;
                    var (ceilHit, _, ceilSpikeDeath) = CheckCeiling(collX_chk, testY_chk, hbW_chk, hbH_chk);
                    if (ceilSpikeDeath)
                    {
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[CEIL_SPIKE_DEATH] ufo prox check X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
                        _lastDeathReason = "CEIL_SPIKE_DEATH"; _lastDeathX = s.X_fixed >> 8; _lastDeathY = s.Y_fixed >> 8;
#endif
                        return false;
                    }
                    if (ceilHit && s.VelY_fixed < 0)
                    {
                        s.VelY_fixed = 0;
                    }
                }

                // ── UFO EJECT (shared with Ship: ceiling + floor, no velocity guard) ──
                ShipEject(ref s, out bool ufoDied);
                if (ufoDied)
                    return false;

                // ── UFO JUMP (tap-to-jump, can jump mid-air) ──
                if (input)
                {
                    int jumpVel = (int)UfoJumpVel(s.Mini) * -s.GravMul; // against gravity
                    s.VelY_fixed = jumpVel;
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[UFO_JUMP] VelY=0x{s.VelY_fixed:X} gravMul={s.GravMul} mini={s.Mini}");
#endif
                }

                if (CheckDeathCollision(ref s))
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
            // NES x_movement_coll runs bg_coll_floor_spikes (4-corner) then bg_coll_R
            // at the same OLD X, post-eject Y coordinates.

            // ── STEP 7a: 4-CORNER SPIKE CHECK (bg_coll_floor_spikes) ──
            if (CheckFloorSpikes(ref s))
            {
#if !DISABLE_DEBUG_LOGGING
                PfLog($"[FLOOR_SPIKE] X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
#endif
                return false;
            }

            // ── STEP 7b: FORWARD COLLISION (bg_coll_R) ──
            if (s.GameMode == 0 || s.GameMode == 1 || s.GameMode == 2 || s.GameMode == 3 || s.GameMode == 4 || s.GameMode == 8 || s.GameMode == 10)
            {
                if (CheckForwardCollision(ref s))
                {
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[FWD_DEATH] X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
#endif
                    _lastDeathReason = "FWD_DEATH"; _lastDeathX = s.X_fixed >> 8; _lastDeathY = s.Y_fixed >> 8;
                    return false;
                }
            }

            // ── STEP 7c: DEATH CHECK at OLD X, post-eject Y (matching NES bg_coll_death) ──
            // NES bg_coll_death uses Generic.x/y which are set by cube_movement
            // from post-eject currplayer_y and pre-x_movement currplayer_x (stale).
            // x_movement() sets Generic.x BEFORE advancing currplayer_x, so
            // Generic.x is still the OLD X value when bg_coll_death runs.
            if (CheckDeathCollision(ref s))
            {
#if !DISABLE_DEBUG_LOGGING
                PfLog($"[DEATH_COLL] X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
                _lastDeathReason = "DEATH_COLL"; _lastDeathX = s.X_fixed >> 8; _lastDeathY = s.Y_fixed >> 8;
#endif
                return false;
            }

            // ── STEP 8: RESTORE NEW X ──
            s.X_fixed = newX_fixed;

            // ── STEP 8b: GAME MODE PORTAL CHECK at NEW X ──
            // The sim detects game mode portals AFTER physics + forward collision,
            // using NEW X (post-advance).  Matching sim's post-physics portal loop
            // (SimulatorWindow line ~10983).  Uses centered 15×15 hitbox and NES-style
            // (+1) overlap conversion, exactly as SpriteIntersectsPlayer does.
            CheckGameModePortalsAtNewX(ref s);

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
        /// Predict whether the cube will land on a floor during the current
        /// StepFrame call.  Used by DecideInput to detect landing frames and
        /// enable landing-frame jumps (matching NES behavior).
        /// 
        /// Simulates one step of gravity + CubeEject floor detection without
        /// modifying any state.  Returns true if the player would touch a
        /// solid floor this frame (meaning CubeEject would zero VelY and the
        /// jump check in StepFrame could fire if input=true).
        /// </summary>
        private bool CubeWillLandThisFrame(SimState state)
        {
            // ── Gravity prediction ──
            int gravity = GetGravity(state.Mini);
            int accel = gravity * state.GravMul;

            // Max fall speed deceleration (matching common_gravity_routine)
            if (state.GravMul > 0 && state.VelY_fixed > maxFallSpeed)
                accel = -accel;
            else if (state.GravMul < 0 && state.VelY_fixed < -maxFallSpeed)
                accel = -accel;

            int futureVelY = state.VelY_fixed + accel;

            if (!state.GravFlipped)
            {
                // Normal gravity: landing = falling (futureVelY >= 0) + floor hit
                if (futureVelY < 0) return false; // Rising — won't land

                int futureY_fixed = state.Y_fixed + futureVelY;
                int hbW = GetHitboxW(state.Mini);
                int hbH = GetHitboxH(state.Mini);
                int hbOffY = GetHitboxOffsetY(state.Mini, state.GravFlipped);
                int collY = (futureY_fixed >> 8) + hbOffY;
                var (floorHit, _, _) = CheckFloor(state.X_fixed >> 8, collY, hbW, hbH);
                return floorHit;
            }
            else
            {
                // Flipped gravity: landing = rising toward ceiling (futureVelY <= 0) + ceiling hit
                if (futureVelY > 0) return false; // Falling away — won't land

                int futureY_fixed = state.Y_fixed + futureVelY;
                int hbW = GetHitboxW(state.Mini);
                int hbH = GetHitboxH(state.Mini);
                int hbOffY = GetHitboxOffsetY(state.Mini, state.GravFlipped);
                int collY = (futureY_fixed >> 8) + hbOffY;
                var (ceilHit, _, _) = CheckCeiling(state.X_fixed >> 8, collY, hbW, hbH);
                return ceilHit;
            }
        }

        /// <summary>
        /// CommonGravityRoutine_Fresh — apply gravity and integrate Y.
        /// Matches NES common_gravity_routine() which ALWAYS applies gravity,
        /// even when grounded.  Grounded frames: gravity adds 0x6B to vel,
        /// shifts Y by a sub-pixel amount, then CubeEject snaps back and zeroes
        /// velocity — same net result as the NES.
        /// </summary>
        private void CubeGravity(ref SimState s)
        {
            // NO GRAV_SKIP — NES common_gravity_routine() always applies gravity.
            // The simulator's CommonGravityRoutine_Fresh also has no GRAV_SKIP.

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
                // NES bg_coll_D velocity guard: the ENTIRE floor collision
                // (including spike-death checks) is skipped when vel_y < 0.
                // This matches: if(!(high_byte(currplayer_vel_y) & 0x80))
                if (s.VelY_fixed >= 0)
                {
                    var (floorHit, floorTopY, floorSpikeDeath) = CheckFloor(collX, collY, hbW, hbH);
                    if (floorSpikeDeath)
                    {
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[EJECT] floor spike death at X={playerX_px} Y={playerY_px}");
#endif
                        died = true;
                        return;
                    }
                    if (floorHit)
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
                    else
                    {
                        // Falling with no floor below → not grounded.
                        // Replaces the removed VerifyGroundSupport check.
                        s.OnGround = false;
                    }
                }

                // NOTE: Ceiling headbonk (CheckCollisionUp) only fires in the simulator
                // when hblocked || fblocked (alphabet blocks). Pathfinder doesn't track
                // these flags, so headbonk is omitted — cube passes through ceilings from
                // below, matching actual NES behavior without alphabet blocks.
            }
            else // Reversed gravity
            {
                // Ceiling landing — NES bg_coll_U only runs when vel < 0
                // (high_byte(vel_y) & 0x80), i.e. strictly negative.
                // When vel == 0 (just ejected), NES skips bg_coll_U entirely.
                if (s.VelY_fixed < 0)
                {
                    var (ceilHit, ceilBotY, ceilSpikeDeath) = CheckCeiling(collX, collY, hbW, hbH);
                    if (ceilSpikeDeath)
                    {
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[CEIL_SPIKE_DEATH] cube eject X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
                        _lastDeathReason = "CEIL_SPIKE_DEATH"; _lastDeathX = s.X_fixed >> 8; _lastDeathY = s.Y_fixed >> 8;
#endif
                        died = true;
                        return;
                    }
                    if (ceilHit)
                    {
                        // NES bg_coll_U probes 1px inside the hitbox, so the cube's
                        // resting position on the ceiling is 1 pixel closer than ceilBotY.
                        int newY = ceilBotY - hbOffY - 1;
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
            // NO GRAV_SKIP — NES common_gravity_routine() always applies gravity.
            // Ball eject does not set WasZeroedByCollision anyway, so this was
            // always dead code.  Removed for NES 1:1 accuracy.

            int gravity = BallGravity(s.Mini);
            int accel = gravity * s.GravMul;

            // Check past max fall speed → decelerate
            // Ball has its own dedicated max fall speed (0x600), NOT the level's
            // maxFallSpeed field which is for cube and may differ (e.g. 0x700).
            int ballMaxFS = BallMaxFallSpeed(s.Mini);
            if (s.GravMul > 0)
            {
                if (s.VelY_fixed > ballMaxFS)
                    accel = -accel;
            }
            else
            {
                if (s.VelY_fixed < -ballMaxFS)
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
        private void BallVelocityZeroing(ref SimState s, out bool died)
        {
            died = false;
            int hbW = GetHitboxW(s.Mini);
            int hbH = GetHitboxH(s.Mini);
            int hbOffY = GetHitboxOffsetY(2, s.Mini, s.GravFlipped);
            int collX = s.X_fixed >> 8;

            if (!s.GravFlipped) // currplayer_gravity == 0
            {
                // Check ceiling collision for velocity zeroing
                int testY = (s.Y_fixed >> 8) + hbOffY - 1;
                var (collided, _, velZeroSpikeDeath) = CheckCeiling(collX, testY, hbW, hbH);
                if (velZeroSpikeDeath)
                {
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[CEIL_SPIKE_DEATH] ball vel-zero X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
#endif
                    died = true;
                    return;
                }
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
                        // NOTE: Do NOT set WasZeroedByCollision here.
                        // SIM's BALL_EJECT_D does not set wasZeroedByCollisionLastFrame.
                        // Setting it causes divergence at ball→ship transitions (ship
                        // gets GRAV_SKIP and stays grounded, while SIM applies gravity).
                        s.OnGround = true;
                    }
                }
            }
            else // Inverted gravity
            {
                // Ceiling collision (landing) — only when moving up
                if (s.VelY_fixed <= 0)
                {
                    var (ceilHit, ceilBotY, ceilSpikeDeath) = CheckCeiling(collX, collY, hbW, hbH);
                    if (ceilSpikeDeath)
                    {
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[CEIL_SPIKE_DEATH] ball eject X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
#endif
                        died = true;
                        return;
                    }
                    if (ceilHit)
                    {
                        int newY = ceilBotY - hbOffY;
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[BALL_EJECT] ceiling land (inverted) Y: {playerY_px} -> {newY} (ceilBot={ceilBotY})");
#endif
                        s.Y_fixed = newY << 8;
                        s.VelY_fixed = 0;
                        // NOTE: Do NOT set WasZeroedByCollision here.
                        // SIM's BALL_EJECT_U does not set wasZeroedByCollisionLastFrame.
                        // Setting it causes divergence at ball→ship transitions.
                        s.OnGround = true;
                    }
                }
            }
        }

        /// <summary>
        /// Check for spike death at the ball's grounded-probe position.
        /// Matches the sim's ball grounded check which calls CheckCollisionDown
        /// (normal gravity) or CheckCollisionUp (inverted gravity).  Those
        /// collision functions have spike-death side-effects that fire before
        /// the solid-collision check itself.
        ///
        /// Normal gravity: probes 3 X-points at Y + hbOffY + hbH + 2
        ///   (CheckCollisionDown: playerBottom_px = testTop + testHeight,
        ///    where testTop = playerBottom = Y+hbOffY+hbH, testHeight = 2)
        /// Inverted gravity: probes 3 X-points at Y + hbOffY - 1
        ///   (CheckCollisionUp: checkY = playerTop_px + 1,
        ///    where playerTop_px = testTop = playerTop - 2, so +1 → Y+hbOffY-1)
        /// </summary>
        private bool BallGroundedProbeSpikeDeath(ref SimState s)
        {
            int playerX_px = s.X_fixed >> 8;
            int playerY_px = s.Y_fixed >> 8;
            int hbW = GetHitboxW(s.Mini);
            int hbH = GetHitboxH(s.Mini);
            int hbOffY = GetHitboxOffsetY(2, s.Mini, s.GravFlipped);
            int collX = playerX_px;

            int checkY;
            if (!s.GravFlipped)
            {
                // Normal gravity: CheckCollisionDown spike check at playerBottom + 2
                checkY = playerY_px + hbOffY + hbH + 2;
            }
            else
            {
                // Inverted gravity: CheckCollisionUp spike check at playerTop - 1
                checkY = playerY_px + hbOffY - 1;
            }

            // 3 X-points matching NES bg_coll_D / bg_coll_U
            for (int cpIdx = 0; cpIdx < 3; cpIdx++)
            {
                int px = cpIdx == 0 ? collX
                       : cpIdx == 1 ? collX + hbW / 2
                       : collX + hbW;
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

                // Only COL_DEATH_TOP and COL_DEATH_BOTTOM (matching NES bg_coll_U_D_checks)
                if ((collision == MetatileCollision.COL_DEATH_TOP || collision == MetatileCollision.COL_DEATH_BOTTOM) &&
                    MetatileCollisionTable.TileKillsAtPixel(collision, localX, localY))
                {
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[BALL_GROUND_SPIKE] at ({px},{checkY}) tile=0x{tileId:X2} localX={localX} localY={localY}");
#endif
                    return true; // spike death
                }
            }

            return false;
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

            // Always evaluate all flip timings when the ball is grounded.
            // Same principle as cube: the pathfinder's core job is to test
            // every possible flip frame. X-progress comparison handles cases
            // where both flip and no-flip survive the full horizon.

#if !DISABLE_DEBUG_LOGGING
            PfLog($"[DECIDE_BALL] noPressFrames={noPressFrames}, testing flips (walkFrames={_ballGroundedWalkFrames})");
#endif

            // Get X-progress for no-press path
            int noPressX = state.X_fixed >> 8;
            {
                _speculativeDepth++;
                SimulateForwardWithJumpAt(state, -1, out noPressX);
                _speculativeDepth--;
            }

            // Test different flip timings
            int bestSurvival = noPressFrames;
            int bestXProgress = noPressX;
            var viableDelays = new List<(int delay, int survival, int xProgress)>();

            // Always test full range of delays (same reasoning as cube).
            const int maxDelay = 35;
            _speculativeDepth++;
            for (int delay = 0; delay < maxDelay; delay++)
            {
                List<(int x, int y)>? specPath = (_speculativeDepth == 1 && OnSpeculativePath != null)
                    ? new List<(int x, int y)>() : null;
                int survival;
                int xProg = state.X_fixed >> 8;

                survival = SimulateForwardWithJumpAt(state, delay, out xProg,
                    pathPoints: specPath);

                OnSpeculativePath?.Invoke(specPath, delay, survival, false);

                if (survival > bestSurvival)
                    bestSurvival = survival;
                if (xProg > bestXProgress)
                    bestXProgress = xProg;

                // Viability: flipping is viable if it survives longer OR
                // achieves more X-progress than no-press.
                if (xProg > noPressX || survival > noPressFrames)
                    viableDelays.Add((delay, survival, xProg));
            }
            _speculativeDepth--;

            // ── Single-jump pass (same rationale as cube) ──
            _speculativeDepth++;
            for (int delay = 0; delay < maxDelay; delay++)
            {
                List<(int x, int y)>? sjPath = (_speculativeDepth == 1 && OnSpeculativePath != null)
                    ? new List<(int x, int y)>() : null;
                int sjXProg = state.X_fixed >> 8;
                int sjSurvival = SimulateForwardWithJumpAt(state, delay, out sjXProg,
                    singleJumpOnly: true, pathPoints: sjPath);
                OnSpeculativePath?.Invoke(sjPath, delay, sjSurvival, false);
                if (sjSurvival > bestSurvival)
                    bestSurvival = sjSurvival;
                if (sjXProg > bestXProgress)
                    bestXProgress = sjXProg;
                if (sjXProg > noPressX || sjSurvival > noPressFrames)
                    viableDelays.Add((delay, sjSurvival, sjXProg));
            }
            _speculativeDepth--;

            if (viableDelays.Count == 0)
            {
#if !DISABLE_DEBUG_LOGGING
                PfLog($"[DECIDE_BALL] no viable flip delays found");
#endif

                // Phase 2: Extended horizon evaluation (same as cube).
                if (noPressFrames >= LOOKAHEAD_HORIZON)
                {
                    const int EXTENDED_HORIZON = 180;
                    _speculativeDepth++;
                    int extNpSurv = SimulateForwardWithJumpAt(state, -1, out int extNpX, horizonOverride: EXTENDED_HORIZON);
                    _speculativeDepth--;
                    if (extNpSurv < EXTENDED_HORIZON)
                    {
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[DECIDE_BALL] Phase 2 extended: no-press dies at extSurv={extNpSurv} extX={extNpX}, re-evaluating flips");
#endif
                        _speculativeDepth++;
                        for (int delay = 0; delay < maxDelay; delay++)
                        {
                            List<(int x, int y)>? specPath2 = (_speculativeDepth == 1 && OnSpeculativePath != null)
                                ? new List<(int x, int y)>() : null;
                            int s2 = SimulateForwardWithJumpAt(state, delay, out int x2,
                                horizonOverride: EXTENDED_HORIZON, pathPoints: specPath2);
                            OnSpeculativePath?.Invoke(specPath2, delay, s2, false);
                            if (x2 > extNpX || s2 > extNpSurv)
                            {
                                viableDelays.Add((delay, s2, x2));
                                if (s2 > bestSurvival) bestSurvival = s2;
                                if (x2 > bestXProgress) bestXProgress = x2;
                            }
                        }
                        _speculativeDepth--;
                    }
                }

                if (viableDelays.Count == 0)
                    return false;
            }

            // Pick timing using bias (same as cube)
            // Rank by X-progress
            List<(int delay, int survival, int xProgress)> bestDelays;
            {
                int xThreshold = bestXProgress - 8; // 8px tolerance
                bestDelays = viableDelays.Where(d => d.xProgress >= xThreshold).ToList();
            }
            if (bestDelays.Count == 0) bestDelays = viableDelays;

            int pickIndex = (int)(JumpTimingBias * (bestDelays.Count - 1));
            pickIndex = Math.Clamp(pickIndex, 0, bestDelays.Count - 1);
            int pickedDelay = bestDelays[pickIndex].delay;

#if !DISABLE_DEBUG_LOGGING
            PfLog($"[DECIDE_BALL] viable={viableDelays.Count} best=[{string.Join(",", bestDelays.Select(d => $"d{d.delay}:s{d.survival}:x{d.xProgress}"))}] bestSurvival={bestSurvival} bestX={bestXProgress} picking delay={pickedDelay}");
#endif

            if (pickedDelay > 0)
            {
                _committedJumpDelay = pickedDelay;
                return false;
            }

            return true; // flip now
        }

        /// <summary>
        /// UFO decision logic: decide when to tap-jump.
        /// UFO jumps on press (not hold), can jump mid-air.
        /// Uses speculative forward-simulation to evaluate survival.
        /// </summary>
        private bool DecideUfoInput(SimState state, bool isOverrideFrame)
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
                    skipState.ProcessedSprites.Add(orbIndex);
                    bool skipAlive = StepFrame(ref skipState, false, out bool skipEnd);
                    if (skipEnd) return false;

                    int skipSurv = 0;
                    if (skipAlive)
                    {
                        int skipMaxDelay = Math.Min(15, LOOKAHEAD_HORIZON);
                        for (int sd = -1; sd < skipMaxDelay; sd++)
                        {
                            int ss = 1 + SimulateForwardWithJumpAt(skipState, sd);
                            if (ss > skipSurv) skipSurv = ss;
                        }
                    }

                    bool useOrb = orbSurv >= skipSurv;
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[DECIDE_UFO_ORB] orbSurv={orbSurv} skipSurv={skipSurv} → {(useOrb ? "ACTIVATE" : "SKIP")}");
#endif
                    return useOrb;
                }
            }

            // ── Evaluate: jump now vs wait ──
            // UFO can jump any time (no grounded requirement).
            // Test no-press survival, then test press at various delays.
            if (_speculativeDepth >= MAX_SPECULATIVE_DEPTH) return false;

            _speculativeDepth++;
            List<(int x, int y)>? noPressPath = (_speculativeDepth == 1 && OnSpeculativePath != null)
                ? new List<(int x, int y)>() : null;
            int noPressFrames = SimulateForwardWithJumpAt(state, -1, pathPoints: noPressPath);
            OnSpeculativePath?.Invoke(noPressPath, -1, noPressFrames, false);
            _speculativeDepth--;

            int bestSurv = noPressFrames;
            int bestDelay = -1;

            _speculativeDepth++;
            int maxDelay = Math.Min(15, LOOKAHEAD_HORIZON);
            for (int delay = 0; delay < maxDelay; delay++)
            {
                List<(int x, int y)>? specPath = (_speculativeDepth == 1 && OnSpeculativePath != null)
                    ? new List<(int x, int y)>() : null;
                int survival = 1 + SimulateForwardWithJumpAt(state, delay, pathPoints: specPath);
                OnSpeculativePath?.Invoke(specPath, delay, survival, false);

                if (survival > bestSurv)
                {
                    bestSurv = survival;
                    bestDelay = delay;
                }
            }
            _speculativeDepth--;

            // If no improvement over no-press, don't jump
            if (bestDelay < 0)
                return false;

            // Bias-aware delay: commit if timing matches
            int biasedDelay = (int)(bestDelay * JumpTimingBias + 0.5);
            if (_committedJumpDelay < 0)
            {
                _committedJumpDelay = biasedDelay;
                if (biasedDelay == 0) return true;
                return false;
            }
            else if (_committedJumpDelay == 0)
            {
                _committedJumpDelay = -1;
                return true;
            }
            else
            {
                _committedJumpDelay--;
                return false;
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        //  UFO PHYSICS (matching UfoPhysics_Fresh)
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// UFO gravity: uses CommonGravityRoutine with UFO-specific constants.
        /// Matches UfoPhysics_Fresh in UfoPhysics_Fresh.partial.cs.
        /// </summary>
        private void UfoGravityStep(ref SimState s)
        {
            // NO GRAV_SKIP — NES common_gravity_routine() always applies gravity.
            // UFO eject does not set WasZeroedByCollision anyway, so this was
            // always dead code.  Removed for NES 1:1 accuracy.

            int gravity = UfoGravity(s.Mini);
            int accel = gravity * s.GravMul;
            int ufoMaxFall = UfoMaxFallSpeed(s.Mini);

            // Check past max fall speed → decelerate
            if (s.GravMul > 0)
            {
                if (s.VelY_fixed > ufoMaxFall)
                    accel = -accel;
            }
            else
            {
                if (s.VelY_fixed < -ufoMaxFall)
                    accel = -accel;
            }

            s.VelY_fixed += accel;
            s.Y_fixed += s.VelY_fixed;
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
            // NO GRAV_SKIP — NES common_gravity_routine() always applies gravity.
            // Ship eject does not set WasZeroedByCollision anyway, so this was
            // always dead code.  Removed for NES 1:1 accuracy.

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

            // ── Ship inverted-gravity ceiling grounding ──
            // Match ShipPhysics_Fresh: after gravity+Y update, if inverted gravity
            // and there's a ceiling 1px above, zero upward velocity to prevent
            // accumulation against the ceiling (before ShipEject does the snap).
            // NOTE: SIM probes at testY = playerY + hitboxOffsetY - 1 (1px above),
            // so we pass cgY - 1 to CheckCeiling which uses topY for its boundary
            // check (topY < colBottom_px). Without the -1, the check fails at
            // the exact ceiling boundary (e.g. 304 < 304 = false).
            if (s.GravFlipped)
            {
                int cgX = s.X_fixed >> 8;
                int cgY = (s.Y_fixed >> 8) + GetHitboxOffsetY(s.Mini, s.GravFlipped) - 1;
                var (ceilGrounded, _, _) = CheckCeiling(cgX, cgY, GetHitboxW(s.Mini), GetHitboxH(s.Mini));
                if (ceilGrounded && s.VelY_fixed < 0)
                    s.VelY_fixed = 0;
            }

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
            var (ceilHit, ceilBotY, shipCeilSpikeDeath) = CheckCeiling(collX, collY, hbW, hbH);
            if (shipCeilSpikeDeath)
            {
#if !DISABLE_DEBUG_LOGGING
                PfLog($"[CEIL_SPIKE_DEATH] ship eject X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
                _lastDeathReason = "CEIL_SPIKE_DEATH"; _lastDeathX = s.X_fixed >> 8; _lastDeathY = s.Y_fixed >> 8;
#endif
                died = true;
                return;
            }
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
        /// Must exactly match SimulatorWindow / CubePhysics_Fresh ground support:
        ///   - Center a 15px hitbox on (playerX + TILE/2).
        ///   - Foot Y = playerY + hitboxOffsetY + hitboxH.
        ///   - Use ProvidesFloorAtColumn(col, localX) per-column check.
        /// </summary>
        private bool VerifyGroundSupport(ref SimState s)
        {
            int playerX_px = s.X_fixed >> 8;
            int playerY_px = s.Y_fixed >> 8;

            if (!s.GravFlipped)
            {
                // Center the 15px ground-check hitbox on the 16x16 tile center.
                // Player position always represents the top-left of a 16x16
                // tile space, so center = playerX + TILE/2 regardless of mini.
                const int HITBOX_W_LOCAL = 15;
                int playerCenter_px = playerX_px + (TILE / 2);
                int playerLeft_px = playerCenter_px - (HITBOX_W_LOCAL / 2);
                int playerRight_px = playerLeft_px + (HITBOX_W_LOCAL - 1);
                // Foot = bottom of actual hitbox (matches CheckCollisionDown's
                // playerBottom_px = collisionY + hitboxH).
                int hbH = GetHitboxH(s.Mini);
                int hbOffY = GetHitboxOffsetY(s.GameMode, s.Mini, s.GravFlipped);
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

                for (int tx = playerLeft_px / TILE; tx <= playerRight_px / TILE; tx++)
                {
                    if (tx < 0 || tx >= mapWidth) continue;
                    int idx = tileArrayY * mapWidth + tx;
                    if (idx < 0 || idx >= tiles.Length) continue;
                    int tid = tiles[idx];
                    var col = MetatileCollisionTable.GetCollision((byte)MapTileForCollision(tid));
                    if (col == MetatileCollision.COL_NONE) continue;

                    // Match sim: use per-column floor support check with
                    // localX based on playerCenter (clamped to tile bounds)
                    int tileStartX = tx * TILE;
                    int localX = Math.Max(0, Math.Min(TILE - 1, playerCenter_px - tileStartX));
                    if (ProvidesFloorAtColumn(col, localX))
                        return true;
                }

                return false;
            }
            else
            {
                // Reversed gravity: check above head
                int hbOffY = GetHitboxOffsetY(s.Mini, s.GravFlipped);
                int headY_px = playerY_px + hbOffY;
                // Floor division for correct tile lookup at negative values
                int valGS = headY_px - 1;
                int tileAboveY = valGS >= 0 ? valGS / TILE : (valGS - TILE + 1) / TILE;
                if (tileAboveY < 0) return false;

                int tileArrayY = tileAboveY + groundRowsToReserve;
                if (tileArrayY < 0 || tileArrayY >= mapHeight) return false;

                // Match sim: centered X range on 16x16 tile space
                const int HITBOX_W_LOCAL = 15;
                int playerCenter_px = playerX_px + (TILE / 2);
                int playerLeft_px = playerCenter_px - (HITBOX_W_LOCAL / 2);
                int playerRight_px = playerLeft_px + (HITBOX_W_LOCAL - 1);

                for (int tx = playerLeft_px / TILE; tx <= playerRight_px / TILE; tx++)
                {
                    if (tx < 0 || tx >= mapWidth) continue;
                    int idx = tileArrayY * mapWidth + tx;
                    if (idx < 0 || idx >= tiles.Length) continue;
                    int tid = tiles[idx];
                    var col = MetatileCollisionTable.GetCollision((byte)MapTileForCollision(tid));
                    if (col == MetatileCollision.COL_NONE) continue;

                    int tileStartX = tx * TILE;
                    int localX = Math.Max(0, Math.Min(TILE - 1, playerCenter_px - tileStartX));
                    if (ProvidesFloorAtColumn(col, localX))
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
            // NES sprite_collide() uses Generic.x = high_byte(currplayer_x) + 1
            int nesX = currentX_px + 1;
            int playerRight = nesX + hbW;  // exclusive right, matching SIM inclusive+1 conversion

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
                    bool xOverlap = !((playerRight) < sp.HitLeft || sp.HitRight < nesX);
                    bool yOverlap = !((playerBottom) < sp.HitTop || sp.HitBottom < playerTop);

                    // End-level trigger (0x0F) fires on X overlap only (full-screen height),
                    // matching the simulator's X-position-only detection.
                    bool hit = IsEndLevel(sid) ? xOverlap : (xOverlap && yOverlap);

#if !DISABLE_DEBUG_LOGGING
                    if (IsGravityPortal(sid) || IsEndLevel(sid))
                    {
                        PfLog($"[SPRITE_CHECK] sid=0x{sid:X2} idx={sp.Index} playerBox=({nesX},{playerTop})-({playerRight},{playerBottom}) spriteBox=({sp.HitLeft},{sp.HitTop})-({sp.HitRight},{sp.HitBottom}) xOvlp={xOverlap} yOvlp={yOverlap} hit={hit}");
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
            int playerCenter = (s.X_fixed >> 8) + 1 + (hbW / 2);  // NES +1 offset
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
                // Blue pads SET gravity based on pad orientation and apply blue pad velocity.
                // Bottom pad (0x0D, 0xFD): sets gravity to inverted, launches WITH new gravity (upward)
                // Top pad (0x0E, 0xFE): sets gravity to normal, launches WITH new gravity (downward)
                // Sim uses fixed PAD_HEIGHT_BLUE constants, NOT PadOrbHeights table.
                bool isBottomPad = (sid == 0x0D || sid == 0xFD);
                s.GravFlipped = isBottomPad;
                s.GravMul = isBottomPad ? -1 : 1;
                // Blue pad velocity: launch WITH new gravity direction.
                // Sim: baseVel = -0x3A0 (negative), newVel = gravInverted ? baseVel : -baseVel
                // When inverted (bottom pad): vel = -0x3A0 (upward, WITH gravity that pulls up)
                // When normal (top pad):      vel = +0x3A0 (downward, WITH gravity that pulls down)
                int bluePadMag = s.Mini ? 0x160 : 0x3A0;
                s.VelY_fixed = s.GravFlipped ? -bluePadMag : bluePadMag;
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
                //   Ball mode uses smaller vel: ORB_BALL_HEIGHT_BLUE_normal = -0x1A0, mini = -0x60
                // Sign: if (!gravInverted) negate → launches toward new ground
                int blueVel;
                if (s.GameMode == 2) // Ball mode uses smaller blue orb velocity
                    blueVel = s.Mini ? -0x60 : -0x1A0;
                else
                    blueVel = s.Mini ? -0x160 : -0x3A0;
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
        /// Per-column floor support check, matching ProvidesFloorAtColumnStatic
        /// in SimulatorWindow.  Returns true if the collision type provides a
        /// floor at the given local X column (0..15) within the tile.
        /// </summary>
        private static bool ProvidesFloorAtColumn(MetatileCollision col, int localX)
        {
            return ProvidesFloorAtColumn(col, localX, out _);
        }

        /// <summary>
        /// Per-column floor support check with floor surface offset,
        /// matching ProvidesFloorAtColumnStatic in SimulatorWindow.
        /// topOffsetPx is the first solid pixel row within the tile (0 = top, 8 = bottom half).
        /// </summary>
        private static bool ProvidesFloorAtColumn(MetatileCollision col, int localX, out int topOffsetPx)
        {
            topOffsetPx = int.MaxValue;
            bool inLeft = (localX >= 0 && localX <= 7);
            bool inRight = (localX >= 8 && localX <= 15);

            switch (col)
            {
                case MetatileCollision.COL_ALL:
                case MetatileCollision.COL_FLOOR_CEIL:
                case MetatileCollision.COL_NO_SIDE:
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

            // Additional combined-stair behavior
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

        /// <summary>
        /// Returns true if the collision type is a slope tile.
        /// Slopes are handled by the dedicated slope collision system, not solid checks.
        /// </summary>
        private static bool IsSlopeTile(MetatileCollision col)
        {
            return col >= MetatileCollision.COL_SLOPE_RD45 && col <= MetatileCollision.COL_SLOPE_LU66_BOT;
        }

        /// <summary>
        /// Returns true for collision types that are quadrant mini-blocks.
        /// These types require BOTH localX and localY checks (handled by
        /// IsMiniBlockFloorHit) and must NOT fall through to ProvidesFloorAtColumn.
        /// </summary>
        private static bool IsMiniBlockType(MetatileCollision col)
        {
            switch (col)
            {
                case MetatileCollision.COL_UP_LEFT:
                case MetatileCollision.COL_UP_RIGHT:
                case MetatileCollision.COL_DOWN_LEFT:
                case MetatileCollision.COL_DOWN_RIGHT:
                case MetatileCollision.COL_LEFT_SPIKE_BLOCK:
                case MetatileCollision.COL_RIGHT_SPIKE_BLOCK:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// NES bg_coll_mini_blocks floor hit check.
        /// Returns true when (localX, localY) falls inside the solid quadrant of a
        /// mini-block tile.  This matches the NES code exactly:
        ///   COL_UP_LEFT:   localY &lt; 8 &amp;&amp; localX &lt; 8
        ///   COL_UP_RIGHT:  localY &lt; 8 &amp;&amp; localX >= 8
        ///   COL_DOWN_LEFT: localY >= 8 &amp;&amp; localX &lt; 8
        ///   COL_DOWN_RIGHT:localY >= 8 &amp;&amp; localX >= 8
        /// Non-quadrant tiles always return false (handled by ProvidesFloorAtColumn).
        /// </summary>
        private static bool IsMiniBlockFloorHit(MetatileCollision col, int localX, int localY)
        {
            switch (col)
            {
                case MetatileCollision.COL_UP_LEFT:
                    return (localY < 8) && (localX < 8);
                case MetatileCollision.COL_UP_RIGHT:
                    return (localY < 8) && (localX >= 8);
                case MetatileCollision.COL_DOWN_LEFT:
                case MetatileCollision.COL_LEFT_SPIKE_BLOCK:
                    return (localY >= 8) && (localX < 8);
                case MetatileCollision.COL_DOWN_RIGHT:
                case MetatileCollision.COL_RIGHT_SPIKE_BLOCK:
                    return (localY >= 8) && (localX >= 8);
                default:
                    return false;
            }
        }

        /// <summary>
        /// Returns the floor surface Y offset (within tile) for a mini-block type.
        /// UP quadrants start at row 0, DOWN quadrants start at row 8.
        /// </summary>
        private static int GetMiniBlockFloorSurface(MetatileCollision col)
        {
            switch (col)
            {
                case MetatileCollision.COL_UP_LEFT:
                case MetatileCollision.COL_UP_RIGHT:
                    return 0;
                case MetatileCollision.COL_DOWN_LEFT:
                case MetatileCollision.COL_LEFT_SPIKE_BLOCK:
                case MetatileCollision.COL_DOWN_RIGHT:
                case MetatileCollision.COL_RIGHT_SPIKE_BLOCK:
                    return 8;
                default:
                    return 0;
            }
        }

        /// <summary>
        /// Per-pixel solid occupancy check matching TileOccupiesPixel in SimulatorWindow.
        /// Returns true when the tile's collision shape occupies the pixel at (localX, localY).
        /// Used by CheckForwardCollision for accurate wall detection.
        /// </summary>
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
                // Tiles that provide floor/ceiling but NOT side collision.
                // NES bg_coll_sides() returns 0 for these — the player can
                // walk/fly through them horizontally.
                case MetatileCollision.COL_FLOOR_CEIL:
                case MetatileCollision.COL_NO_SIDE:
                    return false;
                default:
                    break;
            }

            bool inLeft = (localX >= 0 && localX <= 7);
            bool inRight = (localX >= 8 && localX <= 15);

            switch (col)
            {
                case MetatileCollision.COL_TOP:
                case MetatileCollision.COL_TOP_CENTER_SPIKE:
                    return (localY <= 7);
                case MetatileCollision.COL_TOP_LEFT_STAIRS:
                    return (localY <= 7) || (localX <= 7);
                case MetatileCollision.COL_TOP_RIGHT_STAIRS:
                    return (localY <= 7) || (localX >= 8);
                case MetatileCollision.COL_BOTTOM_LEFT_STAIRS:
                    return (localX <= 7) || (localX >= 8 && localY >= 8);
                case MetatileCollision.COL_BOTTOM_RIGHT_STAIRS:
                    return (localX >= 8) || (localX <= 7 && localY >= 8);
                case MetatileCollision.COL_BOTTOM_LEFT_SPIKE:
                    return (localY >= 8);
                case MetatileCollision.COL_BOTTOM_RIGHT_SPIKE:
                    return (localY >= 8);
                case MetatileCollision.COL_LEFT_SPIKE_BLOCK:
                    return (localX < 8 && localY >= 8);
                case MetatileCollision.COL_RIGHT_SPIKE_BLOCK:
                    return (localX >= 8 && localY >= 8);
                case MetatileCollision.COL_UP_LEFT:
                    return (inLeft && localY <= 7);
                case MetatileCollision.COL_UP_RIGHT:
                    return (inRight && localY <= 7);
                case MetatileCollision.COL_RIGHT:
                    return inRight;  // right-half column solid, left half passable
                case MetatileCollision.COL_LEFT:
                    return inLeft;   // left-half column solid, right half passable
                case MetatileCollision.COL_DOWN_LEFT:
                    return (inLeft && localY >= 8);   // bottom-left quadrant
                case MetatileCollision.COL_DOWN_RIGHT:
                    return (inRight && localY >= 8);  // bottom-right quadrant
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

            // Use floor offset to determine occupancy
            if (ProvidesFloorAtColumn(col, localX, out int topOffsetPx))
            {
                return (localY >= topOffsetPx);
            }

            // Slope tiles are NOT solid blocks
            if (IsSlopeTile(col))
                return false;

            // Pure death has no solid collision
            if (col == MetatileCollision.COL_DEATH)
                return false;

            // Fallback: non-none tiles treated as fully blocking
            if (col != MetatileCollision.COL_NONE) return true;
            return false;
        }

        /// <summary>
        /// Map tile IDs for collision, matching the special remaps from
        /// MapAnimatedTileIndex in SimulatorWindow.  Certain editor-specific
        /// tile IDs (saws, etc.) are remapped to empty or other collision types.
        /// </summary>
        private static int MapTileForCollision(int tid)
        {
            switch (tid)
            {
                case 0xFC: case 0xDF: case 0xE3: case 0xFE: case 0xFF: return 0x00;
                case 0xFD: return 0x26;
                default: return tid;
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

            // ── BOUNDS CHECK ──
            if (tileBelowY < 0 || tileBelowY >= mapHeight) return (false, 0, false);

            int tileArrayYFloor = tileBelowY + groundRowsToReserve;

            // Ground layer is always solid (clears any pending death)
            if (tileArrayYFloor >= mapHeight)
            {
                int groundTop = tileBelowY * TILE;
                return (true, groundTop, false);
            }

            // ── NES-MATCHING 3-POINT INTERLEAVED CHECK ──
            // NES bg_coll_D checks LEFT, CENTER, RIGHT probes at playerBottom.
            // At each probe, COLL_CHECK_BOTTOM calls bg_coll_return_D():
            //   - bg_coll_U_D_checks: COL_DEATH_TOP/BOTTOM → spike death (side-effect),
            //     COL_ALL/NO_SIDE/FLOOR_CEIL → solid floor (return 1)
            //   - bg_coll_mini_blocks: sub-tile solid check
            // If a solid floor is found at ANY probe, the death flag is CLEARED
            // and eject happens immediately (COLL_CHECK_BOTTOM does cube_data &= ~1).
            // This means a spike at one probe can be CANCELLED by a floor at another.
            bool deathPending = false;

            for (int probeIdx = 0; probeIdx < 3; probeIdx++)
            {
                int px = probeIdx == 0 ? playerLeft_px
                       : probeIdx == 1 ? playerLeft_px + collW / 2
                       : playerRight_px;
                int tileX = px / TILE;

                if (tileX < 0 || tileX >= mapWidth) continue;

                int tileIdx = tileArrayYFloor * mapWidth + tileX;
                if (tileIdx < 0 || tileIdx >= tiles.Length) continue;

                int tileId = tiles[tileIdx];
                var collision = MetatileCollisionTable.GetCollision((byte)tileId);
                if (collision == MetatileCollision.COL_NONE) continue;

                int localX = px % TILE;
                int localY = playerBottom_px % TILE;

                // ── SPIKE DEATH CHECK (COL_DEATH_TOP/BOTTOM, matching bg_coll_U_D_checks) ──
                if (collision == MetatileCollision.COL_DEATH_TOP || collision == MetatileCollision.COL_DEATH_BOTTOM)
                {
                    if (MetatileCollisionTable.TileKillsAtPixel(collision, localX, localY))
                    {
                        deathPending = true;
                    }
                    // Death tiles provide no floor — continue to next probe
                    continue;
                }

                // ── SOLID FLOOR CHECK (matching bg_coll_U_D_checks + bg_coll_mini_blocks) ──
                // NES bg_coll_mini_blocks checks BOTH localX and localY for quadrant
                // tiles.  E.g. COL_UP_RIGHT only provides floor when localY < 8 AND
                // localX >= 8.  If the player's bottom has fallen past the quadrant
                // boundary (localY >= 8 for an UP quadrant), the tile provides no
                // floor and the player falls through — matching NES 1:1.
                if (IsMiniBlockFloorHit(collision, localX, localY))
                {
                    int tileWorldY = tileBelowY * TILE;
                    int surfaceY = tileWorldY + GetMiniBlockFloorSurface(collision);
                    if (playerBottom_px >= surfaceY)
                    {
                        // Floor found → clears death (matches COLL_CHECK_BOTTOM: cube_data &= ~1)
                        return (true, surfaceY, false);
                    }
                }
                else if (!IsMiniBlockType(collision) && ProvidesFloorAtColumn(collision, localX, out int topOffsetPx))
                {
                    // Non-mini-block tiles: full/half slabs, stairs, etc.
                    int tileWorldY = tileBelowY * TILE;
                    int surfaceY = tileWorldY + topOffsetPx;
                    if (playerBottom_px >= surfaceY)
                    {
                        return (true, surfaceY, false);
                    }
                }
            }

            // Also check implicit ground floor (clears death)
            // NES: the ground layer acts as a solid tile row.  The player's bottom
            // must be AT or BELOW the ground surface to land — no -1 tolerance.
            // Previous code used `>= groundTopWorld_px - 1` which caused the PF
            // to eject 1 frame earlier than the NES, desynchronising replay.
            if (groundRowsToReserve > 0)
            {
                int groundTopWorld_px = (mapHeight - groundRowsToReserve) * TILE;
                if (playerBottom_px >= groundTopWorld_px)
                    return (true, groundTopWorld_px, false);
            }

            // No floor found — if spike death was pending, report it
            if (deathPending) return (false, 0, true);

            return (false, 0, false);
        }

        /// <summary>
        /// Check ceiling collision (upward). Returns (hit, ceilingBottomY).
        /// Matching CheckCollisionUp in CollisionDetection.partial.cs.
        /// </summary>
        private (bool hit, int ceilingBottomY, bool spikeDeath) CheckCeiling(int collX, int collY, int collW, int collH)
        {
            int topY = collY;

            // ── SPIKE DEATH PRE-CHECK (3 points near top edge) ──
            // Matches sim's CheckCollisionUp: checkY = playerTop_px + 1
            // NES bg_coll_U checks 3 X-points at Y = top + 1 for
            // COL_DEATH_TOP / COL_DEATH_BOTTOM tiles.
            {
                int checkY = topY + 1;
                for (int cpIdx = 0; cpIdx < 3; cpIdx++)
                {
                    int px = cpIdx == 0 ? collX
                           : cpIdx == 1 ? collX + collW / 2
                           : collX + collW;
                    int tileX = px / TILE;
                    int tileY = checkY / TILE;

                    if (tileX < 0 || tileX >= mapWidth || tileY < 0 || tileY >= mapHeight) continue;

                    int tileArrayY_chk = tileY + groundRowsToReserve;
                    if (tileArrayY_chk < 0 || tileArrayY_chk >= mapHeight) continue;

                    int tileIdx = tileArrayY_chk * mapWidth + tileX;
                    if (tileIdx < 0 || tileIdx >= tiles.Length) continue;

                    int tileId = tiles[tileIdx];
                    var collision = MetatileCollisionTable.GetCollision((byte)tileId);

                    int localX = px % TILE;
                    int localY = checkY % TILE;

                    if ((collision == MetatileCollision.COL_DEATH_TOP || collision == MetatileCollision.COL_DEATH_BOTTOM) &&
                        MetatileCollisionTable.TileKillsAtPixel(collision, localX, localY))
                    {
                        return (false, 0, true); // spike death!
                    }
                }
            }

            // Floor division: C# truncates toward zero, so (-1)/16 = 0 instead of -1.
            // Use proper floor division for correct tile lookup at Y=0.
            int valC = topY - 1;
            int tileAboveY = valC >= 0 ? valC / TILE : (valC - TILE + 1) / TILE;
            if (tileAboveY < 0) return (false, 0, false);

            int tileLeftX = collX / TILE;
            // NES bg_coll_U also checks at playerX+width; match with collW not collW-1.
            int tileRightX = (collX + collW) / TILE;
            int tileArrayY = tileAboveY + groundRowsToReserve;
            if (tileArrayY < 0 || tileArrayY >= mapHeight) return (false, 0, false);

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
                        return (true, colBottom_px, false);
                }
            }

            return (false, 0, false);
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
            int mappedTid = MapTileForCollision(tileId);
            var collision = MetatileCollisionTable.GetCollision((byte)mappedTid);

            int tileStartX = tileX * TILE;
            int tileStartY = (tileArrayY - groundRowsToReserve) * TILE;
            int localX = Math.Max(0, Math.Min(TILE - 1, centerX_px - tileStartX));
            int localY = Math.Max(0, Math.Min(TILE - 1, centerY_px - tileStartY));

            return MetatileCollisionTable.TileKillsAtPixel(collision, localX, localY);
        }

        /// <summary>
        /// Helper: check if a single world-pixel coordinate hits a deadly spike tile.
        /// Shared by CheckFloorSpikes and other multi-point death checks.
        /// </summary>
        private bool PointKillsPlayer(int px, int py
#if !DISABLE_DEBUG_LOGGING
            , out int dbg_tid, out int dbg_mappedTid, out MetatileCollision dbg_col, out int dbg_localX, out int dbg_localY
#endif
        )
        {
#if !DISABLE_DEBUG_LOGGING
            dbg_tid = 0; dbg_mappedTid = 0; dbg_col = 0; dbg_localX = 0; dbg_localY = 0;
#endif
            int tileX = px / TILE;
            int tileY = py / TILE;
            int tileArrayY = tileY + groundRowsToReserve;

            if (tileX < 0 || tileX >= mapWidth) return false;
            if (tileArrayY < 0 || tileArrayY >= mapHeight) return false;

            int tileIdx = tileArrayY * mapWidth + tileX;
            if (tileIdx < 0 || tileIdx >= tiles.Length) return false;

            int tid = tiles[tileIdx];
            int mappedTid = MapTileForCollision(tid);
            var col = MetatileCollisionTable.GetCollision((byte)mappedTid);

            int tileStartX = tileX * TILE;
            int tileStartY = (tileArrayY - groundRowsToReserve) * TILE;
            int localX = Math.Max(0, Math.Min(TILE - 1, px - tileStartX));
            int localY = Math.Max(0, Math.Min(TILE - 1, py - tileStartY));

#if !DISABLE_DEBUG_LOGGING
            dbg_tid = tid; dbg_mappedTid = mappedTid; dbg_col = col; dbg_localX = localX; dbg_localY = localY;
#endif
            return MetatileCollisionTable.TileKillsAtPixel(col, localX, localY);
        }

        /// <summary>
        /// 4-corner spike check matching NES bg_coll_floor_spikes (collision.h line 214).
        /// Checks 4 inset corners of the hitbox for ALL spike types via TileKillsAtPixel.
        /// NES runs this in x_movement_coll at OLD X, post-eject Y — same timing as
        /// CheckForwardCollision in The PF.
        ///
        /// Normal cube (15×15): corners at (X+3, Y+13), (X+12, Y+13), (X+3, Y+2), (X+12, Y+2)
        /// Mini cube   (8×7):   corners at (X+3, Y+9),  (X+5, Y+9),  (X+3, Y+4), (X+5, Y+4)
        ///
        /// NES Y offsets:
        ///   "TOP" row (commonly_used_store):    Y = Generic.y + (mini ? (0x10-h)>>1 : 0) + h - 2
        ///   "BOTTOM" row (commonly_stored_2):   Y = Generic.y + (mini ? (0x10-h)>>1 : 2)
        /// X offsets: left = X+3, right = X+3+(w-6) = X+w-3
        /// </summary>
        private bool CheckFloorSpikes(ref SimState s)
        {
            int playerX = s.X_fixed >> 8;
            int playerY = s.Y_fixed >> 8;
            int hbW = GetHitboxW(s.Mini);
            int hbH = GetHitboxH(s.Mini);

            // NES mini centering offset: (0x10 - height) >> 1
            int miniOffY = s.Mini ? ((0x10 - hbH) >> 1) : 0;

            // "TOP" row in NES — near bottom of hitbox
            int topRowY = playerY + miniOffY + hbH - 2;
            // "BOTTOM" row in NES — near top of hitbox
            int botRowY = playerY + (s.Mini ? miniOffY : 2);

            // X inset: +3 from left, width-3 from left (= +3 + (width-6))
            int leftX = playerX + 3;
            int rightX = playerX + hbW - 3;

            // Check all 4 corners — same order as NES bg_coll_floor_spikes
#if !DISABLE_DEBUG_LOGGING
            int _tid, _mtid, _lx, _ly; MetatileCollision _col;
#endif
            if (PointKillsPlayer(leftX, topRowY
#if !DISABLE_DEBUG_LOGGING
                , out _tid, out _mtid, out _col, out _lx, out _ly
#endif
            ))
            {
#if !DISABLE_DEBUG_LOGGING
                PfLog($"[FLOOR_SPIKE_DEATH] corner TL ({leftX},{topRowY}) tid=0x{_tid:X2} mapped=0x{_mtid:X2} col={_col} localXY=({_lx},{_ly})");
                _lastDeathReason = $"FLOOR_SPIKE:TL({leftX},{topRowY})";
                _lastDeathX = playerX; _lastDeathY = playerY;
#endif
                return true;
            }
            if (PointKillsPlayer(rightX, topRowY
#if !DISABLE_DEBUG_LOGGING
                , out _tid, out _mtid, out _col, out _lx, out _ly
#endif
            ))
            {
#if !DISABLE_DEBUG_LOGGING
                PfLog($"[FLOOR_SPIKE_DEATH] corner TR ({rightX},{topRowY}) tid=0x{_tid:X2} mapped=0x{_mtid:X2} col={_col} localXY=({_lx},{_ly})");
                _lastDeathReason = $"FLOOR_SPIKE:TR({rightX},{topRowY})";
                _lastDeathX = playerX; _lastDeathY = playerY;
#endif
                return true;
            }
            if (PointKillsPlayer(leftX, botRowY
#if !DISABLE_DEBUG_LOGGING
                , out _tid, out _mtid, out _col, out _lx, out _ly
#endif
            ))
            {
#if !DISABLE_DEBUG_LOGGING
                PfLog($"[FLOOR_SPIKE_DEATH] corner BL ({leftX},{botRowY}) tid=0x{_tid:X2} mapped=0x{_mtid:X2} col={_col} localXY=({_lx},{_ly})");
                _lastDeathReason = $"FLOOR_SPIKE:BL({leftX},{botRowY})";
                _lastDeathX = playerX; _lastDeathY = playerY;
#endif
                return true;
            }
            if (PointKillsPlayer(rightX, botRowY
#if !DISABLE_DEBUG_LOGGING
                , out _tid, out _mtid, out _col, out _lx, out _ly
#endif
            ))
            {
#if !DISABLE_DEBUG_LOGGING
                PfLog($"[FLOOR_SPIKE_DEATH] corner BR ({rightX},{botRowY}) tid=0x{_tid:X2} mapped=0x{_mtid:X2} col={_col} localXY=({_lx},{_ly})");
                _lastDeathReason = $"FLOOR_SPIKE:BR({rightX},{botRowY})";
                _lastDeathX = playerX; _lastDeathY = playerY;
#endif
                return true;
            }

            return false;
        }

        /// <summary>
        /// CheckDeathCollision — center-point death check matching NES bg_coll_death.
        /// Only checks the center probe: (x + (width>>1) - 1, y + (height>>1)).
        /// </summary>
        private bool CheckDeathCollision(ref SimState s)
        {
            int playerX_px = s.X_fixed >> 8;
            int playerY_px = s.Y_fixed >> 8;
            int hbW = GetHitboxW(s.Mini);
            int hbH = GetHitboxH(s.Mini);
            int hbOffY = GetHitboxOffsetY(s.Mini, s.GravFlipped);

            // Center probe only — matches NES bg_coll_death
            int centerX = playerX_px + (hbW >> 1) - 1;
            int centerY = playerY_px + (hbH / 2) + hbOffY;

            int tileX = centerX / TILE;
            int tileY = centerY / TILE;
            int tileArrayY = tileY + groundRowsToReserve;

            if (tileX < 0 || tileX >= mapWidth) return false;
            if (tileArrayY < 0 || tileArrayY >= mapHeight) return false;

            int tileIdx = tileArrayY * mapWidth + tileX;
            if (tileIdx < 0 || tileIdx >= tiles.Length) return false;

            int tid = tiles[tileIdx];
            int mappedTid = MapTileForCollision(tid);
            var col = MetatileCollisionTable.GetCollision((byte)mappedTid);

            int tileStartX = tileX * TILE;
            int tileStartY = (tileArrayY - groundRowsToReserve) * TILE;
            int localX = Math.Max(0, Math.Min(TILE - 1, centerX - tileStartX));
            int localY = Math.Max(0, Math.Min(TILE - 1, centerY - tileStartY));

            if (MetatileCollisionTable.TileKillsAtPixel(col, localX, localY))
            {
#if !DISABLE_DEBUG_LOGGING
                PfLog($"[DEATH_POINT] center ({centerX},{centerY}) tile=({tileX},{tileArrayY}) tid=0x{tid:X2} col={col}");
                _lastDeathReason = $"DEATH_COLL:center({centerX},{centerY})tile({tileX},{tileArrayY})tid=0x{tid:X2}col={col}";
                _lastDeathX = s.X_fixed >> 8; _lastDeathY = s.Y_fixed >> 8;
#endif
                return true;
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
            // NES and sim skip bg_coll_R entirely when currplayer_was_on_slope_counter
            // or currplayer_slope_frames is non-zero (ShouldSkipSideCollisionForSlope).
            // The PF doesn't track slope state, so use a heuristic: check if any tile
            // near the player's feet is a slope tile, and if so skip forward collision.
            if (HasSlopeNearFeet(s))
                return false;

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
            int mappedTid = MapTileForCollision(tileId);
            var collision = MetatileCollisionTable.GetCollision((byte)mappedTid);

            int tileWorldX = tileX * TILE;
            int tileWorldY = tileY * TILE;
            int localX = Math.Max(0, Math.Min(TILE - 1, rightEdge_px - tileWorldX));
            int localY = Math.Max(0, Math.Min(TILE - 1, centerY_px - tileWorldY));

            // Per-pixel solid occupancy check matching sim's TileOccupiesPixel
            if (TileOccupiesPixel(collision, localX, localY))
            {
#if !DISABLE_DEBUG_LOGGING
                PfLog($"[FWD_COLL] probe=({rightEdge_px},{centerY_px}) tile=({tileX},{tileY}) tid=0x{tileId:X2} col={collision} oldX={playerX_px} newX={(s.X_fixed + s.VelX_fixed) >> 8}");
#endif
                return true; // blocked → death
            }

            // NES bg_side_coll_common: if bg_coll_spikes() is true at the forward
            // probe, it returns 0 (NO collision) — spikes are filtered out so the
            // player passes through them. Only solid walls (bg_coll_sides /
            // bg_coll_mini_blocks) count as forward collisions.
            // So do NOT kill the player here for spikes — only the center-point
            // death check (bg_coll_death / CheckDeathCollision) handles spike kills.

            return false;
        }

        /// <summary>
        /// Heuristic slope proximity check: scan tiles at and below the player's
        /// feet for slope tiles. The NES/sim skip side collision for several frames
        /// after the player was on a slope (currplayer_was_on_slope_counter |
        /// currplayer_slope_frames). Since the PF doesn't track slope state,
        /// this approximation checks the current floor region for slopes.
        /// </summary>
        private bool HasSlopeNearFeet(SimState s)
        {
            int playerX_px = s.X_fixed >> 8;
            int playerY_px = s.Y_fixed >> 8;
            int hbW = GetHitboxW(s.Mini);
            int hbH = GetHitboxH(s.Mini);
            int hbOffY = GetHitboxOffsetY(s.Mini, s.GravFlipped);

            // Check tiles at and below the player's bottom edge
            int bottomY_px = playerY_px + hbOffY + hbH;
            int leftX_px = playerX_px;
            int rightX_px = playerX_px + hbW;

            int tileLeft = leftX_px / TILE;
            int tileRight = rightX_px / TILE;
            int tileYBase = bottomY_px / TILE;

            // Scan 2 rows (at feet and one below) and all columns under the player
            for (int dy = 0; dy <= 1; dy++)
            {
                int ty = tileYBase + dy;
                int tileArrayY = ty + groundRowsToReserve;
                if (tileArrayY < 0 || tileArrayY >= mapHeight) continue;
                for (int tx = tileLeft; tx <= tileRight; tx++)
                {
                    if (tx < 0 || tx >= mapWidth) continue;
                    int idx = tileArrayY * mapWidth + tx;
                    if (idx < 0 || idx >= tiles.Length) continue;
                    int tid = tiles[idx];
                    int mapped = MapTileForCollision(tid);
                    var col = MetatileCollisionTable.GetCollision((byte)mapped);
                    if (IsSlopeTile(col)) return true;
                }
            }
            return false;
        }
    }
}
