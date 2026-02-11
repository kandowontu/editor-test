using System;
using System.Collections.Generic;
using System.Threading;

namespace FamidashEditor
{
    public partial class SimulatorWindow
    {
        // ===== PATHFINDER CONFIGURATION =====
        private const int PF_LOOKAHEAD_FRAMES = 180;   // How many frames to simulate ahead
        private const int PF_BEAM_WIDTH = 32;           // Number of candidate paths to keep
        private const int PF_REPLAN_THRESHOLD = 30;     // Re-plan when fewer frames remain in current plan
        private const int PF_INPUT_GRANULARITY = 3;     // Evaluate input change every N frames
        private const int PF_DEATH_PENALTY = -1000000;  // Fitness penalty for dying
        private const int PF_PROGRESS_WEIGHT = 100;     // Reward per pixel of forward progress
        private const int PF_ALIVE_BONUS = 50000;       // Bonus for surviving the lookahead
        private const int PF_HEIGHT_PENALTY = 1;        // Penalty per pixel away from vertical center
        private const int PF_VELOCITY_PENALTY = 2;      // Penalty for extreme vertical velocity

        // ===== PATHFINDER STATE =====
        private bool pathfinderEnabled = false;
        private List<bool> pfPlan = new List<bool>();    // Planned inputs: true=press, false=release
        private int pfPlanIndex = 0;                     // Current position in plan
        private int pfPlanStartX = 0;                    // playerX_fixed when plan was generated
        private object pfLock = new object();

        // ===== STATE SNAPSHOT =====
        /// <summary>
        /// Captures all mutable physics state needed for deterministic re-simulation.
        /// </summary>
        private struct PFState
        {
            // Position & velocity
            public int playerX_fixed;
            public int playerY_fixed;
            public int playerVelY_fixed;
            public int playerVelX_fixed;
            public int cameraX_fixed;
            public int cameraY_fixed;

            // Mode & configuration
            public int currentGameMode;
            public int currentSpeed_fixed;
            public int speed;
            public bool miniMode;
            public bool gravityFlipped;
            public bool gravityReversed;
            public double gravityMultiplier;
            public byte currplayer_gravity;
            public byte currplayer_mini;
            public int currplayer_table_idx;
            public int currplayer;
            public bool dual;
            public bool twoplayer;

            // Physics flags
            public bool onGround;
            public bool wasZeroedByCollisionLastFrame;
            public bool physicsEnabled;
            public bool deathTriggered;
            public bool paused;
            public bool levelCompleteTriggered;
            public int velocityY;
            public int velocityX;
            public bool previousJumpState;
            public bool effectiveInvertedByW;
            public double simTimeScale;
            public bool isFullSpeed;
            public int tabSpeedMultiplier;

            // Counters
            public int groundStabilizeCounter;
            public int invertedCeilingHoldCounter;
            public int jumpBufferCounter;
            public int pogoBounceAnimationCounter;
            public int ballFlipCooldown;
            public bool ballWasGroundedBeforeFlip;
            public int ballAnimationFrameCounter;

            // Game mode state
            public bool[] ballSwitched;
            public bool ballGoingDown;
            public bool ufoOrbed;
            public bool[] orbed;
            public bool blackOrbed;
            public bool hblocked;
            public bool jblocked;
            public bool dblocked;
            public bool fblocked;
            public int ninjaJumps;
            public bool ninjaJumpedThisFrame;
            public bool robotJumpPressed;
            public bool swingSwitched;
            public int[] ninjajumps;
            public int[] robotJumpTime;
            public int[] robotJumpFrame;
            public int[] chargepower;
            public int[] dashing;
            public int footballChargeFrames;
            public bool footballOrbed;
            public bool footballWasHeld;

            // Orb/pad buffer state
            public bool[] orbBufferActive;
            public bool[] orbActivationConsumedThisPress;
            public bool[] orbHoldConsumed;
            public bool[] orbHoldConsumedKeyStillDown;
            public bool[] orbHoldSuppressing;
            public bool[] orbhitonthisframe;
            public bool keyXHeldStartedOnGround;
            public Dictionary<int, bool> orbActivated;
            public Dictionary<int, bool> bluePadActivated;

