using System;
using System.Collections.Generic;
using System.Threading;

namespace FamidashEditor
{
    public partial class SimulatorWindow
    {
        // ===== PATHFINDER STATE =====
        private volatile bool pathfinderEnabled = false;
        // Remember user's explicit preference across sim open/close (static survives window re-creation)
        private static bool? _pathfinderUserPref = null;
        private volatile bool pfSimulating = false; // kept for compatibility with physics guards (always false now)

        // Precomputed input sequence from editor's PathfinderEngine
        private List<bool>? pfInputSequence = null;
        private int pfFrameIndex = 0;
        private int pfHoldCounter = 0; // Extra frames to keep X held after a jump press
        private bool pfBallHoldContinuation = false; // True when ball hold is continuing (not fresh press)
        private bool pfRawSequenceInput = false; // Raw PF sequence value BEFORE hold-continuation override (for P2 dual input)
        private const int PF_BALL_HOLD_FRAMES = 8; // Ball hold duration to bridge PF/sim timing divergence
        private const int PF_FRAME_DELAY = 0; // Sequence index delay disabled; simulator uses injection queue delay instead
        // Guard: each timer tick increments pfTickGeneration; PF_GetInput only advances pfFrameIndex
        // once per generation to prevent double-stepping even if SimulateNumericStep runs twice.
        private long pfTickGeneration = 0;
        private long pfLastAdvancedTick = -1;
        // Set to true by PF_GetInput when a phantom double-step is detected.
        // SimulateNumericStep checks this and skips the entire physics frame
        // to prevent position divergence from running gravity twice in one tick.
        private bool pfWasPhantomStep = false;
        // Track previous PF input to distinguish press (false→true) from hold (true→true).
        // NES only generates pressJump on the first frame of a button press, not on holds.
        private bool pfPrevInjectedPress = false;
        // Preserve the raw PF rising edge separately from keyXPressedCount. Some
        // non-bufferable orb paths synthesize a press while held, but UFO movement
        // itself must still use the NES false→true edge.
        private bool pfPressEdgeThisFrame = false;

