using System;
using System.Threading;

namespace FamidashEditor
{
    public partial class SimulatorWindow
    {
        // ========================================================================
        // CUBE PHYSICS - Fresh 1:1 Port from cleaned gamemode_cube.h
        // ========================================================================

        // Physics constants — delegate to SharedPhysics to prevent drift
        private const int CUBE_GRAVITY_NORMAL = SharedPhysics.CUBE_GRAVITY_NORMAL;
        private const int CUBE_GRAVITY_MINI = SharedPhysics.CUBE_GRAVITY_MINI;
        private const int CUBE_MAX_FALLSPEED_NORMAL = SharedPhysics.CUBE_MAX_FALLSPEED;
        private const int CUBE_MAX_FALLSPEED_MINI = SharedPhysics.CUBE_MAX_FALLSPEED;
        private const int JUMP_VEL_NORMAL = SharedPhysics.JUMP_VEL_NORMAL;
        private const int JUMP_VEL_MINI = SharedPhysics.JUMP_VEL_MINI;
        
        // Cube hitbox dimensions — delegate to SharedPhysics
        private const int CUBE_HITBOX_W = SharedPhysics.CUBE_HITBOX_W;
        private const int CUBE_HITBOX_H = SharedPhysics.CUBE_HITBOX_H;
        private const int MINI_CUBE_HITBOX_W = SharedPhysics.MINI_CUBE_HITBOX_W;
        private const int MINI_CUBE_HITBOX_H = SharedPhysics.MINI_CUBE_HITBOX_H;
        
        // State variables from famidash.h
        private byte currplayer_mini = 0;      // 0 = normal, 1 = mini
        private byte currplayer_gravity = 0;   // 0 = down, 0xFF = up
        
        // Track if we zeroed velocity due to ground collision (for preventing gravity oscillation)
        // Initialized to true because the player starts grounded on the floor.
        private bool wasZeroedByCollisionLastFrame = true;
        
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
                    int playerX_px_orb = (playerX_fixed >> 8) + 1;
                    int playerY_px_orb = playerY_fixed >> 8;
                    int hitboxW_orb = (currplayer_mini != 0) ? MINI_CUBE_HITBOX_W : CUBE_HITBOX_W;
                    int hitboxH_orb = (currplayer_mini != 0) ? MINI_CUBE_HITBOX_H : CUBE_HITBOX_H;
                    
                    // NES: Generic.y += ((0x10 - height) >> 1); Normal: +0, Mini: +4
                    if (currplayer_mini != 0 && !gravityInverted_orb)
                    {
                        playerY_px_orb += 4;
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
                    int hitboxOffsetY_check = SharedPhysics.GetMiniCenterOffsetY(isMini_check);
                    int collisionX_check = (playerX_fixed >> 8);
                    int testY_check = (playerY_fixed >> 8) + hitboxOffsetY_check - 1;
                    var (collided_check, collisionBottomY_check) = CheckCollisionUp(collisionX_check, testY_check, hitboxW_check, hitboxH_check);
                    
                    if (collided_check && playerVelY_fixed < 0) {
                        // Snap Y to ceiling surface (same formula as CubeEject reversed gravity)
                        // This prevents sub-pixel drift when playerTop == ceilBottom,
                        // where CheckCollisionUp's strict < comparison would miss the eject.
                        int newY_prox = collisionBottomY_check - hitboxOffsetY_check - 1;
                        playerY_fixed = newY_prox << 8;
                        playerVelY_fixed = 0;
                        // AppendSimDebug($"[CUBE] Ceiling grounded - snapped Y to {newY_prox}");
                    }
                }
                
                // AppendSimDebug($"[CUBE] After gravity: velY={playerVelY_fixed}, posY={playerY_fixed >> 8}");
                
                // STEP 3: cube_eject() - collision detection and ejection
                // NES order: cube_movement → common_gravity_routine → cube_eject → (return)
                // then runthecolls → bg_coll_death (uses post-eject Generic.y)
                CubeEject_Fresh();
                
                // Check center-point death AFTER eject (matches NES bg_coll_death() in
                // runthecolls which reads post-eject currplayer_y via Generic.y)
                CheckCenterPointDeath_Fresh();
                
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
                        