            // Dual mode arrays
            public int[] player_x_fixed;
            public int[] player_y_fixed;
            public int[] player_vel_y_fixed;
            public bool[] player_mini;
            public byte[] player_gravity;

            // Slope state
            public int[] slope_type_arr;
            public int[] slope_frames_arr;
            public int[] was_on_slope_counter_arr;
            public int[] last_slope_type_arr;
            public bool make_cube_jump_higher;

            // Input state
            public int keyXPressedCount;
            public bool keyXHeld;
            public bool prevKeyXDown;
            public bool upHeld;
            public bool downHeld;
            public int ballToggleRequested;
            public int keyXPressStartedOnGroundInt;
            public int keyXHeldStartedOnGroundInt;
            public bool jumpedOnce;

            // Portal dedup sets
            public HashSet<int> processedGravityPortals;
            public HashSet<int> processedGravityModPortals;
            public HashSet<int> processedMiniPortals;
            public HashSet<int> processedRandomPortals;
            public HashSet<int> processedSpeedPortals;
            public HashSet<int> processedOrbs;
            public HashSet<int> processedColorTriggers;
            public HashSet<int> processedEndLevelTriggers;
            public HashSet<int> processedTeleportPortals;

            // Rotation (cosmetic but affects state)
            public int cubeRotate_fixed;
            public int cubeRotateMini_fixed;
            public int shipRotate_fixed;
            public int swingcopterRotate_fixed;
            public int footballRotate_fixed;

            // Temp physics
            public int tmpgravity;
            public int tmpfallspeed;
        }

