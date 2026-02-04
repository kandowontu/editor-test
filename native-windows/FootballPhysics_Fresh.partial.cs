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
        /// Hold X to charge (max 50 frames before overcharge), max chargepower at 45 frames
        /// Frames 1-45: charging increases power, frames 46-50: power caps at frame 45 level
        /// Velocity = min(chargeFrames, 45) * 0x004C (if gravity inverted) or min(chargeFrames, 45) * -0x004C (if gravity normal)
        /// Has permanent headbonking like H block (ejects from ceiling with velocity instead of zero)
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
            // BUT skip gravity if a pad/orb was just hit this frame - let the pad velocity apply first
            if (!orbhitonthisframe)
            {
                CommonGravityRoutine_Fresh();
            }
            else
            {
                AppendSimDebug($"[FOOTBALL] Pad/orb hit this frame - skipping gravity. velY=0x{playerVelY_fixed:X4}");
            }
            
            // Football always has headbonking enabled (like H block)
            // This will be handled in CubeEject_Fresh by temporarily setting hblocked
            hblocked = true;
            
            // Handle collision detection like Cube does
            CubeEject_Fresh();
            
            // Clear hblocked flag at end of frame
            hblocked = false;
            
            // Charge mechanic (matching gamemode_cube.h logic)
            bool xHeld = IsXDownAsync() || keyXHeld;
            
            AppendSimDebug($"[FOOTBALL] xHeld={xHeld}, wasHeld={footballWasHeld}, chargeFrames={footballChargeFrames}, orbed={footballOrbed}");
            
            // While holding X and not already orbed, accumulate charge
            if (xHeld && !footballOrbed)
            {
                footballChargeFrames++;
                AppendSimDebug($"[FOOTBALL] Charging: {footballChargeFrames}/50 (power maxes at 45)");
                
                if (footballChargeFrames >= 50)
                {
                    footballOrbed = true;  // Hit max charge frames - can't charge anymore
                    AppendSimDebug($"[FOOTBALL] MAX CHARGE - orbed=true, charge capped at frame 45 power");
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
                    // Clamp charge power to 45 frames max (frames 46-50 don't add more power)
                    int effectiveCharge = Math.Min(footballChargeFrames, 45);
                    int chargeVelocity = effectiveCharge * 0x004C;
                    playerVelY_fixed = gravityInverted ? chargeVelocity : -chargeVelocity;
                    AppendSimDebug($"[FOOTBALL] JUMP! chargeFrames={footballChargeFrames}, effectiveCharge={effectiveCharge}, velocity_set=0x{playerVelY_fixed:X4}");
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
