using System;

namespace FamidashEditor
{
    public partial class SimulatorWindow
    {
        // Additional state variables needed for Fresh ports
        private int currplayer_table_idx = 0;
        private int playerVelX_fixed = 0x02C4; // Default horizontal velocity (CUBE_SPEED_X1)
        
        /// <summary>
        /// BgCollD_Fresh - Downward collision check wrapper for Fresh physics
        /// </summary>
        private bool BgCollD_Fresh()
        {
            Generic_x = playerX_fixed >> 8;
            Generic_y = playerY_fixed >> 8;
            // Use 8x7 hitbox for mini mode, 15x15 for normal mode
            Generic_width = (currplayer_mini != 0) ? 8 : 15;
            Generic_height = (currplayer_mini != 0) ? 7 : 15;
            
            // For mini mode, offset the collision check to bottom-left quadrant when gravity is normal
            // Matches collision.h: (currplayer_mini ? byte(0x10 - Generic.height) >> 1 : 0)
            // For 7-pixel height: (0x10 - 0x07) >> 1 = 0x09 >> 1 = 4
            if (currplayer_mini != 0)
            {
                int yOffset = (0x10 - Generic_height) >> 1;  // 4 for mini (7px height)
                if (currplayer_gravity == 0)
                {
                    // Normal gravity: shift down to bottom-left quadrant
                    Generic_y += yOffset;
                }
                // Reversed gravity: no offset needed, stays in top-left quadrant
            }
            
            return bg_coll_D();
        }
        
        /// <summary>
        /// BgCollU_Fresh - Upward collision check wrapper for Fresh physics
        /// </summary>
        private bool BgCollU_Fresh()
        {
            Generic_x = playerX_fixed >> 8;
            Generic_y = playerY_fixed >> 8;
            // Use 8x7 hitbox for mini mode, 15x15 for normal mode
            Generic_width = (currplayer_mini != 0) ? 8 : 15;
            Generic_height = (currplayer_mini != 0) ? 7 : 15;
            
            // For mini mode, offset the collision check to bottom-left quadrant when gravity is normal
            // Matches collision.h: (currplayer_mini ? byte(0x10 - Generic.height) >> 1 : 0)
            // For 7-pixel height: (0x10 - 0x07) >> 1 = 0x09 >> 1 = 4
            if (currplayer_mini != 0)
            {
                int yOffset = (0x10 - Generic_height) >> 1;  // 4 for mini (7px height)
                if (currplayer_gravity == 0)
                {
                    // Normal gravity: shift down to bottom-left quadrant
                    Generic_y += yOffset;
                }
                // Reversed gravity: no offset needed, stays in top-left quadrant
            }
            
            return bg_coll_U();
        }
    }
}

