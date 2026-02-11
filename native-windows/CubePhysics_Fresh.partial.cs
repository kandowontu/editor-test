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
        private const int MINI_CUBE_HITBOX_H = 7;  // Correct: 8x7 for mini mode
        
        // State variables from famidash.h
        private byte currplayer_mini = 0;      // 0 = normal, 1 = mini
        private byte currplayer_gravity = 0;   // 0 = down, 0xFF = up
        
        // Track if we zeroed velocity due to ground collision (for preventing gravity oscillation)
        private bool wasZeroedByCollisionLastFrame = false;
        
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
                AppendSimDebug($"[CUBE_START] posY=0x{playerY_fixed:X4} ({playerY_fixed >> 8}px), velY=0x{playerVelY_fixed:X4}, gravity=0x{currplayer_gravity:X2}, mini={currplayer_mini}");
                
                // STEP 0: Check for orb activation FIRST (before any input consumption)
                {
                    bool holdJump_orb = IsXDownAsync() || keyXHeld;
                    int pressCount_orb = Interlocked.CompareExchange(ref keyXPressedCount, 0, 0);
                    bool pressJump_orb = pressCount_orb > 0;
                    bool gravityInverted_orb = (currplayer_gravity != 0);
                    int playerX_px_orb = playerX_fixed >> 8;
                    int playerY_px_orb = playerY_fixed >> 8;
                    int hitboxW_orb = (currplayer_mini != 0) ? MINI_CUBE_HITBOX_W : CUBE_HITBOX_W;
                    int hitboxH_orb = (currplayer_mini != 0) ? MINI_CUBE_HITBOX_H : CUBE_HITBOX_H;
                    
                    // Adjust Y position for mini mode collision box (bottom-left alignment)
                    if (currplayer_mini != 0 && !gravityInverted_orb)
                    {
                        playerY_px_orb += 9;
                    }
                    
                    int scrollX_px_orb = 0;
                    
                    int tempVelY = playerVelY_fixed;
                    var (orbActivated, _) = UpdateOrbSystem(0, playerX_px_orb, playerY_px_orb, hitboxW_orb, hitboxH_orb, 
                                                       scrollX_px_orb, pressJump_orb, holdJump_orb, gravityInverted_orb, 
                                                       (currplayer_mini != 0), ref tempVelY);
                    if (orbActivated)
                    {
                        playerVelY_fixed = tempVelY;
                        
                        // Consume the X press if it was used for orb
                        if (pressJump_orb)
                            Interlocked.Exchange(ref keyXPressedCount, 0);
                    }
                    
                    // Clear orb buffer when X is released
                    if (!holdJump_orb)
                        ClearOrbBuffer();
                }
                
                // Save grounded state BEFORE gravity is applied (for Football release)
                bool wasGroundedAtFrameStart = (playerVelY_fixed >= -16 && playerVelY_fixed <= 16);
                
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
                
                AppendSimDebug($"[CUBE_PRE_GRAV] tmpgravity=0x{tmpgravity:X}, tmpfallspeed=0x{tmpfallspeed:X}");
                
                // STEP 2: common_gravity_routine() - applies gravity and integrates velocity
                CommonGravityRoutine_Fresh();
                
                // If grounded with inverted gravity, prevent velocity from pulling into ceiling
                if (currplayer_gravity != 0) {
                    bool isMini_check = (currplayer_mini != 0);
                    int hitboxW_check = isMini_check ? 8 : 15;
                    int hitboxH_check = isMini_check ? 7 : 15;
                    int hitboxOffsetY_check = isMini_check ? ((0x10 - hitboxH_check) >> 1) : 0;
                    int collisionX_check = (playerX_fixed >> 8);
                    int testY_check = (playerY_fixed >> 8) + hitboxOffsetY_check - 1;
                    var (collided_check, _) = CheckCollisionUp(collisionX_check, testY_check, hitboxW_check, hitboxH_check);
                    
                    if (collided_check && playerVelY_fixed < 0) {
                        playerVelY_fixed = 0;
                        // AppendSimDebug($"[CUBE] Ceiling grounded - zeroed velocity");
                    }
                }
                
                // AppendSimDebug($"[CUBE] After gravity: velY={playerVelY_fixed}, posY={playerY_fixed >> 8}");
                
                // Check center-point death (matches bg_coll_death() in collision.h line 1019)
                CheckCenterPointDeath_Fresh();
                
                // STEP 3: cube_eject() - collision detection and ejection
                CubeEject_Fresh();
                
                // AppendSimDebug($"[CUBE] After collision: velY={playerVelY_fixed}, posY={playerY_fixed >> 8}");
                
                // STEP 4: Jump input check (AFTER gravity and collision, matching famidash order)
                // This happens after position update, so on jump frame:
                // - Frame 1: gravity applied (0), position updated (0), then jump sets velocity
                // - Frame 2+: gravity applied to jump velocity, position moves
                if (playerVelY_fixed == 0)
                {
                    // NORMAL CUBE JUMP (if NOT Football mode)
                    if (currentGameMode != 11) // NOT GAMEMODE_FOOTBALL
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
                        
                        // Check gamemode_cube.h line 70: dashing == 0
                        if ((holdJump || pressJump) && isGrounded && !orbed[currplayer] && dashing[currplayer] == 0)
                        {
                            // AppendSimDebug($"[CUBE] JUMP TRIGGERED!");
                            
                            // Consume the press now that we're using it for jump
                            Interlocked.Exchange(ref keyXPressedCount, 0);
                            
                            // Get base physics value (always from down-gravity index)
                            int baseTableIdx = (currplayer_mini != 0 ? 4 : 0);
                            bool gravityInverted = (currplayer_gravity != 0);
                            int gravityMultiplier = gravityInverted ? -1 : 1;
                            
                            // From gamemode_cube.h: currplayer_vel_y = JUMP_VEL(currplayer_table_idx);
                            int jumpVel = GameModePhysics.JUMP_VEL(baseTableIdx) * gravityMultiplier;
                            playerVelY_fixed = jumpVel;
                            
                            // AppendSimDebug($"[CUBE] Jump applied: table_idx={currplayer_table_idx}, jumpVel={jumpVel}, velY now = {playerVelY_fixed}");
                        }
                        
                        // Update orb hold suppression (X hold from ground jump shouldn't activate orbs)
                        UpdateOrbHoldSuppression(holdJump, isGrounded);
                    }
                }
                else
                {
                    // AppendSimDebug($"[CUBE] Cannot jump: velY={playerVelY_fixed} (must be 0)");
                }
                
                // STEP 4A: Football mode charging (gamemode_cube.h lines 140-143)
                // Charging happens every frame while holding X, even in mid-air for buffering
                if (currentGameMode == 11) // GAMEMODE_FOOTBALL
                {
                    bool holdX = IsXDownAsync() || keyXHeld;
                    
                    // Charging phase: increment chargepower while holding X
                    if (holdX && !orbed[currplayer])
                    {
                        chargepower[0]++;
                        
                        // Handle overcharge: if chargepower >= 45, reset and set orbed flag
                        if (chargepower[0] >= 45)
                        {
                            chargepower[0] = 0;
                            orbed[currplayer] = true;
                        }
                        
                        AppendSimDebug($"[FOOTBALL] Charging: chargepower={chargepower[0]}, orbed={orbed[currplayer]}");
                    }
                }
                
                // STEP 4B: Football release (gamemode_cube.h lines 145-155)
                // This is a SEPARATE conditional that runs regardless (not nested in velY==0)
                if (currentGameMode == 11) // GAMEMODE_FOOTBALL
                {
                    bool holdX = IsXDownAsync() || keyXHeld;
                    
                    // Release phase: happens when NOT holding X AND grounded
                    if (!holdX)
                    {
                        // From gamemode_cube.h: Save chargepower before clearing it
                        int tmp3 = chargepower[0];
                        AppendSimDebug($"[FOOTBALL] Release triggered: tmp3={tmp3}, holdX={holdX}");
                        
                        // Clear orbed flag on release
                        orbed[currplayer] = false;
                        
                        // Calculate jump velocity using chargepower (from gamemode_cube.h line 154)
                        // tmpA = chargepower * (currplayer_gravity ? 0x004C : -0x004C)
                        int baseTableIdx_fb = (currplayer_mini != 0 ? 4 : 0);
                        bool gravityInverted_fb = (currplayer_gravity != 0);
                        int gravityMultiplier_fb = gravityInverted_fb ? 1 : -1;
                        
                        int tmpA = tmp3 * (0x004C * gravityMultiplier_fb);
                        
                        // Apply velocity if chargepower > 0 and player velocity is zero or small (grounded state)
                        // From gamemode_cube.h: if (chargepower && currplayer_vel_y == 0)
                        // But we also allow release when velocity is small (just started falling) to handle walk-off-edge case
                        bool isGroundedOrNearGround = (playerVelY_fixed == 0) || 
                                                      (!gravityInverted_fb && playerVelY_fixed > 0 && playerVelY_fixed < 0x100) ||
                                                      (gravityInverted_fb && playerVelY_fixed < 0 && playerVelY_fixed > -0x100);
                        
                        if (tmp3 > 0 && isGroundedOrNearGround)
                        {
                            playerVelY_fixed = tmpA;
                            AppendSimDebug($"[FOOTBALL] Released! chargepower={tmp3}, tmpA=0x{tmpA:X4}, velY now = {playerVelY_fixed}");
                        }
                        
                        // Reset chargepower on release (gamemode_cube.h line 154)
                        chargepower[0] = 0;
                    }
                }
                
                // STEP 5: Update slope counters (decrement each frame)
                UpdateSlopeCounters_Fresh();
                
                // Record position for trail AFTER physics completes (for smooth visualization)
                try
                {
                    int playerWorldCenterX_px = (playerX_fixed >> 8) + (playerVisualWidth / 2);
                    int playerWorldCenterY_px = (playerY_fixed >> 8) + (playerVisualHeight / 2);
                    
                    // Apply mini mode offset for visual position consistency
                    // Mini mode: 8x7 hitbox at Y+9 (normal gravity) or Y+0 (inverted)
                    // So visual position is 8 pixels down from physics position in normal gravity
                    bool isMini = (currplayer_mini != 0);
                    bool gravityInverted = (currplayer_gravity != 0);
                    if (isMini && !gravityInverted)
                    {
                        // Mini normal: hitbox at +9, center of visual is at Y+8+4=12, so add 4 more to 8 base
                        playerWorldCenterY_px += 4;
                    }
                    else if (isMini && gravityInverted)
                    {
                        // Mini inverted: hitbox at top, center already accounts for this
                        // No additional offset needed
                    }
                    
                    // Record to appropriate path list based on which player is active
                    if (currplayer == 0)
                        recordedPlayerPath.Add((playerWorldCenterX_px, playerWorldCenterY_px));
                    else if (dual)
                        recordedPlayer2Path.Add((playerWorldCenterX_px, playerWorldCenterY_px));
                }
                catch { }
                
                // CRITICAL: After all physics updates, verify that cube still has ground support
                // This ensures that walking off a platform immediately triggers falling
                // rather than waiting for the next frame's main loop check
                if (onGround && playerVelY_fixed == 0)
                {
                    try
                    {
                        bool stillSupported = false;
                        if (currplayer_gravity == 0)  // Normal gravity
                        {
                            const int HITBOX_W_LOCAL = 15;
                            int playerCenter_px = (playerX_fixed >> 8) + (playerVisualWidth / 2);
                            int playerLeft_px = playerCenter_px - (HITBOX_W_LOCAL / 2);
                            int playerRight_px = playerLeft_px + (HITBOX_W_LOCAL - 1);
                            int footWorldY_px = (playerY_fixed >> 8) + playerVisualHeight - 1;
                            int tileBelowY_world = footWorldY_px / TILE;
                            int groundRowsToReserve_local = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                            int tileIndexY = tileBelowY_world + groundRowsToReserve_local;
                            if (tileIndexY >= mapHeight)
                            {
                                // Player is over the implicit ground layer — always supported
                                stillSupported = true;
                            }
                            else if (tileIndexY >= 0)
                            {
                                for (int tx = playerLeft_px / TILE; tx <= playerRight_px / TILE; tx++)
                                {
                                    if (tx < 0 || tx >= mapWidth) continue;
                                    int tid = tiles[tileIndexY * mapWidth + tx];
                                    var col = MetatileCollisionTable.GetCollision((byte)tid);
                                    int tileStartX = tx * TILE;
                                    int localX = Math.Max(0, Math.Min(TILE - 1, playerCenter_px - tileStartX));
                                    if (ProvidesFloorAtColumnStatic(col, localX, out int _)) { stillSupported = true; break; }
                                }
                            }
                        }
                        else  // Inverted gravity
                        {
                            stillSupported = IsTouchingCeiling();
                        }

                        if (!stillSupported)
                        {
                            onGround = false;
                            wasZeroedByCollisionLastFrame = false;  // Allow gravity to apply next frame
                            AppendSimDebug($"[CUBE] Ground support lost - clearing onGround flag and collision flag");
                        }
                    }
                    catch { onGround = false; }
                }
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
            // From gamemode_cube.h line 247: register int16_t tmpaccel;
            int tmpaccel;
            
            AppendSimDebug($"[GRAV_START] velY=0x{playerVelY_fixed:X4}, posY=0x{playerY_fixed:X4} ({playerY_fixed >> 8}px), dashing={dashing[currplayer]}, wasZeroedByCollision={wasZeroedByCollisionLastFrame}, mode={currentGameMode}");
            
            // From gamemode_cube.h line 249: tmp1 = dashing;
            int tmp1 = dashing[currplayer];
            
            // CRITICAL FIX: Don't apply gravity if we were just zeroed by collision detection
            // This prevents oscillation where gravity re-applies to already-grounded players
            // EXCEPTION: Pogo mode needs gravity even when grounded so it can bounce
            // Only reset the flag when velocity becomes non-zero (player leaves ground)
            if (playerVelY_fixed == 0 && wasZeroedByCollisionLastFrame && currentGameMode != 9)
            {
                // We're grounded - skip gravity application (but NOT for Pogo mode)
                AppendSimDebug($"[GRAV_SKIP] velY==0 && wasZeroedByCollision=true - skipping gravity, velocity stays at 0");
                return;
            }
            
            // Reset the flag if velocity is non-zero (means player left the ground)
            if (playerVelY_fixed != 0)
            {
                wasZeroedByCollisionLastFrame = false;
                AppendSimDebug($"[GRAV_RESET_FLAG] velY is non-zero, resetting collision flag for next frame");
            }
            
            if (tmp1 == 0)
            {
                // Not dashing: apply normal gravity normally
                // From gamemode_cube.h line 251: tmpaccel = tmpgravity;
                tmpaccel = tmpgravity;
                
                // From gamemode_cube.h line 252-254: check if at max fall speed
                // if((!currplayer_gravity ? currplayer_vel_y > tmpfallspeed : currplayer_vel_y < tmpfallspeed))
                bool atMaxFallSpeed;
                if (currplayer_gravity == 0)
                {
                    // Normal gravity: check if positive velocity exceeds negative fallspeed
                    atMaxFallSpeed = (playerVelY_fixed > tmpfallspeed);
                }
                else
                {
                    // Inverted gravity: check if negative velocity exceeds positive fallspeed
                    atMaxFallSpeed = (playerVelY_fixed < tmpfallspeed);
                }
                
                AppendSimDebug($"[GRAV_CHECK] atMaxFallSpeed={atMaxFallSpeed}, velY=0x{playerVelY_fixed:X4}, fallspeed=0x{tmpfallspeed:X4}");
                
                if (atMaxFallSpeed)
                {
                    // From gamemode_cube.h line 255: tmpaccel = -tmpaccel;
                    tmpaccel = -tmpaccel;
                    AppendSimDebug($"[GRAV_REVERSE] tmpaccel reversed to 0x{tmpaccel:X}");
                }
                
                // gravity_mod handling (lines 256-262) - apply gravity multiplier
                tmpaccel = (int)(tmpaccel * gravityMultiplier);
                
                // From gamemode_cube.h line 264: currplayer_vel_y += tmpaccel;
                int velY_before = playerVelY_fixed;
                playerVelY_fixed += (int)Math.Round(tmpaccel * simTimeScale);
                AppendSimDebug($"[GRAV_APPLY] velY: 0x{velY_before:X4} + (0x{tmpaccel:X} * {simTimeScale:F2}) = 0x{playerVelY_fixed:X4}");
            }
            else if (tmp1 == 2)
            {
                // 45deg up dash: vel_y = -vel_x
                playerVelY_fixed = -velocityX;
            }
            else if (tmp1 == 3)
            {
                // 45deg down dash: vel_y = vel_x
                playerVelY_fixed = velocityX;
            }
            else if (tmp1 == 4)
            {
                // Upward dash: vel_y = vel_x * 2, then subtract from position and return early
                playerVelY_fixed = velocityX * 2;
                playerY_fixed -= (int)Math.Round(playerVelY_fixed * simTimeScale);
                return;
            }
            else if (tmp1 == 5)
            {
                // Downward dash: vel_y = vel_x * 2
                playerVelY_fixed = velocityX * 2;
            }
            else
            {
                // Horizontal dash (tmp1 == 1): vel_y = gravity ? -1 : 1, then return early
                playerVelY_fixed = (currplayer_gravity != 0) ? -1 : 1;
                playerY_fixed += (int)Math.Round(playerVelY_fixed * simTimeScale);
                return;
            }
            
            // From gamemode_cube.h line 278: currplayer_y += currplayer_vel_y;
            int posY_before = playerY_fixed;
            playerY_fixed += (int)Math.Round(playerVelY_fixed * simTimeScale);
            AppendSimDebug($"[GRAV_POS] posY: 0x{posY_before:X4} ({posY_before >> 8}px) + (0x{playerVelY_fixed:X4} * {simTimeScale:F2}) = 0x{playerY_fixed:X4} ({playerY_fixed >> 8}px)");
            
            // Clamp to world bounds (allow negative Y to reach top tiles)
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
            
            // For mini mode, position hitbox based on gravity direction
            // Normal gravity: bottom-left quadrant (9 pixels down to align 7px hitbox at bottom)
            // Flipped gravity: top-left quadrant (no offset)
            int hitboxOffsetX = 0;  // No X offset, left-aligned
            int hitboxOffsetY = (currplayer_mini != 0 && currplayer_gravity == 0) ? 9 : 0;
            
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
                        wasZeroedByCollisionLastFrame = true;  // Signal that gravity should not re-apply next frame
                        
                        // AppendSimDebug($"[CUBE]   AFTER slope eject: eject_D={eject_D}, oldY={oldY}, newY={newPixelY}, playerY_fixed={playerY_fixed}");
                        
                        // CRITICAL FIX: Update playerY_px after slope ejection so subsequent code uses correct position
                        playerY_px = newPixelY;
                    }
                    else
                    {
                        // eject_D=0 means player is at correct height, just stop velocity
                        // AppendSimDebug($"[CUBE]   Slope hit with eject_D=0 - player at correct height");
                        playerVelY_fixed = 0;
                        wasZeroedByCollisionLastFrame = true;  // Signal that gravity should not re-apply next frame
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
                            int newY = collisionTopY - hitboxH - hitboxOffsetY - 1;
                            // AppendSimDebug($"[CUBE]     Eject down: collisionTop={collisionTopY}, newY={newY} (was {playerY_px})");
                            playerY_fixed = newY << 8;
                            playerVelY_fixed = 0;
                            wasZeroedByCollisionLastFrame = true;  // Signal that gravity should not re-apply next frame
                            onGround = true;  // Mark as grounded so main loop can check if still supported
                            
                            // CRITICAL: Update playerY_px and collisionY after floor snap
                            // so ceiling death check uses the NEW position
                            playerY_px = newY;
                            collisionY = playerY_px + hitboxOffsetY;
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
                // H block: skip death check and always eject like UFO
                if ((currentGameMode == 0 || currentGameMode == 4 || currentGameMode == 8) && !MainWindow.Option_NoDeath && !hblocked)
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
                            deathTileX = rightX_px;
                            deathTileY = upperY_px;
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
                else if ((currentGameMode == 0 || currentGameMode == 4 || currentGameMode == 8) && (MainWindow.Option_NoDeath || hblocked))
                {
                    // NO DEATH mode OR H block: eject from top collision like UFO/Ship
                    var (topCollided, collisionBottomY) = CheckCollisionUp(collisionX, collisionY, hitboxW, hitboxH);
                    if (topCollided)
                    {
                        int newY = collisionBottomY - hitboxOffsetY + 1;
                        AppendSimDebug($"[CUBE]     Eject up ({(hblocked ? "H BLOCK" : "NO DEATH")}): collisionBottom={collisionBottomY}, newY={newY} (was {playerY_px})");
                        playerY_fixed = newY << 8;
                        
                        // H block: headbonk - set velocity to 1 instead of 0 for instant ejection (gamemode_cube.h line 309)
                        if (!hblocked)
                            playerVelY_fixed = 0;
                        else
                            playerVelY_fixed = 1;
                        
                        // F block: flip gravity on ceiling hit (gamemode_cube.h line 312-315)
                        if (fblocked)
                        {
                            currplayer_gravity = 0xFF; // GRAVITY_UP
                            gravityFlipped = true;
                            currplayer_table_idx = (currplayer_gravity != 0 ? 1 : 0) | (currplayer_mini != 0 ? 4 : 0);
                        }
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
                        // collisionBottomY is exclusive (one past last solid pixel)
                        // We want hitbox top (playerY + hitboxOffsetY) at collisionBottomY
                        int newY = collisionBottomY - hitboxOffsetY;
                        // AppendSimDebug($"[CUBE]     Eject up: collisionBottom={collisionBottomY}, newY={newY} (was {playerY_px})");
                        playerY_fixed = newY << 8;
                        
                        // CRITICAL: Update playerY_px and collisionY after ceiling snap
                        // so floor death check uses the NEW position
                        playerY_px = newY;
                        collisionY = playerY_px + hitboxOffsetY;
                        
                        // H block: headbonk - set velocity to 1 instead of 0 for instant ejection (gamemode_cube.h line 309)
                        if (!hblocked)
                        {
                            playerVelY_fixed = 0;
                            onGround = true;  // Mark as grounded so main loop can check if still supported
                        }
                        else
                            playerVelY_fixed = 1;
                        
                        // F block: flip gravity on ceiling hit (gamemode_cube.h line 312-315)
                        if (fblocked)
                        {
                            currplayer_gravity = 0xFF; // GRAVITY_UP
                            gravityFlipped = true;
                            currplayer_table_idx = (currplayer_gravity != 0 ? 1 : 0) | (currplayer_mini != 0 ? 4 : 0);
                        }
                    }
                }
                
                // Reversed gravity: Check BOTTOM collision with passthrough/death for Cube/Robot/Ninja
                // H block: skip death check and always eject like UFO
                if ((currentGameMode == 0 || currentGameMode == 4 || currentGameMode == 8) && !MainWindow.Option_NoDeath && !hblocked)
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
                            deathTileX = rightX_px;
                            deathTileY = lowerY_px;
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
                else if ((currentGameMode == 0 || currentGameMode == 4 || currentGameMode == 8) && (MainWindow.Option_NoDeath || hblocked))
                {
                    // NO DEATH mode OR H block: eject from bottom collision like UFO/Ship
                    var (bottomCollided, collisionTopY) = CheckCollisionDown(collisionX, collisionY, hitboxW, hitboxH);
                    if (bottomCollided)
                    {
                        int newY = collisionTopY - hitboxH - hitboxOffsetY - 1;
                        AppendSimDebug($"[CUBE]     Eject down ({(hblocked ? "H BLOCK" : "NO DEATH")}): collisionTop={collisionTopY}, newY={newY} (was {playerY_px})");
                        playerY_fixed = newY << 8;
                        
                        // H block: headbonk for floor - set velocity to 0xffff instead of 0 (gamemode_cube.h line 291)
                        if (!hblocked)
                            playerVelY_fixed = 0;
                        else
                            playerVelY_fixed = unchecked((int)0xffff); // -1 in signed 16-bit
                        
                        // F block: flip gravity on floor hit (gamemode_cube.h line 294-297)
                        if (fblocked)
                        {
                            currplayer_gravity = 0; // GRAVITY_DOWN
                            gravityFlipped = false;
                            currplayer_table_idx = (currplayer_gravity != 0 ? 1 : 0) | (currplayer_mini != 0 ? 4 : 0);
                        }
                    }
                }
            }
            
            // Clear alphabet block flags at end of frame (matches gamemode_cube.h lines 170-172)
            fblocked = false;
            hblocked = false;
            jblocked = false;
        }
        
        /// <summary>
        /// Check center point for death (matches bg_coll_death() from collision.h line 1019)
        /// This checks a single center point of the player hitbox for spike/death collision
        /// </summary>
        private void CheckCenterPointDeath_Fresh()
        {
            if (MainWindow.Option_NoDeath || deathTriggered) return;
            
            int playerX_px = playerX_fixed >> 8;
            int playerY_px = playerY_fixed >> 8;
            
            // Calculate hitbox dimensions
            int hitboxW = (currplayer_mini != 0) ? MINI_CUBE_HITBOX_W : CUBE_HITBOX_W;
            int hitboxH = (currplayer_mini != 0) ? MINI_CUBE_HITBOX_H : CUBE_HITBOX_H;
            
            // Apply mini mode offset for positioning
            int hitboxOffsetY = (currplayer_mini != 0 && currplayer_gravity == 0) ? 9 : 0;
            
            // From collision.h: center X = Generic.x + (Generic.width >> 1) - 1
            // From collision.h: center Y = Generic.y + (Generic.height >> 1) + mini_offset
            int centerX_px = playerX_px + (hitboxW >> 1) - 1;
            int centerY_px = playerY_px + (hitboxH >> 1) + hitboxOffsetY;
            
            int tileX = centerX_px / TILE;
            int tileY = centerY_px / TILE;
            
            if (tileX < 0 || tileX >= mapWidth || tileY < 0 || tileY >= mapHeight) return;
            
            int groundRowsToReserve = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
            int tileArrayY = tileY + groundRowsToReserve;
            if (tileArrayY >= mapHeight) return;
            
            int tileIdx = tileArrayY * mapWidth + tileX;
            if (tileIdx < 0 || tileIdx >= tiles.Length) return;
            
            int tileId = tiles[tileIdx];
            var collision = MetatileCollisionTable.GetCollision((byte)tileId);
            
            int localX = centerX_px % TILE;
            int localY = centerY_px % TILE;
            
            if (MetatileCollisionTable.TileKillsAtPixel(collision, localX, localY))
            {
                AppendSimDebug($"[DEATH] Center point spike at ({centerX_px},{centerY_px}) tile={tileId:X2}");
                deathTriggered = true;
                deathTileX = centerX_px;
                deathTileY = centerY_px;
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
                            try { mw.AddDeathMarker(centerX_px, centerY_px); } catch { }
                        }
                    }));
                }
                catch { }
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



