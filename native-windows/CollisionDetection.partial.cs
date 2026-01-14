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
            switch (collision)
            {
                // L-shaped stairs
                case MetatileCollision.COL_TOP_LEFT_STAIRS:
                    // Top 8 pixels + left 8 pixels (bottom-right quadrant empty)
                    return (localY < 8) || (localX < 8);
                
                case MetatileCollision.COL_TOP_RIGHT_STAIRS:
                    // Top 8 pixels + right 8 pixels (bottom-left quadrant empty)
                    return (localY < 8) || (localX >= 8);
                
                case MetatileCollision.COL_BOTTOM_LEFT_STAIRS:
                    // Full left + bottom-right quadrant
                    return (localX < 8) || (localX >= 8 && localY >= 8);
                
                case MetatileCollision.COL_BOTTOM_RIGHT_STAIRS:
                    // Full right + bottom-left quadrant
                    return (localX >= 8) || (localX < 8 && localY >= 8);
                
                // Diagonal blocks
                case MetatileCollision.COL_TOP_LEFT_BOTTOM_RIGHT:
                    // Top-left 8x8 + bottom-right 8x8
                    return (localX < 8 && localY < 8) || (localX >= 8 && localY >= 8);
                
                case MetatileCollision.COL_TOP_RIGHT_BOTTOM_LEFT:
                    // Top-right 8x8 + bottom-left 8x8
                    return (localX >= 8 && localY < 8) || (localX < 8 && localY >= 8);
                
                default:
                    return false;
            }
        }
        
        /// <summary>
        /// Collision bounds for a metatile (offsets within the 16x16 tile)
        /// Format: (left, top, right, bottom) in pixels from tile origin (0-16)
        /// </summary>
        private (int left, int top, int right, int bottom) GetCollisionBounds(MetatileCollision collision)
        {
            switch (collision)
            {
                // Full tile
                case MetatileCollision.COL_ALL:
                case MetatileCollision.COL_FLOOR_CEIL:
                case MetatileCollision.COL_NO_SIDE:
                    return (0, 0, 16, 16);
                
                // Half slabs
                case MetatileCollision.COL_TOP:
                    return (0, 0, 16, 8);  // Top half only
                
                case MetatileCollision.COL_BOTTOM:
                    return (0, 8, 16, 16); // Bottom half only
                
                case MetatileCollision.COL_LEFT:
                    return (0, 0, 8, 16);  // Left half only
                
                case MetatileCollision.COL_RIGHT:
                    return (8, 0, 16, 16); // Right half only
                
                // Quadrant blocks (8x8)
                case MetatileCollision.COL_UP_LEFT:
                    return (0, 0, 8, 8);   // Top-left quadrant
                
                case MetatileCollision.COL_UP_RIGHT:
                    return (8, 0, 16, 8);  // Top-right quadrant
                
                case MetatileCollision.COL_DOWN_LEFT:
                    return (0, 8, 8, 16);  // Bottom-left quadrant
                
                case MetatileCollision.COL_DOWN_RIGHT:
                    return (8, 8, 16, 16); // Bottom-right quadrant
                
                // Complex shapes (L-shaped, diagonals) - return primary bounds, handle specially in Check functions
                case MetatileCollision.COL_TOP_LEFT_STAIRS:
                case MetatileCollision.COL_TOP_RIGHT_STAIRS:
                case MetatileCollision.COL_BOTTOM_LEFT_STAIRS:
                case MetatileCollision.COL_BOTTOM_RIGHT_STAIRS:
                case MetatileCollision.COL_TOP_LEFT_BOTTOM_RIGHT:
                case MetatileCollision.COL_TOP_RIGHT_BOTTOM_LEFT:
                    return (0, 0, 16, 16); // Use full tile for initial bounds, check regions specially
                
                // Slope tiles - return full tile bounds so flat collision fallback can detect them
                // This matches NES behavior where slope tiles have RISING/FALLING flags in collision_table
                case MetatileCollision.COL_SLOPE_RD45:
                case MetatileCollision.COL_SLOPE_RD22_RIGHT:
                case MetatileCollision.COL_SLOPE_RD22_LEFT:
                case MetatileCollision.COL_SLOPE_RD66_TOP:
                case MetatileCollision.COL_SLOPE_RD66_BOT:
                case MetatileCollision.COL_SLOPE_RU45:
                case MetatileCollision.COL_SLOPE_RU22_RIGHT:
                case MetatileCollision.COL_SLOPE_RU22_LEFT:
                case MetatileCollision.COL_SLOPE_RU66_TOP:
                case MetatileCollision.COL_SLOPE_RU66_BOT:
                case MetatileCollision.COL_SLOPE_LD45:
                case MetatileCollision.COL_SLOPE_LD22_RIGHT:
                case MetatileCollision.COL_SLOPE_LD22_LEFT:
                case MetatileCollision.COL_SLOPE_LD66_BOT:
                case MetatileCollision.COL_SLOPE_LD66_TOP:
                case MetatileCollision.COL_SLOPE_LU45:
                case MetatileCollision.COL_SLOPE_LU22_RIGHT:
                case MetatileCollision.COL_SLOPE_LU22_LEFT:
                case MetatileCollision.COL_SLOPE_LU66_BOT:
                case MetatileCollision.COL_SLOPE_LU66_TOP:
                    return (0, 0, 16, 16); // Full tile bounds for flat collision fallback
                
                // No collision
                case MetatileCollision.COL_NONE:
                default:
                    return (16, 16, 0, 0); // Invalid bounds (right < left) = no collision
            }
        }
        
        /// <summary>
        /// Check collision in downward direction (normal gravity floor detection)
        /// Returns: (collided, collisionTopY) where collisionTopY is the Y position of the collision surface
        /// </summary>
        private (bool collided, int collisionTopY) CheckCollisionDown(int playerX_px, int playerY_px, int width, int height)
        {
            int playerBottom_px = playerY_px + height;
            int tileBelowY = playerBottom_px / TILE;
            
            // Calculate ground layer offset
            int groundRowsToReserve = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
            
            if (tileBelowY < 0 || tileBelowY >= mapHeight) return (false, 0);
            
            // Adjust for ground layer rendering offset
            int tileArrayY = tileBelowY + groundRowsToReserve;
            
            // Ground layer is always solid
            if (tileArrayY >= mapHeight)
            {
                int groundTop = tileBelowY * TILE;
                return (true, groundTop);
            }
            
            int playerLeft_px = playerX_px;
            int playerRight_px = playerX_px + width - 1;
            int tileLeftX = playerLeft_px / TILE;
            int tileRightX = playerRight_px / TILE;
            
            for (int tx = tileLeftX; tx <= tileRightX; tx++)
            {
                if (tx < 0 || tx >= mapWidth) continue;
                
                int tileIdx = tileArrayY * mapWidth + tx;
                if (tileIdx < 0 || tileIdx >= tiles.Length) continue;
                
                int tileId = tiles[tileIdx];
                var collision = MetatileCollisionTable.GetCollision((byte)tileId);
                
                if (collision == MetatileCollision.COL_NONE) continue;
                
                // Check for complex collision types (L-shapes, diagonals)
                bool isComplex = collision == MetatileCollision.COL_TOP_LEFT_STAIRS ||
                                collision == MetatileCollision.COL_TOP_RIGHT_STAIRS ||
                                collision == MetatileCollision.COL_BOTTOM_LEFT_STAIRS ||
                                collision == MetatileCollision.COL_BOTTOM_RIGHT_STAIRS ||
                                collision == MetatileCollision.COL_TOP_LEFT_BOTTOM_RIGHT ||
                                collision == MetatileCollision.COL_TOP_RIGHT_BOTTOM_LEFT;
                
                if (isComplex)
                {
                    // For complex shapes, check each pixel of player's bottom row
                    int tileWorldX = tx * TILE;
                    int tileWorldY = tileBelowY * TILE;
                    
                    for (int px = Math.Max(playerLeft_px, tileWorldX); px <= Math.Min(playerRight_px, tileWorldX + 15); px++)
                    {
                        int localX = px - tileWorldX;
                        int localY = playerBottom_px - tileWorldY;
                        
                        // Check if player's bottom is at or below the tile top (allow localY >= 0 OR at tile boundary)
                        if (localY >= -1 && localY < 16 && CheckComplexCollision(collision, localX, Math.Max(0, localY)))
                        {
                            // Find the top of the solid region at this X position
                            int collisionTop = tileWorldY;
                            for (int y = 0; y < 16; y++)
                            {
                                if (CheckComplexCollision(collision, localX, y))
                                {
                                    collisionTop = tileWorldY + y;
                                    break;
                                }
                            }
                            return (true, collisionTop);
                        }
                    }
                }
                else
                {
                    // Simple rectangular collision
                    var (colLeft, colTop, colRight, colBottom) = GetCollisionBounds(collision);
                    if (colRight <= colLeft || colBottom <= colTop) continue; // Invalid bounds
                    
                    // Calculate world position of collision region
                    int tileWorldX = tx * TILE;
                    int tileWorldY = tileBelowY * TILE;
                    int collisionTop_px = tileWorldY + colTop;
                    int collisionBottom_px = tileWorldY + colBottom;
                    int collisionLeft_px = tileWorldX + colLeft;
                    int collisionRight_px = tileWorldX + colRight;
                    
                    // Check if player bottom overlaps with collision region
                    // For landing: detect collision when player is at or 1 pixel above collision top
                    // This prevents falling through when velocity moves player exactly to boundary
                    if (playerBottom_px >= collisionTop_px - 1 && playerBottom_px <= collisionBottom_px)
                    {
                        // Check horizontal overlap
                        if (playerRight_px >= collisionLeft_px && playerLeft_px < collisionRight_px)
                        {
                            return (true, collisionTop_px);
                        }
                    }
                }
            }
            
            return (false, 0);
        }
        
        /// <summary>
        /// Check collision in upward direction (reversed gravity ceiling detection)
        /// Returns: (collided, collisionBottomY) where collisionBottomY is the Y position of the collision surface
        /// </summary>
        private (bool collided, int collisionBottomY) CheckCollisionUp(int playerX_px, int playerY_px, int width, int height)
        {
            int playerTop_px = playerY_px;
            int tileAboveY = (playerTop_px - 1) / TILE;
            
            if (tileAboveY < 0 || tileAboveY >= mapHeight) return (false, 0);
            
            // Adjust for ground layer rendering offset
            int groundRowsToReserve = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
            int tileArrayY = tileAboveY + groundRowsToReserve;
            
            int playerLeft_px = playerX_px;
            int playerRight_px = playerX_px + width - 1;
            int tileLeftX = playerLeft_px / TILE;
            int tileRightX = playerRight_px / TILE;
            
            for (int tx = tileLeftX; tx <= tileRightX; tx++)
            {
                if (tx < 0 || tx >= mapWidth) continue;
                
                int tileIdx = tileArrayY * mapWidth + tx;
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
                    // For complex shapes, check each pixel of player's top row
                    int tileWorldX = tx * TILE;
                    int tileWorldY = tileAboveY * TILE;
                    
                    for (int px = Math.Max(playerLeft_px, tileWorldX); px <= Math.Min(playerRight_px, tileWorldX + 15); px++)
                    {
                        int localX = px - tileWorldX;
                        int localY = playerTop_px - tileWorldY;
                        
                        if (localY >= 0 && localY < 16 && CheckComplexCollision(collision, localX, localY))
                        {
                            // Find the bottom of the solid region at this X position
                            int collisionBottom = tileWorldY + 15;
                            for (int y = 15; y >= 0; y--)
                            {
                                if (CheckComplexCollision(collision, localX, y))
                                {
                                    collisionBottom = tileWorldY + y + 1;
                                    break;
                                }
                            }
                            return (true, collisionBottom);
                        }
                    }
                }
                else
                {
                    // Simple rectangular collision
                    var (colLeft, colTop, colRight, colBottom) = GetCollisionBounds(collision);
                    if (colRight <= colLeft || colBottom <= colTop) continue; // Invalid bounds
                    
                    // Calculate world position of collision region
                    int tileWorldX = tx * TILE;
                    int tileWorldY = tileAboveY * TILE;
                    int collisionTop_px = tileWorldY + colTop;
                    int collisionBottom_px = tileWorldY + colBottom;
                    int collisionLeft_px = tileWorldX + colLeft;
                    int collisionRight_px = tileWorldX + colRight;
                    
                    // Check if player top overlaps with collision region
                    // For ceiling collision: if ANY pixel of the player top row touches the collision
                    if (playerTop_px >= collisionTop_px && playerTop_px < collisionBottom_px)
                    {
                        // Check horizontal overlap
                        if (playerRight_px >= collisionLeft_px && playerLeft_px < collisionRight_px)
                        {
                            return (true, collisionBottom_px);
                        }
                    }
                }
            }
            
            return (false, 0);
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
