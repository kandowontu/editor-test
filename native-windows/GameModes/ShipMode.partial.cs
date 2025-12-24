using System;

namespace FamidashEditor
{
    public partial class SimulatorWindow
    {
        // Ship mode X-edge behavior: reuse cube-like press semantics (no buffered gravity toggle).
        private void ProcessShipXEdge()
        {
            try
            {
                Interlocked.Increment(ref keyXPressedCount);
                try { jumpBufferCounter = JUMP_BUFFER_FRAMES; } catch { }
                physicsEnabled = true;
                jumpedOnce = true;
                try
                {
                    int onG_local = onGround ? 1 : 0;
                    Interlocked.Exchange(ref keyXPressStartedOnGroundInt, onG_local);
                    Interlocked.Exchange(ref keyXHeldStartedOnGroundInt, onG_local);
                }
                catch { }
            }
            catch { }
        }
    }
}
