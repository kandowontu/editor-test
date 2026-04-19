using System;

namespace FamidashEditor
{
    public partial class SimulatorWindow
    {
        /// <summary>
        /// ball_movement() from gamemode_ball.h - 1:1 port
        /// Handles both ball and swing modes
        /// </summary>
        private void BallPhysics_Fresh()
        {
            // Get base physics values (always from down-gravity index)
            int baseTableIdx = (currplayer_mini != 0 ? 4 : 0);
            bool gravityInverted = (currplayer_gravity != 0);
            int gravityMultiplier = gravityInverted ? -1 : 1;
            
            if (currentGameMode == 7 || currentGameMode == 9) { // GAMEMODE_SWING or GAMEMODE_POGO
                tmpfallspeed = GameModePhysics.SWING_MAX_FALLSPEED(baseTableIdx) * gravityMultiplier;
                tmpgravity = GameModePhysics.SWING_GRAVITY(baseTableIdx) * gravityMultiplier;
            } else {
                tmpfallspeed = GameModePhysics.BALL_MAX_FALLSPEED(baseTableIdx) * gravityMultiplier;
                tmpgravity = GameModePhysics.BALL_GRAVITY(baseTableIdx) * gravityMultiplier;
            }
            
            // Ball gravity flip logic - BEFORE gravity/collision
            if (currentGameMode == 2) { // GAMEMODE_BALL
                // Check for orb activation FIRST (before consuming input)
                {
                    bool holdJump_orb = IsXDownAsync() || keyXHeld;
                    int pressCount_peek = Interlocked.CompareExchange(ref keyXPressedCount, 0, 0);
                    bool pressJump_peek = pressCount_peek > 0;
                    bool gravityInverted_orb = (currplayer_gravity != 0);
                    int playerX_px_orb = (playerX_fixed >> 8) + 1;
                    int playerY_px_orb = playerY_fixed >> 8;
                    int hitboxW_orb = (currplayer_mini != 0) ? 8 : 15;
                    int hitboxH_orb = (currplayer_mini != 0) ? 7 : 15;
                    // NES: Generic.y += ((0x10 - height) >> 1); Normal: +0, Mini: +4
                    if (currplayer_mini != 0 && !gravityInverted_orb)
                    {
                        playerY_px_orb += 4;
                    }
                    
                    int scrollX_px_orb = 0;
                    
                    int tempVelY = playerVelY_fixed;
                    var (orbActivated, _) = UpdateOrbSystem(2, playerX_px_orb, playerY_px_orb, hitboxW_orb, hitboxH_orb, 
                                                       scrollX_px_orb, pressJump_peek, holdJump_orb, gravityInverted_orb, 
                                                       (currplayer_mini != 0), ref tempVelY);
                    if (orbActivated)
                    {
                        playerVelY_fixed = tempVelY;
                        orbhitonthisframe[currplayer] = true;
                        AppendSimDebug($"[BALL] Orb activated! New velY={playerVelY_fixed}");
                        
                        // Consume the X press if it was used for orb
                        if (pressJump_peek)
                            Interlocked.Exchange(ref keyXPressedCount, 0);
                        
                        // Clear buffer on orb activation (require fresh press for next action)
                        ClearOrbBuffer();
                    }
                    
                    // Clear orb buffer when X is released
                    if (!holdJump_orb)
                        ClearOrbBuffer();
                }
                
                // Refresh gravity locals after orb processing — blue/green orbs flip
                // currplayer_gravity inside ActivateOrb(), but gravityInverted was read
                // before orb processing.  Without this, the grounded spike death check
                // uses a stale value and kills the player when PF (correctly) survives.
                gravityInverted = (currplayer_gravity != 0);
                gravityMultiplier = gravityInverted ? -1 : 1;
                tmpfallspeed = GameModePhysics.BALL_MAX_FALLSPEED(baseTableIdx) * gravityMultiplier;
                tmpgravity = GameModePhysics.BALL_GRAVITY(baseTableIdx) * gravityMultiplier;
                
                // Read input for ground flip - track fresh press and hold state
                bool holdJump = IsXDownAsync() || keyXHeld;
                int pressCount = Interlocked.CompareExchange(ref keyXPressedCount, 0, 0);
                bool pressJump = (pressCount > 0);
                
                // Also check ballToggleRequested flag (set by UI KeyDown)
                int toggleRequested = Interlocked.CompareExchange(ref ballToggleRequested, 0, 0);
                if (toggleRequested > 0)
                {
                    pressJump = true; // Treat toggle request as a press
                    Interlocked.Exchange(ref ballToggleRequested, 0); // Consume it
                    LogBallEvent($"[BALL] Consumed ballToggleRequested flag");
                }
                
                // Check if grounded by doing a collision check
                int savedY = playerY_fixed;
                
                // Check if grounded by testing a small collision hitbox slightly below/above player
                // This detects if there's ground to grip onto for gravity flip
                // During cooldown after flip, use cached grounded state from flip frame
                bool isGrounded = false;
                bool isMini = (currplayer_mini != 0);
                int hitboxW = isMini ? 8 : 15;
                int hitboxH = isMini ? 7 : 15;
                int hitboxOffsetY = isMini ? ((0x10 - hitboxH) >> 1) : 0;
                
                // If we're in cooldown from a recent flip, use the cached grounded state
                if (ballFlipCooldown > 0) {
                    isGrounded = ballWasGroundedBeforeFlip;
                    AppendSimDebug($"[BALL] Using cached grounded state during cooldown: isGrounded={isGrounded}");
                } else {
                    // Step 0: Grounded spike death check — runs every frame before flip/gravity.
                    // Normal gravity only: probe 2px below player bottom for floor spikes.
                    // Matches PF's BALL_GROUNDED_SPIKE_DEATH check.
                    if (!gravityInverted)
                    {
                        int playerBottom_g = (playerY_fixed >> 8) + hitboxOffsetY + hitboxH;
                        int groundRowsToReserve_g = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                        var map_g = new SharedPhysics.CollisionMap(tiles, mapWidth, mapHeight, groundRowsToReserve_g);
                        var (_, _, groundedSpike) = SharedPhysics.CheckFloor(in map_g, playerX_fixed >> 8, playerBottom_g, hitboxW, 2);
                        if (groundedSpike && !MainWindow.Option_NoDeath)
                        {
                            AppendSimDebug($"[BALL_GROUNDED_SPIKE_DEATH] X={playerX_fixed >> 8} Y={playerY_fixed >> 8}");
                            deathTriggered = true;
                            deathTileX = playerX_fixed >> 8;
                            deathTileY = playerBottom_g;
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
                                        try { mw.AddDeathMarker(deathTileX, deathTileY); } catch { }
                                    }
                                }));
                            }
                            catch { }
                            return;
                        }
                    }

                    // Use SharedPhysics.BallIsGrounded to match PF exactly:
                    // no spike death side effects, spikes not considered ground.
                    int groundRowsToReserve = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                    var map = new SharedPhysics.CollisionMap(tiles, mapWidth, mapHeight, groundRowsToReserve);
                    isGrounded = SharedPhysics.BallIsGrounded(in map,
                        playerX_fixed >> 8, playerY_fixed >> 8,
                        hitboxW, hitboxH, hitboxOffsetY,
                        gravityInverted);
                    // Match PF's || s.OnGround fallback: slopes set was_on_slope_counter
                    // via bg_coll_D_slopes in eject, allowing flip on the next frame
                    // even when BallIsGrounded probe misses the slope tile.
                    if (!isGrounded && currplayer_was_on_slope_counter > 0)
                        isGrounded = true;
                    AppendSimDebug($"[BALL] Grounded check: isGrounded={isGrounded}");
                }
                
                AppendSimDebug($"[BALL] press={pressJump}, hold={holdJump}, ballSwitched={ballSwitched[currplayer]}, velY={playerVelY_fixed}, grounded={isGrounded}");
                
                // Ground flip buffering - similar to orb buffering
                // Can buffer the input while falling to trigger when landing
                bool shouldFlip = false;
                
                AppendSimDebug($"[BALL_FLIP_CHECK] pressJump={pressJump} holdJump={holdJump} isGrounded={isGrounded} currplayer={currplayer} orbHoldConsumed={orbHoldConsumedKeyStillDown[currplayer]} orbHoldSuppress={orbHoldSuppressing[currplayer]} ballSwitched={ballSwitched[currplayer]} ballFlipBuffer={ballFlipBuffer[currplayer]}");
                
                // Path 1: Fresh press — flip directly if grounded, otherwise buffer for landing
                // NES: orbhitonthisframe suppresses ball flip when an orb was activated this frame
                if (pressJump && !orbhitonthisframe[currplayer] && !orbHoldConsumedKeyStillDown[currplayer] && !orbHoldSuppressing[currplayer])
                {
                    if (isGrounded)
                    {
                        // Allow flip even if ballSwitched is true - fresh press overrides
                        shouldFlip = true;
                    }
                    else
                    {
                        // Buffer the input for landing within 8 frames (matches PF's BallInputBuffer)
                        ballFlipBuffer[currplayer] = 8;
                    }
                    orbBufferActive[currplayer] = true; // Keep for orb system
                }
                // Path 2: Buffered landing flip — countdown-based, matching PF's BallInputBuffer
                else if (ballFlipBuffer[currplayer] > 0 && !orbhitonthisframe[currplayer] && !ballSwitched[currplayer] && isGrounded)
                {
                    shouldFlip = true;
                }
                
                // Ball flips gravity when grounded with buffered/fresh press
                if (shouldFlip) {
                    AppendSimDebug($"[BALL] FLIPPING GRAVITY!");
                    InvertGravity_Fresh();
                    UpdateCurrplayerTableIdx_Fresh(); // Must update table_idx AFTER gravity flip!
                    
                    // Recalculate physics values with new gravity
                    gravityInverted = (currplayer_gravity != 0);
                    gravityMultiplier = gravityInverted ? -1 : 1;
                    tmpfallspeed = GameModePhysics.BALL_MAX_FALLSPEED(baseTableIdx) * gravityMultiplier;
                    tmpgravity = GameModePhysics.BALL_GRAVITY(baseTableIdx) * gravityMultiplier;
                    
                    ballSwitched[currplayer] = true;
                    playerVelY_fixed = GameModePhysics.BALL_SWITCH_VEL(currplayer_table_idx);
                    
                    // Skip collision checks for 2 frames after flip to prevent stutter
                    ballFlipCooldown = 2;
                    // Cache grounded state from flip frame to use during cooldown
                    ballWasGroundedBeforeFlip = true;
                    
                    // Consume the press and clear buffer (require fresh press for next flip)
                    Interlocked.Exchange(ref keyXPressedCount, 0);
                    orbHoldConsumedKeyStillDown[currplayer] = true;
                    ballFlipBuffer[currplayer] = 0;
                    ClearOrbBuffer();
                }
                else if (ballFlipBuffer[currplayer] > 0)
                {
                    // Decrement ball flip buffer countdown (matches PF's BallInputBuffer decrement)
                    ballFlipBuffer[currplayer]--;
                }
                
                // Clear ballSwitched flag when key is released
                if (ballSwitched[currplayer] && !holdJump) {
                    ballSwitched[currplayer] = false;
                }
            }
            
            // Pogo and Swing: Check for orb/pad activations BEFORE gravity.
            // NES: sprite_collide (orb activation) runs BEFORE ball_movement (gravity/eject),
            // so the orb sees the pre-movement position and pre-flip gravity.
            if (currentGameMode == 7 || currentGameMode == 9) { // GAMEMODE_SWING or GAMEMODE_POGO
                bool holdJump_orb = IsXDownAsync() || keyXHeld;
                int pressCount_orb = Interlocked.CompareExchange(ref keyXPressedCount, 0, 0);
                bool pressJump_orb = pressCount_orb > 0;
                bool gravityInverted_orb = (currplayer_gravity != 0);
                int playerX_px_orb = (playerX_fixed >> 8) + 1;
                int playerY_px_orb = playerY_fixed >> 8;
                int hitboxW_orb = (currplayer_mini != 0) ? 8 : 15;
                int hitboxH_orb = (currplayer_mini != 0) ? 7 : 15;
                
                // NES: Generic.y += ((0x10 - height) >> 1); Normal: +0, Mini: +4
                if ((currplayer_mini != 0) && !gravityInverted_orb)
                {
                    playerY_px_orb += 4;
                }
                
                int scrollX_px_orb = 0;
                
                int tempVelY = playerVelY_fixed;
                // Pogo uses Swing (7) interactions, not its own gamemode
                int orbGamemode = (currentGameMode == 9) ? 7 : currentGameMode;
                var (orbActivated, _) = UpdateOrbSystem(orbGamemode, playerX_px_orb, playerY_px_orb, hitboxW_orb, hitboxH_orb, 
                                                   scrollX_px_orb, pressJump_orb, holdJump_orb, gravityInverted_orb, 
                                                   (currplayer_mini != 0), ref tempVelY);
                if (orbActivated)
                {
                    playerVelY_fixed = tempVelY;
                    orbhitonthisframe[currplayer] = true;
                    AppendSimDebug($"[SWING/POGO] Orb/Pad activated! New velY={playerVelY_fixed}");
                    
                    // Consume the X press if it was used for orb
                    if (pressJump_orb)
                        Interlocked.Exchange(ref keyXPressedCount, 0);
                }
                
                // Clear orb buffer when X is released
                if (!holdJump_orb)
                    ClearOrbBuffer();
            }

            CommonGravityRoutine_Fresh();
            
            // Skip velocity zeroing AND collision ejection during flip cooldown
            if (ballFlipCooldown > 0) {
                ballFlipCooldown--;
                AppendSimDebug($"[BALL] Skipping velocity/collision checks during cooldown: {ballFlipCooldown} frames remaining");
                // Clear cached state when cooldown expires
                if (ballFlipCooldown == 0) {
                    ballWasGroundedBeforeFlip = false;
                }
                return;  // Skip everything below during cooldown
            }
            
            // Prevent velocity accumulation when grounded (only runs when cooldown == 0)
            if (currentGameMode == 2 || currentGameMode == 9) { // BALL or POGO
                bool isMini = (currplayer_mini != 0);
                int hitboxW = isMini ? 8 : 15;
                int hitboxH = isMini ? 7 : 15;
                int hitboxOffsetY = isMini ? ((0x10 - hitboxH) >> 1) : 0;
                int collisionX = (playerX_fixed >> 8);
                
                if (currplayer_gravity == 0) {
                    // Inverted gravity - prevent velocity from pulling into ceiling
                    // Only check when actually moving toward ceiling (velY < 0).
                    // CheckCollisionUp may have spike death side-effects.
                    if (playerVelY_fixed < 0) {
                        int testY = (playerY_fixed >> 8) + hitboxOffsetY - 1;
                        var (collided, collisionBottomY_prox) = CheckCollisionUp(collisionX, testY, hitboxW, hitboxH);
                        
                        if (collided) {
                            // Snap Y to ceiling surface (prevents sub-pixel drift)
                            int newY_prox = collisionBottomY_prox - hitboxOffsetY;
                            playerY_fixed = newY_prox << 8;
                            playerVelY_fixed = 0;
                            AppendSimDebug($"[BALL] Ceiling grounded - snapped Y to {newY_prox}");
                        }
                    }
                } else {
                    // Normal gravity - prevent velocity from pulling into ground
                    // CRITICAL: Only call CheckCollisionDown when velY > 0 (moving toward floor).
                    // CheckCollisionDown has a floor-spike death side-effect that fires
                    // unconditionally.  When the ball is moving AWAY from the floor (velY <= 0),
                    // the velocity guard below would reject the collision anyway, but the spike
                    // death has already triggered inside CheckCollisionDown, killing the player
                    // against a spike it's moving away from.
                    if (playerVelY_fixed > 0) {
                        int playerBottom = (playerY_fixed >> 8) + hitboxOffsetY + hitboxH;
                        int testHeight = 2;
                        int testTop = playerBottom - testHeight;
                        var (collided, collisionTopY) = CheckCollisionDown(collisionX, testTop, hitboxW, testHeight);
                        
                        if (collided) {
                            // Fix 34: Snap Y to floor surface (matches PF BallVelocityZeroing).
                            // Previously only zeroed velocity without adjusting position,
                            // causing the ball to drift into the floor when a pad kept
                            // re-applying downward velocity each frame.
                            int newY = collisionTopY - hitboxH - hitboxOffsetY;
                            playerY_fixed = newY << 8;
                            playerVelY_fixed = 0;
                            AppendSimDebug($"[BALL] Floor grounded - zeroed downward velocity, snapped Y to {newY}");
                        }
                    }
                }
            }
            
            // Collision ejection
            if (currentGameMode == 7) // Swingcopter: ship-style eject (both directions, no velocity guard)
                UfoShipEject_Fresh();
            else
                BallEject_Fresh();

            // NES ball_movement does NOT run bg_coll_death inside the mode handler.
            // Center death runs AFTER x_movement advances currplayer_x (at NEW X).
            // The main loop's CheckDeathCollision (Step 8b) handles it for all modes.

            // Update slope exit velocity counters
            UpdateSlopeCounters_Fresh();

            // Swing gravity flip logic
            if (currentGameMode == 7) { // GAMEMODE_SWING
                int pressCount = Interlocked.Exchange(ref keyXPressedCount, 0);
                bool pressedJump = pressCount > 0;
                
                AppendSimDebug($"[SWING] pressCount={pressCount} pressedJump={pressedJump} ufoOrbed={ufoOrbed}");
                
                if (pressedJump && !ufoOrbed) {
                    AppendSimDebug($"[SWING] FLIPPING GRAVITY!");
                    InvertGravity_Fresh();
                    UpdateCurrplayerTableIdx_Fresh(); // Must update table_idx AFTER gravity flip!
                    // Swing does NOT apply velocity like Ball does - just flips gravity
                }
            }
            // Pogo black orb X press activation (in addition to regular orb/pad checks)
            if (currentGameMode == 9) { // GAMEMODE_POGO
                int pressCount = Interlocked.Exchange(ref keyXPressedCount, 0);
                bool pressedJump = pressCount > 0;
                
                if (pressedJump && !orbhitonthisframe[currplayer]
                    && !orbHoldConsumedKeyStillDown[currplayer] && !orbHoldSuppressing[currplayer]) {
                    // Black orb velocity: opposite direction to normal orbs
                    // Normal gravity: positive (downward), Inverted gravity: negative (upward)
                    bool isMini_orb = (currplayer_mini != 0);
                    int blackOrbVel = isMini_orb ? PadOrbHeights_Mini[6][7] : PadOrbHeights[6][7];
                    int orbGravityMult = (currplayer_gravity == 0) ? -1 : 1;  // Normal: negate, Inverted: keep
                    playerVelY_fixed = blackOrbVel * orbGravityMult;
                    AppendSimDebug($"[POGO_BLACKORB] X pressed! velY set to 0x{playerVelY_fixed:X4}");
                    // Consume the press (require fresh press for next activation)
                    orbHoldConsumedKeyStillDown[currplayer] = true;
                    ClearOrbBuffer();
                }
            }
            
            // Record position for trail (skip during pathfinder speculative simulation)
            if (!pfSimulating)
            try
            {
                int playerWorldCenterX_px = (playerX_fixed >> 8) + (playerVisualWidth / 2);
                bool isMini = (currplayer_mini != 0);
                int hitboxH = isMini ? 7 : 15;
                int hitboxOffsetY = isMini ? ((0x10 - hitboxH) >> 1) : 0;
                int playerWorldCenterY_px = (playerY_fixed >> 8) + (playerVisualHeight / 2) + hitboxOffsetY;
                // Record to appropriate path list based on which player is active
                if (currplayer == 0)
                    recordedPlayerPath.Add((playerWorldCenterX_px, playerWorldCenterY_px));
                else if (dual)
                    RecordP2PathPoint(playerWorldCenterX_px, playerWorldCenterY_px);
            }
            catch { }
        }
        
        /// <summary>
        /// ball_eject() — thin wrapper over SharedPhysics.BallEject.
        /// Handles SIM-specific pogo bounce (mode 9) AFTER shared ejection.
        /// </summary>
        private void BallEject_Fresh()
        {
            bool mini = currplayer_mini != 0;
            bool gravFlipped = currplayer_gravity != 0;
            bool inputHeld = IsXDownAsync() || keyXHeld || upHeld;

            int groundRowsToReserve = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
            var map = new SharedPhysics.CollisionMap(tiles, mapWidth, mapHeight, groundRowsToReserve);

            int oldVelY = playerVelY_fixed; // Save for pogo bounce calculation

            var r = SharedPhysics.BallEject(in map,
                playerX_fixed, playerY_fixed, playerVelY_fixed, playerVelX_fixed,
                gravFlipped, mini, currentGameMode, inputHeld,
                currplayer_was_on_slope_counter, currplayer_slope_frames,
                currplayer_slope_type, make_cube_jump_higher,
                currplayer_last_slope_type);

            playerY_fixed = r.NewY_fixed;
            playerVelY_fixed = r.NewVelY_fixed;
            onGround = r.OnGround;
            currplayer_slope_type = r.SlopeType;
            currplayer_slope_frames = r.SlopeFrames;
            currplayer_was_on_slope_counter = r.SlopeWasOnCounter;
            make_cube_jump_higher = r.SlopeJumpHigher;
            currplayer_last_slope_type = r.LastSlopeType;

            if (r.Died && !MainWindow.Option_NoDeath)
            {
                AppendSimDebug($"[DEATH] Floor spike detected (SharedPhysics.BallEject)");
                deathTriggered = true;
                deathTileX = playerX_fixed >> 8;
                deathTileY = (playerY_fixed >> 8) + SharedPhysics.GetCubeHitboxH(mini);
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
                            try { mw.AddDeathMarker(deathTileX, deathTileY); } catch { }
                        }
                    }));
                }
                catch { }
                return;
            }

            // Pogo mode (9): override zeroed velocity with bounce
            if (currentGameMode == 9 && r.OnGround && !orbhitonthisframe[currplayer])
            {
                int newVel = (-oldVelY / 3) * 2;
                int yellowPadMin = mini ? PadOrbHeights_Mini[1][7] : PadOrbHeights[1][7];
                if (!gravFlipped)
                {
                    int minVel = yellowPadMin * -1;
                    if (newVel > minVel) newVel = minVel;
                }
                else
                {
                    int minVel = yellowPadMin;
                    if (newVel < minVel) newVel = minVel;
                }
                playerVelY_fixed = newVel;
                pogoBounceAnimationCounter = 8;
                AppendSimDebug($"[POGO_BOUNCE] velY: old=0x{oldVelY:X4} -> new=0x{newVel:X4}");
            }
        }
        
        /// <summary>
        /// Helper to invert gravity
        /// </summary>
        private void InvertGravity_Fresh()
        {
            gravityFlipped = !gravityFlipped;
            gravityReversed = gravityFlipped;
            currplayer_gravity = (byte)(gravityFlipped ? 0xFF : 0x00);
            UpdateCurrplayerTableIdx_Fresh();
            UpdatePlayerIconFlip();
        }
        
        private void UpdateCurrplayerTableIdx_Fresh()
        {
            currplayer_table_idx = (currplayer_gravity == 0 ? 1 : 0) | (currplayer_mini != 0 ? 4 : 0);
        }
    }
}