                        // Log pathfinder jump check data when pathfinder is active and input is present
                        if (pathfinderEnabled && (holdJump || pressJump))
                            AppendSimDebug($"[PF-JUMP] hold={holdJump}, press={pressJump}, velY=0x{playerVelY_fixed:X4}, orbed={orbed[currplayer]}, dashing={dashing[currplayer]}, wasZeroed={wasZeroedByCollisionLastFrame}, pfIdx={pfFrameIndex}");                        
                        // Two jump paths from gamemode_cube.h lines 50-65:
                        // Path 1: Hold A && !jblocked && !fblocked → if (!orbed) jump
                        // Path 2: Press A && (jblocked || fblocked) → jump (no orbed check!)
                        
                        // Use tolerance for grounded check (check if at rest)
                        bool isGrounded = (playerVelY_fixed >= -16 && playerVelY_fixed <= 16);
                        
                        bool doJump = false;
                        // Path 1: hold-to-jump (no jblocked/fblocked, checks orbed)
                        // During pathfinder replay, use pressJump instead of holdJump.
                        // The PF's input may have consecutive trues from landing
                        // predictions; pressJump is consumed once per true→grounded
                        // transition, preventing stale holds from mis-firing jumps.
                        bool cubeJumpInput = pathfinderEnabled ? pressJump : holdJump;
                        if (cubeJumpInput && !jblocked && !fblocked && isGrounded && !orbed[currplayer] && dashing[currplayer] == 0)
                            doJump = true;
                        // Path 2: press-to-jump (jblocked/fblocked, no orbed check)
                        else if (pressJump && (jblocked || fblocked) && isGrounded && dashing[currplayer] == 0)
                            doJump = true;

                        if (doJump)
                        {
                            if (pathfinderEnabled) AppendSimDebug($"[PF-JUMP] JUMP TRIGGERED! velY → 0x{(GameModePhysics.JUMP_VEL(currplayer_mini != 0 ? 4 : 0) * (currplayer_gravity != 0 ? -1 : 1)):X4}");
                            
                            // Consume the press now that we're using it for jump
                            Interlocked.Exchange(ref keyXPressedCount, 0);
                            
                            // Get base physics value (always from down-gravity index)
                            int baseTableIdx = (currplayer_mini != 0 ? 4 : 0);
                            bool gravityInverted = (currplayer_gravity != 0);
                            int gravityMultiplier = gravityInverted ? -1 : 1;
                            
                            // From gamemode_cube.h: currplayer_vel_y = JUMP_VEL(currplayer_table_idx);
                            int jumpVel = GameModePhysics.JUMP_VEL(baseTableIdx) * gravityMultiplier;
                            playerVelY_fixed = jumpVel;
                            
                            // NES slope_jump_check: add extra velocity when jumping off a slope
                            SlopeJumpCheck_Fresh();
                            
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
                            // NES slope_jump_check: add extra velocity when jumping off a slope
                            SlopeJumpCheck_Fresh();
                            AppendSimDebug($"[FOOTBALL] Released! chargepower={tmp3}, tmpA=0x{tmpA:X4}, velY now = {playerVelY_fixed}");
                        }
                        
