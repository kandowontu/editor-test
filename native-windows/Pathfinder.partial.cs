using System;
using System.Collections.Generic;
using System.Threading;

namespace FamidashEditor
{
    public partial class SimulatorWindow
    {
        // ===== PATHFINDER STATE =====
        private bool pathfinderEnabled = false;
        private volatile bool pfSimulating = false; // kept for compatibility with physics guards (always false now)

        // Precomputed input sequence from editor's PathfinderEngine
        private List<bool>? pfInputSequence = null;
        private int pfFrameIndex = 0;
        private int pfHoldCounter = 0; // Extra frames to keep X held after a jump press
        private const int PF_FRAME_DELAY = 0; // Delay pathfinder inputs by N frames to compensate for sim timing

        /// <summary>
        /// Called from SimulateNumericStep each frame when pathfinder is active.
        /// For cube/robot/ninja: hold X for 2 frames so the input survives the
        /// gravity oscillation (velY==0 only on alternate frames).
        /// For ship/ufo: use raw 1:1 per-frame inputs (pathfinder produces
        /// per-frame hold/release decisions for continuous-thrust modes).
        /// </summary>
        private bool PF_GetInput()
        {
            // If we're in the middle of holding from a previous jump press, keep holding
            if (pfHoldCounter > 0)
            {
                pfHoldCounter--;
                pfFrameIndex++; // Still advance so the delay doesn't accumulate
                return true;
            }

            if (pfInputSequence == null)
                return false;

            // Apply frame delay: read from (pfFrameIndex - PF_FRAME_DELAY)
            int readIndex = pfFrameIndex - PF_FRAME_DELAY;
            pfFrameIndex++;

            if (readIndex < 0 || readIndex >= pfInputSequence.Count)
                return false;

            bool val = pfInputSequence[readIndex];
            if (val)
            {
                // Ship and UFO use per-frame inputs — don't stretch to 2 frames.
                // Cube/robot/ninja need 2-frame hold so the jump registers on the
                // correct velY==0 frame.
                bool isContinuousThrust = (currentGameMode == 1 || currentGameMode == 3); // Ship or UFO
                if (!isContinuousThrust)
                {
                    pfHoldCounter = 1; // Hold for 1 extra frame after this one
                }
            }
            return val;
        }

        /// <summary>
        /// Injects a pathfinder input decision into the simulator's input state.
        /// </summary>
        private void PF_InjectInput(bool press)
        {
            if (press)
            {
                Interlocked.Exchange(ref keyXPressedCount, 1);
                keyXHeld = true;
                prevKeyXDown = true;
                if (currentGameMode == 2)
                    Interlocked.Exchange(ref ballToggleRequested, 1);
            }
            else
            {
                Interlocked.Exchange(ref keyXPressedCount, 0);
                keyXHeld = false;
                prevKeyXDown = false;
            }
        }

        /// <summary>
        /// Loads the precomputed pathfinder input sequence from the editor.
        /// Called when the simulator window opens with pathfinder enabled.
        /// </summary>
        private void PF_LoadPrecomputedInputs()
        {
            pfFrameIndex = 0;
            pfHoldCounter = 0;
            pfInputSequence = null;

            try
            {
                if (this.Owner is MainWindow mw && mw.PrecomputedPathfinderInputs != null)
                {
                    pfInputSequence = new List<bool>(mw.PrecomputedPathfinderInputs);
                }
            }
            catch { }
        }

        /// <summary>
        /// Returns true if pathfinder has valid precomputed input data.
        /// </summary>
        private bool PF_HasInputData()
        {
            return pfInputSequence != null && pfInputSequence.Count > 0;
        }
    }
}
