using System;
using System.Threading;

namespace FamidashEditor
{
    public partial class SimulatorWindow
    {
        // Centralized handler for Ball-mode X-edge behavior. Both UI KeyDown and
        // Timer_Tick call this to keep behavior consistent between input paths.
        private void ProcessBallXEdge()
        {
            try
            {
                // Only accept the first edge until the next surface contact.
                int prevLock = Interlocked.CompareExchange(ref ballToggleLocked, 1, 0);
                if (prevLock == 0)
                {
                    if (onGround)
                    {
                        // Immediate switch when on surface: flip direction and
                        // unsnap the ball so it launches in the new direction.
                        ballGoingDown = !ballGoingDown;
                        int sign = ballGoingDown ? 1 : -1;
                        try { playerVelY_fixed = (int)Math.Round((sign * BALL_IMMEDIATE_VEL) * simTimeScale); } catch { playerVelY_fixed = sign * BALL_IMMEDIATE_VEL; }
                        onGround = false;
                        groundStabilizeCounter = 0;
                        physicsEnabled = true;
                        jumpedOnce = true;
                        // Keep canonical gravity in sync so collisions follow the new direction
                        try { gravityReversed = !ballGoingDown; effectiveInvertedByW = gravityReversed; } catch { }
                        try { UpdateEffectiveGravity(); } catch { }
                        try { UpdatePlayerImageForMode(); } catch { }
                        LogBallEvent($"ProcessBallXEdge: immediate-toggle performed; gravityReversed={gravityReversed} ballGoingDown={ballGoingDown} sign={sign}");
                    }
                    else
                    {
                        // Queue the toggle for the next landing
                        Interlocked.Exchange(ref ballToggleRequested, 1);
                        LogBallEvent($"ProcessBallXEdge: queued toggle; ballToggleLocked=1");
                    }
                }
                else
                {
                    LogBallEvent($"ProcessBallXEdge: ignored because lock != 0 (lock={prevLock}) onGround={onGround} ballGoingDown={ballGoingDown}");
                }
            }
            catch { }
        }
    }
}
