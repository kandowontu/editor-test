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
            // Use 8x8 hitbox for mini mode, 15x15 for normal mode
            Generic_width = (currplayer_mini != 0) ? 8 : 15;
            Generic_height = (currplayer_mini != 0) ? 8 : 15;
            
            // For mini mode, offset the collision check to bottom-left quadrant when gravity is normal
            // or top-left quadrant when gravity is reversed
            if (currplayer_mini != 0)
            {
                if (currplayer_gravity == 0)
                {
                    // Normal gravity: shift down 8 pixels to bottom-left quadrant
                    Generic_y += 8;
                }
                // Reversed gravity: no offset, use top-left quadrant
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
            // Use 8x8 hitbox for mini mode, 15x15 for normal mode
            Generic_width = (currplayer_mini != 0) ? 8 : 15;
            Generic_height = (currplayer_mini != 0) ? 8 : 15;
            
            // For mini mode, offset the collision check to bottom-left quadrant when gravity is normal
            // or top-left quadrant when gravity is reversed
            if (currplayer_mini != 0)
            {
                if (currplayer_gravity == 0)
                {
                    // Normal gravity: shift down 8 pixels to bottom-left quadrant
                    Generic_y += 8;
                }
                // Reversed gravity: no offset, use top-left quadrant
            }
            
            return bg_coll_U();
        }
    }
}