        /// <summary>
        /// Captures current simulator state into a PFState snapshot.
        /// </summary>
        private PFState PF_CaptureState()
        {
            var s = new PFState();

            // Position & velocity
            s.playerX_fixed = playerX_fixed;
            s.playerY_fixed = playerY_fixed;
            s.playerVelY_fixed = playerVelY_fixed;
            s.playerVelX_fixed = playerVelX_fixed;
            s.cameraX_fixed = cameraX_fixed;
            s.cameraY_fixed = cameraY_fixed;

            // Mode & configuration
            s.currentGameMode = currentGameMode;
            s.currentSpeed_fixed = currentSpeed_fixed;
            s.speed = speed;
            s.miniMode = miniMode;
            s.gravityFlipped = gravityFlipped;
            s.gravityReversed = gravityReversed;
            s.gravityMultiplier = gravityMultiplier;
            s.currplayer_gravity = currplayer_gravity;
            s.currplayer_mini = currplayer_mini;
            s.currplayer_table_idx = currplayer_table_idx;
            s.currplayer = currplayer;
            s.dual = dual;
            s.twoplayer = twoplayer;

            // Physics flags
            s.onGround = onGround;
            s.wasZeroedByCollisionLastFrame = wasZeroedByCollisionLastFrame;
            s.physicsEnabled = physicsEnabled;
            s.deathTriggered = deathTriggered;
            s.paused = paused;
            s.levelCompleteTriggered = levelCompleteTriggered;
            s.velocityY = velocityY;
            s.velocityX = velocityX;
            s.previousJumpState = previousJumpState;
            s.effectiveInvertedByW = effectiveInvertedByW;
            s.simTimeScale = simTimeScale;
            s.isFullSpeed = isFullSpeed;
            s.tabSpeedMultiplier = tabSpeedMultiplier;

            // Counters
            s.groundStabilizeCounter = groundStabilizeCounter;
            s.invertedCeilingHoldCounter = invertedCeilingHoldCounter;
            s.jumpBufferCounter = jumpBufferCounter;
            s.pogoBounceAnimationCounter = pogoBounceAnimationCounter;
            s.ballFlipCooldown = ballFlipCooldown;
            s.ballWasGroundedBeforeFlip = ballWasGroundedBeforeFlip;
            s.ballAnimationFrameCounter = ballAnimationFrameCounter;

            // Game mode state (clone arrays)
            s.ballSwitched = (bool[])ballSwitched.Clone();
            s.ballGoingDown = ballGoingDown;
            s.ufoOrbed = ufoOrbed;
            s.orbed = (bool[])orbed.Clone();
            s.blackOrbed = blackOrbed;
            s.hblocked = hblocked;
            s.jblocked = jblocked;
            s.dblocked = dblocked;
            s.fblocked = fblocked;
            s.ninjaJumps = ninjaJumps;
            s.ninjaJumpedThisFrame = ninjaJumpedThisFrame;
            s.robotJumpPressed = robotJumpPressed;
            s.swingSwitched = swingSwitched;
            s.ninjajumps = (int[])ninjajumps.Clone();
            s.robotJumpTime = (int[])robotJumpTime.Clone();
            s.robotJumpFrame = (int[])robotJumpFrame.Clone();
            s.chargepower = (int[])chargepower.Clone();
            s.dashing = (int[])dashing.Clone();
            s.footballChargeFrames = footballChargeFrames;
            s.footballOrbed = footballOrbed;
            s.footballWasHeld = footballWasHeld;

            // Orb/pad buffer state (clone arrays + dicts)
            s.orbBufferActive = (bool[])orbBufferActive.Clone();
            s.orbActivationConsumedThisPress = (bool[])orbActivationConsumedThisPress.Clone();
            s.orbHoldConsumed = (bool[])orbHoldConsumed.Clone();
            s.orbHoldConsumedKeyStillDown = (bool[])orbHoldConsumedKeyStillDown.Clone();
            s.orbHoldSuppressing = (bool[])orbHoldSuppressing.Clone();
            s.orbhitonthisframe = (bool[])orbhitonthisframe.Clone();
            s.keyXHeldStartedOnGround = keyXHeldStartedOnGround;
            s.orbActivated = new Dictionary<int, bool>(orbActivated);
            s.bluePadActivated = new Dictionary<int, bool>(bluePadActivated);

            // Dual mode arrays
            s.player_x_fixed = (int[])player_x_fixed.Clone();
            s.player_y_fixed = (int[])player_y_fixed.Clone();
            s.player_vel_y_fixed = (int[])player_vel_y_fixed.Clone();
            s.player_mini = (bool[])player_mini.Clone();
            s.player_gravity = (byte[])player_gravity.Clone();

            // Slope state
            s.slope_type_arr = (int[])slope_type_arr.Clone();
            s.slope_frames_arr = (int[])slope_frames_arr.Clone();
            s.was_on_slope_counter_arr = (int[])was_on_slope_counter_arr.Clone();
            s.last_slope_type_arr = (int[])last_slope_type_arr.Clone();
            s.make_cube_jump_higher = make_cube_jump_higher;

            // Input state
            s.keyXPressedCount = Interlocked.CompareExchange(ref keyXPressedCount, 0, 0);
            s.keyXHeld = keyXHeld;
            s.prevKeyXDown = prevKeyXDown;
            s.upHeld = upHeld;
            s.downHeld = downHeld;
            s.ballToggleRequested = Interlocked.CompareExchange(ref ballToggleRequested, 0, 0);
            s.keyXPressStartedOnGroundInt = Interlocked.CompareExchange(ref keyXPressStartedOnGroundInt, 0, 0);
            s.keyXHeldStartedOnGroundInt = Interlocked.CompareExchange(ref keyXHeldStartedOnGroundInt, 0, 0);
            s.jumpedOnce = jumpedOnce;

            // Portal dedup sets
            s.processedGravityPortals = new HashSet<int>(processedGravityPortals);
            s.processedGravityModPortals = new HashSet<int>(processedGravityModPortals);
            s.processedMiniPortals = new HashSet<int>(processedMiniPortals);
            s.processedRandomPortals = new HashSet<int>(processedRandomPortals);
            s.processedSpeedPortals = new HashSet<int>(processedSpeedPortals);
            s.processedOrbs = new HashSet<int>(processedOrbs);
            s.processedColorTriggers = new HashSet<int>(processedColorTriggers);
            s.processedEndLevelTriggers = new HashSet<int>(processedEndLevelTriggers);
            s.processedTeleportPortals = new HashSet<int>(processedTeleportPortals);

            // Rotation
            s.cubeRotate_fixed = cubeRotate_fixed;
            s.cubeRotateMini_fixed = cubeRotateMini_fixed;
            s.shipRotate_fixed = shipRotate_fixed;
            s.swingcopterRotate_fixed = swingcopterRotate_fixed;
            s.footballRotate_fixed = footballRotate_fixed;

            // Temp physics
            s.tmpgravity = tmpgravity;
            s.tmpfallspeed = tmpfallspeed;

            return s;
        }

