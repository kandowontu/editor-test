using System;
using System.Threading;

namespace FamidashEditor
{
    public partial class SimulatorWindow
    {
        // ========================================================================
        // CUBE PHYSICS - 1:1 Port from gamemode_cube.h
        // ========================================================================

        // Player state variables (matching famidash.h declarations)
        private byte[] mini = new byte[2];                    // 0 or 1 for each player
        private byte[] gravity = new byte[2];                 // 0 = normal, 1 = reversed (0xFF in C)
#pragma warning disable CS0414
        private byte dual = 0;                                // 0 = single player, 1 = dual mode
#pragma warning restore CS0414
        private byte currplayer = 0;                          // Current player index (0 or 1)
        
        /// <summary>
        /// Process cube physics for the current frame.
        /// Frame order: 1) Apply gravity, 2) Integrate velocity, 3) Check collisions, 4) Handle input
        /// </summary>
        private void ProcessCubePhysics()
        {
            try
            {
                // Read current mini and gravity state for player 0
                bool isMini = mini[currplayer] != 0;
                bool gravityReversed = gravity[currplayer] != 0;
                
                // Get physics constants based on mini mode
                int cubeGravity = isMini ? 0x6F : CUBE_GRAVITY;          // MINI_CUBE_GRAVITY : CUBE_GRAVITY
                int cubeJumpVel = isMini ? -0x4D0 : CUBE_JUMP_VEL;       // MINI_JUMP_VEL : JUMP_VEL
                int cubeMaxFall = isMini ? 0x600 : CUBE_MAX_FALLSPEED;   // MINI_CUBE_MAX_FALLSPEED : CUBE_MAX_FALLSPEED
                
                // ====================================================================
                // STEP 1: GRAVITY APPLICATION + INTEGRATION (common_gravity_routine)
                // ====================================================================
                // common_gravity_routine applies gravity AND integrates velocity into position
                // This must happen before collision detection
                
                // Check if we should apply gravity or clamp to max fall speed
                bool shouldApplyGravity = false;
                if (!gravityReversed)
                {
                    // Normal gravity: apply if not at max fall speed
                    shouldApplyGravity = (playerVelY_fixed < cubeMaxFall);
                }
                else
                {
                    // Reversed gravity: apply if not at max fall speed (negative)
                    shouldApplyGravity = (playerVelY_fixed > -cubeMaxFall);
                }
                
                if (shouldApplyGravity)
                {
                    // Apply gravity in the direction based on gravity flag
                    if (!gravityReversed)
                    {
                        // Normal gravity (downward, positive)
                        playerVelY_fixed += cubeGravity;
                        // Clamp to max fall speed
                        if (playerVelY_fixed > cubeMaxFall)
                            playerVelY_fixed = cubeMaxFall;
                    }
                    else
                    {
                        // Reversed gravity (upward, negative)
                        playerVelY_fixed -= cubeGravity;
                        // Clamp to max fall speed (negative)
                        if (playerVelY_fixed < -cubeMaxFall)
                            playerVelY_fixed = -cubeMaxFall;
                    }
                }
                
                // Integrate velocity into position (part of common_gravity_routine)
                playerY_fixed += playerVelY_fixed;
                
                // Clamp to world bounds
                if (playerY_fixed < 0) playerY_fixed = 0;
                int maxPlayerY_fixed = Math.Max(0, (mapHeight * TILE - playerVisualHeight)) << 8;
                if (playerY_fixed > maxPlayerY_fixed) playerY_fixed = maxPlayerY_fixed;
                
                // ====================================================================
                // STEP 2: COLLISION DETECTION (cube_eject, bg_coll_D2)
                // ====================================================================
                // Calculate hitbox in pixels
                int playerX_px = playerX_fixed >> 8;
                int playerY_px = playerY_fixed >> 8;
                
                // Cube hitbox is 15x15 pixels (CUBE_WIDTH/HEIGHT from physics_defines.h)
                const int CUBE_HITBOX_W = 15;
                const int CUBE_HITBOX_H = 15;
                
                int playerLeft_px = playerX_px;
                int playerRight_px = playerX_px + CUBE_HITBOX_W - 1;
                int playerTop_px = playerY_px;
                int playerBottom_px = playerY_px + CUBE_HITBOX_H - 1;
                
                // Check for floor collision (landing surface)
                // In normal gravity: check below player
                // In reversed gravity: check above player
                // Only do collision detection if class-level onGround flag allows it
                // (W key sets onGround=false to unstick player when inverting gravity)
                bool onGroundLocal = false;
                
                if (!gravityReversed)
                {
                    // Normal gravity: check tiles below the cube's bottom edge
                    // Bottom edge is at playerBottom_px
                    int tileBelowY = (playerBottom_px + 1) / TILE;  // Tile row below bottom edge
                    
                    if (tileBelowY >= 0 && tileBelowY < mapHeight)
                    {
                        // Check tiles that the cube's bottom edge overlaps
                        int tileLeftX = playerLeft_px / TILE;
                        int tileRightX = playerRight_px / TILE;
                        
                        for (int tx = tileLeftX; tx <= tileRightX; tx++)
                        {
                            if (tx < 0 || tx >= mapWidth) continue;
                            
                            int tileIdx = tileBelowY * mapWidth + tx;
                            if (tileIdx < 0 || tileIdx >= tiles.Length) continue;
                            
                            int tileId = tiles[tileIdx];
                            var collision = MetatileCollisionTable.GetCollision((byte)tileId);
                            
                            // Only check COL_ALL for now (solid block)
                            if (collision == MetatileCollision.COL_ALL)
                            {
                                // Player is colliding with floor - snap to top of tile
                                int tileTop_px = tileBelowY * TILE;
                                playerY_fixed = (tileTop_px - CUBE_HITBOX_H) << 8;
                                playerVelY_fixed = 0;
                                onGroundLocal = true;
                                break;
                            }
                        }
                    }
                }
                else
                {
                    // Reversed gravity: check tiles above the cube's top edge
                    // Top edge is at playerTop_px
                    int tileAboveY = (playerTop_px - 1) / TILE;  // Tile row above top edge
                    
                    if (tileAboveY >= 0 && tileAboveY < mapHeight)
                    {
                        // Check tiles that the cube's top edge overlaps
                        int tileLeftX = playerLeft_px / TILE;
                        int tileRightX = playerRight_px / TILE;
                        
                        for (int tx = tileLeftX; tx <= tileRightX; tx++)
                        {
                            if (tx < 0 || tx >= mapWidth) continue;
                            
                            int tileIdx = tileAboveY * mapWidth + tx;
                            if (tileIdx < 0 || tileIdx >= tiles.Length) continue;
                            
                            int tileId = tiles[tileIdx];
                            var collision = MetatileCollisionTable.GetCollision((byte)tileId);
                            
                            // Only check COL_ALL for now (solid block)
                            if (collision == MetatileCollision.COL_ALL)
                            {
                                // Player is colliding with ceiling (which acts as floor in reversed)
                                // Snap to bottom of tile
                                int tileBottom_px = (tileAboveY + 1) * TILE;
                                playerY_fixed = tileBottom_px << 8;
                                playerVelY_fixed = 0;
                                onGroundLocal = true;
                                break;
                            }
                        }
                    }
                }
                
                // Update class-level onGround flag
                onGround = onGroundLocal;
                
                // ====================================================================
                // STEP 3: INPUT HANDLING
                // ====================================================================
                // Match gamemode_cube.h: cube can only jump when vel_y == 0
                // The check is ONLY for vel_y == 0, NOT onGround flag
                // Collision detection (step 3) sets vel_y to 0 when landing
                
                // Read X input state
                bool xHeld = IsXDownAsync() || keyXHeld;
                int xPressCount = Interlocked.Exchange(ref keyXPressedCount, 0);
                bool xJustPressed = xPressCount > 0;
                
                // Jump check: only requires vel_y == 0 (matches gamemode_cube.h exactly)
                if (playerVelY_fixed == 0)
                {
                    // Apply jump if either:
                    // 1) X is being held (buffer jump path from gamemode_cube.h)
                    // 2) X was just pressed this frame (immediate jump path)
                    if (xHeld || xJustPressed)
                    {
                        // Get jump velocity based on mini/gravity state
                        if (!gravityReversed)
                            playerVelY_fixed = cubeJumpVel;  // Negative = upward
                        else
                            playerVelY_fixed = -cubeJumpVel; // Positive (negated) = upward in reversed
                    }
                }
            }
            catch (Exception ex)
            {
                AppendSimDebug($"ProcessCubePhysics error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Initialize cube physics state variables
        /// </summary>
        private void EnableCubePhysics()
        {
            try
            {
                // Initialize player state
                currplayer = 0;
                mini[0] = 0;        // Not mini by default
                mini[1] = 0;
                gravity[0] = 0;     // Normal gravity by default
                gravity[1] = 0;
                dual = 0;           // Single player by default
                
                // Note: physicsEnabled is set by StartSimulation based on cam mode
            }
            catch (Exception ex)
            {
                AppendSimDebug($"EnableCubePhysics error: {ex.Message}");
            }
        }
    }
}
