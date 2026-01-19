using System;

namespace FamidashEditor
{
    public partial class SimulatorWindow
    {
        /// <summary>
        /// spider_movement() from gamemode_spider.h - 1:1 port
        /// </summary>
        private void SpiderPhysics_Fresh()
        {
            // Check for orb activation
            {
                bool holdJump_orb = IsXDownAsync() || keyXHeld;
                int pressCount_orb = Interlocked.CompareExchange(ref keyXPressedCount, 0, 0);
                bool pressJump_orb = pressCount_orb > 0;
                bool gravityInverted_orb = (currplayer_gravity != 0);
                int playerX_px_orb = playerX_fixed >> 8;
                int playerY_px_orb = playerY_fixed >> 8;
                int hitboxW_orb = (currplayer_mini != 0) ? 8 : 15;
                int hitboxH_orb = (currplayer_mini != 0) ? 8 : 15;
                int scrollX_px_orb = 0;
                
                int tempVelY = playerVelY_fixed;
                bool orbActivated = UpdateOrbSystem(5, playerX_px_orb, playerY_px_orb, hitboxW_orb, hitboxH_orb, 
                                                   scrollX_px_orb, pressJump_orb, holdJump_orb, gravityInverted_orb, 
                                                   (currplayer_mini != 0), ref tempVelY);
                if (orbActivated)
                {
                    playerVelY_fixed = tempVelY;
                    AppendSimDebug($"[SPIDER] Orb activated! New velY={playerVelY_fixed}");
                    
                    // Consume the X press if it was used for orb
                    if (pressJump_orb)
                        Interlocked.Exchange(ref keyXPressedCount, 0);
                }
                
                // Clear orb buffer when X is released
                if (!holdJump_orb)
                    ClearOrbBuffer();
            }
            
            // Get base physics values (always from down-gravity index)
            int baseTableIdx = (currplayer_mini != 0 ? 4 : 0);
            bool gravityInverted = (currplayer_gravity != 0);
            int gravityMultiplier = gravityInverted ? -1 : 1;
            
            tmpfallspeed = GameModePhysics.SPIDER_MAX_FALLSPEED(baseTableIdx) * gravityMultiplier;
            tmpgravity = GameModePhysics.SPIDER_GRAVITY(baseTableIdx) * gravityMultiplier;
            
            // Apply gravity and collision FIRST
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
                    AppendSimDebug($"[SPIDER] Ceiling grounded - zeroed velocity");
                }
            }
            
            SpiderEject_Fresh((playerY_fixed >> 8));
            
            // Input handling - check AFTER collision has stabilized
            bool holdingJump = IsXDownAsync() || keyXHeld;
            int pressCount = Interlocked.Exchange(ref keyXPressedCount, 0);
            bool pressedJump = pressCount > 0;
            
            // Check if truly grounded by checking collision with floor/ceiling
            int hitboxW = (currplayer_mini != 0) ? 8 : 15;
            int hitboxH = (currplayer_mini != 0) ? 8 : 15;
            int hitboxOffsetY = (currplayer_mini != 0 && currplayer_gravity == 0) ? 8 : 0;
            int collisionX = (playerX_fixed >> 8);
            
            bool isGrounded = false;
            if (currplayer_gravity == 0) {
                // Check floor collision
                int collisionY = (playerY_fixed >> 8) + hitboxOffsetY;
                var (collided, _) = CheckCollisionDown(collisionX, collisionY, hitboxW, hitboxH);
                isGrounded = collided;
            } else {
                // Check ceiling collision
                int testY = (playerY_fixed >> 8) + hitboxOffsetY - 1;
                var (collided, _) = CheckCollisionUp(collisionX, testY, hitboxW, hitboxH);
                isGrounded = collided;
            }
            
            AppendSimDebug($"[SPIDER] press={pressedJump}, grounded={isGrounded}, velY={playerVelY_fixed}, grav={currplayer_gravity:X2}");
            
