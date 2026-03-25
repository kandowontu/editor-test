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
                    AppendSimDebug($"[BALL] Grounded check: isGrounded={isGrounded}");
                }
                
                AppendSimDebug($"[BALL] press={pressJump}, hold={holdJump}, ballSwitched={ballSwitched[currplayer]}, velY={playerVelY_fixed}, grounded={isGrounded}");
                
                // Ground flip buffering - similar to orb buffering
                // Can buffer the input while falling to trigger when landing
                bool shouldFlip = false;
                
                AppendSimDebug($"[BALL_FLIP_CHECK] pressJump={pressJump} holdJump={holdJump} isGrounded={isGrounded} currplayer={currplayer} orbHoldConsumed={orbHoldConsumedKeyStillDown[currplayer]} orbHoldSuppress={orbHoldSuppressing[currplayer]} ballSwitched={ballSwitched[currplayer]} ballFlipBuffer={ballFlipBuffer[currplayer]}");
                
                // Path 1: Fresh press — flip directly if grounded, otherwise buffer for landing
                if (pressJump && !orbHoldConsumedKeyStillDown[currplayer] && !orbHoldSuppressing[currplayer])
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
                else if (ballFlipBuffer[currplayer] > 0 && !ballSwitched[currplayer] && isGrounded)
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
            if (currentGameMode == 2) {
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
                        int testTop = playerBottom;
                        int testHeight = 2;
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
            // Pogo and Swing: Check for orb/pad activations
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
                    AppendSimDebug($"[SWING/POGO] Orb/Pad activated! New velY={playerVelY_fixed}");
                    
                    // Consume the X press if it was used for orb
                    if (pressJump_orb)
                        Interlocked.Exchange(ref keyXPressedCount, 0);
                }
                
                // Clear orb buffer when X is released
                if (!holdJump_orb)
                    ClearOrbBuffer();
            }
            
            // Pogo black orb X press activation (in addition to regular orb/pad checks)
            if (currentGameMode == 9) { // GAMEMODE_POGO
                int pressCount = Interlocked.Exchange(ref keyXPressedCount, 0);
                bool pressedJump = pressCount > 0;
                
                if (pressedJump && !orbhitonthisframe[currplayer]) {
                    // Black orb velocity: opposite direction to normal orbs
                    // Normal gravity: positive (downward), Inverted gravity: negative (upward)
                    bool isMini_orb = (currplayer_mini != 0);
                    int blackOrbVel = isMini_orb ? PadOrbHeights_Mini[6][7] : PadOrbHeights[6][7];
                    int orbGravityMult = (currplayer_gravity == 0) ? -1 : 1;  // Normal: negate, Inverted: keep
                    playerVelY_fixed = blackOrbVel * orbGravityMult;
                    AppendSimDebug($"[POGO_BLACKORB] X pressed! velY set to 0x{playerVelY_fixed:X4}");
                    // Clear orb buffer on pogo activation (require fresh press for next orb)
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
                    recordedPlayer2Path.Add((playerWorldCenterX_px, playerWorldCenterY_px));
            }
            catch { }
        }
        
        /// <summary>
        /// ball_eject() helper
        /// </summary>
        private void BallEject_Fresh()
        {
            bool isMini = (currplayer_mini != 0);
            int hitboxW = isMini ? 8 : 15;
            int hitboxH = isMini ? 7 : 15;
            int hitboxOffsetY = isMini ? ((0x10 - hitboxH) >> 1) : 0;
            int collisionX = (playerX_fixed >> 8);
            // NES ball_movement: offsets collision 1 pixel to prevent every-other-frame oscillation
            int ballYOffset = (currplayer_gravity == 0) ? 1 : -1;
            int collisionY = (playerY_fixed >> 8) + hitboxOffsetY + ballYOffset;

            // Update slope counters each frame
            UpdateSlopeCounters();
            
            if (currplayer_gravity == 0) {
                // Normal gravity: Check slopes FIRST
                bool slopeHit = bg_coll_D_slopes();
                if (slopeHit)
                {
                    if (eject_D > 0)
                    {
                        int newPixelY = (playerY_fixed >> 8) - eject_D;
                        playerY_fixed = newPixelY << 8;
                    }
                    // Pogo mode: bounce off slopes
                    if (currentGameMode == 9 && !orbhitonthisframe[currplayer])
                    {
                        int newVel = (-playerVelY_fixed / 3) * 2;
                        int yellowPadMin = isMini ? PadOrbHeights_Mini[1][7] : PadOrbHeights[1][7];
                        int minVel = yellowPadMin * -1;
                        if (newVel > minVel)
                            newVel = minVel;
                        playerVelY_fixed = newVel;
                        pogoBounceAnimationCounter = 8;
                    }
                    else
                    {
                        playerVelY_fixed = 0;
                    }
                }
                else
                {
                // No slope hit - fall through to flat collision
                // NOTE: NES ball_eject does NOT have right-side pixel death here;
                // death is handled separately by bg_coll_death / forward collision (bg_coll_R).
                // Removed false-positive right-side pixel death check that triggered
                // before eject could correct the player position.
                
                // Skip downward collision if we just flipped and are moving up
                if (playerVelY_fixed >= 0) {
                    var (collided, collisionTopY) = CheckCollisionDown(collisionX, collisionY, hitboxW, hitboxH);
                    if (collided) {
                        int newY = collisionTopY - hitboxH - hitboxOffsetY - ballYOffset;
                        int oldY = playerY_fixed >> 8;
                        AppendSimDebug($"[BALL_EJECT_D] collisionTopY={collisionTopY}, hitboxH={hitboxH}, hitboxOffsetY={hitboxOffsetY}, oldY={oldY}, newY={newY}");
                        playerY_fixed = (newY << 8);
                        
                        // Pogo mode: bounce instead of zero velocity
                        if (currentGameMode == 9)
                        {
                            if (!orbhitonthisframe[currplayer])
                            {
                                int newVel = (-playerVelY_fixed / 3) * 2;
                                // Bounce minimum = yellow pad velocity for swing (mode 7, col index 7)
                                int yellowPadMin = isMini ? PadOrbHeights_Mini[1][7] : PadOrbHeights[1][7];
                                int minVel = yellowPadMin * -1;
                                // For downward bounce (normal gravity): check if vel > min (more negative)
                                if (newVel > minVel)
                                    newVel = minVel;
                                playerVelY_fixed = newVel;
                                pogoBounceAnimationCounter = 8; // Show pogo2.png for 8 frames
                                AppendSimDebug($"[POGO_BOUNCE_D] velY: old=0x{playerVelY_fixed:X4} -> new=0x{newVel:X4}, min=0x{minVel:X4}");
                            }
                        }
                        else
                        {
                            playerVelY_fixed = 0;
                        }
                    }
                }
                } // close slope else branch
            } else {
                // Inverted gravity
                // NOTE: NES ball_eject does NOT have right-side pixel death here;
                // death is handled by bg_coll_death / forward collision (bg_coll_R).
                // Removed false-positive right-side pixel death check that fired
                // BEFORE eject could correct position, causing spurious deaths.
                
                // Skip upward collision if we just flipped and are moving down
                if (playerVelY_fixed <= 0) {
                    var (collided, collisionBottomY) = CheckCollisionUp(collisionX, collisionY, hitboxW, hitboxH);
                    if (collided) {
                        // Place player directly at collision surface
                        int newY = collisionBottomY - hitboxOffsetY;
                        int oldY = playerY_fixed >> 8;
                        AppendSimDebug($"[BALL_EJECT_U] collisionBottomY={collisionBottomY}, hitboxOffsetY={hitboxOffsetY}, oldY={oldY}, newY={newY}");
                        playerY_fixed = (newY << 8);
                        
                        // Pogo mode: bounce instead of zero velocity
                        if (currentGameMode == 9)
                        {
                            if (!orbhitonthisframe[currplayer])
                            {
                                int newVel = (-playerVelY_fixed / 3) * 2;
                                // Bounce minimum = yellow pad velocity for swing (mode 7, col index 7)
                                int yellowPadMin = isMini ? PadOrbHeights_Mini[1][7] : PadOrbHeights[1][7];
                                int minVel = yellowPadMin;
                                // For upward bounce (inverted gravity): check if vel < min (more positive)
                                if (newVel < minVel)
                                    newVel = minVel;
                                playerVelY_fixed = newVel;
                                pogoBounceAnimationCounter = 8; // Show pogo2.png for 8 frames
                                AppendSimDebug($"[POGO_BOUNCE_U] velY: old=0x{playerVelY_fixed:X4} -> new=0x{newVel:X4}, min=0x{minVel:X4}");
                            }
                        }
                        else
                        {
                            playerVelY_fixed = 0;
                        }
                    }
                }
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





