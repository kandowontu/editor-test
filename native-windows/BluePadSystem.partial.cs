using System;
using System.Collections.Generic;

namespace FamidashEditor
{
    /// <summary>
    /// Blue pad system implementation for the simulator.
    /// Blue pads reverse/normalize gravity and apply velocity.
    /// Bottom pads activate when gravity is normal and reverse it.
    /// Top pads activate when gravity is reversed and normalize it.
    /// </summary>
    public partial class SimulatorWindow
    {
        // Blue pad sprite constants
        private const byte BOTTOM_BLUE_PAD = 0x0D;
        private const byte TOP_BLUE_PAD = 0x0E;
        private const byte BOTTOM_BLUE_PAD_MULTI = 0xFD;
        private const byte TOP_BLUE_PAD_MULTI = 0xFE;

        // Blue pad activation tracking (can only be activated once per pad)
        private Dictionary<int, bool> bluePadActivated = new Dictionary<int, bool>();

        /// <summary>
        /// Check for blue pad collision and apply gravity change + velocity
        /// </summary>
        private void CheckBluePadCollision()
        {
            try
            {
                int playerX_px = playerX_fixed >> 8;
                int playerY_px = playerY_fixed >> 8;
                
                // Player bounding box for collision
                int playerLeft_px = playerX_px;
                int playerRight_px = playerX_px + playerVisualWidth - 1;
                int playerTop_px = playerY_px;
                int playerBottom_px = playerY_px + playerVisualHeight - 1;
                
                bool gravityInverted = (currplayer_gravity != 0);
                
                // Iterate through ALL sprites and check for blue pads
                for (int idx = 0; idx < sprites.Length; idx++)
                {
                    int sid = sprites[idx];
                    if (sid < 0) continue;
                    
                    // Check if this sprite is a blue pad
                    bool isBottomPad = (sid == BOTTOM_BLUE_PAD || sid == BOTTOM_BLUE_PAD_MULTI);
                    bool isTopPad = (sid == TOP_BLUE_PAD || sid == TOP_BLUE_PAD_MULTI);
                    
                    if (!isBottomPad && !isTopPad)
                        continue; // Not a blue pad
                    
                    // Check if already activated
                    if (bluePadActivated.ContainsKey(idx) && bluePadActivated[idx])
                        continue; // Already activated
                    
                    // Check gravity condition for activation
                    bool canActivate = false;
                    if (isBottomPad && !gravityInverted)
                    {
                        // Bottom pad activates when gravity is normal
                        canActivate = true;
                    }
                    else if (isTopPad && gravityInverted)
                    {
                        // Top pad activates when gravity is reversed
                        canActivate = true;
                    }
                    
                    if (!canActivate)
                        continue;
                    
                    // Use SpriteIntersectsPlayer to check sprite hitbox overlap
                    if (SpriteIntersectsPlayer(idx, sid, playerLeft_px, playerRight_px, playerTop_px, playerBottom_px))
                    {
                        // Mark as activated (can only activate once)
                        bluePadActivated[idx] = true;
                        
                        // Reverse gravity state
                        gravityInverted = !gravityInverted;
                        currplayer_gravity = (byte)(gravityInverted ? 1 : 0);
                        gravityReversed = gravityInverted;
                        gravityFlipped = gravityInverted;
                        UpdatePlayerIconFlip();
                        
                        // Apply velocity AFTER gravity change
                        // Negative if gravity is now reversed (upward)
                        // Positive if gravity is now normal (downward)
                        bool isMini = (currplayer_mini != 0);
                        int baseVel = isMini ? PAD_HEIGHT_BLUE_mini : PAD_HEIGHT_BLUE_normal;
                        
                        // baseVel is negative (-0x3A0 or -0x160)
                        // If gravity is now reversed: keep negative (upward in inverted gravity)
                        // If gravity is now normal: negate to positive (downward in normal gravity)
                        int newVel = gravityInverted ? baseVel : -baseVel;
                        
                        playerVelY_fixed = newVel;
                    }
                }
            }
            catch (Exception ex)
            {
            }
        }

        /// <summary>
        /// Reset blue pad activation state when starting/restarting level
        /// </summary>
        private void ResetBluePadSystem()
        {
            bluePadActivated.Clear();
        }
    }
}
