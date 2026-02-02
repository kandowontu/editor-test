using System;
using System.Threading;

namespace FamidashEditor
{
    public partial class SimulatorWindow
    {
        private int footballChargeFrames = 0;
        private bool footballOrbed = false;
        private bool footballWasHeld = false;  // Track previous frame's hold state
        
        /// <summary>
        /// Football mode - Cube physics with charge on release mechanic
        /// Applies Cube gravity via CommonGravityRoutine_Fresh, then handles charge/jump logic
        /// Hold X to charge (max 45 frames), release to jump with charged velocity
        /// Velocity = chargeFrames * 0x004C (if gravity inverted) or chargeFrames * -0x004C (if gravity normal)
        /// At 45 frames, charge resets and player is "orbed" (can't charge again until released)
        /// </summary>
        private void FootballPhysics_Fresh()
        {
            int baseTableIdx = (currplayer_mini != 0 ? 4 : 0);
            bool gravityInverted = (currplayer_gravity != 0);
            int gravityMultiplier = gravityInverted ? -1 : 1;
            
            // Use Cube gravity/fallspeed
            tmpfallspeed = GameModePhysics.CUBE_MAX_FALLSPEED(baseTableIdx, CUBE_MAX_FALLSPEED) * gravityMultiplier;
            tmpgravity = GameModePhysics.CUBE_GRAVITY(baseTableIdx) * gravityMultiplier;
            
            // Apply gravity using common routine
            CommonGravityRoutine_Fresh();
            
            // Handle collision detection like Cube does
            CubeEject_Fresh();
            
            // Charge mechanic (matching gamemode_cube.h logic)
            bool xHeld = IsXDownAsync() || keyXHeld;
            
            AppendSimDebug($"[FOOTBALL] xHeld={xHeld}, wasHeld={footballWasHeld}, chargeFrames={footballChargeFrames}, orbed={footballOrbed}");
            
            // While holding X and not already orbed, accumulate charge
            if (xHeld && !footballOrbed)
            {
                footballChargeFrames++;
                AppendSimDebug($"[FOOTBALL] Charging: {footballChargeFrames}/45");
                
                if (footballChargeFrames >= 45)
                {
                    footballOrbed = true;  // Hit overcharge limit - can't charge anymore
                    AppendSimDebug($"[FOOTBALL] OVERCHARGED - orbed=true, but charge={footballChargeFrames} saved for jump");
                }
                footballWasHeld = true;
            }
            // When X transitions from held to released
            else if (!xHeld && footballWasHeld)
            {
                footballOrbed = false;  // Clear orbed state on release
                footballWasHeld = false;  // Mark that we've processed this release
                
                // Check if on ground
                int playerY_px = playerY_fixed >> 8;
                int playerX_px = playerX_fixed >> 8;
                int hitboxW = (currplayer_mini != 0) ? 8 : 15;
                int hitboxH = (currplayer_mini != 0) ? 7 : 15;
                
                var (hitDown, _) = CheckCollisionDown(playerX_px, playerY_px, hitboxW, hitboxH);
                var (hitUp, _) = CheckCollisionUp(playerX_px, playerY_px, hitboxW, hitboxH);
                
                bool onGround = (!gravityInverted && hitDown) || (gravityInverted && hitUp);
                
                AppendSimDebug($"[FOOTBALL] Released: chargeFrames={footballChargeFrames}, onGround={onGround}, velY_before=0x{playerVelY_fixed:X4}");
                
                // Apply jump velocity if charged and on ground
                if (footballChargeFrames > 0 && onGround)
                {
                    // tmpA = chargeFrames * (gravity ? 0x004C : -0x004C)
                    int chargeVelocity = footballChargeFrames * 0x004C;
                    playerVelY_fixed = gravityInverted ? chargeVelocity : -chargeVelocity;
                    AppendSimDebug($"[FOOTBALL] JUMP! chargeFrames={footballChargeFrames}, velocity_set=0x{playerVelY_fixed:X4}");
                }
                
                footballChargeFrames = 0;  // Reset charge on release
            }
            else if (!xHeld)
            {
                // Still not holding X - keep wasHeld false
                footballWasHeld = false;
            }
        }
    }
}
