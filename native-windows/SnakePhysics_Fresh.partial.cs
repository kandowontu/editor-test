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
                gravityReversed = !gravityReversed;
                gravityFlipped = !gravityFlipped;
                dashing[currplayer] = 1;  // Set to 1 for gravity dash
                Interlocked.Exchange(ref keyXPressedCount, 0);  // Consume the press
                
                // Update icon flip and checkbox when gravity reverses (marshal to UI thread)
                try { Dispatcher?.BeginInvoke(new Action(() => { UpdatePlayerIconFlip(); InvertedCheckBox.IsChecked = gravityReversed; })); } catch { }
            }
            
            // Wave movement calculation (from gamemode_wave.h)
            // tmp1 = dashing
            int tmp1 = dashing[currplayer];
            
            switch (tmp1)
            {
                case 0:
                    // Normal wave movement - convert horizontal velocity to vertical
                    // vel_y = !mini ? (gravity ? -vel_x : vel_x) : (gravity ? -(vel_x << 1) : (vel_x << 1))
                    if (!miniMode)
                    {
                        playerVelY_fixed = gravityFlipped ? -playerVelX_fixed : playerVelX_fixed;
                    }
                    else
                    {
                        playerVelY_fixed = gravityFlipped ? -(playerVelX_fixed << 1) : (playerVelX_fixed << 1);
                    }
                    
                    // If holding X, invert velocity (when holding, go opposite of default direction)
                    bool holding = IsXDownAsync() || keyXHeld;
                    if (holding)
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
                    if (isFullSpeed)
                        playerY_fixed += playerVelY_fixed;
                    else
                        playerY_fixed += (int)Math.Round(playerVelY_fixed * simTimeScale);
                    break;
                case 3:
                    playerVelY_fixed = playerVelX_fixed;
                    if (isFullSpeed)
                        playerY_fixed += playerVelY_fixed;
                    else
                        playerY_fixed += (int)Math.Round(playerVelY_fixed * simTimeScale);
                    break;
                case 4:
                    playerVelY_fixed = playerVelX_fixed;
                    if (isFullSpeed)
                        playerY_fixed -= playerVelY_fixed;
                    else
                        playerY_fixed -= (int)Math.Round(playerVelY_fixed * simTimeScale);
                    break;
                case 5:
                    playerVelY_fixed = playerVelX_fixed;
                    if (isFullSpeed)
                        playerY_fixed += playerVelY_fixed;
                    else
                        playerY_fixed += (int)Math.Round(playerVelY_fixed * simTimeScale);
                    break;
            }
            
            // Apply velocity if not on slope
            if (currplayer_slope_frames == 0 && currplayer_was_on_slope_counter == 0)
            {
                if (isFullSpeed)
                    playerY_fixed += playerVelY_fixed;
                else
                    playerY_fixed += (int)Math.Round(playerVelY_fixed * simTimeScale);
            }
            else
            {
                playerVelY_fixed = 0;
            }
            
            // Collision detection (offset collision 2 pixels based on vel_y direction)
            int offsetY = (playerY_fixed >> 8) + ((playerVelY_fixed < 0) ? 2 : -2);
            WaveEject_Fresh(offsetY);
            
            // Record position for trail (skip during pathfinder speculative simulation)
            if (!pfSimulating)
            try
            {
                int playerWorldCenterX_px = (playerX_fixed >> 8) + (playerVisualWidth / 2);
                int playerY_px_trail = playerY_fixed >> 8;
                // Apply mini mode offset for trail to match visual position
                bool isMini_trail = (miniMode);
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



