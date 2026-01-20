using System;
using System.Threading;

namespace FamidashEditor
{
    public partial class SimulatorWindow
    {
        /// <summary>
        /// spider_movement() from gamemode_spider.h - Complete refactor
        /// Spider teleports to ceiling/floor when X is pressed
        /// </summary>
        private void SpiderPhysics_Fresh()
        {
            // Orb activation check first
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
                var (orbActivated, orbType) = UpdateOrbSystem(5, playerX_px_orb, playerY_px_orb, hitboxW_orb, hitboxH_orb, 
                                                   scrollX_px_orb, pressJump_orb, holdJump_orb, gravityInverted_orb, 
                                                   (currplayer_mini != 0), ref tempVelY);
                if (orbActivated)
                {
                    playerVelY_fixed = tempVelY;
                    AppendSimDebug($"[SPIDER] Orb activated! Type=0x{orbType:X2}, New velY={playerVelY_fixed}");
                    
                    // Set blackOrbed flag if this was a black orb (0x44)
                    if (orbType == 0x44)
                    {
                        blackOrbed = true;
                        AppendSimDebug($"[SPIDER] BLACK ORB activated - blackOrbed set to true");
                    }
                    
                    if (pressJump_orb)
                        Interlocked.Exchange(ref keyXPressedCount, 0);
                }
                
                if (!holdJump_orb)
                    ClearOrbBuffer();
            }
            
            // Get base physics values
            int baseTableIdx = (currplayer_mini != 0 ? 4 : 0);
            bool gravityInverted = (currplayer_gravity != 0);
            int gravityMultiplier = gravityInverted ? -1 : 1;
            
            tmpfallspeed = GameModePhysics.SPIDER_MAX_FALLSPEED(baseTableIdx) * gravityMultiplier;
            tmpgravity = GameModePhysics.SPIDER_GRAVITY(baseTableIdx) * gravityMultiplier;
            
            // Apply gravity and integrate position
            CommonGravityRoutine_Fresh();
            
            // Spider eject - check collision and zero velocity if grounded
            // Offset collision check down 1 pixel (normal) or up 2 pixels (inverted)
            int offsetY = (playerY_fixed >> 8) + (gravityInverted ? -2 : 1);
            SpiderEject_Fresh(offsetY);
            
            // Input handling
            bool holdingJump = IsXDownAsync() || keyXHeld;
            int pressCount = Interlocked.Exchange(ref keyXPressedCount, 0);
            bool pressedJump = pressCount > 0;
            
            // Spider can only teleport when velocity is 0 (grounded)
            bool canTeleport = (playerVelY_fixed == 0) && !ufoOrbed;
            
