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
        private bool pfBallHoldContinuation = false; // True when ball hold is continuing (not fresh press)
        private const int PF_BALL_HOLD_FRAMES = 8; // Ball hold duration to bridge PF/sim timing divergence
        private const int PF_FRAME_DELAY = 0; // Delay pathfinder inputs by N frames to compensate for sim timing
        // Guard: each timer tick increments pfTickGeneration; PF_GetInput only advances pfFrameIndex
        // once per generation to prevent double-stepping even if SimulateNumericStep runs twice.
        private long pfTickGeneration = 0;
        private long pfLastAdvancedTick = -1;
        // Set to true by PF_GetInput when a phantom double-step is detected.
        // SimulateNumericStep checks this and skips the entire physics frame
        // to prevent position divergence from running gravity twice in one tick.
        private bool pfWasPhantomStep = false;

        /// <summary>
        /// Called from SimulateNumericStep each frame when pathfinder is active.
        /// For cube/robot/ninja: hold X for 2 frames so the input survives the
        /// gravity oscillation (velY==0 only on alternate frames).
        /// For ship/ufo: use raw 1:1 per-frame inputs (pathfinder produces
        /// per-frame hold/release decisions for continuous-thrust modes).
        /// </summary>
        private bool PF_GetInput()
        {
            pfBallHoldContinuation = false;
            pfWasPhantomStep = false;

            // Double-step guard: if this tick already advanced pfFrameIndex, replay last input
            if (pfLastAdvancedTick == pfTickGeneration)
            {
                pfWasPhantomStep = true;
                // Return same result as last call without advancing index
                if (pfHoldCounter > 0) return true;
                if (pfInputSequence == null) return false;
                int prevReadIndex = (pfFrameIndex - 1) - PF_FRAME_DELAY;
                return prevReadIndex >= 0 && prevReadIndex < pfInputSequence.Count && pfInputSequence[prevReadIndex];
            }
            pfLastAdvancedTick = pfTickGeneration;

            // If we're in the middle of holding from a previous jump press, keep holding
            if (pfHoldCounter > 0)
            {
                // Ball mode: stop holding early if the ball already flipped.
                // ballFlipCooldown > 0 means a flip just fired in BallPhysics_Fresh.
                // Releasing the hold lets ballSwitched clear and allows the next
                // PF true in the sequence to start a fresh press for the next flip.
                if (currentGameMode == 2 && ballFlipCooldown > 0)
                {
                    pfHoldCounter = 0;
                    // Fall through to read next sequence value normally
                }
                else
                {
                    pfHoldCounter--;
                    pfFrameIndex++; // Still advance so the delay doesn't accumulate
                    if (currentGameMode == 2)
                        pfBallHoldContinuation = true;
                    return true;
                }
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
                // Cube mode uses direct input — the pathfinder's internal sim applies
                // input for exactly 1 frame, so playback must match.
                // Robot and wave modes also use per-frame inputs — the PF stores
                // true for every frame of the hold, so stretching would double
                // the hold duration and skip release boundaries.
                // Ball mode: extended hold to bridge PF/sim timing divergence.
                // The NES ball uses hold (not press) so the button must stay held
                // across the airborne-to-landing gap for the buffered flip to fire.
                // Early termination (ballFlipCooldown check above) prevents consuming
                // subsequent true values needed for staircase multi-flips.
                bool isContinuousThrust = (currentGameMode == 1 || currentGameMode == 3); // Ship or UFO
                bool isCube = (currentGameMode == 0);
                bool isBall = (currentGameMode == 2);
                bool isRobot = (currentGameMode == 4);
                bool isWave = (currentGameMode == 6);
                if (isBall)
                {
                    pfHoldCounter = PF_BALL_HOLD_FRAMES; // Hold for N extra frames after press
                }
                else if (!isContinuousThrust && !isCube && !isRobot && !isWave)
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
                // Ball hold continuation: only maintain keyXHeld (hold state),
                // do NOT generate a fresh press or toggle request.
                // This gives the sim holdJump=true,pressJump=false on hold frames
                // so the hold-buffer path fires on the landing frame.
                if (pfBallHoldContinuation)
                {
                    keyXHeld = true;
                    // Keep prevKeyXDown true so the sim sees continuous hold
                    prevKeyXDown = true;
                    return;
                }

                Interlocked.Exchange(ref keyXPressedCount, 1);
                keyXHeld = true;
                prevKeyXDown = true;
                if (currentGameMode == 2)
                    Interlocked.Exchange(ref ballToggleRequested, 1);
                // Clear orb hold-suppression on EVERY pathfinder press.
                // The pathfinder explicitly decides each frame whether to
                // press or not; it doesn't need the simulator's anti-hold
                // guard (which prevents a continuous hold from a ground jump
                // from accidentally activating orbs).  Without this, a
                // continuous True sequence (ground jump → hold-jump → orb)
                // keeps orbHoldSuppressing=true and blocks orb activation.
                try { orbHoldSuppressing[currplayer] = false; } catch { }
                try { orbHoldConsumedKeyStillDown[currplayer] = false; } catch { }
                try { orbBufferActive[currplayer] = true; } catch { }
            }
            else
            {
                Interlocked.Exchange(ref keyXPressedCount, 0);
                keyXHeld = false;
                prevKeyXDown = false;
                // Mirror what KeyUp handler does: clear orb suppression so a
                // subsequent pathfinder press while airborne can activate orbs.
                // Without this, orbHoldSuppressing stays true after a ground jump
                // because UpdateOrbHoldSuppression only runs when grounded (velY==0).
                try { orbHoldSuppressing[currplayer] = false; } catch { }
                try { orbHoldConsumedKeyStillDown[currplayer] = false; } catch { }
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
            pfBallHoldContinuation = false;
            pfLastAdvancedTick = -1;
            pfInputSequence = null;

            try
            {
                if (this.Owner is MainWindow mw && mw.PrecomputedPathfinderInputs != null)
                {
                    pfInputSequence = new List<bool>(mw.PrecomputedPathfinderInputs);

                    // NOTE: Previously pre-seeded coins here (set sprites[idx]=-1)
                    // so they'd vanish before playback. Removed so coins are visible
                    // during replay and collected naturally via CheckCoinCollision.
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
