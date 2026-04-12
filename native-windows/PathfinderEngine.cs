using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Concurrent;

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
        // -- Constants ------------------------------------------------------
        private const int TILE = 16;
        private const int NES_H = 15;                      // NES screen height in tiles
        private const int SCREEN_H_PX = NES_H * TILE;     // NES screen height in pixels (240)
        private const int SHIP_SCROLL_SPEED_FIXED = 0x0266; // smooth-scroll speed (~2.4 px/frame, 8.8 fixed)
        private const int PORTAL_TO_TOP_DIFF_PX = 0x3A;     // 58px offset from portal Y to screen top
        private const int LOOKAHEAD_HORIZON = 90;
        private const int MAX_SPECULATIVE_DEPTH = 3; // max recursion depth for chained jump evaluation
        private const int CORRIDOR_LOOK_AHEAD_TILES = 15; // scan ahead for obstacles (240px � 87 frames at 1x)
        private const int SHIP_LOOKAHEAD_HORIZON = 30;     // multi-frame lookahead for ship decisions
        private const int SHIP_TREE_DEPTH = 20;            // binary-tree search depth for ship
        private const int SHIP_TREE_MAX_NODES = 16000;      // cap on total nodes explored in tree search
        private const int MAX_FRAMES = 60 * 60 * 8; // 8 minutes at 60fps (enough for ~4600-tile levels at 1x speed)
        private const int MAX_BACKTRACK_ATTEMPTS = 500; // max backtrack retries PER death point (reset after each success)
        private const int MAX_TOTAL_BACKTRACK_ATTEMPTS = 5000; // absolute cap on total backtracks across all deaths
        private const int MAX_TOTAL_ITERATIONS = MAX_FRAMES * 10; // hard cap on total frame iterations (including replays)
        private const int MAX_CHECKPOINT_DEPTH = 200;   // max saved decision checkpoints (deep history for long levels)
        // No rewind limit � backtracker goes as far back as needed
        private const int MIN_CHECKPOINT_SPACING = 4;   // minimum frames between consecutive checkpoints
        private const int ELEV_THRESHOLD = 12;              // <1 tile � elevation difference to trigger exploration

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

        /// <summary>
        /// When true, uses exhaustive BFS exploration instead of heuristic PD controller.
        /// Explores all possible input sequences with state deduplication.
        /// </summary>
        public bool UseBFS { get; set; } = false;

        /// <summary>
        /// When true, verbose BFS/eject messages are printed to stderr.
        /// When false (default), only the progress percentage is shown.
        /// </summary>
        public bool Verbose { get; set; } = false;

        private System.IO.TextWriter _log = System.IO.TextWriter.Null;

        public Action<List<(int x, int y)>?, int, int, bool>? OnSpeculativePath { get; set; }
        public int CurrentX_px => _currentX_px;
        private volatile int _currentX_px;

        // Cube hitbox � delegate to SharedPhysics so SIM and PF can't drift
        private const int CUBE_HITBOX_W = SharedPhysics.CUBE_HITBOX_W;
        private const int CUBE_HITBOX_H = SharedPhysics.CUBE_HITBOX_H;
        private const int MINI_CUBE_HITBOX_W = SharedPhysics.MINI_CUBE_HITBOX_W;
        private const int MINI_CUBE_HITBOX_H = SharedPhysics.MINI_CUBE_HITBOX_H;

        // -- Speed lookup (delegate to SharedPhysics) ----------------------
        private static int SpeedUiIndexToFixed(int uiIndex) => SharedPhysics.SpeedUiIndexToFixed(uiIndex);
        private static int SpriteIdToSpeedFixed(int sid) => SharedPhysics.SpriteIdToSpeedFixed(sid);
        private static int SpriteIdToGameMode(int sid) => SharedPhysics.SpriteIdToGameMode(sid);

        private static int GetGravity(bool mini) => SharedPhysics.GetCubeGravity(mini);
        private static int GetJumpVel(bool mini) => SharedPhysics.GetCubeJumpVel(mini);
        private const int ROBOT_JUMP_VEL = -0x2B0;   // Robot jump velocity (reapplied each held frame)
        private const int ROBOT_JUMP_TIME = 19;       // Max hold frames for robot jump
        private const int NINJA_MAX_JUMPS = 3;        // Triple jump for ninja mode (resets on ground)
        private static int GetHitboxW(bool mini) => SharedPhysics.GetCubeHitboxW(mini);
        private static int GetHitboxH(bool mini) => SharedPhysics.GetCubeHitboxH(mini);
        /// <summary>
        /// Mode-aware hitbox Y offset. Cube/robot/ninja use bottom-aligned hitbox
        /// (offset 9 normal gravity, 0 reversed). All other modes center the hitbox (offset 4).
        /// </summary>
        private static int GetHitboxOffsetY(int gameMode, bool mini, bool gravFlipped)
            => SharedPhysics.GetHitboxOffsetY(gameMode, mini, gravFlipped);

        // Ball physics constants (delegate to SharedPhysics)
        private static int BallGravity(bool mini) => SharedPhysics.BallGravity(mini);
        private static int BallSwitchVel(bool mini) => SharedPhysics.BallSwitchVel(mini);
        private static int BallMaxFallSpeed(bool mini) => SharedPhysics.BallMaxFallSpeed(mini);
        private const int BALL_INPUT_BUFFER_FRAMES = 8; // Must match PF_BALL_HOLD_FRAMES in Pathfinder.partial.cs

        // Ship physics constants (delegate to SharedPhysics)
        private static int ShipGravityBase(bool mini) => SharedPhysics.ShipGravityBase(mini);
        private static int ShipGravityAfterHold(bool mini) => SharedPhysics.ShipGravityAfterHold(mini);
        private static int ShipGravityHoldFall(bool mini) => SharedPhysics.ShipGravityHoldFall(mini);
        private static int ShipGravity(bool mini) => SharedPhysics.ShipGravity(mini);
        private static int ShipMaxFallSpeed(bool mini) => SharedPhysics.ShipMaxFallSpeed(mini);
        private static int ShipMaxFallSpeedHold(bool mini) => SharedPhysics.ShipMaxFallSpeedHold(mini);

        // UFO physics constants (delegate to SharedPhysics)
        private static int UfoGravity(bool mini) => SharedPhysics.UfoGravity(mini);
        private static int UfoJumpVel(bool mini) => SharedPhysics.UfoJumpVel(mini);
        private static int UfoMaxFallSpeed(bool mini) => SharedPhysics.UfoMaxFallSpeed(mini);

        // -- Sprite classification (delegate to SharedPhysics) --------------
        private static bool IsSpeedPortal(int sid) => SharedPhysics.IsSpeedPortal(sid);
        private static bool IsGameModePortal(int sid) => SharedPhysics.IsGameModePortal(sid);
        private static bool IsGravityPortal(int sid) => SharedPhysics.IsGravityPortal(sid);
        private static bool IsReverseGravity(int sid) => SharedPhysics.IsReverseGravity(sid);
        private static bool IsMiniGrowthPortal(int sid) => SharedPhysics.IsMiniGrowthPortal(sid);
        private static bool IsEndLevel(int sid) => SharedPhysics.IsEndLevel(sid);
        private static bool IsYellowPad(int sid) => SharedPhysics.IsYellowPad(sid);
        private static bool IsPinkPad(int sid) => SharedPhysics.IsPinkPad(sid);
        private static bool IsRedPad(int sid) => SharedPhysics.IsRedPad(sid);
        private static bool IsBluePad(int sid) => SharedPhysics.IsBluePad(sid);

        /// <summary>
        /// Extract skipped pad indices from the final state's ProcessedSprites.
        /// Pads are never added to ProcessedSprites when activated (they fire every frame),
        /// so any pad index in ProcessedSprites was intentionally skipped by the pathfinder.
        /// </summary>
        private void ExtractSkippedPads(in SimState finalState)
        {
            SkippedPadIndices = new HashSet<int>();
            foreach (var sp in allSprites)
            {
                if (!finalState.ProcessedSprites.Contains(sp.Index)) continue;
                if (IsAnyPad(sp.SpriteId))
                {
                    SkippedPadIndices.Add(sp.Index);
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[SKIPPED_PAD] idx={sp.Index} sid=0x{sp.SpriteId:X2}");
#endif
                }
            }
        }
        private static bool IsGreenPad(int sid) => SharedPhysics.IsGreenPad(sid);

        // -- Orb classification (delegate to SharedPhysics) -----------------
        private static bool IsYellowOrb(int sid) => SharedPhysics.IsYellowOrb(sid);
        private static bool IsYellowOrbBigger(int sid) => SharedPhysics.IsYellowOrbBigger(sid);
        private static bool IsYellowOrbSmaller(int sid) => SharedPhysics.IsYellowOrbSmaller(sid);
        private static bool IsPinkOrb(int sid) => SharedPhysics.IsPinkOrb(sid);
        private static bool IsRedOrb(int sid) => SharedPhysics.IsRedOrb(sid);
        private static bool IsBlueOrb(int sid) => SharedPhysics.IsBlueOrb(sid);
        private static bool IsGreenOrb(int sid) => SharedPhysics.IsGreenOrb(sid);
        private static bool IsBlackOrb(int sid) => SharedPhysics.IsBlackOrb(sid);
        private static bool IsWhiteOrb(int sid) => SharedPhysics.IsWhiteOrb(sid);
        private static bool IsVelocityOrb(int sid) => SharedPhysics.IsVelocityOrb(sid);
        private static bool IsGravityOrb(int sid) => SharedPhysics.IsGravityOrb(sid);
        private static bool IsOrbSprite(int sid) => SharedPhysics.IsOrbSprite(sid);

        // -- Pad/Orb velocity tables (delegate to SharedPhysics) --------
        private static int GetPadOrbModeCol(int gameMode) => SharedPhysics.GetPadOrbModeCol(gameMode);
        private static int GetPadOrbVel(int row, bool mini, int gameMode) => SharedPhysics.GetPadOrbVel(row, mini, gameMode);

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
            // NES sprite_collide: Generic.x = high_byte(currplayer_x) + 1
            // Both left and right edges use nesX (the +1 offset).
            int currentX_px = state.X_fixed >> 8;
            int nesX = currentX_px + 1;
            int hbW = GetHitboxW(state.Mini);
            int hbH = GetHitboxH(state.Mini);
            // NES orb check uses sprite centering: Generic.y += ((0x10 - height) >> 1)
            // = +4 for mini normal gravity, 0 otherwise (matches CubePhysics_Fresh).
            int spriteOffY = (state.Mini && !state.GravFlipped) ? SharedPhysics.GetMiniCenterOffsetY(true) : 0;
            int playerY_px = state.Y_fixed >> 8;
            int playerTop = playerY_px + spriteOffY;
            int playerBottom = playerTop + hbH;
            int playerRight = nesX + hbW;

            foreach (var sp in allSprites)
            {
                if (state.ProcessedSprites.Contains(sp.Index)) continue;
                if (sp.HitRight < nesX) continue;
                if (sp.AnchorX_px - TILE > playerRight + TILE) break;

                int sid = sp.SpriteId;
                if (!IsOrbSprite(sid)) continue;

                bool xOverlap = !(playerRight < sp.HitLeft || sp.HitRight < nesX);
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
            int hbOffY = GetHitboxOffsetY(state.GameMode, state.Mini, state.GravFlipped);
            int playerY_px = state.Y_fixed >> 8;
            int playerTop = playerY_px + hbOffY;
            int playerBottom = playerTop + hbH;

            foreach (var sp in allSprites)
            {
                if (state.ProcessedSprites.Contains(sp.Index)) continue;
                if (sp.HitRight < currentX_px) continue;
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

        private static bool IsAnyPad(int sid) => SharedPhysics.IsAnyPad(sid);

        // -- Hold-jump state (persists across frames in the main loop) ----
        private bool _cubeJumpedThisStep; // set by StepFrame when a cube jump actually fires
        private bool _cubeHoldJump = false; // when true, keep jumping every landing
        private int _cubeHoldDelay = 0;    // frames to wait before first jump in hold mode
        private int _committedJumpDelay = -1; // when >= 0, counting down to a committed single-jump
        private int _committedRobotHold = 0; // remaining frames to hold robot jump button
        private Queue<int> _committedNinjaJumps = new Queue<int>(); // queued air jump delays for ninja
        private int _ninjaWaitFrames = 0; // countdown to next committed ninja jump
        private bool _prevFrameWasGrounded = true; // tracks whether PREVIOUS frame started grounded; used to gate hold-jump fast path

        // -- Backtracking state -------------------------------------------
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
            public int NextCoinCheckIdx; // _nextCoinCheckIdx at checkpoint
        }
        private List<BacktrackCheckpoint> _backtrackCheckpoints = new();
        // Saved cube checkpoint at the last cube?ship game mode transition.
        // Used during cross-mode escalation to change the ship entry altitude
        // when the normal checkpoint list has been evicted of cube checkpoints.
        private BacktrackCheckpoint? _lastCubeToShipCheckpoint;
        // Persistent copy of ship entry checkpoint for coin collect-lose
        // deep recovery (not consumed on use, unlike _lastCubeToShipCheckpoint).
        private BacktrackCheckpoint? _shipEntryRecoveryCheckpoint;
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
        private int _shipForceReleaseFirstFrames; // when >0, force release BEFORE hold (for dip-first sine waves)
        private int _shipForceHoldFrames;    // when >0, force hold input for this many frames
        private int _shipForceReleaseFrames; // when >0, force release input for this many frames
        private int _shipCommitFrames;       // remaining frames of current hold/release commitment
        private bool _shipCommitHold;        // true = committed to hold, false = committed to release

        // -- Grounded walk counters (for periodic eval triggers) -------
        private int _cubeGroundedWalkFrames;  // consecutive grounded frames without jumping (cube)
        private int _ballGroundedWalkFrames;  // consecutive grounded frames without flipping (ball)
        private int _lastSpecMinLandY;        // min Y where VelY==0 during last speculative sim (surface elevation)

        // -- Best-so-far path tracking (for partial results on fail/cancel) --
        private int _bestPathHighWaterX;      // highest X (pixels) reached by any attempted path
        private List<(int x, int y)> _bestPathPoints = new();  // snapshot of PathPoints at best X
        private List<(int x, int y)> _bestPath2Points = new(); // snapshot of Path2Points at best X
        private List<bool> _bestInputs = new();                 // snapshot of Inputs at best X
        private bool _prevDualActiveForPath; // tracks dual-mode transitions for P2 path sentinel breaks

        // -- Backtrack timing --
        private System.Diagnostics.Stopwatch? _backtrackTimer;
        private const int MAX_BACKTRACK_SECONDS = 60; // hard time limit on backtracking per death point
        private const int MAX_BACKTRACK_SECONDS_COIN = 120; // extended time limit for coin retry backtracking

        // -- Speculative depth / frame counter (needed in both debug and release) ---
        private int _speculativeDepth; // >0 means we're inside lookahead � suppress logging
        private int _frameCounter;     // current frame in the main Run() loop
        [ThreadStatic] private static bool _dualP2Guard; // true during P2's StepFrame call (prevents infinite recursion)
        [ThreadStatic] private static List<int>? _p1OrbIndicesThisFrame; // orb indices P1 added to ProcessedSprites this frame (temporarily removed for P2)
        [ThreadStatic]
        private static bool _p2OrbFlippedOtherGrav; // set by P2's blue/green orb to flip P1's gravity


        // -- Debug logging ---------------------------------------------------
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

        /// <summary>Optional level/TMX name written to the log header for identification.</summary>
        public string LevelName { get; set; } = "";
#else
        public string DebugLogPath => string.Empty;
        /// <summary>Optional level/TMX name (no-op when logging disabled).</summary>
        public string LevelName { get; set; } = "";
#endif

        /// <summary>NES scroll Y config hi byte (optional).</summary>
        public int? ConfigScrollYHi { get; set; }
        /// <summary>NES scroll Y config lo byte (optional).</summary>
        public int? ConfigScrollYLo { get; set; }

        /// <summary>
        /// Compute initial camera Y (fixed-point) for BFS/simulation start.
        /// Uses NES scroll Y config when available, otherwise centers on player.
        /// </summary>
        private int ComputeInitCameraY(int startY_px)
        {
            int maxCamY = Math.Max(0, (mapHeight - NES_H) * TILE) << 8;
            if (ConfigScrollYHi.HasValue)
            {
                int hi = ConfigScrollYHi.Value & 0xFF;
                int lo = (ConfigScrollYLo.HasValue ? ConfigScrollYLo.Value : 0) & 0xFF;
                int linearScroll = hi * 240 + lo;
                int linearMax = 2 * 240 + 239; // 719 = linearize(0x02EF)
                int pixelsFromBottom = linearMax - linearScroll;
                int maxCamPx = (mapHeight - NES_H) * TILE;
                int tmxCamY = Math.Max(0, maxCamPx - pixelsFromBottom);
                return Math.Min(maxCamY, tmxCamY << 8);
            }
            return Math.Max(0, Math.Min(maxCamY, (startY_px << 8) - ((SCREEN_H_PX / 2) << 8)));
        }

        // -- Frame trace for diagnostics ---------------------------------
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
        // Trace disabled in production builds � no-op stubs
        private void TraceFrameOpen() { }
        private void TraceFrame(int frame, ref SimState s, bool input, bool alive) { }
        private void TraceFrameClose() { }
        public string FrameTracePath => string.Empty;
#endif

        // -- Input data (immutable after construction) ----------------------
        private readonly int[] tiles;       // SANITIZED: negatives ? 0, matching simulator
        private readonly int[] sprites;
        private readonly Dictionary<int, (int anchorTileX, int anchorTileY)> spriteAnchors;
        private readonly Dictionary<int, (int offsetX, int offsetY)> spritePixelOffsets;
        private readonly int mapWidth, mapHeight;
        private readonly int groundRowsToReserve;
        private readonly int maxFallSpeed;
        private readonly SharedPhysics.CollisionMap _collisionMap;

        // Pre-sorted sprite list for efficient processing
        private readonly List<SpriteEntry> allSprites;
        private SpriteEntry[] _spritesArr; // array view for indexed access + binary search

        // Coin-specific list (subset of allSprites with coin sprite IDs)
        private readonly List<SpriteEntry> allCoins;
        // Mode portal positions for BFS Y-bias (precomputed at init)
        private readonly List<(int X, int Y)> _modePortalPositions;
        // Next coin index to check for collection/miss detection
        private int _nextCoinCheckIdx;
        // Coins that were missed and forgiven (backtrack failed, continue without them)
        private readonly HashSet<int> _forgivenCoins = new HashSet<int>();
        // Track how many times MISSED_COIN has triggered for each coin index.
        // After a threshold (3), auto-forgive � the coin is collectable but not survivable.
        private readonly Dictionary<int, int> _coinMissRetryCount = new Dictionary<int, int>();
        private const int MAX_COIN_MISS_RETRIES = 5;
        // Coins auto-forgiven due to MAX_COIN_MISS_RETRIES being exceeded.
        // This set PERSISTS across RunSingleAttempt calls so the retry logic
        // can skip coins that were proven unsurviable.
        private readonly HashSet<int> _autoForgivenCoins = new HashSet<int>();
        // Track how many times a coin was collected then invalidated by
        // backtrack (collect-then-lose cycling).  After threshold, auto-forgive.
        private readonly Dictionary<int, int> _coinCollectThenLoseCount = new Dictionary<int, int>();
        private const int MAX_COIN_COLLECT_LOSE = 5;
        // Game mode at the time each coin was forgiven (0=cube,1=ship,2=ball,3=ufo,etc.)
        private readonly Dictionary<int, int> _forgivenCoinGameModes = new Dictionary<int, int>();
        // Permanently collected coins � persists across backtracks so that undoing
        // a path segment doesn't lose a coin that was already collected.  After
        // restoring from a checkpoint, these indices are re-added to ProcessedSprites.
        // Maps coin index ? frame at which it was collected, so we can invalidate
        // coins that were collected on paths that got truncated by a later rewind.
        private readonly Dictionary<int, int> _permanentlyCollectedCoins = new Dictionary<int, int>();
        // Index into allCoins of the most recently missed coin (for forgiveness)
        private int _missedCoinIdx = -1;
        // Altitude ceiling penalties for coin retry
        // Each entry: (startX, endX, ceilingY) � penalize paths above ceilingY in this X range
        private List<(int startX, int endX, int ceilingY)> _coinAltitudePenalties = new List<(int, int, int)>();
        // Force-walk zones for coin retry � forces WALK when grounded in this X range
        // to ensure pad activation (e.g., yellow pad ? coin launch, blue pad ? gravity flip)
        // Each entry: (startX, endX)
        private List<(int startX, int endX)> _coinForceWalkZones = new List<(int, int)>();
        // Ground-level bias zones for coin retry � strongly prefer WALK (stay at ground
        // level) but allow jumping when walking would die within 3 frames. This keeps
        // the player on the ground-level route while still clearing spikes/obstacles.
        // Each entry: (startX, endX)
        private List<(int startX, int endX)> _coinGroundBiasZones = new List<(int, int)>();
        // Mandatory coin indices for retry � coins that MUST be collected.
        // When a missed coin is in this set, forgiveness is denied and
        // crossing past the coin's X is treated as a permanent death.
        private HashSet<int> _retryMandatoryCoins = new HashSet<int>();
        // Ship coin aggressive target � when set (>= 0), the ship's tree search
        // allows larger survival differences to be overridden by coin steering.
        // Also used during coin retry to force the ship toward a specific Y.
        private int _shipCoinAggressiveThreshold = 3; // default: 3 frames tolerance
        // After a game mode transition, suppress coin-seeking for this many
        // frames so the ship/UFO can stabilize in the new corridor via pure
        // tree search before the PD controller starts pulling toward a coin.
        private int _modeTransitionStabilizeFrames = 0;
        // Coin input script: a pre-recorded input sequence from Strategy 0's
        // simulation. When the coin-walk override fires, we record ALL frames'
        // inputs (not just the first). On subsequent frames, we dequeue from
        // this script instead of running the BFS, ensuring the actual execution
        // matches the speculative simulation exactly.
        private Queue<bool> _coinInputScript = new Queue<bool>();
        private int _coinInputScriptCoinIdx = -1; // coin index this script targets
        private int _beamSearchAttemptedCoinIdx = -1; // coin index last beam-searched (avoid re-running)
        private int _beamSearchLastDistX = int.MaxValue; // distX of last beam-search attempt for re-try at closer range
        private int _cubeBeamSearchAttemptedCoinIdx = -1; // coin index last cube-beam-searched (avoid re-running)
        // Cross-corridor coins: coins behind a game-mode portal, pre-forgiven
        // at init so the ship PD doesn't steer toward them. Maps coin index
        // to the game mode active at the coin's position (for un-forgiving
        // when the pathfinder reaches that mode).
        private readonly Dictionary<int, int> _crossCorrForgivenCoins = new Dictionary<int, int>();

        private static bool IsCoinSprite(int sid) => SharedPhysics.IsCoinSprite(sid);
        private static bool IsMiniCoinSprite(int sid) => SharedPhysics.IsMiniCoinSprite(sid);
        private static bool IsDashOrb(int sid) => SharedPhysics.IsDashOrb(sid);
        private static bool IsGravityDashOrb(int sid) => SharedPhysics.IsGravityDashOrb(sid);
        private static int DashOrbMode(int sid) => SharedPhysics.DashOrbMode(sid);
        private static bool IsSpiderOrb(int sid) => SharedPhysics.IsSpiderOrb(sid);
        private static bool IsSpiderPad(int sid) => SharedPhysics.IsSpiderPad(sid);
        private static bool IsTeleportPortalEntrance(int sid) => SharedPhysics.IsTeleportPortalEntrance(sid);
        private static bool IsTeleportPortalExit(int sid) => SharedPhysics.IsTeleportPortalExit(sid);
        private static bool IsVerticalTeleportEntrance(int sid) => SharedPhysics.IsVerticalTeleportEntrance(sid);
        private static bool IsBottomRowTeleportExit(int sid) => SharedPhysics.IsBottomRowTeleportExit(sid);

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

        // -- Sprite geometry tables (delegate to SharedPhysics) ---------
        private static readonly int[] sprite_widths = SharedPhysics.sprite_widths;
        private static readonly int[] sprite_heights = SharedPhysics.sprite_heights;
        private static readonly int[] sprite_x_offset = SharedPhysics.sprite_x_offset;
        private static readonly int[] sprite_y_offset = SharedPhysics.sprite_y_offset;

        // -- Simulation state -----------------------------------------------
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
            public double GravityMod;           // gravity multiplier from portals/triggers (default 1.0)
            public bool WasZeroedByCollision;   // set by eject when landing
            public bool OnGround;               // separate from wasZeroed (cleared by jump)
            public int BallFlipCooldown;         // ball_switched flag (0=can flip, 1=switched)
            public int BallInputBuffer;          // remaining frames to try buffered flip (0 = inactive)
            public int BallCooldownFrames;       // 2-frame cooldown after flip: skip velocity zeroing and eject
            public int RobotJumpTime;            // remaining frames robot can hold jump (0 = not jumping, max 19)
            public int NinjaJumps;               // remaining air jumps for ninja mode (max 3, resets on ground)
            public SpriteSet ProcessedSprites;

            // Orb system: pending orb that overlaps the player (activation requires input)
            public int PendingOrbIndex;         // -1 = no pending orb
            public int PendingOrbSpriteId;      // sprite ID of pending orb

            // Dash state
            public int Dashing;                 // 0=none, 1=horiz, 2=45up, 3=45down, 4=up, 5=down
            public bool Orbed;                  // blocks spider teleport after orb hit
            public bool BlackOrbed;             // spider black orb hold-to-teleport
            public bool PrevInputHeld;          // previous frame input state for press-gated modes like robot
            public bool JBlocked;               // J block press-to-jump gate (cleared each frame after movement)
            public bool FBlocked;               // F block press-to-jump gate (cleared each frame after movement)
            public bool HBlocked;               // H block headbonk gate (ceiling ejection, cleared each frame after movement)
            public bool Dblocked;               // D block wave walkable gate (cleared each frame after movement)
            public bool Step2Ejected;            // Set when H/F_BLOCK Step 2 (opposite-dir eject) fired this frame; used to coarsen BFS dedup
            public bool Step2Ever;               // Sticky: set once Step 2 fires; never cleared. Descendants inherit via struct copy.

            // Slope counter state (matching NES was_on_slope_counter / slope_frames)
            public int SlopeWasOnCounter;       // frames since last slope contact (starts 3, decremented once/frame in eject)
            public int SlopeFrames;             // 1 on slope hit, decremented to 0 triggers apply_slope_vel
            public int SlopeType;               // last slope type (direction + degree bits)
            public bool SlopeJumpHigher;        // NES make_cube_jump_higher flag (set on slope + input held)
            public int LastSlopeType;            // previous slope type for rising→falling transition

            // BFS death diagnostics (set by StepFrame when returning false)
            public byte DeathType; // 0=none,1=CEIL_SPIKE,2=EJECT,3=CENTER,4=BALL_PROBE,5=BALL_VELZERO,6=BALL_EJECT,7=FLOOR_SPIKE,8=FWD,9=DEATH_COLL,10=BOUNDS,11=OOB_TOP

            // ---- Camera state for OOB death (matching NES x_movement Y bounds) ----
            public int CameraY_fixed;           // camera Y in fixed-point (8 frac bits)
            public int TargetCameraY_fixed;     // smooth-scroll target Y (ship/ball/UFO etc)
            public bool NoCamLockForced;        // true = freecam (camera follows player freely)
            public bool WrapMode;               // true = wrap Y instead of OOB death (0x8E on, 0x9E off)
            public int RainbowMaxMode;          // >0 = rainbow portal active: BFS must survive modes 0..RainbowMaxMode-1 (cleared on next game-mode portal)
            public SimState[]? RainbowShadows;   // persistent shadow states for rainbow multi-mode verification (one per alternative mode)

            // ---- Dual portal state ----
            public bool DualActive;             // true when in dual mode (two players)
            // Player 2 physics state (P1 uses the main fields above)
            public int P2_Y_fixed;
            public int P2_VelY_fixed;
            public bool P2_GravFlipped;
            public int P2_GravMul;
            public bool P2_Mini;
            public bool P2_WasZeroedByCollision;
            public bool P2_OnGround;
            public int P2_BallFlipCooldown;
            public int P2_BallInputBuffer;
            public int P2_BallCooldownFrames;
            public int P2_RobotJumpTime;
            public int P2_NinjaJumps;
            public int P2_SlopeWasOnCounter;
            public int P2_SlopeFrames;
            public int P2_SlopeType;
            #pragma warning disable CS0649
            public int P2_LastSlopeType;
            #pragma warning restore CS0649
            public bool P2_Orbed;
            public bool P2_BlackOrbed;
            public bool P2_PrevInputHeld;
            public int P2_Dashing;
            public bool P2_JBlocked;
            public bool P2_FBlocked;
            public bool P2_HBlocked;
            public bool P2_Dblocked;
            public int P2_PendingOrbIndex;
            public int P2_PendingOrbSpriteId;

            public SimState Clone()
            {
                var c = this;
                c.ProcessedSprites = ProcessedSprites.Clone();
                if (RainbowShadows != null)
                {
                    c.RainbowShadows = new SimState[RainbowShadows.Length];
                    for (int i = 0; i < RainbowShadows.Length; i++)
                    {
                        c.RainbowShadows[i] = RainbowShadows[i];
                        c.RainbowShadows[i].ProcessedSprites = RainbowShadows[i].ProcessedSprites.Clone();
                    }
                }
                return c;
            }

            public void ReturnAllSpriteResources()
            {
                ProcessedSprites.Return();
                if (RainbowShadows != null)
                {
                    for (int i = 0; i < RainbowShadows.Length; i++)
                        RainbowShadows[i].ProcessedSprites.Return();
                    RainbowShadows = null;
                }
            }
        }

        // -- Bitmask-based sprite set (replaces HashSet<int> for fast cloning) --
        // Clone rents from ArrayPool to minimize GC pressure during ship tree
        // search (32K clones/frame). Only the logical portion is used; extra
        // rented elements are cleared to zero for correct hash/count.
        private sealed class SpriteSet
        {
            internal readonly ulong[] Bits;
            private readonly int _logicalLen; // actual number of used elements
            private readonly int[] _map; // tile-index ? compact 0..N-1 (or -1)
            private readonly bool _rented; // true if Bits came from ArrayPool

            public SpriteSet(int[] compactMap, int compactCount)
            {
                _map = compactMap;
                _logicalLen = Math.Max(1, (compactCount + 63) >> 6);
                Bits = new ulong[_logicalLen];
                _rented = false;
            }

            private SpriteSet(ulong[] srcBits, int logicalLen, int[] compactMap)
            {
                _map = compactMap;
                _logicalLen = logicalLen;
                Bits = System.Buffers.ArrayPool<ulong>.Shared.Rent(logicalLen);
                _rented = true;
                Buffer.BlockCopy(srcBits, 0, Bits, 0, logicalLen * 8);
                // Clear extra rented elements so hash/count stay correct
                if (Bits.Length > logicalLen)
                    Array.Clear(Bits, logicalLen, Bits.Length - logicalLen);
            }

            public void Add(int tileIndex)
            {
                int c = _map[tileIndex];
                Bits[c >> 6] |= 1UL << (c & 63);
            }

            public bool Contains(int tileIndex)
            {
                if ((uint)tileIndex >= (uint)_map.Length) return false;
                int c = _map[tileIndex];
                if (c < 0) return false;
                return (Bits[c >> 6] & (1UL << (c & 63))) != 0;
            }

            public void Remove(int tileIndex)
            {
                int c = _map[tileIndex];
                if (c >= 0) Bits[c >> 6] &= ~(1UL << (c & 63));
            }

            public SpriteSet Clone() => new SpriteSet(Bits, _logicalLen, _map);

            /// <summary>Return the rented array to the pool. Call when this SpriteSet will not be used again.</summary>
            public void Return()
            {
                if (_rented)
                    System.Buffers.ArrayPool<ulong>.Shared.Return(Bits);
            }

            public int GetBitsHash()
            {
                int h = 0;
                for (int i = 0; i < _logicalLen; i++)
                {
                    ulong v = Bits[i];
                    h = h * 397 ^ (int)v ^ (int)(v >> 32);
                }
                return h;
            }

            public int Count
            {
                get
                {
                    int n = 0;
                    for (int i = 0; i < _logicalLen; i++)
                        n += System.Numerics.BitOperations.PopCount(Bits[i]);
                    return n;
                }
            }
        }

        private int[] _spriteCompactMap;  // tile-index ? compact index (or -1)
        private int _spriteCompactCount;

        private SpriteSet NewSpriteSet() => new SpriteSet(_spriteCompactMap, _spriteCompactCount);

        // -- Output ---------------------------------------------------------
        public List<(int x, int y)> PathPoints { get; private set; } = null!;
        public List<(int x, int y)> Path2Points { get; private set; } = null!;  // Player 2 path (dual mode)
        public List<bool> Inputs { get; private set; } = null!;
        public bool Success { get; private set; }
        public string ResultMessage { get; private set; } = "";

        /// <summary>
        /// All paths attempted during backtracking (failed attempts).
        /// Each entry is a snapshot of the path segment that was explored
        /// before the backtracker rewound to try a different decision.
        /// </summary>
        public List<List<(int x, int y)>> AttemptedPaths { get; private set; } = new();

        // -- Constructor ----------------------------------------------------
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

            // Build collision map for SharedPhysics tile access
            _collisionMap = new SharedPhysics.CollisionMap(this.tiles, this.mapWidth, this.mapHeight, this.groundRowsToReserve);

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

                // Storage position (instance's own tile) � used for hitbox placement
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

            // Sort sprites by X, with a tiebreaker that places gravity portals
            // before orbs at the same X.  The SIM processes gravity portals
            // (CheckGravityPortals) before orbs (UpdateOrbSystem) in separate
            // passes, so gravity is always flipped before orb collision is
            // tested.  Without the tiebreak, unstable sort can place an orb
            // before its co-located gravity portal, causing the mini-ship
            // centering offset to use the pre-flip state and miss the orb.
            allSprites.Sort((a, b) =>
            {
                int cmp = a.AnchorX_px.CompareTo(b.AnchorX_px);
                if (cmp != 0) return cmp;
                // Gravity portals sort first (priority 0), everything else after (1)
                int pa = IsGravityPortal(a.SpriteId) ? 0 : 1;
                int pb = IsGravityPortal(b.SpriteId) ? 0 : 1;
                return pa.CompareTo(pb);
            });
            _spritesArr = allSprites.ToArray();

            // Build mode portal position list (reserved for future use)
            _modePortalPositions = new List<(int X, int Y)>();

            // Build sorted coin list for miss detection and coin-aware pathfinding
            allCoins = allSprites.Where(sp => IsCoinSprite(sp.SpriteId) || IsMiniCoinSprite(sp.SpriteId)).ToList();

            // Build compact sprite index mapping for bitmask-based SpriteSet
            _spriteCompactMap = new int[this.sprites.Length];
            Array.Fill(_spriteCompactMap, -1);
            _spriteCompactCount = 0;
            foreach (var sp in allSprites)
                _spriteCompactMap[sp.Index] = _spriteCompactCount++;

            PathPoints = new List<(int, int)>();
            Path2Points = new List<(int, int)>();
            Inputs = new List<bool>();

            // Diagnostic: dump config hash so pf-test vs editor divergences are detectable
            {
                int tileHash = 0;
                for (int i = 0; i < this.tiles.Length; i++) tileHash = tileHash * 31 + this.tiles[i];
                int sprHash = 0;
                for (int i = 0; i < this.sprites.Length; i++) sprHash = sprHash * 31 + this.sprites[i];
                int offHash = 0;
                foreach (var kv in this.spritePixelOffsets.OrderBy(x => x.Key))
                    offHash = offHash * 31 + kv.Key + kv.Value.offsetX * 7 + kv.Value.offsetY * 13;
                int anchHash = 0;
                foreach (var kv in this.spriteAnchors.OrderBy(x => x.Key))
                    anchHash = anchHash * 31 + kv.Key + kv.Value.anchorTileX * 7 + kv.Value.anchorTileY * 13;
                var diagMsg = $"[PF_DIAG] tiles={this.tiles.Length} tileHash=0x{tileHash:X8} sprites={this.sprites.Length} sprHash=0x{sprHash:X8} offsets={this.spritePixelOffsets.Count} offHash=0x{offHash:X8} anchors={this.spriteAnchors.Count} anchHash=0x{anchHash:X8} w={mapWidth} h={mapHeight} ground={groundRowsToReserve} maxFall=0x{this.maxFallSpeed:X} spriteEntries={allSprites.Count}";
                _log.WriteLine(diagMsg);
                _log.Flush();
                // Dump collision map at death zone (cols 385-420, rows 15-25)
                #pragma warning disable CS8602
                if (mapWidth > 400)
                {
                    for (int dumpR = 15; dumpR <= 25; dumpR++)
                    {
                        var sb = new System.Text.StringBuilder();
                        sb.Append($"[TILE_MAP] row={dumpR} Y={dumpR*16}:");
                        for (int dumpC = 385; dumpC <= 435; dumpC++)
                        {
                            int dumpArrY = dumpR + groundRowsToReserve;
                            if (dumpArrY >= 0 && dumpArrY < mapHeight && dumpC >= 0 && dumpC < mapWidth)
                            {
                                int dumpTid = tiles[dumpArrY * mapWidth + dumpC];
                                int dumpMapped = SharedPhysics.MapTileForCollision(dumpTid);
                                var dumpCol = MetatileCollisionTable.GetCollision((byte)dumpMapped);
                                sb.Append($" c{dumpC}=0x{dumpTid:X2}({dumpCol})");
                            }
                            else sb.Append($" c{dumpC}=OOB");
                        }
                        Console.Error.WriteLine(sb.ToString());
                    }
                    Console.Error.Flush();
                }
                #pragma warning restore CS8602
                // Dump all game-mode portals in allSprites
                {
                    int portalCount = 0;
                    foreach (var sp in allSprites)
                    {
                        if (IsGameModePortal(sp.SpriteId))
                        {
                            int targetMode = SharedPhysics.SpriteIdToGameMode(sp.SpriteId);
                            Console.Error.WriteLine($"[MODE_PORTAL] idx={sp.Index} sid=0x{sp.SpriteId:X2} mode={targetMode} col={sp.AnchorX_px/16} hitBox=({sp.HitLeft},{sp.HitTop})-({sp.HitRight},{sp.HitBottom})");
                            portalCount++;
                        }
                    }
                    Console.Error.WriteLine($"[MODE_PORTAL] total={portalCount}");
                    Console.Error.Flush();
                }
                // Dump all orbs/pads/gravity portals in death zone (cols 385-430)
                {
                    foreach (var sp in allSprites)
                    {
                        int col = sp.AnchorX_px / 16;
                        if (col < 385 || col > 430) continue;
                        int sid = sp.SpriteId;
                        bool isOrb = IsOrbSprite(sid) || IsDashOrb(sid);
                        bool isPad = IsAnyPad(sid);
                        bool isGrav = IsGravityPortal(sid);
                        if (isOrb || isPad || isGrav)
                        {
                            string kind = isOrb ? "ORB" : isPad ? "PAD" : "GRAV";
                            Console.Error.WriteLine($"[DZ_SPRITE] {kind} idx={sp.Index} sid=0x{sid:X2} col={col} hitBox=({sp.HitLeft},{sp.HitTop})-({sp.HitRight},{sp.HitBottom})");
                        }
                    }
                    Console.Error.Flush();
                }
#if !DISABLE_DEBUG_LOGGING
                try { System.IO.File.AppendAllText(pfDebugLogPath, diagMsg + System.Environment.NewLine); } catch { }
#endif
            }
        }

        // -------------------------------------------------------------------
        //  PUBLIC API
        // -------------------------------------------------------------------

        public void Run(int startX_px, int startY_px, int startSpeedUiIndex, int startGameMode,
                        bool startGravFlipped, bool startMini)
        {
            _log = Verbose ? Console.Error : System.IO.TextWriter.Null;
            for (int ci = 0; ci < allCoins.Count; ci++)
                _log.WriteLine($"[COIN_INFO] coin#{ci} idx={allCoins[ci].Index} sid=0x{allCoins[ci].SpriteId:X2} pos=({allCoins[ci].AnchorX_px},{allCoins[ci].AnchorY_px}) hit=({allCoins[ci].HitLeft},{allCoins[ci].HitTop})-({allCoins[ci].HitRight},{allCoins[ci].HitBottom})");
            var sw = System.Diagnostics.Stopwatch.StartNew();
            double originalBias = JumpTimingBias;
            _autoForgivenCoins.Clear();

            // -- Pre-apply coin zones on the FIRST run --
            // Analyze all coins and nearby pads to set up force-walk and
            // altitude-penalty zones BEFORE the first attempt. This way
            // the pathfinder collects coins on the first run instead of
            // requiring an expensive second-pass retry.
            if (false && PreferCoins && allCoins.Count > 0) // TEMPORARILY DISABLED
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
                        if (padCX > cx + 32 || cx - padCX > 200) continue;
                        int padCY = (sp.HitTop + sp.HitBottom) / 2;
                        if (padCY < groundY_pre - 40) continue;
                        bool isGP = (padCY >= groundY_pre - 30);
                        if (isGP && !IsBluePad(sp.SpriteId))
                            preForceWalk.Add((sp.HitLeft - 16, sp.HitRight + 16));
                        if (isGP)
                            preAltPenalties.Add((sp.HitLeft - 150, sp.HitLeft, groundY_pre - 20));
                    }
                }
                if (preForceWalk.Count > 0 || preAltPenalties.Count > 0)
                {
                    _log.WriteLine($"[COIN_PRE_ZONES] forceWalk={preForceWalk.Count} altPenalty={preAltPenalties.Count} � applying on first run");
                    foreach (var fw in preForceWalk)
                        _log.WriteLine($"  forceWalk: X=[{fw.startX},{fw.endX}]");
                    foreach (var ap in preAltPenalties)
                        _log.WriteLine($"  altPenalty: X=[{ap.startX},{ap.endX}] ceilY={ap.ceilingY}");
                    _coinForceWalkZones = preForceWalk;
                    _coinAltitudePenalties = preAltPenalties;
                }
            }

            // -- BFS FIRST --
            // Always try BFS first � it's fast (a few seconds) and handles
            // complex gravity/portal sections that the greedy heuristic can't
            // navigate. If BFS succeeds or UseBFS is set, we're done.
            // If BFS fails, fall back to the heuristic only if BFS made
            // very little progress (< 5%) � otherwise the heuristic is
            // unlikely to do better and takes a very long time.
            RunBFS(startX_px, startY_px, startSpeedUiIndex,
                   startGameMode, startGravFlipped, startMini);

            // If BFS failed and bias is non-neutral, retry BFS with neutral bias.
            // Non-neutral bias in BfsScore can prune altitude-diverse states from
            // the frontier, preventing BFS from discovering viable paths.
            if (!Success && Math.Abs(JumpTimingBias - 0.5) >= 0.05)
            {
                _log.WriteLine($"[BFS_RETRY] BFS failed with bias={JumpTimingBias:F2}, retrying with neutral bias...");
                double savedBias = JumpTimingBias;
                JumpTimingBias = 0.5;
                RunBFS(startX_px, startY_px, startSpeedUiIndex,
                       startGameMode, startGravFlipped, startMini);
                JumpTimingBias = savedBias;
            }

            if (Success || UseBFS)
            {
                // BFS completed the level, or explicit BFS-only mode � done
                sw.Stop();
                if (!string.IsNullOrEmpty(ResultMessage))
                    ResultMessage += $" [{sw.Elapsed.TotalSeconds:F1}s]";
                return;
            }

            // BFS failed � save its partial result
            var bfsInputs = Inputs != null ? new List<bool>(Inputs) : null;
            var bfsPath = PathPoints != null ? new List<(int x, int y)>(PathPoints) : null;
            string bfsMsg = ResultMessage;
            int bfsBestX = PathPoints != null && PathPoints.Count > 0 ? PathPoints[PathPoints.Count - 1].x : 0;
            int bfsLevelLen = mapWidth * TILE;
            int bfsPct = bfsLevelLen > 0 ? bfsBestX * 100 / bfsLevelLen : 0;

            // Always fall back to heuristic when BFS didn't complete the level.
            // The heuristic's backtracking can navigate sections that BFS's
            // frontier pruning misses, regardless of BFS progress percentage.
            {
                _log.WriteLine($"[BFS?HEURISTIC] BFS reached {bfsPct}%, trying heuristic...");
                RunSingleAttempt(startX_px, startY_px, startSpeedUiIndex,
                                 startGameMode, startGravFlipped, startMini);

                // Keep whichever result went further
                if (!Success)
                {
                    int heuristicBestX = PathPoints != null && PathPoints.Count > 0 ? PathPoints[PathPoints.Count - 1].x : 0;
                    if (bfsBestX > heuristicBestX)
                    {
                        Inputs = bfsInputs!;
                        PathPoints = bfsPath!;
                        ResultMessage = bfsMsg;
                        _log.WriteLine($"[BFS?HEURISTIC] Heuristic worse ({heuristicBestX}px vs BFS {bfsBestX}px), keeping BFS result");
                    }
                }
            }

            // Clear pre-applied zones after first run
            _coinForceWalkZones.Clear();
            _coinAltitudePenalties.Clear();

            // -- Coin retry with mandatory coins + ground-bias zones --
            // If the first run missed coins despite pre-applied zones,
            // retry with focused zones. Usually the first run collects
            // all coins and this section is skipped entirely.
            if (Success && PreferCoins && _forgivenCoins.Count > 0 && allCoins.Count > 0)
            {
                int groundY = (mapHeight - groundRowsToReserve) * 16 - 15;
                // Snapshot the current best result
                var overallBestInputs = new List<bool>(Inputs!);
                var overallBestPath = new List<(int x, int y)>(PathPoints!);
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
                            if (padCX > cx + 32 || cx - padCX > 200) continue;
                            int padCY = (sp.HitTop + sp.HitBottom) / 2;
                            if (padCY < groundY - 40) continue;
                            anyPads = true;
                            bool isGP = (padCY >= groundY - 30);
                            if (isGP && !IsBluePad(sp.SpriteId))
                                combinedForceWalk.Add((sp.HitLeft - 100, sp.HitRight + 16));
                            if (isGP)
                                combinedAltPenalties.Add((sp.HitLeft - 150, sp.HitLeft, groundY - 20));
                        }
                    }
                    if (anyPads)
                    {
                        _log.WriteLine($"[COIN_RETRY_COMBINED] Attempting {cubeForgiven.Count} cube-mode forgiven coins, forceWalk={combinedForceWalk.Count} altPenalty={combinedAltPenalties.Count}");
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
                            _log.WriteLine($"[COIN_RETRY_COMBINED] Result: {retryCoins}/{allCoins.Count} coins");
                            if (retryCoins > overallBestCoins)
                            {
                                _log.WriteLine($"[COIN_RETRY_COMBINED] Improved: {retryCoins} vs {overallBestCoins}");
                                overallBestInputs = new List<bool>(Inputs!);
                                overallBestPath = new List<(int x, int y)>(PathPoints!);
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

                    // Skip ship/UFO-mode coins � handled by ship retry below.
                    // Ball coins (gameMode=2) are allowed: force-walk zones are
                    // harmless in ball mode, and the bias change can help.
                    if (_forgivenCoinGameModes.TryGetValue(coin.Index, out int coinGm) && (coinGm == 1 || coinGm == 3))
                    {
                        _log.WriteLine($"[COIN_RETRY_SKIP] coin idx={coin.Index} sid=0x{coin.SpriteId:X2} gameMode={coinGm} � skip non-cube retry");
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
                        if (coinCenterX - padCenterX > 200) continue;
                        int padCenterY = (sp.HitTop + sp.HitBottom) / 2;
                        if (padCenterY < groundY - 40) continue;
                        hasPads = true;
                        _log.WriteLine($"[COIN_RETRY_PAD] coin={coin.Index} pad idx={sp.Index} sid=0x{sp.SpriteId:X2} hit=({sp.HitLeft},{sp.HitTop})-({sp.HitRight},{sp.HitBottom})");
                        // Force-walk zone: only for pads AT ground level (within 30px
                        // of groundY). Elevated pads shouldn't have walk forcing.
                        // Blue pads are EXCLUDED: players typically approach blue pads
                        // from staircases and need the freedom to jump off them.
                        // Force-walking on the staircase can cause CENTER_DEATH on
                        // hazard tiles adjacent to the staircase edge.
                        bool isGroundPad = (padCenterY >= groundY - 30);
                        if (isGroundPad && !IsBluePad(sp.SpriteId))
                        {
                            thisCoinForceWalk.Add((sp.HitLeft - 100, sp.HitRight + 16));
                        }
                        // Altitude penalty zone: only for ground-level pads.
                        // Gently bias BFS toward ground level in the approach area
                        // before the pad. Uses ceiling = groundY - 20 so penalty
                        // applies when player is above ground. ENDS at the pad start
                        // (HitLeft) � MUST NOT extend past the pad, because post-pad
                        // the player is launched upward and needs free BFS navigation.
                        if (isGroundPad)
                        {
                            int ceilingY = groundY - 20; // 349 for groundY=369
                            thisCoinAltPenalties.Add((sp.HitLeft - 150, sp.HitLeft, ceilingY));
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
                        _log.WriteLine($"[COIN_RETRY] Attempting coin idx={coin.Index} sid=0x{coin.SpriteId:X2} bias={retryBias:F2} forceWalk={thisCoinForceWalk.Count} altPenalty={thisCoinAltPenalties.Count}");

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
                            _log.WriteLine($"[COIN_RETRY] Retry result: {retryCoins}/{allCoins.Count} coins");
                            if (retryCoins > overallBestCoins)
                            {
                                _log.WriteLine($"[COIN_RETRY] Improved: {retryCoins} vs {overallBestCoins}");
                                overallBestInputs = new List<bool>(Inputs!);
                                overallBestPath = new List<(int x, int y)>(PathPoints!);
                                overallBestMsg = ResultMessage;
                                overallBestCoins = retryCoins;
                                overallBestFinalCoins = FinalCollectedCoinIndices != null
                                    ? new HashSet<int>(FinalCollectedCoinIndices) : null;
                                break; // this coin is improved, move to next coin
                            }
                        }
                        else
                        {
                            _log.WriteLine($"[COIN_RETRY] Retry for coin {coin.Index} bias={retryBias:F2} FAILED");
                            Success = true; // keep going
                        }
                    }

                    JumpTimingBias = originalBias;
                    _coinForceWalkZones.Clear();
                    _coinAltitudePenalties.Clear();
                    _coinGroundBiasZones.Clear();
                    _retryMandatoryCoins.Clear();
                }

                // === SHIP-MODE COIN RETRY ===
                // Ship/ball/UFO coins can't use force-walk zones, but we can
                // retry with a much higher aggressive threshold so the beam
                // search and PD tiebreaker steer harder toward the coin.
                // This re-run gives the improved beam search (wider range,
                // re-attempts at closer distances) a fresh start.
                // Try multiple biases to maximize chances of finding a path
                // that can collect the ship-mode coin.
                var shipForgiven = forgivenCoinList.Where(c =>
                    _forgivenCoinGameModes.TryGetValue(c.Index, out int gm) && gm == 1).ToList();
                // Skip ship retry if all ship-forgiven coins were MISSED-type
                // auto-forgiven (proven unreachable). Don't skip for coins that
                // were collect-lose forgiven � they ARE reachable but need a
                // different trajectory to survive after collection.
                bool allShipAutoForgiven = shipForgiven.All(c => {
                    // Collect-lose coins are reachable � always retry them
                    if (_coinCollectThenLoseCount.TryGetValue(c.Index, out int lc) && lc > 0)
                        return false;
                    if (_autoForgivenCoins.Contains(c.Index)) return true;
                    _coinMissRetryCount.TryGetValue(c.Index, out int mc);
                    return mc >= 3;
                });
                if (shipForgiven.Count > 0 && allShipAutoForgiven)
                    _log.WriteLine($"[SHIP_RETRY_SKIP] All {shipForgiven.Count} ship coins have high miss counts � skipping ship retry");
                if (shipForgiven.Count > 0 && !allShipAutoForgiven && overallBestCoins < allCoins.Count)
                {
                    double[] shipRetryBiases = new[] { originalBias, 0.3 };
                    foreach (double shipBias in shipRetryBiases)
                    {
                        if (overallBestCoins >= allCoins.Count) break;
                        _log.WriteLine($"[COIN_RETRY_SHIP] Attempting {shipForgiven.Count} ship-mode forgiven coins bias={shipBias:F2} with aggressive threshold");
                        JumpTimingBias = shipBias;
                        _coinForceWalkZones.Clear();
                        _coinAltitudePenalties.Clear();
                        _coinGroundBiasZones.Clear();
                        _retryMandatoryCoins.Clear();
                        // Boost aggressive threshold so the beam search and PD
                        // tiebreaker steer harder toward the coin on re-runs.
                        _shipCoinAggressiveThreshold = 20;
                        RunSingleAttempt(startX_px, startY_px, startSpeedUiIndex,
                                         startGameMode, startGravFlipped, startMini);
                        if (Success)
                        {
                            int retryCoins = FinalCollectedCoinIndices != null ? FinalCollectedCoinIndices.Count : 0;
                            _log.WriteLine($"[COIN_RETRY_SHIP] Result: {retryCoins}/{allCoins.Count} coins (bias={shipBias:F2})");
                            if (retryCoins > overallBestCoins)
                            {
                                _log.WriteLine($"[COIN_RETRY_SHIP] Improved: {retryCoins} vs {overallBestCoins}");
                                overallBestInputs = new List<bool>(Inputs!);
                                overallBestPath = new List<(int x, int y)>(PathPoints!);
                                overallBestMsg = ResultMessage;
                                overallBestCoins = retryCoins;
                                overallBestFinalCoins = FinalCollectedCoinIndices != null
                                    ? new HashSet<int>(FinalCollectedCoinIndices) : null;
                                break; // this bias worked, stop trying
                            }
                        }
                        else
                        {
                            _log.WriteLine($"[COIN_RETRY_SHIP] Retry bias={shipBias:F2} FAILED");
                            Success = true; // keep going
                        }
                    }
                    JumpTimingBias = originalBias;
                }

                // === BALL-MODE COIN RETRY ===
                // Ball coins can't use force-walk zones (cube-specific), but
                // different biases change ball flip timings throughout the level,
                // potentially routing the ball to a different surface/corridor
                // where the coin is reachable. Also re-routes the ship section,
                // potentially keeping coins in ship mode that shifted to ball.
                var ballForgiven = forgivenCoinList.Where(c =>
                    _forgivenCoinGameModes.TryGetValue(c.Index, out int gm) && gm == 2).ToList();
                if (ballForgiven.Count > 0 && overallBestCoins < allCoins.Count)
                {
                    double[] ballRetryBiases = new[] { 0.0, 1.0 };
                    foreach (double ballBias in ballRetryBiases)
                    {
                        if (overallBestCoins >= allCoins.Count) break;
                        if (Math.Abs(ballBias - originalBias) < 0.05) continue;
                        _log.WriteLine($"[COIN_RETRY_BALL] Attempting {ballForgiven.Count} ball-mode forgiven coins bias={ballBias:F2}");
                        JumpTimingBias = ballBias;
                        _coinForceWalkZones.Clear();
                        _coinAltitudePenalties.Clear();
                        _coinGroundBiasZones.Clear();
                        _retryMandatoryCoins.Clear();
                        RunSingleAttempt(startX_px, startY_px, startSpeedUiIndex,
                                         startGameMode, startGravFlipped, startMini);
                        if (Success)
                        {
                            int retryCoins = FinalCollectedCoinIndices != null ? FinalCollectedCoinIndices.Count : 0;
                            _log.WriteLine($"[COIN_RETRY_BALL] Result: {retryCoins}/{allCoins.Count} coins (bias={ballBias:F2})");
                            if (retryCoins > overallBestCoins)
                            {
                                _log.WriteLine($"[COIN_RETRY_BALL] Improved: {retryCoins} vs {overallBestCoins}");
                                overallBestInputs = new List<bool>(Inputs!);
                                overallBestPath = new List<(int x, int y)>(PathPoints!);
                                overallBestMsg = ResultMessage;
                                overallBestCoins = retryCoins;
                                overallBestFinalCoins = FinalCollectedCoinIndices != null
                                    ? new HashSet<int>(FinalCollectedCoinIndices) : null;
                                break;
                            }
                        }
                        else
                        {
                            _log.WriteLine($"[COIN_RETRY_BALL] Retry bias={ballBias:F2} FAILED");
                            Success = true;
                        }
                    }
                    JumpTimingBias = originalBias;
                }

                // Restore overall best result
                Inputs = overallBestInputs;
                PathPoints = overallBestPath;
                FinalCollectedCoinIndices = overallBestFinalCoins;

                // Rebuild result message with accurate coin counts �
                // the stored message may contain stale "unreachable" text
                // from a run where more coins were forgiven than are
                // actually missing from the best result.
                int actualCollected = overallBestFinalCoins != null ? overallBestFinalCoins.Count : 0;
                int actualMissing = allCoins.Count - actualCollected;
                // Extract frame/path info from original message prefix
                int bracketIdx = overallBestMsg.IndexOf('[');
                string msgPrefix = bracketIdx > 0 ? overallBestMsg.Substring(0, bracketIdx).TrimEnd() : overallBestMsg;
                ResultMessage = $"{msgPrefix} [{actualCollected}/{allCoins.Count} coins]";
                if (actualMissing > 0) ResultMessage += $" ({actualMissing} unreachable)";
            }

            // If heuristic also failed, retry with intermediate biases as last resort.
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
                    PfLog($"[RETRY] BFS + primary bias failed, retrying with bias {fb:F2}");
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
        /// Used for click optimization � verify modified inputs still work.
        /// </summary>
        private bool ReplayInputs(List<bool> inputs, int startX_px, int startY_px,
            int startSpeedUiIndex, int startGameMode, bool startGravFlipped, bool startMini,
            out List<(int x, int y)> pathPoints)
        {
            pathPoints = new List<(int x, int y)>();
            int initCamY_ri = ComputeInitCameraY(startY_px);
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
                GravityMod = 1.0,
                WasZeroedByCollision = true,
                OnGround = true,
                ProcessedSprites = NewSpriteSet(),
                PendingOrbIndex = -1,
                PendingOrbSpriteId = -1,
                NinjaJumps = (startGameMode == 8) ? NINJA_MAX_JUMPS : 0,
                CameraY_fixed = initCamY_ri,
                TargetCameraY_fixed = initCamY_ri
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

            if (mode == 1) // Least clicks � remove unnecessary presses
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
                            candidate[i] = true; // restore � this press was needed
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
                    ResultMessage += $" [optimized: {originalClicks}?{newClicks} clicks]";
                }
            }
            else if (mode == 2) // Most clicks � add presses where safe
            {
                var candidate = new List<bool>(originalInputs);
                for (int i = 0; i < candidate.Count; i++)
                {
                    if (candidate[i]) continue; // already pressing
                    candidate[i] = true;
                    if (!ReplayInputs(candidate, startX_px, startY_px, startSpeedUiIndex,
                        startGameMode, startGravFlipped, startMini, out _))
                    {
                        candidate[i] = false; // revert � this press broke the path
                    }
                }
                int newClicks = CountClicks(candidate);
                if (newClicks > originalClicks)
                {
                    Inputs = candidate;
                    ReplayInputs(Inputs, startX_px, startY_px, startSpeedUiIndex,
                        startGameMode, startGravFlipped, startMini, out var finalPath);
                    PathPoints = finalPath;
                    ResultMessage += $" [swag: {originalClicks}?{newClicks} clicks]";
                }
            }
            else if (mode == 3) // Just the clicks needed � remove unnecessary, keep essential
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
                ResultMessage += $" [essential: {originalClicks}?{newClicks} clicks]";
            }
        }

        /// <summary>Count the number of press-on transitions (false?true).</summary>
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
            int initCameraY = ComputeInitCameraY(startY_px);
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
                GravityMod = 1.0,
                WasZeroedByCollision = true,
                OnGround = true,
                ProcessedSprites = NewSpriteSet(),
                PendingOrbIndex = -1,
                PendingOrbSpriteId = -1,
                NinjaJumps = (startGameMode == 8) ? NINJA_MAX_JUMPS : 0,
                CameraY_fixed = initCameraY,
                TargetCameraY_fixed = initCameraY
            };

            ApplyPortalsUpTo(ref state, startX_px);

            PathPoints.Clear();
            Path2Points.Clear();
            Inputs.Clear();
            _prevDualActiveForPath = false;
            _cubeHoldJump = false;
            _cubeHoldDelay = 0;
            _committedJumpDelay = -1;
            _committedRobotHold = 0;
            _committedNinjaJumps.Clear();
            _ninjaWaitFrames = 0;
            _backtrackCheckpoints = new List<BacktrackCheckpoint>();
            _lastCubeToShipCheckpoint = null;
            _shipEntryRecoveryCheckpoint = null;
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
            _shipCoinAggressiveThreshold = 3; // gentle PD; beam search handles coin collection
            _modeTransitionStabilizeFrames = 0;
            _shipForceReleaseFirstFrames = 0;
            _shipForceHoldFrames = 0;
            _shipForceReleaseFrames = 0;
            _shipCommitFrames = 0;
            _shipCommitHold = false;
            _cubeGroundedWalkFrames = 0;
            _ballGroundedWalkFrames = 0;
            _bestPathHighWaterX = startX_px;
            _bestPathPoints.Clear();
            _bestPath2Points.Clear();
            _bestInputs.Clear();
            _nextCoinCheckIdx = 0;
            _forgivenCoins.Clear();
            _forgivenCoinGameModes.Clear();
            _coinMissRetryCount.Clear();
            _coinCollectThenLoseCount.Clear();
            _permanentlyCollectedCoins.Clear();
            _missedCoinIdx = -1;
            _coinInputScript.Clear();
            _coinInputScriptCoinIdx = -1;
            _beamSearchAttemptedCoinIdx = -1;
            _beamSearchLastDistX = int.MaxValue;
            _cubeBeamSearchAttemptedCoinIdx = -1;
            _crossCorrForgivenCoins.Clear();
            PreForgiveCrossCorridorCoins(startGameMode);

            _frameCounter = 0;
            _speculativeDepth = 0;
            TraceFrameOpen();
#if !DISABLE_DEBUG_LOGGING
            PfLog($"[LEVEL] {LevelName}");
            PfLog($"[RUN_START] startX={startX_px} startY={startY_px} speed={startSpeedUiIndex} mode={startGameMode} gravFlipped={startGravFlipped} mini={startMini}");
            PfLog($"[RUN_STATE] X_fixed=0x{state.X_fixed:X4} Y_fixed=0x{state.Y_fixed:X4} VelX=0x{state.VelX_fixed:X4} VelY=0x{state.VelY_fixed:X4} gravMul={state.GravMul} gravMod={state.GravityMod:F3} onGround={state.OnGround}");
            PfLog($"[RUN_MAP] mapWidth={mapWidth} mapHeight={mapHeight} groundRowsToReserve={groundRowsToReserve} maxFallSpeed=0x{maxFallSpeed:X4} sprites={allSprites.Count}");
            foreach (var sp in allSprites)
            {
                int sid = sp.SpriteId;
                if (IsGravityPortal(sid) || IsGameModePortal(sid) || IsEndLevel(sid))
                {
                    PfLog($"[PORTAL_INV] sid=0x{sid:X2} idx={sp.Index} anchorX={sp.AnchorX_px} box=({sp.HitLeft},{sp.HitTop})-({sp.HitRight},{sp.HitBottom})");
                }
            }
#endif

            // -- DIAGNOSTIC: collision map for the stuck region -- (disabled for perf)
            // {
            //     ... collision map diagnostic removed ...
            // }

            // Report progress based on X position relative to level length
            int levelLengthPx = mapWidth * TILE;
            int highWaterX_px = startX_px; // track furthest X reached for progress
            int progressInterval = Math.Max(1, MAX_FRAMES / 200); // check every 0.5% of frame budget
            int totalIterations = 0; // counts every frame step including replays

            // Coin fallback state � persists across death iterations so the coin
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
                    ResultMessage = $"Cancelled � partial path to X={bestX}px ({bestPct}%, {PathPoints.Count} points)";
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
                    ResultMessage = $"Aborted � partial path to X={bestXA}px ({bestPctA}%) exceeded {MAX_TOTAL_ITERATIONS} iterations";
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
                PfLog($"[STEP_START] playerX_fixed=0x{state.X_fixed:X4} ({state.X_fixed >> 8}px), playerY_fixed=0x{state.Y_fixed:X4} ({state.Y_fixed >> 8}px), playerVelY_fixed=0x{state.VelY_fixed:X4}, mode={state.GameMode}, gravFlipped={state.GravFlipped}, onGround={state.OnGround}, wasZeroed={state.WasZeroedByCollision}, mini={state.Mini}");
#endif

                // Snapshot pre-decision state for potential checkpoint
                bool wasGrounded = state.VelY_fixed == 0 && state.OnGround;
                // Also detect "predicted landing" � cube is airborne but will
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
                SimState preDecisionState = default;
                bool hasPreDecision = (wasGrounded || isShipMode || willLand);
                if (hasPreDecision) preDecisionState = state.Clone();
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
                    // Each death point gets a fresh budget � solving early
                    // obstacles shouldn't deplete the budget for harder
                    // sections ahead.
                    _backtrackAttempts = 0;
                    _backtrackTimer = null; // reset timer for next death point
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[BACKTRACK_DONE] advanced past death frame {_btDeathFrame}, resuming normal checkpointing (attempts={_backtrackAttempts}/{MAX_BACKTRACK_ATTEMPTS}) coinRetry={_missedCoinIdx >= 0}");
#endif
                }

                // Save checkpoint at grounded decisions where the cube jumps
                // OR where a committed delay is set (these are also key
                // decision points � choosing delay=17 vs delay=0 matters).
                // Also checkpoint on LANDING frames (willLand) regardless of
                // jump decision � the backtracker needs these to try different
                // jump timings from landing points (e.g. delay=1,2,3 from the
                // exact frame the cube touches ground).
                // Don't create new checkpoints during backtrack replays � the
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
                // always compared against 0 � effectively disabling all cube
                // backtracking.  The cube's forward lookahead decisions were
                // sufficient; forced backtrack alternatives (no-jump, toggle-
                // hold, opposite-bias) consistently produced WORSE paths.
                // Ship/ball/UFO still benefit from backtracking.
                // UPDATE: Cube backtracking re-enabled � the forward-only
                // approach fails at obstacles like triple spikes where the
                // cube needs to try different approaches from nearby checkpoints.
                if (shouldCheckpoint && !isBacktrackFrame && !_backtrackActive)
                {
                    if (_backtrackCheckpoints!.Count >= MAX_CHECKPOINT_DEPTH)
                        _backtrackCheckpoints.RemoveAt(0);
                    // Use pre-decision state if captured, otherwise snapshot now
                    var cpState = hasPreDecision
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
                        PrevFrameWasGrounded = _prevFrameWasGrounded,
                        NextCoinCheckIdx = _nextCoinCheckIdx
                    });
                }

#if !DISABLE_DEBUG_LOGGING
                PfLog($"[DECIDE] input={input}");
#endif

                int preStepGameMode = state.GameMode;
                _cubeJumpedThisStep = false;
                bool alive = StepFrame(ref state, input, out bool endLevel);

                // Fix 23: Post-hoc input correction for cube mode.
                // DecideInput may return True for reasons that don't correspond
                // to an actual cube jump (e.g. CubeWillLandThisFrame passthrough,
                // swing-mode carryover, hold-jump continuity, etc.).
                // If StepFrame didn't actually fire a cube jump, correct the
                // stored input to False so the SIM replay doesn't jump either.
                // Fix 31: Don't clear the input if a mode-change portal fired
                // during this frame.  The press was consumed by the NEW mode's
                // physics (e.g. UFO jump on a cube→UFO transition frame).
                // Fix 36: Don't clear input during dashing — input=true sustains
                // the dash (Dashing != 0 && !input ends it).  The cube jump
                // check is irrelevant while dashing.
                if (preStepGameMode == 0 && input && !_cubeJumpedThisStep
                    && state.GameMode == 0 && state.Dashing == 0)
                {
                    Inputs[frame] = false;
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[FIX23] Corrected Inputs[{frame}] True->False (cube input=True but no jump fired)");
#endif
                }

                TraceFrame(frame, ref state, Inputs[frame], alive);

                // Record path at visual center AFTER physics+eject (matching sim's
                // recording inside UfoShipEject_Fresh / CubePhysics_Fresh which uses
                //   X: (playerX_fixed >> 8) + playerVisualWidth / 2  (= X + 8)
                //   Y: (playerY_fixed >> 8) + [4 if mini && !inverted] + playerVisualHeight / 2
                {
                    int pathMiniOffY = state.Mini ? 4 : 0;
                    PathPoints.Add(((state.X_fixed >> 8) + 8,
                                    (state.Y_fixed >> 8) + pathMiniOffY + 8));
                    if (state.DualActive)
                    {
                        // Insert segment break when dual mode just activated (gap from previous section)
                        if (!_prevDualActiveForPath && Path2Points.Count > 0)
                            Path2Points.Add((-1, -1)); // sentinel: start new segment
                        int p2MiniOffY = state.P2_Mini ? 4 : 0;
                        Path2Points.Add(((state.X_fixed >> 8) + 8,
                                         (state.P2_Y_fixed >> 8) + p2MiniOffY + 8));
                    }
                    _prevDualActiveForPath = state.DualActive;
                }

                // -- Coin miss detection --
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
                            // Track how many times this coin has triggered MISSED_COIN.
                            // After the miss limit, auto-forgive: the coin may be
                            // physically reachable but not survivable.
                            _coinMissRetryCount.TryGetValue(coin.Index, out int missCount);
                            missCount++;
                            _coinMissRetryCount[coin.Index] = missCount;
                            int missLimit = (state.GameMode == 1 || state.GameMode == 3)
                                ? 4 : MAX_COIN_MISS_RETRIES;
                            if (missCount > missLimit)
                            {
                                _forgivenCoins.Add(coin.Index);
                                _forgivenCoinGameModes[coin.Index] = state.GameMode;
                                _autoForgivenCoins.Add(coin.Index);
                                if (ci == _nextCoinCheckIdx) _nextCoinCheckIdx++;
                                _log.WriteLine($"[COIN_AUTO_FORGIVEN] idx={coin.Index} gm={state.GameMode} missCount={missCount} playerX={state.X_fixed >> 8}");
#if !DISABLE_DEBUG_LOGGING
                                PfLog($"[COIN_AUTO_FORGIVEN] idx={coin.Index} sid=0x{coin.SpriteId:X2} missCount={missCount} � auto-forgiven after {MAX_COIN_MISS_RETRIES} retries");
#endif
                                continue;
                            }
                            // Missed this coin � trigger backtrackable death
                            alive = false;
                            _lastDeathReason = "MISSED_COIN";
                            _lastDeathX = coin.HitLeft;
                            _lastDeathY = coin.HitTop;
                            _missedCoinIdx = ci;
                            // Ship coin trajectory diagnostic
                            if (state.GameMode == 1 || state.GameMode == 3)
                            {
                                int playerY = state.Y_fixed >> 8;
                                int coinCY = (coin.HitTop + coin.HitBottom) / 2;
                                int yOff = playerY - coinCY;
                                _log.WriteLine($"[SHIP_COIN_MISS] idx={coin.Index} playerY={playerY} coinY={coinCY} yOff={yOff} missCount={missCount}");
                            }
                            if (state.GameMode == 2)
                            {
                                int playerY = state.Y_fixed >> 8;
                                int coinCY = (coin.HitTop + coin.HitBottom) / 2;
                                int coinCX = (coin.HitLeft + coin.HitRight) / 2;
                                int yOff = playerY - coinCY;
                                _log.WriteLine($"[BALL_COIN_MISS] idx={coin.Index} playerX={state.X_fixed >> 8} playerY={playerY} coinX={coinCX} coinY={coinCY} yOff={yOff} onGround={state.OnGround} gravFlip={state.GravFlipped} missCount={missCount}");
                            }
#if !DISABLE_DEBUG_LOGGING
                            PfLog($"[MISSED_COIN] idx={coin.Index} sid=0x{coin.SpriteId:X2} hitbox=({coin.HitLeft},{coin.HitTop})-({coin.HitRight},{coin.HitBottom}) playerX={playerX} retryCount={missCount}");
#endif
                            break;
                        }
                        break; // next coin is still ahead � stop checking
                    }
                }

                // -- Coin collection detection --
                // Check if the player's hitbox overlaps any uncollected coin.
                // Uses the same overlap logic as ProcessSprites for consistency
                // (exclusive bounds, NES +1 X offset, 2-arg hitbox offset).
                if (alive && _nextCoinCheckIdx < allCoins.Count)
                {
                    int nesX = (state.X_fixed >> 8) + 1; // NES sprite_collide() offset
                    int hbW = GetHitboxW(state.Mini);
                    int hbH = GetHitboxH(state.Mini);
                    int hbOffY = GetHitboxOffsetY(state.GameMode, state.Mini, state.GravFlipped);
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
                            {
                                _permanentlyCollectedCoins[coin.Index] = frame;
                                _log.WriteLine($"[COIN_COLLECTED] idx={coin.Index} sid=0x{coin.SpriteId:X2} gm={state.GameMode} playerX={nesX} playerY={playerTop} coinHit=({coin.HitLeft},{coin.HitTop})-({coin.HitRight},{coin.HitBottom})");
                            }
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
                    PfLog($"[DEATH] frame={frame} X={state.X_fixed >> 8}px Y={state.Y_fixed >> 8}px pct={deathPct}% reason={_lastDeathReason} dX={_lastDeathX} dY={_lastDeathY} VelY=0x{state.VelY_fixed:X4} gravFlipped={state.GravFlipped}");
#endif
                    if (_speculativeDepth == 0 && !UseBFS)
                        _log.WriteLine($"[DEATH_DBG] frame={frame} X={state.X_fixed >> 8} Y={state.Y_fixed >> 8} gm={state.GameMode} reason={_lastDeathReason} dX={_lastDeathX} dY={_lastDeathY} totalBT={_totalBacktrackAttempts}");
                    // Snapshot the current full path if it reached further than any previous attempt
                    SnapshotBestPath();

                    // For MISSED_COIN deaths, save fallback state before backtracking.
                    // If all backtracks fail, we can forgive the coin and continue.
                    // CRITICAL: also save the backtrack checkpoint list � the backtracking
                    // process consumes checkpoints trying to reach the coin, and if we
                    // forgive the coin we need those checkpoints back for the rest of
                    // the level.
                    bool isCoinMiss = (_lastDeathReason == "MISSED_COIN" && _missedCoinIdx >= 0);
                    // Only save fallback state on the FIRST miss of this coin.
                    // Subsequent misses (after backtrack replays) may have
                    // degraded ProcessedSprites (earlier coins lost due to
                    // alternative paths). Preserving the original fallback
                    // ensures all previously collected coins are retained.
                    if (isCoinMiss && coinFallbackCheckpoints == null)
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
                    // from where we were � the coin is simply unreachable.
                    // EXCEPT: during retry, mandatory coins cannot be forgiven �
                    // crossing past them is a permanent death.
                    if (isCoinMiss)
                    {
                        var missedCoin = allCoins[_missedCoinIdx];

                        // Mandatory coins during retry: do NOT forgive � treat as permanent death
                        if (_retryMandatoryCoins.Contains(missedCoin.Index))
                        {
                            _log.WriteLine($"[MANDATORY_COIN_DEATH] idx={missedCoin.Index} sid=0x{missedCoin.SpriteId:X2} � mandatory coin missed, permanent death");
                            // Use the best path we found
                            UseBestPathIfBetter();
                            Success = false;
                            int bestX2 = PathPoints.Count > 0 ? PathPoints[PathPoints.Count - 1].x : 0;
                            int bestPct2 = levelLengthPx > 0 ? bestX2 * 100 / levelLengthPx : 0;
                            ResultMessage = $"Permanent death at frame {frame} (reason: mandatory coin {missedCoin.Index} missed) � best X {bestX2}px ({bestPct2}%)";
                            _log.WriteLine($"[PERM_DEATH] frame={frame} X={state.X_fixed >> 8} Y={state.Y_fixed >> 8} reason=MANDATORY_COIN_{missedCoin.Index}");
                            TraceFrameClose();
                            return;
                        }

                        _forgivenCoins.Add(missedCoin.Index);
                        _forgivenCoinGameModes[missedCoin.Index] = coinFallbackState.GameMode;
                        // Clear coin input script � the coin was forgiven, so the
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
                        // Restore backtrack checkpoints � the backtracking consumed them
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
                        // Re-populate _permanentlyCollectedCoins from restored state.
                        // Backtracking during the coin-miss retry may have invalidated
                        // coins that are still present in coinFallbackState.ProcessedSprites.
                        foreach (var coin in allCoins)
                        {
                            if (state.ProcessedSprites?.Contains(coin.Index) == true && !_permanentlyCollectedCoins.ContainsKey(coin.Index))
                                _permanentlyCollectedCoins[coin.Index] = coinFallbackFrame;
                        }
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
                        // Re-populate _permanentlyCollectedCoins from restored state.
                        foreach (var coin in allCoins)
                        {
                            if (state.ProcessedSprites?.Contains(coin.Index) == true && !_permanentlyCollectedCoins.ContainsKey(coin.Index))
                                _permanentlyCollectedCoins[coin.Index] = coinFallbackFrame;
                        }
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[COIN_FORGIVEN_ALT] idx={missedCoin2.Index} sid=0x{missedCoin2.SpriteId:X2} � coin retry caused death elsewhere, forgiving");
#endif
                        continue;
                    }

                    // Use the best path we found
                    UseBestPathIfBetter();
                    Success = false;
                    int bestX = PathPoints.Count > 0 ? PathPoints[PathPoints.Count - 1].x : 0;
                    int bestPct = levelLengthPx > 0 ? bestX * 100 / levelLengthPx : 0;
                    string deathMsg = $"Died � partial path to X={bestX}px ({bestPct}%) reason={_lastDeathReason} dX={_lastDeathX} dY={_lastDeathY}";
                    if (PreferCoins && allCoins.Count > 0)
                    {
                        int collected = 0;
                        foreach (var c in allCoins)
                            if (state.ProcessedSprites.Contains(c.Index)) collected++;
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
                        _log.WriteLine($"[RUN_COMPLETE] coins={collectedCoinIndices.Count}/{allCoins.Count} forgiven={_forgivenCoins.Count} collected=[{string.Join(",", collectedCoinIndices)}]");
                        foreach (var coin in allCoins)
                        {
                            bool hit = collectedCoinIndices.Contains(coin.Index);
                            _log.WriteLine($"[COIN_RESULT] idx={coin.Index} sid=0x{coin.SpriteId:X2} pos=({coin.AnchorX_px},{coin.AnchorY_px}) hit=({coin.HitLeft},{coin.HitTop})-({coin.HitRight},{coin.HitBottom}) {(hit ? "COLLECTED" : "MISSED")}");
                        }
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
                ResultMessage = $"Timeout � partial path to X={bestXT}px ({bestPctT}%)";
            }
            TraceFrameClose();
#if !DISABLE_DEBUG_LOGGING
            PfLog($"[TIMEOUT]");
#endif
        }

        // -------------------------------------------------------------------
        //  BFS PATHFINDER � exhaustive exploration of all input sequences
        //  Uses frame-indexed parent tracking for path reconstruction
        //  instead of linked-list chains (much less GC pressure).
        // -------------------------------------------------------------------

        /// <summary>Maximum frontier size for BFS exploration.</summary>
        private const int BFS_MAX_FRONTIER = 60000;

        /// <summary>
        /// Binary search: find first index in _spritesArr with AnchorX_px >= targetX_px.
        /// </summary>
        private int SpriteLowerBound(int targetX_px)
        {
            int lo = 0, hi = _spritesArr.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (_spritesArr[mid].AnchorX_px < targetX_px) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        /// <summary>
        /// Quantize a SimState into a 64-bit key for deduplication.
        /// Two states with the same key are considered equivalent �
        /// keeping only the one with the better score.
        /// </summary>
        private long BfsQuantizeKey(ref SimState s)
        {
            // Ship/UFO/Wave/Snake/Swing modes have continuous Y/VelY -- coarser
            // quantization prevents the frontier from filling with near-duplicate states.
            // Discrete modes (cube/ball/robot/spider) use 1/4-pixel Y quantization;
            // finer than that (e.g. 1/64 pixel) creates a combinatorial explosion
            // in sections with many jump timings (gravity flips, staircase sections).
            bool continuous = (s.GameMode == 1 || s.GameMode == 3 || s.GameMode == 6 || s.GameMode == 7 || s.GameMode == 10); // ship, UFO, wave, swing, snake
            
            // Quantize Y: continuous at 2px, discrete at 1/4px.
            int yq = continuous ? ((s.Y_fixed >> 9) & 0xFFF) 
                                : ((s.Y_fixed >> 6) & 0xFFFF);
            // Quantize VelY: continuous 32-unit, discrete 64-unit
            int vq = continuous ? (((s.VelY_fixed + 0x8000) >> 5) & 0xFFF)
                                : (((s.VelY_fixed + 0x8000) >> 6) & 0x7FF);
            // Pack game mode, gravity, mini, onGround, orbed.
            // Orbed is critical for swing: it determines whether a gravity
            // flip can happen this frame. Without it, flip-ready and
            // flip-blocked states get merged, losing corridor navigation paths.
            // Include VelX (speed) so that states at different speeds are never
            // deduped together.  Map the 6 known speed values to distinct 3-bit indices.
            int speedBits = s.VelX_fixed switch
            {
                SharedPhysics.CUBE_SPEED_SLOW => 0,
                SharedPhysics.CUBE_SPEED_X05  => 1,
                SharedPhysics.CUBE_SPEED_X1   => 2,
                SharedPhysics.CUBE_SPEED_X2   => 3,
                SharedPhysics.CUBE_SPEED_X3   => 4,
                SharedPhysics.CUBE_SPEED_X4   => 5,
                _ => 7 // unknown speed
            };
            int flags = (s.GameMode & 0xF) | ((s.GravFlipped ? 1 : 0) << 4)
                      | ((s.Mini ? 1 : 0) << 5) | ((s.OnGround ? 1 : 0) << 6)
                      | ((s.Orbed ? 1 : 0) << 7) | ((speedBits & 0x7) << 8);
            // ProcessedSprites hash (include RobotJumpTime for robot mode dedup)
            int sprHash = s.ProcessedSprites.GetBitsHash();
            if (s.RobotJumpTime > 0)
                sprHash = sprHash * 31 + s.RobotJumpTime;
            // When Step 2 (H/F_BLOCK opposite-direction eject) fires, many states
            // converge to the same Y/VelY but differ only in sprite history.
            // Zero out sprHash so post-eject states at the same Y/vel/flags
            // dedup together regardless of sprite processing history.
            if (s.Step2Ejected)
                sprHash = 0;
            // Mix P2 state into hash when dual is active so that paths with
            // different P2 positions/velocities are not collapsed.
            // Use COARSER P2 quantization than P1 to prevent the state
            // space from exploding during dual sections (P2 adds extra
            // dimensions; fine P2 buckets cause cap-trimming to prune
            // valid paths before they can navigate the corridor).
            if (s.DualActive)
            {
                int p2y = continuous ? ((s.P2_Y_fixed >> 11) & 0x1FF)
                                     : ((s.P2_Y_fixed >> 9) & 0x1FF);
                int p2v = continuous ? (((s.P2_VelY_fixed + 0x8000) >> 8) & 0xFF)
                                     : (((s.P2_VelY_fixed + 0x8000) >> 8) & 0xFF);
                unchecked
                {
                    sprHash = sprHash * 397 + p2y;
                    sprHash = sprHash * 397 + p2v;
                    sprHash = sprHash * 397 + (s.P2_GravFlipped ? 1 : 0);
                }
            }
              // Pack into 64 bits.
              // Layout: [sprHash:17][flags:11][vel:11][y:16]  = 55 bits
              return ((long)(sprHash & 0x1FFFF) << 38)
                  | ((long)(flags & 0x7FF) << 27)
                 | ((long)(vq & 0x7FF) << 16)
                 | ((long)(yq & 0xFFFF));
        }

        /// <summary>
        /// Score a BFS state. Lower is better. Heavily rewards coin collection.
        /// </summary>
        private int BfsScore(ref SimState s, int coinsCollected)
        {
            // Coin bonus: each coin subtracts 1,000,000 from score
            int coinBonus = -coinsCollected * 1_000_000;

            // Jump timing bias: nudge frontier pruning to prefer different altitudes.
            // Earliest (0.0) → prefer lower Y (higher in screen = jumped earlier).
            // Latest  (1.0) → prefer higher Y (lower in screen = walking/grounded).
            // Middle  (0.5) → no altitude preference.
            // Scale is small (±Y/4) so it only breaks ties among states with
            // the same coin count, never overriding coin collection.
            int yBias = 0;
            if (JumpTimingBias < 0.45)
            {
                // Prefer states with lower Y (jumped → higher altitude)
                yBias = (s.Y_fixed >> 8) / 4;  // positive = worse score for low-altitude
            }
            else if (JumpTimingBias > 0.55)
            {
                // Prefer states with higher Y (walked → lower altitude)
                yBias = -(s.Y_fixed >> 8) / 4; // negative = better score for low-altitude
            }

            // Step2Ever penalty: states whose lineage was altered by H/F_BLOCK
            // opposite-direction eject get deprioritized in frontier selection.
            // This preserves non-Step2 states (correct trajectories for narrow
            // gaps) in the main slots while Step2 states survive via diversity.
            int step2Penalty = 0; // disabled after fixing eject execution order

            return coinBonus + yBias + step2Penalty;
        }

        /// <summary>
        /// Run a full BFS exploration over the entire level, trying both
        /// press and release on every frame. States are deduplicated by
        /// quantized physics signature to keep the frontier manageable.
        /// Uses frame-indexed parent arrays for path reconstruction
        /// (very low GC pressure compared to linked-list chains).
        /// </summary>
        private void RunBFS(int startX_px, int startY_px, int startSpeedUiIndex,
                            int startGameMode, bool startGravFlipped, bool startMini)
        {
            var bfsSw = System.Diagnostics.Stopwatch.StartNew();
            if (Verbose) _log.WriteLine($"[BFS] Starting exhaustive BFS exploration (frontier cap={BFS_MAX_FRONTIER})");
            if (Verbose) _log.WriteLine($"[BFS_PARAMS] startX={startX_px} startY={startY_px} speed={startSpeedUiIndex} mode={startGameMode} gravFlip={startGravFlipped} mini={startMini} bias={JumpTimingBias} coins={PreferCoins} useBfs={UseBFS}");
            _step1FireCount = 0; _step2FireCount = 0;
#if !DISABLE_DEBUG_LOGGING
            void BfsLog(string msg) { try { System.IO.File.AppendAllText(pfDebugLogPath, "[BFS] " + msg + System.Environment.NewLine); } catch { } }
            BfsLog($"Starting BFS (frontier cap={BFS_MAX_FRONTIER})");
            BfsLog($"PARAMS startX={startX_px} startY={startY_px} speed={startSpeedUiIndex} mode={startGameMode} gravFlip={startGravFlipped} mini={startMini} bias={JumpTimingBias} coins={PreferCoins} useBfs={UseBFS}");
#endif

            try
            {
                // Initialize starting state
                int initCamY_bfs = ComputeInitCameraY(startY_px);
                var initialState = new SimState
                {
                    X_fixed = startX_px << 8,
                    Y_fixed = startY_px << 8,
                    VelX_fixed = SpeedUiIndexToFixed(startSpeedUiIndex),
                    VelY_fixed = 0,
                    GameMode = startGameMode,
                    GravFlipped = startGravFlipped,
                    Mini = startMini,
                    GravMul = startGravFlipped ? -1 : 1,
                    GravityMod = 1.0,
                    WasZeroedByCollision = true,
                    OnGround = true,
                    ProcessedSprites = NewSpriteSet(),
                    PendingOrbIndex = -1,
                    PendingOrbSpriteId = -1,
                    NinjaJumps = (startGameMode == 8) ? NINJA_MAX_JUMPS : 0,
                    CameraY_fixed = initCamY_bfs,
                    TargetCameraY_fixed = initCamY_bfs
                };
                ApplyPortalsUpTo(ref initialState, startX_px);

                // Reset engine state for speculative execution
                _btSkipAllOrbs = false;
                _btSkipSpecificOrbs.Clear();
                _btSkipSpecificPads.Clear();
                _speculativeDepth = 1;

                int levelLengthPx = mapWidth * TILE;
                int highWaterX = startX_px;
                int lastProgressPct = -1;

                // Current frontier � flat arrays for cache friendliness
                var frontier = new List<SimState> { initialState };

                // Frame-indexed path reconstruction (compact, no GC pressure)
                // histParent[f][i] = parent index in previous frontier
                // histInput[f][i]  = input applied at frame f
                var histParent = new List<int[]>();
                var histInput = new List<bool[]>();

                // Winner tracking
                int winFrame = -1, winParentIdx = -1, winCoins = -1;
                bool winInputVal = false;
                SimState winState = default;

                // Best partial tracking (for incomplete runs)
                int bestFrame = -1, bestIdx = -1, bestX = startX_px;

                // Pre-allocate expansion arrays and candidate lists (reused each frame)
                int maxExpand = BFS_MAX_FRONTIER * 2;
                var rState = new SimState[maxExpand];
                var rAlive = new bool[maxExpand];
                var rEnd   = new bool[maxExpand];
                var candState  = new List<SimState>(maxExpand);
                var candParent = new List<int>(maxExpand);
                var candInput  = new List<bool>(maxExpand);
                var candCoins  = new List<int>(maxExpand);
                var candScore  = new List<int>(maxExpand);
                var deduped = new Dictionary<long, int>(maxExpand);

                for (int frame = 0; frame < MAX_FRAMES && frontier.Count > 0; frame++)
                {
                    if (CancelRequested) break;
                    _frameCounter = frame;

                    // Progress reporting + Follow support
                    int curX = frontier[0].X_fixed >> 8;
                    if (curX > highWaterX) highWaterX = curX;
                    _currentX_px = highWaterX; // Update for Follow button
                    int pct = levelLengthPx > 0 ? highWaterX * 100 / levelLengthPx : 0;
                    if (pct != lastProgressPct && Progress != null)
                    {
                        Progress.Report(pct);
                        lastProgressPct = pct;
                    }
                    // -- Expand all frontier states with both inputs (PARALLEL) --
                    int expandCount = frontier.Count * 2;
                    // Reuse pre-allocated arrays (only first expandCount slots used)

                    Parallel.For(0, expandCount, k =>
                    {
                        int pi = k >> 1;        // parent index
                        bool inp = (k & 1) == 1; // input: 0=release, 1=press
                        var sim = frontier[pi].Clone();
                        bool alive = StepFrame(ref sim, inp, out bool endLevel);

                        // Rainbow multi-mode verification: persistent shadow states
                        // evolve independently with their own physics each frame.
                        // This ensures the input sequence survives ALL possible random
                        // mode outcomes, not just one frame at a time.
                        if (alive && !endLevel)
                        {
                            bool portalFrame = sim.RainbowMaxMode > 0 && frontier[pi].RainbowMaxMode == 0;
                            bool hasShadows = sim.RainbowShadows != null;

                            if (portalFrame)
                            {
                                // Portal frame: create persistent shadows, one per alternative mode
                                int maxMode = sim.RainbowMaxMode;
                                var shadows = new SimState[maxMode - 1];
                                int idx = 0;
                                for (int m = 0; m < maxMode && alive; m++)
                                {
                                    if (m == sim.GameMode) continue;
                                    var shadow = frontier[pi].Clone();
                                    shadow.GameMode = m;
                                    shadow.RainbowMaxMode = maxMode;
                                    shadow.RainbowShadows = null; // shadows don't nest
                                    // NES: VelY zeroed based on entry mode (before randomization)
                                    if (frontier[pi].GameMode == 6 || frontier[pi].GameMode == 10)
                                        shadow.VelY_fixed = 0;
                                    bool shadowAlive = StepFrame(ref shadow, inp, out _);
                                    if (!shadowAlive)
                                    {
                                        shadow.ProcessedSprites.Return();
                                        alive = false;
                                        for (int j = 0; j < idx; j++)
                                            shadows[j].ProcessedSprites.Return();
                                    }
                                    else
                                    {
                                        shadows[idx++] = shadow;
                                    }
                                }
                                if (alive) sim.RainbowShadows = shadows;
                            }
                            else if (hasShadows)
                            {
                                // Subsequent frames: step existing persistent shadows
                                var shadows = sim.RainbowShadows!;
                                for (int si = 0; si < shadows.Length && alive; si++)
                                {
                                    bool shadowAlive = StepFrame(ref shadows[si], inp, out _);
                                    if (!shadowAlive)
                                    {
                                        alive = false;
                                        shadows[si].ProcessedSprites.Return();
                                        for (int sj = si + 1; sj < shadows.Length; sj++)
                                            shadows[sj].ProcessedSprites.Return();
                                        sim.RainbowShadows = null;
                                    }
                                }
                                // Rainbow ended this frame (real mode portal cleared it):
                                // step already done, now discard shadows
                                if (alive && sim.RainbowMaxMode == 0 && sim.RainbowShadows != null)
                                {
                                    for (int si = 0; si < sim.RainbowShadows.Length; si++)
                                        sim.RainbowShadows[si].ProcessedSprites.Return();
                                    sim.RainbowShadows = null;
                                }
                            }
                        }

                        rState[k] = sim;
                        rAlive[k] = alive;
                        rEnd[k]   = endLevel;
                    });

                    // Sequential post-processing of parallel results

                    candState.Clear();
                    candParent.Clear();
                    candInput.Clear();
                    candCoins.Clear();
                    candScore.Clear();
                    int deathCount = 0;
                    int gravFDeathCount = 0;
                    var gravFDeathTypes = new int[13];
                    bool trackDeathTypes = (frame >= 2000 && frame <= 2070);
                    int[]? frameDtCounts = trackDeathTypes ? new int[13] : null;

                    for (int k = 0; k < expandCount; k++)
                    {
                        int pi = k >> 1;
                        bool inp = (k & 1) == 1;

                        if (rEnd[k])
                        {
                            var sim = rState[k];
                            int coins = CountBfsCoins(ref sim);
                            if (winFrame < 0 || coins > winCoins)
                            {
                                winFrame = frame;
                                winParentIdx = pi;
                                winInputVal = inp;
                                winCoins = coins;
                                winState = sim;
                                _log.WriteLine($"[BFS] Level complete at frame {frame}! coins={coins} X�{sim.X_fixed >> 8}px");
                            }
                            continue;
                        }

                        if (!rAlive[k])
                        {
                            // Track death types per frame for detailed logging
                            if (frameDtCounts != null) frameDtCounts[rState[k].DeathType]++;
                            // Track deaths of gravity-flipped states (summary)
                            {
                                int pi3 = k >> 1;
                                var ps3 = frontier[pi3];
                                if (ps3.GravFlipped)
                                {
                                    gravFDeathCount++;
                                    var dt = rState[k].DeathType;
                                    if (dt >= 0 && dt < gravFDeathTypes.Length) gravFDeathTypes[dt]++;
                                    if (_gravFDeathCounts == null) _gravFDeathCounts = new int[13];
                                    if (dt < _gravFDeathCounts.Length) _gravFDeathCounts[dt]++;
                                    _gravFDeathFrame = frame;
                                    // Log first 3 samples per frame
                                    if (gravFDeathCount <= 3 && frame >= 1890)
                                    {
                                        var ds3 = rState[k];
                                        _log.WriteLine($"[GF_DEAD] f={frame} pX={ps3.X_fixed>>8} pY={ps3.Y_fixed>>8} pVelY=0x{ps3.VelY_fixed:X} | cX={ds3.X_fixed>>8} cY={ds3.Y_fixed>>8} dt={dt} inp={(k&1)==1}");
                                    }
                                }
                            }
                            rState[k].ReturnAllSpriteResources(); deathCount++; continue;
                        }

                        var st = rState[k];
                        int nc = CountBfsCoins(ref st);
                        int sc = BfsScore(ref st, nc);
                        candState.Add(st);
                        candParent.Add(pi);
                        candInput.Add(inp);
                        candCoins.Add(nc);
                        candScore.Add(sc);
                    }

                    // Death zone orb tracking: how many candidates processed each green orb
#if !DISABLE_DEBUG_LOGGING
                    if (frame >= 1930 && frame <= 2050 && frame % 10 == 0)
                    {
                        // Green orb indices from DZ_SPRITE dump
                        int[] orbIdx = { 10310, 11414, 10314, 11418, 11432 };
                        int[] orbCount = new int[orbIdx.Length];
                        int redOrbCount = 0; // idx=10319 (red at col 401)
                        int blueOrbCount = 0; // idx=10332 (blue at col 414)
                        int gravPortalCount = 0; // idx=9768 (gravity reverse at col 401)
                        foreach (var cs in candState)
                        {
                            for (int oi = 0; oi < orbIdx.Length; oi++)
                                if (cs.ProcessedSprites.Contains(orbIdx[oi])) orbCount[oi]++;
                            if (cs.ProcessedSprites.Contains(10319)) redOrbCount++;
                            if (cs.ProcessedSprites.Contains(10332)) blueOrbCount++;
                            if (cs.ProcessedSprites.Contains(9768)) gravPortalCount++;
                        }
                        Console.Error.WriteLine($"[ORB_TRACK] f={frame} cands={candState.Count} green392={orbCount[0]} green394={orbCount[1]} green396={orbCount[2]} green398={orbCount[3]} green412={orbCount[4]} red401={redOrbCount} blue414={blueOrbCount} gravPortal401={gravPortalCount}");
                    }
#endif

                    // Y-bin histogram at critical frames
#if !DISABLE_DEBUG_LOGGING
                    if (frame >= 1990 && frame <= 2040 && frame % 5 == 0)
                    {
                        // Bins: <240, 240-255, 256-271, 272-287, 288-303, 304-319, 320-335, 336-351, >=352
                        int[] binsN = new int[9]; // normal grav
                        int[] binsR = new int[9]; // reversed grav
                        foreach (var cs in candState)
                        {
                            int y = cs.Y_fixed >> 8;
                            int bin = y < 240 ? 0 : y >= 352 ? 8 : (y - 240) / 16 + 1;
                            if (cs.GravFlipped) binsR[bin]++; else binsN[bin]++;
                        }
                        Console.Error.WriteLine($"[Y_HIST] f={frame} cands={candState.Count} " +
                            $"N:<240={binsN[0]} 240={binsN[1]} 256={binsN[2]} 272={binsN[3]} 288={binsN[4]} 304={binsN[5]} 320={binsN[6]} 336={binsN[7]} 352+={binsN[8]} " +
                            $"R:<240={binsR[0]} 240={binsR[1]} 256={binsR[2]} 272={binsR[3]} 288={binsR[4]} 304={binsR[5]} 320={binsR[6]} 336={binsR[7]} 352+={binsR[8]}");
                    }
#endif

                    if (candState.Count == 0)
                    {
                        _log.WriteLine($"[BFS] ALL DEAD at frame {frame} (X~{highWaterX}px pct={pct}% expanded={frontier.Count * 2} deaths={deathCount})");
#if !DISABLE_DEBUG_LOGGING
                        BfsLog($"ALL DEAD at frame {frame} (X~{highWaterX}px pct={pct}% expanded={frontier.Count * 2} deaths={deathCount})");
#endif                        // Death type distribution
                        var dtCounts = new int[13];
                        for (int k = 0; k < expandCount; k++)
                            if (!rAlive[k] && !rEnd[k]) dtCounts[rState[k].DeathType]++;
                        var dtNames = new[]{"UNK","CEIL_SPIKE","EJECT","CENTER","BALL_PROBE","BALL_VELZERO","BALL_EJECT","FLOOR_SPIKE","FWD","DEATH_COLL","BOUNDS","BALL_EJECT_CEIL","BALL_EJECT_FLOOR"};
                        var dtParts = new System.Collections.Generic.List<string>();
                        for (int d = 0; d < dtCounts.Length; d++)
                            if (dtCounts[d] > 0) dtParts.Add($"{dtNames[d]}={dtCounts[d]}");
                        _log.WriteLine($"[BFS] Death types: {string.Join(" ", dtParts)}");
                        System.Console.Error.WriteLine($"[BFS] Death types: {string.Join(" ", dtParts)}");
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[BFS_ALLDEAD] Death types: {string.Join(" ", dtParts)}");
#endif
                        // Log a few sample dead states with exact coordinates
                        int samp = 0;
                        for (int k = 0; k < expandCount && samp < 5; k++)
                        {
                            if (!rAlive[k] && !rEnd[k])
                            {
                                var ds = rState[k];
                                int pi2 = k >> 1;
                                var ps = frontier[pi2];
                                _log.WriteLine($"[BFS_DEAD] parent X=0x{ps.X_fixed:X} Y=0x{ps.Y_fixed:X} VelY=0x{ps.VelY_fixed:X} grav={ps.GravFlipped} mini={ps.Mini} dual={ps.DualActive} P2_Y=0x{ps.P2_Y_fixed:X} P2_grav={ps.P2_GravFlipped} | child X=0x{ds.X_fixed:X} Y=0x{ds.Y_fixed:X} VelY=0x{ds.VelY_fixed:X} grav={ds.GravFlipped} mini={ds.Mini} dt={ds.DeathType} inp={(k&1)==1}");
                                System.Console.Error.WriteLine($"[BFS_DEAD] parent Y={ps.Y_fixed>>8} VelY=0x{ps.VelY_fixed:X} grav={ps.GravFlipped} | child Y={ds.Y_fixed>>8} VelY=0x{ds.VelY_fixed:X} dt={dtNames[ds.DeathType]} inp={(k&1)==1}");
#if !DISABLE_DEBUG_LOGGING
                                PfLog($"[BFS_DEAD] parent X=0x{ps.X_fixed:X} Y=0x{ps.Y_fixed:X} VelY=0x{ps.VelY_fixed:X} grav={ps.GravFlipped} dual={ps.DualActive} P2_Y=0x{ps.P2_Y_fixed:X} P2_VelY=0x{ps.P2_VelY_fixed:X} | child X=0x{ds.X_fixed:X} Y=0x{ds.Y_fixed:X} VelY=0x{ds.VelY_fixed:X} grav={ds.GravFlipped} dt={ds.DeathType} inp={(k&1)==1}");
#endif
                                samp++;
                            }
                        }
                        // Log last known frontier stats + velocity info
                        if (frontier.Count > 0)
                        {
                            int minY = int.MaxValue, maxY = int.MinValue;
                            int minVelY = int.MaxValue, maxVelY = int.MinValue;
                            int minX = int.MaxValue, maxX = int.MinValue;
                            int minP2Y = int.MaxValue, maxP2Y = int.MinValue;
                            foreach (var s in frontier)
                            {
                                int y = s.Y_fixed >> 8;
                                int x = s.X_fixed >> 8;
                                if (y < minY) minY = y;
                                if (y > maxY) maxY = y;
                                if (x < minX) minX = x;
                                if (x > maxX) maxX = x;
                                if (s.VelY_fixed < minVelY) minVelY = s.VelY_fixed;
                                if (s.VelY_fixed > maxVelY) maxVelY = s.VelY_fixed;
                                if (s.DualActive)
                                {
                                    int p2y = s.P2_Y_fixed >> 8;
                                    if (p2y < minP2Y) minP2Y = p2y;
                                    if (p2y > maxP2Y) maxP2Y = p2y;
                                }
                            }
                            string dualInfo = frontier[0].DualActive ? $" P2_Y=[{minP2Y}..{maxP2Y}]" : "";
                            _log.WriteLine($"[BFS] Last frontier: size={frontier.Count} X=[{minX}..{maxX}] Y=[{minY}..{maxY}] mode={frontier[0].GameMode} grav={frontier[0].GravFlipped} mini={frontier[0].Mini} VelY=[0x{minVelY:X}..0x{maxVelY:X}]{dualInfo}");
                            System.Console.Error.WriteLine($"[BFS] Last frontier: size={frontier.Count} X=[{minX}..{maxX}] Y=[{minY}..{maxY}] mode={frontier[0].GameMode} grav={frontier[0].GravFlipped} mini={frontier[0].Mini} VelY=[0x{minVelY:X}..0x{maxVelY:X}]{dualInfo}");
#if !DISABLE_DEBUG_LOGGING
                            PfLog($"[BFS_ALLDEAD] Last frontier: size={frontier.Count} X=[{minX}..{maxX}] Y=[{minY}..{maxY}] mode={frontier[0].GameMode} grav={frontier[0].GravFlipped} mini={frontier[0].Mini} dual={frontier[0].DualActive} VelY=[0x{minVelY:X}..0x{maxVelY:X}]{dualInfo}");
#endif
                        }
                        _log.Flush();
                        break;
                    }

                    // Early exit if we found a complete path and nothing can beat it
                    if (winFrame >= 0)
                    {
                        bool anyBetter = false;
                        for (int i = 0; i < candCoins.Count; i++)
                        {
                            if (candCoins[i] > winCoins) { anyBetter = true; break; }
                        }
                        // Even if no candidate currently has more coins, there may be
                        // uncollected coins AHEAD of the current frontier X.  Live
                        // candidates might still reach those coins and complete via a
                        // different end-level trigger, so don't exit early.
                        if (!anyBetter && PreferCoins && allCoins.Count > 0 && candState.Count > 0)
                        {
                            int frontierMaxX = 0;
                            for (int i = 0; i < candState.Count; i++)
                            {
                                int cx = candState[i].X_fixed >> 8;
                                if (cx > frontierMaxX) frontierMaxX = cx;
                            }
                            foreach (var coin in allCoins)
                            {
                                if (coin.HitRight > frontierMaxX && !winState.ProcessedSprites!.Contains(coin.Index))
                                {
                                    anyBetter = true;
                                    break;
                                }
                            }
                        }
                        if (!anyBetter)
                        {
                            _log.WriteLine($"[BFS] Optimal � winning path at frame {winFrame} with {winCoins} coins, no better candidates");
                            break;
                        }
                    }

                    // -- Deduplicate by quantized key --
                    deduped.Clear();
                    for (int i = 0; i < candState.Count; i++)
                    {
                        var s = candState[i];
                        long key = BfsQuantizeKey(ref s);
                        if (!deduped.TryGetValue(key, out int ex) || candScore[i] < candScore[ex])
                            deduped[key] = i;
                    }

                    // -- GRAVF pipeline diagnostic --
                    {
                        int gfCand = 0, gfDedup = 0;
                        for (int i = 0; i < candState.Count; i++) if (candState[i].GravFlipped) gfCand++;
                        foreach (var di in deduped.Values) if (candState[di].GravFlipped) gfDedup++;
                        int gfFront = 0;
                        foreach (var s in frontier) if (s.GravFlipped) gfFront++;
                        if (gfFront > 0 || gfCand > 0)
                        {
                            int gfMinY = int.MaxValue, gfMaxY = int.MinValue;
                            for (int i = 0; i < candState.Count; i++) if (candState[i].GravFlipped) { int y = candState[i].Y_fixed >> 8; if (y < gfMinY) gfMinY = y; if (y > gfMaxY) gfMaxY = y; }
                            string dtStr = "";
                            if (gravFDeathCount > 0) {
                                var dtN = new[]{"UNK","CEIL","EJT","CTR","BPR","BVZ","BEJ","FLR","FWD","DCL","BND","OOT","OOB"};
                                for (int d = 0; d < gravFDeathTypes.Length; d++) if (gravFDeathTypes[d] > 0) dtStr += $" {(d<dtN.Length?dtN[d]:$"d{d}")}={gravFDeathTypes[d]}";
                            }
                            _log.WriteLine($"[GF_PIPE] f={frame} frontGF={gfFront} gfDied={gravFDeathCount}{dtStr} candGF={gfCand} dedupGF={gfDedup} gfY=[{(gfMinY==int.MaxValue?"N/A":gfMinY.ToString())}..{(gfMaxY==int.MinValue?"N/A":gfMaxY.ToString())}]");
                        }
                    }

                    // -- Sort by score, prune to cap with diversity --
                    var sortedIdx = new List<int>(deduped.Values);
                    sortedIdx.Sort((a, b) => candScore[a].CompareTo(candScore[b]));

                    var nextFrontier = new List<SimState>();
                    var frameP = new List<int>();
                    var frameI = new List<bool>();

                    // Main slots: 75% by score
                    int mainSlots = BFS_MAX_FRONTIER * 3 / 4;

                    int mainKeep = Math.Min(mainSlots, sortedIdx.Count);
                    for (int i = 0; i < mainKeep && nextFrontier.Count < BFS_MAX_FRONTIER; i++)
                    {
                        int ci = sortedIdx[i];
                        nextFrontier.Add(candState[ci]);
                        frameP.Add(candParent[ci]);
                        frameI.Add(candInput[ci]);
                    }

                    // Diversity slots: remaining capacity from underrepresented Y bins + gravity diversity
                    if (sortedIdx.Count > mainKeep)
                    {
                        int Y_BIN_SIZE = 16;
                        var yBinCounts = new Dictionary<int, int>();
                        foreach (var s in nextFrontier)
                        {
                            int yBin = (s.Y_fixed >> 8) / Y_BIN_SIZE;
                            yBinCounts.TryGetValue(yBin, out int c);
                            yBinCounts[yBin] = c + 1;
                        }
                        int avgPerBin = nextFrontier.Count / Math.Max(1, yBinCounts.Count);

                        // Count gravity states in main slots to ensure minority grav survives
                        int gravNCount = 0, gravFCount = 0;
                        foreach (var s in nextFrontier)
                        {
                            if (s.GravFlipped) gravFCount++; else gravNCount++;
                        }
                        int gravMinority = Math.Min(gravNCount, gravFCount);
                        bool needGravDiversity = gravMinority < nextFrontier.Count / 20; // < 5%

                        for (int i = mainKeep; i < sortedIdx.Count && nextFrontier.Count < BFS_MAX_FRONTIER; i++)
                        {
                            int ci = sortedIdx[i];
                            int yBin = (candState[ci].Y_fixed >> 8) / Y_BIN_SIZE;
                            yBinCounts.TryGetValue(yBin, out int cnt);
                            // Admit if Y bin has fewer than average (ensures all altitudes explored)
                            bool yUnderrepresented = cnt < avgPerBin + 1;

                            // Also admit gravity-minority states
                            bool gravUnderrepresented = needGravDiversity &&
                                ((candState[ci].GravFlipped && gravFCount < gravNCount) ||
                                 (!candState[ci].GravFlipped && gravNCount < gravFCount));

                            if (yUnderrepresented || gravUnderrepresented)
                            {
                                nextFrontier.Add(candState[ci]);
                                frameP.Add(candParent[ci]);
                                frameI.Add(candInput[ci]);
                                yBinCounts[yBin] = cnt + 1;
                                if (candState[ci].GravFlipped) gravFCount++; else gravNCount++;
                                gravMinority = Math.Min(gravNCount, gravFCount);
                                needGravDiversity = gravMinority < nextFrontier.Count / 20;
                            }
                        }
                    }

                    // -- GRAVF: count after selection --
                    {
                        int gfNext = 0;
                        foreach (var s in nextFrontier) if (s.GravFlipped) gfNext++;
                        // Only log if dedup count was > 0 but next count differs
                        int gfDedup2 = 0;
                        foreach (var di in deduped.Values) if (candState[di].GravFlipped) gfDedup2++;
                        if (gfDedup2 > 0 && gfNext != gfDedup2)
                            _log.WriteLine($"[GF_SELECT] f={frame} dedupGF={gfDedup2} selectedGF={gfNext} totalNext={nextFrontier.Count}");
                    }

                    // Return SpriteSets for candidates not kept in nextFrontier
                    {
                        var keptRefs = new HashSet<object>(nextFrontier.Count);
                        foreach (var s in nextFrontier) keptRefs.Add(s.ProcessedSprites);
                        for (int i = 0; i < candState.Count; i++)
                        {
                            if (!keptRefs.Contains(candState[i].ProcessedSprites))
                            {
                                var tmp = candState[i];
                                tmp.ReturnAllSpriteResources();
                            }
                        }
                    }

                    // Store history for path reconstruction
                    histParent.Add(frameP.ToArray());
                    histInput.Add(frameI.ToArray());

                    // Log frontier Y distribution near 45% zone
#if !DISABLE_DEBUG_LOGGING
                    if (nextFrontier.Count > 0)
                    {
                        int fx = nextFrontier[0].X_fixed >> 8;
                        bool nearWall = fx > 29300 && fx < 29600;
                        if (fx > 28000 && fx < 30000 && (frame % 50 == 0 || (nearWall && frame % 5 == 0)))
                        {
                            int yLow = 0; // Y > 260
                            int yHigh = 0; // Y <= 260
                            int yGapSafe = 0; // Y in [281..296] — can pass through gap
                            int yOrbPending = 0; // states with a yellow orb pending
                            int yMin = int.MaxValue, yMax = int.MinValue;
                            foreach (var s in nextFrontier)
                            {
                                int y = s.Y_fixed >> 8;
                                if (y > 260) yLow++; else yHigh++;
                                if (y >= 281 && y <= 296) yGapSafe++;
                                if (s.PendingOrbSpriteId == 0x28) yOrbPending++;
                                if (y < yMin) yMin = y;
                                if (y > yMax) yMax = y;
                            }
                            // Sample game mode from first state
                            int fMode = nextFrontier[0].GameMode;
                            System.Console.Error.WriteLine($"[BFS_FRON45] f={frame} X={fx} sz={nextFrontier.Count} Y=[{yMin}..{yMax}] yHigh={yHigh} yLow={yLow} gap={yGapSafe} yOrb={yOrbPending} mode={fMode}");
                        }
                    }
#endif

                    // Track best partial result
                    for (int i = 0; i < nextFrontier.Count; i++)
                    {
                        int sx = nextFrontier[i].X_fixed >> 8;
                        if (sx > bestX)
                        {
                            bestFrame = histParent.Count - 1;
                            bestIdx = i;
                            bestX = sx;
                        }
                    }

                    // Return old frontier SpriteSets before replacing
                    foreach (var old in frontier)
                    {
                        var tmp = old;
                        tmp.ReturnAllSpriteResources();
                    }
                    frontier = nextFrontier;

                    // -- Periodic logging + speculative path visualization --
                    bool hasDual = frontier.Count > 0 && frontier[0].DualActive;
                    bool shouldLog = (frame % 100 == 0) || (frontier.Count < 100) || (frame >= 700 && frame <= 810) || hasDual || (frame >= 2000 && frame <= 2070);
                    if (shouldLog)
                    {
                        int minY = int.MaxValue, maxY = int.MinValue;
                        int gravN = 0, gravF = 0;
                        foreach (var s in frontier)
                        {
                            int y = s.Y_fixed >> 8;
                            if (y < minY) minY = y;
                            if (y > maxY) maxY = y;
                            if (s.GravFlipped) gravF++; else gravN++;
                        }
                        double ms = bfsSw.Elapsed.TotalMilliseconds / Math.Max(1, frame + 1);
                        string dualStr = "";
                        if (hasDual)
                        {
                            int p2MinY = int.MaxValue, p2MaxY = int.MinValue;
                            foreach (var s in frontier)
                            {
                                int p2y = s.P2_Y_fixed >> 8;
                                if (p2y < p2MinY) p2MinY = p2y;
                                if (p2y > p2MaxY) p2MaxY = p2y;
                            }
                            dualStr = $" DUAL P2_Y=[{p2MinY}..{p2MaxY}]";
                        }
                        // Track all unique modes in frontier
                        var modeCounts = new int[12];
                        foreach (var s in frontier) if (s.GameMode >= 0 && s.GameMode < 12) modeCounts[s.GameMode]++;
                        var modeStr = "";
                        for (int m = 0; m < 12; m++) if (modeCounts[m] > 0) modeStr += $" m{m}={modeCounts[m]}";
                        if (Verbose)
                            _log.WriteLine(
                                $"[BFS] f={frame} front={frontier.Count} dedup={deduped.Count} " +
                                $"deaths={deathCount} Y=[{minY}..{maxY}] " +
                                $"mode={frontier[0].GameMode} gravN={gravN} gravF={gravF} " +
                                $"X~{highWaterX}px pct={pct}% ms/f={ms:F1}{dualStr} modes:{modeStr}");
                        // Step2Ever diagnostic at interesting frames
                        if (Verbose && (frame == 700 || frame == 710 || frame == 720 || frame == 800 || frame == 900 || frame == 1500 || frame == 2020 || frame == 2035 || frame == 2040))
                        {
                            int s2cnt = 0; int minYno2 = int.MaxValue; int maxYno2 = int.MinValue;
                            int minYs2 = int.MaxValue; int maxYs2 = int.MinValue;
                            for (int fi = 0; fi < frontier.Count; fi++)
                            {
                                var fs = frontier[fi];
                                int fy = fs.Y_fixed >> 8;
                                if (fs.Step2Ever) { s2cnt++; if (fy < minYs2) minYs2 = fy; if (fy > maxYs2) maxYs2 = fy; }
                                else { if (fy < minYno2) minYno2 = fy; if (fy > maxYno2) maxYno2 = fy; }
                            }
                            _log.WriteLine($"[BFS_S2] f={frame} Step2Ever={s2cnt}/{frontier.Count} " +
                                $"noS2_Y=[{(minYno2==int.MaxValue?"N/A":minYno2.ToString())}..{(maxYno2==int.MinValue?"N/A":maxYno2.ToString())}] " +
                                $"s2_Y=[{(minYs2==int.MaxValue?"N/A":minYs2.ToString())}..{(maxYs2==int.MinValue?"N/A":maxYs2.ToString())}]");
                        }
                        // Y histogram at frames near collapse (288-335 is the gap range)
                        if (Verbose && frame >= 2020 && frame <= 2042 && frame % 2 == 0)
                        {
                            int ylt288 = 0, y288_303 = 0, y304_319 = 0, y320_335 = 0, y336_351 = 0, y352p = 0;
                            int gap_gravN = 0, gap_gravF = 0;
                            for (int fi = 0; fi < frontier.Count; fi++)
                            {
                                int fy = frontier[fi].Y_fixed >> 8;
                                if (fy < 288) ylt288++;
                                else if (fy < 304) y288_303++;
                                else if (fy < 320) y304_319++;
                                else if (fy < 336) {
                                    y320_335++;
                                    if (frontier[fi].GravFlipped) gap_gravF++; else gap_gravN++;
                                }
                                else if (fy < 352) y336_351++;
                                else y352p++;
                            }
                            _log.WriteLine($"[BFS_YHIST] f={frame} <288={ylt288} 288-303={y288_303} 304-319={y304_319} 320-335={y320_335}(gN={gap_gravN},gF={gap_gravF}) 336-351={y336_351} 352+={y352p}");
                            // X distribution for gap-range states
                            if (y320_335 > 0)
                            {
                                // Count gap states by X region
                                int gapNear = 0, gapFar = 0;
                                int gapMinX = int.MaxValue, gapMaxX = int.MinValue;
                                for (int fi = 0; fi < frontier.Count; fi++)
                                {
                                    int fy = frontier[fi].Y_fixed >> 8;
                                    if (fy >= 320 && fy < 336)
                                    {
                                        int fx = frontier[fi].X_fixed >> 8;
                                        if (fx < gapMinX) gapMinX = fx;
                                        if (fx > gapMaxX) gapMaxX = fx;
                                        if (fx >= highWaterX - 200) gapNear++; else gapFar++;
                                    }
                                }
                                _log.WriteLine($"[BFS_GAPX] f={frame} count={y320_335} X=[{gapMinX}..{gapMaxX}] near(within200)={gapNear} far={gapFar}");
                            }
                        }
                        if (Verbose) _log.Flush();
                        // Dump frontier state hash at key frames for divergence debugging
                        if (Verbose && frame >= 703 && frame <= 810)
                        {
                            long fHash = 0;
                            for (int fi = 0; fi < frontier.Count; fi++)
                            {
                                var fs = frontier[fi];
                                fHash = fHash * 31 + fs.X_fixed + fs.Y_fixed * 7L + fs.VelY_fixed * 13L + (fs.GravFlipped ? 1 : 0) + fs.GameMode * 37L;
                            }
                            var hashMsg = $"[BFS_HASH] f={frame} frontHash=0x{fHash:X16} first=(X=0x{frontier[0].X_fixed:X},Y=0x{frontier[0].Y_fixed:X},VelY=0x{frontier[0].VelY_fixed:X},mode={frontier[0].GameMode}) last=(X=0x{frontier[frontier.Count-1].X_fixed:X},Y=0x{frontier[frontier.Count-1].Y_fixed:X},VelY=0x{frontier[frontier.Count-1].VelY_fixed:X},mode={frontier[frontier.Count-1].GameMode})";
                            _log.WriteLine(hashMsg);
                            _log.Flush();
#if !DISABLE_DEBUG_LOGGING
                            BfsLog(hashMsg);
#endif
                        }
                        // Verbose-level death type breakdown (works in release builds)
                        if (Verbose && frameDtCounts != null && deathCount > 0)
                        {
                            var vdtNames = new[]{"UNK","CEIL_SPIKE","EJECT","CENTER","BALL_PROBE","BALL_VELZERO","BALL_EJECT","FLOOR_SPIKE","FWD","DEATH_COLL","BOUNDS","BALL_EJECT_CEIL","BALL_EJECT_FLOOR"};
                            var vdtParts = new System.Collections.Generic.List<string>();
                            for (int d = 0; d < vdtNames.Length && d < frameDtCounts.Length; d++)
                                if (frameDtCounts[d] > 0) vdtParts.Add($"{vdtNames[d]}={frameDtCounts[d]}");
                            _log.WriteLine($"[BFS_DT] f={frame} DEATH_TYPES: {string.Join(" ", vdtParts)}");
                        }
#if !DISABLE_DEBUG_LOGGING
                        BfsLog($"f={frame} front={frontier.Count} dedup={deduped.Count} deaths={deathCount} Y=[{minY}..{maxY}] mode={frontier[0].GameMode} gravN={gravN} gravF={gravF} X~{highWaterX}px pct={pct}% ms/f={ms:F1}{dualStr}");
                        // Per-frame death type breakdown for collapse debugging
                        if (frameDtCounts != null && deathCount > 0)
                        {
                            var fdtNames = new[]{"UNK","CEIL_SPIKE","EJECT","CENTER","BALL_PROBE","BALL_VELZERO","BALL_EJECT","FLOOR_SPIKE","FWD","DEATH_COLL","BOUNDS","BALL_EJECT_CEIL","BALL_EJECT_FLOOR"};
                            var fdtParts = new System.Collections.Generic.List<string>();
                            for (int d = 0; d < frameDtCounts.Length; d++)
                                if (frameDtCounts[d] > 0) fdtParts.Add($"{fdtNames[d]}={frameDtCounts[d]}");
                            BfsLog($"f={frame} DEATH_TYPES: {string.Join(" ", fdtParts)}");
                        }
#endif
                        // GravF death summary
                        if (_gravFDeathCounts != null && _gravFDeathFrame == frame)
                        {
                            var gdtNames = new[]{"UNK","CEIL_SPIKE","EJECT","CENTER","BALL_PROBE","BALL_VELZERO","BALL_EJECT","FLOOR_SPIKE","FWD","DEATH_COLL","BOUNDS","OOB_TOP","OOB_BOT"};
                            var gdtParts = new System.Collections.Generic.List<string>();
                            int gdtTotal = 0;
                            for (int d = 0; d < _gravFDeathCounts.Length; d++)
                                if (_gravFDeathCounts[d] > 0) { gdtParts.Add($"{gdtNames[d]}={_gravFDeathCounts[d]}"); gdtTotal += _gravFDeathCounts[d]; }
                            _log.WriteLine($"[GRAVF_SUMMARY] f={frame} total={gdtTotal} {string.Join(" ", gdtParts)}");
                            _gravFDeathCounts = null;
                            _gravFDeathSamples = 0;
                        }

                        // Emit speculative paths for live visualization
                        // Forward-simulate a sample of frontier states for ~30 frames
                        // to produce multi-point trajectory lines (matching heuristic behavior)
                        // (Moved outside shouldLog gate — see below)
                    }

                    // Emit speculative paths EVERY 5 frames (outside shouldLog gate)
                    // so BFS visualization density matches heuristic mode.
                    if (OnSpeculativePath != null && frontier.Count > 0 && frame % 5 == 0)
                    {
                        int sampleCount = Math.Min(8, frontier.Count);
                        int step = Math.Max(1, frontier.Count / sampleCount);
                        _speculativeDepth++;
                        for (int si = 0; si < frontier.Count && si / step < sampleCount; si += step)
                        {
                            var fs = frontier[si];
                            int hbW = GetHitboxW(fs.Mini);
                            // Forward-simulate with no press to trace trajectory
                            var traceSim = fs.Clone();
                            var tracePath = new List<(int x, int y)>();
                            int traceSurv = 0;
                            for (int tf = 0; tf < 30; tf++)
                            {
                                int tx = traceSim.X_fixed >> 8;
                                int ty = traceSim.Y_fixed >> 8;
                                tracePath.Add((tx + hbW / 2, ty + 8));
                                if (!StepFrame(ref traceSim, false, out bool endT) || endT) break;
                                traceSurv = tf + 1;
                            }
                            traceSim.ReturnAllSpriteResources();
                            if (tracePath.Count >= 2)
                                OnSpeculativePath(tracePath, 0, traceSurv, false);

                            // Also emit a "press" trajectory for diversity
                            var traceSimJ = fs.Clone();
                            var tracePathJ = new List<(int x, int y)>();
                            int traceSurvJ = 0;
                            for (int tf = 0; tf < 30; tf++)
                            {
                                int tx = traceSimJ.X_fixed >> 8;
                                int ty = traceSimJ.Y_fixed >> 8;
                                tracePathJ.Add((tx + hbW / 2, ty + 8));
                                bool inp = (tf == 0); // press on first frame, release after
                                if (!StepFrame(ref traceSimJ, inp, out bool endTJ) || endTJ) break;
                                traceSurvJ = tf + 1;
                            }
                            traceSimJ.ReturnAllSpriteResources();
                            if (tracePathJ.Count >= 2)
                                OnSpeculativePath(tracePathJ, 0, traceSurvJ, true);
                        }
                        _speculativeDepth--;
                    }
                }

                _speculativeDepth = 0;

                // --- Process results ---
                if (winFrame >= 0)
                {
                    // Reconstruct the input sequence by walking the parent chain backward
                    var inputs = new List<bool>();
                    int idx = winParentIdx; // index in frontier at start of winFrame
                    // Walk history backward: winFrame-1 down to 0
                    for (int f = winFrame - 1; f >= 0; f--)
                    {
                        inputs.Add(histInput[f][idx]);
                        idx = histParent[f][idx];
                    }
                    inputs.Reverse();
                    inputs.Add(winInputVal); // the winning frame's input

                    _log.WriteLine($"[BFS] Replaying winning path ({inputs.Count} frames)...");
                    ReplayBfsPath(inputs, startX_px, startY_px, startSpeedUiIndex,
                                  startGameMode, startGravFlipped, startMini);

                    int coinTotal = allCoins.Count;
                    double elapsed = bfsSw.Elapsed.TotalSeconds;
                    string msg = $"Completed in {inputs.Count} frames ({PathPoints.Count} path points)";
                    if (PreferCoins && coinTotal > 0) msg += $" [{winCoins}/{coinTotal} coins]";
                    msg += $" [{elapsed:F1}s BFS]";
                    ResultMessage = msg;
                    Success = true;
                    _log.WriteLine($"[BFS] {msg}");
                }
                else
                {
                    _speculativeDepth = 0; // ensure reset even on partial

                    // Replay best partial path
                    if (bestIdx >= 0 && histParent.Count > 0)
                    {
                        var inputs = new List<bool>();
                        int idx = bestIdx;
                        for (int f = bestFrame; f >= 0; f--)
                        {
                            inputs.Add(histInput[f][idx]);
                            idx = histParent[f][idx];
                        }
                        inputs.Reverse();
                        ReplayBfsPath(inputs, startX_px, startY_px, startSpeedUiIndex,
                                      startGameMode, startGravFlipped, startMini);
                    }
                    int bx = PathPoints.Count > 0 ? PathPoints[PathPoints.Count - 1].x : 0;
                    int bp = levelLengthPx > 0 ? bx * 100 / levelLengthPx : 0;
                    ResultMessage = $"BFS failed ~ best path to X={bx}px ({bp}%)";
                    Success = false;
                    _log.WriteLine($"[BFS] {ResultMessage}");
                    _log.WriteLine($"[BFS_DIAG] Step1 fires={_step1FireCount}, Step2 fires={_step2FireCount}");
#if !DISABLE_DEBUG_LOGGING
                    BfsLog($"RESULT: {ResultMessage}");
                    BfsLog($"DIAG: Step1 fires={_step1FireCount}, Step2 fires={_step2FireCount}");
#endif
                }
            }
            catch (Exception ex)
            {
                _speculativeDepth = 0;
                _log.WriteLine($"[BFS] EXCEPTION: {ex.GetType().Name}: {ex.Message}");
                _log.WriteLine(ex.StackTrace);
                ResultMessage = $"BFS crashed: {ex.GetType().Name}";
                Success = false;
#if !DISABLE_DEBUG_LOGGING
                try { System.IO.File.AppendAllText(pfDebugLogPath, $"[BFS] CRASHED: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n"); } catch { }
#endif
            }

            bfsSw.Stop();
        }

        /// <summary>
        /// Count coins collected in a SimState by checking ProcessedSprites
        /// against the allCoins list.
        /// </summary>
        private int CountBfsCoins(ref SimState s)
        {
            if (allCoins.Count == 0) return 0;
            int count = 0;
            foreach (var coin in allCoins)
            {
                if (s.ProcessedSprites.Contains(coin.Index))
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Replay a BFS-found input sequence through the real (non-speculative)
        /// simulation to generate PathPoints, Inputs, and collect final results.
        /// </summary>
        private void ReplayBfsPath(List<bool> inputSequence, int startX_px, int startY_px,
                                    int startSpeedUiIndex, int startGameMode,
                                    bool startGravFlipped, bool startMini)
        {
            int initCamY_rbp = ComputeInitCameraY(startY_px);
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
                GravityMod = 1.0,
                WasZeroedByCollision = true,
                OnGround = true,
                ProcessedSprites = NewSpriteSet(),
                PendingOrbIndex = -1,
                PendingOrbSpriteId = -1,
                NinjaJumps = (startGameMode == 8) ? NINJA_MAX_JUMPS : 0,
                CameraY_fixed = initCamY_rbp,
                TargetCameraY_fixed = initCamY_rbp
            };
            ApplyPortalsUpTo(ref state, startX_px);

            PathPoints.Clear();
            Path2Points.Clear();
            Inputs.Clear();
            _prevDualActiveForPath = false;
            _speculativeDepth = 0;
            _frameCounter = 0;
            TraceFrameOpen();

            for (int f = 0; f < inputSequence.Count; f++)
            {
                _frameCounter = f;
                bool inp = inputSequence[f];
                Inputs.Add(inp);

#if !DISABLE_DEBUG_LOGGING
                // Per-frame state dump for SIM comparison — log Y_fixed, VelY_fixed, mode, and input
                PfLog($"[REPLAY f={f}] X=0x{state.X_fixed:X} ({state.X_fixed >> 8}px) Y=0x{state.Y_fixed:X} ({state.Y_fixed >> 8}px) velY=0x{state.VelY_fixed:X} mode={state.GameMode} grav={(state.GravFlipped ? "FF" : "00")} mini={(state.Mini ? 1 : 0)} inp={(inp ? 1 : 0)}");
#endif

                int preStepGameMode = state.GameMode;
                _cubeJumpedThisStep = false;
                bool alive = StepFrame(ref state, inp, out bool endLevel);

                // Fix 23: Post-hoc input correction for cube mode.
                // The BFS may have produced True inputs for frames where the cube
                // doesn't actually jump (e.g. Orbed blocks the jump, or the cube
                // is airborne). Correct these to False so the SIM replay matches.
                // Fix 31: Don't clear the input if a mode-change portal fired
                // during this frame — the press was consumed by the NEW mode
                // (e.g. UFO jump on a cube→UFO transition frame).
                // Fix 36: Don't clear input during dashing.
                if (preStepGameMode == 0 && inp && !_cubeJumpedThisStep
                    && state.GameMode == 0 && state.Dashing == 0)
                {
                    Inputs[f] = false;
                }

                TraceFrame(f, ref state, Inputs[f], alive);

                int pathMiniOffY = state.Mini ? 4 : 0;
                PathPoints.Add(((state.X_fixed >> 8) + 8,
                                (state.Y_fixed >> 8) + pathMiniOffY + 8));
                if (state.DualActive)
                {
                    if (!_prevDualActiveForPath && Path2Points.Count > 0)
                        Path2Points.Add((-1, -1)); // sentinel: start new segment
                    int p2MiniOffY = state.P2_Mini ? 4 : 0;
                    Path2Points.Add(((state.X_fixed >> 8) + 8,
                                     (state.P2_Y_fixed >> 8) + p2MiniOffY + 8));
                }
                _prevDualActiveForPath = state.DualActive;

                if (endLevel || !alive) break;
            }

            // Gather collected coins
            if (PreferCoins && allCoins.Count > 0)
            {
                var collected = new HashSet<int>();
                foreach (var coin in allCoins)
                {
                    if (state.ProcessedSprites.Contains(coin.Index))
                        collected.Add(coin.Index);
                }
                FinalCollectedCoinIndices = collected;
                _log.WriteLine($"[COIN_STATUS] collected_count={collected.Count} total={allCoins.Count} collected_indices=[{string.Join(",", collected)}]");
                foreach (var coin in allCoins)
                {
                    bool hit = collected.Contains(coin.Index);
                    string status = hit ? "YES" : "NO";
                    _log.WriteLine($"[COIN_RESULT] idx={coin.Index} status={status}");
                }
            }

            ExtractSkippedPads(state);
            TraceFrameClose();
        }

        // -------------------------------------------------------------------
        //  BEST-PATH TRACKING (for partial results on fail/cancel/timeout)
        // -------------------------------------------------------------------

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
                _bestPath2Points = new List<(int x, int y)>(Path2Points);
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
                Path2Points.Clear();
                Path2Points.AddRange(_bestPath2Points);
                Inputs.Clear();
                Inputs.AddRange(_bestInputs);
            }
        }

        // -------------------------------------------------------------------
        //  BACKTRACKING
        // -------------------------------------------------------------------

        /// <summary>
        /// On death, rewind to the most recent checkpoint that still has
        /// untried alternatives.  Each checkpoint offers up to 3 alternatives:
        ///   Stage 1 � "no jump": skip the jump entirely, suppress until airborne.
        ///   Stage 2 � "toggle hold": flip hold-jump on/off.
        ///   Stage 3 � "opposite bias": retry with the opposite timing bias
        ///             (earliest?latest or vice versa).
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
                // First backtrack from this death � record the death frame
                // and original death info for stable maxStages evaluation.
                _btDeathFrame = frame;
                _btOrigDeathReason = _lastDeathReason;
                _btOrigDeathY = _lastDeathY;
                _backtrackTimer = System.Diagnostics.Stopwatch.StartNew();
            }
            _backtrackActive = true;
            int deathGameMode = state.GameMode; // capture death mode before any restoration
            // Only expand limits when the CURRENT death is a coin miss,
            // not when _missedCoinIdx is stale from a prior section.
            bool isCoinRetryDeath = (_missedCoinIdx >= 0 && _lastDeathReason == "MISSED_COIN");

            while (_backtrackCheckpoints.Count > 0 &&
                   _backtrackAttempts < (isCoinRetryDeath ? 1500 : MAX_BACKTRACK_ATTEMPTS) &&
                   _totalBacktrackAttempts < MAX_TOTAL_BACKTRACK_ATTEMPTS &&
                   (_backtrackTimer == null || _backtrackTimer.Elapsed.TotalSeconds < (isCoinRetryDeath ? MAX_BACKTRACK_SECONDS_COIN : MAX_BACKTRACK_SECONDS)))
            {
                int last = _backtrackCheckpoints.Count - 1;
                var cp = _backtrackCheckpoints[last];

                // Allow cross-mode backtracking when the checkpoint is
                // close to the death.  Mode transitions often need the player
                // to undo the last few decisions from the prior mode.
                // COIN RETRY: use a much larger limit (2000 frames) because
                // ship-mode coins may be far from the cube?ship portal �
                // the backtracker needs to reach cube checkpoints to change
                // the ship entry altitude.
                if (cp.GameMode != deathGameMode)
                {
                    int frameDist = _btDeathFrame - cp.Frame;
                    int crossModeLimit = isCoinRetryDeath ? 2000 : 500;
                    if (frameDist > crossModeLimit)
                    {
                        _backtrackCheckpoints.RemoveAt(last);
                        continue;
                    }
                    if (isCoinRetryDeath && cp.RetryStage == 0)
                        _log.WriteLine($"[CROSSMODE_BT] cpMode={cp.GameMode} deathMode={deathGameMode} frameDist={frameDist} attempts={_backtrackAttempts}");
                }

                // SHIP COIN ESCALATION: After 3 failed attempts to collect a
                // ship-mode coin, skip NEARBY same-mode checkpoints (within 200
                // frames of death) � those are past the fork and minor tweaks
                // won't help.  Keep DISTANT ship checkpoints (near the fork
                // entrance) where trajectory changes can route the ship to the
                // coin's corridor.
                if (isCoinRetryDeath && cp.GameMode == deathGameMode
                    && (deathGameMode == 1 || deathGameMode == 3))
                {
                    _coinMissRetryCount.TryGetValue(allCoins[_missedCoinIdx].Index, out int mc);
                    if (mc >= 3)
                    {
                        int frameDist3 = _btDeathFrame - cp.Frame;
                        if (frameDist3 < 200)
                        {
                            // Near death � past the fork, skip quickly
                            _backtrackCheckpoints.RemoveAt(last);
                            continue;
                        }
                        // Keep fork-area and earlier checkpoints
                    }
                }

                cp.RetryStage++;

                // Ship/UFO get 4 stages (bias �, force hold, force release).
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
                // level won't help clear a wall above � the backtracker must
                // reach elevated checkpoints where height matters.
                int maxStages;
                if (cp.GameMode == 1 || cp.GameMode == 3)
                {
                    if (isCoinRetryDeath)
                    {
                        // Ship/UFO coin retry: distance-adaptive staging.
                        // The coin's path typically branches off well before
                        // the coin itself.  Checkpoints near the fork entrance
                        // (300-700 frames from death) get full 16-stage
                        // exploration; nearby ones (minor tweaks) get few.
                        int frameDist4 = _btDeathFrame - cp.Frame;
                        if (frameDist4 < 150)
                            maxStages = 2;   // near death: suppress only
                        else if (frameDist4 < 300)
                            maxStages = 4;   // moderate: bias � and force
                        else if (frameDist4 < 700)
                            maxStages = 23;  // fork area: full exploration incl. bias combos
                        else
                            maxStages = 4;   // before fork: moderate
                    }
                    else
                    {
                        int frameDist5 = _btDeathFrame - cp.Frame;
                        if (frameDist5 <= 60)
                            maxStages = 23;  // nearby: full ship exploration incl. bias combos
                        else if (frameDist5 <= 240)
                            maxStages = 8;   // moderate distance
                        else
                            maxStages = 4;   // distant
                    }
                }
                else if (cp.GameMode == 2 || cp.GameMode == 5 || cp.GameMode == 7)
                    maxStages = 23;
                else
                {
                    int frameDist2 = _btDeathFrame - cp.Frame;
                    // ELEVATION-AWARE FAST SKIP: When death was FWD_DEATH and
                    // checkpoint is at the same floor level as death, trying
                    // different inputs won't help � the player needs to be
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
                    // won't help � we need to reach distant checkpoints where the
                    // player can take a different (higher) route. Cap nearby stages.
                    else if (isCoinRetryDeath)
                    {
                        var mc = allCoins[_missedCoinIdx];
                        int coinCenterY = (mc.HitTop + mc.HitBottom) / 2;
                        bool coinAbove = (cpY - coinCenterY) > 40; // coin is above player by 40+px
                        if (coinAbove && frameDist2 <= 90)
                            maxStages = 4; // fast-skip: only try core strategies near floor
                        else if (frameDist2 <= 60)
                            maxStages = 23; // full exploration for nearby checkpoints
                        else
                            maxStages = 23; // distant: include bias combo stages
                    }
                    else if (frameDist2 <= 60)
                        maxStages = 23;  // within ~1 second: full exploration
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
                // Invalidate permanently collected coins that were collected
                // AFTER this checkpoint � those coins were on the now-truncated
                // path and the new path may not reach them.  Always invalidate
                // regardless of whether this is a coin retry or regular death,
                // because the PATH is being rewritten and must honestly reflect
                // which coins are actually collected on it.
                if (PreferCoins && _permanentlyCollectedCoins.Count > 0)
                {
                    var staleCoins = new List<int>();
                    foreach (var kv in _permanentlyCollectedCoins)
                    {
                        if (kv.Value >= cp.Frame)
                            staleCoins.Add(kv.Key);
                    }
                    foreach (int stale in staleCoins)
                    {
                        _permanentlyCollectedCoins.Remove(stale);
                        // Track collect-then-lose: coin was collected but the
                        // path died, forcing backtrack to invalidate it.
                        _coinCollectThenLoseCount.TryGetValue(stale, out int loseCount);
                        loseCount++;
                        _coinCollectThenLoseCount[stale] = loseCount;
                        if (loseCount >= MAX_COIN_COLLECT_LOSE && !_forgivenCoins.Contains(stale))
                        {
                            // Auto-forgive: coin is physically reachable but
                            // the ship can't survive afterward.
                            _forgivenCoins.Add(stale);
                            _autoForgivenCoins.Add(stale);
                            // Determine the game mode from the coin's allCoins entry
                            for (int ci2 = 0; ci2 < allCoins.Count; ci2++)
                            {
                                if (allCoins[ci2].Index == stale)
                                {
                                    _forgivenCoinGameModes[stale] = state.GameMode;
                                    break;
                                }
                            }
                            _log.WriteLine($"[COIN_COLLECT_LOSE_FORGIVEN] idx={stale} gm={state.GameMode} loseCount={loseCount}");
#if !DISABLE_DEBUG_LOGGING
                            PfLog($"[COIN_COLLECT_LOSE_FORGIVEN] idx={stale} loseCount={loseCount} � collected {MAX_COIN_COLLECT_LOSE}x but path always dies");
#endif
                        }
                    }
                    // DEEP RECOVERY: On first collect-lose for a non-forgiven
                    // ship-mode coin, inject the ship entry checkpoint for a
                    // deep backtrack.  This gives 700+ px of biased flight
                    // (via _coinCollectThenLoseCount ? biasPixels shift in
                    // DecideShipInput) to diverge the trajectory, avoiding the
                    // need for a full COIN_RETRY_SHIP level replay.
                    if (deathGameMode == 1 && _shipEntryRecoveryCheckpoint != null)
                    {
                        bool needDeep = false;
                        foreach (int stale in staleCoins)
                        {
                            if (!_forgivenCoins.Contains(stale)
                                && _coinCollectThenLoseCount.TryGetValue(stale, out int cl2)
                                && cl2 > 0
                                && _shipEntryRecoveryCheckpoint.Frame < cp.Frame - 50)
                            {
                                needDeep = true;
                                break;
                            }
                        }
                        if (needDeep)
                        {
                            var src = _shipEntryRecoveryCheckpoint;
                            var recovery = new BacktrackCheckpoint
                            {
                                Frame = src.Frame,
                                State = src.State.Clone(),
                                HoldJumpState = src.HoldJumpState,
                                HoldDelayState = src.HoldDelayState,
                                CommittedDelayState = src.CommittedDelayState,
                                PathPointCount = src.PathPointCount,
                                InputCount = src.InputCount,
                                RetryStage = 0,
                                UsedBias = src.UsedBias,
                                ShipBias = src.ShipBias,
                                GameMode = src.GameMode,
                                ShipForceHold = src.ShipForceHold,
                                ShipForceRelease = src.ShipForceRelease,
                                ShipCommitFrames = src.ShipCommitFrames,
                                ShipCommitHold = src.ShipCommitHold,
                                ForceJumpRemaining = src.ForceJumpRemaining,
                                SkipAllOrbs = src.SkipAllOrbs,
                                SkipSpecificOrbs = new HashSet<int>(src.SkipSpecificOrbs ?? new()),
                                SkipSpecificPads = new HashSet<int>(src.SkipSpecificPads ?? new()),
                                PrevFrameWasGrounded = src.PrevFrameWasGrounded,
                                NextCoinCheckIdx = src.NextCoinCheckIdx,
                            };
                            _backtrackCheckpoints.Clear();
                            _backtrackCheckpoints.Add(recovery);
                            _backtrackAttempts = 0;
                            _log.WriteLine($"[COIN_DEEP_RECOVERY] injecting ship entry frame={recovery.Frame} mode={recovery.GameMode}");
                            return TryBacktrack(ref state, ref frame);
                        }
                    }
                    // Re-add coins collected BEFORE the checkpoint
                    foreach (var kv in _permanentlyCollectedCoins)
                        state.ProcessedSprites.Add(kv.Key);
                }
                _cubeHoldJump = cp.HoldJumpState;
                _cubeHoldDelay = cp.HoldDelayState;
                _committedJumpDelay = cp.CommittedDelayState; // restore committed delay state
                _committedRobotHold = 0; // reset robot hold on backtrack
                _committedNinjaJumps.Clear(); // reset ninja jump plan on backtrack
                _ninjaWaitFrames = 0;
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
                _nextCoinCheckIdx = cp.NextCoinCheckIdx;
                _coinInputScript.Clear();
                _coinInputScriptCoinIdx = -1;
                // Reset beam search tracking on backtrack � different backtrack
                // stages change ship altitude/velocity, leading to different beam
                // trajectories. Without this, the beam only gets one shot from
                // the initial forward pass.
                _beamSearchAttemptedCoinIdx = -1;
                _beamSearchLastDistX = int.MaxValue;

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

                // Remove all checkpoints after the restored one � they
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

            // CROSS-MODE INJECTION: If all checkpoints are exhausted or the
            // attempt limit was hit, inject the saved pre-ship transition checkpoint.
            // This checkpoint survives FIFO eviction and gives the backtracker access
            // to the last cube/ball decision point before the ship portal.
            // Only triggers when the CURRENT death is MISSED_COIN � NOT stale
            // _missedCoinIdx from a previous section.  Without the death-reason
            // check, a terrain death in ship mode (while _missedCoinIdx is still
            // set from a ball-section miss) would trigger injection, creating an
            // infinite loop that prevents the COIN_FORGIVEN_ALT recovery path.
            if (_lastCubeToShipCheckpoint != null
                && isCoinRetryDeath)
            {
                var injected = _lastCubeToShipCheckpoint;
                _lastCubeToShipCheckpoint = null; // consume � prevents infinite recursion
                injected.RetryStage = 0;
                // Clear remaining checkpoints and reset attempt counter so the
                // injected checkpoint gets a full budget of retries.
                _backtrackCheckpoints.Clear();
                _backtrackCheckpoints.Add(injected);
                _backtrackAttempts = 0;
                _log.WriteLine($"[CROSSMODE_INJECT] frame={injected.Frame} mode={injected.GameMode} attempts={_backtrackAttempts}");
                return TryBacktrack(ref state, ref frame);
            }

            _backtrackActive = false;
            return false;
        }

        // -------------------------------------------------------------------
        //  DECISION LOGIC
        // -------------------------------------------------------------------

        private bool DecideInput(SimState state)
        {
            // -- Backtrack override: handle for ALL game modes before
            //    mode-specific logic.  Without this, ship mode bypasses
            //    all override handling and the backtracker can't alter
            //    ship decisions. --
            bool isOverrideFrame = (_btOverrideFrame == _frameCounter && _speculativeDepth == 0);
            if (isOverrideFrame)
            {
                int stage = _btOverrideStage;
                _btOverrideFrame = -1; // consume override

                if (state.GameMode == 1) // Ship mode overrides
                {
                    // Ship backtrack: 16 stages per checkpoint when retrying for
                    // a missed coin, 4 otherwise.  Coin retries use corridor
                    // biases, force inputs, and sine-wave patterns to navigate
                    // through alternate corridors and exploit ship velocity arcs.
                    int dist = _btOverrideDistFromDeath;
                    int forceDuration = Math.Max(4, dist / 3);
                    int biasAmount = Math.Max(24, Math.Min(72, dist));
                    _shipForceReleaseFirstFrames = 0;
                    _shipForceHoldFrames = 0;
                    _shipForceReleaseFrames = 0;
                    
                    if (_missedCoinIdx >= 0 && _btOrigDeathReason == "MISSED_COIN" && _missedCoinIdx < allCoins.Count)
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
                        // Mix corridor bias, force-hold/release, and sine-wave
                        // stages. Force inputs physically commit the ship to a
                        // different altitude, bypassing corridor geometry clamping.
                        // Sine-wave stages (9-16) use release-first to dip toward
                        // the coin, then hold to recover � exploiting the ship's
                        // velocity-based arc capabilities.
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
                            // -- Sine-wave stages: dip toward coin then recover --
                            // These create hold?release or release?hold sequences
                            // that exploit the ship's velocity for wide arcs.
                            case 9:
                                // Short sine wave toward coin
                                if (coinDir > 0) { _shipForceReleaseFirstFrames = forceDur; _shipForceHoldFrames = forceDur; }
                                else { _shipForceHoldFrames = forceDur; _shipForceReleaseFrames = forceDur; }
                                _shipCorridorBias = coinDir * 100;
                                break;
                            case 10:
                                // Medium sine wave toward coin
                                if (coinDir > 0) { _shipForceReleaseFirstFrames = forceDur * 2; _shipForceHoldFrames = forceDur; }
                                else { _shipForceHoldFrames = forceDur * 2; _shipForceReleaseFrames = forceDur; }
                                _shipCorridorBias = coinDir * 150;
                                break;
                            case 11:
                                // Long sine wave toward coin
                                if (coinDir > 0) { _shipForceReleaseFirstFrames = forceDur * 2; _shipForceHoldFrames = forceDur * 2; }
                                else { _shipForceHoldFrames = forceDur * 2; _shipForceReleaseFrames = forceDur * 2; }
                                _shipCorridorBias = coinDir * 200;
                                break;
                            case 12:
                                // Deep dip: long toward, short recover
                                if (coinDir > 0) { _shipForceReleaseFirstFrames = forceDur * 3; _shipForceHoldFrames = forceDur; }
                                else { _shipForceHoldFrames = forceDur * 3; _shipForceReleaseFrames = forceDur; }
                                _shipCorridorBias = coinDir * 250;
                                break;
                            case 13:
                                // Very deep dip with strong recovery
                                if (coinDir > 0) { _shipForceReleaseFirstFrames = forceDur * 3; _shipForceHoldFrames = forceDur * 3; }
                                else { _shipForceHoldFrames = forceDur * 3; _shipForceReleaseFrames = forceDur * 3; }
                                _shipCorridorBias = coinDir * 300;
                                break;
                            case 14:
                                // Quick dip: short toward, long recover
                                if (coinDir > 0) { _shipForceReleaseFirstFrames = forceDur; _shipForceHoldFrames = forceDur * 2; }
                                else { _shipForceHoldFrames = forceDur; _shipForceReleaseFrames = forceDur * 2; }
                                _shipCorridorBias = coinDir * 80;
                                break;
                            case 15:
                                // Maximum force: very long dip
                                if (coinDir > 0) { _shipForceReleaseFirstFrames = forceDur * 4; _shipForceHoldFrames = forceDur * 2; }
                                else { _shipForceHoldFrames = forceDur * 4; _shipForceReleaseFrames = forceDur * 2; }
                                _shipCorridorBias = coinDir * 350;
                                break;
                            case 16:
                                // Opposite sine wave (some coins need to go around)
                                if (coinDir > 0) { _shipForceHoldFrames = forceDur * 2; _shipForceReleaseFrames = forceDur * 2; }
                                else { _shipForceReleaseFirstFrames = forceDur * 2; _shipForceHoldFrames = forceDur * 2; }
                                _shipCorridorBias = -coinDir * 200;
                                break;
                        }
                    }
                    else
                    {
                        // Normal ship backtrack (no coin): 12 stages.
                        // Stages 1-4: small bias + force adjustments.
                        // Stages 5-8: larger bias swings (±120-300px).
                        // Stages 9-12: sine-wave force patterns + large bias.
                        int forceDur = Math.Max(6, dist / 2);
                        switch (stage)
                        {
                            case 1: _shipCorridorBias = -biasAmount; break;
                            case 2: _shipCorridorBias = biasAmount; break;
                            case 3: _shipForceHoldFrames = forceDuration; break;
                            case 4: _shipForceReleaseFrames = forceDuration; break;
                            case 5: _shipCorridorBias = -120; _shipForceHoldFrames = forceDur; break;
                            case 6: _shipCorridorBias = 120; _shipForceReleaseFrames = forceDur; break;
                            case 7: _shipCorridorBias = -200; _shipForceHoldFrames = forceDur * 2; break;
                            case 8: _shipCorridorBias = 200; _shipForceReleaseFrames = forceDur * 2; break;
                            case 9:
                                _shipForceReleaseFirstFrames = forceDur; _shipForceHoldFrames = forceDur;
                                _shipCorridorBias = -150; break;
                            case 10:
                                _shipForceHoldFrames = forceDur; _shipForceReleaseFrames = forceDur;
                                _shipCorridorBias = 150; break;
                            case 11: _shipCorridorBias = -300; _shipForceHoldFrames = forceDur * 3; break;
                            case 12: _shipCorridorBias = 300; _shipForceReleaseFrames = forceDur * 3; break;
                        }
                    }
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[BACKTRACK_OVERRIDE_SHIP] stage={stage} dist={dist}: bias={_shipCorridorBias} forceRelFirst={_shipForceReleaseFirstFrames} forceHold={_shipForceHoldFrames} forceRel={_shipForceReleaseFrames} coinRetry={_missedCoinIdx >= 0}");
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

                // Spider mode overrides (same set as ball — gravity flip timing)
                if (state.GameMode == 5)
                {
                    switch (stage)
                    {
                        case 1: _btSuppressJumpUntilAirborne = true; return false;
                        case 2: return true;
                        case 3: JumpTimingBias = 1.0 - JumpTimingBias; break;
                        case 4: JumpTimingBias = 1.0 - JumpTimingBias; _btSuppressJumpUntilAirborne = true; return false;
                        case 5: JumpTimingBias = 1.0 - JumpTimingBias; return true;
                        case 6: _committedJumpDelay = 5; return false;
                        case 7: _committedJumpDelay = 10; return false;
                        case 8: _committedJumpDelay = 15; return false;
                        case 9: _committedJumpDelay = 20; return false;
                        case 10: _committedJumpDelay = 25; return false;
                    }
                    return DecideSpiderInput(state, true);
                }

                // Swing mode overrides (same set as ball — gravity flip timing)
                if (state.GameMode == 7)
                {
                    switch (stage)
                    {
                        case 1: _btSuppressJumpUntilAirborne = true; return false;
                        case 2: return true;
                        case 3: JumpTimingBias = 1.0 - JumpTimingBias; break;
                        case 4: JumpTimingBias = 1.0 - JumpTimingBias; _btSuppressJumpUntilAirborne = true; return false;
                        case 5: JumpTimingBias = 1.0 - JumpTimingBias; return true;
                        case 6: _committedJumpDelay = 5; return false;
                        case 7: _committedJumpDelay = 10; return false;
                        case 8: _committedJumpDelay = 15; return false;
                        case 9: _committedJumpDelay = 20; return false;
                        case 10: _committedJumpDelay = 25; return false;
                    }
                    return DecideSwingInput(state, true);
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
                else if (stage == 20) // Intermediate bias 0.25
                {
                    JumpTimingBias = 0.25;
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[BACKTRACK_OVERRIDE] stage=20: bias=0.25");
#endif
                    // Fall through to normal decision
                }
                else if (stage == 21) // Intermediate bias 0.75
                {
                    JumpTimingBias = 0.75;
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[BACKTRACK_OVERRIDE] stage=21: bias=0.75");
#endif
                    // Fall through to normal decision
                }
                else if (stage == 22) // Bias 0.0 + suppress
                {
                    JumpTimingBias = 0.0;
                    _btSuppressJumpUntilAirborne = true;
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[BACKTRACK_OVERRIDE] stage=22: bias=0.0 + suppress");
#endif
                    return false;
                }
                else if (stage == 23) // Bias 1.0 + suppress
                {
                    JumpTimingBias = 1.0;
                    _btSuppressJumpUntilAirborne = true;
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[BACKTRACK_OVERRIDE] stage=23: bias=1.0 + suppress");
#endif
                    return false;
                }
                // Stages 24+ reserved.
            }

            // ---------------------------------------------------------------
            //  Coin input script playback (ALL game modes)
            //  When a coin collection script was recorded (for ship threading,
            //  cube Strategy 0, etc.), play it back verbatim.
            // ---------------------------------------------------------------
            if (_coinInputScript.Count > 0 && _speculativeDepth == 0)
            {
                bool scriptInput = _coinInputScript.Dequeue();
#if !DISABLE_DEBUG_LOGGING
                PfLog($"[COIN_SCRIPT] frame={_frameCounter} remaining={_coinInputScript.Count} input={scriptInput} gameMode={state.GameMode}");
#endif
                return scriptInput;
            }

            // Dash hold/release: when dashing, override mode decision
            if (state.Dashing != 0)
                return DecideDashHold(state);

            if (state.GameMode == 1) return DecideWithBiasFallback(state, isOverrideFrame);
            if (state.GameMode == 2) return DecideBallInput(state, isOverrideFrame);
            if (state.GameMode == 3) return DecideWithBiasFallback(state, isOverrideFrame);
            if (state.GameMode == 4) return DecideWithBiasFallback(state, isOverrideFrame);
            if (state.GameMode == 5) return DecideSpiderInput(state, isOverrideFrame);
            if (state.GameMode == 6) return DecideWithBiasFallback(state, isOverrideFrame);
            if (state.GameMode == 7) return DecideSwingInput(state, isOverrideFrame);
            if (state.GameMode == 8) return DecideNinjaInput(state, isOverrideFrame);
            if (state.GameMode != 0) return false;

            // ---------------------------------------------------------------
            //  Coin input script playback (cube/legacy � handled above for all modes)
            // ---------------------------------------------------------------

            // Pads fire on collision � no skip/decide logic needed.
            // The PF cannot suppress pad activation; if the player overlaps
            // a pad, it MUST activate (matching NES/sim behavior).

            // -- Cube orb decision: evaluate "hit" vs "skip" for overlapping orbs --
            // Compare by X-progress (distance traveled) to avoid gravity-portal-bonus
            // distortion.  If hitting gets further, return true to activate.
            // If skipping gets further, pre-add to ProcessedSprites and return false.
            if (!isOverrideFrame && !_btSuppressJumpUntilAirborne)
            {
                int cubeOrbSid = ScanForOrbOverlap(state, out int cubeOrbIndex);
                if (cubeOrbSid >= 0)
                {
                    // -- Coin-retry orb deferral: when altitude penalties are active
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
                        PfLog($"[ORB_DEFER_PEAK] deferring orb 0x{cubeOrbSid:X2} idx={cubeOrbIndex} velY=0x{state.VelY_fixed:X4} Y={state.Y_fixed >> 8} - waiting for peak");
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
                        return false; // don't press input � skip the orb
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

                        // Evaluate "hit orb" path � jump at frame 0 with singleJumpOnly
                        // so only the orb activation occurs (no chain jumps/orbs).
                        int orbHitBestSurv = SimulateForwardWithJumpAt(state, 0, out int orbHitBestX,
                            singleJumpOnly: true);

                        // Evaluate "skip orb" path � never jump, orb pre-skipped.
                        var skipState = state.Clone();
                        skipState.ProcessedSprites.Add(cubeOrbIndex);
                        skipState.PendingOrbIndex = -1;
                        skipState.PendingOrbSpriteId = -1;
                        int orbSkipBestSurv = SimulateForwardWithJumpAt(skipState, -1, out int orbSkipBestX,
                            singleJumpOnly: true);

                        // -- Extended horizon tiebreaker --
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
                            PfLog($"[ORB_SKIP_CUBE] Skipping orb 0x{cubeOrbSid:X2} � skipX={orbSkipBestX} vs hitX={orbHitBestX}");
#endif
                            return false;
                        }
                        else
                        {
#if !DISABLE_DEBUG_LOGGING
                            PfLog($"[ORB_HIT_CUBE] Hitting orb 0x{cubeOrbSid:X2} � hitX={orbHitBestX} vs skipX={orbSkipBestX}");
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

            // -- Stage-2 "no-jump" suppression: persist until cube is airborne
            //    (walked off edge and is now falling). --
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
                        PfLog($"[BACKTRACK_OVERRIDE] no-jump suppression OVERRIDDEN � danger ahead, allowing jump");
#endif
                        // Fall through to normal decision logic instead of returning false
                    }
                    else
                    {
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[BACKTRACK_OVERRIDE] no-jump suppression active (grounded)");
#endif
                        return false; // don't jump � wait to fall off edge
                    }
                }
            }

            // -- Backtrack force-jump window: when active, jump at every
            //    grounded opportunity UNLESS jumping would immediately die.
            //    This overrides the BFS to chain-jump through maze-like
            //    platforming sections where the BFS can't see far enough
            //    to plan a global path. --
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
                        return false; // walk � jump would die
                    }
                }
                // If airborne, don't decrement � just fall through to normal logic
            }

            // -- Backtrack override was already handled at the top of
            //    DecideInput (before the ship early-return).  If we reach
            //    here, the override was consumed and we fall through to
            //    normal decision logic. --

            // Orbed flag blocks jumping (set by J_BLOCK, orbs, S_BLOCK).
            // Must release input first to clear orbed, then we can jump next frame.
            if (state.Orbed)
                return false;

            // Can only jump when velY == 0 (matching real game's jump check)
            if (state.VelY_fixed != 0)
            {
                // Airborne: decrement committed delay (backtrack overrides only)
                if (_committedJumpDelay > 0)
                    _committedJumpDelay--;

                // -- LANDING-FRAME JUMP (NES MATCH) --
                // On the NES, holding A provides input=true EVERY frame,
                // including the frame the player lands.  In cube_movement:
                //   common_gravity_routine ? cube_eject (VelY?0) ? cube_do_jump.
                // If A is held and VelY==0 after eject, cube_do_jump fires
                // on the SAME frame as landing � zero wasted frames.
                //
                // The PF decides input BEFORE StepFrame runs, so when VelY>0
                // (falling, pre-eject), it used to return false � causing the
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
                    PfLog($"[DECIDE_CUBE] committed delay fired � jumping now");
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
                PfLog($"[DECIDE_CUBE] committed delay fired (was pending from airborne) � jumping now");
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

            // ---------------------------------------------------------------
            //  Force-walk zone override (coin retry only)
            //  When retrying for coins, force WALK when grounded near a pad
            //  that leads to a coin. Prevents the BFS from jumping over
            //  yellow pads (coin launch) or blue pads (gravity flip).
            //  ALSO prevents jump buffering when airborne and falling near
            //  the pad � ensures the player descends to the pad's Y level
            //  instead of bouncing off intermediate platforms.
            //  Highest priority � overrides coin-jump and BFS decisions.
            // ---------------------------------------------------------------
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
                            PfLog($"[FORCE_WALK] Player at X={playerX_fw} in force-walk zone ({fwStart},{fwEnd}) � forcing WALK");
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
                            PfLog($"[FORCE_WALK] Player at X={playerX_fw} Y={state.Y_fixed >> 8} FALLING in force-walk zone � suppressing jump buffer");
#endif
                            return false;
                        }
                    }
                }
            }

            // ---------------------------------------------------------------
            //  Ground-level bias zone (coin retry only)
            //  When retrying for coins, keep the player at ground level by
            //  preferring WALK. Only allows jumping when walking would die
            //  within 3 frames (spike ahead). This forces the BFS to take
            //  ground-level routes instead of climbing staircases/platforms.
            //  Still allows jumping over spikes/obstacles at ground level.
            // ---------------------------------------------------------------
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
                            if (!testWalk.OnGround) break; // walked off edge ? stop
                        }
                        _speculativeDepth--;

                        if (!walkDiesSoon)
                        {
#if !DISABLE_DEBUG_LOGGING
                            PfLog($"[GROUND_BIAS] X={playerX_gb} in bias zone ? WALK (safe)");
#endif
                            return false; // WALK � stay at ground level
                        }
                        // Walk would die ? let BFS/coin-override decide (may jump)
                        break;
                    }
                }
            }

            // ---------------------------------------------------------------
            //  CUBE BEAM SEARCH FOR ELEVATED COINS
            //  When a cube-mode coin is significantly above the player's
            //  normal jump reach (~34px), the standard coin-jump strategies
            //  (walk, always-jump, single-jump) fail. This beam search
            //  explores all jump/walk combinations over a long horizon to
            //  find a path through elevated terrain that collects the coin.
            //  Similar in concept to the ship beam search but adapted for
            //  binary grounded jump/walk decisions.
            // ---------------------------------------------------------------
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
                            // Determine choices: pending orb ? must activate;
                            // grounded or about-to-land ? jump or walk; airborne ? no choice
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
                                int hbOff_s = GetHitboxOffsetY(sim.GameMode, sim.Mini, sim.GravFlipped);
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

            // ---------------------------------------------------------------
            //  Coin-jump override (cube mode only)
            //  When PreferCoins is active, check if jumping NOW would
            //  collect an uncollected coin ahead.  Simulate a jump
            //  trajectory and check for hitbox overlap with each nearby
            //  coin.  If the jump collects a coin AND survives the BFS
            //  horizon, force the jump � don't bother with the full BFS.
            // ---------------------------------------------------------------
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
                    // 0. "Walk only" � pads auto-activate and can launch through coin
                    // 1. "Always jump + activate orbs" � for coins above (pad?orb chains)
                    // 2. "Single jump + fall freely" � for coins below (arc descent)
                    _speculativeDepth++;
                    var jumpSim = state.Clone();
                    bool coinCollected = false;
                    int jumpSurvival = 0;
                    int coinCollFrame = -1;
                    const int COIN_JUMP_HORIZON = 120;

                    // Strategy 0: walk only (no jumping) � pads launch player through coins.
                    // This is the most reliable path for ground-level coins with
                    // pads underneath. Walking straight to the pad is deterministic.
                    // Uses extended horizon (200) to reach distant pads.
                    // Strategy 0: walk-preferred with obstacle jumping at ground level.
                    // Walks by default. When grounded at ground level and walking
                    // would die (death tile ahead), jumps instead. This handles
                    // the pattern: descend from staircase ? jump over ground
                    // obstacle ? arc passes through coin (or land on pad ? launch).
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
                                // At ground level � check if walking (no jump) would die
                                var testState = walkSim0.Clone();
                                bool testAlive = StepFrame(ref testState, false, out _);
                                input = !testAlive; // jump if walk dies
                            }
                            else
                                input = false; // elevated or airborne ? just walk/fall

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
                                int hbOffY = GetHitboxOffsetY(walkSim0.GameMode, walkSim0.Mini, walkSim0.GravFlipped);
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
                            PfLog($"[COIN_WALK_S0] Forcing {(firstFrameInput?"JUMP":"WALK")}: walk?pad?coin idx={coin.Index} sid=0x{coin.SpriteId:X2} at ({coin.HitLeft},{coin.HitTop})-({coin.HitRight},{coin.HitBottom}) walkSurv={walkSurv0}");
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
                            int hbOffY = GetHitboxOffsetY(jumpSim.GameMode, jumpSim.Mini, jumpSim.GravFlipped);
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
                            // Activate orbs mid-air (for pad?orb chains after landing)
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
                                int hbOffY = GetHitboxOffsetY(jumpSim.GameMode, jumpSim.Mini, jumpSim.GravFlipped);
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

            // ---------------------------------------------------------------
            //  Coin-walk override (cube mode only)
            //  When PreferCoins is active and a coin is BELOW the player,
            //  simulate walking off the current platform (never jumping)
            //  and check if the falling trajectory passes through the coin.
            //  This handles the common pattern of descending from an
            //  elevated position to collect a coin mid-fall.
            //  Pads are auto-activated by StepFrame, so pad?coin paths work.
            // ---------------------------------------------------------------
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
                    if (coinCenterY <= playerY) break; // coin is above or level � skip

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
                        // Never jump � just walk/fall. Activate orbs if pending
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
                            int hbOffY = GetHitboxOffsetY(walkSim.GameMode, walkSim.Mini, walkSim.GravFlipped);
                            int pTop = (walkSim.Y_fixed >> 8) + hbOffY;
                            int pBot = pTop + hbH;
                            int pRight = nesX + hbW;
                            bool xOv = !(pRight < coin.HitLeft || coin.HitRight < nesX);
                            bool yOv = !(pBot < coin.HitTop || coin.HitBottom < pTop);
                            if (xOv && yOv) { coinCollected = true; coinCollectedFrame = f; }
                        }
                    }
                    _speculativeDepth--;
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

            // ---------------------------------------------------------------
            //  BFS frame-by-frame pathfinding
            //  Instead of testing 35 pre-set delays and ranking them,
            //  explore every possible jump/nojump choice at every grounded
            //  frame.  This naturally finds hold-jump patterns, delayed
            //  jumps, and any combination thereof without heuristic ranking.
            // ---------------------------------------------------------------

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

            // -- HOLD-JUMP FAST PATH --
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
            // HOLD-JUMP FAST PATH � DISABLED
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
            //   my:             current Y (pixels) at this state � used for terminal Y tracking
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
                // excluded from the jump/walk decision � their survival
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
                    // -- Orb-skip branching --
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

                        // -- Jump branch --
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

                        // -- Walk branch --
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
                    var deduped = new Dictionary<(int, int, bool, bool, int, bool, int, int, bool), (SimState s, bool j0, int lj, int tl, int my, bool os)>();
                    foreach (var n in alive)
                    {
                        var key = (n.s.Y_fixed, n.s.VelY_fixed, n.s.OnGround, n.s.GravFlipped, n.s.GameMode, n.j0,
                                   n.s.DualActive ? n.s.P2_Y_fixed : 0, n.s.DualActive ? n.s.P2_VelY_fixed : 0,
                                   n.s.DualActive && n.s.P2_GravFlipped);
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

            // -- Emit speculative paths for visualization --
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
            // (holdPattern = true), give it a tiebreaker bonus � this means the BFS found
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
            // spikes at Y=497) must NOT receive an elevation bonus � dying at
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
            // BELOW the current position � climbing is counterproductive.
            if (PreferCoins && elevBonus > 0)
            {
                int playerX2 = state.X_fixed >> 8;
                int playerY2 = state.Y_fixed >> 8;
                for (int ci2 = _nextCoinCheckIdx; ci2 < allCoins.Count; ci2++)
                {
                    var coin2 = allCoins[ci2];
                    if (state.ProcessedSprites.Contains(coin2.Index) || _forgivenCoins.Contains(coin2.Index))
                        continue;
                    if (coin2.HitLeft > playerX2 + 1500) break;
                    if (coin2.HitRight < playerX2) continue;
                    int coinCenterY2 = (coin2.HitTop + coin2.HitBottom) / 2;
                    if (coinCenterY2 > playerY2) // coin is below player
                        elevBonus = 0; // don't reward climbing away from the coin
                    break;
                }
            }

            // -- Coin proximity bonus --
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
                    if (coin.HitLeft > playerX + 1500) break; // 1500px lookahead
                    if (coin.HitRight < playerX) continue;    // already passed
                    cubeCoinTargetY = (coin.HitTop + coin.HitBottom) / 2;
                    int coinXDist = Math.Max(0, coin.HitLeft - playerX);
                    double proximityScale = Math.Max(0.1, 1.0 - (double)coinXDist / 1500.0);
                    // For coins ABOVE the player (lower Y): if a path overshoots
                    // past the coin (minY < coinY), it passed through the coin's
                    // Y level, so distance = 0. This prevents penalizing pad-
                    // launched paths that overshoot the coin altitude.
                    int jumpDistToCoin, walkDistToCoin;
                    if (cubeCoinTargetY < (state.Y_fixed >> 8))
                    {
                        jumpDistToCoin = bestF0JumpMinY <= cubeCoinTargetY ? 0 : bestF0JumpMinY - cubeCoinTargetY;
                        walkDistToCoin = bestF0WalkMinY <= cubeCoinTargetY ? 0 : bestF0WalkMinY - cubeCoinTargetY;
                    }
                    else
                    {
                        jumpDistToCoin = Math.Abs(bestF0JumpMinY - cubeCoinTargetY);
                        walkDistToCoin = Math.Abs(bestF0WalkMinY - cubeCoinTargetY);
                    }
                    int rawDiff = walkDistToCoin - jumpDistToCoin;
                    // Scale bonus up to BFS_HORIZON � makes coin proximity a
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
            // ground) � this indicates the cube should walk under an obstacle
            // rather than jumping into it (e.g. COL_TOP platforms).
            // ALSO suppress when walk ALSO survives the full horizon � in that
            // case both paths are equally good and holdBonus would arbitrarily
            // prefer jump, potentially sending the cube to an elevated dead end.
            bool walkStaysLower = bestF0WalkMinY != int.MaxValue
                && bestF0JumpMinY != int.MaxValue
                && bestF0WalkMinY > bestF0JumpMinY;
            int holdBonus = (bestF0JumpHoldPattern && bestF0JumpSurv >= BFS_HORIZON && !walkStaysLower) ? 5 : 0;

            // -- Altitude ceiling penalty (coin retry) --
            // When retrying after forgiven coins, penalize JUMP when the
            // player is already above the coin's altitude ceiling. This
            // gently biases the BFS toward walking (descending staircases)
            // when ground-level routing is needed to reach a pad/coin.
            // Uses 1 frame per pixel above ceiling � soft enough that the
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
                    // 60-30 = 30 > 5 ? jump wins).
                    altPenaltyJump = 30;
                    if (playerY_ap < apCeilingY)
                    {
                        // Player is above the ceiling � penalize jumping (going higher)
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

            // -- Jump Timing Bias --
            // When bias != 0.5 (non-default), add a small tiebreaker term.
            // Earliest (0.0) → +3 to jumpScore (prefer jumping sooner).
            // Latest  (1.0) → +3 to walkScore (prefer delaying jumps).
            // Middle  (0.5) → no change (default behavior).
            // The ±3 magnitude is enough to break ties when both paths
            // survive equally, but never overrides a genuine survival difference.
            if (JumpTimingBias < 0.45)
            {
                int jBias = (int)((0.5 - JumpTimingBias) * 6.0 + 0.5); // 0.0→3, 0.25→2
                jumpScore += jBias;
            }
            else if (JumpTimingBias > 0.55)
            {
                int wBias = (int)((JumpTimingBias - 0.5) * 6.0 + 0.5); // 1.0→3, 0.75→2
                walkScore += wBias;
            }
#if !DISABLE_DEBUG_LOGGING
            string _tbPath = "";
#endif
            if (jumpScore > walkScore)
            { shouldJump = true;
#if !DISABLE_DEBUG_LOGGING
              _tbPath = "JSCORE";
#endif
            }
            else if (walkScore > jumpScore)
            { shouldJump = false;
#if !DISABLE_DEBUG_LOGGING
              _tbPath = "WSCORE";
#endif
            }
            else if (isElevated && bestF0JumpSurv >= BFS_HORIZON && bestF0WalkSurv >= BFS_HORIZON
                     && bestF0JumpMinY > bestF0WalkMinY) // walk genuinely stays higher
            { shouldJump = false;
#if !DISABLE_DEBUG_LOGGING
              _tbPath = "ELEV_ALT";
#endif
            }
            else if (isElevated && bestF0JumpSurv >= BFS_HORIZON && bestF0WalkSurv >= BFS_HORIZON)
            { shouldJump = false;
#if !DISABLE_DEBUG_LOGGING
              _tbPath = "ELEV_GEN";
#endif
            }
            else if (bestF0JumpSurv >= BFS_HORIZON && bestF0WalkSurv >= BFS_HORIZON
                     && bestF0WalkMinY > bestF0JumpMinY) // walk genuinely stays lower (closer to ground)
            { shouldJump = false;
#if !DISABLE_DEBUG_LOGGING
              _tbPath = "FLOOR_TB";
#endif
            }
            // When an uncollected coin is nearby and scores are tied,
            // prefer WALK to stay grounded for pad-based coin collection.
            // Without this, DEFJ launches the cube off course right when
            // the coin proximity bonus equalizes between paths.
            else if (cubeCoinTargetY >= 0 && bestF0JumpX >= bestF0WalkX)
            { shouldJump = false;
#if !DISABLE_DEBUG_LOGGING
              _tbPath = "COIN_WALK";
#endif
            }
            else if (bestF0JumpX >= bestF0WalkX)
            { shouldJump = true;
#if !DISABLE_DEBUG_LOGGING
              _tbPath = "DEFJ";
#endif
            }
            else
            { shouldJump = false;
#if !DISABLE_DEBUG_LOGGING
              _tbPath = "DEFW";
#endif
            }
#if !DISABLE_DEBUG_LOGGING
            if (_speculativeDepth == 0)
            {
                PfLog($"[CUBE_DBG] X={state.X_fixed >> 8} Y={state.Y_fixed >> 8} jS={bestF0JumpSurv} wS={bestF0WalkSurv} jSc={jumpScore} wSc={walkScore} hB={holdBonus} jMY={bestF0JumpMinY} wMY={bestF0WalkMinY} tb={_tbPath}");
            }
#endif

#if !DISABLE_DEBUG_LOGGING
            PfLog($"[DECIDE_CUBE] BFS: jumpSurv={bestF0JumpSurv} jumpX={bestF0JumpX} holdPat={bestF0JumpHoldPattern} jumpMinY={bestF0JumpMinY}, walkSurv={bestF0WalkSurv} walkX={bestF0WalkX} walkMinY={bestF0WalkMinY} elevBonus={elevBonus} coinBonusJ={coinBonusJump} coinBonusW={coinBonusWalk} ? {(shouldJump?"JUMP":"WALK")}");
#endif

            if (!shouldJump)
            {
                // BFS says don't jump this frame. But don't commit a delay �
                // next frame BFS re-evaluates from scratch.
                return false;
            }

            return true;
        }

        // ---------------------------------------------------------------
        //  Cross-corridor coin pre-forgiveness
        //  Scans portals left-to-right to determine the game mode at each
        //  coin's position. Coins that are in a different mode section than
        //  their preceding mode boundary are pre-forgiven so the ship PD
        //  doesn't steer toward unreachable coins.
        // ---------------------------------------------------------------
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
                // Only forgive coins within 2500px of the portal boundary �
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
            if (_speculativeDepth > 0) return; // Skip during BFS (thread safety)
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
        /// <summary>
        /// Per-decision bias fallback: tries the mode-specific decision with the
        /// current JumpTimingBias. If both hold and release die within 3 frames,
        /// retries with neutral bias (0.5) to see if a different altitude target
        /// avoids the dead end. Only applies to continuous modes (ship/UFO/robot/wave).
        /// </summary>
        private bool DecideWithBiasFallback(SimState state, bool isOverrideFrame)
        {
            // Make the mode-specific decision with current bias
            bool input = DecideModeInput(state, isOverrideFrame);

            // Robot mode already performs comprehensive forward simulation
            // (SimulateRobotForward) across multiple hold durations.
            // The 4-frame survival check below is too short to catch hazards
            // the robot evaluator already handles, so skip the override.
            if (state.GameMode == 4) return input;

            // Skip fallback during speculative lookahead or if bias is already neutral
            if (_speculativeDepth > 0) return input;
            double origBias = JumpTimingBias;
            if (Math.Abs(origBias - 0.5) < 0.05) return input; // already neutral

            // Quick survival check: does this decision survive a few frames?
            _speculativeDepth++;
            var sChosen = state.Clone();
            bool chosenAlive = StepFrame(ref sChosen, input, out bool chosenEnd);
            if (chosenEnd) { _speculativeDepth--; return input; }

            int chosenSurv = 0;
            if (chosenAlive)
            {
                for (int f = 0; f < 3; f++)
                {
                    bool a = StepFrame(ref sChosen, input, out bool e);
                    if (e) { chosenSurv = 4; break; }
                    if (!a) break;
                    chosenSurv++;
                }
            }

            // Also check the opposite input
            var sOpp = state.Clone();
            bool oppAlive = StepFrame(ref sOpp, !input, out bool oppEnd);
            if (oppEnd) { _speculativeDepth--; return !input; }

            int oppSurv = 0;
            if (oppAlive)
            {
                for (int f = 0; f < 3; f++)
                {
                    bool a = StepFrame(ref sOpp, !input, out bool e);
                    if (e) { oppSurv = 4; break; }
                    if (!a) break;
                    oppSurv++;
                }
            }
            _speculativeDepth--;

            // If either direction survives >1 frame, the current bias is fine
            if (chosenSurv > 1 || oppSurv > 1) return chosenSurv >= oppSurv ? input : !input;

            // Both die quickly — retry with neutral bias
            JumpTimingBias = 0.5;
            bool neutralInput = DecideModeInput(state, isOverrideFrame);
            JumpTimingBias = origBias; // restore

            return neutralInput;
        }

        /// <summary>
        /// Dispatches to the mode-specific decision method.
        /// </summary>
        private bool DecideModeInput(SimState state, bool isOverrideFrame)
        {
            switch (state.GameMode)
            {
                case 1: return DecideShipInput(state);
                case 3: return DecideUfoInput(state, isOverrideFrame);
                case 4: return DecideRobotInput(state, isOverrideFrame);
                case 5: return DecideSpiderInput(state, isOverrideFrame);
                case 6: return DecideWaveInput(state, isOverrideFrame);
                case 7: return DecideSwingInput(state, isOverrideFrame);
                case 8: return DecideNinjaInput(state, isOverrideFrame);
                case 9: return DecidePogoInput(state, isOverrideFrame);
                default: return false;
            }
        }

        /// <summary>
        /// Binary tree search for ship: explore hold and release branches
        /// recursively to find the longest survival path.
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
            // Release-first fires before hold, enabling dip-then-recover sine waves.
            if (_shipForceReleaseFirstFrames > 0)
            {
                _shipForceReleaseFirstFrames--;
                return false;
            }
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

            // -- Coin-aware: find next coin target Y for PD tiebreaker --
            int coinTargetY = -1; // -1 = no coin target
            int coinTargetIdx = -1;
            int coinDistX = 0; // horizontal distance to coin
            SpriteEntry coinEntry = default;
            bool hasCoinEntry = false;
            // Suppress coin-seeking during mode transition stabilization
            if (_speculativeDepth == 0 && _modeTransitionStabilizeFrames > 0)
                _modeTransitionStabilizeFrames--;
            if (PreferCoins && allCoins.Count > 0 && _modeTransitionStabilizeFrames == 0)
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
            if (!aliveH && !aliveR) return false;

            // Both survive 1 frame � use binary tree search.
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
                { int _mo = (vizH.Mini && !vizH.GravFlipped) ? 4 : 0; holdPath.Add(((vizH.X_fixed >> 8) + 8, (vizH.Y_fixed >> 8) + _mo + 8)); }
                { int _mo = (vizR.Mini && !vizR.GravFlipped) ? 4 : 0; relPath.Add(((vizR.X_fixed >> 8) + 8, (vizR.Y_fixed >> 8) + _mo + 8)); }
                _speculativeDepth++;
                for (int vf = 0; vf < SHIP_TREE_DEPTH; vf++)
                {
                    bool aH = StepFrame(ref vizH, true, out bool eH);
                    { int _mo = (vizH.Mini && !vizH.GravFlipped) ? 4 : 0; holdPath.Add(((vizH.X_fixed >> 8) + 8, (vizH.Y_fixed >> 8) + _mo + 8)); }
                    if (!aH || eH) break;
                }
                for (int vf = 0; vf < SHIP_TREE_DEPTH; vf++)
                {
                    bool aR = StepFrame(ref vizR, false, out bool eR);
                    { int _mo = (vizR.Mini && !vizR.GravFlipped) ? 4 : 0; relPath.Add(((vizR.X_fixed >> 8) + 8, (vizR.Y_fixed >> 8) + _mo + 8)); }
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
            //
            // The search plans a full dive-collect-recover trajectory:
            //   1. Approach: steer toward coin Y while surviving
            //   2. Collect: overlap coin hitbox
            //   3. Recover: pull up and return to corridor center
            //
            // Triggers at distX 20-600px (extended from 300 to plan earlier
            // descents for deep coins). Re-attempts every 30px closer.
            // For coins far off-corridor, extend the beam range so it fires
            // at the fork entrance where the ship can still change corridors.
            // Only extend when the coin is reasonably close (within 1200px) to
            // avoid triggering expensive long-range beams for coins in a
            // different game mode section far ahead.
            int beamMaxRange = 600;
            bool isExtendedBeam = false;
            if (coinTargetY >= 0 && hasCoinEntry && coinDistX <= 1200)
            {
                int corridorY = FindCorridorCenter(ref state);
                int yOff = Math.Abs(coinTargetY - corridorY);
                if (yOff > 40)
                {
                    beamMaxRange = Math.Min(1200, Math.Max(600, yOff * 10));
                    isExtendedBeam = coinDistX > 600;
                }
            }
            // Extended-range beams re-fire less frequently (every 60px) to
            // limit the performance cost of long-horizon searches.
            int beamReFireDist = isExtendedBeam ? 60 : 30;
            bool beamSearchAllowed = coinTargetY >= 0 && hasCoinEntry && _coinInputScript.Count == 0
                && coinDistX >= 20 && coinDistX <= beamMaxRange
                && (_beamSearchAttemptedCoinIdx != coinTargetIdx
                    || coinDistX <= _beamSearchLastDistX - beamReFireDist);
            if (beamSearchAllowed)
            {
                // Extended-range beams use a narrower beam to limit compute cost.
                int BEAM_WIDTH = isExtendedBeam ? 128 : 512;
                // After coin collection, continue beam for RECOVERY_FRAMES more
                // frames to navigate obstacles beyond the coin. This is critical
                // because PD-based recovery can fly into death tiles that the
                // beam search can avoid by exploring both hold/release at each step.
                const int RECOVERY_FRAMES = 120;
                // Horizon: enough to reach and collect coin, plus recovery.
                // For extended-range beams (off-corridor coins), allow a larger
                // horizon so the beam can plan through the gap and reach the coin.
                int beamHorizon = isExtendedBeam
                    ? Math.Max(450, coinDistX / 2 + 60 + RECOVERY_FRAMES)
                    : Math.Min(450, coinDistX * 3 / 4 + 60 + RECOVERY_FRAMES);
                _speculativeDepth++;

                // Each beam state tracks whether the coin has been collected
                // and how many frames ago. collected=-1 means not yet.
                var beam = new List<(SimState st, List<bool> inputs, int collected)>();
                beam.Add((state.Clone(), new List<bool>(), -1));

                bool foundTrajectory = false;
                var bestScript = new List<bool>();
                int bestPostCollectSurvival = 0;

                for (int step = 0; step < beamHorizon && beam.Count > 0; step++)
                {
                    var nextBeam = new List<(SimState st, List<bool> inputs, int collected, int score)>();

                    foreach (var (bs, bInputs, bCollected) in beam)
                    {
                        for (int tryHold = 0; tryHold <= 1; tryHold++)
                        {
                            bool inp = (tryHold == 1);
                            var sim = bs.Clone();
                            if (!StepFrame(ref sim, inp, out _)) continue; // died

                            var newInputs = new List<bool>(bInputs) { inp };
                            int newCollected = bCollected;

                            // Check coin overlap if not yet collected
                            if (newCollected < 0)
                            {
                                int nx = (sim.X_fixed >> 8) + 1;
                                int hbW_s = GetHitboxW(sim.Mini);
                                int hbH_s = GetHitboxH(sim.Mini);
                                int hbOff_s = GetHitboxOffsetY(sim.GameMode, sim.Mini, sim.GravFlipped);
                                int pT = (sim.Y_fixed >> 8) + hbOff_s;
                                if (!(nx + hbW_s < coinEntry.HitLeft || coinEntry.HitRight < nx) &&
                                    !(pT + hbH_s < coinEntry.HitTop || coinEntry.HitBottom < pT))
                                {
                                    newCollected = 0; // just collected
                                }
                            }

                            if (newCollected >= 0)
                            {
                                // Post-collection phase: count survival frames
                                newCollected++;

                                if (newCollected >= RECOVERY_FRAMES)
                                {
                                    // Survived long enough after collection � accept!
                                    foundTrajectory = true;
                                    bestScript = newInputs;
                                    bestPostCollectSurvival = newCollected;
                                    break;
                                }

                                // Track best trajectory so far
                                if (newCollected > bestPostCollectSurvival)
                                {
                                    bestPostCollectSurvival = newCollected;
                                    bestScript = new List<bool>(newInputs);
                                }

                                // Score for post-collection: favor states near corridor
                                // center with low velocity (stable flight). This steers
                                // around upcoming obstacles because the beam explores
                                // both hold/release, keeping alive only paths that
                                // survive the terrain ahead.
                                int sy = sim.Y_fixed >> 8;
                                int corridorY = FindCorridorCenter(ref sim);
                                int dyCorridor = Math.Abs(sy - corridorY);
                                int velYMag = Math.Abs(sim.VelY_fixed) >> 6;
                                // Strongly prefer collected states (score offset -10000)
                                // so they aren't pruned by uncollected states
                                int score = -10000 + dyCorridor * 3 + velYMag;
                                nextBeam.Add((sim, newInputs, newCollected, score));
                            }
                            else
                            {
                                // Pre-collection phase: steer toward coin
                                int sx = sim.X_fixed >> 8;
                                // Eliminate states past the coin that didn't collect it
                                if (sx > coinEntry.HitRight + 4) continue;

                                int sy = sim.Y_fixed >> 8;
                                int dy = Math.Abs(sy - coinTargetY);
                                int dx = Math.Max(0, coinEntry.HitLeft - sx);
                                int score;

                                // Scoring: balance X distance with Y proximity.
                                // All states advance at the same X speed, so dx
                                // differences are small � give meaningful Y weight
                                // at ALL distances so the beam preserves dive
                                // trajectories heading toward the coin.
                                if (dx > 120)
                                    score = dx + dy;          // equal weight (was dx*2 + dy/4)
                                else if (dx > 50)
                                    score = dx / 2 + dy * 2;  // favor Y more
                                else if (dx > 15)
                                    score = dx / 4 + dy * 4;  // strongly favor Y
                                else
                                    score = dy * 6 + dx / 4;  // almost pure Y

                                // Velocity bonus for diving toward coin
                                int velY_s = sim.VelY_fixed;
                                int yDiff = coinTargetY - sy;
                                bool velToward = (yDiff > 0) == (velY_s * sim.GravMul > 0);
                                if (velToward && dy > 4)
                                {
                                    int velMag = Math.Abs(velY_s) >> 7;
                                    score -= Math.Min(dy / 2, velMag);
                                }

                                bool pastCoinY = (sim.GravMul > 0) ? (sy > coinTargetY + 20) : (sy < coinTargetY - 20);
                                bool velAway = !velToward && Math.Abs(velY_s) > 0x200;
                                if (pastCoinY && velAway)
                                    score += 200;

                                nextBeam.Add((sim, newInputs, newCollected, score));
                            }
                        }
                        if (foundTrajectory) break;
                    }
                    if (foundTrajectory) break;

                    // Accept best trajectory if beam exhausted and recovery is full
                    if (bestPostCollectSurvival >= RECOVERY_FRAMES && nextBeam.Count == 0)
                    {
                        foundTrajectory = true;
                        break;
                    }

                    // === DIVERSITY-PRESERVING BEAM PRUNING ===
                    // Standard beam search prunes purely by coin-proximity score,
                    // which kills off "safe but far from coin" states. These safe
                    // states are essential: they survive obstacles that diving states
                    // hit, and may find later openings to dive. Reserve 25% of the
                    // beam for survival-scored states (regardless of coin proximity).
                    //
                    // Split nextBeam into:
                    //   - collected: states that already have the coin (priority)
                    //   - coinSeeking: states scored by coin proximity
                    //   - safeReserve: states scored by survival (corridor proximity)
                    int SAFE_RESERVE = BEAM_WIDTH / 4;
                    int COIN_SEEKING = BEAM_WIDTH - SAFE_RESERVE;

                    var collectedStates = new List<(SimState st, List<bool> inputs, int collected, int score)>();
                    var uncollectedStates = new List<(SimState st, List<bool> inputs, int collected, int score)>();
                    foreach (var ns in nextBeam)
                    {
                        if (ns.collected >= 0)
                            collectedStates.Add(ns);
                        else
                            uncollectedStates.Add(ns);
                    }

                    beam.Clear();

                    // All collected states get priority (they share the -10000 offset)
                    collectedStates.Sort((a, b) => a.score.CompareTo(b.score));
                    int collectedKeep = Math.Min(BEAM_WIDTH, collectedStates.Count);
                    for (int i = 0; i < collectedKeep; i++)
                        beam.Add((collectedStates[i].st, collectedStates[i].inputs, collectedStates[i].collected));

                    int remaining = BEAM_WIDTH - beam.Count;
                    if (remaining > 0 && uncollectedStates.Count > 0)
                    {
                        // Sort uncollected by coin-proximity score
                        uncollectedStates.Sort((a, b) => a.score.CompareTo(b.score));

                        // Take top COIN_SEEKING states by coin score
                        int coinKeep = Math.Min(Math.Min(COIN_SEEKING, remaining), uncollectedStates.Count);
                        var selectedIndices = new HashSet<int>();
                        for (int i = 0; i < coinKeep; i++)
                        {
                            beam.Add((uncollectedStates[i].st, uncollectedStates[i].inputs, uncollectedStates[i].collected));
                            selectedIndices.Add(i);
                        }

                        // Fill SAFE_RESERVE slots with states sorted by survival
                        // (distance from corridor center + low velocity magnitude)
                        remaining = BEAM_WIDTH - beam.Count;
                        if (remaining > 0)
                        {
                            // Compute corridor center once (all states at ~same X)
                            var refState = uncollectedStates[0].st;
                            int safeCorridorY = FindCorridorCenter(ref refState);

                            // Re-score uncollected states by survival quality
                            var safeScored = new List<(int origIdx, int safeScore)>();
                            for (int i = 0; i < uncollectedStates.Count; i++)
                            {
                                if (selectedIndices.Contains(i)) continue;
                                var ns = uncollectedStates[i];
                                int sy = ns.st.Y_fixed >> 8;
                                int dyCorridor = Math.Abs(sy - safeCorridorY);
                                int velMag = Math.Abs(ns.st.VelY_fixed) >> 6;
                                safeScored.Add((i, dyCorridor * 2 + velMag));
                            }
                            safeScored.Sort((a, b) => a.safeScore.CompareTo(b.safeScore));
                            int safeKeep = Math.Min(remaining, safeScored.Count);
                            for (int i = 0; i < safeKeep; i++)
                            {
                                var ns = uncollectedStates[safeScored[i].origIdx];
                                beam.Add((ns.st, ns.inputs, ns.collected));
                            }
                        }
                    }
                }

                // Accept if recovery survived at least 15 frames.
                // With death reason fixes (EJECT_DEATH properly labeled),
                // partial recovery no longer causes MISSED_COIN cycling.
                // Use a low threshold (15) because the auto-forgive counter
                // prevents infinite cycling even if the trajectory fails.
                if (!foundTrajectory && bestPostCollectSurvival >= 15)
                {
                    foundTrajectory = true;
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[SHIP_COIN_BEAM_PARTIAL] Accepting partial recovery={bestPostCollectSurvival} for coin idx={coinEntry.Index} distX={coinDistX}");
#endif
                }

                _speculativeDepth--;
                _beamSearchAttemptedCoinIdx = coinTargetIdx;
                _beamSearchLastDistX = coinDistX; // track distance for re-try

                if (foundTrajectory && bestScript.Count > 0)
                {
                    _coinInputScript.Clear();
                    _coinInputScriptCoinIdx = coinTargetIdx;
                    for (int si = 1; si < bestScript.Count; si++)
                        _coinInputScript.Enqueue(bestScript[si]);
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[SHIP_COIN_BEAM] Found trajectory for coin idx={coinEntry.Index}, scriptLen={bestScript.Count}, distX={coinDistX}, recovery={bestPostCollectSurvival}");
#endif
                    return bestScript[0];
                }
#if !DISABLE_DEBUG_LOGGING
                else
                {
                    PfLog($"[SHIP_COIN_BEAM_FAIL] coin idx={coinEntry.Index} distX={coinDistX} horizon={beamHorizon} beamEnd={beam.Count} bestRecovery={bestPostCollectSurvival} found={foundTrajectory} scriptLen={bestScript.Count} lastDeath={_lastDeathReason} lastDeathX={_lastDeathX} lastDeathY={_lastDeathY}");
                }
#endif

                // === SURVIVAL BEAM RETRY ===
                // When ALL beam states died with no coin collection, the coin may
                // be behind terrain obstacles (e.g., floor gaps between pillars)
                // that coin-proximity scoring can't navigate because it drives
                // states into walls. Retry with SEGMENT-based scoring: any Y
                // between corridor center and coin Y scores equally well, so the
                // beam maintains diversity across the entire descent band. States
                // at the right Y naturally find gaps in the terrain.
                if (beam.Count == 0 && bestPostCollectSurvival == 0)
                {
                    _speculativeDepth++;
                    int retryCorridorY = FindCorridorCenter(ref state);
                    const int RETRY_BEAM_WIDTH = 1024;
                    int retryHorizon = Math.Min(600, coinDistX + 60 + RECOVERY_FRAMES);
                    int segTop = Math.Min(retryCorridorY, coinTargetY);
                    int segBot = Math.Max(retryCorridorY, coinTargetY);
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[SHIP_COIN_BEAM_SURVIVAL_RETRY] coin idx={coinEntry.Index} distX={coinDistX} corridorY={retryCorridorY} coinY={coinTargetY} segY=[{segTop},{segBot}] horizon={retryHorizon}");
#endif
                    beam.Clear();
                    beam.Add((state.Clone(), new List<bool>(), -1));
                    foundTrajectory = false;
                    bestScript.Clear();
                    bestPostCollectSurvival = 0;

                    for (int step = 0; step < retryHorizon && beam.Count > 0; step++)
                    {
                        var nextBeam2 = new List<(SimState st, List<bool> inputs, int collected, int score)>();

                        foreach (var (bs2, bInputs2, bCollected2) in beam)
                        {
                            for (int tryHold = 0; tryHold <= 1; tryHold++)
                            {
                                bool inp = (tryHold == 1);
                                var sim = bs2.Clone();
                                if (!StepFrame(ref sim, inp, out _)) continue;

                                var newInputs = new List<bool>(bInputs2) { inp };
                                int newCollected = bCollected2;

                                // Check coin overlap
                                if (newCollected < 0)
                                {
                                    int nx = (sim.X_fixed >> 8) + 1;
                                    int hbW_s = GetHitboxW(sim.Mini);
                                    int hbH_s = GetHitboxH(sim.Mini);
                                    int hbOff_s = GetHitboxOffsetY(sim.GameMode, sim.Mini, sim.GravFlipped);
                                    int pT = (sim.Y_fixed >> 8) + hbOff_s;
                                    if (!(nx + hbW_s < coinEntry.HitLeft || coinEntry.HitRight < nx) &&
                                        !(pT + hbH_s < coinEntry.HitTop || coinEntry.HitBottom < pT))
                                    {
                                        newCollected = 0;
                                    }
                                }

                                int score;
                                if (newCollected >= 0)
                                {
                                    // Post-collection: count recovery frames
                                    newCollected++;
                                    if (newCollected >= RECOVERY_FRAMES)
                                    {
                                        foundTrajectory = true;
                                        bestScript = newInputs;
                                        bestPostCollectSurvival = newCollected;
                                        break;
                                    }
                                    if (newCollected > bestPostCollectSurvival)
                                    {
                                        bestPostCollectSurvival = newCollected;
                                        bestScript = new List<bool>(newInputs);
                                    }
                                    int sy_rc = sim.Y_fixed >> 8;
                                    int corridorY_rc = FindCorridorCenter(ref sim);
                                    int dyCorridor_rc = Math.Abs(sy_rc - corridorY_rc);
                                    int velYMag_rc = Math.Abs(sim.VelY_fixed) >> 6;
                                    score = -10000 + dyCorridor_rc * 3 + velYMag_rc;
                                }
                                else
                                {
                                    // Pre-collection: SEGMENT scoring
                                    // Any Y between corridor center and coin Y is equally
                                    // good (dySegment=0). Penalize states outside this band.
                                    int sx = sim.X_fixed >> 8;
                                    if (sx > coinEntry.HitRight + 32) continue;

                                    int sy = sim.Y_fixed >> 8;
                                    int dySegment;
                                    if (sy < segTop) dySegment = segTop - sy;
                                    else if (sy > segBot) dySegment = sy - segBot;
                                    else dySegment = 0;

                                    int velMag = Math.Abs(sim.VelY_fixed) >> 7;
                                    score = dySegment * 3 + velMag;
                                }
                                nextBeam2.Add((sim, newInputs, newCollected, score));
                            }
                            if (foundTrajectory) break;
                        }
                        if (foundTrajectory) break;

                        if (bestPostCollectSurvival >= RECOVERY_FRAMES && nextBeam2.Count == 0)
                        {
                            foundTrajectory = true;
                            break;
                        }

                        // Simple sort-and-prune (segment scoring preserves Y diversity)
                        nextBeam2.Sort((a, b) => a.score.CompareTo(b.score));
                        beam.Clear();
                        int keep = Math.Min(RETRY_BEAM_WIDTH, nextBeam2.Count);
                        for (int i = 0; i < keep; i++)
                            beam.Add((nextBeam2[i].st, nextBeam2[i].inputs, nextBeam2[i].collected));
                    }

                    if (!foundTrajectory && bestPostCollectSurvival >= 15)
                    {
                        foundTrajectory = true;
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[SHIP_COIN_BEAM_SURVIVAL_PARTIAL] recovery={bestPostCollectSurvival} coin idx={coinEntry.Index}");
#endif
                    }

                    _speculativeDepth--;

                    if (foundTrajectory && bestScript.Count > 0)
                    {
                        _coinInputScript.Clear();
                        _coinInputScriptCoinIdx = coinTargetIdx;
                        for (int si = 1; si < bestScript.Count; si++)
                            _coinInputScript.Enqueue(bestScript[si]);
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[SHIP_COIN_BEAM_SURVIVAL_OK] coin idx={coinEntry.Index} scriptLen={bestScript.Count} distX={coinDistX} recovery={bestPostCollectSurvival}");
#endif
                        return bestScript[0];
                    }
#if !DISABLE_DEBUG_LOGGING
                    else
                    {
                        PfLog($"[SHIP_COIN_BEAM_SURVIVAL_FAIL] coin idx={coinEntry.Index} distX={coinDistX} horizon={retryHorizon} beamEnd={beam.Count} bestRecovery={bestPostCollectSurvival} lastDeath={_lastDeathReason} lastDeathX={_lastDeathX} lastDeathY={_lastDeathY}");
                    }
#endif
                }
            }

            if (survH != survR)
            {
                // Pre-compute off-corridor flag to adjust survival gate.
                // For coins far from the corridor center (>40px offset) and
                // within 1200px, the ship MUST deviate from the safe path.
                // Lower the minimum survival requirement so the coin-aware
                // code runs even when one option barely survives (near ceiling).
                int preGateCoinYOff = 0;
                bool preGateOffCorridor = false;
                if (coinTargetY >= 0 && hasCoinEntry && coinDistX <= 1200)
                {
                    int corridorCenterRaw = FindCorridorCenter(ref state);
                    preGateCoinYOff = Math.Abs(coinTargetY - corridorCenterRaw);
                    preGateOffCorridor = preGateCoinYOff > 40;
                }
                int minSurvGate = preGateOffCorridor ? SHIP_TREE_DEPTH / 4 : SHIP_TREE_DEPTH / 2;

                if (coinTargetY >= 0 && hasCoinEntry
                    && Math.Min(survH, survR) >= minSurvGate
                    && (coinDistX <= 600 || preGateOffCorridor))
                {
                    // Coin within beam-search range (or off-corridor needing early
                    // positioning) and both paths survive well � check which initial
                    // input leads to collecting the coin within SHIP_COIN_HORIZON
                    // frames.  Beyond beam range, skip coin logic entirely and use
                    // pure survival (survH > survR) to avoid interfering with
                    // obstacle navigation.
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
                            int hbOff_s = GetHitboxOffsetY(sim.GameMode, sim.Mini, sim.GravFlipped);
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
                            int hbOff_s = GetHitboxOffsetY(sim.GameMode, sim.Mini, sim.GravFlipped);
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

                    int coinCollectMinSurv = preGateOffCorridor ? 1 : SHIP_TREE_DEPTH / 2;
                    if (holdCollects && !releaseCollects && survH >= coinCollectMinSurv)
                    {
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[SHIP_COIN] Hold collects coin idx={coinEntry.Index} � forcing hold (survH={survH} survR={survR})");
#endif
                        return true;
                    }
                    if (releaseCollects && !holdCollects && survR >= coinCollectMinSurv)
                    {
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[SHIP_COIN] Release collects coin idx={coinEntry.Index} � forcing release (survH={survH} survR={survR})");
#endif
                        return false;
                    }

                    // Fall through � if survival diff is small enough,
                    // use PD tiebreaker for coin steering.
                    // Use a DISTANCE-ADAPTIVE threshold: closer to the coin,
                    // sacrifice more survival frames to steer toward it.
                    //
                    // For coins far off-corridor (>40px), the threshold is
                    // much higher � the ship must accept survival risk to
                    // deviate from the safe corridor toward the coin's corridor.
                    int coinThreshold = _shipCoinAggressiveThreshold;
                    // Reuse the pre-computed off-corridor flag (with ceiling guard)
                    if (preGateOffCorridor)
                    {
                        // Off-corridor: the ship must deviate from the safe path
                        // to reach the coin, but only when close enough that
                        // obstacle navigation isn't compromised.
                        // At longer range (>400px), keep a moderate threshold
                        // so the ship prioritizes survival over coin-seeking.
                        if (coinDistX > 400)
                            coinThreshold = Math.Max(coinThreshold, 4);                   // mild bias
                        else if (coinDistX > 200)
                            coinThreshold = Math.Max(coinThreshold, SHIP_TREE_DEPTH / 2); // 10
                        else
                            coinThreshold = Math.Max(coinThreshold, SHIP_TREE_DEPTH);     // 20
                    }
                    else
                    {
                        if (coinDistX > 600)
                        {
                            // Beyond beam search range: keep base threshold.
                            // Avoid risking survival for distant coins.
                        }
                        else if (coinDistX > 300)
                            coinThreshold = Math.Max(coinThreshold, 4);
                        else if (coinDistX > 200)
                            coinThreshold = Math.Max(coinThreshold, 6);
                        else if (coinDistX > 100)
                            coinThreshold = Math.Max(coinThreshold, SHIP_TREE_DEPTH / 2);
                        else
                            coinThreshold = Math.Max(coinThreshold, SHIP_TREE_DEPTH);
                    }
                    if (Math.Abs(survH - survR) <= coinThreshold)
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
                    // No coin or not both surviving well � pure survival
                    return survH > survR;
                }
            }

            // Equal survival (or small difference with coin target) � use velocity-aware
            // corridor tracking as tiebreaker.  When coins are active, BLEND the target
            // between corridor center and coin Y based on distance.  Start a gentle
            // descent from 400px out, ramping up to 100% coin Y at close range.
            // This gives the ship time to descend gradually (avoiding obstacles)
            // and provides the beam search a better starting altitude.
            //
            // For coins far off-corridor (>40px offset), the blend starts much
            // earlier (up to yOff*16 px ahead) so the ship drifts toward the
            // corridor fork entrance in time.  The early zone uses a very
            // gentle blend (5-15%) to avoid aggressive pulls toward coins
            // in other game mode sections.
            int biasPixels = (int)((JumpTimingBias - 0.5) * 16.0);
            // Learned bias from coin collect-lose history: shift corridor
            // so subsequent approaches from the ship entry diverge enough
            // to survive past obstacles that killed previous attempts.
            foreach (var kvp in _coinCollectThenLoseCount)
            {
                if (!_forgivenCoins.Contains(kvp.Key))
                    biasPixels -= 3 * kvp.Value;
            }
            int targetY;
            bool offCorridorCoin = false;
            if (coinTargetY >= 0)
            {
                // Compute basic corridor center to determine if coin is off-corridor
                int basicCorridorCenter = FindCorridorCenter(ref state) + _shipCorridorBias + biasPixels;
                int basicYOff = Math.Abs(coinTargetY - basicCorridorCenter);
                offCorridorCoin = basicYOff > 40 && coinDistX <= 2000;

                if (offCorridorCoin)
                {
                    // For off-corridor ceiling coins, compute corridor center from the
                    // COIN's Y perspective with limited look-ahead. This lets the
                    // scanner "see" the corridor the coin is in (above the dividing
                    // wall) rather than the corridor the ship is currently in.
                    // The blend then pulls the ship toward the upper corridor center,
                    // positioning it at the ceiling of the lower corridor, ready to
                    // fly through any gap in the dividing wall.
                    int upperCorr = FindCorridorCenter(ref state, 2, coinTargetY) + _shipCorridorBias + biasPixels;
                    int localCorr = FindCorridorCenter(ref state, 0) + _shipCorridorBias + biasPixels;
                    int upperYOff = Math.Abs(coinTargetY - upperCorr);
                    int localYOff = Math.Abs(coinTargetY - localCorr);
                    bool hasLocalGap = upperYOff < localYOff - 20;
                    int corridorCenter = hasLocalGap ? upperCorr : localCorr;

                    int coinYOff = Math.Abs(coinTargetY - corridorCenter);
                    int blendStartDist = (coinYOff > 40 && coinYOff <= 120 && coinDistX <= 2000)
                        ? Math.Min(2000, Math.Max(400, coinYOff * 16))
                        : 400;

                    if (coinDistX > blendStartDist)
                    {
                        int pct = 80;
                        targetY = corridorCenter + (coinTargetY - corridorCenter) * pct / 100;
                    }
                    else if (coinDistX > 400)
                    {
                        int range = blendStartDist - 400;
                        int pct = hasLocalGap
                            ? (range > 0 ? 70 + (blendStartDist - coinDistX) * 25 / range : 95)
                            : (range > 0 ? 80 + (blendStartDist - coinDistX) * 10 / range : 90);
                        targetY = corridorCenter + (coinTargetY - corridorCenter) * pct / 100;
                    }
                    else if (coinDistX > 300)
                    {
                        int pct = hasLocalGap ? 90 : 85;
                        targetY = corridorCenter + (coinTargetY - corridorCenter) * pct / 100;
                    }
                    else if (coinDistX > 200)
                    {
                        int pct = hasLocalGap ? 95 : 90;
                        targetY = corridorCenter + (coinTargetY - corridorCenter) * pct / 100;
                    }
                    else if (coinDistX > 120)
                    {
                        int pct = hasLocalGap ? 100 : 95;
                        targetY = corridorCenter + (coinTargetY - corridorCenter) * pct / 100;
                    }
                    else
                    {
                        targetY = coinTargetY;
                    }
                }
                else
                {
                    // Normal coin (near corridor center): target coin Y directly
                    // for strongest coin-seeking, matching original behavior.
                    targetY = coinTargetY;
                }
            }
            else
            {
                targetY = FindCorridorCenter(ref state) + _shipCorridorBias + biasPixels;
            }
            int currentY = state.Y_fixed >> 8;
            int velY = state.VelY_fixed;

            int posError = (state.GravMul > 0) ? (currentY - targetY) : (targetY - currentY);
            int velComponent = -(velY * state.GravMul);
            // D-gain=1 when coin-seeking for faster convergence toward coin Y.
            // D-gain=2 otherwise for smooth corridor tracking (more damping).
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
            sH.ReturnAllSpriteResources(); // recycle array to pool

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
            sR.ReturnAllSpriteResources(); // recycle array to pool

            _speculativeDepth--;
            return Math.Max(bestH, bestR);
        }

        /// <summary>
        /// After collecting a coin mid-flight, try multiple recovery strategies
        /// to bring the ship back to safe corridor flight. Returns the number
        /// of frames the best strategy survives (higher = better).
        /// 
        /// Strategies tested:
        ///   1. Immediate max-hold (arrest downward velocity), then PD corridor
        ///   2. Pure PD corridor steering from current state
        ///   3. Aggressive hold for 8 frames, then PD corridor
        ///   4. Tree-search guided recovery (best local survival each step)
        /// </summary>
        private int ShipCoinRecovery(SimState postCollect, int gravMul)
        {
            const int RECOVERY_HORIZON = 120;
            int bestSurvival = 0;

            // Strategy 1: Immediate hold burst (6 frames) then PD
            {
                var sim = postCollect.Clone();
                int survived = 0;
                bool alive = true;
                // Hold for 6 frames to arrest downward velocity
                for (int f = 0; f < 6 && alive; f++)
                {
                    bool inp = (gravMul > 0); // hold = true for normal gravity
                    alive = StepFrame(ref sim, inp, out _);
                    if (alive) survived++;
                }
                // Then PD corridor steering
                if (alive)
                {
                    int corridorY = FindCorridorCenter(ref sim);
                    for (int f = 0; f < RECOVERY_HORIZON - 6 && alive; f++)
                    {
                        if (f > 0 && (f & 3) == 0)
                            corridorY = FindCorridorCenter(ref sim);
                        int sy = sim.Y_fixed >> 8;
                        int vel = sim.VelY_fixed;
                        int err = (sim.GravMul > 0) ? (sy - corridorY) : (corridorY - sy);
                        int vComp = -(vel * sim.GravMul);
                        bool inp = (err - vComp * 2) > 0;
                        alive = StepFrame(ref sim, inp, out _);
                        if (alive) survived++;
                    }
                }
                bestSurvival = Math.Max(bestSurvival, survived);
            }

            // Strategy 2: Pure PD corridor steering from start
            {
                var sim = postCollect.Clone();
                int survived = 0;
                bool alive = true;
                int corridorY = FindCorridorCenter(ref sim);
                for (int f = 0; f < RECOVERY_HORIZON && alive; f++)
                {
                    if (f > 0 && (f & 3) == 0)
                        corridorY = FindCorridorCenter(ref sim);
                    int sy = sim.Y_fixed >> 8;
                    int vel = sim.VelY_fixed;
                    int err = (sim.GravMul > 0) ? (sy - corridorY) : (corridorY - sy);
                    int vComp = -(vel * sim.GravMul);
                    bool inp = (err - vComp * 2) > 0;
                    alive = StepFrame(ref sim, inp, out _);
                    if (alive) survived++;
                }
                bestSurvival = Math.Max(bestSurvival, survived);
            }

            // Strategy 3: Extended hold burst (12 frames) then PD
            {
                var sim = postCollect.Clone();
                int survived = 0;
                bool alive = true;
                for (int f = 0; f < 12 && alive; f++)
                {
                    bool inp = (gravMul > 0);
                    alive = StepFrame(ref sim, inp, out _);
                    if (alive) survived++;
                }
                if (alive)
                {
                    int corridorY = FindCorridorCenter(ref sim);
                    for (int f = 0; f < RECOVERY_HORIZON - 12 && alive; f++)
                    {
                        if (f > 0 && (f & 3) == 0)
                            corridorY = FindCorridorCenter(ref sim);
                        int sy = sim.Y_fixed >> 8;
                        int vel = sim.VelY_fixed;
                        int err = (sim.GravMul > 0) ? (sy - corridorY) : (corridorY - sy);
                        int vComp = -(vel * sim.GravMul);
                        bool inp = (err - vComp * 2) > 0;
                        alive = StepFrame(ref sim, inp, out _);
                        if (alive) survived++;
                    }
                }
                bestSurvival = Math.Max(bestSurvival, survived);
            }

            // Strategy 4: Tree-search guided (pick best survival at each step)
            {
                var sim = postCollect.Clone();
                int survived = 0;
                bool alive = true;
                for (int f = 0; f < RECOVERY_HORIZON && alive; f++)
                {
                    // Pick the input that maximizes short-term survival
                    var sH = sim.Clone();
                    var sR = sim.Clone();
                    bool aliveH = StepFrame(ref sH, true, out _);
                    bool aliveR = StepFrame(ref sR, false, out _);
                    if (!aliveH && !aliveR) break;
                    if (!aliveH) { sim = sR; survived++; continue; }
                    if (!aliveR) { sim = sH; survived++; continue; }

                    // Both survive � use 6-deep tree search to pick
                    _shipTreeNodesExplored = 0;
                    int survHDeep = 1 + ShipTreeSearch(sH, Math.Min(6, RECOVERY_HORIZON - f - 1));
                    _shipTreeNodesExplored = 0;
                    int survRDeep = 1 + ShipTreeSearch(sR, Math.Min(6, RECOVERY_HORIZON - f - 1));
                    
                    if (survHDeep >= survRDeep)
                    {
                        sim = sH;
                        // Also consider corridor center steering as tiebreaker
                        if (survHDeep == survRDeep)
                        {
                            int corridorY = FindCorridorCenter(ref sim);
                            int sy = sim.Y_fixed >> 8;
                            int err = (sim.GravMul > 0) ? (sy - corridorY) : (corridorY - sy);
                            if (err < 0) sim = sR; // prefer getting closer to center
                        }
                    }
                    else
                    {
                        sim = sR;
                    }
                    survived++;
                }
                bestSurvival = Math.Max(bestSurvival, survived);
            }

            return bestSurvival;
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
            return FindCorridorCenter(ref s, CORRIDOR_LOOK_AHEAD_TILES, -1);
        }

        private int FindCorridorCenter(ref SimState s, int lookAhead)
        {
            return FindCorridorCenter(ref s, lookAhead, -1);
        }

        /// <summary>
        /// When overrideY >= 0, scan vertically from that Y instead of the
        /// ship's actual Y. This lets the PD controller discover corridors
        /// that are separated from the ship by a dividing wall.
        /// </summary>
        private int FindCorridorCenter(ref SimState s, int lookAhead, int overrideY)
        {
            int playerX_px = s.X_fixed >> 8;
            int playerY_px = overrideY >= 0 ? overrideY : (s.Y_fixed >> 8);
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

            // Scan vertically at current X AND ahead (lookAhead columns)
            int startTileX = centerX_px / TILE;
            int endTileX = Math.Min(startTileX + lookAhead, mapWidth - 1);

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
                        // Death tiles act as obstacles too � tile bottom is obstacle boundary.
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

                        // Tiles within the hitbox span ? route above or below
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
                        // Tiles above the hitbox ? tighten ceiling
                        else if (ty < topTile)
                        {
                            if (isSolid && blockBottom > ceilingY) ceilingY = blockBottom;
                            if (isDeath && blockBottom > ceilingY) ceilingY = blockBottom;
                        }
                        // Tiles below the hitbox ? tighten floor
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
            => SharedPhysics.IsDeathCollision(col);

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
                    // Both survive � PD controller: position + velocity damping
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
                    var deduped = new Dictionary<(int, int, bool, bool, int, int, int, bool), SimState>();
                    foreach (var nd in alive)
                    {
                        var key = (nd.Y_fixed, nd.VelY_fixed, nd.OnGround, nd.GravFlipped, nd.GameMode,
                                   nd.DualActive ? nd.P2_Y_fixed : 0, nd.DualActive ? nd.P2_VelY_fixed : 0,
                                   nd.DualActive && nd.P2_GravFlipped);
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
            { int _mo = (s.Mini && !s.GravFlipped) ? 4 : 0; pathPoints?.Add(((s.X_fixed >> 8) + 8, (s.Y_fixed >> 8) + _mo + 8)); }
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
                else if (chainJumps && (s.GameMode == 0 || s.GameMode == 2 || s.GameMode == 4) && s.VelY_fixed == 0 && s.OnGround)
                {
                    input = holdAfterLanding || QuickDangerCheck(s);
                }
                else if (chainJumps && s.GameMode == 4 && s.RobotJumpTime > 0)
                {
                    // Robot mid-jump: continue holding if holdAfterLanding
                    input = holdAfterLanding;
                }
                else if (chainJumps && s.GameMode == 6)
                {
                    // Wave: continuous hold/release each frame
                    input = holdAfterLanding;
                }
                else if (chainJumps && s.GameMode == 7 && !s.Orbed)
                {
                    // Swing: after initial flip, evaluate danger to decide next flip
                    input = QuickDangerCheck(s);
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

                { int _mo = (s.Mini && !s.GravFlipped) ? 4 : 0; pathPoints?.Add(((s.X_fixed >> 8) + 8, (s.Y_fixed >> 8) + _mo + 8)); }

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
            { int _mo = (s.Mini && !s.GravFlipped) ? 4 : 0; pathPoints?.Add(((s.X_fixed >> 8) + 8, (s.Y_fixed >> 8) + _mo + 8)); }
            bool initialJumpDone = (jumpFrame < 0);
            bool chainJumps = (jumpFrame >= 0) && !singleJumpOnly;
            bool startGravFlipped = s.GravFlipped; // track gravity portal hits
            bool isBallMode = (s.GameMode == 2); // ball flips change GravFlipped — don't confuse with portal hits
            bool isSpiderMode = (s.GameMode == 5); // spider teleport flips GravFlipped — not a portal event
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
                else if (chainJumps && s.GameMode == 4 && s.VelY_fixed == 0 && s.OnGround)
                {
                    // Robot: after landing, decide whether to jump again
                    input = holdAfterLanding || QuickDangerCheck(s);
                }
                else if (chainJumps && s.GameMode == 4 && s.RobotJumpTime > 0)
                {
                    // Robot mid-jump: continue holding if holdAfterLanding
                    input = holdAfterLanding;
                }
                else if (chainJumps && s.GameMode == 6)
                {
                    // Wave: continuous hold/release each frame
                    input = holdAfterLanding;
                }
                else if (chainJumps && s.GameMode == 7 && !s.Orbed)
                {
                    // Swing: after initial flip, evaluate danger to decide next flip
                    input = QuickDangerCheck(s);
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
                { int _mo = (s.Mini && !s.GravFlipped) ? 4 : 0; pathPoints?.Add(((s.X_fixed >> 8) + 8, (s.Y_fixed >> 8) + _mo + 8)); }
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
                    // Spider mode: teleport flips GravFlipped — not a portal event.
                    int portalBonus = (!isBallMode && !isSpiderMode && s.GravFlipped != startGravFlipped) ? LOOKAHEAD_HORIZON : 0;
                    return f + portalBonus;
                }
                if (endLevel) return LOOKAHEAD_HORIZON;
            }

            // Bonus for paths that went through a gravity portal
            int finalPortalBonus = (!isBallMode && !isSpiderMode && s.GravFlipped != startGravFlipped) ? LOOKAHEAD_HORIZON : 0;
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
            const int ELEV_DROP_THRESHOLD = 16; // 1 tile � detect walking off platform edges
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

        // -------------------------------------------------------------------
        //  FRAME SIMULATION � matches SimulateNumericStep + CubePhysics_Fresh
        // -------------------------------------------------------------------

        /// <summary>
        /// Simulate one frame with EXACT ordering matching the real simulator:
        /// Matching SimulatorWindow.SimulateNumericStep order:
        ///   1. sprite_collide()        � portals, pads at current X
        ///   2. x_movement()            � X advance (for ground support only)
        ///   3. (ground support check)  � verify floor at new X
        ///   4. REVERT to OLD X
        ///   5. movement()              � Y physics + eject at OLD X
        ///   6. x_movement_coll()       � forward wall check at OLD X, post-eject Y (slope skip)
        ///   7. RESTORE NEW X
        ///   8. death collision          � 5-point death check at NEW X (matching sim)
        /// </summary>
        private bool StepFrame(ref SimState s, bool input, out bool endLevel)
        {
            endLevel = false;

            // -- Clear Step2Ejected from previous frame (must be in StepFrame, not CubeEject,
            //    because CubeEject only runs for cube/robot/football modes — otherwise the
            //    flag persists across mode changes like UFO, causing sprHash coarsening
            //    during the entire non-cube section) --
            s.Step2Ejected = false;

            // -- Clear pending orb from previous frame --
            s.PendingOrbIndex = -1;
            s.PendingOrbSpriteId = -1;

            int oldX_fixed = s.X_fixed;
            int oldX_px = oldX_fixed >> 8;
            bool pressInput = input && !s.PrevInputHeld;

            // -- Dash end check (before sprite_collide, matching NES state_game.h line 372-374) --
            if (s.Dashing != 0 && !input)
            {
                s.VelY_fixed = 0;
                s.Dashing = 0;
            }

            // -- Orbed clear (before sprite_collide, matching NES state_game.h line 557-559) --
            if (s.Orbed && !input)
                s.Orbed = false;

            // -- STEP 1: PROCESS SPRITES at current X (sprite_collide) --
            // NES order: sprite_collide() at OLD X → cube_movement() (Y physics)
            // → x_movement() (X advance).  Orbs, pads, gravity/speed/mini portals
            // detected at OLD X.  Game mode portals are detected AFTER Y physics
            // at NEW X (matching sim's post-physics portal loop).

            bool orbHitThisFrame = false;
            endLevel = ProcessSprites(ref s, oldX_px, out orbHitThisFrame);
            if (endLevel) return true;

            // -- STEP 1b: ORB ACTIVATION at OLD X --
            if (!_dualP2Guard) { _p1OrbIndicesThisFrame ??= new(); _p1OrbIndicesThisFrame.Clear(); }
            if (s.PendingOrbIndex >= 0 && input)
            {
                // Save pre-activation state for the overlapping-tile sweep below.
                int activatedSid = s.PendingOrbSpriteId;
                int sweepNesX = oldX_px + 1;
                int sweepHbW = GetHitboxW(s.Mini);
                int sweepHbH = GetHitboxH(s.Mini);
                int sweepOrbOffY = (s.Mini && !s.GravFlipped) ? SharedPhysics.GetMiniCenterOffsetY(true) : 0;
                int sweepPlayerTop = (s.Y_fixed >> 8) + sweepOrbOffY;
                int sweepPlayerBottom = sweepPlayerTop + sweepHbH;
                int sweepPlayerRight = sweepNesX + sweepHbW;

#if !DISABLE_DEBUG_LOGGING
                PfLog($"[ORB_ACTIVATE] sid=0x{s.PendingOrbSpriteId:X2} gravFlipped={s.GravFlipped} mini={s.Mini}");
#endif
                // Record orb hit for targeted backtrack (only during real execution)
                if (_speculativeDepth == 0)
                    _hitOrbHistory.Add(s.PendingOrbIndex);

                if (IsDashOrb(s.PendingOrbSpriteId))
                {
                    ApplyDashOrb(ref s, s.PendingOrbSpriteId);
                    s.ProcessedSprites.Add(s.PendingOrbIndex);
                    if (!_dualP2Guard) _p1OrbIndicesThisFrame!.Add(s.PendingOrbIndex);
                }
                else if (IsSpiderOrb(s.PendingOrbSpriteId))
                {
                    bool goUp = (s.PendingOrbSpriteId == 0x54);
                    ApplySpiderTeleport(ref s, goUp);
                    s.Orbed = true; // NES sets orbed after spider orb teleport (blocks immediate jump)
                    s.ProcessedSprites.Add(s.PendingOrbIndex);
                    if (!_dualP2Guard) _p1OrbIndicesThisFrame!.Add(s.PendingOrbIndex);
                }
                else
                {
                    ApplyOrbSprite(ref s, s.PendingOrbSpriteId);
                    // Black orb in spider mode: enable hold-to-teleport
                    if (IsBlackOrb(s.PendingOrbSpriteId) && s.GameMode == 5)
                        s.BlackOrbed = true;
                    bool isMultiOrb = (s.PendingOrbSpriteId == 0x7B || s.PendingOrbSpriteId == 0x7C);
                    if (!isMultiOrb)
                    {
                        s.ProcessedSprites.Add(s.PendingOrbIndex);
                        if (!_dualP2Guard) _p1OrbIndicesThisFrame!.Add(s.PendingOrbIndex);
                    }
                }

                // Mark ALL other overlapping tiles of the same orb type as processed.
                // Multi-tile orbs that lack spriteAnchors entries appear as independent
                // tiles; without this sweep the player could re-trigger the same physical
                // orb on an adjacent tile in a later frame.
                bool isMultiOrbSweep = (activatedSid == 0x7B || activatedSid == 0x7C);
                if (!isMultiOrbSweep)
                {
                    int orbSweepStart = SpriteLowerBound(oldX_px - 4 * TILE);
                    for (int _oi = orbSweepStart; _oi < _spritesArr.Length; _oi++)
                    {
                        ref readonly var sp = ref _spritesArr[_oi];
                        if (sp.AnchorX_px - TILE > sweepPlayerRight + TILE) break;
                        if (sp.SpriteId != activatedSid) continue;
                        if (s.ProcessedSprites.Contains(sp.Index)) continue;
                        bool xO = !(sweepPlayerRight < sp.HitLeft || sp.HitRight < sweepNesX);
                        bool yO = !(sweepPlayerBottom < sp.HitTop || sp.HitBottom < sweepPlayerTop);
                        if (xO && yO)
                        {
                            s.ProcessedSprites.Add(sp.Index);
                            if (!_dualP2Guard) _p1OrbIndicesThisFrame!.Add(sp.Index);
                        }
                    }
                }

                s.PendingOrbIndex = -1;
                s.PendingOrbSpriteId = -1;
                orbHitThisFrame = true;
                if (_speculativeDepth == 0) _cubeJumpedThisStep = true; // Fix 23: orb activation consumes input — preserve True
            }

            // -- STEP 2: Compute new X (applied at the end, matching NES
            //    where x_movement() runs AFTER all collision checks) --
            int newX_fixed = s.X_fixed + s.VelX_fixed;

#if !DISABLE_DEBUG_LOGGING
            if ((oldX_px >= 6300 && oldX_px <= 6400) || (oldX_px >= 6850 && oldX_px <= 6900))
                PfLog($"[X_TRACK] oldX=0x{oldX_fixed:X} ({oldX_px}px) velX=0x{s.VelX_fixed:X} newX=0x{newX_fixed:X} ({newX_fixed >> 8}px)");
#endif

            // NOTE: The old VerifyGroundSupport check at new-X has been removed.
            // The NES has no such look-ahead � ground loss is detected naturally
            // by gravity pulling the player down and CubeEject finding no floor.
            // With no GRAV_SKIP (matching NES), grounded frames apply gravity
            // (+0x6B), and CubeEject snaps the player back each frame.

            // -- STEP 5: Y PHYSICS + EJECT at OLD X (movement) --
#if !DISABLE_DEBUG_LOGGING
            PfLog($"[PHYSICS] Mode={s.GameMode}, gravity={( s.GravFlipped ? 0xFF : 0x00 ):X2}, mini={( s.Mini ? 1 : 0 )}, table_idx={( s.Mini ? 4 : 0 )}");
            if (s.GameMode == 1 && s.Mini)
            {
                int px = (int)(s.X_fixed >> 8);
                int py = (int)(s.Y_fixed >> 8);
                PfLog($"[MINISHIP_Y] X={px} Y={py} velY=0x{(s.VelY_fixed & 0xFFFF):X4}");
            }
#endif
            if (s.GameMode == 0) // Cube mode
            {
                CubeGravity(ref s);

                // Ceiling proximity check:
                // After gravity, if gravity is flipped and player is moving toward ceiling,
                // check collision at Y-1 to detect boundary hits that CubeEject would miss.
                // NES cube_movement has NO ceiling spike check � spikes handled by
                // bg_coll_floor_spikes (4-corner) which runs for all modes.
                if (s.GravFlipped)
                {
                    int hbW_chk = GetHitboxW(s.Mini);
                    int hbH_chk = GetHitboxH(s.Mini);
                    int hbOffY_chk = SharedPhysics.GetMiniCenterOffsetY(s.Mini);
                    int collX_chk = s.X_fixed >> 8;
                    int testY_chk = (s.Y_fixed >> 8) + hbOffY_chk - 1;
                    var (ceilHit, ceilBotY_prox, _) = CheckCeiling(collX_chk, testY_chk, hbW_chk, hbH_chk);
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
                CubeEject(ref s, input, out ejectDied);
                if (ejectDied)
                {
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[EJECT_DEATH] X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
#endif
                    if (_speculativeDepth == 0) { _lastDeathReason = "EJECT_DEATH"; _lastDeathX = s.X_fixed >> 8; _lastDeathY = s.Y_fixed >> 8; }
                    s.DeathType = 2;
                    return false;
                }
                // col_end logic (SlopeJumpHigher, counter setting) is now
                // handled inside CubeEject based on input + game mode.

                // Center death check at post-eject Y (NES: bg_coll_death in runthecolls
                // reads Generic.y which was set from currplayer_y after cube_eject)
                if (CheckCenterPointDeath(ref s))
                {
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[CENTER_DEATH] X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px (post-eject)");
#endif
                    if (_speculativeDepth == 0) { _lastDeathReason = "CENTER_DEATH"; _lastDeathX = s.X_fixed >> 8; _lastDeathY = s.Y_fixed >> 8; }
                    s.DeathType = 3;
                    return false;
                }

                // Jump input — two paths from gamemode_cube.h lines 50-65:
                // Path 1: hold A && !jblocked && !orbed → jump
                // Path 2: press A && jblocked → jump (no orbed check!)
                if (s.VelY_fixed == 0)
                {
                    bool doJump = false;
                    if (input && !s.JBlocked && !s.FBlocked && !s.Orbed)
                        doJump = true;   // Path 1: hold-to-jump
                    else if (pressInput && (s.JBlocked || s.FBlocked))
                        doJump = true;   // Path 2: press-to-jump on J/F block
                    if (doJump)
                    {
                        s.VelY_fixed = GetJumpVel(s.Mini) * s.GravMul;
                        s.OnGround = false;
                        if (_speculativeDepth == 0) _cubeJumpedThisStep = true;
                        // NES slope_jump_check: add extra velocity when jumping off a slope
                        PfSlopeJumpCheck(ref s);
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[JUMP] VelY=0x{s.VelY_fixed:X4} gravMul={s.GravMul} mini={s.Mini} jblocked={s.JBlocked}");
#endif
                    }
                }

                // Step 5: UpdateSlopeCounters_Fresh — decrement counters, fire apply_slope_vel
                // Must be AFTER jump check (matching SIM order) so velY is still 0 when jump fires
                PfUpdateSlopeCounters_Fresh(ref s);
            }
            else if (s.GameMode == 1) // Ship mode
            {
                ShipGravityAndThrust(ref s, input, out bool shipGravDied);
                if (shipGravDied)
                {
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[GRAV_CEIL_SPIKE_DEATH] ship X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
#endif
                    if (_speculativeDepth == 0) { _lastDeathReason = "GRAV_CEIL_SPIKE_DEATH"; _lastDeathX = s.X_fixed >> 8; _lastDeathY = s.Y_fixed >> 8; }
                    s.DeathType = 2;
                    return false;
                }

                ShipEject(ref s, input, out bool shipDied);
                if (shipDied)
                {
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[EJECT_DEATH] ship X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
#endif
                    if (_speculativeDepth == 0) { _lastDeathReason = "EJECT_DEATH"; _lastDeathX = s.X_fixed >> 8; _lastDeathY = s.Y_fixed >> 8; }
                    s.DeathType = 2;
                    return false;
                }
                // Step 5: UpdateSlopeCounters_Fresh — decrement counters, fire apply_slope_vel
                PfUpdateSlopeCounters_Fresh(ref s);

                // NO center death check here — NES ship_movement does NOT run
                // bg_coll_death inside the mode handler.  Step 7c handles it.
            }
            else if (s.GameMode == 2) // Ball mode
            {
                // -- Matching SIM BallPhysics_Fresh() order --
                // NES/SIM order: grounded spike check ? flip ? clear_switched ? gravity
                //                ? [2-frame cooldown skip] ? velocity zeroing ? eject

                // 0. Grounded spike death check � SIM runs CheckCollisionDown
                //    for the grounded probe EVERY frame (before flip/gravity).
                //    CheckCollisionDown has floor-spike death side-effect.
                //    Normal gravity only (SIM's CheckCollisionUp discards spikeDeath).
                if (!s.GravFlipped)
                {
                    int hbW_g = GetHitboxW(s.Mini);
                    int hbH_g = GetHitboxH(s.Mini);
                    int hbOffY_g = GetHitboxOffsetY(2, s.Mini, s.GravFlipped);
                    int playerBottom_g = (s.Y_fixed >> 8) + hbOffY_g + hbH_g;
                    var (_, _, groundedSpike) = CheckFloor(s.X_fixed >> 8, playerBottom_g, hbW_g, 2);
                    if (groundedSpike)
                    {
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[BALL_GROUNDED_SPIKE_DEATH] X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
                        _lastDeathReason = "BALL_GROUNDED_SPIKE_DEATH"; _lastDeathX = s.X_fixed >> 8; _lastDeathY = s.Y_fixed >> 8;
#endif
                        s.DeathType = s.DeathType >= 11 ? s.DeathType : (byte)6;
                        return false;
                    }
                }

                // 1. Flip check with input buffering (BEFORE gravity � matches SIM/NES).
                //    SIM's ball physics buffers the press (orbBufferActive) and flips
                //    on landing while hold persists.  The SIM playback holds for
                //    PF_BALL_HOLD_FRAMES (8) after each PF true.  Mirror this:
                //    - input=1 & grounded ? flip immediately
                //    - input=1 & !grounded ? buffer for up to 8 frames
                //    - buffer>0 & grounded & !switched ? flip (buffered landing)

                // Refresh OnGround at current position — prevents stale grounding
                // when X has advanced past a platform edge since the last eject.
                // SIM re-evaluates grounding fresh each frame via BallIsGrounded;
                // PF must do the same to match. Keep OnGround true on slopes
                // (SlopeWasOnCounter > 0) since BallIsGrounded can't detect them.
                if (s.OnGround && !BallIsGrounded(ref s) && s.SlopeWasOnCounter == 0)
                    s.OnGround = false;

                {
                    bool shouldFlip = false;
                    if (input && s.BallFlipCooldown == 0)
                    {
                        // BallIsGrounded uses CheckFloor which doesn't detect slopes.
                        // NES ball_eject sets OnGround when on a slope, so use that
                        // as a fallback — matches the NES persistent-flag behavior.
                        if (BallIsGrounded(ref s) || s.OnGround)
                        {
                            shouldFlip = true;
                        }
                        else
                        {
                            s.BallInputBuffer = 8; // PF_BALL_HOLD_FRAMES
                        }
                    }
                    else if (s.BallInputBuffer > 0 && s.BallFlipCooldown == 0 && (BallIsGrounded(ref s) || s.OnGround))
                    {
                        shouldFlip = true;
                    }

                    if (shouldFlip)
                    {
                        s.GravFlipped = !s.GravFlipped;
                        s.GravMul = s.GravFlipped ? -1 : 1;
                        s.VelY_fixed = BallSwitchVel(s.Mini) * s.GravMul;
                        s.OnGround = false;
                        // NOTE: Do NOT clear WasZeroedByCollision here — SIM's
                        // BallPhysics_Fresh / InvertGravity_Fresh does not touch it.
                        // The flag is properly cleared in ApplyPortalSprite when
                        // the gamemode changes (matching SIM's CheckGameModePortals).
                        s.BallFlipCooldown = 1;  // ball_switched = true
                        s.BallInputBuffer = 0;
                        s.BallCooldownFrames = 2; // SIM skips vel zeroing + eject for 2 frames after flip
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[BALL_FLIP] gravFlipped={s.GravFlipped} gravMul={s.GravMul} VelY=0x{s.VelY_fixed:X4} mini={s.Mini}");
#endif
                    }
                    else if (s.BallInputBuffer > 0)
                    {
                        s.BallInputBuffer--;
                    }
                }

                // 2. Clear ball_switched when button released.
                //    SIM clears when !holdJump.  In PF, "hold" = input || buffer>0.
                if (s.BallFlipCooldown != 0 && !input && s.BallInputBuffer == 0)
                {
                    s.BallFlipCooldown = 0;
                }

                // 3. Gravity (common_gravity_routine) � runs AFTER flip so it uses
                //    the post-flip VelY (matching SIM/NES ordering).
                BallGravityStep(ref s);

                // 4. 2-frame cooldown after flip: skip velocity zeroing and eject.
                //    SIM's BallPhysics_Fresh returns early during cooldown, preventing
                //    the eject from snapping the ball back to the surface it just
                //    flipped away from.
                if (s.BallCooldownFrames > 0)
                {
                    s.BallCooldownFrames--;
                }
                else
                {
                    // 5. Velocity zeroing: prevents velocity accumulation when grounded.
                    //    Normal gravity + VelY<0 ? ceiling check ? zero VelY + snap Y
                    //    Flipped gravity + VelY>0 ? floor check ? zero VelY
                    {
                        bool velZeroDied = false;
                        BallVelocityZeroing(ref s, out velZeroDied);
                        if (velZeroDied)
                        {
#if !DISABLE_DEBUG_LOGGING
                            PfLog($"[BALL_VELZERO_DEATH] X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
                            _lastDeathReason = "BALL_VELZERO_DEATH"; _lastDeathX = s.X_fixed >> 8; _lastDeathY = s.Y_fixed >> 8;
#endif
                            s.DeathType = s.DeathType >= 11 ? s.DeathType : (byte)6;
                            return false;
                        }
                    }

                    // 6. Ball eject � NES-accurate tile probe and eject math
                    //    Matches original ball_eject() which calls bg_coll_U then bg_coll_D
                    //    with velocity gating inside each function.
                    {
                        bool died = false;
                        BallEject(ref s, input, out died);
                        if (died)
                        {
#if !DISABLE_DEBUG_LOGGING
                            PfLog($"[BALL_EJECT_DEATH] X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
                            _lastDeathReason = "BALL_EJECT_DEATH"; _lastDeathX = s.X_fixed >> 8; _lastDeathY = s.Y_fixed >> 8;
#endif
                            s.DeathType = s.DeathType >= 11 ? s.DeathType : (byte)6;
                            return false;
                        }
                    }
                    // Step 5: UpdateSlopeCounters_Fresh — decrement counters, fire apply_slope_vel
                    PfUpdateSlopeCounters_Fresh(ref s);
                }

                // NO center death check here — NES ball_movement does NOT run
                // bg_coll_death inside the mode handler.  Step 7c handles it.
            }
            else if (s.GameMode == 3) // UFO mode
            {
                // -- UFO GRAVITY --
                // Same as CommonGravityRoutine_Fresh with UFO constants.
                // UFO uses tap-to-jump (not hold-to-fly like ship).
                UfoGravityStep(ref s);

                // -- CEILING PROXIMITY CHECK (non-flipped gravity only) --
                // NES ufo_movement does NOT have a ceiling spike check here.
                // Spikes are handled by bg_coll_floor_spikes (4-corner) for all modes.
                // This check zeros upward velocity AND snaps Y to the ceiling surface,
                // matching SIM UfoPhysics_Fresh which snaps playerY_fixed to clear
                // sub-pixel drift (prevents trajectory divergence over many frames).
                // Runs unconditionally — needed for both normal and flipped gravity.
                {
                    int hbW_chk = GetHitboxW(s.Mini);
                    int hbH_chk = GetHitboxH(s.Mini);
                    int hbOffY_chk = GetHitboxOffsetY(s.GameMode, s.Mini, s.GravFlipped);
                    int collX_chk = s.X_fixed >> 8;
                    int testY_chk = (s.Y_fixed >> 8) + hbOffY_chk - 1;
                    var (ceilHit, ceilBotY_chk, _) = CheckCeiling(collX_chk, testY_chk, hbW_chk, hbH_chk);
                    if (ceilHit && s.VelY_fixed < 0)
                    {
                        s.Y_fixed = (ceilBotY_chk - hbOffY_chk) << 8;
                        s.VelY_fixed = 0;
                    }
                }

                // -- UFO EJECT (shared with Ship: ceiling + floor, no velocity guard) --
                ShipEject(ref s, input, out bool ufoDied);
                if (ufoDied)
                {
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[EJECT_DEATH] ufo X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
#endif
                    if (_speculativeDepth == 0) { _lastDeathReason = "EJECT_DEATH"; _lastDeathX = s.X_fixed >> 8; _lastDeathY = s.Y_fixed >> 8; }
                    s.DeathType = 2;
                    return false;
                }
                // Step 5: UpdateSlopeCounters_Fresh — decrement counters, fire apply_slope_vel
                PfUpdateSlopeCounters_Fresh(ref s);

                // -- UFO JUMP (tap-to-jump, can jump mid-air) --
                // NES: orb activation consumes the press, so UFO jump doesn't fire
                // on the same frame an orb was hit.
                if (input && !orbHitThisFrame)
                {
                    int jumpVel = (int)UfoJumpVel(s.Mini) * -s.GravMul; // against gravity
                    s.VelY_fixed = jumpVel;
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[UFO_JUMP] VelY=0x{s.VelY_fixed:X4} gravMul={s.GravMul} mini={s.Mini}");
#endif
                }

                // NO center death check here — NES ufo_movement does NOT run
                // bg_coll_death inside the mode handler.  The common post-physics
                // path (Step 7c) handles it for all modes, matching the SIM's
                // UfoPhysics_Fresh which also has no death check in the UFO block.
            }
            else if (s.GameMode == 4) // Robot mode
            {
                // Robot uses cube gravity/eject but with hold-to-jump mechanics.
                // NES order (gamemode_cube.h): jump-continue → gravity → eject → jump-start
                
                // 1. Continue jump if timer active and holding
                if (s.RobotJumpTime > 0 && !s.Orbed)
                {
                    s.RobotJumpTime--;
                    if (input)
                    {
                        // Reapply jump velocity every frame while holding
                        s.VelY_fixed = ROBOT_JUMP_VEL * s.GravMul;
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[ROBOT_HOLD] VelY=0x{s.VelY_fixed:X4} time={s.RobotJumpTime}");
#endif
                    }
                    else
                    {
                        // Released button — stop jump immediately
                        s.RobotJumpTime = 0;
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[ROBOT_RELEASE] jump cancelled");
#endif
                    }
                }
                
                // 2. Gravity (same as cube)
                CubeGravity(ref s);

                // 3. Ceiling proximity check (same as cube — needed for flipped gravity)
                if (s.GravFlipped)
                {
                    int hbW_chk = GetHitboxW(s.Mini);
                    int hbH_chk = GetHitboxH(s.Mini);
                    int hbOffY_chk = SharedPhysics.GetMiniCenterOffsetY(s.Mini);
                    int collX_chk = s.X_fixed >> 8;
                    int testY_chk = (s.Y_fixed >> 8) + hbOffY_chk - 1;
                    var (ceilHit, ceilBotY_prox, _) = CheckCeiling(collX_chk, testY_chk, hbW_chk, hbH_chk);
                    if (ceilHit && s.VelY_fixed < 0)
                    {
                        int newY_prox = ceilBotY_prox - hbOffY_chk - 1;
                        s.Y_fixed = newY_prox << 8;
                        s.VelY_fixed = 0;
                        s.OnGround = true;
                        s.WasZeroedByCollision = true;
                    }
                }

                // 4. Eject (same as cube)
                bool robotEjectDied = false;
                CubeEject(ref s, input, out robotEjectDied);
                if (robotEjectDied)
                {
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[EJECT_DEATH] robot X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
#endif
                    if (_speculativeDepth == 0) { _lastDeathReason = "EJECT_DEATH"; _lastDeathX = s.X_fixed >> 8; _lastDeathY = s.Y_fixed >> 8; }
                    s.DeathType = 2;
                    return false;
                }
                // col_end logic (SlopeJumpHigher, counter setting) is now
                // handled inside CubeEject based on input + game mode.

                // 5. Center death check (same as cube)
                if (CheckCenterPointDeath(ref s))
                {
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[CENTER_DEATH] robot X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
#endif
                    if (_speculativeDepth == 0) { _lastDeathReason = "CENTER_DEATH"; _lastDeathX = s.X_fixed >> 8; _lastDeathY = s.Y_fixed >> 8; }
                    s.DeathType = 3;
                    return false;
                }

                // 6. Jump start — only when grounded and on a fresh press.
                // Famidash gates robot jump start through the per-frame press bit
                // (gamemode_cube.h sets cube_data bit 0b100 from controllingplayer->press).
                if (pressInput && s.VelY_fixed == 0 && !s.Orbed)
                {
                    s.VelY_fixed = ROBOT_JUMP_VEL * s.GravMul;
                    s.RobotJumpTime = ROBOT_JUMP_TIME;
                    s.OnGround = false;
                    // NES slope_jump_check: add extra velocity when jumping off a slope
                    PfSlopeJumpCheck(ref s);
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[ROBOT_JUMP] VelY=0x{s.VelY_fixed:X4} time={s.RobotJumpTime} gravMul={s.GravMul}");
#endif
                }

                // UpdateSlopeCounters — decrement counters, fire apply_slope_vel
                // Must be AFTER jump check (matching SIM order) so velY is still 0 when jump fires
                PfUpdateSlopeCounters_Fresh(ref s);
            }
            else if (s.GameMode == 6) // Wave mode
            {
#if !DISABLE_DEBUG_LOGGING
                PfLog($"[WAVE_PHYS] START X={s.X_fixed >> 8} Y={s.Y_fixed >> 8} velY=0x{s.VelY_fixed:X4} wasZeroed={s.WasZeroedByCollision} mini={s.Mini} grav={s.GravFlipped}");
#endif
                // Wave has no gravity — velocity is derived from VelX.
                // Normal: VelY = ±VelX; Mini: VelY = ±(VelX << 1).
                // Gravity-flipped inverts the default direction.
                // Holding input negates velocity (changes diagonal direction).
                
                // Calculate base velocity from horizontal speed
                int baseVelY = s.Mini ? (s.VelX_fixed << 1) : s.VelX_fixed;
                if (s.GravFlipped) baseVelY = -baseVelY;
                
                // Only recalculate velocity if not on a surface (wasZeroed means walking on surface)
                if (!s.WasZeroedByCollision)
                    s.VelY_fixed = baseVelY;
                s.WasZeroedByCollision = false;
                
                // Input inverts direction
                if (input) s.VelY_fixed = -s.VelY_fixed;
                
#if !DISABLE_DEBUG_LOGGING
                PfLog($"[WAVE_PHYS] postCalc velY=0x{s.VelY_fixed:X4} hold={input}");
#endif
                // Apply movement
                s.Y_fixed += s.VelY_fixed;
                
#if !DISABLE_DEBUG_LOGGING
                PfLog($"[WAVE_PHYS] postMove Y={s.Y_fixed >> 8} Y_fixed=0x{s.Y_fixed:X4}");
#endif
                // Wave eject: check collision based on velocity direction
                // Wave uses special X offsets: +10 when moving up, +4 when moving down
                // Hitbox is 8 wide for collision; height depends on mini
                bool waveDied = false;
                WaveEject(ref s, input, out waveDied);
                if (waveDied)
                {
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[WAVE_EJECT_DEATH] X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px vel=0x{s.VelY_fixed:X4}");
#endif
                    if (_speculativeDepth == 0) { _lastDeathReason = "WAVE_EJECT_DEATH"; _lastDeathX = s.X_fixed >> 8; _lastDeathY = s.Y_fixed >> 8; }
                    s.DeathType = 2;
                    return false;
                }
                // UpdateSlopeCounters_Fresh — SIM's WavePhysics_Fresh calls this after eject.
                // Without it, slope counters from a previous section persist through the
                // entire wave section, causing stale non-zero counters at the next mode
                // transition (forward collision skip divergence).
                PfUpdateSlopeCounters_Fresh(ref s);
                
                if (CheckDeathCollision(ref s))
                {
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[CENTER_DEATH] wave X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
                    _lastDeathReason = "CENTER_DEATH"; _lastDeathX = s.X_fixed >> 8; _lastDeathY = s.Y_fixed >> 8;
#endif
                    s.DeathType = 3;
                    return false;
                }
                
                // --- Wave-specific slope death checks (NES bg_coll_death + bg_coll_R) ---
                // NES x_movement sets Generic to (playerX, playerY, WAVE_WIDTH=8, WAVE_HEIGHT=8).
                // bg_coll_death probes center point, bg_coll_R probes right edge — both call
                // bg_coll_slope() and kill if hit (dblocked doesn't prevent slope death in
                // bg_coll_death; only skips bg_coll_U_D_checks).
                // These catch the wave entering a slope horizontally, which the U/D eject
                // slope probes miss.
                {
                    int wPx = s.X_fixed >> 8;
                    int wPy = s.Y_fixed >> 8;
                    const int WAVE_W = 8, WAVE_H = 8;
                    
                    // 1) Center-point slope check (bg_coll_death → bg_coll_slope)
                    //    Center = (playerX + width/2 - 1, playerY + height/2)
                    int cX = wPx + (WAVE_W >> 1) - 1;
                    int cY = wPy + (WAVE_H >> 1);
                    int cTileX = cX / TILE, cTileY = cY / TILE;
                    var cCol = GetTileCollision(cTileX, cTileY);
                    if (cCol >= MetatileCollision.COL_SLOPE_RD45 && cCol <= MetatileCollision.COL_SLOPE_LU66_TOP)
                    {
                        if (!(!s.Mini && cCol == MetatileCollision.COL_SLOPE_LU45) &&
                            !(s.Mini && (cCol == MetatileCollision.COL_SLOPE_LU66_TOP || cCol == MetatileCollision.COL_SLOPE_LU66_BOT)))
                        {
                            var (cHit, _, _) = PfSlopeCalc(cX, cY, cCol);
                            if (cHit)
                            {
#if !DISABLE_DEBUG_LOGGING
                                PfLog($"[WAVE_DEATH] center slope X={wPx} Y={wPy} tile=({cTileX},{cTileY}) col={cCol}");
                                _lastDeathReason = "WAVE_CENTER_SLOPE"; _lastDeathX = wPx; _lastDeathY = wPy;
#endif
                                s.DeathType = 3;
                                return false;
                            }
                        }
                    }
                    
                    // 2) Right-edge slope check (bg_coll_R → bg_side_coll_common → bg_coll_slope)
                    //    NES: skip when slope counters active
                    if ((s.SlopeWasOnCounter | s.SlopeFrames) == 0)
                    {
                        int rX = wPx + WAVE_W;  // right edge
                        int rY = wPy + (WAVE_H >> 1);  // center Y
                        int rTileX = rX / TILE, rTileY = rY / TILE;
                        var rCol = GetTileCollision(rTileX, rTileY);
                        if (rCol >= MetatileCollision.COL_SLOPE_RD45 && rCol <= MetatileCollision.COL_SLOPE_LU66_TOP)
                        {
                            if (!(!s.Mini && rCol == MetatileCollision.COL_SLOPE_LU45) &&
                                !(s.Mini && (rCol == MetatileCollision.COL_SLOPE_LU66_TOP || rCol == MetatileCollision.COL_SLOPE_LU66_BOT)))
                            {
                                var (rHit, _, _) = PfSlopeCalc(rX, rY, rCol);
                                if (rHit)
                                {
#if !DISABLE_DEBUG_LOGGING
                                    PfLog($"[WAVE_DEATH] R-edge slope X={wPx} Y={wPy} tile=({rTileX},{rTileY}) col={rCol}");
                                    _lastDeathReason = "WAVE_REDGE_SLOPE"; _lastDeathX = wPx; _lastDeathY = wPy;
#endif
                                    s.DeathType = 3;
                                    return false;
                                }
                            }
                        }
                    }
                }
            }
            else if (s.GameMode == 5) // Spider mode
            {
                // Spider uses same gravity constants as ball but fixed values
                SpiderGravityStep(ref s);

                // Spider eject
                int spiderOffY = s.GravFlipped ? -2 : 1;
                int spiderEjectY = (s.Y_fixed >> 8) + spiderOffY;
                SpiderEject(ref s, spiderEjectY, input, out bool spiderEjectDied);
                if (spiderEjectDied)
                {
                    s.DeathType = 2;
                    return false;
                }
                PfUpdateSlopeCounters_Fresh(ref s);

                // Spider grounded = velY == 0
                s.OnGround = (s.VelY_fixed == 0);

                // Teleport input: when grounded and not orbed, flip gravity and scan to opposite surface
                // BlackOrbed allows teleport even while Orbed (hold-to-teleport after black orb)
                // NES order: gravity → eject → teleport → death check (death check is AFTER spider_movement returns)
                bool canTeleport = (s.VelY_fixed == 0) && (!s.Orbed || s.BlackOrbed);
                if (input && canTeleport)
                {
                    if (!s.GravFlipped)
                    {
                        // Normal gravity → teleport to ceiling
                        s.GravFlipped = true;
                        s.GravMul = -1;
                        SpiderScanUp(ref s);
                        s.VelY_fixed = 0;
                    }
                    else
                    {
                        // Inverted gravity → teleport to floor
                        s.GravFlipped = false;
                        s.GravMul = 1;
                        SpiderScanDown(ref s);
                        s.VelY_fixed = 0;
                    }
                    s.BlackOrbed = false;
                    s.Orbed = true; // Prevent immediate re-teleport
                    // Snap camera to new position (NES calls process_y_scroll in scan loop)
                    SnapCameraToPlayerY(ref s);
                }
                else if (!input)
                {
                    s.BlackOrbed = false;
                    s.Orbed = false;
                }

                // Death check AFTER teleport to match NES order
                // (NES checks death after spider_movement() returns, which includes teleport)
                if (CheckDeathCollision(ref s))
                {
                    s.DeathType = 3;
                    return false;
                }
            }
            else if (s.GameMode == 7) // Swingcopter mode
            {
                // Swing reuses ball physics with different constants and flip-on-press
                // (no velocity impulse, no grounded requirement, can flip mid-air)
                SwingGravityStep(ref s);

                // Ship-style eject: always check BOTH ceiling and floor (no velocity guard)
                // BallVelocityZeroing is redundant since ShipEject is unconditional.
                // so swingcopter navigates corridors like ship/UFO instead of clipping into ceiling.
                {
                    bool died = false;
                    ShipEject(ref s, input, out died);
                    if (died)
                    {
                        s.DeathType = 6;
                        return false;
                    }
                }
                PfUpdateSlopeCounters_Fresh(ref s);

                // Swing gravity flip: press (rising edge) → flip gravity
                // NES uses controllingplayer->press (rising edge, not hold).
                // Use PrevInputHeld for edge detection so a held button from a
                // previous mode (e.g. robot) doesn't cause a spurious flip.
                if (input && !s.PrevInputHeld && !s.Orbed)
                {
                    s.GravFlipped = !s.GravFlipped;
                    s.GravMul = s.GravFlipped ? -1 : 1;
                }
                // NES: ufo_orbed is cleared every frame at end of ball_movement
                s.Orbed = false;

                if (CheckDeathCollision(ref s))
                {
                    s.DeathType = 3;
                    return false;
                }
            }
            else if (s.GameMode == 8) // Ninja mode — cube physics with triple jump
            {
                // Ninja uses cube gravity, cube eject, cube center death — identical to cube/robot.
                // Key difference: ninja can jump up to 3 times in the air (ninjajumps counter).
                // Jump count resets when grounded (VelY == 0). Input is press-only (tap, no hold buffer).
                // Uses cube jump velocity (not robot); shares cube pad/orb column.

                // 1. Reset ninja jumps if grounded and not pressing jump
                // (NES: ninjajumps = 3 when onGround && !pressJump — matching SIM)
                if (s.OnGround && !pressInput)
                    s.NinjaJumps = NINJA_MAX_JUMPS;

                // 2. Gravity (same as cube)
                CubeGravity(ref s);

                // 3. Ceiling proximity check (needed for flipped gravity — same as cube/robot)
                if (s.GravFlipped)
                {
                    int hbW_chk = GetHitboxW(s.Mini);
                    int hbH_chk = GetHitboxH(s.Mini);
                    int hbOffY_chk = SharedPhysics.GetMiniCenterOffsetY(s.Mini);
                    int collX_chk = s.X_fixed >> 8;
                    int testY_chk = (s.Y_fixed >> 8) + hbOffY_chk - 1;
                    var (ceilHit, ceilBotY_prox, _) = CheckCeiling(collX_chk, testY_chk, hbW_chk, hbH_chk);
                    if (ceilHit && s.VelY_fixed < 0)
                    {
                        int newY_prox = ceilBotY_prox - hbOffY_chk - 1;
                        s.Y_fixed = newY_prox << 8;
                        s.VelY_fixed = 0;
                        s.OnGround = true;
                        s.WasZeroedByCollision = true;
                    }
                }

                // 4. Eject (same as cube)
                bool ninjaEjectDied = false;
                CubeEject(ref s, input, out ninjaEjectDied);
                if (ninjaEjectDied)
                {
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[EJECT_DEATH] ninja X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
#endif
                    if (_speculativeDepth == 0) { _lastDeathReason = "EJECT_DEATH"; _lastDeathX = s.X_fixed >> 8; _lastDeathY = s.Y_fixed >> 8; }
                    s.DeathType = 2;
                    return false;
                }

                // 5. Center death check (same as cube)
                if (CheckCenterPointDeath(ref s))
                {
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[CENTER_DEATH] ninja X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
#endif
                    if (_speculativeDepth == 0) { _lastDeathReason = "CENTER_DEATH"; _lastDeathX = s.X_fixed >> 8; _lastDeathY = s.Y_fixed >> 8; }
                    s.DeathType = 3;
                    return false;
                }

                // 6. Jump — press-only (no hold buffer). Can jump while airborne if jumps remain.
                // NES: ninja checks press (not hold), ninjajumps > 0, !orbhitonthisframe, !hblocked, dashing==0
                if (pressInput && s.NinjaJumps > 0 && !orbHitThisFrame && !s.HBlocked && s.Dashing == 0)
                {
                    s.VelY_fixed = GetJumpVel(s.Mini) * s.GravMul;
                    s.NinjaJumps--;
                    s.OnGround = false;
                    // NES slope_jump_check: add extra velocity when jumping off a slope
                    PfSlopeJumpCheck(ref s);
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[NINJA_JUMP] VelY=0x{s.VelY_fixed:X4} jumpsLeft={s.NinjaJumps} gravMul={s.GravMul} mini={s.Mini}");
#endif
                }

                // 7. UpdateSlopeCounters — AFTER jump check (matching NES/SIM order)
                PfUpdateSlopeCounters_Fresh(ref s);
            }
            else if (s.GameMode == 9) // Pogo mode
            {
                // Pogo uses swing gravity + ball-style (velocity-gated) eject + auto-bounce
                SwingGravityStep(ref s);

                // No ball cooldown for pogo (pogo has no flip mechanic)

                // Velocity zeroing (same as ball — prevents vel accumulation when grounded)
                {
                    bool velZeroDied = false;
                    BallVelocityZeroing(ref s, out velZeroDied);
                    if (velZeroDied)
                    {
                        s.DeathType = s.DeathType >= 11 ? s.DeathType : (byte)6;
                        return false;
                    }
                }

                // Ball eject with pogo bounce — save pre-eject velocity for bounce formula
                {
                    int preEjectVelY = s.VelY_fixed;
                    bool wasOnGround = s.OnGround;
                    bool died = false;
                    BallEject(ref s, input, out died);
                    if (died)
                    {
                        s.DeathType = s.DeathType >= 11 ? s.DeathType : (byte)6;
                        return false;
                    }

                    // Pogo bounce: if BallEject landed (OnGround became true and vel was zeroed),
                    // apply bounce using pre-eject velocity
                    if (s.OnGround && s.VelY_fixed == 0 && !s.Orbed)
                    {
                        int newVel = (-preEjectVelY / 3) * 2;
                        // Minimum bounce = yellow pad velocity for swing column
                        int yellowPadMin = s.Mini
                            ? SharedPhysics.PadOrbHeights_Mini[1][7]
                            : SharedPhysics.PadOrbHeights[1][7];
                        if (s.GravFlipped)
                        {
                            // Inverted gravity: bounce downward (positive vel)
                            if (newVel < yellowPadMin)
                                newVel = yellowPadMin;
                        }
                        else
                        {
                            // Normal gravity: bounce upward (negative vel)
                            int minVel = -yellowPadMin;
                            if (newVel > minVel)
                                newVel = minVel;
                        }
                        s.VelY_fixed = newVel;
                        s.OnGround = false; // bouncing = airborne
                    }
                }
                PfUpdateSlopeCounters_Fresh(ref s);

                // Pogo black orb on press: input activates black orb velocity
                if (input && !s.Orbed)
                {
                    int blackOrbVel = s.Mini
                        ? SharedPhysics.PadOrbHeights_Mini[6][7]
                        : SharedPhysics.PadOrbHeights[6][7];
                    // Black orb vel is negative; apply in anti-gravity direction
                    s.VelY_fixed = s.GravFlipped ? -blackOrbVel : blackOrbVel;
                    s.Orbed = true;
                }
                else if (!input)
                {
                    s.Orbed = false;
                }

                if (CheckDeathCollision(ref s))
                {
                    s.DeathType = 3;
                    return false;
                }
            }

            // -- STEP 6: (removed — SIM has no post-Y gravity portal check at OLD X;
            //    gravity portals after physics are detected at NEW X in Step 8b) --

            // -- STEP 7: FLOOR SPIKES + FORWARD COLLISION at OLD X, post-eject Y --
            // NES runthecolls() calls x_movement_coll() BEFORE x_movement().
            // x_movement_coll() loads Generic.x = high_byte(currplayer_x) (OLD X)
            // and Generic.y = high_byte(currplayer_y) (post-eject Y).
            // Then bg_coll_floor_spikes() + bg_coll_R() run at OLD X.
            // x_movement() advances X AFTER these checks.
            // bg_coll_death() runs after x_movement(), at NEW X.

            // -- STEP 7a: 4-CORNER SPIKE CHECK (bg_coll_floor_spikes) at OLD X --
            if (CheckFloorSpikes(ref s))
            {
#if !DISABLE_DEBUG_LOGGING
                PfLog($"[FLOOR_SPIKE] X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
#endif
                if (_speculativeDepth == 0) { _lastDeathReason = "FLOOR_SPIKE"; _lastDeathX = s.X_fixed >> 8; _lastDeathY = s.Y_fixed >> 8; }
                s.DeathType = 7;
                return false;
            }

            // -- STEP 7b: FORWARD COLLISION (bg_coll_R) at OLD X --
            // Forward collision (bg_coll_R) — runs for all game modes.
            // H_BLOCK does NOT skip forward collision; it only enables ceiling eject.
            if (s.GameMode == 0 || s.GameMode == 1 || s.GameMode == 2 || s.GameMode == 3 || s.GameMode == 4 || s.GameMode == 5 || s.GameMode == 6 || s.GameMode == 7 || s.GameMode == 8 || s.GameMode == 9 || s.GameMode == 10)
            {
                if (CheckForwardCollision(ref s))
                {
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[FWD_DEATH] X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
#endif
                    if (_speculativeDepth == 0) { _lastDeathReason = "FWD_DEATH"; _lastDeathX = s.X_fixed >> 8; _lastDeathY = s.Y_fixed >> 8; }
                    s.DeathType = 8;
                    return false;
                }
            }

            // -- STEP 7c/7d: Center death + slope penetration moved to after X advance --
            // NES bg_coll_death runs inside runthecolls() AFTER x_movement() advances
            // currplayer_x, using the NEW X position.

            // -- STEP 7e: CAMERA FOLLOW + OOB DEATH (matching NES x_movement Y bounds) --
            {
                // NES process_y_scroll: when dual && !twoplayer, camera ALWAYS uses
                // ship-style smooth scroll toward target_scroll_y (set by the dual
                // portal).  It never tracks the player's Y directly.
                bool camFollowsY = (!s.DualActive) &&
                    (s.GameMode == 0 || s.GameMode == 4 || s.GameMode == 5 || s.GameMode == 8 || s.GameMode == 9 || s.NoCamLockForced);
                if (camFollowsY)
                {
                    // Match NES process_y_scroll: top threshold 0x4000 (64px), bottom 0xA0 (160px)
                    int minCamY_reserved = -(groundRowsToReserve * TILE) << 8;
                    int screenY_fixed = s.Y_fixed - s.CameraY_fixed;
                    if (screenY_fixed < 0x4000)
                    {
                        int need_fixed = 0x4000 - screenY_fixed;
                        s.CameraY_fixed -= need_fixed;
                        if (s.CameraY_fixed < minCamY_reserved) s.CameraY_fixed = minCamY_reserved;
                    }
                    else if ((screenY_fixed >> 8) >= 0xA0)
                    {
                        int need_fixed = screenY_fixed - 0xA000;
                        int maxCamY = Math.Max(0, (mapHeight - NES_H) * TILE) << 8;
                        s.CameraY_fixed += need_fixed;
                        if (s.CameraY_fixed > maxCamY) s.CameraY_fixed = maxCamY;
                    }
                }
                else
                {
                    // Ship-style smooth scroll toward target.
                    // NES process_y_scroll does NOT update target_scroll_y per-frame;
                    // target is set only by portal hits.  The camera scrolls toward
                    // the portal-set target at a fixed speed, enforcing OOB death
                    // when the player flies too far from the locked camera.

                    // Ship-style smooth scroll toward target
                    if (s.TargetCameraY_fixed > s.CameraY_fixed)
                    {
                        s.CameraY_fixed += SHIP_SCROLL_SPEED_FIXED;
                        if (s.CameraY_fixed > s.TargetCameraY_fixed) s.CameraY_fixed = s.TargetCameraY_fixed;
                    }
                    else if (s.TargetCameraY_fixed < s.CameraY_fixed)
                    {
                        s.CameraY_fixed -= SHIP_SCROLL_SPEED_FIXED;
                        if (s.CameraY_fixed < s.TargetCameraY_fixed) s.CameraY_fixed = s.TargetCameraY_fixed;
                    }
                    int maxCamY2 = Math.Max(0, (mapHeight - NES_H) * TILE) << 8;
                    int minCamY2 = -(groundRowsToReserve * TILE) << 8;
                    if (s.CameraY_fixed < minCamY2) s.CameraY_fixed = minCamY2;
                    if (s.CameraY_fixed > maxCamY2) s.CameraY_fixed = maxCamY2;
                }

                // OOB top/bottom / wrap mode: NES x_movement checks screen-relative Y.
                // NES currplayer_y is uint16_t — going past 0xFFFF wraps to 0x0000,
                // triggering the < 0x0600 death.  PF uses 32-bit Y so we must check
                // both top (< 0x0600) and bottom (> 0xF900) explicitly.
                // NES enforces OOB death even during dual mode.
                {
                    int screenRelY = s.Y_fixed - s.CameraY_fixed;
                    if (!s.WrapMode)
                    {
                        if (screenRelY < 0x0600)
                        {
#if !DISABLE_DEBUG_LOGGING
                            PfLog($"[OOB_TOP] screenRelY=0x{screenRelY:X4} camY={s.CameraY_fixed >> 8}");
#endif
                            if (_speculativeDepth == 0) { _lastDeathReason = "OOB_TOP"; _lastDeathX = s.X_fixed >> 8; _lastDeathY = s.Y_fixed >> 8; }
                            s.DeathType = 11;
                            return false;
                        }
                        // Bottom OOB: NES uint16_t wraps past 0xFFFF→0x0000, hitting < 0x0600
                        // next frame.  In PF the 32-bit screenRelY just keeps growing, so
                        // check explicitly at the same 0xF900 boundary used by wrap mode.
                        if (screenRelY > 0xF900)
                        {
#if !DISABLE_DEBUG_LOGGING
                            PfLog($"[OOB_BOTTOM] screenRelY=0x{screenRelY:X4} camY={s.CameraY_fixed >> 8}");
#endif
                            if (_speculativeDepth == 0) { _lastDeathReason = "OOB_BOTTOM"; _lastDeathX = s.X_fixed >> 8; _lastDeathY = s.Y_fixed >> 8; }
                            s.DeathType = 11;
                            return false;
                        }
                    }
                    else
                    {
                        // Wrap mode: wrap Y between 0x0600 and 0xF900 (screen-relative)
                        if (screenRelY < 0x0600)
                        {
                            s.Y_fixed = s.CameraY_fixed + 0xF900;
                        }
                        else if (screenRelY > 0xF900)
                        {
                            s.Y_fixed = s.CameraY_fixed + 0x0600;
                        }
                    }
                }
            }

            // Gravity modifier triggers (0x70-0x74) use camera-center-based
            // activation, same as speed portals.  Only P1 checks these in SIM.
            // CRITICAL: Check BEFORE advancing X to match SIM which uses the
            // pre-advance camera position (oldX + 48px as center).
            if (!_dualP2Guard)
                CheckGravityModTriggersAtNewX(ref s);

            // -- STEP 8: RESTORE NEW X --
            s.X_fixed = newX_fixed;

            // -- STEP 8b: DEATH CHECK at NEW X (matching NES bg_coll_death) --
            // NES bg_coll_death runs INSIDE x_movement, AFTER advancing currplayer_x.
            // Reference: "bg_coll_death() — death check at new X".
            if (CheckDeathCollision(ref s))
            {
#if !DISABLE_DEBUG_LOGGING
                PfLog($"[DEATH_COLL] X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
#endif
                if (_speculativeDepth == 0) { _lastDeathReason = "DEATH_COLL"; _lastDeathX = s.X_fixed >> 8; _lastDeathY = s.Y_fixed >> 8; }
                s.DeathType = 9;
                return false;
            }

            // -- STEP 8b2: SLOPE PENETRATION DEATH at NEW X --
            if (CheckSlopePenetrationDeath(ref s))
            {
#if !DISABLE_DEBUG_LOGGING
                PfLog($"[SLOPE_PENETRATION_DEATH] X={s.X_fixed >> 8}px Y={s.Y_fixed >> 8}px");
#endif
                if (_speculativeDepth == 0) { _lastDeathReason = "SLOPE_DEATH"; _lastDeathX = s.X_fixed >> 8; _lastDeathY = s.Y_fixed >> 8; }
                s.DeathType = 9;
                return false;
            }

            // -- STEP 8c: SPEED PORTAL CHECK --
            // Speed portals are now detected via collision overlap in ProcessSprites
            // (matching SIM's cam-OFF behavior). No separate check needed here.

            // -- STEP 10: MAP BOUNDS CHECK --
            int playerY_px = s.Y_fixed >> 8;
            int worldBottom = (mapHeight - groundRowsToReserve) * TILE;
            int worldTop = -(groundRowsToReserve * TILE);
            if (playerY_px < worldTop || playerY_px >= worldBottom + TILE)
            {
#if !DISABLE_DEBUG_LOGGING
                PfLog($"[BOUNDS_DEATH] Y={playerY_px}px worldBottom={worldBottom}");
#endif
                if (_speculativeDepth == 0) { _lastDeathReason = "BOUNDS_DEATH"; _lastDeathX = s.X_fixed >> 8; _lastDeathY = playerY_px; }
                s.DeathType = 10;
                return false;
            }

            // Clear jblocked/fblocked/hblocked/dblocked at end of movement (gamemode_cube.h line 162)
            s.JBlocked = false;
            s.FBlocked = false;
            s.HBlocked = false;
            s.Dblocked = false;

            // Orbed clear now happens before ProcessSprites (matching NES order)
            s.PrevInputHeld = input;

            // ---- DUAL MODE: PLAYER 2 PROCESSING ----
            // After P1 completes successfully, if dual is active, context-switch
            // to P2 and run a full physics frame.  Both players share X and
            // VelX; P2 has independent Y/VelY/gravity/mini.
            // Matching SIM order: save P1 → load P2 → sprites + physics → save P2 → restore P1.
            if (s.DualActive && !_dualP2Guard)
            {
                // Save P1 state
                int p1_Y = s.Y_fixed;
                int p1_VelY = s.VelY_fixed;
                bool p1_GravFlipped = s.GravFlipped;
                int p1_GravMul = s.GravMul;
                bool p1_Mini = s.Mini;
                bool p1_WasZeroed = s.WasZeroedByCollision;
                bool p1_OnGround = s.OnGround;
                int p1_BallFlipCooldown = s.BallFlipCooldown;
                int p1_BallInputBuffer = s.BallInputBuffer;
                int p1_BallCooldownFrames = s.BallCooldownFrames;
                int p1_RobotJumpTime = s.RobotJumpTime;
                int p1_SlopeWasOnCounter = s.SlopeWasOnCounter;
                int p1_SlopeFrames = s.SlopeFrames;
                int p1_SlopeType = s.SlopeType;
                bool p1_Orbed = s.Orbed;
                bool p1_BlackOrbed = s.BlackOrbed;
                bool p1_PrevInputHeld = s.PrevInputHeld;
                int p1_Dashing = s.Dashing;
                bool p1_JBlocked = s.JBlocked;
                bool p1_FBlocked = s.FBlocked;
                bool p1_HBlocked = s.HBlocked;
                int p1_NinjaJumps = s.NinjaJumps;
                int p1_PendingOrbIndex = s.PendingOrbIndex;
                int p1_PendingOrbSpriteId = s.PendingOrbSpriteId;

                // SIM syncs mini globally when any player hits a mini portal
                // (CheckMiniGrowthPortals sets both player_mini[0] and [1]).
                // Mirror that here: if P1's mini changed, push it to P2.
                s.P2_Mini = p1_Mini;

                // Load P2 state into s — X starts at P1's OLD X so P2's sprite
                // checks and physics match the SIM's per-frame ordering.
                s.X_fixed = oldX_fixed;
                s.Y_fixed = s.P2_Y_fixed;
                s.VelY_fixed = s.P2_VelY_fixed;
                s.GravFlipped = s.P2_GravFlipped;
                s.GravMul = s.P2_GravMul;
                s.Mini = s.P2_Mini;
                s.WasZeroedByCollision = s.P2_WasZeroedByCollision;
                s.OnGround = s.P2_OnGround;
                s.BallFlipCooldown = s.P2_BallFlipCooldown;
                s.BallInputBuffer = s.P2_BallInputBuffer;
                s.BallCooldownFrames = s.P2_BallCooldownFrames;
                s.RobotJumpTime = s.P2_RobotJumpTime;
                s.SlopeWasOnCounter = s.P2_SlopeWasOnCounter;
                s.SlopeFrames = s.P2_SlopeFrames;
                s.SlopeType = s.P2_SlopeType;
                s.Orbed = s.P2_Orbed;
                s.BlackOrbed = s.P2_BlackOrbed;
                s.PrevInputHeld = s.P2_PrevInputHeld;
                s.Dashing = s.P2_Dashing;
                s.JBlocked = s.P2_JBlocked;
                s.FBlocked = s.P2_FBlocked;
                s.HBlocked = s.P2_HBlocked;
                s.Dblocked = s.P2_Dblocked;
                s.NinjaJumps = s.P2_NinjaJumps;
                s.PendingOrbIndex = s.P2_PendingOrbIndex;
                s.PendingOrbSpriteId = s.P2_PendingOrbSpriteId;

                // FAMIDASH shared-press model: both players read from the same
                // NES button press counter.  P1 runs first and may consume it;
                // P2 only sees what remains.
                //   Ball mode: consumed on flip (BallCooldownFrames set to 2,
                //              decremented to 1 by end of P1's physics).
                //   Orb/pad activation: does NOT consume the press (NES orb
                //     detection is in sprite_collide, not the jump counter path).
                //   Hold-based modes (ship, wave): unaffected — both see held.
                bool p1ConsumedPress = false;
                if (s.GameMode == 2 && p1_BallCooldownFrames > 0)
                    p1ConsumedPress = true;
                bool p2Input = input && !p1ConsumedPress;

                // Capture P2's pre-physics state for NES single-portal sync.
                // NES spcl_sngl_pt writes player_*[0] = currplayer_* at
                // sprite_collide time (before P2's movement/collisions).
                int p2PreY = s.Y_fixed;
                int p2PreVelY = s.VelY_fixed;
                bool p2PreGrav = s.GravFlipped;

                // Run P2's StepFrame (recursion guard prevents infinite dual loop)
                // Temporarily remove P1's orb additions from ProcessedSprites so
                // P2 can independently detect and activate the same orb (matching
                // SIM where both players process sprites independently).
                foreach (var idx in _p1OrbIndicesThisFrame!)
                    s.ProcessedSprites.Remove(idx);
                _p2OrbFlippedOtherGrav = false;
                _dualP2Guard = true;
                bool p2Alive = StepFrame(ref s, p2Input, out bool p2EndLevel);
                _dualP2Guard = false;
                foreach (var idx in _p1OrbIndicesThisFrame!)
                    s.ProcessedSprites.Add(idx);

                // dual_cap_check: P2 hit blue/green orb → flip P1's gravity + halve velocity
                if (_p2OrbFlippedOtherGrav)
                {
                    p1_GravFlipped = !p1_GravFlipped;
                    p1_GravMul = p1_GravFlipped ? -1 : 1;
                    p1_VelY /= 2;
                    _p2OrbFlippedOtherGrav = false;
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[DUAL_CAP_CHECK] P2 orb flipped P1 grav→{p1_GravFlipped} velY→0x{p1_VelY:X4}");
#endif
                }

                // Save P2 state back from s
                s.P2_Y_fixed = s.Y_fixed;
                s.P2_VelY_fixed = s.VelY_fixed;
                s.P2_GravFlipped = s.GravFlipped;
                s.P2_GravMul = s.GravMul;
                s.P2_Mini = s.Mini;
                s.P2_WasZeroedByCollision = s.WasZeroedByCollision;
                s.P2_OnGround = s.OnGround;
                s.P2_BallFlipCooldown = s.BallFlipCooldown;
                s.P2_BallInputBuffer = s.BallInputBuffer;
                s.P2_BallCooldownFrames = s.BallCooldownFrames;
                s.P2_RobotJumpTime = s.RobotJumpTime;
                s.P2_SlopeWasOnCounter = s.SlopeWasOnCounter;
                s.P2_SlopeFrames = s.SlopeFrames;
                s.P2_SlopeType = s.SlopeType;
                s.P2_Orbed = s.Orbed;
                s.P2_BlackOrbed = s.BlackOrbed;
                s.P2_PrevInputHeld = s.PrevInputHeld;
                s.P2_Dashing = s.Dashing;
                s.P2_JBlocked = s.JBlocked;
                s.P2_FBlocked = s.FBlocked;
                s.P2_HBlocked = s.HBlocked;
                s.P2_Dblocked = s.Dblocked;
                s.P2_NinjaJumps = s.NinjaJumps;
                s.P2_PendingOrbIndex = s.PendingOrbIndex;
                s.P2_PendingOrbSpriteId = s.PendingOrbSpriteId;

                // SIM syncs mini globally — if P2 hit a mini portal, propagate to P1
                if (s.P2_Mini != p1_Mini)
                    p1_Mini = s.P2_Mini;

                // P2 hit single portal → NES copies P2's pre-physics Y/gravity/vel
                // into player_*[0] (overwriting P1's saved state).  P1 gets P2's
                // state from the moment the portal was hit, NOT P1's own physics.
                if (!s.DualActive)
                {
                    // NES: player_y[0]=currplayer_y, player_gravity[0]=currplayer_gravity,
                    // player_vel_y[0]=currplayer_vel_y — P2's pre-physics state
                    s.X_fixed = newX_fixed;
                    s.Y_fixed = p2PreY;
                    s.VelY_fixed = p2PreVelY;
                    s.GravFlipped = p2PreGrav;
                    s.GravMul = p2PreGrav ? -1 : 1;
                    s.Mini = p1_Mini;
                    s.WasZeroedByCollision = p1_WasZeroed;
                    s.OnGround = p1_OnGround;
                    s.BallFlipCooldown = p1_BallFlipCooldown;
                    s.BallInputBuffer = p1_BallInputBuffer;
                    s.BallCooldownFrames = p1_BallCooldownFrames;
                    s.RobotJumpTime = p1_RobotJumpTime;
                    s.SlopeWasOnCounter = p1_SlopeWasOnCounter;
                    s.SlopeFrames = p1_SlopeFrames;
                    s.SlopeType = p1_SlopeType;
                    s.Orbed = p1_Orbed;
                    s.BlackOrbed = p1_BlackOrbed;
                    s.PrevInputHeld = p1_PrevInputHeld;
                    s.Dashing = p1_Dashing;
                    s.JBlocked = p1_JBlocked;
                    s.FBlocked = p1_FBlocked;
                    s.HBlocked = p1_HBlocked;
                    s.PendingOrbIndex = p1_PendingOrbIndex;
                    s.PendingOrbSpriteId = p1_PendingOrbSpriteId;
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[SINGLE_P2_SYNC] P2 hit single portal — P1 gets P2 state: Y={s.Y_fixed >> 8} VelY=0x{s.VelY_fixed:X4} GravFlipped={s.GravFlipped}");
#endif
                    if (p2EndLevel) { endLevel = true; return true; }
                    if (!p2Alive) return false;
                    return true;
                }

                // Restore P1 state
                s.X_fixed = newX_fixed; // P1's post-advance X (shared)
                s.Y_fixed = p1_Y;
                s.VelY_fixed = p1_VelY;
                s.GravFlipped = p1_GravFlipped;
                s.GravMul = p1_GravMul;
                s.Mini = p1_Mini;
                s.WasZeroedByCollision = p1_WasZeroed;
                s.OnGround = p1_OnGround;
                s.BallFlipCooldown = p1_BallFlipCooldown;
                s.BallInputBuffer = p1_BallInputBuffer;
                s.BallCooldownFrames = p1_BallCooldownFrames;
                s.RobotJumpTime = p1_RobotJumpTime;
                s.SlopeWasOnCounter = p1_SlopeWasOnCounter;
                s.SlopeFrames = p1_SlopeFrames;
                s.SlopeType = p1_SlopeType;
                s.Orbed = p1_Orbed;
                s.BlackOrbed = p1_BlackOrbed;
                s.PrevInputHeld = p1_PrevInputHeld;
                s.Dashing = p1_Dashing;
                s.JBlocked = p1_JBlocked;
                s.FBlocked = p1_FBlocked;
                s.HBlocked = p1_HBlocked;
                s.NinjaJumps = p1_NinjaJumps;
                s.PendingOrbIndex = p1_PendingOrbIndex;
                s.PendingOrbSpriteId = p1_PendingOrbSpriteId;

                if (p2EndLevel) { endLevel = true; return true; }
                if (!p2Alive)
                {
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[DUAL_P2_DEATH] X={s.X_fixed >> 8}px P2_Y={s.P2_Y_fixed >> 8}px deathType={s.DeathType}");
#endif
                    return false;
                }
            }

            return true;
        }

        // -------------------------------------------------------------------
        //  CUBE PHYSICS (matching ProcessCubePhysics_Fresh)
        // -------------------------------------------------------------------

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
            // -- Gravity prediction --
            int gravity = GetGravity(state.Mini);
            int accel = gravity * state.GravMul;

            // Max fall speed deceleration (matching common_gravity_routine)
            if (state.GravMul > 0 && state.VelY_fixed > maxFallSpeed)
                accel = -accel;
            else if (state.GravMul < 0 && state.VelY_fixed < -maxFallSpeed)
                accel = -accel;

            // Apply gravity modifier (portals/triggers 0x5F-0x63, 0x70-0x74)
            accel = (int)(accel * state.GravityMod);

            int futureVelY = state.VelY_fixed + accel;

            if (!state.GravFlipped)
            {
                // Normal gravity: landing = falling (futureVelY >= 0) + floor hit
                if (futureVelY < 0) return false; // Rising � won't land

                int futureY_fixed = state.Y_fixed + futureVelY;
                int hbW = GetHitboxW(state.Mini);
                int hbH = GetHitboxH(state.Mini);
                int hbOffY = GetHitboxOffsetY(state.GameMode, state.Mini, state.GravFlipped);
                int collY = (futureY_fixed >> 8) + hbOffY;
                var (floorHit, _, _) = CheckFloor(state.X_fixed >> 8, collY, hbW, hbH);
                return floorHit;
            }
            else
            {
                // Flipped gravity: landing = rising toward ceiling (futureVelY <= 0) + ceiling hit
                if (futureVelY > 0) return false; // Falling away � won't land

                int futureY_fixed = state.Y_fixed + futureVelY;
                int hbW = GetHitboxW(state.Mini);
                int hbH = GetHitboxH(state.Mini);
                int hbOffY = GetHitboxOffsetY(state.GameMode, state.Mini, state.GravFlipped);
                int collY = (futureY_fixed >> 8) + hbOffY;
                var (ceilHit, _, _) = CheckCeiling(state.X_fixed >> 8, collY, hbW, hbH);
                return ceilHit;
            }
        }

        /// <summary>
        /// CommonGravityRoutine � delegates to SharedPhysics.CommonGravityRoutine
        /// so PF and SIM use identical gravity logic.
        /// </summary>
        private void CubeGravity(ref SimState s)
        {
#if !DISABLE_DEBUG_LOGGING
            int oldVelY = s.VelY_fixed;
            int oldY = s.Y_fixed;
#endif

            // Build direction-adjusted constants matching how the SIM sets
            // tmpgravity / tmpfallspeed before calling CommonGravityRoutine_Fresh.
            int g = SharedPhysics.GetCubeGravity(s.Mini);
            int fs = maxFallSpeed;
            if (s.GravFlipped) { g = -g; fs = -fs; }

            int clampMaxY = Math.Max(0, (mapHeight * TILE - TILE)) << 8;

            SharedPhysics.CommonGravityRoutine(
                ref s.VelY_fixed,
                ref s.Y_fixed,
                g,            // tmpgravity (direction-adjusted)
                fs,           // tmpfallspeed (direction-adjusted)
                s.GravFlipped ? 0xFF : 0,   // gravityDir
                s.Dashing,    // dashMode
                s.GravityMod, // gravityMod
                1.0,          // timeScale
                true,         // isFullSpeed
                s.VelX_fixed, // velocityX (used by dash modes)
                clampMaxY);

#if !DISABLE_DEBUG_LOGGING
            PfLog($"[CUBE_GRAV] velY: 0x{oldVelY:X4} -> 0x{s.VelY_fixed:X4}, posY: 0x{oldY:X4} ({oldY >> 8}px) -> 0x{s.Y_fixed:X4} ({s.Y_fixed >> 8}px)");
#endif
        }

        /// <summary>
        /// CubeEject_Fresh — collision detection and position/velocity correction.
        /// Matching CubeEject_Fresh in CubePhysics_Fresh.partial.cs:
        ///   Normal:   CheckCollisionDown = unconditional landing.
        ///             CheckCollisionUp   = only if hblocked||fblocked (alphabet blocks).
        ///   Reversed: CheckCollisionUp   = unconditional landing (velY &lt;= 0).
        ///             CheckCollisionDown = only if hblocked||fblocked (alphabet blocks).
        /// </summary>
        private void CubeEject(ref SimState s, bool input, out bool died)
        {
            died = false;
            var r = SharedPhysics.CubeEject(in _collisionMap,
                s.X_fixed, s.Y_fixed, s.VelY_fixed, s.VelX_fixed,
                s.GravFlipped, s.Mini, s.GameMode, input,
                s.SlopeWasOnCounter, s.SlopeFrames, s.SlopeType,
                s.SlopeJumpHigher, s.LastSlopeType);
            s.Y_fixed = r.NewY_fixed;
            s.VelY_fixed = r.NewVelY_fixed;
            s.OnGround = r.OnGround;
            s.WasZeroedByCollision = r.WasZeroed;
            s.SlopeType = r.SlopeType;
            s.SlopeFrames = r.SlopeFrames;
            s.SlopeWasOnCounter = r.SlopeWasOnCounter;
            s.SlopeJumpHigher = r.SlopeJumpHigher;
            s.LastSlopeType = r.LastSlopeType;
            if (r.Died) { died = true; s.DeathType = 6; }

            // NES cube_eject() hblocked/fblocked handling (gamemode_cube.h lines 201-237).
            // SharedPhysics.CubeEject already handled the normal direction.
            // When hblocked||fblocked, ALSO check opposite direction.  Both can fire one frame.
            if ((s.GameMode == 0 || s.GameMode == 4 || s.GameMode == 8 || s.GameMode == 11) && (s.HBlocked || s.FBlocked))
            {
                // NES cube_eject() inline behavior:
                //   Primary eject (bg_coll_D for normal grav) sets vel=0xFFFF when hblocked.
                //   Then secondary eject (bg_coll_U) sees vel=0xFFFF → guard (vel<0) passes.
                // Our SharedPhysics.CubeEject sets vel=0 (WasZeroed). We must fix the
                // velocity BEFORE the opposite-direction check so the guard evaluates
                // against the NES-correct velocity (0xFFFF, not 0).

                // Step 1: WasZeroed velocity fix (matches NES inline vel assignment).
                if (s.HBlocked && r.WasZeroed)
                {
                    if (_speculativeDepth == 0) _step1FireCount++;
                    if (!s.GravFlipped)
                        s.VelY_fixed = -1;  // NES 0xFFFF = -1 signed 16-bit
                    else
                        s.VelY_fixed = 1;
                }

                // Step 2: Opposite-direction eject (NES secondary bg_coll_U / bg_coll_D).
                int collisionX = s.X_fixed >> 8;
                int hitboxW = SharedPhysics.GetCubeHitboxW(s.Mini);
                int hitboxH = SharedPhysics.GetCubeHitboxH(s.Mini);
                int hitboxOffsetY = SharedPhysics.GetHitboxOffsetY(s.GameMode, s.Mini, s.GravFlipped);
                if (!s.GravFlipped)
                {
                    // Normal grav → opposite = ceiling (bg_coll_U).  Guard: vel < 0.
                    if ((short)(s.VelY_fixed & 0xFFFF) < 0)
                    {
                        int collisionY = (s.Y_fixed >> 8) + hitboxOffsetY;
                        var (topCollided, collisionBottomY, _) = CheckCeiling(collisionX, collisionY, hitboxW, hitboxH);
                        if (topCollided)
                        {
                            int newY = collisionBottomY - hitboxOffsetY;
                            s.Y_fixed = newY << 8;
                            s.VelY_fixed = s.HBlocked ? 1 : 0;
                            s.OnGround = !s.HBlocked; // landed on ceiling surface
                            s.WasZeroedByCollision = !s.HBlocked;
                            s.Orbed = false; // NES: orbactive = 0
                            if (s.FBlocked)
                            {
                                s.GravFlipped = true;
                                s.GravMul = -1;
                            }
                            s.Step2Ejected = true;
                            s.Step2Ever = true;
                            if (_speculativeDepth == 0) _step2FireCount++;
                        }
                    }
                }
                else
                {
                    // Reversed grav → opposite = floor (bg_coll_D).  Guard: vel >= 0.
                    if ((short)(s.VelY_fixed & 0xFFFF) >= 0)
                    {
                        int collisionY = (s.Y_fixed >> 8) + hitboxOffsetY;
                        var (bottomCollided, collisionTopY, _) = CheckFloor(collisionX, collisionY, hitboxW, hitboxH);
                        if (bottomCollided)
                        {
                            int newY = collisionTopY - hitboxH - hitboxOffsetY;
                            s.Y_fixed = newY << 8;
                            s.VelY_fixed = s.HBlocked ? -1 : 0;  // NES 0xFFFF = -1 signed 16-bit
                            s.OnGround = !s.HBlocked; // landed on floor surface
                            s.WasZeroedByCollision = !s.HBlocked;
                            s.Orbed = false; // NES: orbactive = 0
                            if (s.FBlocked)
                            {
                                s.GravFlipped = false;
                                s.GravMul = 1;
                            }
                            s.Step2Ejected = true;
                            s.Step2Ever = true;
                            if (_speculativeDepth == 0) _step2FireCount++;
                        }
                    }
                }
            }
        }

        // -------------------------------------------------------------------
        //  BALL PHYSICS (matching BallPhysics_Fresh + BallEject_Fresh)
        // -------------------------------------------------------------------

        /// <summary>
        /// Ball gravity: same structure as CubeGravity but with ball-specific constants.
        /// Ball gravity is lighter than cube (0x47 vs 0x6B normal, 0x57 vs 0x6F mini).
        /// </summary>
        private void BallGravityStep(ref SimState s)
        {
#if !DISABLE_DEBUG_LOGGING
            int oldVelY = s.VelY_fixed;
            int oldY = s.Y_fixed;
#endif

            int g = BallGravity(s.Mini);
            int fs = BallMaxFallSpeed(s.Mini);
            if (s.GravFlipped) { g = -g; fs = -fs; }

            int clampMaxY = Math.Max(0, (mapHeight * TILE - TILE)) << 8;

            SharedPhysics.CommonGravityRoutine(
                ref s.VelY_fixed,
                ref s.Y_fixed,
                g, fs,
                s.GravFlipped ? 0xFF : 0,
                s.Dashing, s.GravityMod, 1.0, true, s.VelX_fixed,
                clampMaxY);

#if !DISABLE_DEBUG_LOGGING
            PfLog($"[BALL_GRAV] velY: 0x{oldVelY:X4} -> 0x{s.VelY_fixed:X4}, posY: 0x{oldY:X4} ({oldY >> 8}px) -> 0x{s.Y_fixed:X4} ({s.Y_fixed >> 8}px)");
#endif
        }

        /// <summary>
        /// Ball velocity zeroing: prevents velocity accumulation when grounded.
        /// Normal gravity: if touching ceiling and moving up ? zero vel.
        /// Inverted gravity (gravFlipped=true, currplayer_gravity=0xFF ? NES check
        /// is on gravity==0 which is "normal" i.e. gravFlipped=false in pathfinder
        /// terms): if touching floor and moving down ? zero vel.
        ///
        /// Wait � the sim code is confusing. Let me re-read the sim:
        ///   if (currplayer_gravity == 0) { // Normal gravity in NES terms
        ///       // "Inverted gravity - prevent velocity from pulling into ceiling"
        ///       CheckCollisionUp; if collided && velY < 0 ? zero
        ///   } else {
        ///       // "Normal gravity - prevent velocity from pulling into ground"
        ///       CheckCollisionDown; if collided && velY > 0 ? zero
        ///   }
        ///
        /// In the sim, currplayer_gravity==0 means GravFlipped=false.  But the
        /// comment says "Inverted gravity."  The sim checks ceiling for normal grav
        /// and floor for flipped grav � this catches headbonk scenarios.
        /// </summary>
        private void BallVelocityZeroing(ref SimState s, out bool died)
        {
            died = false;
            int hbW = GetHitboxW(s.Mini);
            int hbH = GetHitboxH(s.Mini);
            int hbOffY = GetHitboxOffsetY(2, s.Mini, s.GravFlipped);
            int collX = s.X_fixed >> 8;

            if (!s.GravFlipped) // currplayer_gravity == 0 ? normal gravity
            {
                // SIM: ceiling check when velY < 0 (moving toward ceiling)
                // Prevents velocity from pulling ball into ceiling when grounded.
                // SIM uses CheckCollisionUp which discards spikeDeath � match that.
                if (s.VelY_fixed < 0)
                {
                    int testY = (s.Y_fixed >> 8) + hbOffY - 1;
                    var (collided, ceilingBottomY, _) = CheckCeiling(collX, testY, hbW, hbH);
                    if (collided)
                    {
                        // SIM snaps Y to ceiling surface to prevent sub-pixel drift
                        int newY = ceilingBottomY - hbOffY;
                        s.Y_fixed = newY << 8;
                        s.VelY_fixed = 0;
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[BALL_VEL_ZERO] ceiling zeroed upward velocity, snapped Y to {newY}");
#endif
                    }
                }
            }
            else // currplayer_gravity != 0 ? inverted gravity
            {
                // SIM: floor check when velY > 0 (moving toward floor)
                // Prevents velocity from pulling ball into ground when ceiling-grounded.
                // SIM CheckCollisionDown has floor-spike death side-effect � match it.
                if (s.VelY_fixed > 0)
                {
                    int playerBottom = (s.Y_fixed >> 8) + hbOffY + hbH;
                    int testHeight = 2;
                    var (collided, surfaceY, velZeroFloorSpike) = CheckFloor(collX, playerBottom - testHeight, hbW, testHeight);
                    if (velZeroFloorSpike)
                    {
                        died = true;
                        return;
                    }
                    if (collided)
                    {
                        // SIM snaps Y to floor surface
                        int newY = surfaceY - hbH - hbOffY;
                        s.Y_fixed = newY << 8;
                        s.VelY_fixed = 0;
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[BALL_VEL_ZERO] floor zeroed downward velocity, snapped Y to {newY}");
#endif
                    }
                }
            }
        }

        /// <summary>
        /// Ball eject: directly mirrors NES ball_eject() from gamemode_ball.h.
        /// Uses NES bg_coll_U/bg_coll_D tile probe logic and eject math directly,
        /// bypassing CheckCeiling/CheckFloor (which have different probe offsets).
        ///
        /// NES bg_coll_U probes at: Generic.y + miniOffset + 1  (3 X-points)
        ///   velocity gate: only when vel < 0 (high_byte(vel_y) & 0x80)
        ///   eject: high_byte(currplayer_y) -= (tmp8 | 0xf0)  ? playerY += (16 - tmp8)
        ///
        /// NES bg_coll_D probes at: Generic.y + height + miniOffset  (3 X-points)
        ///   velocity gate: only when vel >= 0 (!(high_byte(vel_y) & 0x80))
        ///   eject: high_byte(currplayer_y) -= tmp8  ? playerY -= tmp8
        ///
        /// Generic.y = high_byte(currplayer_y) + 1 (normal) or -1 (inverted).
        /// No spike death � bg_coll_U_D_checks returns 0 for spike tiles in eject context.
        /// </summary>
        #pragma warning disable CS0414
        private int _ballEjectTraceCount = 0;
        #pragma warning restore CS0414
        private int _step1FireCount = 0;
        private int _step2FireCount = 0;
        private int[]? _gravFDeathCounts;
        private int _gravFDeathFrame = -1;
        private int _gravFDeathSamples = 0;
        private void BallEject(ref SimState s, bool input, out bool died)
        {
            died = false;
            s.OnGround = false;
            var r = SharedPhysics.BallEject(in _collisionMap,
                s.X_fixed, s.Y_fixed, s.VelY_fixed, s.VelX_fixed,
                s.GravFlipped, s.Mini, s.GameMode, input,
                s.SlopeWasOnCounter, s.SlopeFrames, s.SlopeType,
                s.SlopeJumpHigher, s.LastSlopeType);
            s.Y_fixed = r.NewY_fixed;
            s.VelY_fixed = r.NewVelY_fixed;
            s.OnGround = r.OnGround;
            s.SlopeType = r.SlopeType;
            s.SlopeFrames = r.SlopeFrames;
            s.SlopeWasOnCounter = r.SlopeWasOnCounter;
            s.SlopeJumpHigher = r.SlopeJumpHigher;
            s.LastSlopeType = r.LastSlopeType;
            if (r.Died) { died = true; s.DeathType = 6; }
        }

        /// <summary>
        /// Check for spike death at the ball's grounded-probe position.
        /// Matches the sim's ball grounded check which calls CheckCollisionDown
        /// (normal gravity) or CheckCollisionUp (inverted gravity).  Those
        /// collision functions have spike-death side-effects that fire before
        /// the solid-collision check itself.
        ///
        /// Normal gravity: probes 3 X-points at Y + hbOffY + hbH + 2
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
        ///    where playerTop_px = testTop = playerTop - 2, so +1 ? Y+hbOffY-1)
        /// </summary>
        private bool BallGroundedProbeSpikeDeath(ref SimState s)
        {
            int playerX_px = s.X_fixed >> 8;
            int playerY_px = s.Y_fixed >> 8;
            int hbW = GetHitboxW(s.Mini);
            int hbH = GetHitboxH(s.Mini);
            int hbOffY = GetHitboxOffsetY(2, s.Mini, s.GravFlipped);

            bool result = SharedPhysics.BallGroundedProbeSpikeDeath(_collisionMap,
                playerX_px, playerY_px, hbW, hbH, hbOffY, s.GravFlipped);
#if !DISABLE_DEBUG_LOGGING
            if (result)
                PfLog($"[BALL_GROUND_SPIKE] detected at X={playerX_px} Y={playerY_px}");
#endif
            return result;
        }

        /// <summary>
        /// Fresh ball grounded probe � matches SIM's per-frame grounded check
        /// in BallPhysics_Fresh.partial.cs.  The SIM re-probes collision every
        /// frame instead of relying on a persistent OnGround flag.  This detects
        /// when the ball has slid past the edge of a platform (OnGround would be
        /// stale, but the real ground is gone).
        /// Normal gravity:  CheckFloor  at playerBottom,     height 2.
        /// Inverted gravity: CheckCeiling at playerTop - 2, height 2.
        /// </summary>
        private bool BallIsGrounded(ref SimState s)
        {
            return SharedPhysics.BallIsGrounded(_collisionMap,
                s.X_fixed >> 8, s.Y_fixed >> 8,
                GetHitboxW(s.Mini), GetHitboxH(s.Mini),
                GetHitboxOffsetY(2, s.Mini, s.GravFlipped),
                s.GravFlipped);
        }

        /// <summary>
        /// Dash hold/release decision: when the player is dashing (Dashing != 0),
        /// decide whether to keep holding (true) or release (false).
        /// Tests multiple release timings via forward simulation and picks the
        /// optimal one.
        /// </summary>
        private bool DecideDashHold(SimState state)
        {
            if (_speculativeDepth >= MAX_SPECULATIVE_DEPTH) return true; // default: keep holding

            _speculativeDepth++;

            int bestSurv = -1;
            int bestRelease = 0;

            for (int holdFrames = 0; holdFrames <= 40; holdFrames++)
            {
                var s = state.Clone();
                bool alive = true;
                int survived = 0;
                bool dashEndedNaturally = false;

                // Phase 1: hold for holdFrames frames (input=true keeps dashing)
                for (int f = 0; f < holdFrames && alive; f++)
                {
                    alive = StepFrame(ref s, true, out bool endLvl);
                    if (endLvl) { survived = LOOKAHEAD_HORIZON; break; }
                    if (alive) survived++;
                    // If dash ended naturally (S_BLOCK), stop holding phase
                    if (s.Dashing == 0) { dashEndedNaturally = true; break; }
                }

                // Phase 2: release (input=false ends dash)
                if (alive && survived < LOOKAHEAD_HORIZON && s.Dashing != 0)
                {
                    alive = StepFrame(ref s, false, out bool endLvl);
                    if (endLvl) survived = LOOKAHEAD_HORIZON;
                    else if (alive) survived++;
                }

                // Phase 3: continue with normal mode decisions
                if (alive && survived < LOOKAHEAD_HORIZON)
                {
                    int remaining = LOOKAHEAD_HORIZON - survived;
                    for (int f = 0; f < remaining && alive; f++)
                    {
                        bool inp = DecideInput(s);
                        alive = StepFrame(ref s, inp, out bool endLvl);
                        if (endLvl) { survived = LOOKAHEAD_HORIZON; break; }
                        if (alive) survived++;
                    }
                }

                if (survived > bestSurv)
                {
                    bestSurv = survived;
                    bestRelease = holdFrames;
                }
                if (bestSurv >= LOOKAHEAD_HORIZON) break; // perfect survival, stop searching
                // If dash ended naturally (S_BLOCK), longer holds are identical
                if (dashEndedNaturally) break;
            }

            _speculativeDepth--;

            // If best release is at 0 (release this frame), return false
            // Otherwise keep holding (we'll re-evaluate next frame)
            return bestRelease > 0;
        }

        /// <summary>
        /// Spider decision logic: decide when to teleport to opposite surface.
        /// Spider teleports when grounded (velY==0) and not orbed.
        /// Uses forward simulation to compare teleport vs stay.
        /// </summary>
        private bool DecideSpiderInput(SimState state, bool isOverrideFrame)
        {
            // Orb decision (spider orbs require input)
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
                        for (int od = -1; od < 15; od++)
                        {
                            int os = 1 + SimulateForwardWithJumpAt(orbState, od);
                            if (os > orbSurv) orbSurv = os;
                        }
                    }

                    var skipState = state.Clone();
                    bool skipAlive = StepFrame(ref skipState, false, out _);
                    skipState.ProcessedSprites.Add(orbIndex);
                    int skipSurv = 0;
                    if (skipAlive)
                    {
                        skipSurv = 1 + Math.Max(SimulateForwardWithJumpAt(skipState, -1),
                                                 SimulateForwardWithJumpAt(skipState, 0));
                    }
                    if (orbSurv >= skipSurv) return true;
                    state.ProcessedSprites.Add(orbIndex);
                    return false;
                }
            }

            // Spider can only teleport when grounded and not orbed
            if (state.VelY_fixed != 0 || state.Orbed) return false;
            if (_speculativeDepth >= MAX_SPECULATIVE_DEPTH) return false;

            // Compare teleport (input=true) vs stay (input=false)
            _speculativeDepth++;
            List<(int x, int y)>? noPressPath = (_speculativeDepth == 1 && OnSpeculativePath != null)
                ? new List<(int x, int y)>() : null;
            int noPressSurv = SimulateForwardWithJumpAt(state, -1, pathPoints: noPressPath);
            OnSpeculativePath?.Invoke(noPressPath, -1, noPressSurv, false);

            int bestPressSurv = 0;
            for (int delay = 0; delay < 20; delay++)
            {
                List<(int x, int y)>? specPath = (_speculativeDepth == 1 && OnSpeculativePath != null)
                    ? new List<(int x, int y)>() : null;
                int pressSurv = SimulateForwardWithJumpAt(state, delay, pathPoints: specPath);
                OnSpeculativePath?.Invoke(specPath, delay, pressSurv, false);
                if (pressSurv > bestPressSurv) bestPressSurv = pressSurv;
            }
            _speculativeDepth--;

#if !DISABLE_DEBUG_LOGGING
            PfLog($"[DECIDE_SPIDER] noPress={noPressSurv} bestPress={bestPressSurv}");
#endif
            // Walking survives the full lookahead horizon — no reason to
            // teleport now.  The BFS re-evaluates every grounded frame, so
            // as the spider approaches real danger noPressSurv will drop and
            // teleport will be reconsidered at the proper time.
            if (noPressSurv >= LOOKAHEAD_HORIZON) return false;

            // Stay-bias: scale with walking survival so the spider waits
            // until danger is genuinely close before teleporting.
            // At noPressSurv=50 → stayBias=47, need bestPressSurv>97 (won't fire)
            // At noPressSurv=20 → stayBias=17, need bestPressSurv>37 (fires if ceiling is safe)
            // At noPressSurv=5  → stayBias=2,  need bestPressSurv>7  (easy, emergency teleport)
            int stayBias = Math.Max(0, noPressSurv - 3);
            if (bestPressSurv > noPressSurv + stayBias && bestPressSurv >= 2)
            {
                _committedJumpDelay = -1;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Swing decision logic: decide when to flip gravity.
        /// Swing flips gravity on press (no velocity impulse, no grounded requirement).
        /// Uses forward simulation similarly to ball.
        /// </summary>
        private bool DecideSwingInput(SimState state, bool isOverrideFrame)
        {
            // Orb decision
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
                        for (int od = -1; od < 15; od++)
                        {
                            int os = 1 + SimulateForwardWithJumpAt(orbState, od);
                            if (os > orbSurv) orbSurv = os;
                        }
                    }

                    var skipState = state.Clone();
                    bool skipAlive = StepFrame(ref skipState, false, out _);
                    skipState.ProcessedSprites.Add(orbIndex);
                    int skipSurv = 0;
                    if (skipAlive)
                    {
                        skipSurv = 1 + Math.Max(SimulateForwardWithJumpAt(skipState, -1),
                                                 SimulateForwardWithJumpAt(skipState, 0));
                    }
                    if (orbSurv >= skipSurv) return true;
                    state.ProcessedSprites.Add(orbIndex);
                    return false;
                }
            }

            // If orbed, don't flip again
            if (state.Orbed) return false;
            if (_speculativeDepth >= MAX_SPECULATIVE_DEPTH) return false;

            // Compare flip (input=true) vs hold (input=false)
            _speculativeDepth++;
            List<(int x, int y)>? noPressSwingPath = (_speculativeDepth == 1 && OnSpeculativePath != null)
                ? new List<(int x, int y)>() : null;
            int noPressSurv = SimulateForwardWithJumpAt(state, -1, pathPoints: noPressSwingPath);
            OnSpeculativePath?.Invoke(noPressSwingPath, -1, noPressSurv, false);

            int bestPressSurv = 0;
            for (int delay = 0; delay < 25; delay++)
            {
                List<(int x, int y)>? specSwingPath = (_speculativeDepth == 1 && OnSpeculativePath != null)
                    ? new List<(int x, int y)>() : null;
                int pressSurv = SimulateForwardWithJumpAt(state, delay, pathPoints: specSwingPath);
                OnSpeculativePath?.Invoke(specSwingPath, delay, pressSurv, false);
                if (pressSurv > bestPressSurv) bestPressSurv = pressSurv;
            }
            _speculativeDepth--;

#if !DISABLE_DEBUG_LOGGING
            PfLog($"[DECIDE_SWING] noPress={noPressSurv} bestPress={bestPressSurv}");
#endif
            if (bestPressSurv > noPressSurv && bestPressSurv >= 2)
            {
                _committedJumpDelay = -1;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Pogo decision logic: decide when to activate the black orb press.
        /// Pogo auto-bounces on surface contact; pressing input fires a black orb
        /// velocity impulse (swing column). Strategy: compare no-press survival
        /// (auto-bounce only) vs press at various delays.
        /// </summary>
        private bool DecidePogoInput(SimState state, bool isOverrideFrame)
        {
            // Orb decision (same as other modes)
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
                        for (int od = -1; od < 15; od++)
                        {
                            int os = 1 + SimulateForwardWithJumpAt(orbState, od);
                            if (os > orbSurv) orbSurv = os;
                        }
                    }

                    var skipState = state.Clone();
                    bool skipAlive = StepFrame(ref skipState, false, out _);
                    skipState.ProcessedSprites.Add(orbIndex);
                    int skipSurv = 0;
                    if (skipAlive)
                    {
                        skipSurv = 1 + Math.Max(SimulateForwardWithJumpAt(skipState, -1),
                                                 SimulateForwardWithJumpAt(skipState, 0));
                    }
                    if (orbSurv >= skipSurv) return true;
                    state.ProcessedSprites.Add(orbIndex);
                    return false;
                }
            }

            // Don't press again while orbed (prevents double-activation)
            if (state.Orbed) return false;
            if (_speculativeDepth >= MAX_SPECULATIVE_DEPTH) return false;

            // Compare no-press (auto-bounce only) vs press at various delays
            _speculativeDepth++;
            List<(int x, int y)>? noPressPath = (_speculativeDepth == 1 && OnSpeculativePath != null)
                ? new List<(int x, int y)>() : null;
            int noPressSurv = SimulateForwardWithJumpAt(state, -1, pathPoints: noPressPath);
            OnSpeculativePath?.Invoke(noPressPath, -1, noPressSurv, false);

            int bestPressSurv = 0;
            int bestDelay = -1;
            for (int delay = 0; delay < 20; delay++)
            {
                List<(int x, int y)>? specPath = (_speculativeDepth == 1 && OnSpeculativePath != null)
                    ? new List<(int x, int y)>() : null;
                int pressSurv = SimulateForwardWithJumpAt(state, delay, pathPoints: specPath);
                OnSpeculativePath?.Invoke(specPath, delay, pressSurv, false);
                if (pressSurv > bestPressSurv)
                {
                    bestPressSurv = pressSurv;
                    bestDelay = delay;
                }
            }
            _speculativeDepth--;

#if !DISABLE_DEBUG_LOGGING
            PfLog($"[DECIDE_POGO] noPress={noPressSurv} bestPress={bestPressSurv} bestDelay={bestDelay}");
#endif
            // Only press if it survives longer than auto-bounce alone
            if (bestPressSurv > noPressSurv && bestPressSurv >= 2)
            {
                _committedJumpDelay = bestDelay;
                return bestDelay == 0;
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
            // Pads fire on collision � no skip/decide logic needed.

            // -- Orb decision (same as cube � orbs apply to all modes) --
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
                    PfLog($"[DECIDE_BALL] committed delay fired � flipping now");
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

            // -- Single-jump pass (same rationale as cube) --
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

            // -- Ball coin-collection preference --
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

                    // Found a nearby coin � test ALL delays for coin collection,
                    // not just viable ones.  A coin-collecting delay that has
                    // lower survival is still worth choosing over a non-collecting
                    // delay with better survival.
                    const int BALL_COIN_HORIZON = 200;
                    var coinDelays = new List<(int delay, int survival, int xProgress)>();

                    // Build lookup for viable delay survival/xProgress
                    var viableLookup = new Dictionary<int, (int survival, int xProgress)>();
                    foreach (var (d, s, xp) in viableDelays)
                        viableLookup[d] = (s, xp);

                    // Distance gate: skip expensive coin-seeking entirely when
                    // the coin is too far to reach within the horizon.
                    int maxReachPx = BALL_COIN_HORIZON * state.VelX_fixed / 256;
                    int coinCX_sc = (coin.HitLeft + coin.HitRight) / 2;
                    int coinDistX_sc = coinCX_sc - playerX_sc;
                    int coinSeekMax = Math.Max(maxDelay, BALL_COIN_HORIZON);

                    if (coinDistX_sc <= maxReachPx)
                    {
                    _speculativeDepth++;
                    int dbgClosestXDist = int.MaxValue;
                    int dbgClosestYDist = int.MaxValue;
                    int dbgClosestDelay = -1;
                    int dbgClosestFrame = -1;
                    int dbgDiedCount = 0;
                    int dbgMaxFrame = 0;
                    int dbgDeathX = 0;
                    int dbgDeathY = 0;
                    int dbgBestDeathDelay = -1;
                    int dbgBestDeathFrame = 0;
                    for (int delay = 0; delay < coinSeekMax; delay++)
                    {
                        // Simulate this delay path with multi-flip
                        var sim = state.Clone();
                        bool collected = false;
                        bool initialFlipDone = false;
                        int simSurvival = 0;
                        int collectFrame = -1;
                        for (int f = 0; f < BALL_COIN_HORIZON; f++)
                        {
                            bool inp = false;
                            if (!initialFlipDone)
                            {
                                if (f == delay)
                                {
                                    inp = true;
                                    initialFlipDone = true;
                                }
                            }
                            else if (sim.VelY_fixed == 0 && sim.OnGround)
                            {
                                inp = QuickDangerCheck(sim);
                            }
                            // Auto-activate orbs encountered after initial flip
                            // (matches SimulateForwardWithJumpAt chainJumps behavior)
                            if (initialFlipDone && !inp && sim.PendingOrbIndex >= 0)
                                inp = true;
                            if (!StepFrame(ref sim, inp, out bool eol))
                            {
                                simSurvival = f;
                                if (f > dbgBestDeathFrame) { dbgBestDeathFrame = f; dbgDeathX = sim.X_fixed >> 8; dbgDeathY = sim.Y_fixed >> 8; dbgBestDeathDelay = delay; }
                                if (collected && (f - collectFrame) < 10)
                                    collected = false; // dies too soon after collecting � backtracking would undo
                                break;
                            }
                            if (eol) { simSurvival = BALL_COIN_HORIZON; break; }

                            // Check coin hitbox overlap
                            int nx = (sim.X_fixed >> 8) + 1;
                            int hbW_b = GetHitboxW(sim.Mini);
                            int hbH_b = GetHitboxH(sim.Mini);
                            int hbOff_b = GetHitboxOffsetY(sim.GameMode, sim.Mini, sim.GravFlipped);
                            int pT = (sim.Y_fixed >> 8) + hbOff_b;
                            if (!collected &&
                                !(nx + hbW_b < coin.HitLeft || coin.HitRight < nx) &&
                                !(pT + hbH_b < coin.HitTop || coin.HitBottom < pT))
                            { collected = true; collectFrame = f; }
                            // Track closest approach for diagnostics
                            int xDist = Math.Max(coin.HitLeft - (nx + hbW_b), nx - coin.HitRight);
                            int yDist = Math.Max(coin.HitTop - (pT + hbH_b), pT - coin.HitBottom);
                            int combinedDist = Math.Max(xDist, 0) + Math.Max(yDist, 0);
                            long prevBest = (long)dbgClosestXDist + dbgClosestYDist;
                            if (combinedDist < prevBest || (combinedDist == prevBest && f < dbgClosestFrame))
                            {
                                dbgClosestXDist = Math.Max(xDist, 0);
                                dbgClosestYDist = Math.Max(yDist, 0);
                                dbgClosestDelay = delay;
                                dbgClosestFrame = f;
                            }
                            if (f + 1 > dbgMaxFrame) { dbgMaxFrame = f + 1; }
                            simSurvival = f + 1;
                        }
                        if (!collected && simSurvival < BALL_COIN_HORIZON) dbgDiedCount++;

                        if (collected)
                        {
                            // Use survival/xProgress from viable lookup if available
                            if (viableLookup.TryGetValue(delay, out var vd))
                                coinDelays.Add((delay, vd.survival, vd.xProgress));
                            else
                                coinDelays.Add((delay, simSurvival, sim.X_fixed >> 8));
                        }
                    }
                    _speculativeDepth--;

                    if (coinDelays.Count == 0 && coinDistX_sc <= maxReachPx)
                    {
                        _log.WriteLine($"[BALL_COIN_MISS_DETAIL] idx={coin.Index} playerX={playerX_sc} playerY={state.Y_fixed >> 8} gravFlip={state.GravFlipped} coinHit=({coin.HitLeft},{coin.HitTop})-({coin.HitRight},{coin.HitBottom}) closestXDist={dbgClosestXDist} closestYDist={dbgClosestYDist} closestDelay={dbgClosestDelay} closestFrame={dbgClosestFrame} diedCount={dbgDiedCount}/{coinSeekMax} maxFrame={dbgMaxFrame} deathX={dbgDeathX} deathY={dbgDeathY} deathDelay={dbgBestDeathDelay}");
                    }
                    } // end distance gate

                    if (coinDelays.Count > 0)
                    {
                        // Check how many are also normally viable
                        int alsoViable = coinDelays.Count(cd => viableLookup.ContainsKey(cd.delay));
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[BALL_COIN] {coinDelays.Count}/{coinSeekMax} delays collect coin idx={coin.Index} ({alsoViable} also viable) � preferring coin delays");
#endif
                        _log.WriteLine($"[BALL_COIN_FOUND] idx={coin.Index} {coinDelays.Count}/{coinSeekMax} delays collect coin ({alsoViable} viable) at playerX={playerX_sc} playerY={state.Y_fixed >> 8} gravFlip={state.GravFlipped} delays=[{string.Join(",", coinDelays.Select(cd => cd.delay).Take(10))}]");
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
                            int hbOff_b = GetHitboxOffsetY(simNp.GameMode, simNp.Mini, simNp.GravFlipped);
                            int pT = (simNp.Y_fixed >> 8) + hbOff_b;
                            if (!(nx + hbW_b < coin.HitLeft || coin.HitRight < nx) &&
                                !(pT + hbH_b < coin.HitTop || coin.HitBottom < pT))
                            { npCollects = true; break; }
                        }
                        _speculativeDepth--;

                        if (npCollects)
                        {
#if !DISABLE_DEBUG_LOGGING
                            PfLog($"[BALL_COIN] No-press collects coin idx={coin.Index}, no flip delays do � returning false");
#endif
                            return false; // Don't flip, let no-press collect the coin
                        }
                    }

                    // Ball coin approach suppression: no delay collects the coin
                    // from the current position. If the ball is on a surface from
                    // which a FUTURE flip could reach the coin, suppress flipping
                    // to stay grounded and walk forward to the optimal flip point.
                    if (coinDelays.Count == 0 && coin.HitLeft > playerX_sc + 20)
                    {
                        int coinCY = (coin.HitTop + coin.HitBottom) / 2;
                        int playerY = state.Y_fixed >> 8;
                        bool coinAbove = (coinCY < playerY); // coin above ball (lower Y)
                        bool ballOnFloor = !state.GravFlipped;
                        // Floor ball with coin above: check if the floor path can
                        // reach the coin's X.  If the ball dies on the floor before
                        // reaching the coin (e.g. due to death tiles), flip to
                        // ceiling immediately instead of suppressing.
                        if (coinAbove && ballOnFloor)
                        {
                            int coinCX = (coin.HitLeft + coin.HitRight) / 2;
                            int framesNeeded = (coinCX - playerX_sc) * 256 / Math.Max(state.VelX_fixed, 1);
                            if (noPressFrames >= framesNeeded)
                            {
                                // Floor path reaches coin ? suppress and walk forward
                                _log.WriteLine($"[BALL_COIN_APPROACH] idx={coin.Index} suppress flip, walk forward playerX={playerX_sc} coinX={coinCX} dist={coinCX-playerX_sc} noPressFrames={noPressFrames}");
                                return false;
                            }
                            else
                            {
                                // Floor path dies before coin ? check ceiling path too
                                // Simulate an actual flip (delay=0) to see if ceiling survives
                                _speculativeDepth++;
                                int ceilSurv = SimulateForwardWithJumpAt(state, 0);
                                _speculativeDepth--;
                                if (ceilSurv >= framesNeeded)
                                {
                                    _log.WriteLine($"[BALL_COIN_APPROACH] idx={coin.Index} FLIP to ceiling (floor dies at {noPressFrames}, ceiling survives {ceilSurv}, need {framesNeeded}) playerX={playerX_sc}");
                                    return true;
                                }
                                else if (ceilSurv >= noPressFrames)
                                {
                                    // Ceiling survives at least as long as floor � floor is dying
                                    // anyway, so flip to ceiling and let the full pathfinder navigate.
                                    _log.WriteLine($"[BALL_COIN_APPROACH] idx={coin.Index} FLIP to ceiling (floor dies at {noPressFrames}, ceiling dies at {ceilSurv}, need {framesNeeded} � ceiling at least as good) playerX={playerX_sc}");
                                    return true;
                                }
                                else
                                {
                                    // Ceiling dies sooner than floor ? stay on floor (pads/orbs may help)
                                    _log.WriteLine($"[BALL_COIN_APPROACH] idx={coin.Index} suppress flip (ceiling dies at {ceilSurv} < floor {noPressFrames}, need {framesNeeded}) playerX={playerX_sc}");
                                    return false;
                                }
                            }
                        }
                        // Ceiling ball can flip downward to reach coins below
                        bool coinBelow = (coinCY > playerY);
                        bool ballOnCeiling = state.GravFlipped;
                        if (coinBelow && ballOnCeiling)
                        {
                            int coinCX_c = (coin.HitLeft + coin.HitRight) / 2;
                            int framesNeeded_c = (coinCX_c - playerX_sc) * 256 / Math.Max(state.VelX_fixed, 1);
                            if (noPressFrames >= framesNeeded_c)
                            {
                                // Ceiling path survives to coin ? stay on ceiling
                                _log.WriteLine($"[BALL_COIN_APPROACH] idx={coin.Index} suppress flip (ceiling), walk forward playerX={playerX_sc} coinX={coinCX_c} dist={coinCX_c-playerX_sc} noPressFrames={noPressFrames}");
                                return false;
                            }
                            // else: ceiling can't reach coin � fall through to
                            // default decision (don't force a flip; the coin may
                            // be across a mode boundary or far away).
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
            // -- Orb decision (same as cube � orbs apply to all modes) --
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
                    PfLog($"[DECIDE_UFO_ORB] orbSurv={orbSurv} skipSurv={skipSurv} ? {(useOrb ? "ACTIVATE" : "SKIP")}");
#endif
                    return useOrb;
                }
            }

            // -- Evaluate: jump now vs wait --
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

        // -------------------------------------------------------------------
        //  ROBOT INPUT DECISION (heuristic mode)
        // -------------------------------------------------------------------

        /// <summary>
        /// Robot decision logic: decide when to jump and how long to hold.
        /// Robot has variable-height jumps — holding the button reapplies
        /// jump velocity each frame for up to 19 frames.
        /// Uses speculative forward-simulation to evaluate different hold durations.
        /// </summary>
        private bool DecideRobotInput(SimState state, bool isOverrideFrame)
        {
            // -- Orb decision (same as cube — orbs apply to all modes) --
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
                        orbSurv = 1 + SimulateRobotForward(orbState, 0); // min hold after orb

                    var skipState = state.Clone();
                    skipState.ProcessedSprites.Add(orbIndex);
                    bool skipAlive = StepFrame(ref skipState, false, out bool skipEnd);
                    if (skipEnd) return false;

                    int skipSurv = 0;
                    if (skipAlive)
                        skipSurv = 1 + SimulateRobotForward(skipState, 0);

                    bool useOrb = orbSurv >= skipSurv;
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[DECIDE_ROBOT_ORB] orbSurv={orbSurv} skipSurv={skipSurv} → {(useOrb ? "ACTIVATE" : "SKIP")}");
#endif
                    return useOrb;
                }
            }

            // If we're mid-hold from a previous decision, keep holding
            if (_committedRobotHold > 0)
            {
                _committedRobotHold--;
                return true;
            }

            // If airborne (not grounded), don't start a new jump
            if (state.VelY_fixed != 0)
                return false;

            if (_speculativeDepth >= MAX_SPECULATIVE_DEPTH) return false;

            // -- Evaluate: no-jump vs various hold durations --
            _speculativeDepth++;
            List<(int x, int y)>? noJumpPath = (_speculativeDepth == 1 && OnSpeculativePath != null)
                ? new List<(int x, int y)>() : null;
            int noJumpSurv = SimulateRobotForward(state, 0, noJumpPath);
            _speculativeDepth--;
            OnSpeculativePath?.Invoke(noJumpPath, -1, noJumpSurv, false);

            int bestSurv = noJumpSurv;
            int bestHold = 0; // 0 = don't jump

            // Test hold durations: 1 (tap), then increasing up to ROBOT_JUMP_TIME
            _speculativeDepth++;
            int[] holdDurations = { 1, 3, 5, 8, 11, 14, 17, ROBOT_JUMP_TIME };
            for (int hi = 0; hi < holdDurations.Length; hi++)
            {
                int holdFrames = holdDurations[hi];
                List<(int x, int y)>? specPath = (_speculativeDepth == 1 && OnSpeculativePath != null)
                    ? new List<(int x, int y)>() : null;
                int survival = SimulateRobotForward(state, holdFrames, specPath);
                OnSpeculativePath?.Invoke(specPath, holdFrames, survival, true);

                if (survival > bestSurv)
                {
                    bestSurv = survival;
                    bestHold = holdFrames;
                }
            }
            _speculativeDepth--;

            // If no improvement with any jump, don't jump
            if (bestHold == 0)
                return false;

            // Apply jump timing bias to hold duration
            // Earliest → shorter holds (quicker, lower jumps)
            // Latest → longer holds (higher jumps)
            if (JumpTimingBias < 0.45)
            {
                // Find shortest hold that still survives well
                _speculativeDepth++;
                for (int h = 1; h < bestHold; h++)
                {
                    int s = SimulateRobotForward(state, h);
                    if (s >= bestSurv - 2) { bestHold = h; break; }
                }
                _speculativeDepth--;
            }
            else if (JumpTimingBias > 0.55)
            {
                // Find longest hold that still survives well
                _speculativeDepth++;
                for (int h = ROBOT_JUMP_TIME; h > bestHold; h--)
                {
                    int s = SimulateRobotForward(state, h);
                    if (s >= bestSurv - 2) { bestHold = h; break; }
                }
                _speculativeDepth--;
            }

            // Commit: hold for bestHold frames
            _committedRobotHold = bestHold - 1; // -1 because this frame's return true counts as frame 1
#if !DISABLE_DEBUG_LOGGING
            PfLog($"[ROBOT_DECIDE] noJumpSurv={noJumpSurv} bestHold={bestHold} bestSurv={bestSurv} bias={JumpTimingBias}");
#endif
            return true;
        }

        /// <summary>
        /// Simulate robot forward with a specific hold duration.
        /// holdFrames=0 means no jump (just walk forward).
        /// holdFrames=N means press for N consecutive frames then release.
        /// </summary>
        private int SimulateRobotForward(SimState state, int holdFrames, List<(int x, int y)>? pathPoints = null)
        {
            var s = state.Clone();
            { int _mo = (s.Mini && !s.GravFlipped) ? 4 : 0; pathPoints?.Add(((s.X_fixed >> 8) + 8, (s.Y_fixed >> 8) + _mo + 8)); }
            for (int f = 0; f < LOOKAHEAD_HORIZON; f++)
            {
                bool input;
                if (holdFrames > 0 && f < holdFrames)
                {
                    // Holding jump button — first frame starts the jump,
                    // subsequent frames continue it (StepFrame handles timer)
                    input = true;
                }
                else if (f >= holdFrames && s.VelY_fixed == 0 && s.OnGround)
                {
                    // After landing from the initial jump, check for danger
                    input = QuickDangerCheck(s);
                }
                else
                {
                    input = false;
                }

                // Auto-activate orbs encountered during simulation
                if (!input && s.PendingOrbIndex >= 0
                    && !_btSkipSpecificOrbs.Contains(s.PendingOrbIndex))
                {
                    input = true;
                }

                bool alive = StepFrame(ref s, input, out bool endLevel);
                if (!alive) return f;
                if (endLevel) return LOOKAHEAD_HORIZON;
                { int _mo = (s.Mini && !s.GravFlipped) ? 4 : 0; pathPoints?.Add(((s.X_fixed >> 8) + 8, (s.Y_fixed >> 8) + _mo + 8)); }
            }
            return LOOKAHEAD_HORIZON;
        }

        // -------------------------------------------------------------------
        //  NINJA INPUT DECISION
        // -------------------------------------------------------------------

        // Committed ninja jump sequence: when a multi-jump plan is chosen,
        // _committedNinjaJumps holds a queue of frame delays for upcoming air jumps.
        // Each entry is the number of frames to WAIT before the next tap.
        // 0 means jump immediately on this frame.

        /// <summary>
        /// Ninja decision: tap-to-jump with up to 3 air jumps.
        /// Strategy: evaluate single, double, and triple jump sequences at various
        /// timing offsets, comparing survival distances. The jump timing bias
        /// adjusts how aggressively the ninja uses its air jumps.
        /// </summary>
        private bool DecideNinjaInput(SimState state, bool isOverrideFrame)
        {
            // -- Orb decision (same as cube — orbs apply to all modes) --
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
                        orbSurv = 1 + SimulateNinjaForward(orbState, null);

                    var skipState = state.Clone();
                    skipState.ProcessedSprites.Add(orbIndex);
                    bool skipAlive = StepFrame(ref skipState, false, out bool skipEnd);
                    if (skipEnd) return false;

                    int skipSurv = 0;
                    if (skipAlive)
                        skipSurv = 1 + SimulateNinjaForward(skipState, null);

                    bool useOrb = orbSurv >= skipSurv;
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[DECIDE_NINJA_ORB] orbSurv={orbSurv} skipSurv={skipSurv} → {(useOrb ? "ACTIVATE" : "SKIP")}");
#endif
                    return useOrb;
                }
            }

            // If we have committed jumps from a previous plan, execute them
            if (_committedNinjaJumps.Count > 0)
            {
                if (_ninjaWaitFrames > 0)
                {
                    _ninjaWaitFrames--;
                    return false;
                }
                // Time to jump
                _ninjaWaitFrames = _committedNinjaJumps.Dequeue();
                return true;
            }

            // Grounded: evaluate whether to jump vs wait
            // Airborne with jumps left: handled by committed plan
            bool grounded = state.VelY_fixed == 0;

            if (!grounded && state.NinjaJumps <= 0)
                return false; // Airborne with no jumps left — nothing to do

            if (_speculativeDepth >= MAX_SPECULATIVE_DEPTH) return false;

            // -- Evaluate: no-jump vs various jump plans --
            _speculativeDepth++;

            // No-jump baseline
            List<(int x, int y)>? noJumpPath = (_speculativeDepth == 1 && OnSpeculativePath != null)
                ? new List<(int x, int y)>() : null;
            int noJumpSurv = SimulateNinjaForward(state, null, noJumpPath);
            _speculativeDepth--;
            OnSpeculativePath?.Invoke(noJumpPath, -1, noJumpSurv, false);

            int bestSurv = noJumpSurv;
            int[]? bestPlan = null;

            // Generate candidate jump plans: single, double, and triple jumps
            // with varying inter-jump delays (0 = immediate, up to 15 frame gaps)
            int jumpsAvailable = grounded ? NINJA_MAX_JUMPS : state.NinjaJumps;

            _speculativeDepth++;

            // Single jump plans
            {
                List<(int x, int y)>? specPath = (_speculativeDepth == 1 && OnSpeculativePath != null)
                    ? new List<(int x, int y)>() : null;
                int surv = SimulateNinjaForward(state, new int[0], specPath);
                OnSpeculativePath?.Invoke(specPath, 0, surv, true);
                if (surv > bestSurv) { bestSurv = surv; bestPlan = new int[0]; }
            }

            // Double jump plans (if we have 2+ jumps)
            if (jumpsAvailable >= 2)
            {
                int[] delays = { 3, 6, 10, 15, 20 };
                for (int di = 0; di < delays.Length; di++)
                {
                    int delay = delays[di];
                    List<(int x, int y)>? specPath = (_speculativeDepth == 1 && OnSpeculativePath != null)
                        ? new List<(int x, int y)>() : null;
                    int surv = SimulateNinjaForward(state, new int[] { delay }, specPath);
                    OnSpeculativePath?.Invoke(specPath, delay, surv, true);
                    if (surv > bestSurv) { bestSurv = surv; bestPlan = new int[] { delay }; }
                }
            }

            // Triple jump plans (if we have 3 jumps)
            if (jumpsAvailable >= 3)
            {
                int[] delays = { 3, 6, 10, 15 };
                for (int d1i = 0; d1i < delays.Length; d1i++)
                {
                    for (int d2i = 0; d2i < delays.Length; d2i++)
                    {
                        int d1 = delays[d1i], d2 = delays[d2i];
                        List<(int x, int y)>? specPath = (_speculativeDepth == 1 && OnSpeculativePath != null)
                            ? new List<(int x, int y)>() : null;
                        int surv = SimulateNinjaForward(state, new int[] { d1, d2 }, specPath);
                        OnSpeculativePath?.Invoke(specPath, d1 * 100 + d2, surv, true);
                        if (surv > bestSurv) { bestSurv = surv; bestPlan = new int[] { d1, d2 }; }
                    }
                }
            }

            _speculativeDepth--;

            // No improvement → don't jump
            if (bestPlan == null)
                return false;

            // Commit the plan — queue follow-up jumps
            _committedNinjaJumps.Clear();
            _ninjaWaitFrames = 0;
            for (int i = 0; i < bestPlan.Length; i++)
                _committedNinjaJumps.Enqueue(bestPlan[i]);

#if !DISABLE_DEBUG_LOGGING
            PfLog($"[NINJA_DECIDE] noJumpSurv={noJumpSurv} bestSurv={bestSurv} plan=[{string.Join(",", bestPlan)}] jumpsAvail={jumpsAvailable}");
#endif
            return true; // Execute first jump now
        }

        /// <summary>
        /// Simulate ninja forward with a specific jump plan.
        /// jumpDelays=null means no jumping at all (walk baseline).
        /// jumpDelays=int[0] means single jump (frame 0 only).
        /// jumpDelays=int[]{N} means double jump: jump at frame 0, then wait N frames and jump again.
        /// jumpDelays=int[]{N,M} means triple jump: jump at frame 0, wait N, jump, wait M, jump.
        /// </summary>
        private int SimulateNinjaForward(SimState state, int[]? jumpDelays, List<(int x, int y)>? pathPoints = null)
        {
            var s = state.Clone();
            { int _mo = (s.Mini && !s.GravFlipped) ? 4 : 0; pathPoints?.Add(((s.X_fixed >> 8) + 8, (s.Y_fixed >> 8) + _mo + 8)); }

            // Build a simple per-frame input schedule from the jump plan
            // Frame 0: always jump (first jump)
            // Frame (delay1): second jump
            // Frame (delay1+delay2): third jump
            // Between jumps and after all jumps: no input (unless danger-based re-jump)
            int nextJumpFrame = -1; // -1 = first jump already at frame 0
            int jumpScheduleIdx = 0;
            int jumpAccum = 0; // accumulates delays to find absolute frame of next jump
            if (jumpDelays != null && jumpDelays.Length > 0)
            {
                jumpAccum = jumpDelays[0];
                nextJumpFrame = jumpAccum;
                jumpScheduleIdx = 1;
            }

            bool planDone = (jumpDelays == null); // null = no jumps at all
            int jumpsUsed = 0; // track how many jumps we've used

            for (int f = 0; f < LOOKAHEAD_HORIZON; f++)
            {
                bool input = false;

                if (!planDone)
                {
                    if (f == 0)
                    {
                        // First jump
                        input = true;
                        jumpsUsed++;
                    }
                    else if (nextJumpFrame >= 0 && f == nextJumpFrame)
                    {
                        // Scheduled air jump
                        input = true;
                        jumpsUsed++;
                        // Queue next jump if available
                        if (jumpDelays != null && jumpScheduleIdx < jumpDelays.Length)
                        {
                            jumpAccum += jumpDelays[jumpScheduleIdx];
                            nextJumpFrame = jumpAccum;
                            jumpScheduleIdx++;
                        }
                        else
                        {
                            nextJumpFrame = -1; // no more scheduled jumps
                        }
                    }
                }

                // After plan exhausted and grounded again, use danger check for re-jumps
                if (!input && s.VelY_fixed == 0 && s.OnGround && (planDone || jumpsUsed > 0))
                {
                    input = QuickDangerCheck(s);
                }

                // Auto-activate orbs encountered during simulation
                if (!input && s.PendingOrbIndex >= 0
                    && !_btSkipSpecificOrbs.Contains(s.PendingOrbIndex))
                {
                    input = true;
                }

                bool alive = StepFrame(ref s, input, out bool endLevel);
                if (!alive) return f;
                if (endLevel) return LOOKAHEAD_HORIZON;
                { int _mo = (s.Mini && !s.GravFlipped) ? 4 : 0; pathPoints?.Add(((s.X_fixed >> 8) + 8, (s.Y_fixed >> 8) + _mo + 8)); }
            }
            return LOOKAHEAD_HORIZON;
        }

        // -------------------------------------------------------------------
        //  WAVE INPUT DECISION (heuristic mode)
        // -------------------------------------------------------------------

        /// <summary>
        /// Wave decision logic: hold or release to control diagonal direction.
        /// Wave moves diagonally — holding input reverses vertical direction.
        /// Strategy: probe for walls above and below to find the corridor bounds,
        /// then steer toward the center. Jump timing bias shifts the target:
        /// earliest = upper bias, latest = lower bias.
        /// Falls back to survival comparison when corridor centering doesn't apply.
        /// </summary>
        private bool DecideWaveInput(SimState state, bool isOverrideFrame)
        {
            // -- Orb decision --
            if (!isOverrideFrame && !_btSuppressJumpUntilAirborne)
            {
                int orbSid = ScanForOrbOverlap(state, out int orbIndex);
                if (orbSid >= 0)
                {
                    var orbState = state.Clone();
                    bool orbAlive = StepFrame(ref orbState, true, out bool orbEnd);
                    if (orbEnd) return true;
                    int orbSurv = orbAlive ? 1 + SimulateWaveForward(orbState, true) : 0;

                    var skipState = state.Clone();
                    skipState.ProcessedSprites.Add(orbIndex);
                    bool skipAlive = StepFrame(ref skipState, false, out bool skipEnd);
                    if (skipEnd) return false;
                    int skipSurv = skipAlive ? 1 + SimulateWaveForward(skipState, false) : 0;

                    return orbSurv >= skipSurv;
                }
            }

            if (_speculativeDepth >= MAX_SPECULATIVE_DEPTH) return false;

            // -- Survival check: reject directions that die quickly --
            _speculativeDepth++;
            List<(int x, int y)>? holdPath = (_speculativeDepth == 1 && OnSpeculativePath != null)
                ? new List<(int x, int y)>() : null;
            int holdSurv = SimulateWaveForward(state, true, holdPath);
            List<(int x, int y)>? releasePath = (_speculativeDepth == 1 && OnSpeculativePath != null)
                ? new List<(int x, int y)>() : null;
            int releaseSurv = SimulateWaveForward(state, false, releasePath);
            _speculativeDepth--;
            OnSpeculativePath?.Invoke(holdPath, 0, holdSurv, true);
            OnSpeculativePath?.Invoke(releasePath, -1, releaseSurv, false);

            // If one direction dies much sooner, pick the surviving one
            if (holdSurv > releaseSurv + 3) return true;
            if (releaseSurv > holdSurv + 3) return false;

            // -- Corridor centering: probe up and down for walls --
            int playerY = state.Y_fixed >> 8;
            int playerX = state.X_fixed >> 8;
            int probeX = playerX + 8; // center of player

            // Scan upward for nearest solid tile
            int distUp = 0;
            for (int dy = 1; dy <= 80; dy++)
            {
                int testY = playerY - dy;
                if (testY < 0) { distUp = dy; break; }
                int tileX = probeX / TILE;
                int tileY = testY / TILE;
                var col = GetTileCollision(tileX, tileY);
                    if (col != MetatileCollision.COL_NONE &&
                        !SharedPhysics.IsDeathCollision(col))
                {
                    distUp = dy;
                    break;
                }
                if (dy == 80) distUp = 80;
            }

            // Scan downward for nearest solid tile
            int distDown = 0;
            for (int dy = 1; dy <= 80; dy++)
            {
                int testY = playerY + 16 + dy; // below hitbox bottom
                if (testY >= mapHeight * TILE) { distDown = dy; break; }
                int tileX = probeX / TILE;
                int tileY = testY / TILE;
                var col = GetTileCollision(tileX, tileY);
                    if (col != MetatileCollision.COL_NONE &&
                        !SharedPhysics.IsDeathCollision(col))
                {
                    distDown = dy;
                    break;
                }
                if (dy == 80) distDown = 80;
            }

            // Calculate corridor center and determine which direction to steer
            // Apply jump timing bias: 0.0 = favor upper half, 1.0 = favor lower half
            // 0.5 = true center
            double biasedCenter = 0.5 + (JumpTimingBias - 0.5) * 0.6; // range 0.2..0.8
            int targetDistUp = (int)((distUp + distDown) * (1.0 - biasedCenter));
            int targetDistDown = (int)((distUp + distDown) * biasedCenter);

            // Determine which direction the wave needs to move
            // If we're closer to the ceiling than target, move down (don't hold in normal gravity)
            // If we're closer to the floor than target, move up (hold in normal gravity)
            bool shouldGoUp;
            if (distUp < targetDistUp - 2)
                shouldGoUp = false; // too close to ceiling, go down
            else if (distDown < targetDistDown - 2)
                shouldGoUp = true;  // too close to floor, go up
            else
            {
                // Near center — use survival as tiebreaker
                if (holdSurv > releaseSurv) return true;
                if (releaseSurv > holdSurv) return false;
                return false; // default
            }

            // Map "should go up" to hold/release based on gravity
            // Normal gravity: release = down (default), hold = up
            // Flipped gravity: release = up (default), hold = down
            bool holdGoesUp = !state.GravFlipped;
            return shouldGoUp == holdGoesUp;
        }

        /// <summary>
        /// Simulate wave forward with a fixed input (hold or release).
        /// Returns number of frames survived.
        /// </summary>
        private int SimulateWaveForward(SimState state, bool hold, List<(int x, int y)>? pathPoints = null)
        {
            var s = state.Clone();
            { int _mo = (s.Mini && !s.GravFlipped) ? 4 : 0; pathPoints?.Add(((s.X_fixed >> 8) + 8, (s.Y_fixed >> 8) + _mo + 8)); }
            for (int f = 0; f < LOOKAHEAD_HORIZON; f++)
            {
                bool input = hold;

                // Auto-activate orbs
                if (!input && s.PendingOrbIndex >= 0
                    && !_btSkipSpecificOrbs.Contains(s.PendingOrbIndex))
                    input = true;

                bool alive = StepFrame(ref s, input, out bool endLevel);
                if (!alive) return f;
                if (endLevel) return LOOKAHEAD_HORIZON;
                { int _mo = (s.Mini && !s.GravFlipped) ? 4 : 0; pathPoints?.Add(((s.X_fixed >> 8) + 8, (s.Y_fixed >> 8) + _mo + 8)); }
            }
            return LOOKAHEAD_HORIZON;
        }

        // -------------------------------------------------------------------
        //  WAVE PHYSICS (wave eject)
        // -------------------------------------------------------------------

        /// <summary>
        /// Wave eject — check collision based on velocity direction.
        /// Uses 8-wide hitbox with X-offset +10 (moving up) or +4 (moving down).
        /// COL_FLOOR_CEIL/COL_ALL tiles eject the wave; other solid tiles kill.
        /// NES also checks slopes (bg_coll_D_slopes) before regular collision.
        /// </summary>
        private void WaveEject(ref SimState s, bool input, out bool died)
        {
            died = false;
            int playerX_px = s.X_fixed >> 8;
            int playerY_px = s.Y_fixed >> 8;
            
            // Wave X-offset based on velocity direction
            int xOffset = (s.VelY_fixed < 0) ? 10 : 4;
            int collX = playerX_px + xOffset;
            
            // Mini wave: use top 8×8 when moving up, bottom 8×8 when moving down
            int miniOffset = 0;
            if (s.Mini)
            {
                bool isMovingUp = s.GravFlipped ? (s.VelY_fixed > 0) : (s.VelY_fixed < 0);
                miniOffset = isMovingUp ? 0 : 8;
            }
            int collY = playerY_px + miniOffset;
            
            const int waveW = 8;
            int waveH = s.Mini ? 8 : 16;
            
#if !DISABLE_DEBUG_LOGGING
            PfLog($"[WAVE_EJECT] Generic=({collX},{collY}) {waveW}x{waveH} velY=0x{s.VelY_fixed:X4} miniOff={miniOffset}");
#endif
            // -- Slope checks (NES: bg_coll_U/bg_coll_D both contain slope sections) --
            // NES wave_eject calls bg_coll_U when velY<0 and bg_coll_D when velY>=0.
            // Each function checks slopes at its respective edge before block collision.
            // Probe two edge points and apply direction filtering
            // (LEFT rejects RISING, RIGHT rejects non-RISING).
            // NES wave-specific slope filters:
            //   - non-mini wave: LU45 → skip (return 0)
            //   - mini wave: LU66_TOP/LU66_BOT → skip (return 0)
            if (s.VelY_fixed >= 0)
            {
                // -- D slopes: bottom-edge probe matching bg_coll_D_slopes --
                // NES WAVE_HEIGHT = 0x08.  bg_coll_D centering is mini-only.
                int slopeCheckX = playerX_px + 4;
                int slopeGenY = playerY_px + (-2); // waveYOffset = -2 when velY >= 0
                const int slopeH = 8; // NES: WAVE_HEIGHT = 0x08
                int slopeMiniAdj = s.Mini ? ((16 - slopeH) >> 1) : 0;
                int slopeCheckY = slopeGenY + slopeH - 2 + slopeMiniAdj;
                int slopeCheckW = 8;

                for (int probe = 0; probe < 2; probe++)
                {
                    int probeX = slopeCheckX + (probe * slopeCheckW);
                    int tileX = probeX / TILE;
                    int tileY = slopeCheckY / TILE;
                    var sCol = GetTileCollision(tileX, tileY);
                    if (sCol >= MetatileCollision.COL_SLOPE_RD45 && sCol <= MetatileCollision.COL_SLOPE_LU66_TOP)
                    {
                        // NES wave-specific slope filters
                        if (!s.Mini && sCol == MetatileCollision.COL_SLOPE_LU45) continue;
                        if (s.Mini && (sCol == MetatileCollision.COL_SLOPE_LU66_TOP || sCol == MetatileCollision.COL_SLOPE_LU66_BOT)) continue;

                        var (sHit, sEject, sType) = PfSlopeCalc(probeX, slopeCheckY, sCol);
                        if (sHit)
                        {
                            // Direction filter: LEFT (probe 0) rejects RISING, RIGHT (probe 1) rejects non-RISING
                            bool isRising = (sType & 0x04) != 0; // SLOPE_RISING bit
                            if (probe == 0 && isRising) continue;
                            if (probe == 1 && !isRising) continue;

                            if (s.Dblocked)
                            {
                                // dblocked: eject instead of dying
                                if (sEject > 0)
                                {
                                    int curY = s.Y_fixed >> 8;
                                    curY -= sEject;
                                    s.Y_fixed = curY << 8;
                                }
                                s.VelY_fixed = 0;
                                s.WasZeroedByCollision = true;
#if !DISABLE_DEBUG_LOGGING
                                PfLog($"[WAVE_EJECT] D slope eject dblocked Y={s.Y_fixed >> 8}");
#endif
                                return;
                            }
#if !DISABLE_DEBUG_LOGGING
                            PfLog($"[WAVE_DEATH] D slope death X={playerX_px} Y={playerY_px} slopeTile=({tileX},{tileY}) col={sCol} probe={probe}");
#endif
                            died = true;
                            return;
                        }
                    }
                }
            }
            else // velY < 0 — moving UP
            {
                // -- U slopes: top-edge probe matching bg_coll_U_slopes --
                // NES probe Y: Generic.y + ((0x10-height)>>1) + (mini?1:2)
                // Centering ((0x10-height)>>1) is applied UNCONDITIONALLY in bg_coll_U.
                //   non-mini: (playerY+2) + 4 + 2 = playerY+8
                //   mini:     (playerY+2) + 4 + 1 = playerY+7
                int slopeCheckX = playerX_px + 4;
                int slopeGenY = playerY_px + 2; // waveYOffset = +2 when velY < 0
                const int slopeH = 8; // NES: WAVE_HEIGHT = 0x08
                int slopeCenterAdj = (16 - slopeH) >> 1; // Always applied for bg_coll_U
                int slopeCheckY = slopeGenY + slopeCenterAdj + (s.Mini ? 1 : 2);
                int slopeCheckW = 8;

                for (int probe = 0; probe < 2; probe++)
                {
                    int probeX = slopeCheckX + (probe * slopeCheckW);
                    int tileX = probeX / TILE;
                    int tileY = slopeCheckY / TILE;
                    var sCol = GetTileCollision(tileX, tileY);
                    if (sCol >= MetatileCollision.COL_SLOPE_RD45 && sCol <= MetatileCollision.COL_SLOPE_LU66_TOP)
                    {
                        // NES wave-specific slope filters
                        if (!s.Mini && sCol == MetatileCollision.COL_SLOPE_LU45) continue;
                        if (s.Mini && (sCol == MetatileCollision.COL_SLOPE_LU66_TOP || sCol == MetatileCollision.COL_SLOPE_LU66_BOT)) continue;

                        var (sHit, sEject, sType) = PfSlopeCalc(probeX, slopeCheckY, sCol);
                        if (sHit)
                        {
                            bool isRising = (sType & 0x04) != 0;
                            if (probe == 0 && isRising) continue;
                            if (probe == 1 && !isRising) continue;

                            if (s.Dblocked)
                            {
                                // dblocked: eject instead of dying
                                int curY = s.Y_fixed >> 8;
                                curY -= sEject;
                                s.Y_fixed = curY << 8;
                                s.VelY_fixed = 0;
                                s.WasZeroedByCollision = true;
#if !DISABLE_DEBUG_LOGGING
                                PfLog($"[WAVE_EJECT] U slope eject dblocked Y={s.Y_fixed >> 8}");
#endif
                                return;
                            }
#if !DISABLE_DEBUG_LOGGING
                            PfLog($"[WAVE_DEATH] U slope death X={playerX_px} Y={playerY_px} slopeTile=({tileX},{tileY}) col={sCol} probe={probe}");
#endif
                            died = true;
                            return;
                        }
                    }
                }
            }
            
            if (s.VelY_fixed < 0) // Moving UP — check ceiling
            {
                var (ceilHit, ceilBotY, ceilSpike) = CheckCeiling(collX, collY, waveW, waveH);
                if (ceilSpike)
                {
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[WAVE_DEATH] ceiling spike X={playerX_px} Y={playerY_px}");
#endif
                    died = true;
                    return;
                }
                if (ceilHit)
                {
                    // NES: COL_FLOOR_CEIL sets dblocked; dblocked allows eject on any solid
                    int tileX = collX / TILE;
                    int tileY = (collY - 1) / TILE;
                    var col = GetTileCollision(tileX, tileY);
                    if (col == MetatileCollision.COL_FLOOR_CEIL)
                        s.Dblocked = true;
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[WAVE_EJECT] coll_U hit tile={col} dblocked={s.Dblocked} ceilBotY={ceilBotY}");
#endif
                    if (s.Dblocked)
                    {
                        int newY = ceilBotY + 1 - miniOffset;
                        s.Y_fixed = newY << 8;
                        s.VelY_fixed = 0;
                        s.WasZeroedByCollision = true;
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[WAVE_EJECT] UP eject Y={s.Y_fixed >> 8}");
#endif
                    }
                    else
                    {
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[WAVE_DEATH] UP non-walkable tile={col} X={playerX_px} Y={playerY_px} probe=({collX},{collY - 1})");
#endif
                        died = true;
                        return;
                    }
                }
            }
            else if (s.VelY_fixed > 0) // Moving DOWN — check floor
            {
                var (floorHit, floorTopY, floorSpike) = CheckFloor(collX, collY, waveW, waveH);
                if (floorSpike)
                {
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[WAVE_DEATH] floor spike X={playerX_px} Y={playerY_px}");
#endif
                    died = true;
                    return;
                }
                if (floorHit)
                {
                    // NES: COL_FLOOR_CEIL sets dblocked; dblocked allows eject on any solid
                    int tileX = collX / TILE;
                    int tileY = (collY + waveH) / TILE;
                    var col = GetTileCollision(tileX, tileY);
                    if (col == MetatileCollision.COL_FLOOR_CEIL)
                        s.Dblocked = true;
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[WAVE_EJECT] coll_D hit tile={col} dblocked={s.Dblocked} floorTopY={floorTopY}");
#endif
                    if (s.Dblocked)
                    {
                        int newY = floorTopY - waveH - miniOffset;
                        s.Y_fixed = newY << 8;
                        s.VelY_fixed = 0;
                        s.WasZeroedByCollision = true;
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[WAVE_EJECT] DOWN eject Y={s.Y_fixed >> 8}");
#endif
                    }
                    else
                    {
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[WAVE_DEATH] DOWN non-walkable tile={col} X={playerX_px} Y={playerY_px} probe=({collX},{collY + waveH})");
#endif
                        died = true;
                        return;
                    }
                }
            }
            else
            {
#if !DISABLE_DEBUG_LOGGING
                PfLog($"[WAVE_EJECT] velY==0 skip collision");
#endif
            }
        }

        // -------------------------------------------------------------------
        //  SPIDER PHYSICS (matching SpiderPhysics_Fresh)
        // -------------------------------------------------------------------

        private void SpiderGravityStep(ref SimState s)
        {
            int g = 0x4B; // SPIDER_GRAVITY (fixed, same for normal and mini)
            int fs = 0x600; // SPIDER_MAX_FALLSPEED
            if (s.GravFlipped) { g = -g; fs = -fs; }

            int clampMaxY = Math.Max(0, (mapHeight * TILE - TILE)) << 8;
            SharedPhysics.CommonGravityRoutine(
                ref s.VelY_fixed, ref s.Y_fixed,
                g, fs,
                s.GravFlipped ? 0xFF : 0,
                s.Dashing, s.GravityMod, 1.0, true, s.VelX_fixed,
                clampMaxY);
        }

        private void SpiderEject(ref SimState s, int offsetY, bool input, out bool died)
        {
            died = false;
            int hbW = s.Mini ? 8 : 15;
            int hbH = s.Mini ? 7 : 15;
            int hbOffY = s.Mini ? ((16 - hbH) >> 1) : 0;
            int collisionX = s.X_fixed >> 8;
            int collisionY = offsetY + hbOffY;

            PfUpdateSlopeCounters(ref s);

            if (!s.GravFlipped)
            {
                // Normal gravity: check slopes first, then floor
                var (slopeHit, slopeEject, slopeType) = PfCheckSlopes(ref s, input);
                if (slopeHit)
                {
                    if (slopeEject > 0)
                        s.Y_fixed = ((s.Y_fixed >> 8) - slopeEject) << 8;
                    s.VelY_fixed = 0;
                    s.WasZeroedByCollision = true;
                    s.SlopeFrames = 1; s.SlopeWasOnCounter = 3; s.SlopeType = slopeType;
                }
                else
                {
                    // spider_eject calls bg_coll_D (3 probes, no inset), not bg_coll_D_spider.
                    var (hit, ejectAmt) = BgCollD_Spider(collisionX, collisionY, hbW, hbH, useEjectProbes: true);
                    if (hit)
                    {
                        s.Y_fixed = ((s.Y_fixed >> 8) - ejectAmt) << 8;
                        s.VelY_fixed = 0;
                        s.WasZeroedByCollision = true;
                    }
                    else
                    {
                        s.WasZeroedByCollision = false;
                    }
                }
            }
            else
            {
                // Inverted gravity: ceiling check
                // spider_eject calls bg_coll_U (3 probes, no inset), not bg_coll_U_spider.
                var (hit, ejectAmt) = BgCollU_Spider(collisionX, collisionY, hbW, hbH, useEjectProbes: true);
                if (hit)
                {
                    // NES eject lands at exactly surfaceBottom via byte-wrap math.
                    // collisionY + ejectAmt == surfaceBottom (the offset cancels out).
                    s.Y_fixed = (collisionY + ejectAmt) << 8;
                    s.VelY_fixed = 0;
                    s.WasZeroedByCollision = true;
                }
                else
                {
                    s.WasZeroedByCollision = false;
                }
            }
        }

        private void SpiderScanUp(ref SimState s)
        {
            int hbW = s.Mini ? 8 : 15;
            int hbH = s.Mini ? 8 : 15; // scan-up uses 8×8 for mini (not 8×7)
            int playerX_px = s.X_fixed >> 8;
            int scanY = s.Y_fixed >> 8;

            for (int i = 0; i < 200; i++)
            {
                scanY -= 8;
                if (scanY <= -(groundRowsToReserve * TILE))
                {
                    scanY = 0;
                    break;
                }
                var (hit, ejectAmt) = BgCollU_Spider(playerX_px, scanY, hbW, hbH);
                if (hit)
                {
                    scanY += ejectAmt;
                    break;
                }
            }
            s.Y_fixed = scanY << 8;
        }

        private void SpiderScanDown(ref SimState s)
        {
            int hbW = s.Mini ? 8 : 15;
            int hbH = s.Mini ? 7 : 15;
            int hbOffY = s.Mini ? ((16 - hbH) >> 1) : 0;
            int playerX_px = s.X_fixed >> 8;
            int scanY = s.Y_fixed >> 8;
            int maxY = (mapHeight - groundRowsToReserve) * TILE - hbH;

            for (int i = 0; i < 200; i++)
            {
                scanY += 8;
                if (scanY >= maxY)
                {
                    scanY = maxY;
                    break;
                }
                var (hit, ejectAmt) = BgCollD_Spider(playerX_px, scanY + hbOffY, hbW, hbH);
                if (hit)
                {
                    scanY -= ejectAmt;
                    break;
                }
            }
            s.Y_fixed = scanY << 8;
        }

        /// <summary>Spider floor collision.
        /// useEjectProbes=false: bg_coll_D_spider style (2 probes, 3px inset) — for scan.
        /// useEjectProbes=true:  bg_coll_D style (3 probes, no inset) — for spider_eject.
        /// </summary>
        private (bool hit, int ejectAmount) BgCollD_Spider(int playerX_px, int playerY_px, int width, int height, bool useEjectProbes = false)
        {
            int checkY = playerY_px + height;

            int tileY = checkY / TILE;
            int tileArrayY = tileY + _collisionMap.GroundRowsToReserve;

            // Map bottom = solid
            if (tileArrayY >= _collisionMap.MapHeight)
            {
                int groundTop = (mapHeight - groundRowsToReserve) * TILE;
                return (true, checkY - groundTop);
            }
            if (tileArrayY < 0) return (false, 0);

            int[] probes;
            if (useEjectProbes)
            {
                // NES bg_coll_D: 3 probes at X, X+width/2, X+width (no inset)
                probes = new[] { playerX_px, playerX_px + (width >> 1), playerX_px + width };
            }
            else
            {
                // NES bg_coll_D_spider: 2 probes with 3px inset
                probes = new[] { playerX_px + 3, playerX_px + width - 3 };
            }

            foreach (int probeX in probes)
            {
                int tx = probeX / TILE;
                if (tx < 0 || tx >= _collisionMap.MapWidth) continue;
                int idx = tileArrayY * _collisionMap.MapWidth + tx;
                if (idx < 0 || idx >= _collisionMap.Tiles.Length) continue;

                var col = MetatileCollisionTable.GetCollision((byte)_collisionMap.Tiles[idx]);
                if (IsSolidForSpider(col))
                {
                    var (_, colTop, _, _) = SharedPhysics.GetCollisionBounds(col);
                    int tileWorldY = (tileArrayY - _collisionMap.GroundRowsToReserve) * TILE;
                    int surfaceY = tileWorldY + colTop;
                    if (checkY >= surfaceY)
                        return (true, checkY - surfaceY);
                }
            }
            return (false, 0);
        }

        /// <summary>Spider ceiling collision.
        /// useEjectProbes=false: bg_coll_U_spider style (2 probes, 3px inset) — for scan.
        /// useEjectProbes=true:  bg_coll_U style (3 probes, no inset) — for spider_eject.
        /// </summary>
        private (bool hit, int ejectAmount) BgCollU_Spider(int playerX_px, int playerY_px, int width, int height, bool useEjectProbes = false)
        {
            int checkY = playerY_px;

            int tileY = checkY / TILE;
            int tileArrayY = tileY + _collisionMap.GroundRowsToReserve;

            // Map top = solid ceiling
            if (tileArrayY < 0)
                return (true, -checkY);
            if (tileArrayY >= _collisionMap.MapHeight) return (false, 0);

            int[] probes;
            if (useEjectProbes)
            {
                // NES bg_coll_U: 3 probes at X, X+width/2, X+width (no inset)
                probes = new[] { playerX_px, playerX_px + (width >> 1), playerX_px + width };
            }
            else
            {
                // NES bg_coll_U_spider: 2 probes with 3px inset
                probes = new[] { playerX_px + 3, playerX_px + width - 3 };
            }

            foreach (int probeX in probes)
            {
                int tx = probeX / TILE;
                if (tx < 0 || tx >= _collisionMap.MapWidth) continue;
                int idx = tileArrayY * _collisionMap.MapWidth + tx;
                if (idx < 0 || idx >= _collisionMap.Tiles.Length) continue;

                var col = MetatileCollisionTable.GetCollision((byte)_collisionMap.Tiles[idx]);
                if (IsSolidForSpider(col))
                {
                    var (_, _, _, colBottom) = SharedPhysics.GetCollisionBounds(col);
                    int tileWorldY = (tileArrayY - _collisionMap.GroundRowsToReserve) * TILE;
                    int surfaceBottom = tileWorldY + colBottom;
                    return (true, surfaceBottom - checkY);
                }
            }
            return (false, 0);
        }

        private static bool IsSolidForSpider(MetatileCollision col)
        {
             return col != MetatileCollision.COL_NONE &&
                 !SharedPhysics.IsDeathCollision(col);
        }

        // -------------------------------------------------------------------
        //  SWINGCOPTER PHYSICS (reuses ball eject/gravity with swing constants)
        // -------------------------------------------------------------------

        private void SwingGravityStep(ref SimState s)
        {
            bool mini = s.Mini;
            int g = mini ? 0x38 : 0x32; // SWING_GRAVITY
            int fs = 0x430;              // SWING_MAX_FALLSPEED
            if (s.GravFlipped) { g = -g; fs = -fs; }

            int clampMaxY = Math.Max(0, (mapHeight * TILE - TILE)) << 8;
            SharedPhysics.CommonGravityRoutine(
                ref s.VelY_fixed, ref s.Y_fixed,
                g, fs,
                s.GravFlipped ? 0xFF : 0,
                s.Dashing, s.GravityMod, 1.0, true, s.VelX_fixed,
                clampMaxY);
        }

        // -------------------------------------------------------------------
        //  UFO PHYSICS (matching UfoPhysics_Fresh)
        // -------------------------------------------------------------------

        /// <summary>
        /// UFO gravity � delegates to SharedPhysics.CommonGravityRoutine.
        /// </summary>
        private void UfoGravityStep(ref SimState s)
        {
            int g = UfoGravity(s.Mini);
            int fs = UfoMaxFallSpeed(s.Mini);
            if (s.GravFlipped) { g = -g; fs = -fs; }

            SharedPhysics.CommonGravityRoutine(
                ref s.VelY_fixed,
                ref s.Y_fixed,
                g, fs,
                s.GravFlipped ? 0xFF : 0,
                s.Dashing, s.GravityMod, 1.0, true, s.VelX_fixed,
                int.MaxValue); // no clamp for UFO (handled elsewhere)
        }

        // -------------------------------------------------------------------
        //  SHIP PHYSICS (matching ShipPhysics_Fresh + UfoShipEject_Fresh)
        // -------------------------------------------------------------------

        /// <summary>
        /// Ship gravity: 4 gravity variants depending on hold state and fall direction.
        /// When holding, gravity is negated (thrust opposite to gravity direction).
        /// Matching ShipPhysics_Fresh in ShipPhysics_Fresh.partial.cs.
        /// </summary>
        private void ShipGravityAndThrust(ref SimState s, bool holding, out bool died)
        {
            died = false;
            // NO GRAV_SKIP � NES common_gravity_routine() always applies gravity.
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

            // Ship uses SHIP_MAX_FALLSPEED_HOLD as tmpfallspeed in CommonGravityRoutine_Fresh.
            // Delegate to SharedPhysics for the accel/decel/integrate.
            int shipMaxFS = ShipMaxFallSpeedHold(s.Mini) * gravMul;

            int clampMaxY = Math.Max(0, (mapHeight * TILE - TILE)) << 8;

            SharedPhysics.CommonGravityRoutine(
                ref s.VelY_fixed,
                ref s.Y_fixed,
                gravity,       // tmpgravity (already direction-adjusted and negated for hold)
                shipMaxFS,     // tmpfallspeed (direction-adjusted)
                s.GravFlipped ? 0xFF : 0,
                s.Dashing, s.GravityMod, 1.0, true, s.VelX_fixed,
                clampMaxY);

            // -- Ship inverted-gravity ceiling grounding --
            // Match ShipPhysics_Fresh: after gravity+Y update, if inverted gravity
            // and there's a ceiling 1px above, zero upward velocity AND snap Y to
            // the ceiling surface to prevent sub-pixel drift (matching SIM).
            // NOTE: SIM probes at testY = playerY + hitboxOffsetY - 1 (1px above),
            // so we pass cgY - 1 to CheckCeiling which uses topY for its boundary
            // check (topY < colBottom_px). Without the -1, the check fails at
            // the exact ceiling boundary (e.g. 304 < 304 = false).
            // NES ship_movement has NO ceiling spike check � spikes handled
            // by bg_coll_floor_spikes (4-corner) which runs for all modes.
            if (s.GravFlipped)
            {
                int cgX = s.X_fixed >> 8;
                int hbOffY_cg = GetHitboxOffsetY(s.GameMode, s.Mini, s.GravFlipped);
                int cgY = (s.Y_fixed >> 8) + hbOffY_cg - 1;
                var (ceilGrounded, ceilBotY_cg, _) = CheckCeiling(cgX, cgY, GetHitboxW(s.Mini), GetHitboxH(s.Mini));
                if (ceilGrounded && s.VelY_fixed < 0)
                {
                    s.Y_fixed = (ceilBotY_cg - hbOffY_cg) << 8;
                    s.VelY_fixed = 0;
                }
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
        }

        /// <summary>
        /// Ship eject: ceiling + floor collision (matching UfoShipEject_Fresh).
        /// </summary>
        private void ShipEject(ref SimState s, bool input, out bool died)
        {
            died = false;
            var r = SharedPhysics.ShipUfoEject(in _collisionMap,
                s.X_fixed, s.Y_fixed, s.VelY_fixed, s.VelX_fixed,
                s.GravFlipped, s.Mini, s.GameMode, input,
                s.SlopeWasOnCounter, s.SlopeFrames, s.SlopeType,
                s.SlopeJumpHigher, s.LastSlopeType);
            s.Y_fixed = r.NewY_fixed;
            s.VelY_fixed = r.NewVelY_fixed;
            s.SlopeType = r.SlopeType;
            s.SlopeFrames = r.SlopeFrames;
            s.SlopeWasOnCounter = r.SlopeWasOnCounter;
            s.SlopeJumpHigher = r.SlopeJumpHigher;
            s.LastSlopeType = r.LastSlopeType;
            if (r.Died) { died = true; }
        }

        // -------------------------------------------------------------------
        //  GROUND SUPPORT VERIFICATION
        // -------------------------------------------------------------------

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
            return SharedPhysics.VerifyGroundSupport(_collisionMap,
                s.X_fixed >> 8, s.Y_fixed >> 8,
                s.GameMode, s.Mini, s.GravFlipped);
        }

        // -------------------------------------------------------------------
        //  SPRITE PROCESSING
        // -------------------------------------------------------------------

        private void ApplyPortalsUpTo(ref SimState s, int targetX_px)
        {
            int? lastGameModeSid = null, lastGameModeX = null;
            int? lastMiniSid = null, lastMiniX = null;
            int? lastGravSid = null, lastGravX = null;
            int? lastSpeedSid = null, lastSpeedX = null;
            int? lastDualSingleSid = null, lastDualSingleX = null;
            int? lastGravModSid = null, lastGravModX = null;
            int? lastCamLockSid = null, lastCamLockX = null;
            int? lastWrapSid = null, lastWrapX = null;

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
                    // Only pre-apply speed portals strictly BEFORE startX.
                    // Portals at startX are encountered naturally by ProcessSprites
                    // on frame 0, matching NES/SIM timing (speed change applies
                    // after X advance, not before).
                    if (sp.AnchorX_px < targetX_px)
                    {
                        if (!lastSpeedX.HasValue || sp.AnchorX_px > lastSpeedX.Value)
                        { lastSpeedSid = sid; lastSpeedX = sp.AnchorX_px; }
                    }
                }
                else if (sid == 0x22 || sid == 0x23)
                {
                    if (!lastDualSingleX.HasValue || sp.AnchorX_px > lastDualSingleX.Value)
                    { lastDualSingleSid = sid; lastDualSingleX = sp.AnchorX_px; }
                }
                else if ((sid >= 0x5F && sid <= 0x63) || (sid >= 0x70 && sid <= 0x74))
                {
                    // Gravity mod portals (0x5F-0x63) and triggers (0x70-0x74)
                    // both set the gravity multiplier; keep whichever is last by X
                    if (!lastGravModX.HasValue || sp.AnchorX_px >= lastGravModX.Value)
                    { lastGravModSid = sid; lastGravModX = sp.AnchorX_px; }
                }
                else if (sid == 0xDD || sid == 0xED)
                {
                    if (!lastCamLockX.HasValue || sp.AnchorX_px > lastCamLockX.Value)
                    { lastCamLockSid = sid; lastCamLockX = sp.AnchorX_px; }
                }
                else if (sid == 0x8E || sid == 0x9E)
                {
                    if (!lastWrapX.HasValue || sp.AnchorX_px > lastWrapX.Value)
                    { lastWrapSid = sid; lastWrapX = sp.AnchorX_px; }
                }

                // Don't mark speed portals at startX as processed — let
                // ProcessSprites handle them on the first frame naturally.
                if (!(IsSpeedPortal(sid) && sp.AnchorX_px >= targetX_px))
                    s.ProcessedSprites.Add(sp.Index);
            }

            if (lastGameModeSid.HasValue)
            {
                int mode = SpriteIdToGameMode(lastGameModeSid.Value);
                if (mode >= 0)
                {
                    s.GameMode = mode;
                    if (mode == 8) s.NinjaJumps = NINJA_MAX_JUMPS;
                }
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
            if (lastGravModSid.HasValue)
            {
                int gsid = lastGravModSid.Value;
                double newMod = 1.0;
                if (gsid >= 0x70 && gsid <= 0x74)
                {
                    switch (gsid) { case 0x70: newMod = 1.0/3.0; break; case 0x71: newMod = 0.5; break; case 0x72: newMod = 2.0/3.0; break; case 0x73: newMod = 2.0; break; case 0x74: newMod = 1.0; break; }
                }
                else
                {
                    switch (gsid) { case 0x5F: newMod = 1.0/3.0; break; case 0x60: newMod = 0.5; break; case 0x61: newMod = 2.0/3.0; break; case 0x62: newMod = 2.0; break; case 0x63: newMod = 1.0; break; }
                }
                s.GravityMod = newMod;
            }
            if (lastDualSingleSid.HasValue)
            {
                if (lastDualSingleSid.Value == 0x22)
                {
                    // Dual portal before start — activate dual with P2 at same pos, inverted gravity
                    s.DualActive = true;
                    s.P2_Y_fixed = s.Y_fixed;
                    s.P2_VelY_fixed = 0;
                    s.P2_GravFlipped = !s.GravFlipped;
                    s.P2_GravMul = -s.GravMul;
                    s.P2_Mini = s.Mini;
                    s.P2_WasZeroedByCollision = true;
                    s.P2_OnGround = true;
                    s.P2_PendingOrbIndex = -1;
                    s.P2_PendingOrbSpriteId = -1;
                }
                else
                {
                    s.DualActive = false;
                }
            }
            if (lastCamLockSid.HasValue)
            {
                s.NoCamLockForced = (lastCamLockSid.Value == 0xDD);
            }
            if (lastWrapSid.HasValue)
            {
                s.WrapMode = (lastWrapSid.Value == 0x8E);
            }
        }

        private bool ProcessSprites(ref SimState s, int currentX_px, out bool orbHitThisFrame)
        {
            orbHitThisFrame = false;
            int playerY_px = s.Y_fixed >> 8;
            int hbW = GetHitboxW(s.Mini);
            int hbH = GetHitboxH(s.Mini);
            int hbOffY = GetHitboxOffsetY(s.GameMode, s.Mini, s.GravFlipped);
            int playerTop = playerY_px + hbOffY;
            int playerBottom = playerTop + hbH;
            // NES sprite_collide() uses Generic.x = high_byte(currplayer_x) + 1
            int nesX = currentX_px + 1;
            int playerRight = nesX + hbW;  // exclusive right, matching SIM inclusive+1 conversion

            // --- Pre-scan for dual/single portals (0x22/0x23) ---
            // SIM checks dual portals BEFORE gamemode portals (CheckDualPortal runs
            // first). PF iterates sprites by index, so a gamemode portal at a lower
            // index can halve VelY before the dual portal captures it for P2.  Fix:
            // process dual/single portals in an earlier pass, matching SIM order.
            var sprArr = _spritesArr;
            int sprLen = sprArr.Length;
            int sprStart = SpriteLowerBound(currentX_px - 4 * TILE);
            for (int _si = sprStart; _si < sprLen; _si++)
            {
                ref readonly var sp = ref sprArr[_si];
                if (s.ProcessedSprites.Contains(sp.Index)) continue;
                if (sp.HitRight < currentX_px) continue;
                if (sp.AnchorX_px - TILE > playerRight + TILE) break;
                int dsid = sp.SpriteId;
                if (dsid != 0x22 && dsid != 0x23) continue;
                bool dxOverlap = !((playerRight) < sp.HitLeft || sp.HitRight < nesX);
                bool dyOverlap = !((playerBottom) < sp.HitTop || sp.HitBottom < playerTop);
                if (dxOverlap && dyOverlap)
                {
                    if (dsid == 0x22 && !s.DualActive)
                    {
                        s.DualActive = true;
                        // NES sets target_scroll_y from the dual portal's Y (spcl_dual_pt)
                        s.TargetCameraY_fixed = Math.Max(0, (sp.AnchorY_px - PORTAL_TO_TOP_DIFF_PX) << 8);
                        s.P2_Y_fixed = s.Y_fixed;
                        s.P2_VelY_fixed = -s.VelY_fixed;
                        s.P2_GravFlipped = !s.GravFlipped;
                        s.P2_GravMul = -s.GravMul;
                        s.P2_Mini = s.Mini;
                        s.P2_WasZeroedByCollision = false;
                        s.P2_OnGround = false;
                        s.P2_BallFlipCooldown = 0;
                        s.P2_BallInputBuffer = 0;
                        s.P2_BallCooldownFrames = 0;
                        s.P2_RobotJumpTime = 0;
                        s.P2_SlopeWasOnCounter = 0;
                        s.P2_SlopeFrames = 0;
                        s.P2_SlopeType = 0;
                        s.P2_Orbed = false;
                        s.P2_BlackOrbed = false;
                        s.P2_PrevInputHeld = s.PrevInputHeld;
                        s.P2_Dashing = 0;
                        s.P2_JBlocked = false;
                        s.P2_FBlocked = false;
                        s.P2_HBlocked = false;
                        s.P2_Dblocked = false;
                        s.P2_NinjaJumps = 0;
                        s.P2_PendingOrbIndex = -1;
                        s.P2_PendingOrbSpriteId = -1;
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[DUAL_ACTIVATE] idx={sp.Index} P2_Y={s.P2_Y_fixed >> 8} P2_VelY=0x{s.P2_VelY_fixed:X4} P2_GravFlipped={s.P2_GravFlipped}");
#endif
                    }
                    else if (dsid == 0x23 && s.DualActive)
                    {
                        s.DualActive = false;
                        // Set target_scroll_y from the portal's Y position (matching dual portal behavior)
                        s.TargetCameraY_fixed = Math.Max(0, (sp.AnchorY_px - PORTAL_TO_TOP_DIFF_PX) << 8);
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[SINGLE_ACTIVATE] idx={sp.Index} targetCamY={s.TargetCameraY_fixed >> 8}");
#endif
                    }
                    s.ProcessedSprites.Add(sp.Index);
                }
            }

            for (int _si = sprStart; _si < sprLen; _si++)
            {
                ref readonly var sp = ref sprArr[_si];
                if (s.ProcessedSprites.Contains(sp.Index)) continue;
                // Use strict < (not <=) because blue pads use padLeft = currentX_px
                // (no +1 offset). NES check_collision treats exclusive-bound == start
                // as overlapping (bcc = branch if less-than), so hitRight == currentX_px
                // is a valid overlap for blue pads and must not be skipped.
                if (sp.HitRight < currentX_px) continue;
                if (sp.AnchorX_px - TILE > playerRight + TILE) break;

                int sid = sp.SpriteId;

                // Speed portals: collision-based overlap detection (matching SIM cam-OFF behavior)
                if (IsSpeedPortal(sid))
                {
                    bool xOverlap = !((playerRight) < sp.HitLeft || sp.HitRight < nesX);
                    bool yOverlap = !((playerBottom) < sp.HitTop || sp.HitBottom < playerTop);
                    if (xOverlap && yOverlap)
                    {
                        int spd = SpriteIdToSpeedFixed(sid);
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[PORTAL_SPEED] sid=0x{sid:X2} idx={sp.Index} VelX: 0x{s.VelX_fixed:X4} -> 0x{spd:X4}");
#endif
                        if (spd > 0) s.VelX_fixed = spd;
                        s.ProcessedSprites.Add(sp.Index);
                    }
                    continue;
                }

                // Dual/single portals (0x22/0x23) — already handled in pre-scan above
                if (sid == 0x22 || sid == 0x23) continue;

                // Freecam portals (0xDD/0xED) — cam lock state
                if (sid == 0xDD || sid == 0xED)
                {
                    bool xOverlap = !((playerRight) < sp.HitLeft || sp.HitRight < nesX);
                    bool yOverlap = !((playerBottom) < sp.HitTop || sp.HitBottom < playerTop);
                    if (xOverlap && yOverlap)
                    {
                        s.NoCamLockForced = (sid == 0xDD);
                        s.ProcessedSprites.Add(sp.Index);
                    }
                    continue;
                }

                // Wrap mode portals (0x8E/0x9E) — wrap Y instead of OOB death
                if (sid == 0x8E || sid == 0x9E)
                {
                    bool xOverlap = !((playerRight) < sp.HitLeft || sp.HitRight < nesX);
                    bool yOverlap = !((playerBottom) < sp.HitTop || sp.HitBottom < playerTop);
                    if (xOverlap && yOverlap)
                    {
                        s.WrapMode = (sid == 0x8E);
                        s.ProcessedSprites.Add(sp.Index);
                    }
                    continue;
                }

                // Rainbow portals (0x64 = random mode 0-7, 0x7E = random mode 0-11)
                // NES spcl_rndmode / spcl_suprrnd — picks a random game mode.
                // PF can't do randomness; instead we set RainbowMaxMode so the BFS
                // verifies survival in ALL possible modes each frame.
                if ((sid == 0x64 || sid == 0x7E) && !_dualP2Guard)
                {
                    bool xOverlap = !((playerRight) < sp.HitLeft || sp.HitRight < nesX);
                    bool yOverlap = !((playerBottom) < sp.HitTop || sp.HitBottom < playerTop);
                    if (xOverlap && yOverlap)
                    {
                        if (s.RainbowMaxMode == 0)
                        {
                            // NES: if (gamemode == 0x06 || gamemode == 0x0A) currplayer_vel_y = 0;
                            if (s.GameMode == 6 || s.GameMode == 10) s.VelY_fixed = 0;
                            s.RainbowMaxMode = (sid == 0x64) ? 8 : 12;
#if !DISABLE_DEBUG_LOGGING
                            PfLog($"[RAINBOW_PORTAL] sid=0x{sid:X2} idx={sp.Index} maxMode={s.RainbowMaxMode} entryMode={s.GameMode}");
#endif
                        }
                        // NES gamemode_stuff: clearrobotjumpframes + set target_scroll_y
                        s.RobotJumpTime = 0;
                        bool modeFollowsY = (s.GameMode == 0 || s.GameMode == 4 || s.GameMode == 8 || s.GameMode == 9);
                        if (!modeFollowsY && !s.DualActive)
                        {
                            int portalWorldY_px = sp.AnchorY_px - TILE / 2;
                            s.TargetCameraY_fixed = Math.Max(0, (portalWorldY_px - PORTAL_TO_TOP_DIFF_PX) << 8);
                        }
                        s.ProcessedSprites.Add(sp.Index);
                    }
                    continue;
                }

                // Game mode portals, gravity portals, mini/growth portals, and end-level
                // are all handled in sprite_collide at OLD X (matching NES).
                // SIM skips CheckGameModePortals() for P2 in dual mode — only P1
                // detects gamemode portals.  Skip them here when processing P2.
                if (IsGameModePortal(sid) && _dualP2Guard)
                    continue;
                if (IsGameModePortal(sid) || IsGravityPortal(sid) ||
                    IsMiniGrowthPortal(sid) || IsEndLevel(sid))
                {
                    bool xOverlap = !((playerRight) < sp.HitLeft || sp.HitRight < nesX);
                    bool yOverlap = !((playerBottom) < sp.HitTop || sp.HitBottom < playerTop);

                    // End-level trigger (0x0F) fires on X overlap only, but must be
                    // vertically on-screen (NES only processes on-screen activesprites).
                    bool hit;
                    if (IsEndLevel(sid))
                    {
                        int screenTopY = s.CameraY_fixed >> 8;
                        int screenBottomY = screenTopY + SCREEN_H_PX;
                        int spriteWorldY = sp.AnchorY_px - TILE / 2;
                        bool onScreenY = !(spriteWorldY + TILE < screenTopY || spriteWorldY >= screenBottomY);
                        hit = xOverlap && onScreenY;
                    }
                    else
                    {
                        hit = xOverlap && yOverlap;
                    }

#if !DISABLE_DEBUG_LOGGING
                    if (IsGravityPortal(sid) || IsEndLevel(sid))
                    {
                        PfLog($"[SPRITE_CHECK] sid=0x{sid:X2} idx={sp.Index} playerBox=({nesX},{playerTop})-({playerRight},{playerBottom}) spriteBox=({sp.HitLeft},{sp.HitTop})-({sp.HitRight},{sp.HitBottom}) xOvlp={xOverlap} yOvlp={yOverlap} hit={hit}");
                    }
#endif

                    if (hit)
                    {
                        int prevMode = s.GameMode;
                        bool applied = ApplyPortalSprite(ref s, sid);
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[SPRITE_HIT] sid=0x{sid:X2} idx={sp.Index} applied={applied} gravFlipped={s.GravFlipped} gravMul={s.GravMul}");
#endif
                        if (applied)
                            s.ProcessedSprites.Add(sp.Index);
                        // Update camera target on ANY game mode portal hit
                        // (matching NES: target_scroll_y is set unconditionally
                        // for ship/UFO/ball/wave/etc portals, even same-mode).
                        if (IsGameModePortal(sid) && applied)
                        {
                            bool modeFollowsY = (s.GameMode == 0 || s.GameMode == 4 || s.GameMode == 8 || s.GameMode == 9);
                            if (!modeFollowsY && !s.DualActive)
                            {
                                int portalWorldY_px = sp.AnchorY_px - TILE / 2;
                                s.TargetCameraY_fixed = Math.Max(0, (portalWorldY_px - PORTAL_TO_TOP_DIFF_PX) << 8);
                            }
                        }
                        if (IsEndLevel(sid)) return true;
                    }
                    continue;
                }

                // Gravity modifier portals (0x5F-0x63) — collision-based, both P1 and P2
                if (sid >= 0x5F && sid <= 0x63)
                {
                    bool xOverlap = !((playerRight) < sp.HitLeft || sp.HitRight < nesX);
                    bool yOverlap = !((playerBottom) < sp.HitTop || sp.HitBottom < playerTop);
                    if (xOverlap && yOverlap)
                    {
                        double newMod = 1.0;
                        switch (sid)
                        {
                            case 0x5F: newMod = 1.0 / 3.0; break;
                            case 0x60: newMod = 0.5; break;
                            case 0x61: newMod = 2.0 / 3.0; break;
                            case 0x62: newMod = 2.0; break;
                            case 0x63: newMod = 1.0; break;
                        }
                        s.GravityMod = newMod;
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[GRAV_MOD_PORTAL] sid=0x{sid:X2} idx={sp.Index} multiplier={newMod:F3}x");
#endif
                        s.ProcessedSprites.Add(sp.Index);
                    }
                    continue;
                }

                // Gravity modifier triggers (0x70-0x74) — handled by CheckGravityModTriggersAtNewX
                if (sid >= 0x70 && sid <= 0x74) continue;

                // Pad detection (requires hitbox overlap)
                // Pads fire EVERY overlapping frame (famidash has activation tracking commented out).
                // Do NOT add to ProcessedSprites � must match sim behavior.
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

#if !DISABLE_DEBUG_LOGGING
                    if (IsBluePad(sid) && xOverlap)
                        PfLog($"[BPAD_PF] idx={sp.Index} sid=0x{sid:X2} pad=({padLeft},{playerTop})-({padRight},{playerBottom}) spr=({sp.HitLeft},{sp.HitTop})-({sp.HitRight},{sp.HitBottom}) xO={xOverlap} yO={yOverlap}");
#endif

                    if (xOverlap && yOverlap)
                    {
                        ApplyPadSprite(ref s, sid);
                        orbHitThisFrame = true;
                    }
                    continue;
                }

                // Orb detection: orbs require player input to activate.
                // When overlapping, store as pending � the decision logic or
                // StepFrame will decide whether to activate.
                if (IsOrbSprite(sid))
                {
                    if (s.Dashing != 0) continue; // block orbs while dashing (matches SIM/NES)
                    // NES orb check uses sprite centering (Generic.y += (0x10-h)>>1 = +4 for mini),
                    // distinct from floor-collision offset (+9) used by pads/portals.
                    int orbOffY = (s.Mini && !s.GravFlipped) ? SharedPhysics.GetMiniCenterOffsetY(true) : 0;
                    int orbPlayerTop = playerY_px + orbOffY;
                    int orbPlayerBottom = orbPlayerTop + hbH;
                    bool xOverlap = !((playerRight) < sp.HitLeft || sp.HitRight < nesX);
                    bool yOverlap = !((orbPlayerBottom) < sp.HitTop || sp.HitBottom < orbPlayerTop);

                    if (xOverlap && yOverlap)
                    {
                        s.PendingOrbIndex = sp.Index;
                        s.PendingOrbSpriteId = sid;
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[ORB_PENDING] sid=0x{sid:X2} idx={sp.Index} playerBox=({nesX},{orbPlayerTop})-({playerRight},{orbPlayerBottom}) spriteBox=({sp.HitLeft},{sp.HitTop})-({sp.HitRight},{sp.HitBottom})");
#endif
                    }
                    continue;
                }

                // Dash orb detection: store as pending (same as regular orbs, but block other orbs while dashing)
                if (IsDashOrb(sid))
                {
                    if (s.Dashing != 0) continue; // block orbs while dashing
                    int orbOffY = (s.Mini && !s.GravFlipped) ? SharedPhysics.GetMiniCenterOffsetY(true) : 0;
                    int orbPlayerTop = playerY_px + orbOffY;
                    int orbPlayerBottom = orbPlayerTop + hbH;
                    bool xOverlap = !((playerRight) < sp.HitLeft || sp.HitRight < nesX);
                    bool yOverlap = !((orbPlayerBottom) < sp.HitTop || sp.HitBottom < orbPlayerTop);
                    if (xOverlap && yOverlap)
                    {
                        s.PendingOrbIndex = sp.Index;
                        s.PendingOrbSpriteId = sid;
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[DASH_ORB_PENDING] sid=0x{sid:X2} idx={sp.Index}");
#endif
                    }
                    continue;
                }

                // Spider orb/pad detection
                if (IsSpiderOrb(sid) || IsSpiderPad(sid))
                {
                    int orbOffY = (s.Mini && !s.GravFlipped) ? SharedPhysics.GetMiniCenterOffsetY(true) : 0;
                    int orbPlayerTop = playerY_px + orbOffY;
                    int orbPlayerBottom = orbPlayerTop + hbH;
                    bool xOverlap = !((playerRight) < sp.HitLeft || sp.HitRight < nesX);
                    bool yOverlap = !((orbPlayerBottom) < sp.HitTop || sp.HitBottom < orbPlayerTop);
                    if (xOverlap && yOverlap)
                    {
                        bool isOrb = IsSpiderOrb(sid);
                        if (isOrb)
                        {
                            // Spider orbs require input — store as pending
                            s.PendingOrbIndex = sp.Index;
                            s.PendingOrbSpriteId = sid;
                        }
                        else
                        {
                            // Spider pads fire immediately
                            bool goUp = (sid == 0x56);
                            ApplySpiderTeleport(ref s, goUp);
                            orbHitThisFrame = true;
                        }
                    }
                    continue;
                }

                // S_BLOCK detection: stops dash
                if (sid == 0xF9)
                {
                    bool xOverlap = !((playerRight) < sp.HitLeft || sp.HitRight < nesX);
                    bool yOverlap = !((playerBottom) < sp.HitTop || sp.HitBottom < playerTop);
                    if (xOverlap && yOverlap && s.Dashing != 0)
                    {
                        s.Dashing = 0;
                        s.Orbed = true;
                        s.VelY_fixed = 0;
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[S_BLOCK] Stopped dash at idx={sp.Index}");
#endif
                    }
                    continue;
                }

                // J_BLOCK detection: sets jblocked (gamemode_cube.h Path 2 press-to-jump)
                // NES also sets orbed here, but the PF AI doesn't need the
                // "release button to clear orbed" gate — jblocked alone gates
                // Path 1 (!jblocked) and enables Path 2 (pressInput && jblocked).
                if (sid == 0xF7)
                {
                    bool xOverlap = !((playerRight) < sp.HitLeft || sp.HitRight < nesX);
                    bool yOverlap = !((playerBottom) < sp.HitTop || sp.HitBottom < playerTop);
                    if (xOverlap && yOverlap)
                    {
                        s.JBlocked = true;
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[J_BLOCK] Set jblocked at idx={sp.Index}");
#endif
                    }
                    continue;
                }

                // F_BLOCK detection: sets fblocked (gamemode_cube.h Path 2 press-to-jump)
                if (sid == 0xF6)
                {
                    bool xOverlap = !((playerRight) < sp.HitLeft || sp.HitRight < nesX);
                    bool yOverlap = !((playerBottom) < sp.HitTop || sp.HitBottom < playerTop);
                    if (xOverlap && yOverlap)
                    {
                        s.FBlocked = true;
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[F_BLOCK] Set fblocked at idx={sp.Index}");
#endif
                    }
                    continue;
                }

                // H_BLOCK detection: sets hblocked (headbonk - ceiling ejection)
                if (sid == 0xF8)
                {
                    bool xOverlap = !((playerRight) < sp.HitLeft || sp.HitRight < nesX);
                    bool yOverlap = !((playerBottom) < sp.HitTop || sp.HitBottom < playerTop);
                    if (xOverlap && yOverlap)
                    {
                        s.HBlocked = true;
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[H_BLOCK] Set hblocked at idx={sp.Index}");
#endif
                    }
                    continue;
                }

                // D_BLOCK detection: sets dblocked (wave walks on surfaces instead of dying)
                if (sid == 0xFA)
                {
                    bool xOverlap = !((playerRight) < sp.HitLeft || sp.HitRight < nesX);
                    bool yOverlap = !((playerBottom) < sp.HitTop || sp.HitBottom < playerTop);
                    if (xOverlap && yOverlap)
                    {
                        s.Dblocked = true;
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[D_BLOCK] Set dblocked at idx={sp.Index}");
#endif
                    }
                    continue;
                }

                // Teleport portal entrance detection
                if (IsTeleportPortalEntrance(sid))
                {
                    bool xOverlap = !((playerRight) < sp.HitLeft || sp.HitRight < nesX);
                    bool yOverlap = !((playerBottom) < sp.HitTop || sp.HitBottom < playerTop);
                    if (xOverlap && yOverlap)
                    {
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[TELEPORT_ENTRANCE] sid=0x{sid:X2} idx={sp.Index} playerBox=({nesX},{playerTop})-({playerRight},{playerBottom}) spriteBox=({sp.HitLeft},{sp.HitTop})-({sp.HitRight},{sp.HitBottom})");
#endif
                        ApplyTeleportPortal(ref s, sp, sid, currentX_px);
                        s.ProcessedSprites.Add(sp.Index);
                    }
                    continue;
                }

                // Coin collection (during BFS / speculative execution)
                if (IsCoinSprite(sid) || IsMiniCoinSprite(sid))
                {
                    bool xOverlap = !((playerRight) < sp.HitLeft || sp.HitRight < nesX);
                    bool yOverlap = !((playerBottom) < sp.HitTop || sp.HitBottom < playerTop);
                    if (xOverlap && yOverlap)
                        s.ProcessedSprites.Add(sp.Index);
                    continue;
                }
            }

            return false;
        }

        /// <summary>
        /// Post-Y-integration gravity portal check. Matching the simulator's inline
        /// gravity portal detection that runs AFTER playerY_fixed += playerVelY_fixed
        /// (SimulatorWindow line ~10821). Uses 14�14 center-based hitbox.
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

            int gmStart = SpriteLowerBound(prevX_px - 4 * TILE);
            for (int _gm = gmStart; _gm < _spritesArr.Length; _gm++)
            {
                ref readonly var sp = ref _spritesArr[_gm];
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
                    PfLog($"[GRAV_POSTY_HIT] sid=0x{sid:X2} applied={applied} gravFlipped={s.GravFlipped} gravMul={s.GravMul} VelY=0x{s.VelY_fixed:X4}");
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
        /// Uses a centered 15�15 hitbox and NES-style (+1) overlap conversion matching
        /// SpriteIntersectsPlayer (line 1136): (playerRight+1) &lt; spriteLeft.
        /// </summary>
        private void CheckGameModePortalsAtNewX(ref SimState s)
        {
            int playerX_px = s.X_fixed >> 8;
            // NES sprite_collide: Generic.x = high_byte(currplayer_x) + 1
            // Hitbox size depends on mini mode (matching SIM)
            int hitboxW = s.Mini ? 8 : 15;
            int hitboxH = s.Mini ? 7 : 15;
            int playerLeft = playerX_px + 1;
            int playerRight = playerLeft + hitboxW - 1;
            int playerTop = (s.Y_fixed >> 8) + SharedPhysics.GetHitboxOffsetY(s.GameMode, s.Mini, s.GravFlipped);
            int playerBottom = playerTop + hitboxH - 1;

            int gmStart = SpriteLowerBound(playerX_px - 4 * TILE);
            for (int _gm = gmStart; _gm < _spritesArr.Length; _gm++)
            {
                ref readonly var sp = ref _spritesArr[_gm];
                if (s.ProcessedSprites.Contains(sp.Index)) continue;
                if (sp.HitRight <= playerLeft) continue;
                if (sp.AnchorX_px - TILE > playerRight + TILE) break;

                int sid = sp.SpriteId;

                // SIM's post-physics portal loop checks BOTH game-mode portals
                // and gravity portals at NEW X (SimulatorWindow line ~11231).
                if (!IsGameModePortal(sid) && !IsGravityPortal(sid)) continue;

                // NES-style overlap: convert inclusive player bounds to exclusive with +1,
                // matching SpriteIntersectsPlayer's check:
                //   !((playerRight+1) < spriteLeft || spriteRight < playerLeft ||
                //     (playerBottom+1) < spriteTop || spriteBottom < playerTop)
                // Sprite HitRight/HitBottom are already exclusive (left + width).
                bool xOverlap = !((playerRight + 1) < sp.HitLeft || sp.HitRight < playerLeft);
                bool yOverlap = !((playerBottom + 1) < sp.HitTop || sp.HitBottom < playerTop);
                bool hit = xOverlap && yOverlap;

#if !DISABLE_DEBUG_LOGGING
                if (IsGameModePortal(sid))
                    PfLog($"[GAMEMODE_NEWX_CHECK] sid=0x{sid:X2} idx={sp.Index} playerBox=({playerLeft},{playerTop})-({playerRight},{playerBottom}) spriteBox=({sp.HitLeft},{sp.HitTop})-({sp.HitRight},{sp.HitBottom}) xOvlp={xOverlap} yOvlp={yOverlap} hit={hit}");
                else
                    PfLog($"[GRAV_NEWX_CHECK] sid=0x{sid:X2} idx={sp.Index} playerBox=({playerLeft},{playerTop})-({playerRight},{playerBottom}) spriteBox=({sp.HitLeft},{sp.HitTop})-({sp.HitRight},{sp.HitBottom}) xOvlp={xOverlap} yOvlp={yOverlap} hit={hit}");
#endif

                if (hit)
                {
                    bool applied = ApplyPortalSprite(ref s, sid);
#if !DISABLE_DEBUG_LOGGING
                    if (IsGameModePortal(sid))
                        PfLog($"[GAMEMODE_NEWX_HIT] sid=0x{sid:X2} applied={applied} mode={s.GameMode} VelY=0x{s.VelY_fixed:X4}");
                    else
                        PfLog($"[GRAV_NEWX_HIT] sid=0x{sid:X2} applied={applied} gravFlipped={s.GravFlipped} gravMul={s.GravMul} VelY=0x{s.VelY_fixed:X4}");
#endif
                    if (applied)
                        s.ProcessedSprites.Add(sp.Index);
                    // Update camera target on ANY game mode portal hit (NES sets
                    // target_scroll_y unconditionally, even for same-mode portals).
                    if (IsGameModePortal(sid) && applied)
                    {
                        bool modeFollowsY = (s.GameMode == 0 || s.GameMode == 4 || s.GameMode == 8 || s.GameMode == 9);
                        if (!modeFollowsY && !s.DualActive)
                        {
                            int portalWorldY_px = sp.AnchorY_px - TILE / 2;
                            s.TargetCameraY_fixed = Math.Max(0, (portalWorldY_px - PORTAL_TO_TOP_DIFF_PX) << 8);
                        }
                    }
                    break; // only one portal per frame (matching sim's break)
                }
            }
        }

        /// <summary>
        /// Post-physics speed portal check at NEW X using anchor-based detection.
        /// The SIM activates speed portals when the portal's anchor center is behind
        /// the camera center (playerX + 48px), matching NES interaction-line behaviour.
        /// The camera center is computed as: cameraX + screenWidth/2.  After the
        /// interaction line is crossed at screen-X 80, cameraX = playerX - 80*256,
        /// so center = playerX + (128-80)*256 = playerX + 0x3000.
        /// </summary>
        private void CheckSpeedPortalsAtNewX(ref SimState s)
        {
            const int CAMERA_CENTER_OFFSET = 0x3000; // (NES_W*TILE/2 - 80) << 8
            int center_fixed = s.X_fixed + CAMERA_CENTER_OFFSET;

            int spStart = SpriteLowerBound((s.X_fixed >> 8) - 4 * TILE);
            for (int _si = spStart; _si < _spritesArr.Length; _si++)
            {
                ref readonly var sp = ref _spritesArr[_si];
                if (s.ProcessedSprites.Contains(sp.Index)) continue;
                int sid = sp.SpriteId;
                if (!IsSpeedPortal(sid)) continue;

                int anchorX_center_fixed = sp.AnchorX_px << 8;
                if (anchorX_center_fixed <= center_fixed)
                {
                    int spd = SpriteIdToSpeedFixed(sid);
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[PORTAL_SPEED] sid=0x{sid:X2} VelX: 0x{s.VelX_fixed:X4} -> 0x{spd:X4}");
#endif
                    if (spd > 0) s.VelX_fixed = spd;
                    s.ProcessedSprites.Add(sp.Index);
                    break; // one speed portal per frame
                }
            }
        }

        /// <summary>
        /// Gravity modifier triggers (0x70-0x74) use camera-center-based activation,
        /// matching SIM's CheckGravityModTriggers.  Only called for P1.
        /// </summary>
        private void CheckGravityModTriggersAtNewX(ref SimState s)
        {
            const int CAMERA_CENTER_OFFSET = 0x3000;
            int center_fixed = s.X_fixed + CAMERA_CENTER_OFFSET;

            int gmtStart = SpriteLowerBound((s.X_fixed >> 8) - 4 * TILE);
            for (int _si = gmtStart; _si < _spritesArr.Length; _si++)
            {
                ref readonly var sp = ref _spritesArr[_si];
                if (s.ProcessedSprites.Contains(sp.Index)) continue;
                int sid = sp.SpriteId;
                if (sid < 0x70 || sid > 0x74) continue;

                int anchorX_center_fixed = sp.AnchorX_px << 8;
                if (anchorX_center_fixed <= center_fixed)
                {
                    double newMod = 1.0;
                    switch (sid)
                    {
                        case 0x70: newMod = 1.0 / 3.0; break;
                        case 0x71: newMod = 0.5; break;
                        case 0x72: newMod = 2.0 / 3.0; break;
                        case 0x73: newMod = 2.0; break;
                        case 0x74: newMod = 1.0; break;
                    }
                    s.GravityMod = newMod;
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[GRAV_MOD_TRIG] sid=0x{sid:X2} idx={sp.Index} multiplier={newMod:F3}x");
#endif
                    s.ProcessedSprites.Add(sp.Index);
                    // Don't break — multiple triggers at same X should all fire (last wins)
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
                    PfLog($"[PORTAL_GAMEMODE] sid=0x{sid:X2} mode {s.GameMode} -> {mode} VelY halved: 0x{s.VelY_fixed:X4} -> 0x{s.VelY_fixed / 2:X4}");
#endif
                    if (_speculativeDepth == 0 && false) // DEBUG: mode transition logging
                        _log.WriteLine($"[MODE_TRANSITION] {s.GameMode}->{mode} Y={s.Y_fixed >> 8} VelY=0x{s.VelY_fixed:X} X={s.X_fixed >> 8} GravMul={s.GravMul} GravFlip={s.GravFlipped} Processed={s.ProcessedSprites.Count}");
                    // Save last cube/ball checkpoint before entering ship/UFO so
                    // cross-mode backtracking can reach it even after FIFO eviction.
                    if ((s.GameMode == 0 || s.GameMode == 2) && (mode == 1 || mode == 3) && _backtrackCheckpoints != null)
                    {
                        for (int ci = _backtrackCheckpoints.Count - 1; ci >= 0; ci--)
                        {
                            if (_backtrackCheckpoints[ci].GameMode == s.GameMode)
                            {
                                var src = _backtrackCheckpoints[ci];
                                _lastCubeToShipCheckpoint = new BacktrackCheckpoint
                                {
                                    Frame = src.Frame,
                                    State = src.State.Clone(),
                                    HoldJumpState = src.HoldJumpState,
                                    HoldDelayState = src.HoldDelayState,
                                    CommittedDelayState = src.CommittedDelayState,
                                    PathPointCount = src.PathPointCount,
                                    InputCount = src.InputCount,
                                    RetryStage = 0,
                                    UsedBias = src.UsedBias,
                                    ShipBias = src.ShipBias,
                                    GameMode = src.GameMode,
                                    ShipForceHold = src.ShipForceHold,
                                    ShipForceRelease = src.ShipForceRelease,
                                    ShipCommitFrames = src.ShipCommitFrames,
                                    ShipCommitHold = src.ShipCommitHold,
                                    ForceJumpRemaining = src.ForceJumpRemaining,
                                    SkipAllOrbs = src.SkipAllOrbs,
                                    SkipSpecificOrbs = new HashSet<int>(src.SkipSpecificOrbs ?? new()),
                                    SkipSpecificPads = new HashSet<int>(src.SkipSpecificPads ?? new()),
                                    PrevFrameWasGrounded = src.PrevFrameWasGrounded,
                                    NextCoinCheckIdx = src.NextCoinCheckIdx,
                                };
                                // Also save a persistent copy for coin collect-lose recovery
                                _shipEntryRecoveryCheckpoint = new BacktrackCheckpoint
                                {
                                    Frame = src.Frame,
                                    State = src.State.Clone(),
                                    HoldJumpState = src.HoldJumpState,
                                    HoldDelayState = src.HoldDelayState,
                                    CommittedDelayState = src.CommittedDelayState,
                                    PathPointCount = src.PathPointCount,
                                    InputCount = src.InputCount,
                                    RetryStage = 0,
                                    UsedBias = src.UsedBias,
                                    ShipBias = src.ShipBias,
                                    GameMode = src.GameMode,
                                    ShipForceHold = src.ShipForceHold,
                                    ShipForceRelease = src.ShipForceRelease,
                                    ShipCommitFrames = src.ShipCommitFrames,
                                    ShipCommitHold = src.ShipCommitHold,
                                    ForceJumpRemaining = src.ForceJumpRemaining,
                                    SkipAllOrbs = src.SkipAllOrbs,
                                    SkipSpecificOrbs = new HashSet<int>(src.SkipSpecificOrbs ?? new()),
                                    SkipSpecificPads = new HashSet<int>(src.SkipSpecificPads ?? new()),
                                    PrevFrameWasGrounded = src.PrevFrameWasGrounded,
                                    NextCoinCheckIdx = src.NextCoinCheckIdx,
                                };
                                break;
                            }
                        }
                    }
                    int prevMode = s.GameMode;
                    s.GameMode = mode;
                    // Real game-mode portal ends rainbow multi-mode verification
                    s.RainbowMaxMode = 0;
                    // Shadow cleanup is handled by the BFS expansion loop after
                    // StepFrame returns (it detects RainbowMaxMode==0 with non-null
                    // RainbowShadows and returns their resources there).
                    // NES: unconditionally halve velocity on mode change
                    // (sprite_loading.h line 764: currplayer_vel_y /= 2)
                    s.VelY_fixed /= 2;
                    // Clear stale collision-zeroing flag so it doesn't leak
                    // across game modes.  E.g. ball eject sets the flag; wave
                    // then wrongly skips velY recalculation.  Matches SIM's
                    // CheckGameModePortals which clears wasZeroedByCollisionLastFrame.
                    s.WasZeroedByCollision = false;
                    // Clear ball input buffer — a buffered flip from a previous
                    // ball segment shouldn't leak through other modes.
                    s.BallInputBuffer = 0;
                    // Initialize ninja jump count on entering ninja mode
                    // NES sprite_loading.h: clearrobotjumpframes() on ninja portal
                    if (mode == 8)
                    {
                        s.NinjaJumps = NINJA_MAX_JUMPS;
                        s.RobotJumpTime = 0;
                        if (_speculativeDepth == 0)
                        {
                            _committedNinjaJumps.Clear();
                            _ninjaWaitFrames = 0;
                        }
                    }
                    else if (mode == 4)
                    {
                        // Entering robot — clear ninja state
                        s.RobotJumpTime = 0;
                    }
                    // Ball?ship/UFO stabilization: suppress coin-seeking for
                    // a limited window so the ship navigates initial obstacles with
                    // pure survival + corridorCenter PD (matching no-coin
                    // behavior).  Cube?ship/UFO gets no stabilization since
                    // those transitions are typically obstacle-free.
                    // If there's a coin near the transition, reduce stabilization
                    // so the ship can target it in time.
                    if (_speculativeDepth == 0 && (mode == 1 || mode == 3))
                    {
                        int baseStab = (prevMode == 2) ? 200 : 0;
                        if (baseStab > 0 && PreferCoins && allCoins.Count > 0)
                        {
                            int shipX = s.X_fixed >> 8;
                            for (int ci = _nextCoinCheckIdx; ci < allCoins.Count; ci++)
                            {
                                var coin = allCoins[ci];
                                if (s.ProcessedSprites.Contains(coin.Index) || _forgivenCoins.Contains(coin.Index))
                                    continue;
                                if (coin.HitLeft > shipX + 600) break;
                                if (coin.HitRight < shipX) continue;
                                // Coin is within 600px of the transition � reduce stabilization
                                // drastically. The ship needs nearly the full distance to
                                // descend/ascend to the coin's altitude.
                                baseStab = 20; // minimal stabilization for initial obstacle avoidance
                                break;
                            }
                        }
                        _modeTransitionStabilizeFrames = baseStab;
                        _log.WriteLine($"[STAB_SET] prevMode={prevMode} mode={mode} stab={_modeTransitionStabilizeFrames}");
                    }
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
                PfLog($"[PORTAL_SPEED] sid=0x{sid:X2} VelX: 0x{s.VelX_fixed:X4} -> 0x{spd:X4}");
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
                    PfLog($"[PORTAL_GRAV_FLIP] REVERSED! VelY halved: 0x{s.VelY_fixed:X4} -> 0x{s.VelY_fixed / 2:X4}");
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
                    PfLog($"[PORTAL_GRAV_FLIP] NORMAL! VelY halved: 0x{s.VelY_fixed:X4} -> 0x{s.VelY_fixed / 2:X4}");
#endif
                    s.VelY_fixed /= 2;
                    s.WasZeroedByCollision = false;
                    return true;
                }
                // Conditions not met � portal not consumed
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
            // Sim applies: baseVel * (gravInverted ? 1 : -1)  ?  launch against gravity
            int gravSign = s.GravFlipped ? 1 : -1;

            if (IsYellowPad(sid))
            {
                s.VelY_fixed = GetPadOrbVel(1, s.Mini, s.GameMode) * gravSign;
                s.OnGround = false;
            }
            else if (IsPinkPad(sid))
            {
                s.VelY_fixed = GetPadOrbVel(3, s.Mini, s.GameMode) * gravSign;
                s.OnGround = false;
            }
            else if (IsRedPad(sid))
            {
                s.VelY_fixed = GetPadOrbVel(8, s.Mini, s.GameMode) * gravSign;
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
                int bluePadMag = SharedPhysics.BluePadVel(s.Mini);
                s.VelY_fixed = s.GravFlipped ? -bluePadMag : bluePadMag;
                s.OnGround = false;
            }
            else if (IsGreenPad(sid))
            {
                s.GravFlipped = !s.GravFlipped;
                s.GravMul = s.GravFlipped ? -1 : 1;
                int greenGravSign = s.GravFlipped ? 1 : -1;
                s.VelY_fixed = GetPadOrbVel(0, s.Mini, s.GameMode) * greenGravSign;
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
            // Sim applies: baseVel * (gravInverted ? 1 : -1)  ?  launch against gravity
            int orbGravSign = s.GravFlipped ? 1 : -1;

            if (IsYellowOrb(sid))
            {
                s.VelY_fixed = GetPadOrbVel(0, s.Mini, s.GameMode) * orbGravSign;
                s.OnGround = false;
            }
            else if (IsYellowOrbBigger(sid))
            {
                s.VelY_fixed = GetPadOrbVel(5, s.Mini, s.GameMode) * orbGravSign;
                s.OnGround = false;
            }
            else if (IsYellowOrbSmaller(sid))
            {
                s.VelY_fixed = GetPadOrbVel(7, s.Mini, s.GameMode) * orbGravSign;
                s.OnGround = false;
            }
            else if (IsPinkOrb(sid))
            {
                s.VelY_fixed = GetPadOrbVel(2, s.Mini, s.GameMode) * orbGravSign;
                s.OnGround = false;
            }
            else if (IsRedOrb(sid))
            {
                s.VelY_fixed = GetPadOrbVel(4, s.Mini, s.GameMode) * orbGravSign;
                s.OnGround = false;
            }
            else if (IsBlackOrb(sid))
            {
                // Black orb launches WITH gravity (downward when normal)
                // Sim: baseVel is already negative in table, * (gravInverted ? 1 : -1)
                // For normal gravity: negative baseVel * -1 = positive (downward) ?
                s.VelY_fixed = GetPadOrbVel(6, s.Mini, s.GameMode) * orbGravSign;
                s.OnGround = false;
            }
            else if (IsBlueOrb(sid))
            {
                // Flip gravity first, then launch TOWARD new ground
                s.GravFlipped = !s.GravFlipped;
                s.GravMul = s.GravFlipped ? -1 : 1;

                // dual_cap_check: blue orb flips BOTH players' gravity
                if (s.DualActive)
                {
                    if (_dualP2Guard)
                    {
                        // P2 is running — signal caller to flip P1's saved gravity + halve vel
                        _p2OrbFlippedOtherGrav = true;
                    }
                    else
                    {
                        // P1 is running — directly flip P2's stored gravity and halve velocity
                        s.P2_GravFlipped = !s.P2_GravFlipped;
                        s.P2_GravMul = s.P2_GravFlipped ? -1 : 1;
                        s.P2_VelY_fixed /= 2;
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[DUAL_CAP_CHECK] P1 blue orb flipped P2 grav→{s.P2_GravFlipped} velY→0x{s.P2_VelY_fixed:X4}");
#endif
                    }
                }

                // Sim uses hardcoded constants (NOT PadOrbHeights):
                //   PAD_HEIGHT_BLUE_normal = -0x3A0, PAD_HEIGHT_BLUE_mini = -0x160
                //   Ball mode uses smaller vel: ORB_BALL_HEIGHT_BLUE_normal = -0x1A0, mini = -0x60
                // Sign: if (!gravInverted) negate ? launches toward new ground
                int blueVel = -(int)SharedPhysics.BlueOrbVel(s.Mini, s.GameMode);
                if (!s.GravFlipped)
                    blueVel = -blueVel;
                s.VelY_fixed = blueVel;
                s.OnGround = false;
            }
            else if (IsGreenOrb(sid))
            {
                // Flip gravity, then bounce AGAINST new gravity (yellow-orb-strength)
                s.GravFlipped = !s.GravFlipped;
                s.GravMul = s.GravFlipped ? -1 : 1;

                // dual_cap_check: green orb flips BOTH players' gravity
                if (s.DualActive)
                {
                    if (_dualP2Guard)
                    {
                        _p2OrbFlippedOtherGrav = true;
                    }
                    else
                    {
                        s.P2_GravFlipped = !s.P2_GravFlipped;
                        s.P2_GravMul = s.P2_GravFlipped ? -1 : 1;
                        s.P2_VelY_fixed /= 2;
#if !DISABLE_DEBUG_LOGGING
                        PfLog($"[DUAL_CAP_CHECK] P1 green orb flipped P2 grav→{s.P2_GravFlipped} velY→0x{s.P2_VelY_fixed:X4}");
#endif
                    }
                }

                int greenOrbGravSign = s.GravFlipped ? 1 : -1;
                s.VelY_fixed = GetPadOrbVel(0, s.Mini, s.GameMode) * greenOrbGravSign;
                s.OnGround = false;
            }
            else if (IsWhiteOrb(sid))
            {
                // White orb: zero Y velocity
                s.VelY_fixed = 0;
            }
        }

        /// <summary>
        /// Apply dash orb activation. Sets dash state and velocity.
        /// Gravity dash variants flip gravity first (if not already dashing).
        /// </summary>
        private void ApplyDashOrb(ref SimState s, int sid)
        {
#if !DISABLE_DEBUG_LOGGING
            PfLog($"[DASH_ORB_ACTIVATE] sid=0x{sid:X2} mode={DashOrbMode(sid)} gravFlip={IsGravityDashOrb(sid)}");
#endif
            // Gravity dash orbs flip gravity first
            if (IsGravityDashOrb(sid) && s.Dashing == 0)
            {
                s.GravFlipped = !s.GravFlipped;
                s.GravMul = s.GravFlipped ? -1 : 1;
            }

            int mode = DashOrbMode(sid);
            s.Dashing = mode;

            switch (mode)
            {
                case 1: // Horizontal dash
                    s.VelY_fixed = 0;
                    break;
                case 2: // 45° up
                    s.VelY_fixed = -s.VelX_fixed;
                    break;
                case 3: // 45° down
                    s.VelY_fixed = s.VelX_fixed;
                    break;
                case 4: // Straight up
                    s.VelY_fixed = s.VelX_fixed * 4;
                    break;
                case 5: // Straight down
                    s.VelY_fixed = -s.VelX_fixed * 4;
                    break;
            }
            s.OnGround = false;
        }

        /// <summary>
        /// Apply spider teleport (used by spider orbs, spider pads, and spider mode tap).
        /// goUp=true: flip to inverted gravity, scan upward for ceiling.
        /// goUp=false: flip to normal gravity, scan downward for floor.
        /// </summary>
        private void ApplySpiderTeleport(ref SimState s, bool goUp)
        {
#if !DISABLE_DEBUG_LOGGING
            PfLog($"[SPIDER_TELEPORT] goUp={goUp} from Y={s.Y_fixed >> 8}");
#endif
            int playerX_px = (s.X_fixed >> 8) + 1;
            int playerY_px = s.Y_fixed >> 8;
            int hbW = GetHitboxW(s.Mini);
            int hbH = GetHitboxH(s.Mini);
            int hbOffY = s.Mini ? ((0x10 - hbH) >> 1) : 0;

            if (goUp)
            {
                // Pre-eject from floor
                var (collPre, ejectPre) = BgCollD_Spider(playerX_px, playerY_px + hbOffY, hbW, hbH);
                if (collPre)
                    s.Y_fixed -= (ejectPre << 8);
                s.VelY_fixed = 0;

                // Flip to inverted gravity
                s.GravFlipped = true;
                s.GravMul = -1;

                // Scan upward for ceiling (positions player at ceiling surface)
                SpiderScanUp(ref s);
                s.VelY_fixed = 0;
            }
            else
            {
                // Pre-eject from ceiling
                var (collPre, ejectPre) = BgCollU_Spider(playerX_px, playerY_px, hbW, hbH);
                if (collPre)
                    s.Y_fixed += ((ejectPre + 1) << 8);
                s.VelY_fixed = 0;

                // Flip to normal gravity
                s.GravFlipped = false;
                s.GravMul = 1;

                // Scan downward for floor (positions player at floor surface)
                SpiderScanDown(ref s);
                s.VelY_fixed = 0;
            }
            s.Orbed = true;
            s.SlopeFrames = 0;
            s.SlopeWasOnCounter = 0;

            // NES spider_up_wait/spider_down_wait call process_y_scroll in a
            // loop during the scan.  Since spider is NOT in the follow-Y mode
            // list, the smooth-scroll branch runs many times, effectively
            // snapping the camera to the new player Y.  Simulate this by
            // applying cube-style instant camera follow at the final position.
            SnapCameraToPlayerY(ref s);

#if !DISABLE_DEBUG_LOGGING
            PfLog($"[SPIDER_TELEPORT] result Y={s.Y_fixed >> 8} gravFlipped={s.GravFlipped} camY={s.CameraY_fixed >> 8}");
#endif
        }

        /// <summary>
        /// Snap camera Y to match the player's current Y using cube-style
        /// threshold logic (top=0x4000, bottom=0xA0).  Used after spider
        /// teleport to simulate NES's process_y_scroll loop during the scan.
        /// </summary>
        private void SnapCameraToPlayerY(ref SimState s)
        {
            int minCamY = -(groundRowsToReserve * TILE) << 8;
            int maxCamY = Math.Max(0, (mapHeight - NES_H) * TILE) << 8;
            int screenY = s.Y_fixed - s.CameraY_fixed;
            if (screenY < 0x4000)
            {
                s.CameraY_fixed -= (0x4000 - screenY);
                if (s.CameraY_fixed < minCamY) s.CameraY_fixed = minCamY;
            }
            else if ((screenY >> 8) >= 0xA0)
            {
                s.CameraY_fixed += (screenY - 0xA000);
                if (s.CameraY_fixed > maxCamY) s.CameraY_fixed = maxCamY;
            }
        }

        /// <summary>
        /// Apply teleport portal: find exit portal and teleport Y position.
        /// PF doesn't track camera, so we use a simplified approach:
        /// find the nearest visible exit portal ahead of the entrance.
        /// </summary>
        private void ApplyTeleportPortal(ref SimState s, SpriteEntry entrance, int entranceSid, int currentX_px)
        {
            bool isVertical = IsVerticalTeleportEntrance(entranceSid);

            // Calculate approximate camera bounds based on player X
            // NES screen is 16 tiles wide; center on player
            int cameraLeft = currentX_px - 8 * TILE;
            int cameraRight = currentX_px + 8 * TILE;

            int bestExitY = -1;
            bool foundExit = false;

            foreach (var sp in allSprites)
            {
                int sid = sp.SpriteId;
                if (!IsTeleportPortalExit(sid)) continue;

                // Match exit type to entrance type
                bool validExit = false;
                if (isVertical && sid == 0x4F) validExit = true;
                else if (!isVertical)
                {
                    if (IsBottomRowTeleportExit(sid) || sid == 0x69 || sid == 0x78) validExit = true;
                }
                if (!validExit) continue;

                // Check if exit is roughly on screen (PF approximation)
                int exitX = sp.AnchorX_px;
                if (exitX >= cameraLeft && exitX <= cameraRight)
                {
                    if (isVertical)
                    {
                        // Vertical: exit at middle tile of 3-tile-tall portal
                        // +1 compensates for the NES -1 sprite offset baked into HitTop;
                        // teleport destination is a position, not a collision probe.
                        bestExitY = sp.HitTop + TILE + 1;
                    }
                    else
                    {
                        bestExitY = sp.HitTop + 1;
                    }
                    foundExit = true;
                    // Last visible exit wins (matching SIM)
                }
            }

            if (foundExit)
            {
                // Apply ground offset (3 tiles reserved at bottom)
                // Note: hitTop already has groundRowsToReserve subtracted in SpriteEntry construction
                s.Y_fixed = bestExitY << 8;
#if !DISABLE_DEBUG_LOGGING
                PfLog($"[TELEPORT_PORTAL] Teleported to Y={bestExitY}");
#endif
            }
        }

        // -------------------------------------------------------------------
        //  COLLISION DETECTION (delegates to SharedPhysics)
        // -------------------------------------------------------------------

        private MetatileCollision GetTileCollision(int tileX, int tileY)
            => SharedPhysics.GetTileCollision(_collisionMap, tileX, tileY);

        private static (int left, int top, int right, int bottom) GetCollisionBounds(MetatileCollision col)
            => SharedPhysics.GetCollisionBounds(col);

        private static bool ProvidesFloorAtColumn(MetatileCollision col, int localX)
            => SharedPhysics.ProvidesFloorAtColumn(col, localX);

        private static bool ProvidesFloorAtColumn(MetatileCollision col, int localX, out int topOffsetPx)
            => SharedPhysics.ProvidesFloorAtColumn(col, localX, out topOffsetPx);

        private static bool IsSlopeTile(MetatileCollision col)
            => SharedPhysics.IsSlopeTile(col);

        private static bool IsMiniBlockType(MetatileCollision col)
            => SharedPhysics.IsMiniBlockType(col);

        private static bool IsMiniBlockFloorHit(MetatileCollision col, int localX, int localY)
            => SharedPhysics.IsMiniBlockFloorHit(col, localX, localY);

        private static int GetMiniBlockFloorSurface(MetatileCollision col)
            => SharedPhysics.GetMiniBlockFloorSurface(col);

        private static bool TileOccupiesPixel(MetatileCollision col, int localX, int localY)
            => SharedPhysics.TileOccupiesPixel(col, localX, localY);

        private static int MapTileForCollision(int tid)
            => SharedPhysics.MapTileForCollision(tid);

        // -------------------------------------------------------------------
        //  FLOOR / CEILING COLLISION (delegates to SharedPhysics)
        // -------------------------------------------------------------------

        private (bool hit, int surfaceY, bool spikeDeath) CheckFloor(int collX, int collY, int collW, int collH)
            => SharedPhysics.CheckFloor(_collisionMap, collX, collY, collW, collH);

        private (bool hit, int ceilingBottomY, bool spikeDeath) CheckCeiling(int collX, int collY, int collW, int collH)
            => SharedPhysics.CheckCeiling(_collisionMap, collX, collY, collW, collH);

        // -------------------------------------------------------------------
        //  DEATH CHECKS (delegates to SharedPhysics, with PF debug logging)
        // -------------------------------------------------------------------

        private bool CheckCenterPointDeath(ref SimState s)
        {
            int px = s.X_fixed >> 8, py = s.Y_fixed >> 8;
            return SharedPhysics.CheckCenterPointDeath(_collisionMap, px, py,
                GetHitboxW(s.Mini), GetHitboxH(s.Mini),
                GetHitboxOffsetY(s.GameMode, s.Mini, s.GravFlipped));
        }

        private bool PointKillsPlayer(int px, int py
#if !DISABLE_DEBUG_LOGGING
            , out int dbg_tid, out int dbg_mappedTid, out MetatileCollision dbg_col, out int dbg_localX, out int dbg_localY
#endif
        )
        {
#if !DISABLE_DEBUG_LOGGING
            return SharedPhysics.PointKillsPlayer(_collisionMap, px, py,
                out dbg_tid, out dbg_mappedTid, out dbg_col, out dbg_localX, out dbg_localY);
#else
            return SharedPhysics.PointKillsPlayer(_collisionMap, px, py);
#endif
        }

        /// <summary>
        /// 4-corner spike check matching NES bg_coll_floor_spikes.
        /// Checks 4 inset corners of the hitbox for ALL spike types via TileKillsAtPixel.
        /// </summary>
        private bool CheckFloorSpikes(ref SimState s)
        {
            int playerX = s.X_fixed >> 8;
            int playerY = s.Y_fixed >> 8;
            int hbW = GetHitboxW(s.Mini);
            int hbH = GetHitboxH(s.Mini);

#if !DISABLE_DEBUG_LOGGING
            bool killed = SharedPhysics.CheckFloorSpikes(in _collisionMap, playerX, playerY, hbW, hbH, s.Mini,
                out int deathX, out int deathY,
                out string cornerName, out int _tid, out int _mtid,
                out MetatileCollision _col, out int _lx, out int _ly);

            if (killed)
            {
                PfLog($"[FLOOR_SPIKE_DEATH] corner {cornerName} ({deathX},{deathY}) tid=0x{_tid:X2} mapped=0x{_mtid:X2} col={_col} localXY=({_lx},{_ly})");
                _lastDeathReason = $"FLOOR_SPIKE:{cornerName}({deathX},{deathY})";
                _lastDeathX = playerX; _lastDeathY = playerY;
                System.Console.WriteLine($"[DBG_SPIKE_DETAIL] pX={playerX} pY={playerY} corner={cornerName} deathPt=({deathX},{deathY}) tid=0x{_tid:X2} mapped=0x{_mtid:X2} col={_col} local=({_lx},{_ly})");
            }
            return killed;
#else
            return SharedPhysics.CheckFloorSpikes(in _collisionMap, playerX, playerY, hbW, hbH, s.Mini,
                out _, out _);
#endif
        }

        /// <summary>
        /// CheckDeathCollision � center-point death check matching NES bg_coll_death.
        /// </summary>
        private bool CheckDeathCollision(ref SimState s)
        {
            int playerX_px = s.X_fixed >> 8;
            int playerY_px = s.Y_fixed >> 8;
            int hbW = GetHitboxW(s.Mini);
            int hbH = GetHitboxH(s.Mini);
            int hbOffY = GetHitboxOffsetY(s.GameMode, s.Mini, s.GravFlipped);

            bool result = SharedPhysics.CheckDeathCollision(_collisionMap,
                playerX_px, playerY_px, hbW, hbH, hbOffY, s.GameMode);
#if !DISABLE_DEBUG_LOGGING
            if (result)
            {
                int centerX = playerX_px + (hbW >> 1) - 1;
                int centerY = playerY_px + (hbH / 2) + hbOffY;
                int tileX = centerX / TILE;
                int tileY = centerY / TILE;
                int tileArrayY = tileY + _collisionMap.GroundRowsToReserve;
                int tid = 0;
                if (tileX >= 0 && tileX < _collisionMap.MapWidth &&
                    tileArrayY >= 0 && tileArrayY < _collisionMap.MapHeight)
                {
                    int tileIdx = tileArrayY * _collisionMap.MapWidth + tileX;
                    if (tileIdx >= 0 && tileIdx < _collisionMap.Tiles.Length)
                        tid = _collisionMap.Tiles[tileIdx];
                }
                var col = MetatileCollisionTable.GetCollision((byte)MapTileForCollision(tid));
                PfLog($"[DEATH_POINT] center ({centerX},{centerY}) tile=({tileX},{tileArrayY}) tid=0x{tid:X2} col={col}");
                if (_speculativeDepth == 0)
                {
                    _lastDeathReason = $"DEATH_COLL:tid=0x{tid:X2}col={col}";
                    _lastDeathX = playerX_px; _lastDeathY = playerY_px;
                }
            }
#endif
            return result;
        }

        /// <summary>
        /// Slope penetration death check — matches NES bg_coll_death's bg_coll_slope() call.
        /// NES bg_coll_death checks if the center pixel is inside a slope surface.
        /// SharedPhysics.CheckDeathCollision only checks death/spike tiles via TileKillsAtPixel,
        /// missing the slope check. This method fills that gap so the PF correctly prunes
        /// BFS paths that pass through slope terrain.
        /// </summary>
        private bool CheckSlopePenetrationDeath(ref SimState s)
        {
            int hbW = GetHitboxW(s.Mini);
            int hbH = GetHitboxH(s.Mini);
            int hbOffY = GetHitboxOffsetY(s.GameMode, s.Mini, s.GravFlipped);

            // NES bg_coll_death center pixel: Generic.x + (width>>1)-1, Generic.y + (height>>1) + miniOffset
            int centerX = (s.X_fixed >> 8) + (hbW >> 1) - 1;
            int centerY = (s.Y_fixed >> 8) + (hbH / 2) + hbOffY;

            int tileX = centerX / TILE;
            int tileY = centerY / TILE;
            var collision = GetTileCollision(tileX, tileY);

            // Check if tile is a slope type (use full range including LU66_TOP)
            if (collision >= MetatileCollision.COL_SLOPE_RD45 && collision <= MetatileCollision.COL_SLOPE_LU66_TOP)
            {
                var (hit, _, _) = PfSlopeCalc(centerX, centerY, collision);
                if (hit)
                {
#if !DISABLE_DEBUG_LOGGING
                    PfLog($"[SLOPE_DEATH] center ({centerX},{centerY}) inside slope {collision}");
#endif
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Forward collision check � right edge middle pixel for solid/spike collision.
        /// </summary>
        private bool CheckForwardCollision(ref SimState s)
        {
            // NES bg_side_coll_common: skip entirely when slope counters are active
            // (matches ShouldSkipSideCollisionForSlope in the SIM)
            if ((s.SlopeWasOnCounter | s.SlopeFrames) != 0)
                return false;

            int playerX_px = s.X_fixed >> 8;
            int playerY_px = s.Y_fixed >> 8;
            int hbW = GetHitboxW(s.Mini);
            int hbH = GetHitboxH(s.Mini);
            int hbOffY = GetHitboxOffsetY(s.GameMode, s.Mini, s.GravFlipped);

            bool result = SharedPhysics.CheckForwardCollision(_collisionMap,
                playerX_px, playerY_px, hbW, hbH, hbOffY,
                s.GameMode, s.Mini, s.GravFlipped, skipSlopeCheck: true);

            // BFS override: COL_FLOOR_CEIL and COL_NO_SIDE tiles don't block
            // sideways in the NES (bg_coll_sides returns 0).  SharedPhysics
            // correctly returns false for these.  However, treating them as
            // non-blocking in BFS creates a much larger state space that
            // overwhelms the frontier cap, causing coin-path states to be
            // lost during dedup/pruning.  Re-check these tiles here so the
            // BFS prunes states at floor/ceiling tiles (the winning path
            // never actually collides with these tiles sideways, so the
            // pruned states are on suboptimal trajectories anyway).
            if (!result && _speculativeDepth > 0)
            {
                int rightEdge_px = playerX_px + hbW;
                int centerY_px;
                if (s.Mini)
                {
                    int miniTopOffset = (0x10 - hbH) >> 1;
                    centerY_px = playerY_px + miniTopOffset + (hbH >> 1);
                    if (s.GameMode == 0 || s.GameMode == 4 || s.GameMode == 8)
                        centerY_px += s.GravFlipped ? 3 : -2;
                }
                else
                    centerY_px = playerY_px + (hbH >> 1);

                int tileX = rightEdge_px / TILE;
                int tileY = centerY_px / TILE;
                int tileArrY = tileY + _collisionMap.GroundRowsToReserve;
                if (tileX >= 0 && tileX < _collisionMap.MapWidth &&
                    tileArrY >= 0 && tileArrY < _collisionMap.MapHeight)
                {
                    int tid = _collisionMap.Tiles[tileArrY * _collisionMap.MapWidth + tileX];
                    int mapped = MapTileForCollision(tid);
                    var col = MetatileCollisionTable.GetCollision((byte)mapped);
                    if (col == MetatileCollision.COL_FLOOR_CEIL || col == MetatileCollision.COL_NO_SIDE)
                    {
                        int twX = tileX * TILE, twY = tileY * TILE;
                        int lx = Math.Max(0, Math.Min(15, rightEdge_px - twX));
                        int ly = Math.Max(0, Math.Min(15, centerY_px - twY));
                        if (SharedPhysics.TileOccupiesPixel(col, lx, ly))
                            result = true;
                    }
                }
            }

            // Targeted FWD diagnostic for gap-range states near collapse
#if !DISABLE_DEBUG_LOGGING
            if (result && _frameCounter >= 2020 && _frameCounter <= 2042 && playerY_px >= 288 && playerY_px <= 350)
            {
                int dbgRightEdge = playerX_px + hbW;
                int dbgCenterY = s.Mini ? playerY_px + ((0x10 - hbH) >> 1) + (hbH >> 1) : playerY_px + (hbH >> 1);
                int dbgTileX = dbgRightEdge / TILE;
                int dbgTileY = dbgCenterY / TILE;
                int dbgTileArrayY = dbgTileY + _collisionMap.GroundRowsToReserve;
                int dbgTid = -1;
                if (dbgTileX >= 0 && dbgTileX < _collisionMap.MapWidth && dbgTileArrayY >= 0 && dbgTileArrayY < _collisionMap.MapHeight)
                {
                    int dbgIdx = dbgTileArrayY * _collisionMap.MapWidth + dbgTileX;
                    if (dbgIdx >= 0 && dbgIdx < _collisionMap.Tiles.Length) dbgTid = _collisionMap.Tiles[dbgIdx];
                }
                int dbgMapped = MapTileForCollision(dbgTid);
                var dbgColl = MetatileCollisionTable.GetCollision((byte)dbgMapped);
                Console.Error.WriteLine($"[FWD_GAP] f={_frameCounter} X={playerX_px} Y={playerY_px} rightEdge={dbgRightEdge} centerY={dbgCenterY} tile=({dbgTileX},{dbgTileY}) arrY={dbgTileArrayY} tid=0x{dbgTid:X2} mapped=0x{dbgMapped:X2} col={dbgColl} mode={s.GameMode} mini={s.Mini} grav={s.GravFlipped}");
            }
            if (_frameCounter >= 2020 && _frameCounter <= 2042 && !result && playerY_px >= 300 && playerY_px <= 335)
            {
                // Log gap-range states that SURVIVE forward collision (to confirm they exist)
                Console.Error.WriteLine($"[FWD_SURV] f={_frameCounter} X={playerX_px} Y={playerY_px} mode={s.GameMode}");
            }
#endif

#if !DISABLE_DEBUG_LOGGING
            // Diagnostic: always log forward check near the problem area
            if (playerX_px >= 16380 && playerX_px <= 16400 && s.GameMode == 1 && s.Mini)
            {
                int dbgRightEdge = playerX_px + hbW;
                int dbgCenterY;
                if (s.Mini) { int mto = (0x10 - hbH) >> 1; dbgCenterY = playerY_px + mto + (hbH >> 1); } else { dbgCenterY = playerY_px + (hbH >> 1); }
                int dbgTileX = dbgRightEdge / TILE;
                int dbgTileY = dbgCenterY / TILE;
                int dbgTileArrayY = dbgTileY + _collisionMap.GroundRowsToReserve;
                int dbgTid = -1;
                if (dbgTileX >= 0 && dbgTileX < _collisionMap.MapWidth && dbgTileArrayY >= 0 && dbgTileArrayY < _collisionMap.MapHeight)
                {
                    int dbgIdx = dbgTileArrayY * _collisionMap.MapWidth + dbgTileX;
                    if (dbgIdx >= 0 && dbgIdx < _collisionMap.Tiles.Length) dbgTid = _collisionMap.Tiles[dbgIdx];
                }
                PfLog($"[FWD_DIAG] X={playerX_px} Y={playerY_px} rightEdge={dbgRightEdge} centerY={dbgCenterY} tile=({dbgTileX},{dbgTileY}) tid=0x{dbgTid:X2} result={result} slopeSkip={s.SlopeWasOnCounter}|{s.SlopeFrames}");
            }
            if (result)
            {
                int rightEdge_px = playerX_px + hbW;
                int centerY_px;
                if (s.Mini)
                {
                    int miniTopOffset = (0x10 - hbH) >> 1;
                    centerY_px = playerY_px + miniTopOffset + (hbH >> 1);
                    if (s.GameMode == 0 || s.GameMode == 4 || s.GameMode == 8 || s.GameMode == 11)
                        centerY_px += s.GravFlipped ? 3 : -2;
                }
                else
                {
                    centerY_px = playerY_px + (hbH >> 1);
                }
                int tileX = rightEdge_px / TILE;
                int tileY = centerY_px / TILE;
                int tileArrayY = tileY + _collisionMap.GroundRowsToReserve;
                int tileId = 0;
                if (tileX >= 0 && tileX < _collisionMap.MapWidth &&
                    tileArrayY >= 0 && tileArrayY < _collisionMap.MapHeight)
                {
                    int tileIdx = tileArrayY * _collisionMap.MapWidth + tileX;
                    if (tileIdx >= 0 && tileIdx < _collisionMap.Tiles.Length)
                        tileId = _collisionMap.Tiles[tileIdx];
                }
                var collision = MetatileCollisionTable.GetCollision((byte)MapTileForCollision(tileId));
                int tileWorldX = tileX * TILE;
                int tileWorldY = tileY * TILE;
                int localX = Math.Max(0, Math.Min(TILE - 1, rightEdge_px - tileWorldX));
                int localY = Math.Max(0, Math.Min(TILE - 1, centerY_px - tileWorldY));
                if (TileOccupiesPixel(collision, localX, localY))
                    PfLog($"[FWD_COLL] probe=({rightEdge_px},{centerY_px}) tile=({tileX},{tileY}) tid=0x{tileId:X2} col={collision} oldX={playerX_px} newX={(s.X_fixed + s.VelX_fixed) >> 8}");
                else
                    PfLog($"[FWD_SPIKE] probe=({rightEdge_px},{centerY_px}) tile=({tileX},{tileY}) tid=0x{tileId:X2} col={collision} local=({localX},{localY})");
            }
#endif
            return result;
        }

        // -------------------------------------------------------------------
        //  SLOPE COLLISION — mirrors SIM bg_coll_D_slopes() / bg_coll_slope()
        //  Checks two hitbox edges (left and right) at Y+hbH-2 for slope tiles,
        //  computes pixel-perfect ejection amount matching the NES slope math.
        // -------------------------------------------------------------------

        // Slope direction flags (matching SIM SlopeCollision.partial.cs)
        private const int PF_SLOPE_RISING = 0b0100;

        /// <summary>
        /// Per-tile slope surface calculation — mirrors SIM bg_coll_slope().
        /// Given a pixel coordinate (temp_x, temp_y) that lands on a slope tile,
        /// returns (hit, ejectionAmount, slopeType).
        /// ejectionAmount = how many pixels below the slope surface the point is.
        /// </summary>
        private static (bool hit, int ejection, int slopeType) PfSlopeCalc(int temp_x, int temp_y, MetatileCollision collision)
            => SharedPhysics.SlopeCalc(temp_x, temp_y, collision);

        /// <summary>
        /// UpdateSlopeCounters — pre-eject counter decrement (matches SIM UpdateSlopeCounters).
        /// Called at the TOP of each eject function, before bg_coll_D_slopes.
        /// </summary>
        /// <summary>
        /// decrement_was_on_slope() — called at TOP of eject, BEFORE slope probes.
        /// Matches NES: only decrements SlopeWasOnCounter (NOT SlopeFrames).
        /// When counter reaches 0: applies EXIT_SLOPE velocity tables, clears SlopeType.
        /// SlopeFrames is handled after eject by PfUpdateSlopeCounters_Fresh.
        /// </summary>
        private static readonly short[] PF_EXIT_SLOPE_BALL_22 = SharedPhysics.EXIT_SLOPE_BALL_22;
        private static readonly short[] PF_EXIT_SLOPE_BALL_66 = SharedPhysics.EXIT_SLOPE_BALL_66;
        private static readonly short[] PF_EXIT_SLOPE_CUBE_22 = SharedPhysics.EXIT_SLOPE_CUBE_22;
        private static void PfUpdateSlopeCounters(ref SimState s)
        {
            SharedPhysics.UpdateSlopeCounters(ref s.SlopeWasOnCounter, ref s.SlopeType,
                ref s.VelY_fixed, s.GameMode, s.GravFlipped, s.Mini, ref s.LastSlopeType);
        }

        /// <summary>
        /// UpdateSlopeCounters_Fresh — post-eject counter decrement + apply_slope_vel
        /// (matches SIM UpdateSlopeCounters_Fresh, called in ProcessCubePhysics Step 5).
        /// When slope_frames decrements to 0 and slope_type is set, fires PfApplySlopeVelocity.
        /// When was_on_slope_counter decrements to 0, clears slope_type.
        /// </summary>
        /// <summary>
        /// x_movement_coll slope_frames handling — called AFTER eject.
        /// Matches NES: only decrements SlopeFrames (NOT SlopeWasOnCounter).
        /// When SlopeFrames was >0 and SlopeType is set: fires PfApplySlopeVelocity.
        /// SlopeWasOnCounter is handled before eject by PfUpdateSlopeCounters.
        /// </summary>
        private static void PfUpdateSlopeCounters_Fresh(ref SimState s)
        {
            SharedPhysics.UpdateSlopeCountersFresh(ref s.SlopeFrames, s.SlopeType,
                ref s.VelY_fixed, s.VelX_fixed);
        }

        /// <summary>
        /// Apply slope exit velocity matching SIM's apply_slope_vel().
        /// Called when slope_frames reaches 0 and slope_type is still set
        /// (via PfUpdateSlopeCounters_Fresh).
        /// </summary>
        private static void PfApplySlopeVelocity(ref SimState s, int slopeType)
        {
            SharedPhysics.ApplySlopeVelocity(ref s.VelY_fixed, slopeType, s.VelX_fixed);
        }

        /// <summary>
        /// NES slope_jump_check: when make_cube_jump_higher is set and slope is not 22°,
        /// add a velocity bonus to the jump. Matches SIM SlopeJumpCheck_Fresh().
        /// </summary>
        private static void PfSlopeJumpCheck(ref SimState s)
        {
            SharedPhysics.SlopeJumpCheck(ref s.VelY_fixed, ref s.SlopeJumpHigher, s.SlopeType, s.Mini, s.GravFlipped);
        }

        private const int PF_SLOPE_UD = 0b1000;

        private (bool hit, int ejection, int slopeType) PfCheckSlopes(ref SimState s, bool input)
        {
            int playerX_px = s.X_fixed >> 8;
            int hbW = GetHitboxW(s.Mini);
            int hbH = GetHitboxH(s.Mini);
            int slopeHbOffY = s.Mini ? ((0x10 - hbH) >> 1) : 0;
            int checkBaseY = (s.Y_fixed >> 8) + slopeHbOffY + hbH - 2;
            return SharedPhysics.CheckSlopesDown(in _collisionMap,
                playerX_px, playerX_px, checkBaseY, hbW,
                input, s.GameMode, s.GravFlipped, s.VelX_fixed,
                ref s.LastSlopeType, ref s.SlopeJumpHigher);
        }

        /// <summary>
        /// Mirrors SIM bg_coll_U_slopes() — checks two hitbox edges for ceiling slope tiles.
        /// </summary>
        private (bool hit, int ejection, int slopeType) PfCheckSlopesUp(ref SimState s, bool input)
        {
            int playerX_px = s.X_fixed >> 8;
            int hbW = GetHitboxW(s.Mini);
            int hbH = GetHitboxH(s.Mini);
            int slopeHbOffY = s.Mini ? ((0x10 - hbH) >> 1) : 0;
            int checkBaseY = (s.Y_fixed >> 8) + slopeHbOffY + (s.Mini ? 1 : 2) + (s.GameMode == 1 ? 1 : 0);
            return SharedPhysics.CheckSlopesUp(in _collisionMap,
                playerX_px, playerX_px, checkBaseY, hbW,
                input, s.GameMode, s.GravFlipped, s.VelX_fixed,
                ref s.LastSlopeType, ref s.SlopeJumpHigher);
        }
    }
}
