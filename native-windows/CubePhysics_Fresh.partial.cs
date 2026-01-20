using System;
using System.Threading;

namespace FamidashEditor
{
    public partial class SimulatorWindow
    {
        // ========================================================================
        // CUBE PHYSICS - Fresh 1:1 Port from cleaned gamemode_cube.h
        // ========================================================================

        // Physics constants from physics_table_defines.cmp.h
        // Table index 4 = normal size (mini=0), normal gravity (gravity=0), framerate=0
        private const int CUBE_GRAVITY_NORMAL = 0x6B;
        private const int CUBE_GRAVITY_MINI = 0x6F;
        private const int CUBE_MAX_FALLSPEED_NORMAL = 0x0600;
        private const int CUBE_MAX_FALLSPEED_MINI = 0x0600;
        private const int JUMP_VEL_NORMAL = -0x590;  // 0xFA70 as signed 16-bit
        private const int JUMP_VEL_MINI = -0x4D0;    // 0xFB30 as signed 16-bit
        
        // Cube hitbox dimensions (from physics_defines.h)
        private const int CUBE_HITBOX_W = 15;
        private const int CUBE_HITBOX_H = 15;
        private const int MINI_CUBE_HITBOX_W = 8;
        private const int MINI_CUBE_HITBOX_H = 8;  // Using 8 for centered bottom-left quadrant
        
        // State variables from famidash.h
        private byte currplayer_mini = 0;      // 0 = normal, 1 = mini
        private byte currplayer_gravity = 0;   // 0 = down, 0xFF = up
        
        // Temporary variables used in cube_movement()
        private int tmpgravity = 0;
        private int tmpfallspeed = 0;
        
