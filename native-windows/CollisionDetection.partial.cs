using System;

namespace FamidashEditor
{
    public partial class SimulatorWindow
    {
        // ========================================================================
        // GLOBAL COLLISION DETECTION SYSTEM
        // Based on famidash/SAUCE/functions/collision.h
        // ========================================================================
        
        /// <summary>
        /// Check if a local position (x, y) within a tile (0-15) collides with complex collision types.
        /// Returns true if the position is solid for the given collision type.
        /// </summary>
        private bool CheckComplexCollision(MetatileCollision collision, int localX, int localY)
        {
            return SharedPhysics.CheckComplexCollision(collision, localX, localY);
        }
        
        /// <summary>
        /// Collision bounds for a metatile (offsets within the 16x16 tile)
        /// Format: (left, top, right, bottom) in pixels from tile origin (0-16)
        /// Delegates to SharedPhysics for the common cases;
        /// adds SIM-specific spike-quadrant bounds that the PF doesn't need.
        /// </summary>
        private (int left, int top, int right, int bottom) GetCollisionBounds(MetatileCollision collision)
        {
            switch (collision)
            {
                // SIM-specific: treat UP spike-quadrants as solid-bounded
                case MetatileCollision.COL_UP_LEFT_SPIKE:
                    return (0, 0, 8, 8);
                case MetatileCollision.COL_UP_RIGHT_SPIKE:
                    return (8, 0, 16, 8);
                // Pure death spike tiles (NO solid collision, only death)
                case MetatileCollision.COL_DOWN_LEFT_SPIKE:
                case MetatileCollision.COL_DOWN_RIGHT_SPIKE:
                case MetatileCollision.COL_DOWN_BOTH_SPIKES:
                case MetatileCollision.COL_UP_BOTH_SPIKES:
                    return (0, 0, 0, 0);
                default:
                    return SharedPhysics.GetCollisionBounds(collision);
            }
        }
        
        /// <summary>
        /// Check collision in downward direction (normal gravity floor detection)
        /// Thin wrapper over SharedPhysics.CheckFloor — adds SIM-specific death UI.
        /// </summary>
        private (bool collided, int collisionTopY) CheckCollisionDown(int playerX_px, int playerY_px, int width, int height)
        {
            int groundRowsToReserve = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
            var map = new SharedPhysics.CollisionMap(tiles, mapWidth, mapHeight, groundRowsToReserve);

            var (hit, surfaceY, spikeDeath) = SharedPhysics.CheckFloor(in map, playerX_px, playerY_px, width, height);

            if (spikeDeath && !MainWindow.Option_NoDeath)
            {
                AppendSimDebug($"[DEATH] Floor spike detected (SharedPhysics.CheckFloor)");
                deathTriggered = true;
                deathTileX = playerX_px;
                deathTileY = playerY_px + height;
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

                return (false, 0);
            }

            if (hit)
            {
                AppendSimDebug($"[COLL_DOWN] Hit! collisionTop_px={surfaceY}, playerBottom={playerY_px + height}");
            }

            return (hit, surfaceY);
        }
        
        /// <summary>
        /// Check collision in upward direction (reversed gravity ceiling detection)
        /// Thin wrapper over SharedPhysics.CheckCeiling.
        /// NES bg_coll_U does NOT cause spike death — spikes are detected separately
        /// by bg_coll_floor_spikes (4-corner check) at the post-eject position.
        /// The spikeDeath flag from CheckCeiling is intentionally ignored here so
        /// that the solid ceiling surface is still found and the player can be
        /// snapped to it before the floor-spike check runs.
        /// </summary>
        private (bool collided, int collisionBottomY) CheckCollisionUp(int playerX_px, int playerY_px, int width, int height)
        {
            int groundRowsToReserve = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
            var map = new SharedPhysics.CollisionMap(tiles, mapWidth, mapHeight, groundRowsToReserve);

            var (hit, ceilingBottomY, _) = SharedPhysics.CheckCeiling(in map, playerX_px, playerY_px, width, height);

            return (hit, ceilingBottomY);
        }
        
