using System;

namespace FamidashEditor
{
    public partial class MainWindow
    {
        // Find valid Y position for START POS (move up if inside solid ground)
        private int FindValidStartPosY(int worldX, int worldY)
        {
            try
            {
                // Player hitbox is 16x16 pixels
                int hitboxW = 16;
                int hitboxH = 16;
                int maxIterations = 100; // Prevent stack overflow
                
                // Iteratively move up until we find clear space
                for (int iteration = 0; iteration < maxIterations; iteration++)
                {
                    bool foundCollision = false;
                    
                    // Check if any part of the 16x16 hitbox overlaps with solid tiles
                    for (int dy = 0; dy < hitboxH; dy++)
                    {
                        for (int dx = 0; dx < hitboxW; dx++)
                        {
                            int checkX = worldX + dx;
                            int checkY = worldY + dy;
                            
                            // Convert to tile coordinates
                            int tileX = checkX / TileSize;
                            int tileY = checkY / TileSize;
                            
                            if (tileX < 0 || tileY < 0 || tileX >= mapWidth || tileY >= mapHeight) continue;
                            
                            // Check if this tile is solid (id > 0)
                            int tileIndex = tileY * mapWidth + tileX;
                            if (tileIndex >= 0 && tileIndex < tiles.Length)
                            {
                                int tileId = tiles[tileIndex];
                                if (tileId > 0)
                                {
                                    foundCollision = true;
                                    break;
                                }
                            }
                        }
                        if (foundCollision) break;
                    }
                    
                    if (!foundCollision)
                    {
                        // Position is valid (no collision)
                        return worldY;
                    }
                    
                    // Move up one tile and try again
                    worldY -= TileSize;
                    if (worldY < 0) return 0;
                }
                
                // Hit iteration limit - return original worldY to preserve click position
                // (This happens in very tile-dense areas)
                return worldY;
            }
            catch
            {
                return worldY;
            }
        }
    }
}