        /// <summary>
        /// Main cube physics routine - matches cube_movement() from gamemode_cube.h
        /// </summary>
        private void ProcessCubePhysics_Fresh()
        {
            try
            {
                int groundRowsCalc = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                // AppendSimDebug($"[CUBE] Start: velY={playerVelY_fixed}, posY={playerY_fixed >> 8}, gravity={currplayer_gravity:X2}");
                
                // Set physics constants using table index
                // From gamemode_cube.h lines 16-22
                tmpfallspeed = GameModePhysics.CUBE_MAX_FALLSPEED(currplayer_table_idx, CUBE_MAX_FALLSPEED);
                tmpgravity = GameModePhysics.CUBE_GRAVITY(currplayer_table_idx);
                
                // AppendSimDebug($"[CUBE] Constants: table_idx={currplayer_table_idx}, gravity_const={tmpgravity:X}, fallspeed={tmpfallspeed:X}, grav_byte={currplayer_gravity:X2}");
                
                // STEP 1: Set physics values and apply gravity/collision (happens BEFORE jump check)
                // Set physics values for CommonGravityRoutine
                int baseTableIdx2 = (currplayer_mini != 0 ? 4 : 0);
                bool gravityInverted2 = (currplayer_gravity != 0);
                int gravityMultiplier2 = gravityInverted2 ? -1 : 1;
                tmpgravity = GameModePhysics.CUBE_GRAVITY(baseTableIdx2) * gravityMultiplier2;
                tmpfallspeed = GameModePhysics.CUBE_MAX_FALLSPEED(baseTableIdx2, CUBE_MAX_FALLSPEED) * gravityMultiplier2;
                
                // STEP 2: common_gravity_routine() - applies gravity and integrates velocity
                CommonGravityRoutine_Fresh();
                
                // If grounded with inverted gravity, prevent velocity from pulling into ceiling
                if (currplayer_gravity != 0) {
                    int hitboxW_check = (currplayer_mini != 0) ? 8 : 15;
                    int hitboxH_check = (currplayer_mini != 0) ? 8 : 15;
                    int hitboxOffsetY_check = (currplayer_mini != 0 && currplayer_gravity == 0) ? 8 : 0;
                    int collisionX_check = (playerX_fixed >> 8);
                    int testY_check = (playerY_fixed >> 8) + hitboxOffsetY_check - 1;
                    var (collided_check, _) = CheckCollisionUp(collisionX_check, testY_check, hitboxW_check, hitboxH_check);
                    
                    if (collided_check && playerVelY_fixed < 0) {
                        playerVelY_fixed = 0;
                        // AppendSimDebug($"[CUBE] Ceiling grounded - zeroed velocity");
                    }
                }
                
                // AppendSimDebug($"[CUBE] After gravity: velY={playerVelY_fixed}, posY={playerY_fixed >> 8}");
                
                // STEP 3: cube_eject() - collision detection and ejection
                CubeEject_Fresh();
                
                // AppendSimDebug($"[CUBE] After collision: velY={playerVelY_fixed}, posY={playerY_fixed >> 8}");
                
                // STEP 4: Jump input check (AFTER gravity and collision, matching famidash order)
                // This happens after position update, so on jump frame:
                // - Frame 1: gravity applied (0), position updated (0), then jump sets velocity
                // - Frame 2+: gravity applied to jump velocity, position moves
                if (playerVelY_fixed == 0)
                {
                    // Read input - PEEK first, don't consume yet
                    bool holdJump = IsXDownAsync() || keyXHeld;
                    int pressCount = Interlocked.CompareExchange(ref keyXPressedCount, 0, 0);
                    bool pressJump = pressCount > 0;
                    
                    // AppendSimDebug($"[CUBE] Input check: hold={holdJump}, press={pressJump}, pressCount={pressCount}");
                    
                    // Two jump paths from gamemode_cube.h:
                    // Path 1 (lines 81-91): Hold A to buffer jump (no jblocked/fblocked)
                    // Path 2 (lines 92-102): Press A for immediate jump (with jblocked/fblocked)
                    // For now, simplified: either hold or press allows jump
                    
                    // Use tolerance for grounded check (check if at rest)
                    bool isGrounded = (playerVelY_fixed >= -16 && playerVelY_fixed <= 16);
                    
                    if ((holdJump || pressJump) && isGrounded)
                    {
                        AppendSimDebug($"[CUBE] JUMP TRIGGERED!");
                        
                        // Consume the press now that we're using it for jump
                        Interlocked.Exchange(ref keyXPressedCount, 0);
                        
                        // Get base physics value (always from down-gravity index)
                        int baseTableIdx = (currplayer_mini != 0 ? 4 : 0);
                        bool gravityInverted = (currplayer_gravity != 0);
                        int gravityMultiplier = gravityInverted ? -1 : 1;
                        
                        // From gamemode_cube.h: currplayer_vel_y = JUMP_VEL(currplayer_table_idx);
                        int jumpVel = GameModePhysics.JUMP_VEL(baseTableIdx) * gravityMultiplier;
                        playerVelY_fixed = jumpVel;
                        
                        AppendSimDebug($"[CUBE] Jump applied: table_idx={currplayer_table_idx}, jumpVel={jumpVel}, velY now = {playerVelY_fixed}");
                    }
                    
                    // Update orb hold suppression (X hold from ground jump shouldn't activate orbs)
                    UpdateOrbHoldSuppression(holdJump, isGrounded);
                }
                else
                {
                    AppendSimDebug($"[CUBE] Cannot jump: velY={playerVelY_fixed} (must be 0)");
                }
                
                // STEP 5: Check for orb activation (after jump and collision)
                {
                    bool holdJump = IsXDownAsync() || keyXHeld;
                    int pressCount = Interlocked.CompareExchange(ref keyXPressedCount, 0, 0); // Peek without consuming
                    bool pressJump = pressCount > 0;
                    bool gravityInverted = (currplayer_gravity != 0);
                    int playerX_px = playerX_fixed >> 8;
                    int playerY_px = playerY_fixed >> 8;
                    int hitboxW = (currplayer_mini != 0) ? MINI_CUBE_HITBOX_W : CUBE_HITBOX_W;
                    int hitboxH = (currplayer_mini != 0) ? MINI_CUBE_HITBOX_H : CUBE_HITBOX_H;
                    int scrollX_px = 0; // Cube mode doesn't scroll in this implementation
                    
                    int tempVelY = playerVelY_fixed;
                    var (orbActivated, _) = UpdateOrbSystem(0, playerX_px, playerY_px, hitboxW, hitboxH, 
                                                       scrollX_px, pressJump, holdJump, gravityInverted, 
                                                       (currplayer_mini != 0), ref tempVelY);
                    if (orbActivated)
                    {
                        playerVelY_fixed = tempVelY;
                        AppendSimDebug($"[CUBE] Orb activated! New velY={playerVelY_fixed}");
                        
                        // Consume the X press if it was used for orb
                        if (pressJump)
                            Interlocked.Exchange(ref keyXPressedCount, 0);
                    }
                    
                    // Clear orb buffer when X is released
                    if (!holdJump)
                        ClearOrbBuffer();
                }
                
                // STEP 6: Update slope counters (decrement each frame)
                UpdateSlopeCounters_Fresh();
                
                // Record position for trail AFTER physics completes (for smooth visualization)
                try
                {
                    int playerWorldCenterX_px = (playerX_fixed >> 8) + (playerVisualWidth / 2);
                    int playerWorldCenterY_px = (playerY_fixed >> 8) + (playerVisualHeight / 2);
                    
                    // Move path down 8 pixels when mini and gravity is normal
                    bool isMini = (currplayer_mini != 0);
                    bool gravityInverted = (currplayer_gravity != 0);
                    if (isMini && !gravityInverted)
                    {
                        playerWorldCenterY_px += 8;
                    }
                    
                    recordedPlayerPath.Add((playerWorldCenterX_px, playerWorldCenterY_px));
                }
                catch { }
            }
            catch (Exception ex)
            {
                try { AppendSimDebug($"ProcessCubePhysics_Fresh error: {ex.Message}"); } catch { }
            }
        }
        