        /// <summary>
        /// Restores simulator state from a PFState snapshot.
        /// </summary>
        private void PF_RestoreState(PFState s)
        {
            // Position & velocity
            playerX_fixed = s.playerX_fixed;
            playerY_fixed = s.playerY_fixed;
            playerVelY_fixed = s.playerVelY_fixed;
            playerVelX_fixed = s.playerVelX_fixed;
            cameraX_fixed = s.cameraX_fixed;
            cameraY_fixed = s.cameraY_fixed;

            // Mode & configuration
            currentGameMode = s.currentGameMode;
            currentSpeed_fixed = s.currentSpeed_fixed;
            speed = s.speed;
            miniMode = s.miniMode;
            gravityFlipped = s.gravityFlipped;
            gravityReversed = s.gravityReversed;
            gravityMultiplier = s.gravityMultiplier;
            currplayer_gravity = s.currplayer_gravity;
            currplayer_mini = s.currplayer_mini;
            currplayer_table_idx = s.currplayer_table_idx;
            currplayer = s.currplayer;
            dual = s.dual;
            twoplayer = s.twoplayer;

            // Physics flags
            onGround = s.onGround;
            wasZeroedByCollisionLastFrame = s.wasZeroedByCollisionLastFrame;
            physicsEnabled = s.physicsEnabled;
            deathTriggered = s.deathTriggered;
            paused = s.paused;
            levelCompleteTriggered = s.levelCompleteTriggered;
            velocityY = s.velocityY;
            velocityX = s.velocityX;
            previousJumpState = s.previousJumpState;
            effectiveInvertedByW = s.effectiveInvertedByW;
            simTimeScale = s.simTimeScale;
            isFullSpeed = s.isFullSpeed;
            tabSpeedMultiplier = s.tabSpeedMultiplier;

            // Counters
            groundStabilizeCounter = s.groundStabilizeCounter;
            invertedCeilingHoldCounter = s.invertedCeilingHoldCounter;
            jumpBufferCounter = s.jumpBufferCounter;
            pogoBounceAnimationCounter = s.pogoBounceAnimationCounter;
            ballFlipCooldown = s.ballFlipCooldown;
            ballWasGroundedBeforeFlip = s.ballWasGroundedBeforeFlip;
            ballAnimationFrameCounter = s.ballAnimationFrameCounter;

            // Game mode state (copy arrays back)
            Array.Copy(s.ballSwitched, ballSwitched, 2);
            ballGoingDown = s.ballGoingDown;
            ufoOrbed = s.ufoOrbed;
            Array.Copy(s.orbed, orbed, 2);
            blackOrbed = s.blackOrbed;
            hblocked = s.hblocked;
            jblocked = s.jblocked;
            dblocked = s.dblocked;
            fblocked = s.fblocked;
            ninjaJumps = s.ninjaJumps;
            ninjaJumpedThisFrame = s.ninjaJumpedThisFrame;
            robotJumpPressed = s.robotJumpPressed;
            swingSwitched = s.swingSwitched;
            Array.Copy(s.ninjajumps, ninjajumps, 2);
            Array.Copy(s.robotJumpTime, robotJumpTime, 2);
            Array.Copy(s.robotJumpFrame, robotJumpFrame, 2);
            Array.Copy(s.chargepower, chargepower, 2);
            Array.Copy(s.dashing, dashing, 2);
            footballChargeFrames = s.footballChargeFrames;
            footballOrbed = s.footballOrbed;
            footballWasHeld = s.footballWasHeld;

            // Orb/pad buffer state
            Array.Copy(s.orbBufferActive, orbBufferActive, 2);
            Array.Copy(s.orbActivationConsumedThisPress, orbActivationConsumedThisPress, 2);
            Array.Copy(s.orbHoldConsumed, orbHoldConsumed, 2);
            Array.Copy(s.orbHoldConsumedKeyStillDown, orbHoldConsumedKeyStillDown, 2);
            Array.Copy(s.orbHoldSuppressing, orbHoldSuppressing, 2);
            Array.Copy(s.orbhitonthisframe, orbhitonthisframe, 2);
            keyXHeldStartedOnGround = s.keyXHeldStartedOnGround;
            orbActivated = new Dictionary<int, bool>(s.orbActivated);
            bluePadActivated = new Dictionary<int, bool>(s.bluePadActivated);

            // Dual mode arrays
            Array.Copy(s.player_x_fixed, player_x_fixed, 2);
            Array.Copy(s.player_y_fixed, player_y_fixed, 2);
            Array.Copy(s.player_vel_y_fixed, player_vel_y_fixed, 2);
            Array.Copy(s.player_mini, player_mini, 2);
            Array.Copy(s.player_gravity, player_gravity, 2);

            // Slope state
            Array.Copy(s.slope_type_arr, slope_type_arr, 2);
            Array.Copy(s.slope_frames_arr, slope_frames_arr, 2);
            Array.Copy(s.was_on_slope_counter_arr, was_on_slope_counter_arr, 2);
            Array.Copy(s.last_slope_type_arr, last_slope_type_arr, 2);
            make_cube_jump_higher = s.make_cube_jump_higher;

            // Input state
            Interlocked.Exchange(ref keyXPressedCount, s.keyXPressedCount);
            keyXHeld = s.keyXHeld;
            prevKeyXDown = s.prevKeyXDown;
            upHeld = s.upHeld;
            downHeld = s.downHeld;
            Interlocked.Exchange(ref ballToggleRequested, s.ballToggleRequested);
            Interlocked.Exchange(ref keyXPressStartedOnGroundInt, s.keyXPressStartedOnGroundInt);
            Interlocked.Exchange(ref keyXHeldStartedOnGroundInt, s.keyXHeldStartedOnGroundInt);
            jumpedOnce = s.jumpedOnce;

            // Portal dedup sets
            processedGravityPortals = new HashSet<int>(s.processedGravityPortals);
            processedGravityModPortals = new HashSet<int>(s.processedGravityModPortals);
            processedMiniPortals = new HashSet<int>(s.processedMiniPortals);
            processedRandomPortals = new HashSet<int>(s.processedRandomPortals);
            processedSpeedPortals = new HashSet<int>(s.processedSpeedPortals);
            processedOrbs = new HashSet<int>(s.processedOrbs);
            processedColorTriggers = new HashSet<int>(s.processedColorTriggers);
            processedEndLevelTriggers = new HashSet<int>(s.processedEndLevelTriggers);
            processedTeleportPortals = new HashSet<int>(s.processedTeleportPortals);

            // Rotation
            cubeRotate_fixed = s.cubeRotate_fixed;
            cubeRotateMini_fixed = s.cubeRotateMini_fixed;
            shipRotate_fixed = s.shipRotate_fixed;
            swingcopterRotate_fixed = s.swingcopterRotate_fixed;
            footballRotate_fixed = s.footballRotate_fixed;

            // Temp physics
            tmpgravity = s.tmpgravity;
            tmpfallspeed = s.tmpfallspeed;
        }