        /// <summary>
        /// Check collision in leftward direction (side collision)
        /// Returns: (collided, collisionRightX) where collisionRightX is the X position of the collision surface
        /// </summary>
        private (bool collided, int collisionRightX) CheckCollisionLeft(int playerX_px, int playerY_px, int width, int height)
        {
            int playerLeft_px = playerX_px;
            int tileLeftX = playerLeft_px / TILE;
            
            if (tileLeftX < 0 || tileLeftX >= mapWidth) return (false, 0);
            
            // Adjust for ground layer rendering offset
            int groundRowsToReserve = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
            
            int playerTop_px = playerY_px;
            int playerBottom_px = playerY_px + height - 1;
            int tileTopY = playerTop_px / TILE;
            int tileBottomY = playerBottom_px / TILE;
            
            for (int ty = tileTopY; ty <= tileBottomY; ty++)
            {
                if (ty < 0 || ty >= mapHeight) continue;
                
                int tileArrayY = ty + groundRowsToReserve;
                if (tileArrayY >= mapHeight) continue;
                
                int tileIdx = tileArrayY * mapWidth + tileLeftX;
                if (tileIdx < 0 || tileIdx >= tiles.Length) continue;
                
                int tileId = tiles[tileIdx];
                var collision = MetatileCollisionTable.GetCollision((byte)tileId);
                
                if (collision == MetatileCollision.COL_NONE) continue;
                
                // Check for complex collision types
                bool isComplex = collision == MetatileCollision.COL_TOP_LEFT_STAIRS ||
                                collision == MetatileCollision.COL_TOP_RIGHT_STAIRS ||
                                collision == MetatileCollision.COL_BOTTOM_LEFT_STAIRS ||
                                collision == MetatileCollision.COL_BOTTOM_RIGHT_STAIRS ||
                                collision == MetatileCollision.COL_TOP_LEFT_BOTTOM_RIGHT ||
                                collision == MetatileCollision.COL_TOP_RIGHT_BOTTOM_LEFT;
                
                if (isComplex)
                {
                    // For complex shapes, check each pixel of player's left edge
                    int tileWorldX = tileLeftX * TILE;
                    int tileWorldY = ty * TILE;
                    
                    for (int py = Math.Max(playerTop_px, tileWorldY); py <= Math.Min(playerBottom_px, tileWorldY + 15); py++)
                    {
                        int localX = playerLeft_px - tileWorldX;
                        int localY = py - tileWorldY;
                        
                        if (localX >= 0 && localX < 16 && CheckComplexCollision(collision, localX, localY))
                        {
                            // Find the right edge of the solid region at this Y position
                            int collisionRight = tileWorldX;
                            for (int x = 15; x >= 0; x--)
                            {
                                if (CheckComplexCollision(collision, x, localY))
                                {
                                    collisionRight = tileWorldX + x + 1;
                                    break;
                                }
                            }
                            return (true, collisionRight);
                        }
                    }
                }
                else
                {
                    // Simple rectangular collision
                    var (colLeft, colTop, colRight, colBottom) = GetCollisionBounds(collision);
                    if (colRight <= colLeft || colBottom <= colTop) continue;
                    
                    // Calculate world position of collision region
                    int tileWorldX = tileLeftX * TILE;
                    int tileWorldY = ty * TILE;
                    int collisionTop_px = tileWorldY + colTop;
                    int collisionBottom_px = tileWorldY + colBottom;
                    int collisionLeft_px = tileWorldX + colLeft;
                    int collisionRight_px = tileWorldX + colRight;
                    
                    // Check if player left side overlaps with collision region
                    if (playerLeft_px >= collisionLeft_px && playerLeft_px < collisionRight_px)
                    {
                        // Check vertical overlap
                        if (playerBottom_px >= collisionTop_px && playerTop_px < collisionBottom_px)
                        {
                            return (true, collisionRight_px);
                        }
                    }
                }
            }
            
            return (false, 0);
        }
        