            if (currplayer_gravity == 0)
            {
                // Normal gravity - on floor, can teleport to ceiling
                if ((pressedJump || (holdingJump && blackOrbed)) && canTeleport)
                {
                    AppendSimDebug($"[SPIDER] TELEPORTING TO CEILING! Start Y={playerY_fixed >> 8}");
                    
                    // Flip gravity and table index
                    currplayer_gravity = 0xFF; // GRAVITY_UP
                    gravityReversed = true;
                    gravityFlipped = true;
                    UpdateCurrplayerTableIdx_Fresh();
                    
                    // Scan upward for ceiling
                    SpiderUpWait_Fresh();
                    
                    // Apply eject to position correctly (matching famidash: scan then eject)
                    int groundRowsToReserve_up = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                    int hitboxH_up = (currplayer_mini != 0) ? 8 : 15;
                    int hitboxW_up = (currplayer_mini != 0) ? 8 : 15;
                    int hitboxOffsetY_up = (currplayer_mini != 0) ? 8 : 0;  // Was normal gravity before flip
                    int playerX_up = playerX_fixed >> 8;
                    int playerY_up = playerY_fixed >> 8;
                    var (collided_up, ejectAmount_up) = BgCollU_Spider(playerX_up, playerY_up + hitboxOffsetY_up, hitboxW_up, hitboxH_up, groundRowsToReserve_up);
                    if (collided_up) {
                        playerY_fixed = ((playerY_up - ejectAmount_up) << 8);
                    }
                    
                    playerVelY_fixed = 0;
                    try { Dispatcher?.BeginInvoke(new Action(() => UpdatePlayerIconFlip())); } catch { }
                    
                    AppendSimDebug($"[SPIDER] Teleported to ceiling Y={playerY_fixed >> 8}");
                    blackOrbed = false;
                }
                else if (!holdingJump) 
                {
                    blackOrbed = false;
                }
            }
            else
            {
                // Inverted gravity - on ceiling, can teleport to floor
                if ((pressedJump || (holdingJump && blackOrbed)) && canTeleport)
                {
                    AppendSimDebug($"[SPIDER] TELEPORTING TO FLOOR! Start Y={playerY_fixed >> 8}");
                    
                    // Flip gravity and table index
                    currplayer_gravity = 0; // GRAVITY_DOWN
                    gravityReversed = false;
                    gravityFlipped = false;
                    UpdateCurrplayerTableIdx_Fresh();
                    
                    // Scan downward for floor
                    SpiderDownWait_Fresh();
                    
                    // Apply eject to position correctly (matching famidash: scan then eject)
                    int groundRowsToReserve_down = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                    int hitboxH_down = (currplayer_mini != 0) ? 8 : 15;
                    int hitboxW_down = (currplayer_mini != 0) ? 8 : 15;
                    int hitboxOffsetY_down = (currplayer_mini != 0 && currplayer_gravity == 0) ? 8 : 0;
                    int playerX_down = playerX_fixed >> 8;
                    int playerY_down = playerY_fixed >> 8;
                    var (collided_down, ejectAmount_down) = BgCollD_Spider(playerX_down, playerY_down + hitboxOffsetY_down, hitboxW_down, hitboxH_down, groundRowsToReserve_down);
                    if (collided_down) {
                        playerY_fixed = ((playerY_down - ejectAmount_down) << 8);
                    }
                    
                    playerVelY_fixed = 0;
                    try { Dispatcher?.BeginInvoke(new Action(() => UpdatePlayerIconFlip())); } catch { }
                    
                    AppendSimDebug($"[SPIDER] Teleported to floor Y={playerY_fixed >> 8}");
                    blackOrbed = false;
                }
                else if (!holdingJump)
                {
                    blackOrbed = false;
                }
            }
        }
        
        /// <summary>
        /// spider_eject() from gamemode_spider.h
        /// Checks collision and zeros velocity if grounded
        /// offsetY is the Y position to check (already offset by +1 or -2)
        /// </summary>
        private void SpiderEject_Fresh(int offsetY)
        {
            int hitboxW = (currplayer_mini != 0) ? 8 : 15;
            int hitboxH = (currplayer_mini != 0) ? 8 : 15;
            int hitboxOffsetY = (currplayer_mini != 0 && currplayer_gravity == 0) ? 8 : 0;
            int collisionX = (playerX_fixed >> 8);
            int collisionY = offsetY + hitboxOffsetY;
            
            int groundRowsToReserve = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
            
            if (currplayer_gravity == 0)
            {
                // Normal gravity - check floor collision
                var (collided, ejectAmount) = BgCollD_Spider(collisionX, collisionY, hitboxW, hitboxH, groundRowsToReserve);
                if (collided)
                {
                    // Eject upward from floor
                    int currentY_px = playerY_fixed >> 8;
                    int newY_px = currentY_px - ejectAmount;
                    playerY_fixed = newY_px << 8;
                    playerVelY_fixed = 0;
                    AppendSimDebug($"[SPIDER_EJECT] Floor collision: eject={ejectAmount}, Y {currentY_px} -> {newY_px}");
                }
            }
            else
            {
                // Inverted gravity - check ceiling collision
                var (collided, ejectAmount) = BgCollU_Spider(collisionX, collisionY, hitboxW, hitboxH, groundRowsToReserve);
                if (collided)
                {
                    // Eject downward from ceiling - use minimal adjustment
                    int currentY_px = playerY_fixed >> 8;
                    int newY_px = currentY_px + ejectAmount;
                    playerY_fixed = newY_px << 8;
                    playerVelY_fixed = 0;
                    AppendSimDebug($"[SPIDER_EJECT] Ceiling collision: eject={ejectAmount}, Y {currentY_px} -> {newY_px}");
                }
            }
        }
        