                        // Reset chargepower on release (gamemode_cube.h line 154)
                        chargepower[0] = 0;
                    }
                }
                
                // Clear alphabet block flags AFTER jump check (matches PF/NES order:
                // ProcessSprites sets jblocked → gravity → eject → jump check reads jblocked → clear)
                // Previously these were cleared inside CubeEject_Fresh, BEFORE the jump check,
                // which caused Path 2 (pressJump && jblocked) to never fire.
                jblocked = false;
                fblocked = false;
                hblocked = false;

                // STEP 5: Update slope counters (decrement each frame)
                UpdateSlopeCounters_Fresh();
                
                // Record position for trail AFTER physics completes (for smooth visualization)
                // Skip during pathfinder speculative simulation to avoid polluting trail
                if (!pfSimulating)
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
                            // Always center on TILE/2 (player pos = 16x16 tile space)
                            int playerCenter_px = (playerX_fixed >> 8) + (TILE / 2);
                            int playerLeft_px = playerCenter_px - (HITBOX_W_LOCAL / 2);
                            int playerRight_px = playerLeft_px + (HITBOX_W_LOCAL - 1);
                            // Foot = bottom of actual hitbox (hitboxOffset + hitboxH)
                            int hitboxH_gs = (currplayer_mini != 0) ? MINI_CUBE_HITBOX_H : CUBE_HITBOX_H;
                            int hitboxOffY_gs = (currplayer_mini != 0) ? 9 : 0; // cube mode, normal gravity
                            int footWorldY_px = (playerY_fixed >> 8) + hitboxOffY_gs + hitboxH_gs;
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
        /// Delegates to SharedPhysics.CommonGravityRoutine so PF and SIM use identical logic.
        /// </summary>
        private void CommonGravityRoutine_Fresh()
        {
            AppendSimDebug($"[GRAV_START] velY=0x{playerVelY_fixed:X4}, posY=0x{playerY_fixed:X4} ({playerY_fixed >> 8}px), dashing={dashing[currplayer]}, wasZeroedByCollision={wasZeroedByCollisionLastFrame}, mode={currentGameMode}");

            int velBefore = playerVelY_fixed;
            int posBefore = playerY_fixed;

            int clampMaxY = Math.Max(0, (mapHeight * TILE - playerVisualHeight)) << 8;

            SharedPhysics.CommonGravityRoutine(
                ref playerVelY_fixed,
                ref playerY_fixed,
                tmpgravity,
                tmpfallspeed,
                currplayer_gravity,
                dashing[currplayer],
                gravityMultiplier,
                simTimeScale,
                isFullSpeed,
                velocityX,
                clampMaxY);

            AppendSimDebug($"[GRAV_POS] posY: 0x{posBefore:X4} ({posBefore >> 8}px) -> 0x{playerY_fixed:X4} ({playerY_fixed >> 8}px), velY: 0x{velBefore:X4} -> 0x{playerVelY_fixed:X4}");
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
            
            int hitboxOffsetX = 0;
            int hitboxOffsetY = SharedPhysics.GetCubeHitboxOffsetY(currplayer_mini != 0, currplayer_gravity != 0);
            
            int collisionX = playerX_px + hitboxOffsetX;
            int collisionY = playerY_px + hitboxOffsetY;
            
            // Update slope counters
            UpdateSlopeCounters();
            
            // Cube collision detection based on gravity direction
            if (currplayer_gravity == 0)
            {
                // Normal gravity: check slopes first (NES bg_coll_D slope section
                // has NO velocity guard — slopes are always checked regardless of vel_y)
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
                else if (playerVelY_fixed >= 0)
                {
                    // Flat floor check — NES bg_coll_D velocity guard:
                    // if(!(high_byte(currplayer_vel_y) & 0x80))
                    // Only runs when vel_y >= 0 (falling/grounded).
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
                            int newY = collisionTopY - hitboxH - hitboxOffsetY;
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
                
                // Normal gravity: Check TOP collision for hblocked/fblocked eject
                if ((currentGameMode == 0 || currentGameMode == 4 || currentGameMode == 8) && (hblocked || fblocked))
                {
                    var (topCollided, collisionBottomY) = CheckCollisionUp(collisionX, collisionY, hitboxW, hitboxH);
                    if (topCollided)
                    {
                        int newY = collisionBottomY - hitboxOffsetY;
                        AppendSimDebug($"[CUBE]     Eject up ({(hblocked ? "H BLOCK" : "F BLOCK")}): collisionBottom={collisionBottomY}, newY={newY} (was {playerY_px})");
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
                        // NES bg_coll_U probes at Generic.y + miniOffset + 1 (1 pixel inside),
                        // so resting position is 1 pixel closer to ceiling than collisionBottomY.
                        int newY = collisionBottomY - hitboxOffsetY - 1;
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
                
                // Reversed gravity: Check BOTTOM collision for hblocked/fblocked eject
                if ((currentGameMode == 0 || currentGameMode == 4 || currentGameMode == 8) && (hblocked || fblocked))
                {
                    var (bottomCollided, collisionTopY) = CheckCollisionDown(collisionX, collisionY, hitboxW, hitboxH);
                    if (bottomCollided)
                    {
                        int newY = collisionTopY - hitboxH - hitboxOffsetY;
                        AppendSimDebug($"[CUBE]     Eject down ({(hblocked ? "H BLOCK" : "F BLOCK")}): collisionTop={collisionTopY}, newY={newY} (was {playerY_px})");
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
            
            // jblocked/fblocked/hblocked clearing moved to ProcessCubePhysics_Fresh
            // AFTER the jump check, matching PF order where these flags persist
            // through the jump check and are only cleared afterward.
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
            
            int hitboxOffsetY = SharedPhysics.GetCubeHitboxOffsetY(currplayer_mini != 0, currplayer_gravity != 0);
            
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
                
                // Skip UI side effects during pathfinder speculative simulation
                if (pfSimulating) return;
                
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