        /// <summary>
        /// Check collision in rightward direction (side collision)
        /// Returns: (collided, collisionLeftX) where collisionLeftX is the X position of the collision surface
        /// </summary>
        private (bool collided, int collisionLeftX) CheckCollisionRight(int playerX_px, int playerY_px, int width, int height)
        {
            int playerRight_px = playerX_px + width - 1;
            int tileRightX = playerRight_px / TILE;
            
            if (tileRightX < 0 || tileRightX >= mapWidth) return (false, 0);
            
            // Adjust for ground layer rendering offset
            int groundRowsToReserve = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
            
            int playerTop_px = playerY_px;
            int playerBottom_px = playerY_px + height - 1;
            int tileTopY = playerTop_px / TILE;
            int tileBottomY = playerBottom_px / TILE;
            
            for (int ty = tileTopY; ty <= tileBottomY; ty++)
            {
                if (ty < 0 || ty >= mapHeight) continue;
                
                int tileArrayY = ty + groundRowsToReserve;
                if (tileArrayY >= mapHeight) continue;
                
                int tileIdx = tileArrayY * mapWidth + tileRightX;
                if (tileIdx < 0 || tileIdx >= tiles.Length) continue;
                
                int tileId = tiles[tileIdx];
                var collision = MetatileCollisionTable.GetCollision((byte)tileId);
                
                if (collision == MetatileCollision.COL_NONE) continue;
                
                // Check for complex collision types
                bool isComplex = collision == MetatileCollision.COL_TOP_LEFT_STAIRS ||
                                collision == MetatileCollision.COL_TOP_RIGHT_STAIRS ||
                                collision == MetatileCollision.COL_BOTTOM_LEFT_STAIRS ||
                                collision == MetatileCollision.COL_BOTTOM_RIGHT_STAIRS ||
                                collision == MetatileCollision.COL_TOP_LEFT_BOTTOM_RIGHT ||
                                collision == MetatileCollision.COL_TOP_RIGHT_BOTTOM_LEFT;
                
                if (isComplex)
                {
                    // For complex shapes, check each pixel of player's right edge
                    int tileWorldX = tileRightX * TILE;
                    int tileWorldY = ty * TILE;
                    
                    for (int py = Math.Max(playerTop_px, tileWorldY); py <= Math.Min(playerBottom_px, tileWorldY + 15); py++)
                    {
                        int localX = playerRight_px - tileWorldX;
                        int localY = py - tileWorldY;
                        
                        if (localX >= 0 && localX < 16 && CheckComplexCollision(collision, localX, localY))
                        {
                            // Find the left edge of the solid region at this Y position
                            int collisionLeft = tileWorldX + 15;
                            for (int x = 0; x < 16; x++)
                            {
                                if (CheckComplexCollision(collision, x, localY))
                                {
                                    collisionLeft = tileWorldX + x;
                                    break;
                                }
                            }
                            return (true, collisionLeft);
                        }
                    }
                }
                else
                {
                    // Simple rectangular collision
                    var (colLeft, colTop, colRight, colBottom) = GetCollisionBounds(collision);
                    if (colRight <= colLeft || colBottom <= colTop) continue;
                    
                    // Calculate world position of collision region
                    int tileWorldX = tileRightX * TILE;
                    int tileWorldY = ty * TILE;
                    int collisionTop_px = tileWorldY + colTop;
                    int collisionBottom_px = tileWorldY + colBottom;
                    int collisionLeft_px = tileWorldX + colLeft;
                    int collisionRight_px = tileWorldX + colRight;
                    
                    // Check if player right side overlaps with collision region
                    if (playerRight_px >= collisionLeft_px && playerRight_px < collisionRight_px)
                    {
                        // Check vertical overlap
                        if (playerBottom_px >= collisionTop_px && playerTop_px < collisionBottom_px)
                        {
                            return (true, collisionLeft_px);
                        }
                    }
                }
            }
            
            return (false, 0);
        }
    }
}