        /// <summary>
        /// spider_up_wait() from gamemode_spider.h
        /// Scans upward in 8-pixel steps until ceiling is found
        /// </summary>
        private void SpiderUpWait_Fresh()
        {
            int hitboxW = (currplayer_mini != 0) ? 8 : 15;
            int hitboxH = (currplayer_mini != 0) ? 8 : 15;
            int hitboxOffsetY = (currplayer_mini != 0) ? 0 : 0; // Spider doesn't use mini offset during scan
            int playerX_px = playerX_fixed >> 8;
            int groundRowsToReserve = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
            
            int scanY_px = playerY_fixed >> 8;
            int maxIterations = 200; // Safety limit
            int iteration = 0;
            
            while (iteration < maxIterations)
            {
                // Move up 8 pixels
                scanY_px -= 8;
                playerY_fixed = scanY_px << 8;
                
                // Process camera scroll (matching famidash's process_y_scroll)
                ProcessCameraScrollDuringSpiderScan();
                
                // Check if too high (world Y < 0 accounting for ground offset)
                if (scanY_px <= -(groundRowsToReserve * TILE))
                {
                    AppendSimDebug($"[SPIDER_UP] Hit top boundary at Y={scanY_px}");
                    playerY_fixed = 0;
                    break;
                }
                
                // Check for ceiling collision
                var (collided, eject) = BgCollU_Spider(playerX_px, scanY_px + hitboxOffsetY, hitboxW, hitboxH, groundRowsToReserve);
                if (collided)
                {
                    // Use eject amount to position precisely at collision surface
                    scanY_px += eject;
                    AppendSimDebug($"[SPIDER_UP] Found ceiling, eject={eject}, final scanY={scanY_px}");
                    playerY_fixed = scanY_px << 8;
                    break;
                }
                
                iteration++;
            }
            
            if (iteration >= maxIterations)
            {
                AppendSimDebug($"[SPIDER_UP] Max iterations reached, stopping at Y={scanY_px}");
                playerY_fixed = scanY_px << 8;
            }
        }
        
        /// <summary>
        /// spider_down_wait() from gamemode_spider.h
        /// Scans downward in 8-pixel steps until floor is found
        /// </summary>
        private void SpiderDownWait_Fresh()
        {
            int hitboxW = (currplayer_mini != 0) ? 8 : 15;
            int hitboxH = (currplayer_mini != 0) ? 8 : 15;
            int hitboxOffsetY = (currplayer_mini != 0 && currplayer_gravity == 0) ? 8 : 0;
            int playerX_px = playerX_fixed >> 8;
            int groundRowsToReserve = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
            
            int scanY_px = playerY_fixed >> 8;
            int maxY_px = (mapHeight - groundRowsToReserve) * TILE - hitboxH;
            int maxIterations = 200; // Safety limit
            int iteration = 0;
            
            while (iteration < maxIterations)
            {
                // Move down 8 pixels
                scanY_px += 8;
                playerY_fixed = scanY_px << 8;
                
                // Process camera scroll (matching famidash's process_y_scroll)
                ProcessCameraScrollDuringSpiderScan();
                
                // Check if too low
                if (scanY_px >= maxY_px)
                {
                    AppendSimDebug($"[SPIDER_DOWN] Hit bottom boundary at Y={scanY_px}");
                    playerY_fixed = maxY_px << 8;
                    break;
                }
                
                // Check for floor collision
                var (collided, eject) = BgCollD_Spider(playerX_px, scanY_px + hitboxOffsetY, hitboxW, hitboxH, groundRowsToReserve);
                if (collided)
                {
                    // Use eject amount to position precisely at collision surface
                    scanY_px -= eject;
                    AppendSimDebug($"[SPIDER_DOWN] Found floor, eject={eject}, final scanY={scanY_px}");
                    playerY_fixed = scanY_px << 8;
                    break;
                }
                
                iteration++;
            }
            
            if (iteration >= maxIterations)
            {
                AppendSimDebug($"[SPIDER_DOWN] Max iterations reached, stopping at Y={scanY_px}");
                playerY_fixed = scanY_px << 8;
            }
        }
        
