using System;
using System.Threading;

namespace FamidashEditor
{
    public partial class SimulatorWindow
    {
        /// <summary>
        /// Snake mode - Wave physics where X press activates gravity dash
        /// First X press: invert gravity once and set dashing=1 for straight dash
        /// Wave movement converted to vertical, then wave_eject handles collisions
        /// </summary>
        private void SnakePhysics_Fresh()
        {
            // Check for X press to activate gravity dash (using keyXPressedCount like other modes)
            int pressCount = Interlocked.CompareExchange(ref keyXPressedCount, 0, 0);
            if (pressCount > 0)
            {
                // On X press: invert gravity once and activate dash
                currplayer_gravity = (byte)(currplayer_gravity == 0 ? 0xFF : 0);
                dashing = 1;  // Set to 1 for gravity dash
                Interlocked.Exchange(ref keyXPressedCount, 0);  // Consume the press
            }
            
            // Wave movement calculation (from gamemode_wave.h)
            // tmp1 = dashing
            int tmp1 = dashing;
            
            switch (tmp1)
            {
                case 0:
                    // Normal wave movement - convert horizontal velocity to vertical
                    // vel_y = !mini ? (gravity ? -vel_x : vel_x) : (gravity ? -(vel_x << 1) : (vel_x << 1))
                    if (currplayer_mini == 0)
                    {
                        playerVelY_fixed = currplayer_gravity != 0 ? -playerVelX_fixed : playerVelX_fixed;
                    }
                    else
                    {
                        playerVelY_fixed = currplayer_gravity != 0 ? -(playerVelX_fixed << 1) : (playerVelX_fixed << 1);
                    }
                    
                    // Invert velocity if not holding X (normal wave behavior)
                    bool holding = IsXDownAsync() || keyXHeld;
                    if (!holding)
                    {
                        playerVelY_fixed = -playerVelY_fixed;
                    }
                    break;
                    
                case 1:
                    // Gravity dash - move straight
                    playerVelY_fixed = 1;  // Minimal velocity for straight movement
                    break;
                    
                case 2:
                    playerVelY_fixed = -playerVelX_fixed;
                    playerY_fixed += (int)Math.Round(playerVelY_fixed * simTimeScale);
                    break;
                case 3:
                    playerVelY_fixed = playerVelX_fixed;
                    playerY_fixed += (int)Math.Round(playerVelY_fixed * simTimeScale);
                    break;
                case 4:
                    playerVelY_fixed = playerVelX_fixed;
                    playerY_fixed -= (int)Math.Round(playerVelY_fixed * simTimeScale);
                    break;
                case 5:
                    playerVelY_fixed = playerVelX_fixed;
                    playerY_fixed += (int)Math.Round(playerVelY_fixed * simTimeScale);
                    break;
            }
            
            // Apply velocity if not on slope
            if (currplayer_slope_frames == 0 && currplayer_was_on_slope_counter == 0)
            {
                playerY_fixed += (int)Math.Round(playerVelY_fixed * simTimeScale);
            }
            else
            {
                playerVelY_fixed = 0;
            }
            
            // Collision detection (offset collision 2 pixels based on vel_y direction)
            int offsetY = (playerY_fixed >> 8) + ((playerVelY_fixed < 0) ? 2 : -2);
            WaveEject_Fresh(offsetY);
        }
    }
}