            if (currplayer_gravity == 0) {
                // On floor, can jump to ceiling
                if (pressedJump && isGrounded && !ufoOrbed) {
                    AppendSimDebug($"[SPIDER] TELEPORTING TO CEILING! Start Y={playerY_fixed >> 8}");
                    
                    // Scan upward for ceiling
                    int scanY = (playerY_fixed >> 8);
                    bool foundCeiling = false;
                    
                    while (scanY > 8) {
                        scanY -= 8;
                        int scanCollisionX = (playerX_fixed >> 8);
                        int collisionY = scanY + hitboxOffsetY;
                        
                        var (collided, collisionBottomY) = CheckCollisionUp(scanCollisionX, collisionY, hitboxW, hitboxH);
                        if (collided) {
                            foundCeiling = true;
                            playerY_fixed = ((collisionBottomY + 1 - hitboxOffsetY) << 8);
                            AppendSimDebug($"[SPIDER] Found ceiling at Y={playerY_fixed >> 8}");
                            break;
                        }
                    }
                    
                    if (foundCeiling) {
                        playerVelY_fixed = 0;
                        currplayer_gravity = 0xFF; // GRAVITY_UP
                        gravityReversed = true;
                        gravityFlipped = true;
                        UpdateCurrplayerTableIdx_Fresh();
                        UpdatePlayerIconFlip();
                        
                        // Update physics values for new gravity
                        gravityInverted = true;
                        gravityMultiplier = -1;
                        tmpfallspeed = GameModePhysics.SPIDER_MAX_FALLSPEED(baseTableIdx) * gravityMultiplier;
                        tmpgravity = GameModePhysics.SPIDER_GRAVITY(baseTableIdx) * gravityMultiplier;
                        
                        AppendSimDebug($"[SPIDER] Teleported to Y={playerY_fixed >> 8}, grav=UP");
                    }
                    
                    blackOrbed = false;
                }
                else if (!holdingJump) blackOrbed = false;
            } else {
                // On ceiling, can jump to floor
                if (pressedJump && isGrounded && !ufoOrbed) {
                    AppendSimDebug($"[SPIDER] TELEPORTING TO FLOOR! Start Y={playerY_fixed >> 8}");
                    
                    // Scan downward for floor
                    int scanY = (playerY_fixed >> 8);
                    bool foundFloor = false;
                    
                    while (scanY < 240) {
                        scanY += 8;
                        int scanCollisionX = (playerX_fixed >> 8);
                        int collisionY = scanY + hitboxOffsetY;
                        
                        var (collided, collisionTopY) = CheckCollisionDown(scanCollisionX, collisionY, hitboxW, hitboxH);
                        if (collided) {
                            foundFloor = true;
                            playerY_fixed = ((collisionTopY - hitboxH - 1 - hitboxOffsetY) << 8);
                            AppendSimDebug($"[SPIDER] Found floor at Y={playerY_fixed >> 8}");
                            break;
                        }
                    }
                    
                    if (foundFloor) {
                        playerVelY_fixed = 0;
                        currplayer_gravity = 0; // GRAVITY_DOWN
                        gravityReversed = false;
                        gravityFlipped = false;
                        UpdateCurrplayerTableIdx_Fresh();
                        UpdatePlayerIconFlip();
                        
                        // Update physics values for new gravity
                        gravityInverted = false;
                        gravityMultiplier = 1;
                        tmpfallspeed = GameModePhysics.SPIDER_MAX_FALLSPEED(baseTableIdx) * gravityMultiplier;
                        tmpgravity = GameModePhysics.SPIDER_GRAVITY(baseTableIdx) * gravityMultiplier;
                        
                        AppendSimDebug($"[SPIDER] Teleported to Y={playerY_fixed >> 8}, grav=DOWN");
                    }
                    
                    blackOrbed = false;
                }
                else if (!holdingJump) blackOrbed = false;
            }
            