        /// <summary>
        /// bg_coll_D_spider() - Spider-specific floor collision check
        /// Uses LEFT_POS and RIGHT_POS with 3-pixel inset from edges
        /// Returns (collided, ejectAmount) where ejectAmount is pixels to move up
        /// </summary>
        private (bool collided, int ejectAmount) BgCollD_Spider(int playerX_px, int playerY_px, int width, int height, int groundRowsToReserve)
        {
            // LEFT_POS and RIGHT_POS with 3-pixel inset
            int leftX = playerX_px + 3;
            int rightX = playerX_px + width - 3;
            
            // Check bottom of hitbox
            int checkY_px = playerY_px + height;
            
            // Convert world Y to tile Y (accounting for ground offset)
            int tileY = (checkY_px / TILE) + groundRowsToReserve;
            
            // Check if beyond map bottom (ground layer = solid)
            if (tileY >= mapHeight)
            {
                int groundTop_world = (mapHeight - groundRowsToReserve) * TILE;
                int eject = checkY_px - groundTop_world;
                return (true, eject);
            }
            
            if (tileY < 0) return (false, 0);
            
            // Check left position
            int leftTileX = leftX / TILE;
            if (leftTileX >= 0 && leftTileX < mapWidth)
            {
                int tileIdx = tileY * mapWidth + leftTileX;
                if (tileIdx >= 0 && tileIdx < tiles.Length)
                {
                    int tileId = tiles[tileIdx];
                    var collision = MetatileCollisionTable.GetCollision((byte)tileId);
                    
                    if (IsSolidCollisionForSpider(collision))
                    {
                        // Get collision bounds for this tile type
                        var (colLeft, colTop, colRight, colBottom) = GetCollisionBoundsForType(collision);
                        
                        // Calculate tile top in world coordinates
                        int tileTopLeft_world = (tileY - groundRowsToReserve) * TILE;
                        // Add the collision top offset to get actual collision surface
                        int collisionTop_world = tileTopLeft_world + colTop;
                        
                        // Only collide if checkY has reached or passed the collision surface
                        if (checkY_px >= collisionTop_world)
                        {
                            int eject = checkY_px - collisionTop_world;
                            return (true, eject);
                        }
                    }
                }
            }
            
            // Check right position
            int rightTileX = rightX / TILE;
            if (rightTileX >= 0 && rightTileX < mapWidth)
            {
                int tileIdx = tileY * mapWidth + rightTileX;
                if (tileIdx >= 0 && tileIdx < tiles.Length)
                {
                    int tileId = tiles[tileIdx];
                    var collision = MetatileCollisionTable.GetCollision((byte)tileId);
                    
                    if (IsSolidCollisionForSpider(collision))
                    {
                        // Get collision bounds for this tile type
                        var (colLeft, colTop, colRight, colBottom) = GetCollisionBoundsForType(collision);
                        
                        // Calculate tile top in world coordinates
                        int tileTopLeft_world = (tileY - groundRowsToReserve) * TILE;
                        // Add the collision top offset to get actual collision surface
                        int collisionTop_world = tileTopLeft_world + colTop;
                        
                        // Only collide if checkY has reached or passed the collision surface
                        if (checkY_px >= collisionTop_world)
                        {
                            int eject = checkY_px - collisionTop_world;
                            return (true, eject);
                        }
                    }
                }
            }
            
            return (false, 0);
        }
        
