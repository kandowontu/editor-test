using System;

namespace FamidashEditor
{
    public partial class SimulatorWindow
    {
        /// <summary>
        /// wave_movement() from gamemode_wave.h - 1:1 port
        /// Note: Wave mode doesn't use traditional gravity/fallspeed constants.
        /// It calculates velocity based on direction and playerVelX_fixed.
        /// </summary>
        private void WavePhysics_Fresh()
        {
            // Check for orb activation
            {
                bool holdJump_orb = IsXDownAsync() || keyXHeld;
                int pressCount_orb = Interlocked.CompareExchange(ref keyXPressedCount, 0, 0);
                bool pressJump_orb = pressCount_orb > 0;
                bool gravityInverted_orb = (currplayer_gravity != 0);
                int playerX_px_orb = playerX_fixed >> 8;
                int playerY_px_orb = playerY_fixed >> 8;
                int hitboxW_orb = (currplayer_mini != 0) ? 8 : 15;
                int hitboxH_orb = (currplayer_mini != 0) ? 8 : 15;
                int scrollX_px_orb = 0;
                
                int tempVelY = playerVelY_fixed;
                var (orbActivated, _) = UpdateOrbSystem(4, playerX_px_orb, playerY_px_orb, hitboxW_orb, hitboxH_orb, 
                                                   scrollX_px_orb, pressJump_orb, holdJump_orb, gravityInverted_orb, 
                                                   (currplayer_mini != 0), ref tempVelY);
                if (orbActivated)
                {
                    playerVelY_fixed = tempVelY;
                    AppendSimDebug($"[WAVE] Orb activated! New velY={playerVelY_fixed}");
                    
                    // Consume the X press if it was used for orb
                    if (pressJump_orb)
                        Interlocked.Exchange(ref keyXPressedCount, 0);
                }
                
                // Clear orb buffer when X is released
                if (!holdJump_orb)
                    ClearOrbBuffer();
            }
            
            // tmp1 = dashing (we don't have dashing, assume 0)
            int tmp1 = 0;
            
            switch (tmp1) {
                case 0:
                    // Calculate vel_y based on vel_x
                    if (currplayer_mini == 0) {
                        playerVelY_fixed = currplayer_gravity != 0 ? -playerVelX_fixed : playerVelX_fixed;
                    } else {
                        playerVelY_fixed = currplayer_gravity != 0 ? -(playerVelX_fixed << 1) : (playerVelX_fixed << 1);
                    }
                    
                    // Input handling - use same system as cube
                    bool holding = IsXDownAsync() || keyXHeld;
                    if (holding) playerVelY_fixed = -playerVelY_fixed;
                    
                    // Apply velocity if not on slope
                    if (currplayer_slope_frames == 0 && currplayer_was_on_slope_counter == 0) {
                        playerY_fixed += (int)Math.Round(playerVelY_fixed * simTimeScale);
                    } else {
                        playerVelY_fixed = 0;
                    }
                    break;
                case 1: 
                    playerVelY_fixed = 1; 
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
            
            // Offset collision 2 pixels based on vel_y direction
            int offsetY = (playerY_fixed >> 8) + ((playerVelY_fixed < 0) ? 2 : -2);
            
            WaveEject_Fresh(offsetY);
            
            // Record position for trail
            try
            {
                int playerWorldCenterX_px = (playerX_fixed >> 8) + (playerVisualWidth / 2);
                int playerWorldCenterY_px = (playerY_fixed >> 8) + (playerVisualHeight / 2);
                recordedPlayerPath.Add((playerWorldCenterX_px, playerWorldCenterY_px));
            }
            catch { }
        }
        
        /// <summary>
        /// wave_eject() from gamemode_wave.h
        /// </summary>
        private void WaveEject_Fresh(int offsetY)
        {
            int hitboxW = (currplayer_mini != 0) ? 8 : 15;
            int hitboxH = (currplayer_mini != 0) ? 8 : 15;
            int hitboxOffsetY = (currplayer_mini != 0 && currplayer_gravity == 0) ? 8 : 0;
            int collisionX = (playerX_fixed >> 8);
            int collisionY = offsetY + hitboxOffsetY;
            
            if (playerVelY_fixed < 0) { // Moving up
                var (collided, collisionBottomY) = CheckCollisionUp(collisionX, collisionY, hitboxW, hitboxH);
                if (collided) {
                    if (dblocked[0]) { // dblocked check
                        playerY_fixed = ((collisionBottomY - hitboxOffsetY) << 8);
                        playerVelY_fixed = 0;
                    }
                }
            } else { // Moving down
                var (collided, collisionTopY) = CheckCollisionDown(collisionX, collisionY, hitboxW, hitboxH);
                if (collided) {
                    if (dblocked[0]) { // dblocked check
                        playerY_fixed = ((collisionTopY - hitboxH - 1 - hitboxOffsetY) << 8);
                        playerVelY_fixed = 0;
                    }
                }
            }
        }
        
        private bool[] dblocked = new bool[2];
    }
}
