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
        private const int MAX_BACKTRACK_ATTEMPTS = 500; // max backtrack retries PER death point (reset after each success)
        private const int MAX_TOTAL_BACKTRACK_ATTEMPTS = 5000; // absolute cap on total backtracks across all deaths
        private const int MAX_TOTAL_ITERATIONS = MAX_FRAMES * 10; // hard cap on total frame iterations (including replays)
        private const int MAX_CHECKPOINT_DEPTH = 200;   // max saved decision checkpoints (deep history for long levels)
        // No rewind limit — backtracker goes as far back as needed
        private const int MIN_CHECKPOINT_SPACING = 4;   // minimum frames between consecutive checkpoints
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
        /// Indices of pads that the pathfinder decided to skip (not activate).
        /// These must be communicated to the simulator so it also skips them.
        /// </summary>
        public HashSet<int>? SkippedPadIndices { get; private set; }

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
        private const int BALL_INPUT_BUFFER_FRAMES = 8; // Must match PF_BALL_HOLD_FRAMES in Pathfinder.partial.cs

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

        /// <summary>
        /// Extract skipped pad indices from the final state's ProcessedSprites.
        /// Pads are never added to ProcessedSprites when activated (they fire every frame),
        /// so any pad index in ProcessedSprites was intentionally skipped by the pathfinder.
        /// </summary>
        private void ExtractSkippedPads(in SimState finalState)
        {
            SkippedPadIndices = new HashSet<int>();
            if (finalState.ProcessedSprites == null) return;
            foreach (int idx in finalState.ProcessedSprites)
            {
                if (idx < 0 || idx >= sprites.Length) continue;
                int sid = sprites[idx];
                if (IsAnyPad(sid))
                {
                    SkippedPadIndices.Add(idx);
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[SKIPPED_PAD] idx={idx} sid=0x{sid:X2}");
#endif
                }
            }
        }
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
            // Use current X (pre-advance) to match NES sprite_collide timing.
            // Must use nesX = currentX_px + 1 for the right edge, matching
            // ProcessSprites' calculation (Generic.x = high_byte(currplayer_x) + 1).
            int currentX_px = state.X_fixed >> 8;
            int nesX = currentX_px + 1;
            int hbW = GetHitboxW(state.Mini);
            int hbH = GetHitboxH(state.Mini);
            int hbOffY = GetHitboxOffsetY(state.Mini, state.GravFlipped);
            int playerY_px = state.Y_fixed >> 8;
            int playerTop = playerY_px + hbOffY;
            int playerBottom = playerTop + hbH;
            int playerRight = nesX + hbW;

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

        /// <summary>
        /// Scan for a pad sprite overlapping the player at the current position.
        /// Matches ProcessSprites pad detection hitbox exactly.
        /// Returns the pad sprite ID (>= 0) if found, or -1 if none.
        /// </summary>
        private int ScanForPadOverlap(in SimState state, out int padSpriteIndex)
        {
            padSpriteIndex = -1;
            int currentX_px = state.X_fixed >> 8;
            int hbW = GetHitboxW(state.Mini);
            int hbH = GetHitboxH(state.Mini);
            int hbOffY = GetHitboxOffsetY(state.Mini, state.GravFlipped);
            int playerY_px = state.Y_fixed >> 8;
            int playerTop = playerY_px + hbOffY;
            int playerBottom = playerTop + hbH;

            foreach (var sp in allSprites)
            {
                if (state.ProcessedSprites.Contains(sp.Index)) continue;
                if (sp.HitRight <= currentX_px) continue;
                int nesX = currentX_px + 1;
                int farRight = nesX + hbW;
                if (sp.AnchorX_px - TILE > farRight + TILE) break;

                int sid = sp.SpriteId;
                if (!(IsYellowPad(sid) || IsPinkPad(sid) || IsRedPad(sid) || IsBluePad(sid) || IsGreenPad(sid)))
                    continue;

                // Blue pads have a gravity gate: bottom pads only activate when
                // gravity is normal, top pads only when gravity is inverted.
                if (IsBluePad(sid))
                {
                    bool isBottomBluePad = (sid == 0x0D || sid == 0xFD);
                    if (isBottomBluePad && state.GravFlipped) continue;   // bottom pad needs normal grav
                    if (!isBottomBluePad && !state.GravFlipped) continue; // top pad needs inverted grav
                }

                // Blue pads use plain currentX_px; others use nesX (currentX_px + 1)
                int padXOffset = IsBluePad(sid) ? 0 : 1;
                int padLeft = currentX_px + padXOffset;
                int padRight = padLeft + hbW;
                bool xOverlap = !(padRight < sp.HitLeft || sp.HitRight < padLeft);
                bool yOverlap = !(playerBottom < sp.HitTop || sp.HitBottom < playerTop);

                if (xOverlap && yOverlap)
                {
                    padSpriteIndex = sp.Index;
                    return sid;
                }
            }
            return -1;
        }

        private static bool IsAnyPad(int sid) =>
            IsYellowPad(sid) || IsPinkPad(sid) || IsRedPad(sid) || IsBluePad(sid) || IsGreenPad(sid);

        // ── Hold-jump state (persists across frames in the main loop) ────
        private bool _cubeHoldJump = false; // when true, keep jumping every landing
        private int _cubeHoldDelay = 0;    // frames to wait before first jump in hold mode
        private int _committedJumpDelay = -1; // when >= 0, counting down to a committed single-jump
        private bool _prevFrameWasGrounded = true; // tracks whether PREVIOUS frame started grounded; used to gate hold-jump fast path

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
            public bool SkipAllOrbs;       // _btSkipAllOrbs at checkpoint
            public HashSet<int> SkipSpecificOrbs = new(); // _btSkipSpecificOrbs at checkpoint
            public HashSet<int> SkipSpecificPads = new(); // _btSkipSpecificPads at checkpoint
            public bool PrevFrameWasGrounded; // _prevFrameWasGrounded at checkpoint
        }
        private List<BacktrackCheckpoint> _backtrackCheckpoints = new();
        private int _backtrackAttempts;
        private int _totalBacktrackAttempts;
        private int _btOverrideFrame = -1;  // frame at which to apply override
        private int _btOverrideStage = 0;   // which alternative to try
        private int _btOverrideDistFromDeath; // frames between override checkpoint and death (for adaptive forcing)
        private bool _backtrackActive;      // true while replaying from a checkpoint (suppress new checkpoints)
        private bool _btSuppressJumpUntilAirborne; // stage-1 no-jump persists until cube falls off edge
        private bool _btSkipAllOrbs;         // when true, cube orb decision always skips orbs (trick orb strategy)
        private HashSet<int> _btSkipSpecificOrbs = new(); // specific orb indices to skip (targeted trick orb strategy)
        private List<int> _hitOrbHistory = new();          // orb indices hit during current run (for targeted backtrack)
        private HashSet<int> _btSkipSpecificPads = new(); // specific pad indices to skip (targeted pad strategy)
        private List<int> _hitPadHistory = new();          // pad indices hit during current run (for targeted backtrack)
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
        private const int MAX_BACKTRACK_SECONDS_COIN = 120; // extended time limit for coin retry backtracking

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

        // Coin-specific list (subset of allSprites with coin sprite IDs)
        private readonly List<SpriteEntry> allCoins;
        // Next coin index to check for collection/miss detection
        private int _nextCoinCheckIdx;
        // Coins that were missed and forgiven (backtrack failed, continue without them)
        private readonly HashSet<int> _forgivenCoins = new HashSet<int>();
        // Game mode at the time each coin was forgiven (0=cube,1=ship,2=ball,3=ufo,etc.)
        private readonly Dictionary<int, int> _forgivenCoinGameModes = new Dictionary<int, int>();
        // Permanently collected coins — persists across backtracks so that undoing
        // a path segment doesn't lose a coin that was already collected.  After
        // restoring from a checkpoint, these indices are re-added to ProcessedSprites.
        private readonly HashSet<int> _permanentlyCollectedCoins = new HashSet<int>();
        // Index into allCoins of the most recently missed coin (for forgiveness)
        private int _missedCoinIdx = -1;
        // Altitude ceiling penalties for coin retry
        // Each entry: (startX, endX, ceilingY) — penalize paths above ceilingY in this X range
        private List<(int startX, int endX, int ceilingY)> _coinAltitudePenalties = new List<(int, int, int)>();
        // Force-walk zones for coin retry — forces WALK when grounded in this X range
        // to ensure pad activation (e.g., yellow pad → coin launch, blue pad → gravity flip)
        // Each entry: (startX, endX)
        private List<(int startX, int endX)> _coinForceWalkZones = new List<(int, int)>();
        // Ground-level bias zones for coin retry — strongly prefer WALK (stay at ground
        // level) but allow jumping when walking would die within 3 frames. This keeps
        // the player on the ground-level route while still clearing spikes/obstacles.
        // Each entry: (startX, endX)
        private List<(int startX, int endX)> _coinGroundBiasZones = new List<(int, int)>();
        // Mandatory coin indices for retry — coins that MUST be collected.
        // When a missed coin is in this set, forgiveness is denied and
        // crossing past the coin's X is treated as a permanent death.
        private HashSet<int> _retryMandatoryCoins = new HashSet<int>();
        // Ship coin aggressive target — when set (>= 0), the ship's tree search
        // allows larger survival differences to be overridden by coin steering.
        // Also used during coin retry to force the ship toward a specific Y.
        private int _shipCoinAggressiveThreshold = 3; // default: 3 frames tolerance
        // Coin input script: a pre-recorded input sequence from Strategy 0's
        // simulation. When the coin-walk override fires, we record ALL frames'
        // inputs (not just the first). On subsequent frames, we dequeue from
        // this script instead of running the BFS, ensuring the actual execution
        // matches the speculative simulation exactly.
        private Queue<bool> _coinInputScript = new Queue<bool>();
        private int _coinInputScriptCoinIdx = -1; // coin index this script targets
        private int _beamSearchAttemptedCoinIdx = -1; // coin index last beam-searched (avoid re-running)
        private int _cubeBeamSearchAttemptedCoinIdx = -1; // coin index last cube-beam-searched (avoid re-running)
        // Cross-corridor coins: coins behind a game-mode portal, pre-forgiven
        // at init so the ship PD doesn't steer toward them. Maps coin index
        // to the game mode active at the coin's position (for un-forgiving
        // when the pathfinder reaches that mode).
        private readonly Dictionary<int, int> _crossCorrForgivenCoins = new Dictionary<int, int>();

        // Coin sprite IDs: 0x07 (secret coin), 0x1A, 0x1B
        private static bool IsCoinSprite(int sid) => sid == 0x07 || sid == 0x1A || sid == 0x1B;

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
            public int BallInputBuffer;          // remaining frames to try buffered flip (0 = inactive)
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

            // Build sorted coin list for miss detection and coin-aware pathfinding
            allCoins = allSprites.Where(sp => IsCoinSprite(sp.SpriteId)).ToList();

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

            // ── Pre-apply coin zones on the FIRST run ──
            // Analyze all coins and nearby pads to set up force-walk and
            // altitude-penalty zones BEFORE the first attempt. This way
            // the pathfinder collects coins on the first run instead of
            // requiring an expensive second-pass retry.
            if (PreferCoins && allCoins.Count > 0)
            {
                int groundY_pre = (mapHeight - groundRowsToReserve) * 16 - 15;
                var preForceWalk = new List<(int startX, int endX)>();
                var preAltPenalties = new List<(int startX, int endX, int ceilingY)>();
                foreach (var coin in allCoins)
                {
                    int cx = (coin.HitLeft + coin.HitRight) / 2;
                    foreach (var sp in allSprites)
                    {
                        if (!IsAnyPad(sp.SpriteId)) continue;
                        int padCX = (sp.HitLeft + sp.HitRight) / 2;
                        if (padCX > cx + 32 || cx - padCX > 500) continue;
                        int padCY = (sp.HitTop + sp.HitBottom) / 2;
                        if (padCY < groundY_pre - 40) continue;
                        bool isGP = (padCY >= groundY_pre - 30);
                        if (isGP && !IsBluePad(sp.SpriteId))
                            preForceWalk.Add((sp.HitLeft - 48, sp.HitRight + 16));
                        if (isGP)
                            preAltPenalties.Add((sp.HitLeft - 600, sp.HitLeft, groundY_pre - 20));
                    }
                }
                if (preForceWalk.Count > 0 || preAltPenalties.Count > 0)
                {
                    Console.Error.WriteLine($"[COIN_PRE_ZONES] forceWalk={preForceWalk.Count} altPenalty={preAltPenalties.Count} — applying on first run");
                    _coinForceWalkZones = preForceWalk;
                    _coinAltitudePenalties = preAltPenalties;
                }
            }

            RunSingleAttempt(startX_px, startY_px, startSpeedUiIndex,
                             startGameMode, startGravFlipped, startMini);

            // Clear pre-applied zones after first run
            _coinForceWalkZones.Clear();
            _coinAltitudePenalties.Clear();

            // ── Coin retry with mandatory coins + ground-bias zones ──
            // If the first run missed coins despite pre-applied zones,
            // retry with focused zones. Usually the first run collects
            // all coins and this section is skipped entirely.
            if (Success && PreferCoins && _forgivenCoins.Count > 0 && allCoins.Count > 0)
            {
                int groundY = (mapHeight - groundRowsToReserve) * 16 - 15;
                // Snapshot the current best result
                var overallBestInputs = new List<bool>(Inputs);
                var overallBestPath = new List<(int x, int y)>(PathPoints);
                string overallBestMsg = ResultMessage;
                int overallBestCoins = FinalCollectedCoinIndices != null ? FinalCollectedCoinIndices.Count : 0;
                var overallBestFinalCoins = FinalCollectedCoinIndices != null
                    ? new HashSet<int>(FinalCollectedCoinIndices) : null;

                // Build per-coin zones for each forgiven coin
                var forgivenCoinList = allCoins.Where(c => _forgivenCoins.Contains(c.Index)).ToList();

                // Filter to only cube-mode coins for zone-based retries.
                // Ship/ball/UFO coins can't benefit from force-walk or altitude
                // penalty zones (those only affect cube BFS). Including non-cube
                // coins' nearby pads creates zones that DISRUPT cube-mode paths,
                // losing previously-collected cube coins.
                var cubeForgiven = forgivenCoinList.Where(c =>
                    _forgivenCoinGameModes.TryGetValue(c.Index, out int gm) && gm == 0).ToList();

                // === COMBINED RETRY: try all forgiven coins' zones simultaneously ===
                // When multiple coins are forgiven, their per-coin zones may not
                // interfere (at different X positions). Running a single retry with
                // all zones combined gives the pathfinder the best chance to collect
                // ALL missed coins in one run.
                if (cubeForgiven.Count > 1)
                {
                    var combinedForceWalk = new List<(int startX, int endX)>();
                    var combinedAltPenalties = new List<(int startX, int endX, int ceilingY)>();
                    bool anyPads = false;
                    foreach (var c in cubeForgiven)
                    {
                        int cx = (c.HitLeft + c.HitRight) / 2;
                        foreach (var sp in allSprites)
                        {
                            if (!IsAnyPad(sp.SpriteId)) continue;
                            int padCX = (sp.HitLeft + sp.HitRight) / 2;
                            if (padCX > cx + 32 || cx - padCX > 500) continue;
                            int padCY = (sp.HitTop + sp.HitBottom) / 2;
                            if (padCY < groundY - 40) continue;
                            anyPads = true;
                            bool isGP = (padCY >= groundY - 30);
                            if (isGP && !IsBluePad(sp.SpriteId))
                                combinedForceWalk.Add((sp.HitLeft - 48, sp.HitRight + 16));
                            if (isGP)
                                combinedAltPenalties.Add((sp.HitLeft - 600, sp.HitLeft, groundY - 20));
                        }
                    }
                    if (anyPads)
                    {
                        Console.Error.WriteLine($"[COIN_RETRY_COMBINED] Attempting {cubeForgiven.Count} cube-mode forgiven coins, forceWalk={combinedForceWalk.Count} altPenalty={combinedAltPenalties.Count}");
                        JumpTimingBias = originalBias;
                        _coinForceWalkZones = combinedForceWalk;
                        _coinAltitudePenalties = combinedAltPenalties;
                        _coinGroundBiasZones.Clear();
                        _retryMandatoryCoins.Clear();
                        RunSingleAttempt(startX_px, startY_px, startSpeedUiIndex,
                                         startGameMode, startGravFlipped, startMini);
                        if (Success)
                        {
                            int retryCoins = FinalCollectedCoinIndices != null ? FinalCollectedCoinIndices.Count : 0;
                            Console.Error.WriteLine($"[COIN_RETRY_COMBINED] Result: {retryCoins}/{allCoins.Count} coins");
                            if (retryCoins > overallBestCoins)
                            {
                                Console.Error.WriteLine($"[COIN_RETRY_COMBINED] Improved: {retryCoins} vs {overallBestCoins}");
                                overallBestInputs = new List<bool>(Inputs);
                                overallBestPath = new List<(int x, int y)>(PathPoints);
                                overallBestMsg = ResultMessage;
                                overallBestCoins = retryCoins;
                                overallBestFinalCoins = FinalCollectedCoinIndices != null
                                    ? new HashSet<int>(FinalCollectedCoinIndices) : null;
                            }
                        }
                        else
                        {
                            Success = true; // keep going
                        }
                        JumpTimingBias = originalBias;
                        _coinForceWalkZones.Clear();
                        _coinAltitudePenalties.Clear();
                        _coinGroundBiasZones.Clear();
                        _retryMandatoryCoins.Clear();
                    }
                }

                foreach (var coin in forgivenCoinList)
                {
                    // Skip individual retries if combined retry already got all coins
                    if (overallBestCoins >= allCoins.Count) break;

                    // Skip non-cube-mode coins — force-walk and altitude zones
                    // only affect cube BFS and can disrupt other sections.
                    if (_forgivenCoinGameModes.TryGetValue(coin.Index, out int coinGm) && coinGm != 0)
                    {
                        Console.Error.WriteLine($"[COIN_RETRY_SKIP] coin idx={coin.Index} sid=0x{coin.SpriteId:X2} gameMode={coinGm} — skip non-cube retry");
                        continue;
                    }

                    int coinCenterX = (coin.HitLeft + coin.HitRight) / 2;
                    int coinCenterY = (coin.HitTop + coin.HitBottom) / 2;

                    var thisCoinForceWalk = new List<(int startX, int endX)>();
                    var thisCoinAltPenalties = new List<(int startX, int endX, int ceilingY)>();

                    // Find pads near this coin (at ground level)
                    bool hasPads = false;
                    foreach (var sp in allSprites)
                    {
                        if (!IsAnyPad(sp.SpriteId)) continue;
                        int padCenterX = (sp.HitLeft + sp.HitRight) / 2;
                        if (padCenterX > coinCenterX + 32) continue;
                        if (coinCenterX - padCenterX > 500) continue;
                        int padCenterY = (sp.HitTop + sp.HitBottom) / 2;
                        if (padCenterY < groundY - 40) continue;
                        hasPads = true;
                        Console.Error.WriteLine($"[COIN_RETRY_PAD] coin={coin.Index} pad idx={sp.Index} sid=0x{sp.SpriteId:X2} hit=({sp.HitLeft},{sp.HitTop})-({sp.HitRight},{sp.HitBottom})");
                        // Force-walk zone: only for pads AT ground level (within 30px
                        // of groundY). Elevated pads shouldn't have walk forcing.
                        // Blue pads are EXCLUDED: players typically approach blue pads
                        // from staircases and need the freedom to jump off them.
                        // Force-walking on the staircase can cause CENTER_DEATH on
                        // hazard tiles adjacent to the staircase edge.
                        bool isGroundPad = (padCenterY >= groundY - 30);
                        if (isGroundPad && !IsBluePad(sp.SpriteId))
                        {
                            thisCoinForceWalk.Add((sp.HitLeft - 48, sp.HitRight + 16));
                        }
                        // Altitude penalty zone: only for ground-level pads.
                        // Gently bias BFS toward ground level in the approach area
                        // before the pad. Uses ceiling = groundY - 20 so penalty
                        // applies when player is above ground. ENDS at the pad start
                        // (HitLeft) — MUST NOT extend past the pad, because post-pad
                        // the player is launched upward and needs free BFS navigation.
                        if (isGroundPad)
                        {
                            int ceilingY = groundY - 20; // 349 for groundY=369
                            thisCoinAltPenalties.Add((sp.HitLeft - 600, sp.HitLeft, ceilingY));
                        }
                    }

                    if (!hasPads)
                    {
                        continue;
                    }

                    // Try with mandatory coin + zones at different biases
                    double[] retryBiases = new[] { originalBias, 0.0, 1.0 };
                    foreach (double retryBias in retryBiases)
                    {
                        Console.Error.WriteLine($"[COIN_RETRY] Attempting coin idx={coin.Index} sid=0x{coin.SpriteId:X2} bias={retryBias:F2} forceWalk={thisCoinForceWalk.Count} altPenalty={thisCoinAltPenalties.Count}");

                        JumpTimingBias = retryBias;
                        _coinForceWalkZones = thisCoinForceWalk;
                        _coinAltitudePenalties = thisCoinAltPenalties;
                        _coinGroundBiasZones.Clear();
                        _retryMandatoryCoins.Clear();
                        RunSingleAttempt(startX_px, startY_px, startSpeedUiIndex,
                                         startGameMode, startGravFlipped, startMini);

                        if (Success)
                        {
                            int retryCoins = FinalCollectedCoinIndices != null ? FinalCollectedCoinIndices.Count : 0;
                            Console.Error.WriteLine($"[COIN_RETRY] Retry result: {retryCoins}/{allCoins.Count} coins");
                            if (retryCoins > overallBestCoins)
                            {
                                Console.Error.WriteLine($"[COIN_RETRY] Improved: {retryCoins} vs {overallBestCoins}");
                                overallBestInputs = new List<bool>(Inputs);
                                overallBestPath = new List<(int x, int y)>(PathPoints);
                                overallBestMsg = ResultMessage;
                                overallBestCoins = retryCoins;
                                overallBestFinalCoins = FinalCollectedCoinIndices != null
                                    ? new HashSet<int>(FinalCollectedCoinIndices) : null;
                                break; // this coin is improved, move to next coin
                            }
                        }
                        else
                        {
                            Console.Error.WriteLine($"[COIN_RETRY] Retry for coin {coin.Index} bias={retryBias:F2} FAILED");
                            Success = true; // keep going
                        }
                    }

                    JumpTimingBias = originalBias;
                    _coinForceWalkZones.Clear();
                    _coinAltitudePenalties.Clear();
                    _coinGroundBiasZones.Clear();
                    _retryMandatoryCoins.Clear();
                }

                // Restore overall best result
                Inputs = overallBestInputs;
                PathPoints = overallBestPath;
                ResultMessage = overallBestMsg;
                FinalCollectedCoinIndices = overallBestFinalCoins;
            }

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
            _btSkipAllOrbs = false;
            _btSkipSpecificOrbs.Clear();
            _hitOrbHistory.Clear();
            _btSkipSpecificPads.Clear();
            _hitPadHistory.Clear();
            _btDeathFrame = 0;
            _shipCorridorBias = 0;
            _shipCoinAggressiveThreshold = PreferCoins ? 8 : 3;
            _shipForceHoldFrames = 0;
            _shipForceReleaseFrames = 0;
            _shipCommitFrames = 0;
            _shipCommitHold = false;
            _cubeGroundedWalkFrames = 0;
            _ballGroundedWalkFrames = 0;
            _bestPathHighWaterX = startX_px;
            _bestPathPoints.Clear();
            _bestInputs.Clear();
            _nextCoinCheckIdx = 0;
            _forgivenCoins.Clear();
            _forgivenCoinGameModes.Clear();
            _permanentlyCollectedCoins.Clear();
            _missedCoinIdx = -1;
            _coinInputScript.Clear();
            _coinInputScriptCoinIdx = -1;
            _beamSearchAttemptedCoinIdx = -1;
            _cubeBeamSearchAttemptedCoinIdx = -1;
            _crossCorrForgivenCoins.Clear();
            PreForgiveCrossCorridorCoins(startGameMode);

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

            // Coin fallback state — persists across death iterations so the coin
            // can be forgiven even when a retry path causes a different death.
            SimState coinFallbackState = default;
            int coinFallbackFrame = 0;
            int coinFallbackInputCount = 0;
            int coinFallbackPathCount = 0;
            int coinFallbackNextCheck = 0;
            List<BacktrackCheckpoint>? coinFallbackCheckpoints = null;

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
                // Also detect "predicted landing" — cube is airborne but will
                // land during this frame's step.  The BFS branches at these
                // frames, so we need checkpoints here for the backtracker to
                // retry different strategies (e.g., walk vs jump at landing).
                bool willLand = !wasGrounded && state.GameMode == 0
                    && CubeWillLandThisFrame(state);
                bool preHoldJump = _cubeHoldJump;
                int preHoldDelay = _cubeHoldDelay;
                int preCommittedDelay = _committedJumpDelay;
                int prePathCount = PathPoints.Count;
                int preInputCount = Inputs.Count;
                bool isShipMode = (state.GameMode == 1);
                SimState preDecisionState = (wasGrounded || isShipMode || willLand) ? state.Clone() : default;
                bool isBacktrackFrame = (_btOverrideFrame == frame);

                bool input = DecideInput(state);
                Inputs.Add(input);

                // Record whether THIS frame started grounded, for next frame's
                // hold-jump landing-frame gate.
                _prevFrameWasGrounded = wasGrounded;

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
                    _shipCoinAggressiveThreshold = 3;
                    _shipForceHoldFrames = 0;
                    _shipForceReleaseFrames = 0;
                    _shipCommitFrames = 0;
                    _btSkipAllOrbs = false; // clear orb-skip mode once past the obstacle
                    _btSkipSpecificOrbs.Clear(); // clear targeted orb skips
                    _hitOrbHistory.Clear(); // reset orb history for next segment
                    _btSkipSpecificPads.Clear(); // clear targeted pad skips
                    _hitPadHistory.Clear(); // reset pad history for next segment
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
                // Also checkpoint on LANDING frames (willLand) regardless of
                // jump decision — the backtracker needs these to try different
                // jump timings from landing points (e.g. delay=1,2,3 from the
                // exact frame the cube touches ground).
                // Don't create new checkpoints during backtrack replays — the
                // original checkpoints are the only ones we want to revisit.
                bool shouldCheckpoint = (wasGrounded || willLand) && (input || _committedJumpDelay > 0);
                if (!shouldCheckpoint && willLand)
                    shouldCheckpoint = true;

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
                if (shouldCheckpoint && _backtrackCheckpoints!.Count > 0)
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
                    if (_backtrackCheckpoints!.Count >= MAX_CHECKPOINT_DEPTH)
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
                        ForceJumpRemaining = _btForceJumpFramesRemaining,
                        SkipAllOrbs = _btSkipAllOrbs,
                        SkipSpecificOrbs = new HashSet<int>(_btSkipSpecificOrbs),
                        SkipSpecificPads = new HashSet<int>(_btSkipSpecificPads),
                        PrevFrameWasGrounded = _prevFrameWasGrounded
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

                // ── Coin miss detection ──
                // When PreferCoins is on, check if the player has passed any
                // uncollected coins.  Trigger a backtrackable death so the
                // pathfinder retries from an earlier checkpoint.
                if (alive && PreferCoins && _speculativeDepth == 0 && _nextCoinCheckIdx < allCoins.Count)
                {
                    int playerX = state.X_fixed >> 8;
                    for (int ci = _nextCoinCheckIdx; ci < allCoins.Count; ci++)
                    {
                        var coin = allCoins[ci];
                        if (coin.HitLeft > playerX + 16) break; // far ahead, stop checking
                        if (state.ProcessedSprites.Contains(coin.Index) || _forgivenCoins.Contains(coin.Index))
                        {
                            if (ci == _nextCoinCheckIdx) _nextCoinCheckIdx++;
                            continue;
                        }
                        if (playerX >= coin.HitRight)
                        {
                            // Missed this coin — trigger backtrackable death
                            alive = false;
                            _lastDeathReason = "MISSED_COIN";
                            _lastDeathX = coin.HitLeft;
                            _lastDeathY = coin.HitTop;
                            _missedCoinIdx = ci;
#if !DISABLE_DEBUG_LOGGING
                            PfLog($"[MISSED_COIN] idx={coin.Index} sid=0x{coin.SpriteId:X2} hitbox=({coin.HitLeft},{coin.HitTop})-({coin.HitRight},{coin.HitBottom}) playerX={playerX}");
#endif
                            break;
                        }
                        break; // next coin is still ahead — stop checking
                    }
                }

                // ── Coin collection detection ──
                // Check if the player's hitbox overlaps any uncollected coin.
                // Uses the same overlap logic as ProcessSprites for consistency
                // (exclusive bounds, NES +1 X offset, 2-arg hitbox offset).
                if (alive && _nextCoinCheckIdx < allCoins.Count)
                {
                    int nesX = (state.X_fixed >> 8) + 1; // NES sprite_collide() offset
                    int hbW = GetHitboxW(state.Mini);
                    int hbH = GetHitboxH(state.Mini);
                    // NES sprite_collide uses (0x10-h)>>1 for the Y offset unconditionally
                    // (centers the hitbox in the 16px cell), regardless of gravity state.
                    int hbOffY = state.Mini ? 4 : 0; // (0x10 - 7) >> 1 = 4 for mini, 0 for normal
                    int playerTop = (state.Y_fixed >> 8) + hbOffY;
                    int playerBottom = playerTop + hbH;     // exclusive
                    int playerRight = nesX + hbW;           // exclusive

                    for (int ci = _nextCoinCheckIdx; ci < allCoins.Count; ci++)
                    {
                        var coin = allCoins[ci];
                        if (coin.HitLeft > playerRight + 16) break;
                        if (state.ProcessedSprites.Contains(coin.Index) || _forgivenCoins.Contains(coin.Index)) continue;

                        // Overlap check identical to ProcessSprites (exclusive bounds)
                        bool xOverlap = !(playerRight < coin.HitLeft || coin.HitRight < nesX);
                        bool yOverlap = !(playerBottom < coin.HitTop || coin.HitBottom < playerTop);
                        if (xOverlap && yOverlap)
                        {
                            state.ProcessedSprites.Add(coin.Index);
                            if (_speculativeDepth == 0)
                                _permanentlyCollectedCoins.Add(coin.Index);
#if !DISABLE_DEBUG_LOGGING
                            PfLog($"[COIN_COLLECTED] idx={coin.Index} sid=0x{coin.SpriteId:X2} hitbox=({coin.HitLeft},{coin.HitTop})-({coin.HitRight},{coin.HitBottom}) player=({nesX},{playerTop})-({playerRight},{playerBottom})");
#endif
                        }
                    }
                }

                if (!alive)
                {
#if !DISABLE_DEBUG_LOGGING
                    int deathPct = levelLengthPx > 0 ? (state.X_fixed >> 8) * 100 / levelLengthPx : 0;
                    PfLog($"[DEATH] frame={frame} X={state.X_fixed >> 8}px Y={state.Y_fixed >> 8}px pct={deathPct}% reason={_lastDeathReason} dX={_lastDeathX} dY={_lastDeathY} VelY=0x{state.VelY_fixed:X} gravFlipped={state.GravFlipped}");
#endif
                    // Snapshot the current full path if it reached further than any previous attempt
                    SnapshotBestPath();

                    // For MISSED_COIN deaths, save fallback state before backtracking.
                    // If all backtracks fail, we can forgive the coin and continue.
                    // CRITICAL: also save the backtrack checkpoint list — the backtracking
                    // process consumes checkpoints trying to reach the coin, and if we
                    // forgive the coin we need those checkpoints back for the rest of
                    // the level.
                    bool isCoinMiss = (_lastDeathReason == "MISSED_COIN" && _missedCoinIdx >= 0);
                    if (isCoinMiss)
                    {
                        coinFallbackState = state.Clone();
                        coinFallbackFrame = frame;
                        coinFallbackInputCount = Inputs.Count;
                        coinFallbackPathCount = PathPoints.Count;
                        coinFallbackNextCheck = _nextCoinCheckIdx;
                        // Deep-copy checkpoints (Clone() the SimState inside each)
                        coinFallbackCheckpoints = _backtrackCheckpoints!.Select(cp => new BacktrackCheckpoint
                        {
                            Frame = cp.Frame,
                            State = cp.State.Clone(),
                            HoldJumpState = cp.HoldJumpState,
                            HoldDelayState = cp.HoldDelayState,
                            CommittedDelayState = cp.CommittedDelayState,
                            PathPointCount = cp.PathPointCount,
                            InputCount = cp.InputCount,
                            RetryStage = cp.RetryStage,
                            UsedBias = cp.UsedBias,
                            ShipBias = cp.ShipBias,
                            GameMode = cp.GameMode,
                            ShipForceHold = cp.ShipForceHold,
                            ShipForceRelease = cp.ShipForceRelease,
                            ShipCommitFrames = cp.ShipCommitFrames,
                            ShipCommitHold = cp.ShipCommitHold,
                            ForceJumpRemaining = cp.ForceJumpRemaining,
                            SkipAllOrbs = cp.SkipAllOrbs,
                            SkipSpecificOrbs = cp.SkipSpecificOrbs != null ? new HashSet<int>(cp.SkipSpecificOrbs) : new HashSet<int>(),
                            SkipSpecificPads = cp.SkipSpecificPads != null ? new HashSet<int>(cp.SkipSpecificPads) : new HashSet<int>(),
                            PrevFrameWasGrounded = cp.PrevFrameWasGrounded,
                        }).ToList();
                    }

                    // Try backtracking to a previous decision point
                    if (TryBacktrack(ref state, ref frame))
                        continue;

                    // All backtracks exhausted.
                    // If the death was a missed coin, forgive it and continue
                    // from where we were — the coin is simply unreachable.
                    // EXCEPT: during retry, mandatory coins cannot be forgiven —
                    // crossing past them is a permanent death.
                    if (isCoinMiss)
                    {
                        var missedCoin = allCoins[_missedCoinIdx];

                        // Mandatory coins during retry: do NOT forgive — treat as permanent death
                        if (_retryMandatoryCoins.Contains(missedCoin.Index))
                        {
                            Console.Error.WriteLine($"[MANDATORY_COIN_DEATH] idx={missedCoin.Index} sid=0x{missedCoin.SpriteId:X2} — mandatory coin missed, permanent death");
                            // Use the best path we found
                            UseBestPathIfBetter();
                            Success = false;
                            int bestX2 = PathPoints.Count > 0 ? PathPoints[PathPoints.Count - 1].x : 0;
                            int bestPct2 = levelLengthPx > 0 ? bestX2 * 100 / levelLengthPx : 0;
                            ResultMessage = $"Permanent death at frame {frame} (reason: mandatory coin {missedCoin.Index} missed) — best X {bestX2}px ({bestPct2}%)";
                            Console.Error.WriteLine($"[PERM_DEATH] frame={frame} X={state.X_fixed >> 8} Y={state.Y_fixed >> 8} reason=MANDATORY_COIN_{missedCoin.Index}");
                            TraceFrameClose();
                            return;
                        }

                        _forgivenCoins.Add(missedCoin.Index);
                        _forgivenCoinGameModes[missedCoin.Index] = coinFallbackState.GameMode;
                        // Clear coin input script — the coin was forgiven, so the
                        // script (which aimed to collect it) is no longer valid.
                        _coinInputScript.Clear();
                        _coinInputScriptCoinIdx = -1;
                        state = coinFallbackState;
                        frame = coinFallbackFrame;
                        _nextCoinCheckIdx = coinFallbackNextCheck;
                        // Restore outputs to pre-death state
                        if (Inputs.Count > coinFallbackInputCount)
                            Inputs.RemoveRange(coinFallbackInputCount, Inputs.Count - coinFallbackInputCount);
                        if (PathPoints.Count > coinFallbackPathCount)
                            PathPoints.RemoveRange(coinFallbackPathCount, PathPoints.Count - coinFallbackPathCount);
                        // Restore backtrack checkpoints — the backtracking consumed them
                        // trying to reach the coin, but they're needed for the rest of the level
                        _backtrackCheckpoints = coinFallbackCheckpoints!;
                        _backtrackActive = false;
                        _backtrackAttempts = 0;
                        _btOverrideFrame = -1;
                        _btOverrideStage = 0;
                        _btSuppressJumpUntilAirborne = false;
                        _btSkipAllOrbs = false;
                        _btSkipSpecificOrbs.Clear();
                        _btSkipSpecificPads.Clear();
                        _btForceJumpFramesRemaining = 0;
                        _shipCorridorBias = 0;
                        _shipCoinAggressiveThreshold = 3;
                        _shipForceHoldFrames = 0;
                        _shipForceReleaseFrames = 0;
                        _shipCommitFrames = 0;
                        _missedCoinIdx = -1;
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[COIN_FORGIVEN] idx={missedCoin.Index} sid=0x{missedCoin.SpriteId:X2} hitbox=({missedCoin.HitLeft},{missedCoin.HitTop})-({missedCoin.HitRight},{missedCoin.HitBottom}) forgiven={_forgivenCoins.Count}");
#endif
                        continue;
                    }

                    // If we were retrying for a missed coin and the retry path
                    // caused a DIFFERENT death (not the coin miss), forgive the
                    // coin and restore to the pre-coin-miss state instead of
                    // declaring permanent death.
                    if (_missedCoinIdx >= 0 && _missedCoinIdx < allCoins.Count && coinFallbackCheckpoints != null)
                    {
                        var missedCoin2 = allCoins[_missedCoinIdx];
                        _forgivenCoins.Add(missedCoin2.Index);
                        _forgivenCoinGameModes[missedCoin2.Index] = coinFallbackState.GameMode;
                        _coinInputScript.Clear();
                        _coinInputScriptCoinIdx = -1;
                        state = coinFallbackState;
                        frame = coinFallbackFrame;
                        _nextCoinCheckIdx = coinFallbackNextCheck;
                        if (Inputs.Count > coinFallbackInputCount)
                            Inputs.RemoveRange(coinFallbackInputCount, Inputs.Count - coinFallbackInputCount);
                        if (PathPoints.Count > coinFallbackPathCount)
                            PathPoints.RemoveRange(coinFallbackPathCount, PathPoints.Count - coinFallbackPathCount);
                        _backtrackCheckpoints = coinFallbackCheckpoints;
                        _backtrackActive = false;
                        _backtrackAttempts = 0;
                        _btOverrideFrame = -1;
                        _btOverrideStage = 0;
                        _btSuppressJumpUntilAirborne = false;
                        _btSkipAllOrbs = false;
                        _btSkipSpecificOrbs.Clear();
                        _btSkipSpecificPads.Clear();
                        _btForceJumpFramesRemaining = 0;
                        _shipCorridorBias = 0;
                        _shipCoinAggressiveThreshold = 3;
                        _shipForceHoldFrames = 0;
                        _shipForceReleaseFrames = 0;
                        _shipCommitFrames = 0;
                        _missedCoinIdx = -1;
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[COIN_FORGIVEN_ALT] idx={missedCoin2.Index} sid=0x{missedCoin2.SpriteId:X2} — coin retry caused death elsewhere, forgiving");
#endif
                        continue;
                    }

                    // Use the best path we found
                    UseBestPathIfBetter();
                    Success = false;
                    int bestX = PathPoints.Count > 0 ? PathPoints[PathPoints.Count - 1].x : 0;
                    int bestPct = levelLengthPx > 0 ? bestX * 100 / levelLengthPx : 0;
                    string deathMsg = $"Died — partial path to X={bestX}px ({bestPct}%) reason={_lastDeathReason} dX={_lastDeathX} dY={_lastDeathY}";
                    if (PreferCoins && allCoins.Count > 0)
                    {
                        int collected = state.ProcessedSprites.Count(idx => allCoins.Any(c => c.Index == idx));
                        deathMsg += $" [{collected}/{allCoins.Count} coins]";
                        if (_forgivenCoins.Count > 0) deathMsg += $" ({_forgivenCoins.Count} unreachable)";
                    }
                    ResultMessage = deathMsg;
                    TraceFrameClose();
                    return;
                }
                if (endLevel)
                {
                    // Path point already recorded above after StepFrame
                    Success = true;
                    string successMsg = $"Completed in {frame} frames ({PathPoints.Count} path points)";
                    if (PreferCoins && allCoins.Count > 0)
                    {
                        // Gather collected coin indices for downstream use
                        var collectedCoinIndices = new HashSet<int>();
                        foreach (var coin in allCoins)
                        {
                            if (state.ProcessedSprites.Contains(coin.Index))
                                collectedCoinIndices.Add(coin.Index);
                        }
                        FinalCollectedCoinIndices = collectedCoinIndices;
                        successMsg += $" [{collectedCoinIndices.Count}/{allCoins.Count} coins]";
                        if (_forgivenCoins.Count > 0) successMsg += $" ({_forgivenCoins.Count} unreachable)";
                    }
                    ResultMessage = successMsg;
                    ExtractSkippedPads(state);
                    TraceFrameClose();
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[END_LEVEL] frame={frame} skippedPads={SkippedPadIndices?.Count ?? 0}");
#endif
                    return;
                }
            }

            SnapshotBestPath();
            UseBestPathIfBetter();
            ExtractSkippedPads(state);
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
        // Saved at the START of each backtrack cycle to prevent nested deaths
        // from poisoning the maxStages calculation.
        private string _btOrigDeathReason = "";
        private int _btOrigDeathY = 0;

        private bool TryBacktrack(ref SimState state, ref int frame)
        {
            if (!_backtrackActive)
            {
                // First backtrack from this death — record the death frame
                // and original death info for stable maxStages evaluation.
                _btDeathFrame = frame;
                _btOrigDeathReason = _lastDeathReason;
                _btOrigDeathY = _lastDeathY;
                _backtrackTimer = System.Diagnostics.Stopwatch.StartNew();
            }
            _backtrackActive = true;
            int deathGameMode = state.GameMode; // capture death mode before any restoration

            while (_backtrackCheckpoints.Count > 0 &&
                   _backtrackAttempts < MAX_BACKTRACK_ATTEMPTS &&
                   _totalBacktrackAttempts < MAX_TOTAL_BACKTRACK_ATTEMPTS &&
                   (_backtrackTimer == null || _backtrackTimer.Elapsed.TotalSeconds < (_missedCoinIdx >= 0 ? MAX_BACKTRACK_SECONDS_COIN : MAX_BACKTRACK_SECONDS)))
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
                    maxStages = (_missedCoinIdx >= 0) ? 8 : 4;
                else if (cp.GameMode == 2)
                    maxStages = 10;
                else
                {
                    int frameDist2 = _btDeathFrame - cp.Frame;
                    // ELEVATION-AWARE FAST SKIP: When death was FWD_DEATH and
                    // checkpoint is at the same floor level as death, trying
                    // different inputs won't help — the player needs to be
                    // HIGHER to clear the wall.  Skip to earlier checkpoints
                    // where route changes can gain elevation.
                    int cpY = cp.State.Y_fixed >> 8;
                    if (_btOrigDeathReason == "FWD_DEATH"
                        && Math.Abs(cpY - _btOrigDeathY) < ELEV_THRESHOLD
                        && frameDist2 > 30)
                    {
                        maxStages = 1; // just try suppress, then move on
                    }
                    // Adaptive staging: nearby checkpoints get full exploration
                    // (fine delays, orb-skip, etc). Distant checkpoints get only
                    // aggressive strategies (suppress/force/bias) so the backtracker
                    // can quickly reach earlier forks and route divergences.
                    // COIN RETRY: use more stages at all distances so the force-jump
                    // window stages (11-12) are tried even for distant checkpoints.
                    // ELEVATED COIN FAST SKIP: when the missed coin is significantly
                    // ABOVE the player (>40px), nearby checkpoints at similar altitude
                    // won't help — we need to reach distant checkpoints where the
                    // player can take a different (higher) route. Cap nearby stages.
                    else if (_missedCoinIdx >= 0)
                    {
                        var mc = allCoins[_missedCoinIdx];
                        int coinCenterY = (mc.HitTop + mc.HitBottom) / 2;
                        bool coinAbove = (cpY - coinCenterY) > 40; // coin is above player by 40+px
                        if (coinAbove && frameDist2 <= 90)
                            maxStages = 4; // fast-skip: only try core strategies near floor
                        else if (frameDist2 <= 60)
                            maxStages = 19; // full exploration for nearby checkpoints
                        else
                            maxStages = 12; // distant: include force-jump window
                    }
                    else if (frameDist2 <= 60)
                        maxStages = 19;  // within ~1 second: full exploration
                    else if (frameDist2 <= 240)
                        maxStages = 8;   // within ~4 seconds: core strategies
                    else
                        maxStages = 4;   // distant: suppress, force, bias, bias+force
                }
                if (cp.RetryStage > maxStages)
                {
                    _backtrackCheckpoints.RemoveAt(last);
                    continue; // try the previous checkpoint
                }


                // Restore state to before the decision at this checkpoint
                state = cp.State.Clone();
                // Re-add permanently collected coins — backtracking should not
                // undo coin collection.  Coins are non-physical collectibles so
                // having them in ProcessedSprites merely prevents re-collection.
                if (PreferCoins && _permanentlyCollectedCoins.Count > 0)
                {
                    foreach (int coinIdx in _permanentlyCollectedCoins)
                        state.ProcessedSprites.Add(coinIdx);
                }
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
                _btSkipAllOrbs = cp.SkipAllOrbs;
                _btSkipSpecificOrbs = new HashSet<int>(cp.SkipSpecificOrbs ?? new());
                _btSkipSpecificPads = new HashSet<int>(cp.SkipSpecificPads ?? new());
                _prevFrameWasGrounded = cp.PrevFrameWasGrounded;
                _coinInputScript.Clear();
                _coinInputScriptCoinIdx = -1;

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
                    // Ship backtrack: 8 stages per checkpoint when retrying for
                    // a missed coin, 4 otherwise.  Coin retries apply much larger
                    // corridor biases to navigate through alternate corridors.
                    int dist = _btOverrideDistFromDeath;
                    int forceDuration = Math.Max(4, dist / 3);
                    int biasAmount = Math.Max(24, Math.Min(72, dist));
                    _shipForceHoldFrames = 0;
                    _shipForceReleaseFrames = 0;
                    
                    if (_missedCoinIdx >= 0 && _missedCoinIdx < allCoins.Count)
                    {
                        // Coin-aware stages: use large biases toward the coin Y.
                        // Also increase aggressive threshold so PD overrides tree
                        // search more often, allowing the ship to navigate to
                        // alternate corridors where coins reside.
                        _shipCoinAggressiveThreshold = 20; // SHIP_TREE_DEPTH
                        var mc = allCoins[_missedCoinIdx];
                        int coinCenterY = (mc.HitTop + mc.HitBottom) / 2;
                        int corr = FindCorridorCenter(ref state);
                        int coinBias = coinCenterY - corr; // bias to steer toward coin
                        int coinDir = (coinBias < 0) ? -1 : 1; // direction to coin
                        // Mix corridor bias and force-hold/release stages.
                        // Force inputs physically commit the ship to a different
                        // altitude, bypassing the corridor finder's geometry clamping.
                        // coinDir > 0 means coin is below (need to release/fall).
                        // coinDir < 0 means coin is above (need to hold/thrust).
                        int forceDur = Math.Max(6, dist / 2);
                        switch (stage)
                        {
                            case 1: _shipCorridorBias = coinDir * 60; break;
                            case 2:
                                // Force toward coin: release to fall or hold to rise
                                if (coinDir > 0) _shipForceReleaseFrames = forceDur;
                                else _shipForceHoldFrames = forceDur;
                                _shipCorridorBias = coinDir * 120;
                                break;
                            case 3: _shipCorridorBias = coinDir * 160; break;
                            case 4:
                                // Stronger force toward coin
                                if (coinDir > 0) _shipForceReleaseFrames = forceDur * 2;
                                else _shipForceHoldFrames = forceDur * 2;
                                _shipCorridorBias = coinDir * 200;
                                break;
                            case 5: _shipCorridorBias = -coinDir * 60; break; // opposite
                            case 6:
                                // Force opposite direction (some coins require going
                                // around an obstacle)
                                if (coinDir > 0) _shipForceHoldFrames = forceDur;
                                else _shipForceReleaseFrames = forceDur;
                                _shipCorridorBias = -coinDir * 120;
                                break;
                            case 7:
                                if (coinDir > 0) _shipForceReleaseFrames = forceDur * 3;
                                else _shipForceHoldFrames = forceDur * 3;
                                _shipCorridorBias = coinDir * 250;
                                break;
                            case 8: _shipCorridorBias = coinDir * 300; break;
                        }
                    }
                    else
                    {
                        // Normal ship backtrack (no coin)
                        switch (stage)
                        {
                            case 1: _shipCorridorBias = -biasAmount; break;
                            case 2: _shipCorridorBias = biasAmount; break;
                            case 3: _shipForceHoldFrames = forceDuration; break;
                            case 4: _shipForceReleaseFrames = forceDuration; break;
                        }
                    }
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[BACKTRACK_OVERRIDE_SHIP] stage={stage} dist={dist}: bias={_shipCorridorBias} forceHold={_shipForceHoldFrames} forceRel={_shipForceReleaseFrames} coinRetry={_missedCoinIdx >= 0}");
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

                // Cube mode overrides (19 stages)
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
                //  13: skip all orbs
                //  14: skip all orbs + opposite bias
                //  15: skip last orb hit before death
                //  16: skip last 2 orbs
                //  17: force jump after delay=1 (fine-grained)
                //  18: force jump after delay=2 (fine-grained)
                //  19: force jump after delay=3 (fine-grained)
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
                else if (stage == 13) // Skip all orbs: try the segment without hitting any orbs
                {
                    _btSkipAllOrbs = true;
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[BACKTRACK_OVERRIDE] stage=13: skip all orbs mode enabled");
#endif
                    // Fall through to normal decision (orb decision code will see the flag)
                }
                else if (stage == 14) // Skip all orbs + opposite bias
                {
                    _btSkipAllOrbs = true;
                    JumpTimingBias = 1.0 - JumpTimingBias;
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[BACKTRACK_OVERRIDE] stage=14: skip all orbs + flipped bias to {JumpTimingBias:F2}");
#endif
                    // Fall through to normal decision
                }
                else if (stage == 15) // Skip last orb hit before death
                {
                    if (_hitOrbHistory.Count > 0)
                    {
                        int lastOrb = _hitOrbHistory[_hitOrbHistory.Count - 1];
                        _btSkipSpecificOrbs.Add(lastOrb);
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[BACKTRACK_OVERRIDE] stage=15: skip last orb idx={lastOrb} (history={_hitOrbHistory.Count})");
#endif
                    }
                    // Fall through to normal decision
                }
                else if (stage == 16) // Skip last 2 orbs hit before death
                {
                    for (int oi = Math.Max(0, _hitOrbHistory.Count - 2); oi < _hitOrbHistory.Count; oi++)
                        _btSkipSpecificOrbs.Add(_hitOrbHistory[oi]);
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[BACKTRACK_OVERRIDE] stage=16: skip last 2 orbs (specific={_btSkipSpecificOrbs.Count})");
#endif
                    // Fall through to normal decision
                }
                else if (stage == 17) // Fine-grained delay=1
                {
                    _committedJumpDelay = 1;
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[BACKTRACK_OVERRIDE] stage=17: committed delay=1");
#endif
                    return false;
                }
                else if (stage == 18) // Fine-grained delay=2
                {
                    _committedJumpDelay = 2;
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[BACKTRACK_OVERRIDE] stage=18: committed delay=2");
#endif
                    return false;
                }
                else if (stage == 19) // Fine-grained delay=3
                {
                    _committedJumpDelay = 3;
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[BACKTRACK_OVERRIDE] stage=19: committed delay=3");
#endif
                    return false;
                }
                // Stages 20+ (pad-skip) removed: pads fire on collision,
                // cannot be suppressed by the pathfinder.
            }

            // ═══════════════════════════════════════════════════════════════
            //  Coin input script playback (ALL game modes)
            //  When a coin collection script was recorded (for ship threading,
            //  cube Strategy 0, etc.), play it back verbatim.
            // ═══════════════════════════════════════════════════════════════
            if (_coinInputScript.Count > 0 && _speculativeDepth == 0)
            {
                bool scriptInput = _coinInputScript.Dequeue();
#if !DISABLE_DEBUG_LOGGING
                PfLog($"[COIN_SCRIPT] frame={_frameCounter} remaining={_coinInputScript.Count} input={scriptInput} gameMode={state.GameMode}");
#endif
                return scriptInput;
            }

            if (state.GameMode == 1) return DecideShipInput(state);
            if (state.GameMode == 2) return DecideBallInput(state, isOverrideFrame);
            if (state.GameMode == 3) return DecideUfoInput(state, isOverrideFrame);
            if (state.GameMode != 0) return false; // only cube/ship/ball/ufo for now

            // ═══════════════════════════════════════════════════════════════
            //  Coin input script playback (cube/legacy — handled above for all modes)
            // ═══════════════════════════════════════════════════════════════

            // Pads fire on collision — no skip/decide logic needed.
            // The PF cannot suppress pad activation; if the player overlaps
            // a pad, it MUST activate (matching NES/sim behavior).

            // ── Cube orb decision: evaluate "hit" vs "skip" for overlapping orbs ──
            // Compare by X-progress (distance traveled) to avoid gravity-portal-bonus
            // distortion.  If hitting gets further, return true to activate.
            // If skipping gets further, pre-add to ProcessedSprites and return false.
            if (!isOverrideFrame && !_btSuppressJumpUntilAirborne)
            {
                int cubeOrbSid = ScanForOrbOverlap(state, out int cubeOrbIndex);
                if (cubeOrbSid >= 0)
                {
                    // ── Coin-retry orb deferral: when altitude penalties are active
                    // and the player is rapidly rising (from a pad bounce), defer
                    // orb activation until near the peak of the arc.  This ensures
                    // the orb launches from maximum altitude, giving the best
                    // trajectory to clear post-coin obstacles (walls, etc.).
                    // Without deferral the orb fires too early (while still rising
                    // fast), producing a lower peak that can't clear the wall.
                    if (_coinAltitudePenalties.Count > 0
                        && !state.GravFlipped
                        && state.VelY_fixed < -100   // still rising rapidly
                        && _speculativeDepth == 0)   // only real (non-speculative) decisions
                    {
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[ORB_DEFER_PEAK] deferring orb 0x{cubeOrbSid:X2} idx={cubeOrbIndex} velY=0x{state.VelY_fixed:X} Y={state.Y_fixed >> 8} — waiting for peak");
#endif
                        return false; // defer: don't activate, don't skip permanently
                    }

                    // Backtrack "skip all orbs" mode: unconditionally skip
                    if (_btSkipAllOrbs)
                    {
                        state.ProcessedSprites.Add(cubeOrbIndex);
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[ORB_SKIP_CUBE_BT] Force-skipping orb 0x{cubeOrbSid:X2} (btSkipAllOrbs mode)");
#endif
                        return false; // don't press input — skip the orb
                    }

                    // Backtrack "skip specific orbs" mode: skip only targeted orbs
                    if (_btSkipSpecificOrbs.Contains(cubeOrbIndex))
                    {
                        state.ProcessedSprites.Add(cubeOrbIndex);
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[ORB_SKIP_CUBE_SPECIFIC] Force-skipping orb 0x{cubeOrbSid:X2} idx={cubeOrbIndex} (targeted skip)");
#endif
                        return false;
                    }

                    if (_speculativeDepth < MAX_SPECULATIVE_DEPTH)
                    {
                        _speculativeDepth++;

                        // Evaluate "hit orb" path — jump at frame 0 with singleJumpOnly
                        // so only the orb activation occurs (no chain jumps/orbs).
                        int orbHitBestSurv = SimulateForwardWithJumpAt(state, 0, out int orbHitBestX,
                            singleJumpOnly: true);

                        // Evaluate "skip orb" path — never jump, orb pre-skipped.
                        var skipState = state.Clone();
                        skipState.ProcessedSprites.Add(cubeOrbIndex);
                        skipState.PendingOrbIndex = -1;
                        skipState.PendingOrbSpriteId = -1;
                        int orbSkipBestSurv = SimulateForwardWithJumpAt(skipState, -1, out int orbSkipBestX,
                            singleJumpOnly: true);

                        // ── Extended horizon tiebreaker ──
                        if (orbHitBestSurv >= LOOKAHEAD_HORIZON &&
                            orbSkipBestSurv >= LOOKAHEAD_HORIZON &&
                            orbHitBestX == orbSkipBestX)
                        {
                            int extHorizon = LOOKAHEAD_HORIZON * 3;
                            int extHitSurv = SimulateForwardWithJumpAt(state, 0, out int extHitX,
                                singleJumpOnly: true, horizonOverride: extHorizon);
                            int extSkipSurv = SimulateForwardWithJumpAt(skipState, -1, out int extSkipX,
                                singleJumpOnly: true, horizonOverride: extHorizon);
#if !DISABLE_DEBUG_LOGGING
                            PfLog($"[ORB_DECIDE_CUBE_EXT] extended horizon={extHorizon}: hitX={extHitX} skipX={extSkipX} hitSurv={extHitSurv} skipSurv={extSkipSurv}");
#endif
                            orbHitBestX = extHitX;
                            orbSkipBestX = extSkipX;
                            orbHitBestSurv = extHitSurv;
                            orbSkipBestSurv = extSkipSurv;
                        }

                        _speculativeDepth--;

#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[ORB_DECIDE_CUBE] sid=0x{cubeOrbSid:X2} idx={cubeOrbIndex} hitX={orbHitBestX} skipX={orbSkipBestX} hitSurv={orbHitBestSurv} skipSurv={orbSkipBestSurv} pending={state.PendingOrbIndex}");
#endif
                        // Use X-progress as primary metric; survival as tiebreaker
                        bool orbSkipBetter = (orbSkipBestX > orbHitBestX) ||
                            (orbSkipBestX == orbHitBestX && orbSkipBestSurv > orbHitBestSurv);
                        if (orbSkipBetter)
                        {
                            state.ProcessedSprites.Add(cubeOrbIndex);
#if !DISABLE_DEBUG_LOGGING
                            PfLog($"[ORB_SKIP_CUBE] Skipping orb 0x{cubeOrbSid:X2} — skipX={orbSkipBestX} vs hitX={orbHitBestX}");
#endif
                            return false;
                        }
                        else
                        {
#if !DISABLE_DEBUG_LOGGING
                            PfLog($"[ORB_HIT_CUBE] Hitting orb 0x{cubeOrbSid:X2} — hitX={orbHitBestX} vs skipX={orbSkipBestX}");
#endif
                            _hitOrbHistory.Add(cubeOrbIndex);
                            return true;
                        }
                    }
                    else
                    {
                        // At max speculative depth, fall through to BFS
                        // (orb will be handled by BFS orb-skip variant)
                    }
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
            //  Force-walk zone override (coin retry only)
            //  When retrying for coins, force WALK when grounded near a pad
            //  that leads to a coin. Prevents the BFS from jumping over
            //  yellow pads (coin launch) or blue pads (gravity flip).
            //  ALSO prevents jump buffering when airborne and falling near
            //  the pad — ensures the player descends to the pad's Y level
            //  instead of bouncing off intermediate platforms.
            //  Highest priority — overrides coin-jump and BFS decisions.
            // ═══════════════════════════════════════════════════════════════
            if (_coinForceWalkZones.Count > 0)
            {
                int playerX_fw = state.X_fixed >> 8;
                foreach (var (fwStart, fwEnd) in _coinForceWalkZones)
                {
                    if (playerX_fw >= fwStart && playerX_fw <= fwEnd)
                    {
                        // On ground: force walk to activate pad
                        if (state.OnGround && state.VelY_fixed == 0)
                        {
#if !DISABLE_DEBUG_LOGGING
                            PfLog($"[FORCE_WALK] Player at X={playerX_fw} in force-walk zone ({fwStart},{fwEnd}) — forcing WALK");
#endif
                            return false;
                        }
                        // Airborne and falling: suppress jump buffer to let
                        // the player fall to the pad's level
                        bool isFalling = !state.OnGround &&
                            ((state.GravMul > 0 && state.VelY_fixed > 0) ||
                             (state.GravMul < 0 && state.VelY_fixed < 0));
                        if (isFalling)
                        {
#if !DISABLE_DEBUG_LOGGING
                            PfLog($"[FORCE_WALK] Player at X={playerX_fw} Y={state.Y_fixed >> 8} FALLING in force-walk zone — suppressing jump buffer");
#endif
                            return false;
                        }
                    }
                }
            }

            // ═══════════════════════════════════════════════════════════════
            //  Ground-level bias zone (coin retry only)
            //  When retrying for coins, keep the player at ground level by
            //  preferring WALK. Only allows jumping when walking would die
            //  within 3 frames (spike ahead). This forces the BFS to take
            //  ground-level routes instead of climbing staircases/platforms.
            //  Still allows jumping over spikes/obstacles at ground level.
            // ═══════════════════════════════════════════════════════════════
            if (_coinGroundBiasZones.Count > 0 && state.OnGround && state.VelY_fixed == 0
                && state.GameMode == 0 && _speculativeDepth == 0)
            {
                int playerX_gb = state.X_fixed >> 8;
                foreach (var (gbStart, gbEnd) in _coinGroundBiasZones)
                {
                    if (playerX_gb >= gbStart && playerX_gb <= gbEnd)
                    {
                        // Check if walking would die within 3 frames
                        bool walkDiesSoon = false;
                        _speculativeDepth++;
                        var testWalk = state.Clone();
                        for (int t = 0; t < 3; t++)
                        {
                            bool testAlive = StepFrame(ref testWalk, false, out _);
                            if (!testAlive) { walkDiesSoon = true; break; }
                            if (!testWalk.OnGround) break; // walked off edge → stop
                        }
                        _speculativeDepth--;

                        if (!walkDiesSoon)
                        {
#if !DISABLE_DEBUG_LOGGING
                            PfLog($"[GROUND_BIAS] X={playerX_gb} in bias zone → WALK (safe)");
#endif
                            return false; // WALK — stay at ground level
                        }
                        // Walk would die → let BFS/coin-override decide (may jump)
                        break;
                    }
                }
            }

            // ═══════════════════════════════════════════════════════════════
            //  CUBE BEAM SEARCH FOR ELEVATED COINS
            //  When a cube-mode coin is significantly above the player's
            //  normal jump reach (~34px), the standard coin-jump strategies
            //  (walk, always-jump, single-jump) fail. This beam search
            //  explores all jump/walk combinations over a long horizon to
            //  find a path through elevated terrain that collects the coin.
            //  Similar in concept to the ship beam search but adapted for
            //  binary grounded jump/walk decisions.
            // ═══════════════════════════════════════════════════════════════
            if (PreferCoins && allCoins.Count > 0 && state.VelY_fixed == 0
                && state.GameMode == 0 && _speculativeDepth == 0
                && _coinInputScript.Count == 0)
            {
                int playerX_cb = state.X_fixed >> 8;
                int playerY_cb = state.Y_fixed >> 8;
                for (int ci = _nextCoinCheckIdx; ci < allCoins.Count; ci++)
                {
                    var coin = allCoins[ci];
                    if (state.ProcessedSprites.Contains(coin.Index) || _forgivenCoins.Contains(coin.Index))
                        continue;
                    int coinCenterY = (coin.HitTop + coin.HitBottom) / 2;
                    int coinDistX = coin.HitLeft - playerX_cb;
                    if (coinDistX < 0) continue;    // already past
                    if (coinDistX > 800) break;      // too far for any coin

                    // Only beam-search for elevated coins (>30px above player center)
                    if ((playerY_cb - coinCenterY) < 30) continue;

                    // Trigger at maximum range for best exploration; skip if too close
                    if (coinDistX < 100) continue;

                    // Only attempt once per coin per run
                    if (_cubeBeamSearchAttemptedCoinIdx == coin.Index) continue;
                    _cubeBeamSearchAttemptedCoinIdx = coin.Index;

                    const int CUBE_BEAM_WIDTH = 512;
                    int beamHorizon = coinDistX / 3 + 60;
                    _speculativeDepth++;

                    var cbeam = new List<(SimState st, List<bool> inputs)>();
                    cbeam.Add((state.Clone(), new List<bool>()));

                    bool cubeBeamFound = false;
                    var cubeBeamScript = new List<bool>();

                    for (int step = 0; step < beamHorizon && cbeam.Count > 0; step++)
                    {
                        var nextBeam = new List<(SimState st, List<bool> inputs, int coinDist)>();

                        foreach (var (bs, bInputs) in cbeam)
                        {
                            // Determine choices: pending orb → must activate;
                            // grounded or about-to-land → jump or walk; airborne → no choice
                            int numChoices;
                            bool forceOrb = (bs.PendingOrbIndex >= 0);
                            bool aboutToLand = (!bs.OnGround && bs.VelY_fixed != 0
                                && CubeWillLandThisFrame(bs));
                            if (forceOrb)
                                numChoices = 1;
                            else if ((bs.OnGround && bs.VelY_fixed == 0) || aboutToLand)
                                numChoices = 2;
                            else
                                numChoices = 1;

                            for (int choice = 0; choice < numChoices; choice++)
                            {
                                bool inp;
                                if (forceOrb)
                                    inp = true;
                                else if (numChoices == 1)
                                    inp = false; // airborne: fall
                                else
                                    inp = (choice == 1); // 0=walk, 1=jump

                                var sim = bs.Clone();
                                if (!StepFrame(ref sim, inp, out _)) continue; // died

                                var newInputs = new List<bool>(bInputs) { inp };

                                // Check coin overlap
                                int nx = (sim.X_fixed >> 8) + 1;
                                int hbW_s = GetHitboxW(sim.Mini);
                                int hbH_s = GetHitboxH(sim.Mini);
                                int hbOff_s = GetHitboxOffsetY(sim.Mini, sim.GravFlipped);
                                int pT = (sim.Y_fixed >> 8) + hbOff_s;
                                if (!(nx + hbW_s < coin.HitLeft || coin.HitRight < nx) &&
                                    !(pT + hbH_s < coin.HitTop || coin.HitBottom < pT))
                                {
                                    // Coin collected! Verify survival (30 frames of
                                    // "jump when grounded" to clear obstacles)
                                    bool survives = true;
                                    var postSim = sim.Clone();
                                    for (int pf = 0; pf < 30; pf++)
                                    {
                                        bool pInp = postSim.OnGround && postSim.VelY_fixed == 0;
                                        if (!StepFrame(ref postSim, pInp, out _))
                                        { survives = false; break; }
                                    }
                                    if (survives)
                                    {
                                        cubeBeamFound = true;
                                        cubeBeamScript = newInputs;
                                        break;
                                    }
                                }

                                // Eliminate states past the coin
                                int sx = sim.X_fixed >> 8;
                                if (sx > coin.HitRight + 16) continue;

                                int sy = sim.Y_fixed >> 8;
                                int dy = Math.Abs(sy - coinCenterY);
                                int dx = Math.Max(0, coin.HitLeft - sx);
                                // Use dx-weighted score with mild dy preference.
                                // Aggressive dy weighting crowds out floor-level states
                                // needed to survive tight spike corridors en route.
                                int dist = dy + dx / 3;
                                nextBeam.Add((sim, newInputs, dist));
                            }
                            if (cubeBeamFound) break;
                        }
                        if (cubeBeamFound) break;

                        nextBeam.Sort((a, b) => a.coinDist.CompareTo(b.coinDist));
                        cbeam.Clear();
                        int keep = Math.Min(CUBE_BEAM_WIDTH, nextBeam.Count);
                        for (int i = 0; i < keep; i++)
                            cbeam.Add((nextBeam[i].st, nextBeam[i].inputs));


                    }

                    _speculativeDepth--;

                    if (cubeBeamFound)
                    {
                        _coinInputScript.Clear();
                        _coinInputScriptCoinIdx = coin.Index;
                        for (int si = 1; si < cubeBeamScript.Count; si++)
                            _coinInputScript.Enqueue(cubeBeamScript[si]);
                        return cubeBeamScript[0];
                    }

                    break; // only try one coin per frame
                }
            }

            // ═══════════════════════════════════════════════════════════════
            //  Coin-jump override (cube mode only)
            //  When PreferCoins is active, check if jumping NOW would
            //  collect an uncollected coin ahead.  Simulate a jump
            //  trajectory and check for hitbox overlap with each nearby
            //  coin.  If the jump collects a coin AND survives the BFS
            //  horizon, force the jump — don't bother with the full BFS.
            // ═══════════════════════════════════════════════════════════════
            if (PreferCoins && allCoins.Count > 0 && state.OnGround && state.VelY_fixed == 0
                && state.GameMode == 0 && _speculativeDepth == 0)
            {
                int playerX = state.X_fixed >> 8;
                // Find the next uncollected coin within jump-arc range (~160px)
                for (int ci = _nextCoinCheckIdx; ci < allCoins.Count; ci++)
                {
                    var coin = allCoins[ci];
                    if (state.ProcessedSprites.Contains(coin.Index) || _forgivenCoins.Contains(coin.Index))
                        continue;
                    if (coin.HitLeft > playerX + 500) break; // beyond walk-to-pad range
                    if (coin.HitRight < playerX) continue;   // already passed

                    // Simulate jump trajectory and check for coin collection.
                    // Try THREE strategies (in priority order):
                    // 0. "Walk only" — pads auto-activate and can launch through coin
                    // 1. "Always jump + activate orbs" — for coins above (pad→orb chains)
                    // 2. "Single jump + fall freely" — for coins below (arc descent)
                    _speculativeDepth++;
                    var jumpSim = state.Clone();
                    bool coinCollected = false;
                    int jumpSurvival = 0;
                    int coinCollFrame = -1;
                    const int COIN_JUMP_HORIZON = 120;

                    // Strategy 0: walk only (no jumping) — pads launch player through coins.
                    // This is the most reliable path for ground-level coins with
                    // pads underneath. Walking straight to the pad is deterministic.
                    // Uses extended horizon (200) to reach distant pads.
                    // Strategy 0: walk-preferred with obstacle jumping at ground level.
                    // Walks by default. When grounded at ground level and walking
                    // would die (death tile ahead), jumps instead. This handles
                    // the pattern: descend from staircase → jump over ground
                    // obstacle → arc passes through coin (or land on pad → launch).
                    // Pads auto-activate by StepFrame when the player overlaps.
                    // Uses extended horizon (200) to reach distant scenarios.
                    {
                        var walkSim0 = state.Clone();
                        bool walkCoinColl = false;
                        int walkSurv0 = 0;
                        int walkCoinFrame0 = -1;
                        bool firstFrameInput = false;
                        var s0Inputs = new List<bool>(); // record ALL inputs for script replay
                        const int WALK_PAD_HORIZON = 200;
                        int simGroundY = (mapHeight - groundRowsToReserve) * 16 - 15;
                        for (int f = 0; f < WALK_PAD_HORIZON; f++)
                        {
                            bool input;
                            if (walkSim0.PendingOrbIndex >= 0)
                                input = true; // activate pending orb
                            else if (walkSim0.OnGround && walkSim0.VelY_fixed == 0
                                     && (walkSim0.Y_fixed >> 8) >= simGroundY - 5)
                            {
                                // At ground level — check if walking (no jump) would die
                                var testState = walkSim0.Clone();
                                bool testAlive = StepFrame(ref testState, false, out _);
                                input = !testAlive; // jump if walk dies
                            }
                            else
                                input = false; // elevated or airborne → just walk/fall

                            s0Inputs.Add(input); // record for script replay
                            if (f == 0) firstFrameInput = input;

                            bool walkAlive0 = StepFrame(ref walkSim0, input, out bool end0);
                            if (!walkAlive0) break;
                            walkSurv0 = f + 1;
                            if (end0) { walkSurv0 = COIN_JUMP_HORIZON; break; }

                            if (!walkCoinColl)
                            {
                                int nesX = (walkSim0.X_fixed >> 8) + 1;
                                int hbW = GetHitboxW(walkSim0.Mini);
                                int hbH = GetHitboxH(walkSim0.Mini);
                                int hbOffY = GetHitboxOffsetY(walkSim0.Mini, walkSim0.GravFlipped);
                                int pTop = (walkSim0.Y_fixed >> 8) + hbOffY;
                                int pBot = pTop + hbH;
                                int pRight = nesX + hbW;
                                bool xOv = !(pRight < coin.HitLeft || coin.HitRight < nesX);
                                bool yOv = !(pBot < coin.HitTop || coin.HitBottom < pTop);
                                if (xOv && yOv) { walkCoinColl = true; walkCoinFrame0 = f; }
                            }
                        }
                        if (walkCoinColl && walkSurv0 >= 30)
                        {
                            _speculativeDepth--;
                            // Load the remaining inputs (frames 1+) into the coin
                            // input script queue so subsequent frames replay exactly
                            // what Strategy 0 simulated, instead of the BFS diverging.
                            _coinInputScript.Clear();
                            _coinInputScriptCoinIdx = coin.Index;
                            for (int si = 1; si < s0Inputs.Count; si++)
                                _coinInputScript.Enqueue(s0Inputs[si]);
#if !DISABLE_DEBUG_LOGGING
                            PfLog($"[COIN_WALK_S0] Forcing {(firstFrameInput?"JUMP":"WALK")}: walk→pad→coin idx={coin.Index} sid=0x{coin.SpriteId:X2} at ({coin.HitLeft},{coin.HitTop})-({coin.HitRight},{coin.HitBottom}) walkSurv={walkSurv0}");
#endif
                            return firstFrameInput; // first frame's decision
                        }
                    }

                    // Strategy 1: always jump when grounded + activate orbs
                    List<bool>? coinJumpWinningInputs = null;
                    bool coinJumpHadOrbs = false;
                    var s1Inputs = new List<bool>();
                    bool s1HadOrb = false;
                    for (int f = 0; f < COIN_JUMP_HORIZON; f++)
                    {
                        if (jumpSim.PendingOrbIndex >= 0) s1HadOrb = true;
                        bool input = (jumpSim.VelY_fixed == 0 && jumpSim.OnGround)
                                  || (jumpSim.PendingOrbIndex >= 0);
                        s1Inputs.Add(input);
                        bool coinJumpAlive = StepFrame(ref jumpSim, input, out bool end);
                        if (!coinJumpAlive) break;
                        jumpSurvival = f + 1;
                        if (end) { jumpSurvival = COIN_JUMP_HORIZON; break; }

                        if (!coinCollected)
                        {
                            int nesX = (jumpSim.X_fixed >> 8) + 1;
                            int hbW = GetHitboxW(jumpSim.Mini);
                            int hbH = GetHitboxH(jumpSim.Mini);
                            int hbOffY = GetHitboxOffsetY(jumpSim.Mini, jumpSim.GravFlipped);
                            int pTop = (jumpSim.Y_fixed >> 8) + hbOffY;
                            int pBot = pTop + hbH;
                            int pRight = nesX + hbW;
                            bool xOv = !(pRight < coin.HitLeft || coin.HitRight < nesX);
                            bool yOv = !(pBot < coin.HitTop || coin.HitBottom < pTop);
                            if (xOv && yOv) { coinCollected = true; coinCollFrame = f; }
                        }
                    }
                    if (coinCollected) { coinJumpWinningInputs = s1Inputs; coinJumpHadOrbs = s1HadOrb; }

                    // Strategy 2: single jump + fall freely (no subsequent jumps)
                    // This handles coins below the player: the jump arc clears
                    // obstacles, then free-fall intersects the coin.
                    if (!coinCollected)
                    {
                        jumpSim = state.Clone();
                        jumpSurvival = 0;
                        bool hasJumped = false;
                        var s2Inputs = new List<bool>();
                        bool s2HadOrb = false;
                        for (int f = 0; f < COIN_JUMP_HORIZON; f++)
                        {
                            // Jump ONLY on first grounded frame, then fall freely
                            // Activate orbs mid-air (for pad→orb chains after landing)
                            if (jumpSim.PendingOrbIndex >= 0) s2HadOrb = true;
                            bool input = false;
                            if (!hasJumped && jumpSim.VelY_fixed == 0 && jumpSim.OnGround)
                            {
                                input = true;
                                hasJumped = true;
                            }
                            else if (jumpSim.PendingOrbIndex >= 0)
                            {
                                input = true; // activate orbs
                            }
                            s2Inputs.Add(input);
                            bool coinJumpAlive = StepFrame(ref jumpSim, input, out bool end2);
                            if (!coinJumpAlive) break;
                            jumpSurvival = f + 1;
                            if (end2) { jumpSurvival = COIN_JUMP_HORIZON; break; }

                            if (!coinCollected)
                            {
                                int nesX = (jumpSim.X_fixed >> 8) + 1;
                                int hbW = GetHitboxW(jumpSim.Mini);
                                int hbH = GetHitboxH(jumpSim.Mini);
                                int hbOffY = GetHitboxOffsetY(jumpSim.Mini, jumpSim.GravFlipped);
                                int pTop = (jumpSim.Y_fixed >> 8) + hbOffY;
                                int pBot = pTop + hbH;
                                int pRight = nesX + hbW;
                                bool xOv = !(pRight < coin.HitLeft || coin.HitRight < nesX);
                                bool yOv = !(pBot < coin.HitTop || coin.HitBottom < pTop);
                                if (xOv && yOv) { coinCollected = true; coinCollFrame = f; }
                            }
                        }
                        if (coinCollected) { coinJumpWinningInputs = s2Inputs; coinJumpHadOrbs = s2HadOrb; }
                    }
                    _speculativeDepth--;

                    // Only require survival for 10 frames AFTER coin collection.
                    if (coinCollected && jumpSurvival >= coinCollFrame + 10)
                    {
                        // Load the winning strategy's full input sequence (frames 1+)
                        // into the coin input script so ALL subsequent frames replay
                        // exactly what the simulation found, preventing BFS divergence.
                        if (coinJumpWinningInputs != null && coinJumpWinningInputs.Count > 1 && !coinJumpHadOrbs)
                        {
                            _coinInputScript.Clear();
                            _coinInputScriptCoinIdx = coin.Index;
                            for (int si = 1; si < coinJumpWinningInputs.Count; si++)
                                _coinInputScript.Enqueue(coinJumpWinningInputs[si]);
                        }
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[COIN_JUMP] Forcing jump to collect coin idx={coin.Index} sid=0x{coin.SpriteId:X2} at ({coin.HitLeft},{coin.HitTop})-({coin.HitRight},{coin.HitBottom}) jumpSurv={jumpSurvival} scriptLen={_coinInputScript.Count}");
#endif
                        return true;
                    }
                    break; // only check the nearest coin
                }
            }

            // ═══════════════════════════════════════════════════════════════
            //  Coin-walk override (cube mode only)
            //  When PreferCoins is active and a coin is BELOW the player,
            //  simulate walking off the current platform (never jumping)
            //  and check if the falling trajectory passes through the coin.
            //  This handles the common pattern of descending from an
            //  elevated position to collect a coin mid-fall.
            //  Pads are auto-activated by StepFrame, so pad→coin paths work.
            // ═══════════════════════════════════════════════════════════════
            if (PreferCoins && allCoins.Count > 0 && state.OnGround && state.VelY_fixed == 0
                && state.GameMode == 0 && _speculativeDepth == 0)
            {
                int playerX = state.X_fixed >> 8;
                int playerY = state.Y_fixed >> 8;
                for (int ci = _nextCoinCheckIdx; ci < allCoins.Count; ci++)
                {
                    var coin = allCoins[ci];
                    if (state.ProcessedSprites.Contains(coin.Index) || _forgivenCoins.Contains(coin.Index))
                        continue;
                    if (coin.HitLeft > playerX + 300) break;
                    if (coin.HitRight < playerX) continue;

                    int coinCenterY = (coin.HitTop + coin.HitBottom) / 2;
                    if (coinCenterY <= playerY) break; // coin is above or level — skip

                    // Coin is BELOW player. Simulate walk-only trajectory.
                    _speculativeDepth++;
                    var walkSim = state.Clone();
                    bool coinCollected = false;
                    int walkSurvival = 0;
                    int coinCollectedFrame = -1;
                    const int COIN_WALK_HORIZON = 120;
                    int dbgMinY = playerY, dbgMaxY = playerY;
                    int dbgEndX = playerX, dbgEndY = playerY;
                    for (int f = 0; f < COIN_WALK_HORIZON; f++)
                    {
                        // Never jump — just walk/fall. Activate orbs if pending
                        // (orb chains after pad bounces may be needed).
                        bool input = (walkSim.PendingOrbIndex >= 0);
                        bool walkAlive2 = StepFrame(ref walkSim, input, out bool end);
                        if (!walkAlive2) break;
                        walkSurvival = f + 1;
                        int simY = walkSim.Y_fixed >> 8;
                        int simX = walkSim.X_fixed >> 8;
                        dbgEndX = simX; dbgEndY = simY;
                        if (simY < dbgMinY) dbgMinY = simY;
                        if (simY > dbgMaxY) dbgMaxY = simY;
                        if (end) { walkSurvival = COIN_WALK_HORIZON; break; }

                        if (!coinCollected)
                        {
                            int nesX = (walkSim.X_fixed >> 8) + 1;
                            int hbW = GetHitboxW(walkSim.Mini);
                            int hbH = GetHitboxH(walkSim.Mini);
                            int hbOffY = GetHitboxOffsetY(walkSim.Mini, walkSim.GravFlipped);
                            int pTop = (walkSim.Y_fixed >> 8) + hbOffY;
                            int pBot = pTop + hbH;
                            int pRight = nesX + hbW;
                            bool xOv = !(pRight < coin.HitLeft || coin.HitRight < nesX);
                            bool yOv = !(pBot < coin.HitTop || coin.HitBottom < pTop);
                            if (xOv && yOv) { coinCollected = true; coinCollectedFrame = f; }
                        }
                    }
                    _speculativeDepth--;
                    Console.Error.WriteLine($"[COIN_WALK_DBG] idx={coin.Index} playerXY=({playerX},{playerY}) coinXY=({coin.HitLeft},{coin.HitTop})-({coin.HitRight},{coin.HitBottom}) collected={coinCollected} collFrame={coinCollectedFrame} walkSurv={walkSurvival} minY={dbgMinY} maxY={dbgMaxY} endXY=({dbgEndX},{dbgEndY})");
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[COIN_WALK_DBG] idx={coin.Index} playerXY=({playerX},{playerY}) coinXY=({coin.HitLeft},{coin.HitTop})-({coin.HitRight},{coin.HitBottom}) coinCenterY={coinCenterY} collected={coinCollected} collFrame={coinCollectedFrame} walkSurv={walkSurvival}");
#endif

                    if (coinCollected && walkSurvival >= 30)
                    {
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[COIN_WALK] Forcing WALK to descend toward coin idx={coin.Index} sid=0x{coin.SpriteId:X2} at ({coin.HitLeft},{coin.HitTop})-({coin.HitRight},{coin.HitBottom}) walkSurv={walkSurvival}");
#endif
                        return false; // force WALK (don't jump)
                    }
                    break;
                }
            }

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
            // Standard grounded position: floorYpx - 15 (16px cube).
            // "Elevated" means the cube is noticeably above the standard floor,
            // which can happen on platforms, stairs, or obstacle tops.
            // Use a 16px (1 tile) threshold: any cube at least 1 full tile above
            // the normal floor gets an extended horizon to see distant walls
            // that would otherwise be beyond the 120-frame normal horizon.
            int standardGroundedY = floorYpx - 15;
            bool isElevated = currentYpx <= standardGroundedY - 16;
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
            //
            // HOLD-JUMP FAST PATH — DISABLED
            // The hold-jump fast path was originally an optimization that
            // simulated "always jump at every landing" and, if the path
            // survived the full BFS horizon, immediately returned JUMP
            // without running BFS.  This optimization broke levels like
            // baseafterbase where the cube must walk under a COL_TOP
            // platform:  the fast path "survived" but committed the cube
            // to a jump+rhythm that later collided with the obstacle just
            // beyond the horizon.  The BFS alone correctly differentiates
            // JUMP vs WALK by exploring both branches, so we now always
            // fall through to BFS.
            // (Code removed to avoid dead-code warnings.)

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
            //   os:             orb-skipped: true if this node descends from a speculative orb-skip variant
            var alive = new List<(SimState s, bool j0, int lj, int tl, int my, bool os)>();

            // Best results per frame-0 choice (survival frames, then X as tiebreaker)
            int bestF0JumpSurv = -1, bestF0JumpX = int.MinValue;
            bool bestF0JumpHoldPattern = false;
            int bestF0JumpMinY = int.MaxValue; // Y of best-scoring terminal in jump lineage
            int bestF0WalkSurv = -1, bestF0WalkX = int.MinValue;
            int bestF0WalkMinY = int.MaxValue; // Y of best-scoring terminal in walk lineage

            void RecordTerminal(bool j0, int survFrames, int xPx, int lj, int tl, int minY, bool orbSkipped = false)
            {
                // Orb-skip descendants are explored for BFS diversity but
                // excluded from the jump/walk decision — their survival
                // represents phantom paths that actual execution won't follow.
                if (orbSkipped) return;
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
            alive.Add((state.Clone(), true, 0, 0, state.Y_fixed >> 8, false));
            alive.Add((state.Clone(), false, 0, 0, state.Y_fixed >> 8, false));

            _speculativeDepth++;

            for (int f = 0; f < BFS_HORIZON && alive.Count > 0; f++)
            {
                var next = new List<(SimState s, bool j0, int lj, int tl, int my, bool os)>();

                foreach (var node in alive)
                {
                    // ── Orb-skip branching ──
                    // Detect if an orb overlaps the player (pending from previous
                    // frame OR will be detected this frame by ProcessSprites).
                    // When an orb is present, expand TWO variants of this node:
                    //   variant 0: normal (orb may activate if input=true)
                    //   variant 1: orb pre-skipped (added to ProcessedSprites)
                    // This allows the BFS to find "trick orb" solutions where
                    // NOT hitting the orb produces a longer survival path.
                    int orbSkipTarget = -1;
                    if (node.s.PendingOrbIndex >= 0)
                        orbSkipTarget = node.s.PendingOrbIndex;
                    else
                    {
                        ScanForOrbOverlap(node.s, out int nearIdx);
                        if (nearIdx >= 0 && !node.s.ProcessedSprites.Contains(nearIdx))
                            orbSkipTarget = nearIdx;
                    }
                    int numVariants = (orbSkipTarget >= 0) ? 2 : 1;

                    for (int ov = 0; ov < numVariants; ov++)
                    {
                        SimState varState;
                        if (ov == 0)
                            varState = node.s;
                        else
                        {
                            varState = node.s.Clone();
                            varState.ProcessedSprites.Add(orbSkipTarget);
                            varState.PendingOrbIndex = -1;
                            varState.PendingOrbSpriteId = -1;
                        }
                        // Track orb-skip lineage: true if this variant or any ancestor was orb-skipped
                        bool isOS = node.os || ov == 1;

                    bool isGrounded = varState.VelY_fixed == 0 && varState.OnGround
                        && (varState.GameMode == 0 || varState.GameMode == 2);

                    if (isGrounded)
                    {
                        // Landing frame: branch into "jump" and "don't jump"
                        int newTl = node.tl + 1;

                        // ── Jump branch ──
                        {
                            var sj = varState.Clone();
                            bool aliveJ = StepFrame(ref sj, true, out bool endJ);
                            bool j0 = (f == 0) ? true : node.j0;
                            int newLj = node.lj + 1;
                            int jMinY = sj.Y_fixed >> 8;
                            if (endJ) RecordTerminal(j0, BFS_HORIZON, sj.X_fixed >> 8, newLj, newTl, jMinY, isOS);
                            else if (aliveJ) next.Add((sj, j0, newLj, newTl, jMinY, isOS));
                            else RecordTerminal(j0, f, sj.X_fixed >> 8, newLj, newTl, jMinY, isOS);
                        }

                        // ── Walk branch ──
                        {
                            var sw = varState.Clone();
                            bool orbForce = sw.PendingOrbIndex >= 0
                                && !_btSkipSpecificOrbs.Contains(sw.PendingOrbIndex);
                            if (sw.PendingOrbIndex >= 0 && _btSkipSpecificOrbs.Contains(sw.PendingOrbIndex))
                            {
                                sw.ProcessedSprites.Add(sw.PendingOrbIndex);
                                sw.PendingOrbIndex = -1;
                                sw.PendingOrbSpriteId = -1;
                            }
                            bool aliveW = StepFrame(ref sw, orbForce, out bool endW);
                            bool j0 = (f == 0) ? false : node.j0;
                            int wMinY = sw.Y_fixed >> 8;
                            if (endW) RecordTerminal(j0, BFS_HORIZON, sw.X_fixed >> 8, node.lj, newTl, wMinY, isOS);
                            else if (aliveW) next.Add((sw, j0, node.lj, newTl, wMinY, isOS));
                            else RecordTerminal(j0, f, sw.X_fixed >> 8, node.lj, newTl, wMinY, isOS);
                        }
                    }
                    else
                    {
                        // Airborne: predict whether landing occurs this frame.
                        bool predictLanding = (varState.GameMode == 0) && CubeWillLandThisFrame(varState);
                        if (predictLanding)
                        {
                            // Landing predicted: branch into "jump on landing" and "walk"
                            int newTl = node.tl + 1;
                            for (int bi = 0; bi < 2; bi++)
                            {
                                bool bfsInput = (bi == 0); // true = jump on landing
                                if (varState.PendingOrbIndex >= 0
                                    && !_btSkipSpecificOrbs.Contains(varState.PendingOrbIndex))
                                    bfsInput = true;
                                var sa = varState.Clone();
                                if (sa.PendingOrbIndex >= 0 && _btSkipSpecificOrbs.Contains(sa.PendingOrbIndex))
                                {
                                    sa.ProcessedSprites.Add(sa.PendingOrbIndex);
                                    sa.PendingOrbIndex = -1;
                                    sa.PendingOrbSpriteId = -1;
                                }
                                bool aliveA = StepFrame(ref sa, bfsInput, out bool endA);
                                bool j0 = (f == 0) ? bfsInput : node.j0;
                                int newLj = (bi == 0) ? node.lj + 1 : node.lj;
                                int aMinY = sa.Y_fixed >> 8;
                                if (endA) RecordTerminal(j0, BFS_HORIZON, sa.X_fixed >> 8, newLj, newTl, aMinY, isOS);
                                else if (aliveA) next.Add((sa, j0, newLj, newTl, aMinY, isOS));
                                else RecordTerminal(j0, f, sa.X_fixed >> 8, newLj, newTl, aMinY, isOS);
                            }
                        }
                        else
                        {
                            // Pure airborne: single successor, no choice to make
                            var sa = varState.Clone();
                            bool orbInput = sa.PendingOrbIndex >= 0
                                && !_btSkipSpecificOrbs.Contains(sa.PendingOrbIndex);
                            if (sa.PendingOrbIndex >= 0 && _btSkipSpecificOrbs.Contains(sa.PendingOrbIndex))
                            {
                                sa.ProcessedSprites.Add(sa.PendingOrbIndex);
                                sa.PendingOrbIndex = -1;
                                sa.PendingOrbSpriteId = -1;
                            }
                            bool aliveA = StepFrame(ref sa, orbInput, out bool endA);
                            int aMinY = sa.Y_fixed >> 8;
                            if (endA) RecordTerminal(node.j0, BFS_HORIZON, sa.X_fixed >> 8, node.lj, node.tl, aMinY, isOS);
                            else if (aliveA) next.Add((sa, node.j0, node.lj, node.tl, aMinY, isOS));
                            else RecordTerminal(node.j0, f, sa.X_fixed >> 8, node.lj, node.tl, aMinY, isOS);
                        }
                    }

                    } // end variant loop
                }

                alive = next;

                if (alive.Count > MAX_ALIVE)
                {
                    // Deduplicate: states with same physics within a lineage are redundant.
                    // Key: (Y_fixed, VelY_fixed, OnGround, GravFlipped, GameMode, jumpedFrame0)
                    // Keep the one with highest landingsJumped/totalLandings (most "jump-like")
                    var deduped = new Dictionary<(int, int, bool, bool, int, bool), (SimState s, bool j0, int lj, int tl, int my, bool os)>();
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
                            var sampled = new List<(SimState s, bool j0, int lj, int tl, int my, bool os)>(half);
                            for (int i = 0; i < half; i++)
                                sampled.Add(jumpL[(int)((long)i * jumpL.Count / half)]);
                            jumpL = sampled;
                        }
                        if (walkL.Count > half)
                        {
                            var sampled = new List<(SimState s, bool j0, int lj, int tl, int my, bool os)>(half);
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
                RecordTerminal(node.j0, BFS_HORIZON, node.s.X_fixed >> 8, node.lj, node.tl, node.my, node.os);
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

            // Suppress elevation bonus when coin-seeking toward a coin that is
            // BELOW the current position — climbing is counterproductive.
            if (PreferCoins && elevBonus > 0)
            {
                int playerX2 = state.X_fixed >> 8;
                int playerY2 = state.Y_fixed >> 8;
                for (int ci2 = _nextCoinCheckIdx; ci2 < allCoins.Count; ci2++)
                {
                    var coin2 = allCoins[ci2];
                    if (state.ProcessedSprites.Contains(coin2.Index) || _forgivenCoins.Contains(coin2.Index))
                        continue;
                    if (coin2.HitLeft > playerX2 + 2000) break;
                    if (coin2.HitRight < playerX2) continue;
                    int coinCenterY2 = (coin2.HitTop + coin2.HitBottom) / 2;
                    if (coinCenterY2 > playerY2) // coin is below player
                        elevBonus = 0; // don't reward climbing away from the coin
                    break;
                }
            }

            // ── Coin proximity bonus ──
            // When PreferCoins is active and an uncollected coin is ahead,
            // reward the lineage whose terminal Y is closer to the coin's
            // center Y.  The bonus scales with proximity (stronger when
            // closer) AND with survival ratio (weaker when the favored
            // path has much worse survival than the other).
            // Range: 2000px lookahead so routing decisions start early
            // enough to reach coins that require different altitude paths.
            // NOT gated on both-paths-surviving: we want to bias routing
            // even when one path is riskier, as long as that path still
            // survives enough frames to be viable.
            int coinBonusJump = 0, coinBonusWalk = 0;
            int cubeCoinTargetY = -1; // for elevation bonus suppression
            if (PreferCoins && allCoins.Count > 0
                && bestF0JumpMinY != int.MaxValue && bestF0WalkMinY != int.MaxValue
                && bestF0JumpSurv > 0 && bestF0WalkSurv > 0)
            {
                int playerX = state.X_fixed >> 8;
                for (int ci = _nextCoinCheckIdx; ci < allCoins.Count; ci++)
                {
                    var coin = allCoins[ci];
                    if (state.ProcessedSprites.Contains(coin.Index) || _forgivenCoins.Contains(coin.Index))
                        continue;
                    if (coin.HitLeft > playerX + 4000) break; // 4000px lookahead
                    if (coin.HitRight < playerX) continue;    // already passed
                    cubeCoinTargetY = (coin.HitTop + coin.HitBottom) / 2;
                    int coinXDist = Math.Max(0, coin.HitLeft - playerX);
                    double proximityScale = Math.Max(0.1, 1.0 - (double)coinXDist / 4000.0);
                    int jumpDistToCoin = Math.Abs(bestF0JumpMinY - cubeCoinTargetY);
                    int walkDistToCoin = Math.Abs(bestF0WalkMinY - cubeCoinTargetY);
                    int rawDiff = walkDistToCoin - jumpDistToCoin;
                    // Scale bonus up to BFS_HORIZON — makes coin proximity a
                    // dominant factor when both paths survive equally.
                    // Use a threshold: if the favored path survives at least
                    // MIN_COIN_SURV frames, apply the full bonus. Below that,
                    // scale linearly. This allows the BFS to choose riskier
                    // paths toward coins as long as they're minimally viable.
                    const int MIN_COIN_SURV = 30;
                    int scaledBonus = (int)(Math.Abs(rawDiff) * proximityScale);
                    int favoredSurv = rawDiff > 0 ? bestF0JumpSurv : bestF0WalkSurv;
                    if (favoredSurv < MIN_COIN_SURV)
                        scaledBonus = scaledBonus * favoredSurv / MIN_COIN_SURV;
                    scaledBonus = Math.Min(scaledBonus, BFS_HORIZON);
                    if (rawDiff > 0) coinBonusJump = scaledBonus;
                    else if (rawDiff < 0) coinBonusWalk = scaledBonus;
                    break; // only target the first uncollected coin
                }
            }

            bool shouldJump;
            // Only give hold-pattern bonus when jump lineage survives the full
            // BFS horizon.  A jump that dies in 8 frames shouldn't get +5 just
            // because the terminal happened to be in a hold-jump pattern.
            // ALSO suppress holdBonus when the walk path stays lower (closer to
            // ground) — this indicates the cube should walk under an obstacle
            // rather than jumping into it (e.g. COL_TOP platforms).
            bool walkStaysLower = bestF0WalkMinY != int.MaxValue
                && bestF0JumpMinY != int.MaxValue
                && bestF0WalkMinY > bestF0JumpMinY;
            int holdBonus = (bestF0JumpHoldPattern && bestF0JumpSurv >= BFS_HORIZON && !walkStaysLower) ? 5 : 0;

            // ── Altitude ceiling penalty (coin retry) ──
            // When retrying after forgiven coins, penalize JUMP when the
            // player is already above the coin's altitude ceiling. This
            // gently biases the BFS toward walking (descending staircases)
            // when ground-level routing is needed to reach a pad/coin.
            // Uses 1 frame per pixel above ceiling — soft enough that the
            // BFS can still jump for survival (e.g. over spikes) when walk
            // dies quickly, but strong enough to prefer walk when both paths
            // survive similarly.
            int altPenaltyJump = 0;
            if (_coinAltitudePenalties.Count > 0)
            {
                int playerX_ap = state.X_fixed >> 8;
                int playerY_ap = state.Y_fixed >> 8;
                foreach (var (apStartX, apEndX, apCeilingY) in _coinAltitudePenalties)
                {
                    if (playerX_ap < apStartX || playerX_ap > apEndX) continue;
                    // Apply a FLAT minimum penalty within the zone to always
                    // prefer walking over jumping.  Without this, players at
                    // ground level (below the ceiling) see zero penalty and
                    // happily jump onto staircases, defeating the purpose of
                    // the altitude bias.  The flat penalty is large enough to
                    // dominate tiebreakers (both paths survive full BFS) but
                    // small enough that survival-critical jumps still win
                    // (e.g. jump survives 60 frames vs walk dies at 5 frames:
                    // 60-30 = 30 > 5 → jump wins).
                    altPenaltyJump = 30;
                    if (playerY_ap < apCeilingY)
                    {
                        // Player is above the ceiling — penalize jumping (going higher)
                        // 1 frame per pixel above ceiling = moderate bias toward walk
                        altPenaltyJump = Math.Max(altPenaltyJump, apCeilingY - playerY_ap);
                    }
                    break;
                }
            }

            // Suppress elevation bonus when altitude penalty zones are active.
            // The altitude penalty steers the player toward ground level for
            // coin collection via pads; the elevation bonus would counteract
            // this by rewarding the exact climbing behavior we're trying to prevent.
            if (altPenaltyJump > 0 && elevBonus > 0)
                elevBonus = 0;

            // Suppress coin proximity bonus (jump) when altitude penalty is active.
            // Without this, a high coin (e.g. Y=183 for blue-pad gravity-flip coin)
            // biases the BFS toward jumping (climbing closer to the coin), which
            // counteracts the altitude penalty's ground-level descent bias.
            // The player needs to descend to the pad first; the pad provides the
            // lift to the coin, not direct jumping.
            if (altPenaltyJump > 0 && coinBonusJump > 0)
                coinBonusJump = 0;

            int jumpScore = bestF0JumpSurv + holdBonus + elevBonus + coinBonusJump - altPenaltyJump;
            int walkScore = bestF0WalkSurv + coinBonusWalk;
            if (jumpScore > walkScore)
                shouldJump = true;
            else if (walkScore > jumpScore)
                shouldJump = false;
            else if (isElevated && bestF0JumpSurv >= BFS_HORIZON && bestF0WalkSurv >= BFS_HORIZON
                     && bestF0JumpMinY > bestF0WalkMinY) // walk genuinely stays higher
                shouldJump = false; // elevated tiebreak: walk preserves altitude better
            else if (isElevated && bestF0JumpSurv >= BFS_HORIZON && bestF0WalkSurv >= BFS_HORIZON)
                shouldJump = false; // elevated generic: walk maintains the trajectory the
                                    // elevated BFS internally found viable; jumping changes
                                    // the arc phase and can miss the wall gap
            else if (bestF0JumpSurv >= BFS_HORIZON && bestF0WalkSurv >= BFS_HORIZON
                     && bestF0WalkMinY > bestF0JumpMinY) // walk genuinely stays lower (closer to ground)
                shouldJump = false; // floor-level tiebreak: walk avoids overhead obstacles
            else if (bestF0JumpX >= bestF0WalkX)
                shouldJump = true;  // tied: prefer jump (maintains momentum)
            else
                shouldJump = false;

#if !DISABLE_DEBUG_LOGGING
            PfLog($"[DECIDE_CUBE] BFS: jumpSurv={bestF0JumpSurv} jumpX={bestF0JumpX} holdPat={bestF0JumpHoldPattern} jumpMinY={bestF0JumpMinY}, walkSurv={bestF0WalkSurv} walkX={bestF0WalkX} walkMinY={bestF0WalkMinY} elevBonus={elevBonus} coinBonusJ={coinBonusJump} coinBonusW={coinBonusWalk} → {(shouldJump?"JUMP":"WALK")}");
#endif

            if (!shouldJump)
            {
                // BFS says don't jump this frame. But don't commit a delay —
                // next frame BFS re-evaluates from scratch.
                return false;
            }

            return true;
        }

        // ═══════════════════════════════════════════════════════════════
        //  Cross-corridor coin pre-forgiveness
        //  Scans portals left-to-right to determine the game mode at each
        //  coin's position. Coins that are in a different mode section than
        //  their preceding mode boundary are pre-forgiven so the ship PD
        //  doesn't steer toward unreachable coins.
        // ═══════════════════════════════════════════════════════════════
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private void PreForgiveCrossCorridorCoins(int startGameMode)
        {
            if (!PreferCoins || allCoins.Count == 0) return;

            // Build a sorted list of mode transitions: (X position, new mode)
            var modeTransitions = new List<(int x, int mode)>();
            modeTransitions.Add((0, startGameMode)); // level starts in this mode
            foreach (var sp in allSprites)
            {
                if (!IsGameModePortal(sp.SpriteId)) continue;
                int portalMode = SpriteIdToGameMode(sp.SpriteId);
                if (portalMode < 0) continue;
                modeTransitions.Add((sp.AnchorX_px, portalMode));
            }
            // allSprites is already sorted by AnchorX_px, so modeTransitions is sorted

            // For each coin, determine what mode is active at the coin's X
            // and what mode the PREVIOUS section was in.
            foreach (var coin in allCoins)
            {
                int coinX = coin.HitLeft;
                // Scan transitions to find the mode at the coin and the
                // mode of the section immediately before it.
                int prevSectionMode = startGameMode;
                int modeAtCoin = startGameMode;
                int lastTransitionX = 0;
                for (int t = 0; t < modeTransitions.Count; t++)
                {
                    if (modeTransitions[t].x > coinX) break;
                    prevSectionMode = modeAtCoin;
                    modeAtCoin = modeTransitions[t].mode;
                    lastTransitionX = modeTransitions[t].x;
                }

                // Pre-forgive coins where the previous section is ship (mode=1)
                // and the coin is in a different (non-ship) section.
                // Only ships have the PD drift problem.
                // Only forgive coins within 2500px of the portal boundary —
                // the ship PD scans ~2000px ahead, so coins further out
                // can't be reached by ship drift.
                int distFromBoundary = coinX - lastTransitionX;
                if (modeAtCoin != 1 && prevSectionMode == 1 && distFromBoundary <= 2000)
                {
                    _forgivenCoins.Add(coin.Index);
                    _crossCorrForgivenCoins[coin.Index] = modeAtCoin;
                }
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private void UnforgiveCrossCorridorCoins(int newMode)
        {
            if (_crossCorrForgivenCoins.Count == 0) return;
            var toRemove = new List<int>();
            foreach (var kvp in _crossCorrForgivenCoins)
            {
                if (kvp.Value == newMode)
                {
                    _forgivenCoins.Remove(kvp.Key);
                    toRemove.Add(kvp.Key);
                }
            }
            foreach (var idx in toRemove)
                _crossCorrForgivenCoins.Remove(idx);
        }

        /// <summary>
        /// Ship decision: track the corridor center using simple proportional control.
        /// Hold (thrust up in normal gravity) if below the target, release if above.
        /// Uses 1-frame safety check: if the chosen action causes death, flip.
        /// This replaces the previous greedy lookahead which had subtle bugs causing
        /// the ship to stay at ground level and never climb.
        ///
        /// When PreferCoins is enabled, the tree search adds a leaf proximity bonus
        /// that biases the ship toward the next uncollected coin, while still
        /// prioritizing survival (scaled by SURV_SCALE=1000 per frame).
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

            // ── Coin-aware: find next coin target Y for PD tiebreaker ──
            int coinTargetY = -1; // -1 = no coin target
            int coinTargetIdx = -1;
            int coinDistX = 0; // horizontal distance to coin
            SpriteEntry coinEntry = default;
            bool hasCoinEntry = false;
            if (PreferCoins && allCoins.Count > 0)
            {
                int playerX = state.X_fixed >> 8;
                for (int ci = _nextCoinCheckIdx; ci < allCoins.Count; ci++)
                {
                    var coin = allCoins[ci];
                    if (state.ProcessedSprites.Contains(coin.Index) || _forgivenCoins.Contains(coin.Index))
                        continue;
                    if (coin.HitLeft > playerX + 2000) break; // increased from 1000 to 2000
                    if (coin.HitRight < playerX) continue; // already passed
                    coinTargetY = (coin.HitTop + coin.HitBottom) / 2;
                    coinTargetIdx = coin.Index;
                    coinDistX = coin.HitLeft - playerX;
                    if (coinDistX < 0) coinDistX = 0;
                    coinEntry = coin;
                    hasCoinEntry = true;
                    break;
                }
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

            // ===== BEAM SEARCH FOR SHIP COINS =====
            // Runs INDEPENDENTLY of survival difference (survH vs survR).
            // At large distances, both hold/release may survive equally well,
            // but we need to start descending early to reach the coin.
            // Only run once per approach: when distX is in the sweet spot (40-200px).
            if (coinTargetY >= 0 && hasCoinEntry && _coinInputScript.Count == 0
                && coinDistX >= 40 && coinDistX <= 200
                && _beamSearchAttemptedCoinIdx != coinTargetIdx)
            {
                const int BEAM_WIDTH = 128;
                int beamHorizon = Math.Min(80, coinDistX + 10); // enough to reach coin
                _speculativeDepth++;

                var beam = new List<(SimState st, List<bool> inputs)>();
                beam.Add((state.Clone(), new List<bool>()));

                bool foundTrajectory = false;
                var bestScript = new List<bool>();

                for (int step = 0; step < beamHorizon && beam.Count > 0; step++)
                {
                    var nextBeam = new List<(SimState st, List<bool> inputs, int coinDist)>();

                    foreach (var (bs, bInputs) in beam)
                    {
                        for (int tryHold = 0; tryHold <= 1; tryHold++)
                        {
                            bool inp = (tryHold == 1);
                            var sim = bs.Clone();
                            if (!StepFrame(ref sim, inp, out _)) continue; // died

                            var newInputs = new List<bool>(bInputs) { inp };

                            // Check coin overlap
                            int nx = (sim.X_fixed >> 8) + 1;
                            int hbW_s = GetHitboxW(sim.Mini);
                            int hbH_s = GetHitboxH(sim.Mini);
                            int hbOff_s = GetHitboxOffsetY(sim.Mini, sim.GravFlipped);
                            int pT = (sim.Y_fixed >> 8) + hbOff_s;
                            if (!(nx + hbW_s < coinEntry.HitLeft || coinEntry.HitRight < nx) &&
                                !(pT + hbH_s < coinEntry.HitTop || coinEntry.HitBottom < pT))
                            {
                                // Coin collected! But verify the ship can survive
                                // by simulating PD steering for 40 more frames.
                                int corridorY = FindCorridorCenter(ref sim);
                                bool survives = true;
                                var postSim = sim.Clone();
                                for (int pf = 0; pf < 40; pf++)
                                {
                                    int psy = postSim.Y_fixed >> 8;
                                    int psVel = postSim.VelY_fixed;
                                    int ppErr = (postSim.GravMul > 0) ? (psy - corridorY) : (corridorY - psy);
                                    int pvComp = -(psVel * postSim.GravMul);
                                    bool pInp = (ppErr - pvComp * 2) > 0;
                                    if (!StepFrame(ref postSim, pInp, out _)) { survives = false; break; }
                                }
                                if (survives)
                                {
                                    foundTrajectory = true;
                                    bestScript = newInputs;
                                    break;
                                }
                                // else: would die after collecting, keep searching
                            }

                            // Eliminate states past the coin
                            int sx = sim.X_fixed >> 8;
                            if (sx > coinEntry.HitRight + 4) continue;

                            int sy = sim.Y_fixed >> 8;
                            int dy = Math.Abs(sy - coinTargetY);
                            int dx = Math.Max(0, coinEntry.HitLeft - sx);
                            int dist = dy + dx / 4;
                            nextBeam.Add((sim, newInputs, dist));
                        }
                        if (foundTrajectory) break;
                    }
                    if (foundTrajectory) break;

                    nextBeam.Sort((a, b) => a.coinDist.CompareTo(b.coinDist));
                    beam.Clear();
                    int keep = Math.Min(BEAM_WIDTH, nextBeam.Count);
                    for (int i = 0; i < keep; i++)
                        beam.Add((nextBeam[i].st, nextBeam[i].inputs));

                }

                _speculativeDepth--;
                _beamSearchAttemptedCoinIdx = coinTargetIdx; // don't re-run for this coin

                if (foundTrajectory)
                {
                    _coinInputScript.Clear();
                    _coinInputScriptCoinIdx = coinTargetIdx;
                    for (int si = 1; si < bestScript.Count; si++)
                        _coinInputScript.Enqueue(bestScript[si]);
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[SHIP_COIN_BEAM] Found trajectory for coin idx={coinEntry.Index}, scriptLen={bestScript.Count}");
#endif
                    return bestScript[0];
                }
            }

            if (survH != survR)
            {
                if (coinTargetY >= 0 && hasCoinEntry
                    && Math.Min(survH, survR) >= SHIP_TREE_DEPTH / 2)
                {
                    // Coin nearby and both paths survive well — check which initial
                    // input (hold vs release) leads to collecting the coin within
                    // SHIP_COIN_HORIZON frames of PD steering.
                    // No survival-difference threshold: always simulate when both
                    // paths have decent survival. Only force the coin-collecting
                    // path if it survives at least SHIP_TREE_DEPTH/2 frames.
                    const int SHIP_COIN_HORIZON = 120;
                    _speculativeDepth++;

                    bool holdCollects = false;
                    int holdDiedAtFrame = -1;
                    {
                        var sim = state.Clone();
                        StepFrame(ref sim, true, out _);
                        for (int f = 1; f < SHIP_COIN_HORIZON; f++)
                        {
                            int sy = sim.Y_fixed >> 8;
                            int sVel = sim.VelY_fixed;
                            int pErr = (sim.GravMul > 0) ? (sy - coinTargetY) : (coinTargetY - sy);
                            int vComp = -(sVel * sim.GravMul);
                            bool inp = (pErr - vComp) > 0; // D-gain=1 for faster coin convergence
                            if (!StepFrame(ref sim, inp, out _)) { holdDiedAtFrame = f; break; }
                            int nx = (sim.X_fixed >> 8) + 1;
                            int hbW_s = GetHitboxW(sim.Mini);
                            int hbH_s = GetHitboxH(sim.Mini);
                            int hbOff_s = GetHitboxOffsetY(sim.Mini, sim.GravFlipped);
                            int pT = (sim.Y_fixed >> 8) + hbOff_s;
                            if (!(nx + hbW_s < coinEntry.HitLeft || coinEntry.HitRight < nx) &&
                                !(pT + hbH_s < coinEntry.HitTop || coinEntry.HitBottom < pT))
                            { holdCollects = true; break; }
                        }
                    }

                    bool releaseCollects = false;
                    int relDiedAtFrame = -1;
                    {
                        var sim = state.Clone();
                        StepFrame(ref sim, false, out _);
                        for (int f = 1; f < SHIP_COIN_HORIZON; f++)
                        {
                            int sy = sim.Y_fixed >> 8;
                            int sVel = sim.VelY_fixed;
                            int pErr = (sim.GravMul > 0) ? (sy - coinTargetY) : (coinTargetY - sy);
                            int vComp = -(sVel * sim.GravMul);
                            bool inp = (pErr - vComp) > 0; // D-gain=1 for faster coin convergence
                            if (!StepFrame(ref sim, inp, out _)) { relDiedAtFrame = f; break; }
                            int nx = (sim.X_fixed >> 8) + 1;
                            int hbW_s = GetHitboxW(sim.Mini);
                            int hbH_s = GetHitboxH(sim.Mini);
                            int hbOff_s = GetHitboxOffsetY(sim.Mini, sim.GravFlipped);
                            int pT = (sim.Y_fixed >> 8) + hbOff_s;
                            if (!(nx + hbW_s < coinEntry.HitLeft || coinEntry.HitRight < nx) &&
                                !(pT + hbH_s < coinEntry.HitTop || coinEntry.HitBottom < pT))
                            { releaseCollects = true; break; }
                        }
                    }

                    _speculativeDepth--;

#if !DISABLE_DEBUG_LOGGING
                    if (coinDistX < 400)
                        PfLog($"[SHIP_COIN_SIM] idx={coinEntry.Index} playerX={state.X_fixed >> 8} distX={coinDistX} holdCol={holdCollects} holdDied={holdDiedAtFrame} relCol={releaseCollects} relDied={relDiedAtFrame} survH={survH} survR={survR} threshold={_shipCoinAggressiveThreshold}");
#endif

                    if (holdCollects && !releaseCollects && survH >= SHIP_TREE_DEPTH / 2)
                    {
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[SHIP_COIN] Hold collects coin idx={coinEntry.Index} — forcing hold (survH={survH} survR={survR})");
#endif
                        return true;
                    }
                    if (releaseCollects && !holdCollects && survR >= SHIP_TREE_DEPTH / 2)
                    {
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[SHIP_COIN] Release collects coin idx={coinEntry.Index} — forcing release (survH={survH} survR={survR})");
#endif
                        return false;
                    }

                    // Fall through — if survival diff is small,
                    // use PD tiebreaker for gradual coin steering
                    if (Math.Abs(survH - survR) <= _shipCoinAggressiveThreshold)
                    {
                        // fall through to PD tiebreaker below
                    }
                    else
                    {
                        return survH > survR;
                    }
                }
                else
                {
                    // No coin or not both surviving well — pure survival
                    return survH > survR;
                }
            }

            // Equal survival (or small difference with coin target) — use velocity-aware
            // corridor tracking as tiebreaker.  When coins are active, steer fully
            // toward the coin Y (not blended) for stronger coin-seeking.
            int biasPixels = (int)((JumpTimingBias - 0.5) * 16.0);
            int targetY;
            if (coinTargetY >= 0)
            {
                targetY = coinTargetY;
            }
            else
            {
                targetY = FindCorridorCenter(ref state) + _shipCorridorBias + biasPixels;
            }
            int currentY = state.Y_fixed >> 8;
            int velY = state.VelY_fixed;

            int posError = (state.GravMul > 0) ? (currentY - targetY) : (targetY - currentY);
            int velComponent = -(velY * state.GravMul);
            // Use D-gain=1 when coin-seeking for faster convergence (less damping),
            // D-gain=2 normally for smoother corridor tracking
            int dGain = (coinTargetY >= 0) ? 1 : 2;
            int pdSignal = posError - (velComponent * dGain);

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
        /// Run a mini-BFS from the given state, branching at every landing
        /// into jump/walk.  Auto-activates all encountered orbs (no orb-skip
        /// branching).  Returns (bestSurvivalFrames, bestX_px).
        /// Used for orb hit/skip evaluation so the future path quality matches
        /// the full BFS rather than the simplistic linear sim.
        /// </summary>
        private (int bestSurv, int bestX) EvaluatePathBFS(SimState startState, int horizon, int maxAlive)
        {
            int bestSurv = 0;
            int bestX = startState.X_fixed >> 8;
            var alive = new List<SimState>();
            alive.Add(startState.Clone());

            for (int f = 0; f < horizon && alive.Count > 0; f++)
            {
                var next = new List<SimState>();
                foreach (var s in alive)
                {
                    bool isGrounded = s.VelY_fixed == 0 && s.OnGround
                        && (s.GameMode == 0 || s.GameMode == 2);

                    if (isGrounded)
                    {
                        // Landing frame: branch into "jump" and "don't jump"
                        for (int bi = 0; bi < 2; bi++)
                        {
                            var sb = s.Clone();
                            bool bfsInput = (bi == 0); // 0=jump, 1=walk
                            // Auto-activate pending orbs
                            if (sb.PendingOrbIndex >= 0
                                && !_btSkipSpecificOrbs.Contains(sb.PendingOrbIndex))
                                bfsInput = true;
                            else if (sb.PendingOrbIndex >= 0
                                && _btSkipSpecificOrbs.Contains(sb.PendingOrbIndex))
                            {
                                sb.ProcessedSprites.Add(sb.PendingOrbIndex);
                                sb.PendingOrbIndex = -1;
                                sb.PendingOrbSpriteId = -1;
                            }
                            bool aliveB = StepFrame(ref sb, bfsInput, out bool endB);
                            if (endB)
                            {
                                bestSurv = horizon;
                                bestX = Math.Max(bestX, sb.X_fixed >> 8);
                            }
                            else if (aliveB)
                                next.Add(sb);
                            else
                            {
                                int xPx = sb.X_fixed >> 8;
                                if (f > bestSurv || (f == bestSurv && xPx > bestX))
                                { bestSurv = f; bestX = xPx; }
                            }
                        }
                    }
                    else
                    {
                        bool predictLanding = (s.GameMode == 0) && CubeWillLandThisFrame(s);
                        if (predictLanding)
                        {
                            // Landing predicted: branch into "jump on landing" and "walk"
                            for (int bi = 0; bi < 2; bi++)
                            {
                                var sa = s.Clone();
                                bool bfsInput = (bi == 0);
                                if (sa.PendingOrbIndex >= 0
                                    && !_btSkipSpecificOrbs.Contains(sa.PendingOrbIndex))
                                    bfsInput = true;
                                else if (sa.PendingOrbIndex >= 0
                                    && _btSkipSpecificOrbs.Contains(sa.PendingOrbIndex))
                                {
                                    sa.ProcessedSprites.Add(sa.PendingOrbIndex);
                                    sa.PendingOrbIndex = -1;
                                    sa.PendingOrbSpriteId = -1;
                                }
                                bool aliveA = StepFrame(ref sa, bfsInput, out bool endA);
                                if (endA)
                                {
                                    bestSurv = horizon;
                                    bestX = Math.Max(bestX, sa.X_fixed >> 8);
                                }
                                else if (aliveA)
                                    next.Add(sa);
                                else
                                {
                                    int xPx = sa.X_fixed >> 8;
                                    if (f > bestSurv || (f == bestSurv && xPx > bestX))
                                    { bestSurv = f; bestX = xPx; }
                                }
                            }
                        }
                        else
                        {
                            // Pure airborne: single successor, auto-activate orbs
                            var sa = s.Clone();
                            bool orbInput = sa.PendingOrbIndex >= 0
                                && !_btSkipSpecificOrbs.Contains(sa.PendingOrbIndex);
                            if (sa.PendingOrbIndex >= 0
                                && _btSkipSpecificOrbs.Contains(sa.PendingOrbIndex))
                            {
                                sa.ProcessedSprites.Add(sa.PendingOrbIndex);
                                sa.PendingOrbIndex = -1;
                                sa.PendingOrbSpriteId = -1;
                            }
                            bool aliveA = StepFrame(ref sa, orbInput, out bool endA);
                            if (endA)
                            {
                                bestSurv = horizon;
                                bestX = Math.Max(bestX, sa.X_fixed >> 8);
                            }
                            else if (aliveA)
                                next.Add(sa);
                            else
                            {
                                int xPx = sa.X_fixed >> 8;
                                if (f > bestSurv || (f == bestSurv && xPx > bestX))
                                { bestSurv = f; bestX = xPx; }
                            }
                        }
                    }
                }

                alive = next;

                // Cap alive states
                if (alive.Count > maxAlive)
                {
                    // Deduplicate by physics state
                    var deduped = new Dictionary<(int, int, bool, bool, int), SimState>();
                    foreach (var nd in alive)
                    {
                        var key = (nd.Y_fixed, nd.VelY_fixed, nd.OnGround, nd.GravFlipped, nd.GameMode);
                        if (!deduped.ContainsKey(key))
                            deduped[key] = nd;
                    }
                    alive = deduped.Values.ToList();

                    // Y-diverse sampling if still over cap
                    if (alive.Count > maxAlive)
                    {
                        alive = alive.OrderBy(nd => nd.Y_fixed).ToList();
                        var sampled = new List<SimState>(maxAlive);
                        for (int i = 0; i < maxAlive; i++)
                            sampled.Add(alive[(int)((long)i * alive.Count / maxAlive)]);
                        alive = sampled;
                    }
                }

                if (bestSurv >= horizon) break;
            }

            return (bestSurv, bestX);
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
                    if (!input && f >= jumpFrame && jumpFrame >= 0 && s.PendingOrbIndex >= 0
                        && !_btSkipSpecificOrbs.Contains(s.PendingOrbIndex))
                        input = true;
                    if (input) initialJumpDone = true;
                }
                else if (chainJumps && (s.GameMode == 0 || s.GameMode == 2) && s.VelY_fixed == 0 && s.OnGround)
                {
                    input = holdAfterLanding || QuickDangerCheck(s);
                }

                // Auto-activate orbs encountered after the initial action.
                // Gated on chainJumps so that singleJumpOnly callers (orb/pad
                // evaluations) test only the direct physical consequence.
                // Respect _btSkipSpecificOrbs: suppress targeted trick orbs.
                if (chainJumps && initialJumpDone && !input && s.PendingOrbIndex >= 0)
                {
                    if (_btSkipSpecificOrbs.Contains(s.PendingOrbIndex))
                    {
                        s.ProcessedSprites.Add(s.PendingOrbIndex);
                        s.PendingOrbIndex = -1;
                        s.PendingOrbSpriteId = -1;
                    }
                    else
                        input = true;
                }

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
                    // Respect _btSkipSpecificOrbs: suppress targeted trick orbs.
                    if (!input && f >= jumpFrame && jumpFrame >= 0 && s.PendingOrbIndex >= 0
                        && !_btSkipSpecificOrbs.Contains(s.PendingOrbIndex))
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
                // Gated on chainJumps so singleJumpOnly paths stay passive.
                // Respect _btSkipSpecificOrbs: suppress targeted trick orbs.
                if (chainJumps && initialJumpDone && !input && s.PendingOrbIndex >= 0)
                {
                    if (_btSkipSpecificOrbs.Contains(s.PendingOrbIndex))
                    {
                        s.ProcessedSprites.Add(s.PendingOrbIndex);
                        s.PendingOrbIndex = -1;
                        s.PendingOrbSpriteId = -1;
                    }
                    else
                        input = true;
                }

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
            if (s.PendingOrbIndex >= 0 && input)
            {
#if !DISABLE_DEBUG_LOGGING
                PfLog($"[ORB_ACTIVATE] sid=0x{s.PendingOrbSpriteId:X2} gravFlipped={s.GravFlipped} mini={s.Mini}");
#endif
                // Record orb hit for targeted backtrack (only during real execution)
                if (_speculativeDepth == 0)
                    _hitOrbHistory.Add(s.PendingOrbIndex);
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
                    var (ceilHit, ceilBotY_prox, ceilSpikeDeath) = CheckCeiling(collX_chk, testY_chk, hbW_chk, hbH_chk);
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
                        // Snap Y to ceiling surface (same formula as CubeEject)
                        // to prevent sub-pixel drift each frame.
                        int newY_prox = ceilBotY_prox - hbOffY_chk - 1;
                        s.Y_fixed = newY_prox << 8;
                        s.VelY_fixed = 0;
                        s.OnGround = true;
                        s.WasZeroedByCollision = true;
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
                // Use fresh grounded probe (like sim) instead of stale OnGround flag —
                // the ball can slide past the edge of a platform between frames,
                // making OnGround stale while the sim correctly detects no surface.
                // NOTE: SIM does NOT require VelY==0 for ball flip — only grounded + input.
                //
                // INPUT BUFFER: The SIM has a multi-frame input buffer mechanism.
                // When the player presses while airborne, the SIM buffers the press
                // and waits up to PF_BALL_HOLD_FRAMES for the ball to become grounded,
                // then executes the flip.  The PF must model this to stay in sync:
                // input=True starts/refreshes the buffer even if not grounded;
                // subsequent frames check buffer + grounded to fire the flip.
                bool ballGrounded = (s.BallFlipCooldown > 0) || BallIsGrounded(ref s);
                bool shouldFlip = false;
                if (input)
                {
                    s.BallInputBuffer = BALL_INPUT_BUFFER_FRAMES;
                    if (ballGrounded)
                        shouldFlip = true;
                }
                else if (s.BallInputBuffer > 0)
                {
                    if (ballGrounded)
                        shouldFlip = true;
                    else
                        s.BallInputBuffer--;
                }
                if (shouldFlip)
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
                    s.BallInputBuffer = 0; // Consume buffer
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
                // Ceiling landing — NES CubeEject_Fresh uses velY <= 0
                // so that the ceiling check also runs when velocity was
                // zeroed by the proximity check (matching the NES simulator's
                // unconditional landing branch for reversed gravity).
                if (s.VelY_fixed <= 0)
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
                    else
                    {
                        // No ceiling above — cube is falling away from ceiling.
                        // Mirrors the normal-gravity "no floor" path.
                        s.OnGround = false;
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
        /// Fresh ball grounded probe — matches SIM's per-frame grounded check
        /// in BallPhysics_Fresh.partial.cs.  The SIM re-probes collision every
        /// frame instead of relying on a persistent OnGround flag.  This detects
        /// when the ball has slid past the edge of a platform (OnGround would be
        /// stale, but the real ground is gone).
        /// Normal gravity:  CheckFloor  at playerBottom,     height 2.
        /// Inverted gravity: CheckCeiling at playerTop − 2, height 2.
        /// </summary>
        private bool BallIsGrounded(ref SimState s)
        {
            int playerX_px = s.X_fixed >> 8;
            int playerY_px = s.Y_fixed >> 8;
            int hbW = GetHitboxW(s.Mini);
            int hbH = GetHitboxH(s.Mini);
            int hbOffY = GetHitboxOffsetY(2, s.Mini, s.GravFlipped);
            int collX = playerX_px;

            if (!s.GravFlipped)
            {
                // Normal gravity: test 2px tall region at player's bottom edge
                int playerBottom = playerY_px + hbOffY + hbH;
                var (hit, _, _) = CheckFloor(collX, playerBottom, hbW, 2);
                return hit;
            }
            else
            {
                // Inverted gravity: test 2px tall region above player's top
                int playerTop = playerY_px + hbOffY;
                var (hit, _, _) = CheckCeiling(collX, playerTop - 2, hbW, 2);
                return hit;
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
            // Pads fire on collision — no skip/decide logic needed.

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

            // ── Ball coin-collection preference ──
            // When coins are enabled and a coin is nearby, check which viable
            // delays actually collect the coin.  Prefer those over non-collecting.
            if (PreferCoins && allCoins.Count > 0 && viableDelays.Count > 0)
            {
                int playerX_sc = state.X_fixed >> 8;
                for (int ci = _nextCoinCheckIdx; ci < allCoins.Count; ci++)
                {
                    var coin = allCoins[ci];
                    if (state.ProcessedSprites.Contains(coin.Index) || _forgivenCoins.Contains(coin.Index))
                        continue;
                    if (coin.HitLeft > playerX_sc + 800) break;
                    if (coin.HitRight < playerX_sc) continue;

                    // Found a nearby coin — test which delays collect it
                    const int BALL_COIN_HORIZON = 120;
                    var coinDelays = new List<(int delay, int survival, int xProgress)>();

                    _speculativeDepth++;
                    foreach (var (delay, survival, xProgress) in viableDelays)
                    {
                        // Simulate this delay path and check for coin overlap
                        var sim = state.Clone();
                        bool collected = false;
                        for (int f = 0; f < BALL_COIN_HORIZON; f++)
                        {
                            bool inp = (f == delay); // flip at the delay frame
                            if (!StepFrame(ref sim, inp, out bool eol)) break;
                            if (eol) break;

                            // Check coin hitbox overlap
                            int nx = (sim.X_fixed >> 8) + 1;
                            int hbW_b = GetHitboxW(sim.Mini);
                            int hbH_b = GetHitboxH(sim.Mini);
                            int hbOff_b = GetHitboxOffsetY(sim.Mini, sim.GravFlipped);
                            int pT = (sim.Y_fixed >> 8) + hbOff_b;
                            if (!(nx + hbW_b < coin.HitLeft || coin.HitRight < nx) &&
                                !(pT + hbH_b < coin.HitTop || coin.HitBottom < pT))
                            { collected = true; break; }
                        }

                        if (collected)
                            coinDelays.Add((delay, survival, xProgress));
                    }
                    _speculativeDepth--;

                    if (coinDelays.Count > 0)
                    {
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[BALL_COIN] {coinDelays.Count}/{viableDelays.Count} delays collect coin idx={coin.Index} — preferring coin delays");
#endif
                        viableDelays = coinDelays;
                    }

                    // Also test no-press path for coin collection
                    if (coinDelays.Count == 0)
                    {
                        _speculativeDepth++;
                        var simNp = state.Clone();
                        bool npCollects = false;
                        for (int f = 0; f < BALL_COIN_HORIZON; f++)
                        {
                            if (!StepFrame(ref simNp, false, out bool eol)) break;
                            if (eol) break;
                            int nx = (simNp.X_fixed >> 8) + 1;
                            int hbW_b = GetHitboxW(simNp.Mini);
                            int hbH_b = GetHitboxH(simNp.Mini);
                            int hbOff_b = GetHitboxOffsetY(simNp.Mini, simNp.GravFlipped);
                            int pT = (simNp.Y_fixed >> 8) + hbOff_b;
                            if (!(nx + hbW_b < coin.HitLeft || coin.HitRight < nx) &&
                                !(pT + hbH_b < coin.HitTop || coin.HitBottom < pT))
                            { npCollects = true; break; }
                        }
                        _speculativeDepth--;

                        if (npCollects)
                        {
#if !DISABLE_DEBUG_LOGGING
                            PfLog($"[BALL_COIN] No-press collects coin idx={coin.Index}, no flip delays do — returning false");
#endif
                            return false; // Don't flip, let no-press collect the coin
                        }
                    }

                    break; // Only check the nearest coin
                }
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

            // Check past max fall speed → decelerate (NES common_gravity_routine)
            // Ship uses SHIP_MAX_FALLSPEED_HOLD as tmpfallspeed in CommonGravityRoutine_Fresh.
            // Matches CubeGravity/BallGravityStep/UfoGravityStep pattern.
            int shipMaxFS = ShipMaxFallSpeedHold(s.Mini);
            if (gravMul > 0)
            {
                if (s.VelY_fixed > shipMaxFS)
                    gravity = -gravity;
            }
            else
            {
                if (s.VelY_fixed < -shipMaxFS)
                    gravity = -gravity;
            }

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
            // NES sprite_collide uses (0x10-h)>>1 unconditionally (centers hitbox in cell).
            // No gravity dependence for sprite collision.
            int hbOffY = s.Mini ? 4 : 0; // (0x10 - 7) >> 1 = 4 for mini
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
                    // Blue pads have a gravity gate: bottom pads only when gravity
                    // is normal, top pads only when gravity is inverted.
                    if (IsBluePad(sid))
                    {
                        bool isBottomBluePad = (sid == 0x0D || sid == 0xFD);
                        if (isBottomBluePad && s.GravFlipped) continue;   // bottom pad needs normal grav
                        if (!isBottomBluePad && !s.GravFlipped) continue; // top pad needs inverted grav
                    }

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
                    // Un-forgive cross-corridor coins whose section mode
                    // matches the new mode so they can now be collected.
                    UnforgiveCrossCorridorCoins(mode);
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

            // NOTE: NES bg_side_coll_common also calls bg_coll_spikes() which can
            // set the death flag (cube_data |= 1).  However, the NES uses a
            // DEFERRED death flag that gets CLEARED by floor/ceiling landing
            // (COLL_CHECK_BOTTOM/TOP does cube_data &= ~1).  Without that
            // cancellation mechanism, firing death here is too aggressive —
            // the player would die when running past upward spikes at ground
            // level, which the NES does not.  The center-point death check
            // (CheckDeathCollision) already handles the final, non-cancellable
            // death.  Side-probe spike death is omitted until a full deferred
            // death-flag system is implemented.

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
