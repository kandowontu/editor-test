using System;

namespace FamidashEditor
{
    public partial class SimulatorWindow
    {
        // Generic dispatcher for X-edge handling across game modes.
        private void ProcessModeXEdge()
        {
            try
            {
                switch (currentGameMode)
                {
                    case 0: // Cube
                        ProcessCubeXEdge();
                        break;
                    case 1: // Ship
                        ProcessShipXEdge();
                        break;
                    case 2: // Ball
                        ProcessBallXEdge();
                        break;
                    case 3: // Ufo
                        ProcessUfoXEdge();
                        break;
                    default:
                        // Fallback to cube-like behavior
                        ProcessCubeXEdge();
                        break;
                }
            }
            catch { }
        }

        // Apply per-mode startup/consistency state after `currentGameMode` changes
        private void ModeDispatch_ApplyModeState()
        {
            try
            {
                switch (currentGameMode)
                {
                    case 2: // Ball: ensure `ballGoingDown` aligns with canonical gravity
                        try { ballGoingDown = !gravityReversed; } catch { }
                        break;
                    default:
                        break;
                }
            }
            catch { }
        }

        // Dispatch pending-sim-edge presses to the appropriate mode handler.
        private void ProcessModeSimPendingPress(int pendingPress, bool effectiveOnGround_local, ref bool jumpAppliedThisFrame)
        {
            try
            {
                switch (currentGameMode)
                {
                    case 0: // Cube
                        try { Cube_HandleSimPendingPress(pendingPress, effectiveOnGround_local, ref jumpAppliedThisFrame); } catch { }
                        break;
                    case 1: // Ship: no immediate sim-path pending-press handling
                        break;
                    case 2: // Ball: defer to cube-like immediate behavior for now
                        try { Cube_HandleSimPendingPress(pendingPress, effectiveOnGround_local, ref jumpAppliedThisFrame); } catch { }
                        break;
                    case 3: // Ufo: allow immediate jump
                        try { Cube_HandleSimPendingPress(pendingPress, effectiveOnGround_local, ref jumpAppliedThisFrame); } catch { }
                        break;
                    default:
                        try { Cube_HandleSimPendingPress(pendingPress, effectiveOnGround_local, ref jumpAppliedThisFrame); } catch { }
                        break;
                }
            }
            catch { }
        }
    }
}

