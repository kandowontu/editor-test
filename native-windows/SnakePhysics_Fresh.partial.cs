using System;
using System.Threading;

namespace FamidashEditor
{
    public partial class SimulatorWindow
    {
        /// <summary>
        /// Exact GAMEMODE_SNAKE path through wave_movement().
        /// A fresh held press invokes the synthetic gravity-dash controller;
        /// holding by itself does not reverse the normal wave velocity.
        /// </summary>
        private void SnakePhysics_Fresh()
        {
            int pressCount = Interlocked.CompareExchange(ref keyXPressedCount, 0, 0);
            bool pressed = pressCount > 0;
            bool holding = IsXDownAsync() || keyXHeld;

            // NES snapshots tmp1 before sprite_gamemode_controller_check can
            // change dashing on this frame.
            int tmp1 = dashing[currplayer];

            switch (tmp1)
            {
                case 0:
                    int snakeVelX = currplayer_mini == 0
                        ? playerVelX_fixed
                        : playerVelX_fixed << 1;
                    playerVelY_fixed = currplayer_gravity != 0
                        ? -snakeVelX
                        : snakeVelX;

                    // collided = DASH_GRAVITY_ORB followed by the controller-only
                    // handler.  It acts on a fresh press, flips gravity, zeros Y,
                    // and sets horizontal dash mode 1.
                    if (holding && pressed)
                    {
                        currplayer_gravity = (byte)(currplayer_gravity == 0 ? 0xFF : 0);
                        gravityReversed = currplayer_gravity != 0;
                        gravityFlipped = gravityReversed;
                        playerVelY_fixed = 0;
                        dashing[currplayer] = 1;
                        Interlocked.Exchange(ref keyXPressedCount, 0);
                        try { Dispatcher?.BeginInvoke(new Action(() => { UpdatePlayerIconFlip(); InvertedCheckBox.IsChecked = gravityReversed; })); } catch { }
                    }

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
                    break;

                case 1:
                    // Horizontal dash does not move Y.
                    playerVelY_fixed = 1;
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

            WaveEject_Fresh(playerY_fixed >> 8);
            
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