            // Record position for trail
            try
            {
                int playerWorldCenterX_px = (playerX_fixed >> 8) + (playerVisualWidth / 2);
                int playerWorldCenterY_px = (playerY_fixed >> 8) + (playerVisualHeight / 2);
                recordedPlayerPath.Add((playerWorldCenterX_px, playerWorldCenterY_px));
            }
            catch { }
        }
        
        private bool blackOrbed = false;
        
        /// <summary>
        /// spider_eject() from gamemode_spider.h
        /// </summary>
        private void SpiderEject_Fresh(int offsetY)
        {
            int hitboxW = (currplayer_mini != 0) ? 8 : 15;
            int hitboxH = (currplayer_mini != 0) ? 8 : 15;
            int hitboxOffsetY = (currplayer_mini != 0 && currplayer_gravity == 0) ? 8 : 0;
            int collisionX = (playerX_fixed >> 8);
            int collisionY = (playerY_fixed >> 8) + hitboxOffsetY;
            
            if (currplayer_gravity == 0) {
                // Normal gravity - check floor collision
                var (collided, collisionTopY) = CheckCollisionDown(collisionX, collisionY, hitboxW, hitboxH);
                if (collided) {
                    playerY_fixed = ((collisionTopY - hitboxH - 1 - hitboxOffsetY) << 8);
                    playerVelY_fixed = 0;
                }
            } else {
                // Inverted gravity - check ceiling collision
                var (collided, collisionBottomY) = CheckCollisionUp(collisionX, collisionY, hitboxW, hitboxH);
                if (collided) {
                    playerY_fixed = ((collisionBottomY - hitboxOffsetY) << 8);
                    playerVelY_fixed = 0;
                }
            }
        }
        
        /// <summary>
        /// spider_up_wait() - scan upward for ceiling
        /// </summary>
        private void SpiderUpWait_Fresh()
        {
            do {
                int oldY = playerY_fixed >> 8;
                playerY_fixed = ((oldY - 0x08) << 8);
                
                if ((playerY_fixed >> 8) <= 0x07) {
                    break; // Would go off screen
                }
            } while (!BgCollU_Spider_Fresh());
        }
        
        /// <summary>
        /// spider_down_wait() - scan downward for floor
        /// </summary>
        private void SpiderDownWait_Fresh()
        {
            do {
                int oldY = playerY_fixed >> 8;
                playerY_fixed = ((oldY + 0x08) << 8);
                
                if ((playerY_fixed >> 8) >= 0xF8) {
                    break; // Would go off screen
                }
            } while (!BgCollD_Spider_Fresh());
        }
        
        /// <summary>
        /// bg_coll_U_spider() - check ceiling collision for spider
        /// </summary>
        private bool BgCollU_Spider_Fresh()
        {
            int hitboxW = (currplayer_mini != 0) ? 8 : 15;
            int hitboxH = (currplayer_mini != 0) ? 8 : 15;
            int hitboxOffsetY = (currplayer_mini != 0 && currplayer_gravity == 0) ? 8 : 0;
            int collisionX = (playerX_fixed >> 8);
            int collisionY = (playerY_fixed >> 8) + hitboxOffsetY;
            var (collided, _) = CheckCollisionUp(collisionX, collisionY, hitboxW, hitboxH);
            return collided;
        }
        
        /// <summary>
        /// bg_coll_D_spider() - check floor collision for spider
        /// </summary>
        private bool BgCollD_Spider_Fresh()
        {
            int hitboxW = (currplayer_mini != 0) ? 8 : 15;
            int hitboxH = (currplayer_mini != 0) ? 8 : 15;
            int hitboxOffsetY = (currplayer_mini != 0 && currplayer_gravity == 0) ? 8 : 0;
            int collisionX = (playerX_fixed >> 8);
            int collisionY = (playerY_fixed >> 8) + hitboxOffsetY;
            var (collided, _) = CheckCollisionDown(collisionX, collisionY, hitboxW, hitboxH);
            return collided;
        }
    }
}
