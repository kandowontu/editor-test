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
                int playerX_px_orb = (playerX_fixed >> 8) + 1;
                int playerY_px_orb = playerY_fixed >> 8;
                int hitboxW_orb = (currplayer_mini != 0) ? 8 : 15;
                int hitboxH_orb = (currplayer_mini != 0) ? 7 : 15;
                
                // NES: Generic.y += ((0x10 - height) >> 1); Normal: +0, Mini: +4
if (currplayer_mini != 0)
                {
                    playerY_px_orb += 4;
                }
                
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
            int baseTableIdx = (miniMode ? 4 : 0);
            bool gravityInverted = gravityFlipped;
            int gravityMultiplier = gravityInverted ? -1 : 1;
            
            tmpfallspeed = GameModePhysics.SPIDER_MAX_FALLSPEED(baseTableIdx) * gravityMultiplier;
            tmpgravity = GameModePhysics.SPIDER_GRAVITY(baseTableIdx) * gravityMultiplier;
            
            // Apply gravity and integrate position
            CommonGravityRoutine_Fresh();
            
            // Spider eject - check collision and zero velocity if grounded
            // Offset collision check down 1 pixel (normal) or up 2 pixels (inverted)
            int offsetY = (playerY_fixed >> 8) + (gravityInverted ? -2 : 1);
            SpiderEject_Fresh(offsetY);

            // bg_coll_U_D_checks can set cube_data death as a side effect while
            // still returning no solid ejection.  Do not let a same-frame input
            // teleport the simulator away after that NES death has occurred.
            if (deathTriggered)
                return;

            // Update the global onGround flag based on whether we're grounded
            // If velocity is 0 after eject, we hit something and are grounded
            onGround = (playerVelY_fixed == 0);
            AppendSimDebug($"[SPIDER] After eject: onGround={onGround}, velY={playerVelY_fixed}");
            
            // Input handling
            bool holdingJump = IsXDownAsync() || keyXHeld;
            int pressCount = Interlocked.Exchange(ref keyXPressedCount, 0);
            bool pressedJump = pressCount > 0;
            
            // Spider can only teleport when velocity is 0 (grounded) and not orbed
            bool canTeleport = (playerVelY_fixed == 0) && !orbed[currplayer];
            bool bufferedJump = holdingJump && orbBufferActive[currplayer] && !orbHoldSuppressing[currplayer];
            
            if (!gravityFlipped)
            {
                // Normal gravity - on floor, can teleport to ceiling
                if ((pressedJump || bufferedJump || (holdingJump && blackOrbed)) && canTeleport)
                {
                    AppendSimDebug($"[SPIDER] TELEPORTING TO CEILING! Start Y={playerY_fixed >> 8}");
                    
                    // Flip gravity and table index
                    currplayer_gravity = 0xFF; // GRAVITY_UP
                    gravityFlipped = true;
                    gravityReversed = true;
                    UpdateCurrplayerTableIdx_Fresh();
                    
                    // Scan upward for ceiling (positions player at ceiling surface)
                    SpiderUpWait_Fresh();
					if (deathTriggered)
						return;
                    
                    playerVelY_fixed = 0;
                    try { Dispatcher?.BeginInvoke(new Action(() => UpdatePlayerIconFlip())); } catch { }
                    
                    AppendSimDebug($"[SPIDER] Teleported to ceiling Y={playerY_fixed >> 8}");
                    blackOrbed = false;
                    orbBufferActive[currplayer] = false;
                    ballInputBufferCountdown[currplayer] = 0;
                }
                else if (!holdingJump) 
                {
                    blackOrbed = false;
                }
            }
            else
            {
                // Inverted gravity - on ceiling, can teleport to floor
                if ((pressedJump || bufferedJump || (holdingJump && blackOrbed)) && canTeleport)
                {
                    AppendSimDebug($"[SPIDER] TELEPORTING TO FLOOR! Start Y={playerY_fixed >> 8}");
                    
                    // Flip gravity and table index
                    currplayer_gravity = 0x00; // GRAVITY_DOWN
                    gravityFlipped = false;
                    gravityReversed = false;
                    UpdateCurrplayerTableIdx_Fresh();
                    
                    // Scan downward for floor (positions player at floor surface)
                    SpiderDownWait_Fresh();
					if (deathTriggered)
						return;
                    
                    playerVelY_fixed = 0;
                    try { Dispatcher?.BeginInvoke(new Action(() => UpdatePlayerIconFlip())); } catch { }
                    
                    AppendSimDebug($"[SPIDER] Teleported to floor Y={playerY_fixed >> 8}");
                    blackOrbed = false;
                    orbBufferActive[currplayer] = false;
                    ballInputBufferCountdown[currplayer] = 0;
                }
                else if (!holdingJump)
                {
                    blackOrbed = false;
                }
            }

            // NES consumes currplayer_slope_frames in x_movement_coll(), after
            // spider_movement() has already run the grounded teleport gate.
            // Applying it before this gate makes slope landings miss a valid
            // same-frame spider flip.
            UpdateSlopeCounters_Fresh();
            onGround = (playerVelY_fixed == 0);
            
            // Record position for trail (skip during pathfinder speculative simulation)
            if (!pfSimulating)
            try
            {
                int playerWorldCenterX_px = (playerX_fixed >> 8) + (playerVisualWidth / 2);
                int playerY_px_trail = playerY_fixed >> 8;
                // Apply mini mode offset for trail to match visual position
                bool isMini_trail = (currplayer_mini != 0);
                if (isMini_trail)
                {
                    playerY_px_trail += 4;
                }
                int playerWorldCenterY_px = playerY_px_trail + (playerVisualHeight / 2);
                
                // Record to appropriate path list based on which player is active
                if (currplayer == 0)
                    recordedPlayerPath.Add((playerWorldCenterX_px, playerWorldCenterY_px));
                else if (dual)
                    RecordP2PathPoint(playerWorldCenterX_px, playerWorldCenterY_px);
            }
            catch { }
        }
        
        /// <summary>
        /// spider_eject() from gamemode_spider.h
        /// Checks collision and zeros velocity if grounded
        /// offsetY is the Y position to check (already offset by +1 or -2)
        /// </summary>
        private void SpiderEject_Fresh(int offsetY)
        {
            bool isMini = (miniMode);
            int hitboxW = isMini ? 8 : 15;
            int hitboxH = isMini ? 7 : 15;
            int hitboxOffsetY = isMini ? ((0x10 - hitboxH) >> 1) : 0;
            int collisionX = (playerX_fixed >> 8);
            int collisionY = offsetY + hitboxOffsetY;
            
            int groundRowsToReserve = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;

			if (!gravityFlipped)
            {
                // Normal gravity - check slopes first, then floor collision
                bool slopeHit = bg_coll_D_slopes(offsetY);
                if (slopeHit)
                {
                    if (eject_D > 0)
                    {
                        int currentY_px = playerY_fixed >> 8;
                        int newY_px = currentY_px - eject_D;
                        playerY_fixed = newY_px << 8;
                    }
                    playerVelY_fixed = 0;
                    wasZeroedByCollisionLastFrame = true;
                }
                else
                {
                    // spider_eject calls bg_coll_D (3 probes, no inset), not bg_coll_D_spider.
                    // NES bg_coll_D only runs ordinary bottom probes while moving
                    // downward/non-negative; its slope pass above is unconditional.
                    var (collided, ejectAmount) = playerVelY_fixed >= 0
                        ? BgCollD_Spider(collisionX, collisionY, hitboxW, hitboxH, groundRowsToReserve, useEjectProbes: true)
                        : (false, 0);
                    if (collided)
                    {
                        int currentY_px = playerY_fixed >> 8;
                        int newY_px = currentY_px - ejectAmount;
                        playerY_fixed = newY_px << 8;
                        playerVelY_fixed = 0;
                        wasZeroedByCollisionLastFrame = true;
                        AppendSimDebug($"[SPIDER_EJECT] Floor collision: eject={ejectAmount}, Y {currentY_px} -> {newY_px}");
                    }
                    else
                    {
                        // No collision - allow gravity to apply
                        wasZeroedByCollisionLastFrame = false;
                    }
                }
            }
            else
            {
                // Inverted gravity - check slopes first, then ceiling collision
                bool slopeHit = bg_coll_U_slopes(offsetY);
                if (slopeHit)
                {
                    int currentY_px = playerY_fixed >> 8;
                    // NES spider_eject: high_byte(currplayer_y) -= eject_U + 1.
                    // bg_coll_return_slope_U stores eject_U as -tmp8.
                    int newY_px = currentY_px - eject_U - 1;
                    playerY_fixed = newY_px << 8;
                    playerVelY_fixed = 0;
                    wasZeroedByCollisionLastFrame = true;
                    AppendSimDebug($"[SPIDER_EJECT] Ceiling slope: ejectU={eject_U}, Y {currentY_px} -> {newY_px}");
                }
                else
                {
                // Inverted gravity - check ceiling collision
                // spider_eject calls bg_coll_U (3 probes, no inset), not bg_coll_U_spider.
                // NES bg_coll_U only runs ordinary top probes while moving upward;
                // its slope pass above is unconditional.
                var (collided, ejectAmount) = playerVelY_fixed < 0
                    ? BgCollU_Spider(collisionX, collisionY, hitboxW, hitboxH, groundRowsToReserve, useEjectProbes: true)
                    : (false, 0);
                if (collided)
                {
                    // NES probes bg_coll_U with Generic.y offset/mini centering,
                    // but applies eject_U + 1 back to currplayer_y itself.  The
                    // mini probe offset is detection-only and cancels out here.
                    int newY_px = collisionY + ejectAmount - hitboxOffsetY;
                    playerY_fixed = newY_px << 8;
                    playerVelY_fixed = 0;
                    wasZeroedByCollisionLastFrame = true;  // Signal gravity not to re-apply next frame
                    AppendSimDebug($"[SPIDER_EJECT] Ceiling collision: eject={ejectAmount}, Y -> {newY_px}");
                }
                else
                {
                    // No collision - allow gravity to apply
                    wasZeroedByCollisionLastFrame = false;
                }
                }
            }
        }
        
        /// <summary>
        /// spider_up_wait() from gamemode_spider.h
        /// Scans upward in 8-pixel steps until ceiling is found
        /// </summary>
        private void SpiderUpWait_Fresh(int playerXBias = 0)
        {
            bool isMini = miniMode;
            // This wait is entered from sprite_collide, which uses the 8x8 box
            // only for wave. Snake intentionally inherits the cube dimensions.
            bool wave = currentGameMode == 6;
            int hitboxW = wave ? 8 : (isMini ? 8 : 15);
            int hitboxH = wave ? 8 : (isMini ? 7 : 15);
            int hitboxOffsetY = isMini ? ((0x10 - hitboxH) >> 1) : 0;
            int playerX_px = (playerX_fixed >> 8) + playerXBias;
            int groundRowsToReserve = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
            
            int maxIterations = 200; // Safety limit
            int iteration = 0;
            int screenY_fixed = playerY_fixed - cameraY_fixed;
            
            while (iteration < maxIterations)
            {
                // NES decrements high_byte(currplayer_y), which is screen-space.
                screenY_fixed = ((((screenY_fixed >> 8) - 8) << 8) | (screenY_fixed & 0xFF));
                playerY_fixed = cameraY_fixed + screenY_fixed;

                // Process camera scroll (matching famidash's process_y_scroll)
                ProcessCameraScrollDuringSpiderScan();
                screenY_fixed = playerY_fixed - cameraY_fixed;
                int screenY_px = screenY_fixed >> 8;
                int worldY_px = playerY_fixed >> 8;

                // NES death guard checks high_byte(currplayer_y), not world Y.
                if (screenY_px <= 0x07)
                {
                    AppendSimDebug($"[SPIDER_UP] Hit top boundary at screenY={screenY_px}");
                    // spider_up_wait sets cube_data's death bit. state_game does
                    // not observe it until after x_movement and clears it while
                    // P1's resulting X is still <= $20.
                    simulatorSpiderBoundaryDeathPending[currplayer] = true;
                    // The caller always executes high_byte(currplayer_y) -=
                    // eject_U after spider_up_wait, including this guard exit.
                    ApplySimulatorNesPlayerYHighSubtract(eject_U);
                    break;
                }

                // Check for ceiling collision
                UpdateSimulatorSpiderScanUpEjectSideEffect(playerX_px,
                    worldY_px + hitboxOffsetY, hitboxW, groundRowsToReserve);
                var (collided, eject) = BgCollU_Spider(playerX_px, worldY_px + hitboxOffsetY, hitboxW, hitboxH, groundRowsToReserve);
                if (collided)
                {
                    // NES applies the post-wait eject to high_byte(currplayer_y).
                    screenY_fixed = ((((screenY_fixed >> 8) + eject) << 8) | (screenY_fixed & 0xFF));
                    playerY_fixed = cameraY_fixed + screenY_fixed;
                    AppendSimDebug($"[SPIDER_UP] Found ceiling, eject={eject}, final screenY={screenY_fixed >> 8}");
                    break;
                }
                
                iteration++;
            }
            
            if (iteration >= maxIterations)
            {
                AppendSimDebug($"[SPIDER_UP] Max iterations reached, stopping at screenY={(playerY_fixed - cameraY_fixed) >> 8}");
            }
        }
        
        /// <summary>
        /// spider_down_wait() from gamemode_spider.h
        /// Scans downward in 8-pixel steps until floor is found
        /// </summary>
        private void SpiderDownWait_Fresh(int playerXBias = 0)
        {
            bool isMini = (miniMode);
            bool wave = currentGameMode == 6;
            int hitboxW = wave ? 8 : (isMini ? 8 : 15);
            int hitboxH = wave ? 8 : (isMini ? 7 : 15);
            int hitboxOffsetY = isMini ? ((0x10 - hitboxH) >> 1) : 0;
            int playerX_px = (playerX_fixed >> 8) + playerXBias;
            int groundRowsToReserve = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
            
            int maxIterations = 200; // Safety limit
            int iteration = 0;
            int screenY_fixed = playerY_fixed - cameraY_fixed;
            
            while (iteration < maxIterations)
            {
                // NES increments high_byte(currplayer_y), which is screen-space.
                screenY_fixed = ((((screenY_fixed >> 8) + 8) << 8) | (screenY_fixed & 0xFF));
                playerY_fixed = cameraY_fixed + screenY_fixed;
                
                // Process camera scroll (matching famidash's process_y_scroll)
                ProcessCameraScrollDuringSpiderScan();
                screenY_fixed = playerY_fixed - cameraY_fixed;
                int screenY_px = screenY_fixed >> 8;
                int worldY_px = playerY_fixed >> 8;
                
                // NES death guard checks high_byte(currplayer_y), not world Y.
                if (screenY_px >= 0xF8)
                {
                    AppendSimDebug($"[SPIDER_DOWN] Hit bottom boundary at screenY={screenY_px}");
                    simulatorSpiderBoundaryDeathPending[currplayer] = true;
                    ApplySimulatorNesPlayerYHighSubtract(eject_D);
                    break;
                }

                // Check for floor collision
                UpdateSimulatorSpiderScanDownEjectSideEffect(playerX_px,
                    worldY_px + hitboxOffsetY, hitboxW, hitboxH, groundRowsToReserve);
                var (collided, eject) = BgCollD_Spider(playerX_px, worldY_px + hitboxOffsetY, hitboxW, hitboxH, groundRowsToReserve);
                if (collided)
                {
                    // NES applies the post-wait eject to high_byte(currplayer_y).
                    screenY_fixed = ((((screenY_fixed >> 8) - eject) << 8) | (screenY_fixed & 0xFF));
                    playerY_fixed = cameraY_fixed + screenY_fixed;
                    AppendSimDebug($"[SPIDER_DOWN] Found floor, eject={eject}, final screenY={screenY_fixed >> 8}");
                    break;
                }
                
                iteration++;
            }
            
            if (iteration >= maxIterations)
            {
                AppendSimDebug($"[SPIDER_DOWN] Max iterations reached, stopping at screenY={(playerY_fixed - cameraY_fixed) >> 8}");
            }
        }

        private void ApplySimulatorNesPlayerYHighSubtract(int amount)
        {
            int screenY_fixed = playerY_fixed - cameraY_fixed;
            int rawScreenY = screenY_fixed & 0xFFFF;
            int high = unchecked((byte)((rawScreenY >> 8) - unchecked((byte)amount)));
            playerY_fixed = cameraY_fixed + (high << 8) + (rawScreenY & 0xFF);
        }

        private void UpdateSimulatorSpiderScanUpEjectSideEffect(int playerX_px,
            int playerY_px, int width, int groundRowsToReserve)
        {
            int tileY = playerY_px / TILE + groundRowsToReserve;
            if (tileY < 0 || tileY >= mapHeight)
                return;

            int playerWorldX = playerX_fixed >> 8;
            int currplayerScreenX = playerWorldX - Math.Max(0, playerWorldX - 0x50);
            int[] probes = { playerX_px + 3, playerX_px + width - 3 };
            foreach (int probeX in probes)
            {
                int tileX = probeX / TILE;
                if (tileX < 0 || tileX >= mapWidth)
                    continue;
                MetatileCollision collision = MetatileCollisionTable.GetCollision(
                    (byte)SharedPhysics.MapTileForCollision(tiles[tileY * mapWidth + tileX]));
                if (collision == MetatileCollision.COL_NONE)
                    continue;

                bool fullSolid = collision == MetatileCollision.COL_NO_SIDE ||
                    collision == MetatileCollision.COL_FLOOR_CEIL ||
                    (collision == MetatileCollision.COL_ALL && currplayerScreenX >= 0x10);
                int tmp8 = (playerY_px + _sim_nesCoordOffset) & 0x0F;
                eject_U = unchecked((sbyte)(byte)((fullSolid ? 0xF0 : 0xF8) | tmp8));
                if (IsSolidCollisionForSpider(collision, probeX, playerY_px))
                    return;
            }
        }

        private void UpdateSimulatorSpiderScanDownEjectSideEffect(int playerX_px,
            int playerY_px, int width, int height, int groundRowsToReserve)
        {
            int probeY = playerY_px + height;
            int tileY = probeY / TILE + groundRowsToReserve;
            if (tileY < 0 || tileY >= mapHeight)
                return;

            int[] probes = { playerX_px + 3, playerX_px + width - 3 };
            foreach (int probeX in probes)
            {
                int tileX = probeX / TILE;
                if (tileX < 0 || tileX >= mapWidth)
                    continue;
                MetatileCollision collision = MetatileCollisionTable.GetCollision(
                    (byte)SharedPhysics.MapTileForCollision(tiles[tileY * mapWidth + tileX]));
                if (collision == MetatileCollision.COL_NONE)
                    continue;

                eject_D = (probeY + _sim_nesCoordOffset) & 0x0F;
                if (IsSolidCollisionForSpider(collision, probeX, probeY))
                    return;
            }
        }
        
        /// <summary>
        /// Spider floor collision check.
        /// useEjectProbes=false: bg_coll_D_spider style (2 probes, 3px inset) — for scan.
        /// useEjectProbes=true:  bg_coll_D style (3 probes, no inset) — for spider_eject.
        /// Returns (collided, ejectAmount) where ejectAmount is pixels to move up
        /// </summary>
        private (bool collided, int ejectAmount) BgCollD_Spider(int playerX_px, int playerY_px, int width, int height, int groundRowsToReserve, bool useEjectProbes = false)
        {
            // Check bottom of hitbox
            int checkY_px = playerY_px + height;
            
            // Convert world Y to tile Y (accounting for ground offset)
            int tileY = (checkY_px / TILE) + groundRowsToReserve;
            
            // Check if beyond map bottom (ground layer = solid)
            if (tileY >= mapHeight)
            {
				if (!useEjectProbes)
					return (false, 0);
                int groundTop_world = (mapHeight - groundRowsToReserve) * TILE;
                int eject = checkY_px - groundTop_world;
                return (true, eject);
            }
            
            if (tileY < 0) return (false, 0);

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
                int probeTileX = probeX / TILE;
                if (probeTileX >= 0 && probeTileX < mapWidth)
                {
                    int tileIdx = tileY * mapWidth + probeTileX;
                    if (tileIdx >= 0 && tileIdx < tiles.Length)
                    {
                        int tileId = tiles[tileIdx];
                        var collision = MetatileCollisionTable.GetCollision((byte)SharedPhysics.MapTileForCollision(tileId));
                        
                        if (CheckSpiderEjectDirectionalDeath(collision, probeX, checkY_px, isTop: false, useEjectProbes))
                            continue;

                        if (IsSolidCollisionForSpider(collision, probeX, checkY_px))
                        {
                            var (colLeft, colTop, colRight, colBottom) = SharedPhysics.GetCollisionBounds(collision);
                            int tileTopLeft_world = (tileY - groundRowsToReserve) * TILE;
                            int collisionTop_world = tileTopLeft_world + colTop;
                            
                            if (checkY_px >= collisionTop_world)
                            {
                                int eject = checkY_px - collisionTop_world;
                                return (true, eject);
                            }
                        }
                    }
                }
            }
            
            return (false, 0);
        }
        
        /// <summary>
        /// Spider ceiling collision check.
        /// useEjectProbes=false: bg_coll_U_spider style (2 probes, 3px inset) — for scan.
        /// useEjectProbes=true:  bg_coll_U style (3 probes, no inset) — for spider_eject.
        /// Returns (collided, ejectAmount) where ejectAmount is pixels to move down
        /// </summary>
        private (bool collided, int ejectAmount) BgCollU_Spider(int playerX_px, int playerY_px, int width, int height, int groundRowsToReserve, bool useEjectProbes = false)
        {
            // Ordinary bg_coll_U uses Generic.y + 1.  bg_coll_U_spider, used by
            // the teleport scan, uses Generic.y with no bias.
            int checkY_px = playerY_px + (useEjectProbes ? 1 : 0);
            
            // Convert world Y to tile Y (accounting for ground offset)
            int tileY = (checkY_px / TILE) + groundRowsToReserve;
            
            // Check if above map top (solid ceiling)
            if (tileY < 0)
            {
				if (!useEjectProbes)
					return (false, 0);
                int eject = 0 - checkY_px;
                return (true, eject);
            }
            
            if (tileY >= mapHeight) return (false, 0);

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
                int probeTileX = probeX / TILE;
                if (probeTileX >= 0 && probeTileX < mapWidth)
                {
                    int tileIdx = tileY * mapWidth + probeTileX;
                    if (tileIdx >= 0 && tileIdx < tiles.Length)
                    {
                        int tileId = tiles[tileIdx];
                        var collision = MetatileCollisionTable.GetCollision((byte)SharedPhysics.MapTileForCollision(tileId));
                        
                        if (CheckSpiderEjectDirectionalDeath(collision, probeX, checkY_px, isTop: true, useEjectProbes))
                            continue;

                        if (IsSolidCollisionForSpider(collision, probeX, checkY_px))
                        {
							if (!useEjectProbes)
							{
								int scanEject = SharedPhysics.NesSpiderScanUpEject(
									collision, playerY_px, _sim_nesCoordOffset);
								return (true, scanEject);
							}
                            var (colLeft, colTop, colRight, colBottom) = SharedPhysics.GetCollisionBounds(collision);
                            int tileTopLeft_world = (tileY - groundRowsToReserve) * TILE;
                            int collisionBottom_world = tileTopLeft_world + colBottom;
                            
                            int eject = collisionBottom_world - checkY_px;
                            return (true, eject);
                        }
                    }
                }
            }
            
            return (false, 0);
        }

        private bool CheckSpiderEjectDirectionalDeath(MetatileCollision collision, int probeX,
            int probeY, bool isTop, bool useEjectProbes)
        {
            // Only ordinary bg_coll_U/bg_coll_D run bg_coll_U_D_checks.  The
            // two-probe spider wait helpers share the solid lookup but do not
            // need a separate simulator death side effect here.
            if (!useEjectProbes || MainWindow.Option_NoDeath)
                return false;

            int localX = probeX & 0x0F;
            int localY = probeY & 0x0F;
            bool killed = isTop
                ? collision == MetatileCollision.COL_DEATH_TOP && localY < 0x06 && localX >= 0x05 && localX <= 0x07
                : collision == MetatileCollision.COL_DEATH_BOTTOM && localY > 0x0A && localX >= 0x05 && localX <= 0x07;

            if (!killed)
                return false;

            deathTriggered = true;
            deathTileX = probeX;
            deathTileY = probeY;
            paused = true;
            _ = StopMusicAsync();
            AppendSimDebug($"[SPIDER_EJECT] Directional death side effect at ({probeX},{probeY})");
            return true;
        }
        
        /// <summary>
        /// Checks if a collision type is solid for spider (excludes death tiles)
        /// </summary>
        private bool IsSolidCollisionForSpider(MetatileCollision collision, int probeX, int probeY)
        {
            if (collision == MetatileCollision.COL_NONE ||
                SharedPhysics.IsDeathCollision(collision) ||
                SharedPhysics.IsSlopeTile(collision))
                return false;

            int playerWorldX = playerX_fixed >> 8;
            int currplayerScreenX = playerWorldX - Math.Max(0, playerWorldX - 0x50);

            // bg_coll_U_D_checks() has a startup/left-edge exception for
            // COL_ALL. COL_NO_SIDE and COL_FLOOR_CEIL remain unconditional.
            switch (collision)
            {
                case MetatileCollision.COL_NO_SIDE:
                case MetatileCollision.COL_FLOOR_CEIL:
                    return true;
                case MetatileCollision.COL_ALL:
                    return currplayerScreenX >= 0x10;
            }

            int localX = probeX & 0x0F;
            int localY = probeY & 0x0F;

            // bg_coll_mini_blocks() skips every non-floor/ceiling type while
            // high_byte(currplayer_x) is below $10.
            if (currplayerScreenX >= 0x10)
            {
                switch (collision)
                {
                    case MetatileCollision.COL_UP_LEFT:
                        return localY < 8 && localX < 8;
                    case MetatileCollision.COL_UP_RIGHT:
                        return localY < 8 && localX >= 8;
                    case MetatileCollision.COL_DOWN_LEFT:
                    case MetatileCollision.COL_LEFT_SPIKE_BLOCK:
                        return localY >= 8 && localX < 8;
                    case MetatileCollision.COL_DOWN_RIGHT:
                    case MetatileCollision.COL_RIGHT_SPIKE_BLOCK:
                        return localY >= 8 && localX >= 8;
                    case MetatileCollision.COL_BOTTOM_LEFT_SPIKE:
                    case MetatileCollision.COL_BOTTOM_RIGHT_SPIKE:
                    case MetatileCollision.COL_BOTTOM_CENTER_SPIKE:
                    case MetatileCollision.COL_BOTTOM_SPIKES:
                        return localY >= 8;
                    case MetatileCollision.COL_TOP_CENTER_SPIKE:
                        return localY < 8;
                    case MetatileCollision.COL_LEFT:
                        return localX < 8;
                    case MetatileCollision.COL_RIGHT:
                        return localX >= 8;
                    case MetatileCollision.COL_TOP_LEFT_BOTTOM_RIGHT:
                        return (localX < 8 && localY < 8) || (localX >= 8 && localY >= 8);
                    case MetatileCollision.COL_TOP_RIGHT_BOTTOM_LEFT:
                        return (localX < 8 && localY >= 8) || (localX >= 8 && localY < 8);
                    case MetatileCollision.COL_TOP_RIGHT_STAIRS:
                        return !(localY >= 8 && localX < 8);
                    case MetatileCollision.COL_TOP_LEFT_STAIRS:
                        return !(localY >= 8 && localX >= 8);
                    case MetatileCollision.COL_BOTTOM_RIGHT_STAIRS:
                        return !(localY < 8 && localX < 8);
                    case MetatileCollision.COL_BOTTOM_LEFT_STAIRS:
                        return !(localY < 8 && localX >= 8);
                }
            }

            // bg_coll_top_bottom_slabs() still runs in the left-edge zone.
            return collision switch
            {
                MetatileCollision.COL_BOTTOM => localY >= 8,
                MetatileCollision.COL_TOP => localY < 8,
                _ => false
            };
        }
        
        /// <summary>
        /// Process camera scroll during spider scan (matching famidash's process_y_scroll)
        /// </summary>
        private void ProcessCameraScrollDuringSpiderScan()
        {
            try
            {
                ApplySimulatorNesCameraScroll();
            }
            catch { }
            
            // Record position for trail (skip during pathfinder speculative simulation)
            if (!pfSimulating)
            try
            {
                int playerWorldCenterX_px = (playerX_fixed >> 8) + (playerVisualWidth / 2);
                int playerY_px_trail = playerY_fixed >> 8;
                // Apply mini mode offset for trail to match visual position
                bool isMini_trail = (currplayer_mini != 0);
                if (isMini_trail)
                {
                    playerY_px_trail += 4;
                }
                int playerWorldCenterY_px = playerY_px_trail + (playerVisualHeight / 2);
                
                // Record to appropriate path list based on which player is active
                if (currplayer == 0)
                    recordedPlayerPath.Add((playerWorldCenterX_px, playerWorldCenterY_px));
                else if (dual)
                    RecordP2PathPoint(playerWorldCenterX_px, playerWorldCenterY_px);
            }
            catch { }
        }
    }
}