        /// <summary>
        /// Called from SimulateNumericStep each frame when pathfinder is active.
        /// For cube/robot/ninja: hold X for 2 frames so the input survives the
        /// gravity oscillation (velY==0 only on alternate frames).
        /// For ship/ufo: use raw 1:1 per-frame inputs (pathfinder produces
        /// per-frame hold/release decisions for continuous-thrust modes).
        /// </summary>
        private bool PF_GetInput()
        {
            bool wasBallHoldContinuation = pfBallHoldContinuation;
            pfBallHoldContinuation = false;
            pfWasPhantomStep = false;

            // Read the raw PF sequence value for this frame BEFORE hold-continuation
            // may override it.  P2 dual input uses this to avoid inheriting P1's hold.
            {
                int rawIdx = pfFrameIndex - PF_FRAME_DELAY;
                pfRawSequenceInput = (pfInputSequence != null && rawIdx >= 0 && rawIdx < pfInputSequence.Count)
                    ? pfInputSequence[rawIdx]
                    : false;
            }

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

            // When ball hold continuation just expired (wasBallHoldContinuation
            // was set on the previous frame's hold path), the next PF sequence
            // TRUE is a fresh press — the PF independently decided to flip again.
            // Reset pfPrevInjectedPress so PF_InjectInput generates
            // keyXPressedCount=1 instead of treating it as a continuous hold.
            if (currentGameMode == 2 && wasBallHoldContinuation)
            {
                pfPrevInjectedPress = false;
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
                bool isSpider = (currentGameMode == 5);
                bool isWave = (currentGameMode == 6);
                bool isSwing = (currentGameMode == 7);
                bool isNinja = (currentGameMode == 8);
                bool isPogo = (currentGameMode == 9);
                if (isBall)
                {
                    // Ball mode: do NOT extend holds.  The ballFlipBuffer countdown
                    // (matching PF's BallInputBuffer) handles airborne-to-landing
                    // buffering independently.  Extending the hold would skip
                    // reading PF sequence values for 8 frames, missing the
                    // critical input=true at the exact landing frame.
                    pfHoldCounter = 0;
                }
                else if (!isContinuousThrust && !isCube && !isRobot && !isSpider && !isWave && !isSwing && !isNinja && !isPogo)
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
            pfPressEdgeThisFrame = press && !pfPrevInjectedPress;

            // Always clear stale press at the start of each injection.
            // On the NES, pressJump is a single-frame rising edge — if nothing
            // consumed it this frame (e.g. cube was airborne), it's gone next
            // frame.  Without this, keyXPressedCount=1 persists across airborne
            // frames and triggers a phantom jump on landing.
            Interlocked.Exchange(ref keyXPressedCount, 0);
            // Also clear ballToggleRequested — if a ball-mode press set it but a
            // portal changed the mode before BallPhysics_Fresh consumed it, the
            // stale flag persists through non-ball modes and triggers a false
            // press when ball mode is re-entered (wave→ball death in TOE2).
            Interlocked.Exchange(ref ballToggleRequested, 0);

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
                    pfPrevInjectedPress = true;
                    // Ball hold continuation is a synthetic hold for the ball flip
                    // mechanic — the PF raw input at this frame is likely false.
                    // Suppress orb activation entirely: UpdateOrbSystem has three
                    // xHeld-based paths (buffer active, buffer start, hold start)
                    // and orbHoldSuppressing blocks ALL of them.  Without this,
                    // the synthetic hold triggers orb activation up to 5+ frames
                    // early, diverging from PF which checks raw input.
                    try { orbHoldSuppressing[currplayer] = true; } catch { }
                    // Do NOT clear orbBufferActive here — the buffer was primed
                    // by a real press on the previous frame and must survive until
                    // BallPhysics_Fresh can consume it for the landing flip.
                    // Clearing it prevented the ball from flipping on landing,
                    // diverging from the PF which keeps BallInputBuffer alive.
                    return;
                }

                // Only generate a fresh press (keyXPressedCount=1) on a false→true
                // transition. Continuous holds (true→true) should NOT generate new
                // presses — matching NES behavior where holding the button keeps
                // holdJump=true but pressJump only fires on the initial press frame.
                // Exception 1: Non-bufferable orb modes (ship=1, UFO=3, wave=6)
                // need a fresh press on EVERY true frame so the orb system can
                // activate orbs during sustained holds — these modes use xPressed
                // (not xHeld+buffer) for orb activation.
                // Exception 2: Cube mode (0) — the PF's input sequence may contain
                // consecutive trues from CubeWillLandThisFrame predictions across
                // airborne frames.  If the SIM's landing frame differs from the
                // PF's prediction, a stale held-true triggers a hold-jump at the
                // wrong frame.  Always generating a press lets the cube jump code
                // use pressJump (consumed once) instead of holdJump (persistent),
                // ensuring the jump only fires on the exact frame velY==0.
                if (!pfPrevInjectedPress || !CanBufferOrb(currentGameMode) || currentGameMode == 0)
                {
                    Interlocked.Exchange(ref keyXPressedCount, 1);
                    // ballToggleRequested must be guarded by the same fresh-press
                    // condition.  Without this, a continued hold (pfPrevInjected
                    // =true) still sets ballToggleRequested→pressJump=true, which
                    // bypasses BallFlipCooldown and causes a spurious double-flip.
                    if (currentGameMode == 2)
                        Interlocked.Exchange(ref ballToggleRequested, 1);
                }
                keyXHeld = true;
                prevKeyXDown = true;
                pfPrevInjectedPress = true;
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
                keyXHeld = false;
                prevKeyXDown = false;
                pfPrevInjectedPress = false;
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
            pfRawSequenceInput = false;
            pfLastAdvancedTick = -1;
            pfPrevInjectedPress = false;
            pfPressEdgeThisFrame = false;
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