        /// <summary>
        /// common_gravity_routine() from gamemode_cube.h lines 177-206
        /// Applies gravity and integrates velocity into position
        /// </summary>
        private void CommonGravityRoutine_Fresh()
        {
            // From gamemode_cube.h line 178: register int16_t tmpaccel;
            int tmpaccel;
            
            // From gamemode_cube.h line 179: tmp1 = dashing[currplayer];
            // We don't have dashing, so assume tmp1 = 0 (not dashing)
            int tmp1 = 0;
            
            if (tmp1 == 0)
            {
                // From gamemode_cube.h line 181: tmpaccel = tmpgravity;
                tmpaccel = tmpgravity;
                
                // From gamemode_cube.h line 182-184: check if at max fall speed
                // if((!currplayer_gravity ? currplayer_vel_y > tmpfallspeed : currplayer_vel_y < tmpfallspeed))
                bool atMaxFallSpeed;
                if (currplayer_gravity == 0)
                {
                    // Normal gravity: falling down is positive velocity
                    atMaxFallSpeed = (playerVelY_fixed > tmpfallspeed);
                }
                else
                {
                    // Reversed gravity: falling up is negative velocity  
                    atMaxFallSpeed = (playerVelY_fixed < tmpfallspeed);
                }
                
                if (atMaxFallSpeed)
                {
                    // From gamemode_cube.h line 185: tmpaccel = -tmpaccel;
                    tmpaccel = -tmpaccel;
                }
                
                // gravity_mod handling (lines 186-192) - skip for now, assume gravity_mod = 0
                
                // From gamemode_cube.h line 194: currplayer_vel_y += tmpaccel;
                playerVelY_fixed += (int)Math.Round(tmpaccel * simTimeScale);
            }
            // Other dashing cases (lines 195-203) skipped
            
            // From gamemode_cube.h line 205: currplayer_y += currplayer_vel_y;
            playerY_fixed += (int)Math.Round(playerVelY_fixed * simTimeScale);
            
            // Clamp to world bounds
            if (playerY_fixed < 0) playerY_fixed = 0;
            int maxPlayerY_fixed = Math.Max(0, (mapHeight * TILE - playerVisualHeight)) << 8;
            if (playerY_fixed > maxPlayerY_fixed) playerY_fixed = maxPlayerY_fixed;
        }
        