        /// <summary>
        /// bg_coll_U_spider() - Spider-specific ceiling collision check
        /// Uses LEFT_POS and RIGHT_POS with 3-pixel inset from edges
        /// Returns (collided, ejectAmount) where ejectAmount is pixels to move down
        /// </summary>
        private (bool collided, int ejectAmount) BgCollU_Spider(int playerX_px, int playerY_px, int width, int height, int groundRowsToReserve)
        {
            // LEFT_POS and RIGHT_POS with 3-pixel inset
            int leftX = playerX_px + 3;
            int rightX = playerX_px + width - 3;
            
            // Check top of hitbox
            int checkY_px = playerY_px;
            
            // Convert world Y to tile Y (accounting for ground offset)
            int tileY = (checkY_px / TILE) + groundRowsToReserve;
            
            // Check if above map top (solid ceiling)
            if (tileY < 0)
            {
                int eject = 0 - checkY_px; // Distance from checkY to world Y=0
                return (true, eject);
            }
            
            if (tileY >= mapHeight) return (false, 0);
            
            // Check left position
            int leftTileX = leftX / TILE;
            if (leftTileX >= 0 && leftTileX < mapWidth)
            {
                int tileIdx = tileY * mapWidth + leftTileX;
                if (tileIdx >= 0 && tileIdx < tiles.Length)
                {
                    int tileId = tiles[tileIdx];
                    var collision = MetatileCollisionTable.GetCollision((byte)tileId);
                    
                    if (IsSolidCollisionForSpider(collision))
                    {
                        // Get collision bounds for this tile type
                        var (colLeft, colTop, colRight, colBottom) = GetCollisionBoundsForType(collision);
                        
                        // Calculate tile top in world coordinates
                        int tileTopLeft_world = (tileY - groundRowsToReserve) * TILE;
                        // Add the collision bottom offset to get actual collision surface
                        int collisionBottom_world = tileTopLeft_world + colBottom;
                        
                        int eject = collisionBottom_world - checkY_px;
                        return (true, eject);
                    }
                }
            }
            
            // Check right position
            int rightTileX = rightX / TILE;
            if (rightTileX >= 0 && rightTileX < mapWidth)
            {
                int tileIdx = tileY * mapWidth + rightTileX;
                if (tileIdx >= 0 && tileIdx < tiles.Length)
                {
                    int tileId = tiles[tileIdx];
                    var collision = MetatileCollisionTable.GetCollision((byte)tileId);
                    
                    if (IsSolidCollisionForSpider(collision))
                    {
                        // Get collision bounds for this tile type
                        var (colLeft, colTop, colRight, colBottom) = GetCollisionBoundsForType(collision);
                        
                        // Calculate tile top in world coordinates
                        int tileTopLeft_world = (tileY - groundRowsToReserve) * TILE;
                        // Add the collision bottom offset to get actual collision surface
                        int collisionBottom_world = tileTopLeft_world + colBottom;
                        
                        int eject = collisionBottom_world - checkY_px;
                        return (true, eject);
                    }
                }
            }
            
            return (false, 0);
        }
        
        /// <summary>
        /// Checks if a collision type is solid for spider (excludes death tiles)
        /// </summary>
        private bool IsSolidCollisionForSpider(MetatileCollision collision)
        {
            return collision != MetatileCollision.COL_NONE && 
                   collision != MetatileCollision.COL_DEATH_BOTTOM;
        }
        
        /// <summary>
        /// Process camera scroll during spider scan (matching famidash's process_y_scroll)
        /// </summary>
        private void ProcessCameraScrollDuringSpiderScan()
        {
            try
            {
                if (physicsEnabled && jumpedOnce)
                {
                    int playerCenterScreenY = (playerY_fixed >> 8) + (playerVisualHeight / 2) - (cameraY_fixed >> 8);
                    int topThreshold = 5 * TILE;
                    int bottomThreshold = NES_H * TILE - 5 * TILE;

                    if (playerCenterScreenY <= topThreshold)
                    {
                        int need = topThreshold - playerCenterScreenY;
                        int camMove = Math.Min(need, (cameraY_fixed >> 8));
                        cameraY_fixed -= (camMove << 8);
                        if (cameraY_fixed < 0) cameraY_fixed = 0;
                    }
                    else if (playerCenterScreenY >= bottomThreshold)
                    {
                        int need = playerCenterScreenY - bottomThreshold;
                        int maxCameraY_fixed = Math.Max(0, (mapHeight - NES_H) * TILE) << 8;
                        int camAvail = (maxCameraY_fixed - cameraY_fixed) >> 8;
                        int camMove = Math.Min(need, camAvail);
                        cameraY_fixed += (camMove << 8);
                        if (cameraY_fixed > maxCameraY_fixed) cameraY_fixed = maxCameraY_fixed;
                    }
                }
            }
            catch { }
        }
        
        // blackOrbed flag for spider black orb hold mechanic
        private bool blackOrbed = false;
    }
}
