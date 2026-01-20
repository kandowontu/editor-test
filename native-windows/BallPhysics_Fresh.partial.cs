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
            
            if (currentGameMode == 7) { // GAMEMODE_SWING
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
                    int playerX_px_orb = playerX_fixed >> 8;
                    int playerY_px_orb = playerY_fixed >> 8;
                    int hitboxW_orb = (currplayer_mini != 0) ? 8 : 15;
                    int hitboxH_orb = (currplayer_mini != 0) ? 8 : 15;
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
                        
                        // Clear buffer on orb activation (require fresh press for next orb chain)
                        ClearOrbBuffer();
                    }
                    
                    // Clear orb buffer when X is released
                    if (!holdJump_orb)
                        ClearOrbBuffer();
                }
                
                // Read input - use press instead of hold
                int pressCount = Interlocked.Exchange(ref keyXPressedCount, 0);
                bool pressJump = pressCount > 0;
                
                // Check if grounded by doing a collision check
                int savedY = playerY_fixed;
                
                // Check if grounded by testing a small collision hitbox slightly below/above player
                // This detects if there's ground to grip onto for gravity flip
                // During cooldown after flip, use cached grounded state from flip frame
                bool isGrounded = false;
                int hitboxW = (currplayer_mini != 0) ? 8 : 15;
                int hitboxH = (currplayer_mini != 0) ? 8 : 15;
                int hitboxOffsetY = (currplayer_mini != 0 && currplayer_gravity == 0) ? 8 : 0;
                int collisionX = (playerX_fixed >> 8);
                
                // If we're in cooldown from a recent flip, use the cached grounded state
                if (ballFlipCooldown > 0) {
                    isGrounded = ballWasGroundedBeforeFlip;
                    AppendSimDebug($"[BALL] Using cached grounded state during cooldown: isGrounded={isGrounded}");
                } else if (currplayer_gravity == 0) {
                    // Normal gravity - test a 2px tall hitbox starting at player's bottom (tests downward)
                    int playerBottom = (playerY_fixed >> 8) + hitboxOffsetY + hitboxH;
                    int testHeight = 2;
                    int testTop = playerBottom;  // Start test at player's bottom edge
                    var (collided, _) = CheckCollisionDown(collisionX, testTop, hitboxW, testHeight);
                    isGrounded = collided;
                    AppendSimDebug($"[BALL] Grounded check (normal): playerBottom={playerBottom}, testTop={testTop}, isGrounded={isGrounded}");
                } else {
                    // Inverted gravity - test UPWARD at player's TOP (ceiling contact)
                    // When inverted, the ball hangs from ceiling, so TOP touches the surface
                    int playerTop = (playerY_fixed >> 8) + hitboxOffsetY;
                    int testHeight = 2;
                    int testTop = playerTop - 2;  // Start test 2px above player's top to detect ceiling
                    var (collided, _) = CheckCollisionUp(collisionX, testTop, hitboxW, testHeight);
                    isGrounded = collided;
                    AppendSimDebug($"[BALL] Grounded check (inverted): playerTop={playerTop}, testTop={testTop}, isGrounded={isGrounded}");
                }
                
                AppendSimDebug($"[BALL] press={pressJump}, ballSwitched={ballSwitched[0]}, velY={playerVelY_fixed}, grounded={isGrounded}");
                
                // Ball flips gravity when pressing X and grounded
                if (pressJump && !ballSwitched[0] && isGrounded) {
                    AppendSimDebug($"[BALL] FLIPPING GRAVITY!");
                    InvertGravity_Fresh();
                    UpdateCurrplayerTableIdx_Fresh(); // Must update table_idx AFTER gravity flip!
                    
                    // Recalculate physics values with new gravity
                    gravityInverted = (currplayer_gravity != 0);
                    gravityMultiplier = gravityInverted ? -1 : 1;
                    tmpfallspeed = GameModePhysics.BALL_MAX_FALLSPEED(baseTableIdx) * gravityMultiplier;
                    tmpgravity = GameModePhysics.BALL_GRAVITY(baseTableIdx) * gravityMultiplier;
                    
                    ballSwitched[0] = true;
                    playerVelY_fixed = GameModePhysics.BALL_SWITCH_VEL(currplayer_table_idx);
                    
                    // Skip collision checks for 2 frames after flip to prevent stutter
                    ballFlipCooldown = 2;
                    // Cache grounded state from flip frame to use during cooldown
                    ballWasGroundedBeforeFlip = true;
                }
                
                if (ballSwitched[0] && pressCount == 0) {
                    ballSwitched[0] = false;
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
                int hitboxW = (currplayer_mini != 0) ? 8 : 15;
                int hitboxH = (currplayer_mini != 0) ? 8 : 15;
                int hitboxOffsetY = (currplayer_mini != 0 && currplayer_gravity == 0) ? 8 : 0;
                int collisionX = (playerX_fixed >> 8);
                
                if (currplayer_gravity != 0) {
                    // Inverted gravity - prevent velocity from pulling into ceiling
                    int testY = (playerY_fixed >> 8) + hitboxOffsetY - 1;
                    var (collided, _) = CheckCollisionUp(collisionX, testY, hitboxW, hitboxH);
                    
                    // Only zero velocity if moving UP (negative Y velocity) into ceiling
                    if (collided && playerVelY_fixed < 0) {
                        playerVelY_fixed = 0;
                        AppendSimDebug($"[BALL] Ceiling grounded - zeroed upward velocity");
                    }
                } else {
                    // Normal gravity - prevent velocity from pulling into ground
                    int playerBottom = (playerY_fixed >> 8) + hitboxOffsetY + hitboxH;
                    int testTop = playerBottom;
                    int testHeight = 2;
                    var (collided, _) = CheckCollisionDown(collisionX, testTop, hitboxW, testHeight);
                    
                    // Only zero velocity if moving DOWN (positive Y velocity) into ground
                    if (collided && playerVelY_fixed > 0) {
                        playerVelY_fixed = 0;
                        AppendSimDebug($"[BALL] Floor grounded - zeroed downward velocity");
                    }
                }
            }
            
            // Collision ejection (only runs when cooldown == 0)
            BallEject_Fresh();
            
            // Swing gravity flip logic
            if (currentGameMode == 7) { // GAMEMODE_SWING
                int pressCount = Interlocked.Exchange(ref keyXPressedCount, 0);
                bool pressedJump = pressCount > 0;
                
                if (pressedJump && !ufoOrbed) {
                    InvertGravity_Fresh();
                    UpdateCurrplayerTableIdx_Fresh(); // Must update table_idx AFTER gravity flip!
                    // Swing does NOT apply velocity like Ball does - just flips gravity
                }
            }
            
            // Record position for trail
            try
            {
                int playerWorldCenterX_px = (playerX_fixed >> 8) + (playerVisualWidth / 2);
                int hitboxOffsetY = (currplayer_mini != 0 && currplayer_gravity == 0) ? 8 : 0;
                int playerWorldCenterY_px = (playerY_fixed >> 8) + (playerVisualHeight / 2) + hitboxOffsetY;
                recordedPlayerPath.Add((playerWorldCenterX_px, playerWorldCenterY_px));
            }
            catch { }
        }
        
        /// <summary>
        /// ball_eject() helper
        /// </summary>
        private void BallEject_Fresh()
        {
            int hitboxW = (currplayer_mini != 0) ? 8 : 15;
            int hitboxH = (currplayer_mini != 0) ? 8 : 15;
            int hitboxOffsetY = (currplayer_mini != 0 && currplayer_gravity == 0) ? 8 : 0;
            int collisionX = (playerX_fixed >> 8);
            int collisionY = (playerY_fixed >> 8) + hitboxOffsetY;
            
            if (currplayer_gravity == 0) {
                // Normal gravity: Check TOP collision with right-side pixel test
                if (!MainWindow.Option_NoDeath)
                {
                    var (topCollided, collisionBottomY) = CheckCollisionUp(collisionX, collisionY, hitboxW, hitboxH);
                    if (topCollided)
                    {
                        // Check right-side pixel (right edge X, upper portion Y)
                        int rightX_px = collisionX + hitboxW - 1;
                        int upperY_px = collisionY + (hitboxH / 3);
                        int groundRowsToReserve = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                        
                        bool rightPixelBlocked = CheckPixelCollision(rightX_px, upperY_px, groundRowsToReserve);
                        
                        if (rightPixelBlocked)
                        {
                            // Right pixel hit - DEATH
                            AppendSimDebug($"[DEATH] Ball top right pixel collision at ({rightX_px},{upperY_px})");
                            deathTriggered = true;
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
                                        try { mw.AddDeathMarker(rightX_px, upperY_px); } catch { }
                                    }
                                }));
                            }
                            catch { }
                            return;  // Don't do eject if death triggered
                        }
                    }
                }
                
                // Skip downward collision if we just flipped and are moving up
                if (playerVelY_fixed >= 0) {
                    var (collided, collisionTopY) = CheckCollisionDown(collisionX, collisionY, hitboxW, hitboxH);
                    if (collided) {
                        // Calculate where player Y should be so that bottom lands correctly
                        // Player bottom = playerY + hitboxOffsetY + hitboxH
                        // We want: playerY + hitboxOffsetY + hitboxH = collisionTopY - 1
                        // So: playerY = collisionTopY - 1 - hitboxOffsetY - hitboxH
                        int newY = collisionTopY - hitboxH - hitboxOffsetY - 1;
                        int oldY = playerY_fixed >> 8;
                        AppendSimDebug($"[BALL_EJECT_D] collisionTopY={collisionTopY}, hitboxH={hitboxH}, hitboxOffsetY={hitboxOffsetY}, oldY={oldY}, newY={newY}");
                        playerY_fixed = (newY << 8);
                        playerVelY_fixed = 0;
                    }
                }
            } else {
                // Inverted gravity: Check BOTTOM collision with right-side pixel test
                if (!MainWindow.Option_NoDeath)
                {
                    var (bottomCollided, collisionTopY) = CheckCollisionDown(collisionX, collisionY, hitboxW, hitboxH);
                    if (bottomCollided)
                    {
                        // Check right-side pixel (right edge X, lower portion Y)
                        int rightX_px = collisionX + hitboxW - 1;
                        int lowerY_px = collisionY + (hitboxH * 2 / 3);
                        int groundRowsToReserve = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                        
                        bool rightPixelBlocked = CheckPixelCollision(rightX_px, lowerY_px, groundRowsToReserve);
                        
                        if (rightPixelBlocked)
                        {
                            // Right pixel hit - DEATH
                            AppendSimDebug($"[DEATH] Ball bottom right pixel collision at ({rightX_px},{lowerY_px})");
                            deathTriggered = true;
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
                                        try { mw.AddDeathMarker(rightX_px, lowerY_px); } catch { }
                                    }
                                }));
                            }
                            catch { }
                            return;  // Don't do eject if death triggered
                        }
                    }
                }
                
                // Skip upward collision if we just flipped and are moving down
                if (playerVelY_fixed <= 0) {
                    var (collided, collisionBottomY) = CheckCollisionUp(collisionX, collisionY, hitboxW, hitboxH);
                    if (collided) {
                        // Place player directly at collision surface
                        int newY = collisionBottomY - hitboxOffsetY;
                        int oldY = playerY_fixed >> 8;
                        AppendSimDebug($"[BALL_EJECT_U] collisionBottomY={collisionBottomY}, hitboxOffsetY={hitboxOffsetY}, oldY={oldY}, newY={newY}");
                        playerY_fixed = (newY << 8);
                        playerVelY_fixed = 0;
                    }
                }
            }
        }
        
        /// <summary>
        /// Helper to invert gravity
        /// </summary>
        private void InvertGravity_Fresh()
        {
            currplayer_gravity = (byte)(currplayer_gravity == 0 ? 0xFF : 0);
            gravityReversed = (currplayer_gravity != 0);
            gravityFlipped = (currplayer_gravity != 0);
            UpdateCurrplayerTableIdx_Fresh();
            UpdatePlayerIconFlip();
        }
        
        private void UpdateCurrplayerTableIdx_Fresh()
        {
            currplayer_table_idx = (currplayer_gravity != 0 ? 1 : 0) | (currplayer_mini != 0 ? 4 : 0);
        }
    }
}