        /// <summary>
        /// cube_eject() from gamemode_cube.h lines 209-254
        /// Collision detection and position/velocity correction
        /// </summary>
        private void CubeEject_Fresh()
        {
            // Calculate hitbox in pixels
            int playerX_px = playerX_fixed >> 8;
            int playerY_px = playerY_fixed >> 8;
            
            // Dynamic hitbox size based on mini mode
            int hitboxW = (currplayer_mini != 0) ? MINI_CUBE_HITBOX_W : CUBE_HITBOX_W;
            int hitboxH = (currplayer_mini != 0) ? MINI_CUBE_HITBOX_H : CUBE_HITBOX_H;
            
            // For mini mode, position hitbox in bottom-left quadrant (8 pixels down)
            // This matches the visual offset applied during rendering
            int hitboxOffsetX = 0;  // No X offset, left-aligned
            int hitboxOffsetY = (currplayer_mini != 0) ? 8 : 0;  // 8 pixels down to bottom-left quadrant
            
            int collisionX = playerX_px + hitboxOffsetX;
            int collisionY = playerY_px + hitboxOffsetY;
            
            // Update slope counters
            UpdateSlopeCounters();
            
            // Cube collision detection based on gravity direction
            if (currplayer_gravity == 0)
            {
                // Normal gravity: check slopes first (from collision.h line 920)
                bool slopeHit = bg_coll_D_slopes();
                // AppendSimDebug($"[CUBE]   Slope check result: slopeHit={slopeHit}, eject_D={eject_D}, counter={currplayer_was_on_slope_counter}");
                if (slopeHit)
                {
                    // Slope collision succeeded
                    if (eject_D > 0)
                    {
                        // Apply slope ejection
                        // From gamemode_cube.h line 236-240:
                        // high_byte(currplayer_y) -= eject_D;
                        // low_byte(currplayer_y) = 0;
                        // This subtracts eject_D from pixel position and clears subpixels
                        int oldY = playerY_fixed >> 8;
                        // AppendSimDebug($"[CUBE]   BEFORE slope eject: playerY_fixed={playerY_fixed}, oldY={oldY}");
                        
                        // Get current pixel position, subtract eject_D, then clear subpixels
                        int newPixelY = (playerY_fixed >> 8) - eject_D;
                        playerY_fixed = newPixelY << 8;  // Clear subpixels by shifting back
                        playerVelY_fixed = 0;
                        
                        // AppendSimDebug($"[CUBE]   AFTER slope eject: eject_D={eject_D}, oldY={oldY}, newY={newPixelY}, playerY_fixed={playerY_fixed}");
                        
                        // CRITICAL FIX: Update playerY_px after slope ejection so subsequent code uses correct position
                        playerY_px = newPixelY;
                    }
                    else
                    {
                        // eject_D=0 means player is at correct height, just stop velocity
                        // AppendSimDebug($"[CUBE]   Slope hit with eject_D=0 - player at correct height");
                        playerVelY_fixed = 0;
                    }
                }
                else
                {
                    // Slope check failed - fall back to flat collision
                    // This matches collision.h lines 946-968
                    // AppendSimDebug($"[CUBE]   Falling back to flat collision (slopeHit={slopeHit}, eject_D={eject_D})");
                    // AppendSimDebug($"[CUBE]   Checking from: playerX={playerX_px}, playerY={playerY_px}, velY={playerVelY_fixed}");
                    
                    // Normal gravity: Bottom is for landing (always collide), top can passthrough
                    // Check downward collision for landing
                    var (collided, collisionTopY) = CheckCollisionDown(collisionX, collisionY, hitboxW, hitboxH);
                    // AppendSimDebug($"[CUBE]   Collision check down: collided={collided}, collisionTop={collisionTopY}");
                    if (collided)
                    {
                        // Only snap if falling/stationary (velY >= 0). Don't snap while jumping up (velY < 0).
                        // This prevents snapping onto higher floors during upward jump arc.
                        if (playerVelY_fixed >= 0)
                        {
                            // Snap player to rest position above the collision surface
                            int newY = collisionTopY - hitboxH - 1 - hitboxOffsetY;
                            // AppendSimDebug($"[CUBE]     Eject down: collisionTop={collisionTopY}, newY={newY} (was {playerY_px})");
                            playerY_fixed = newY << 8;
                            playerVelY_fixed = 0;
                        }
                        else
                        {
                            // AppendSimDebug($"[CUBE]     Collision detected but NOT snapping (jumping up, velY={playerVelY_fixed})");
                        }
                    }
                    else
                    {
                        // AppendSimDebug($"[CUBE]     NO COLLISION - falling! Y={playerY_px}");
                    }
                }
                
                // Normal gravity: Check TOP collision with passthrough/death for Cube/Robot/Ninja
                if ((currentGameMode == 0 || currentGameMode == 4 || currentGameMode == 8) && !MainWindow.Option_NoDeath)
                {
                    var (topCollided, collisionBottomY) = CheckCollisionUp(collisionX, collisionY, hitboxW, hitboxH);
                    if (topCollided)
                    {
                        // Check right-side pixel of player (right edge horizontally, upper portion vertically)
                        // Use right edge X, and Y + hitboxH/3 to catch COL_TOP (collision in top 8px)
                        int rightX_px = collisionX + hitboxW - 1;  // Right edge of hitbox
                        int upperY_px = collisionY + (hitboxH / 3);  // Upper third
                        int groundRowsToReserve = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                        
                        bool rightPixelBlocked = CheckPixelCollision(rightX_px, upperY_px, groundRowsToReserve);
                        AppendSimDebug($"[CUBE] Top collision check: rightX={rightX_px}, upperY={upperY_px}, blocked={rightPixelBlocked}, collisionY={collisionY}, hitboxH={hitboxH}");
                        
                        if (rightPixelBlocked)
                        {
                            // Center pixel hit - DEATH
                            AppendSimDebug($"[DEATH] Top right pixel collision at ({rightX_px},{upperY_px})");
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
                        }
                        else
                        {
                            // Center is clear - allow passthrough (no collision)
                            AppendSimDebug($"[CUBE]   Top center clear - passthrough allowed");
                        }
                    }
                }
                else if ((currentGameMode == 0 || currentGameMode == 4 || currentGameMode == 8) && MainWindow.Option_NoDeath)
                {
                    // NO DEATH mode: eject from top collision
                    var (topCollided, collisionBottomY) = CheckCollisionUp(collisionX, collisionY, hitboxW, hitboxH);
                    if (topCollided)
                    {
                        int newY = collisionBottomY - hitboxOffsetY;
                        AppendSimDebug($"[CUBE]     Eject up (NO DEATH): collisionBottom={collisionBottomY}, newY={newY} (was {playerY_px})");
                        playerY_fixed = newY << 8;
                        playerVelY_fixed = 0;
                    }
                }
            }
            else
            {
                // Reversed gravity: Top is for landing (always collide), bottom can passthrough
                // Check upward collision for landing (only when moving toward ceiling or grounded)
                if (playerVelY_fixed <= 0) // Moving toward ceiling or stationary
                {
                    var (collided, collisionBottomY) = CheckCollisionUp(collisionX, collisionY, hitboxW, hitboxH);
                    if (collided)
                    {
                        // Snap player to rest position below the collision surface
                        int newY = collisionBottomY - hitboxOffsetY;
                        AppendSimDebug($"[CUBE]     Eject up: collisionBottom={collisionBottomY}, newY={newY} (was {playerY_px})");
                        playerY_fixed = newY << 8;
                        playerVelY_fixed = 0;
                    }
                }
                
                // Reversed gravity: Check BOTTOM collision with passthrough/death for Cube/Robot/Ninja
                if ((currentGameMode == 0 || currentGameMode == 4 || currentGameMode == 8) && !MainWindow.Option_NoDeath)
                {
                    var (bottomCollided, collisionTopY) = CheckCollisionDown(collisionX, collisionY, hitboxW, hitboxH);
                    if (bottomCollided)
                    {
                        // Check right-side pixel of player (right edge horizontally, lower portion vertically)
                        // Use right edge X, and Y + hitboxH*2/3 to catch COL_BOTTOM (collision in bottom 8px)
                        int rightX_px = collisionX + hitboxW - 1;  // Right edge of hitbox
                        int lowerY_px = collisionY + (hitboxH * 2 / 3);  // Lower two-thirds
                        int groundRowsToReserve = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                        
                        bool rightPixelBlocked = CheckPixelCollision(rightX_px, lowerY_px, groundRowsToReserve);
                        AppendSimDebug($"[CUBE] Bottom collision check: rightX={rightX_px}, lowerY={lowerY_px}, blocked={rightPixelBlocked}, collisionY={collisionY}, hitboxH={hitboxH}");
                        
                        if (rightPixelBlocked)
                        {
                            // Center pixel hit - DEATH
                            AppendSimDebug($"[DEATH] Bottom right pixel collision at ({rightX_px},{lowerY_px})");
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
                        }
                        else
                        {
                            // Center is clear - allow passthrough (no collision)
                            AppendSimDebug($"[CUBE]   Bottom center clear - passthrough allowed");
                        }
                    }
                }
                else if ((currentGameMode == 0 || currentGameMode == 4 || currentGameMode == 8) && MainWindow.Option_NoDeath)
                {
                    // NO DEATH mode: eject from bottom collision
                    var (bottomCollided, collisionTopY) = CheckCollisionDown(collisionX, collisionY, hitboxW, hitboxH);
                    if (bottomCollided)
                    {
                        int newY = collisionTopY - hitboxH - 1 - hitboxOffsetY;
                        AppendSimDebug($"[CUBE]     Eject down (NO DEATH): collisionTop={collisionTopY}, newY={newY} (was {playerY_px})");
                        playerY_fixed = newY << 8;
                        playerVelY_fixed = 0;
                    }
                }
            }
        }
        
        /// <summary>
        /// Initialize cube physics state
        /// </summary>
        private void EnableCubePhysics_Fresh()
        {
            try
            {
                currplayer_mini = 0;      // Normal size
                currplayer_gravity = 0;   // Normal gravity (down)
            }
            catch (Exception ex)
            {
                try { AppendSimDebug($"EnableCubePhysics_Fresh error: {ex.Message}"); } catch { }
            }
        }
    }
}