        // ===== SIMULATION ENGINE =====

        /// <summary>
        /// Injects pathfinder input into the simulator's input fields.
        /// Called at the start of each frame when pathfinder is active.
        /// </summary>
        private void PF_InjectInput(bool press)
        {
            if (press)
            {
                // Simulate a key press
                Interlocked.Exchange(ref keyXPressedCount, 1);
                keyXHeld = true;
                prevKeyXDown = true;

                // Ball mode needs ballToggleRequested
                if (currentGameMode == 2)
                    Interlocked.Exchange(ref ballToggleRequested, 1);
            }
            else
            {
                // Simulate key released
                Interlocked.Exchange(ref keyXPressedCount, 0);
                keyXHeld = false;
                prevKeyXDown = false;
            }
        }

        /// <summary>
        /// Runs one frame of physics simulation for pathfinder evaluation.
        /// This calls the same physics dispatch as SimulateNumericStep but
        /// without rendering, camera updates, or death UI side effects.
        /// </summary>
        private void PF_SimulateOneFrame()
        {
            if (deathTriggered || levelCompleteTriggered) return;

            int centerOffset_fixed = (TILE / 2) << 8;
            int speedMultiplierLocal = tabSpeedMultiplier;

            isFullSpeed = (simTimeScale == 1.0);

            int attemptedPlayerX_fixed;
            if (isFullSpeed)
                attemptedPlayerX_fixed = playerX_fixed + (currentSpeed_fixed * speedMultiplierLocal);
            else
                attemptedPlayerX_fixed = playerX_fixed + (int)Math.Round((currentSpeed_fixed * speedMultiplierLocal) * simTimeScale);

            int prevPlayerCenter_fixed = playerX_fixed + centerOffset_fixed;
            int attemptedPlayerCenter_fixed = attemptedPlayerX_fixed + centerOffset_fixed;

            // Sprite interactions (same order as SimulateNumericStep)
            if (physicsEnabled)
            {
                try
                {
                    orbhitonthisframe[currplayer] = false;
                    CheckDualPortal();
                    CheckSinglePortal();
                    CheckGravityPortals();
                    CheckTeleportPortals();
                    CheckGravityModPortals();
                    CheckGravityModTriggers(prevPlayerCenter_fixed, attemptedPlayerCenter_fixed);
                    CheckMiniGrowthPortals();
                    CheckPadCollision();
                    CheckSpiderOrbPadCollision();
                    CheckDashOrbCollision();
                    CheckAlphabetBlocks();
                    CheckBluePadCollision();
                }
                catch { }
            }

            // Horizontal advance
            playerX_fixed = attemptedPlayerX_fixed;

            // Ground support check (same as SimulateNumericStep)
            if (onGround && physicsEnabled)
            {
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
                        int hitboxH_local = (currplayer_mini != 0) ? 7 : 15;
                        int hitboxOffsetY_local = (currplayer_mini != 0) ? ((0x10 - hitboxH_local) >> 1) : 0;
                        int playerBottom_px = (playerY_fixed >> 8) + hitboxOffsetY_local + hitboxH_local;
                        int tileIndexY = playerBottom_px / TILE;

                        // Implicit ground layer: treat below-map as solid
                        if (tileIndexY >= mapHeight)
                        {
                            stillSupported = true;
                        }
                        else
                        {
                            for (int testX = playerLeft_px; testX <= playerLeft_px + HITBOX_W_LOCAL; testX += TILE)
                            {
                                int tileIndexX = testX / TILE;
                                if (tileIndexX >= 0 && tileIndexX < mapWidth && tileIndexY >= 0 && tileIndexY < mapHeight)
                                {
                                    int tid = tiles[tileIndexY * mapWidth + tileIndexX];
                                    if (tid > 0)
                                    {
                                        stillSupported = true;
                                        break;
                                    }
                                }
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

            // Physics dispatch (same switch as SimulateNumericStep)
            if (physicsEnabled)
            {
                try
                {
                    currplayer_mini = (byte)(miniMode ? 1 : 0);
                    currplayer_gravity = (byte)(gravityFlipped ? 0xFF : 0);
                    currplayer_table_idx = (currplayer_gravity != 0 ? 1 : 0) | (currplayer_mini != 0 ? 4 : 0);
                    gravityFlipped = (currplayer_gravity != 0);

                    switch (currentGameMode)
                    {
                        case 0: ProcessCubePhysics_Fresh(); break;
                        case 1: ShipPhysics_Fresh(); break;
                        case 2: BallPhysics_Fresh(); break;
                        case 3: UfoPhysics_Fresh(); break;
                        case 4: RobotPhysics_Fresh(); break;
                        case 5: SpiderPhysics_Fresh(); break;
                        case 6: WavePhysics_Fresh(); break;
                        case 7: BallPhysics_Fresh(); break;  // Swingcopter
                        case 8: NinjaPhysics_Fresh(); break;
                        case 9: BallPhysics_Fresh(); break;  // Pogo
                        case 10: SnakePhysics_Fresh(); break;
                        case 11: FootballPhysics_Fresh(); break;
                    }

                    gravityFlipped = (currplayer_gravity != 0);

                    // Forward collision check (death)
                    if (!MainWindow.Option_NoDeath && !hblocked && !deathTriggered)
                    {
                        bool needsForwardCheck = currentGameMode == 0 || currentGameMode == 4 ||
                                                 currentGameMode == 8 || currentGameMode == 10;
                        if (needsForwardCheck)
                        {
                            int hitboxW_fwd = (currplayer_mini != 0) ? 8 : 15;
                            int hitboxH_fwd = (currplayer_mini != 0) ? 7 : 15;
                            int hitboxOffsetY_fwd = (currplayer_mini != 0) ? ((0x10 - hitboxH_fwd) >> 1) : 0;
                            int rightEdgeX = (playerX_fixed >> 8) + hitboxW_fwd;
                            int middleY = (playerY_fixed >> 8) + hitboxOffsetY_fwd + (hitboxH_fwd / 2);
                            int groundRowsToReserve = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                            if (CheckPixelCollision(rightEdgeX, middleY, groundRowsToReserve))
                            {
                                deathTriggered = true;
                            }
                        }
                    }

                    // Death collision
                    if (!deathTriggered && !MainWindow.Option_NoDeath)
                    {
                        if (CheckDeathCollision(out int dx, out int dy))
                            deathTriggered = true;
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// Evaluates the fitness of a state after simulation.
        /// Higher = better. Rewards forward progress, penalizes death and extreme positions.
        /// </summary>
        private int PF_EvaluateFitness(PFState initial, PFState final_state)
        {
            if (final_state.deathTriggered)
                return PF_DEATH_PENALTY;

            if (final_state.levelCompleteTriggered)
                return int.MaxValue / 2; // Level complete = best possible

            int fitness = PF_ALIVE_BONUS;

            // Reward forward progress (X distance in pixels)
            int progressPx = (final_state.playerX_fixed - initial.playerX_fixed) >> 8;
            fitness += progressPx * PF_PROGRESS_WEIGHT;

            // Penalize extreme vertical positions (prefer staying near vertical center of screen)
            int playerY_px = final_state.playerY_fixed >> 8;
            int screenCenterY = (mapHeight * TILE) / 2;
            int yDistance = Math.Abs(playerY_px - screenCenterY);
            fitness -= yDistance * PF_HEIGHT_PENALTY;

            // Penalize extreme vertical velocity (prefer controlled movement)
            int absVelY = Math.Abs(final_state.playerVelY_fixed);
            fitness -= (absVelY >> 8) * PF_VELOCITY_PENALTY;

            // Bonus for being on the ground (stable state)
            if (final_state.onGround)
                fitness += 1000;

            return fitness;
        }

        // ===== BEAM SEARCH =====

        /// <summary>
        /// A candidate path in the beam search.
        /// </summary>
        private class PFCandidate
        {
            public List<bool> inputs;     // Input sequence (true=press, false=release)
            public PFState state;         // State after applying inputs
            public int fitness;           // Evaluated fitness
            
            public PFCandidate()
            {
                inputs = new List<bool>();
                fitness = int.MinValue;
            }

            public PFCandidate Clone()
            {
                var c = new PFCandidate();
                c.inputs = new List<bool>(inputs);
                c.state = state; // struct copy
                c.fitness = fitness;
                return c;
            }
        }

        /// <summary>
        /// Runs beam search to find the best input sequence from the current state.
        /// Must be called while holding simLock.
        /// </summary>
        private List<bool> PF_RunBeamSearch()
        {
            // Capture the current state as our starting point
            PFState rootState = PF_CaptureState();

            // Initialize beam with two seeds: press and release
            var beam = new List<PFCandidate>();

            // Seed 1: start with press
            {
                var c = new PFCandidate();
                c.state = rootState;
                c.inputs.Add(true);
                beam.Add(c);
            }
            // Seed 2: start with release
            {
                var c = new PFCandidate();
                c.state = rootState;
                c.inputs.Add(false);
                beam.Add(c);
            }

            // Simulate frame-by-frame, expanding and pruning the beam
            for (int frame = 0; frame < PF_LOOKAHEAD_FRAMES; frame++)
            {
                // Simulate each candidate for one frame
                foreach (var candidate in beam)
                {
                    PF_RestoreState(candidate.state);

                    // Get the input for this frame
                    bool input = candidate.inputs[candidate.inputs.Count - 1];
                    PF_InjectInput(input);

                    // Simulate one frame
                    PF_SimulateOneFrame();

                    // Capture resulting state
                    candidate.state = PF_CaptureState();
                    candidate.fitness = PF_EvaluateFitness(rootState, candidate.state);
                }

                // At input granularity boundaries, branch and prune
                if ((frame + 1) % PF_INPUT_GRANULARITY == 0 && frame < PF_LOOKAHEAD_FRAMES - 1)
                {
                    // Expand: each candidate branches into press and release
                    var expanded = new List<PFCandidate>(beam.Count * 2);
                    foreach (var candidate in beam)
                    {
                        // Skip dead candidates from further branching
                        if (candidate.state.deathTriggered)
                        {
                            expanded.Add(candidate); // Keep it for comparison but don't branch
                            continue;
                        }

                        // Branch: press
                        var pressCandidate = candidate.Clone();
                        pressCandidate.inputs.Add(true);
                        expanded.Add(pressCandidate);

                        // Branch: release
                        var releaseCandidate = candidate.Clone();
                        releaseCandidate.inputs.Add(false);
                        expanded.Add(releaseCandidate);
                    }

                    // Prune: keep the top PF_BEAM_WIDTH candidates by fitness
                    expanded.Sort((a, b) => b.fitness.CompareTo(a.fitness));
                    beam = expanded.GetRange(0, Math.Min(PF_BEAM_WIDTH, expanded.Count));
                }
            }

            // Restore original state
            PF_RestoreState(rootState);

            // Return the input sequence from the best candidate
            beam.Sort((a, b) => b.fitness.CompareTo(a.fitness));
            return beam.Count > 0 ? beam[0].inputs : new List<bool>();
        }

        // ===== PUBLIC API =====

        /// <summary>
        /// Called from SimulateNumericStep to get the pathfinder's input for this frame.
        /// Runs beam search when the plan is exhausted or nearly exhausted.
        /// Returns true if input should be pressed, false if released.
        /// </summary>
        private bool PF_GetInput()
        {
            lock (pfLock)
            {
                // Check if we need to (re)plan
                bool needsReplan = pfPlan.Count == 0 ||
                                   pfPlanIndex >= pfPlan.Count ||
                                   (pfPlan.Count - pfPlanIndex) <= PF_REPLAN_THRESHOLD;

                if (needsReplan)
                {
                    pfPlan = PF_RunBeamSearch();
                    pfPlanIndex = 0;
                    pfPlanStartX = playerX_fixed;
                }

                // Get current input from plan
                if (pfPlanIndex < pfPlan.Count)
                {
                    return pfPlan[pfPlanIndex++];
                }

                // Fallback: no input
                return false;
            }
        }
    }
}
