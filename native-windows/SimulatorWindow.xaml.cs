using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Threading;

namespace FamidashEditor
{
    public partial class SimulatorWindow : Window
    {
        // Cached embedded resource lookup to avoid calling GetManifestResourceNames() every frame.
        // Maps resource suffix (e.g. "cube.png") to BitmapImage. Thread-safe via lock.
        private static readonly object s_resourceCacheLock = new object();
        private static System.Collections.Generic.Dictionary<string, BitmapImage?>? s_resourceImageCache;
        private static string[]? s_resourceNames;

        // Static arrays to avoid per-frame allocation in animation/render loops
        private static readonly string[] s_cubeFrameNames = new string[]
        {
            "cube_00_frame_0_upright.png",
            "cube_01_frame_1_45cw.png",
            "cube_02_frame_2_90cw_side.png",
            "cube_03_frame_3_135cw.png",
            "cube_04_frame_4_180_upside.png",
            "cube_05_frame_5_225cw.png",
            "cube_06_frame_6_270cw_opposite.png"
        };
        private static readonly string[] s_ninjaFrameNames = new string[]
        {
            "ninja_00_frame_0.png",
            "ninja_01_frame_1.png",
            "ninja_02_frame_2.png",
            "ninja_03_frame_3.png",
            "ninja_04_frame_4.png",
            "ninja_05_frame_5.png",
            "ninja_06_frame_6.png"
        };

        /// <summary>
        /// Load an embedded resource image by filename suffix, using a static cache so each
        /// image is decoded from the assembly at most once across all SimulatorWindow instances.
        /// Returns null if not found.
        /// </summary>
        private static BitmapImage? LoadCachedResourceImage(string suffix)
        {
            lock (s_resourceCacheLock)
            {
                s_resourceImageCache ??= new System.Collections.Generic.Dictionary<string, BitmapImage?>(StringComparer.OrdinalIgnoreCase);
                if (s_resourceImageCache.TryGetValue(suffix, out var cached)) return cached;

                s_resourceNames ??= System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceNames();
                var found = Array.Find(s_resourceNames, n => n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
                BitmapImage? result = null;
                if (found != null)
                {
                    using (var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(found))
                    {
                        if (stream != null)
                        {
                            var bi = new BitmapImage();
                            bi.BeginInit();
                            bi.CacheOption = BitmapCacheOption.OnLoad;
                            bi.StreamSource = stream;
                            bi.EndInit();
                            bi.Freeze();
                            result = bi;
                        }
                    }
                }
                s_resourceImageCache[suffix] = result;
                return result;
            }
        }

        // Current player index for dual-player support (0 or 1)
        private int currplayer = 0;

        // Window base title used for UI string formatting
        private string baseWindowTitle = "Simulator";
        // Option to hide trigger sprites in simulator rendering
        private bool hideTriggerSprites = false;

        public void SetHideTriggerSprites(bool v) { try { hideTriggerSprites = v; } catch { } }

        // Public toggle to show sprite hitboxes in the simulator viewport.
        public bool ShowSpriteHitboxes { get; set; } = false;

        // Public toggle used by the main window: show tile/hitbox overlays in the simulator.
        public bool ShowTileHitboxes { get; set; } = false;

        // Starting speed UI index (0=0.5x,1=1x,2=2x,3=3x,4=4x)
        private int startingSpeedUiIndex = 1;
		private readonly bool forcePlatformer;
		private readonly SharedPhysics.CollisionMap platformerCollisionMap;
		private int currXScrollStop_fixed = 0x5000;
		private int targetXScrollStop_fixed = 0x5000;

        // Called by the editor to set the starting speed UI index so simulators match the editor.
        public void SetStartingSpeedUiIndex(int idx)
        {
            try
            {
                startingSpeedUiIndex = idx;
                speed = idx;
                try { Dispatcher.BeginInvoke(new Action(() => { try { UpdateSpeedDisplay(); } catch { } try { RenderFrame(); } catch { } })); } catch { }
            }
            catch { }
        }

            private void UpdateSimTitle()
            {
                try
                {
                    string s = $"{baseWindowTitle} — Sim {(simTimeScale * 100.0):F0}%";
                        try { this.Title = s; } catch { }
                        orbHoldSuppressing[currplayer] = false;
                }
                catch { }
            }

        private bool IsHiddenTriggerSprite(int s)
        {
            try
            {
                // Always hide freecam portal sprites (they are invisible triggers)
                if (s == 0xDD || s == 0xED) return true;
                // Always hide wrap mode portal sprites (they are invisible triggers)
                if (s == 0x8E || s == 0x9E) return true;
                // Always hide timewarp triggers
                if (s == 0xF4 || s == 0xF5) return true;
                // Always hide player visibility triggers
                if (s == 0x6F || s == 0x7F) return true;
                // Always hide player trail triggers
                if (s == 0xF2 || s == 0xF3) return true;
                // Always hide invisible teleport portals (0x75-0x78)
                if (s >= 0x75 && s <= 0x78) return true;
                if (!hideTriggerSprites) return false;
                return false;
            }
            catch { return false; }
        }

        // In-memory debug buffer for simulator collision/pass-through decisions.
        // We avoid writing persistent log files by default; this buffer captures
        // recent messages and also forwards them to Debug.WriteLine so IDEs
        // can show them during runs. The buffer is bounded to avoid unbounded
        // memory growth.
        private readonly System.Collections.Generic.List<string> simDebugBuffer = new System.Collections.Generic.List<string>();
        private const int SIM_DEBUG_BUFFER_MAX = 4096;
        // By default we write debug output to a timestamped temp file so you can
        // open it after reproducing the issue. Change to false to disable file
        // writes.
#pragma warning disable CS0414 // assigned but never used (consumed inside DISABLE_DEBUG_LOGGING guard)
        private readonly bool simDebugWriteToFile = true;
#pragma warning restore CS0414
        private readonly string simDebugLogPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"famidash_sim_debug_{System.DateTime.UtcNow:yyyyMMdd_HHmmss}.txt");
        private static readonly object simDebugFileLock = new object();

        private string _levelName = "";
        private bool _levelNameLogged;
        public string LevelName { get => _levelName; set { _levelName = value ?? ""; } }

        private void AppendSimDebug(string msg)
        {
#if !DISABLE_DEBUG_LOGGING
            // Skip ALL debug logging during pathfinder speculative simulation.
            // Each physics frame generates ~6 debug entries with string interpolation,
            // DateTime formatting, lock acquisition, and FILE I/O. At 180 speculative
            // frames per evaluation this would be ~1000 file writes per real frame.
            if (pfSimulating) return;

            // Early return if debug is completely disabled
            if (!simDebugWriteToFile && simDebugBuffer.Count == 0)
            {
                return;
            }
            
            try
            {
                string t = DateTime.UtcNow.ToString("o") + " " + msg;
                lock (simDebugBuffer)
                {
                    simDebugBuffer.Add(t);
                    if (simDebugBuffer.Count > SIM_DEBUG_BUFFER_MAX)
                    {
                        // Trim oldest entries
                        int remove = simDebugBuffer.Count - SIM_DEBUG_BUFFER_MAX;
                        simDebugBuffer.RemoveRange(0, remove);
                    }
                }
                try { System.Diagnostics.Debug.WriteLine(t); } catch { }
#endif
#if !DISABLE_DEBUG_LOGGING
                if (simDebugWriteToFile)
                {
                    lock (simDebugFileLock)
                    {
                        try
                        {
                            System.IO.File.AppendAllText(simDebugLogPath, t + System.Environment.NewLine);
                        }
                        catch { }
                    }
                }
            }
            catch { }
#endif
        }

        // Return a snapshot of the current in-memory debug buffer (most-recent last).
        public string[] GetSimDebugSnapshot()
        {
            try
            {
                lock (simDebugBuffer)
                {
                    return simDebugBuffer.ToArray();
                }
            }
            catch { return new string[0]; }
        }

        // Return the path to the on-disk debug log (may not exist until a message is written).
        public string GetSimDebugLogPath()
        {
            try { return simDebugLogPath; } catch { return string.Empty; }
        }

        // Update effective gravity/jump/max-fall based on the current mode and flags
        private void UpdateEffectiveGravity()
        {
            try
            {
                // Choose base values depending on the current game mode.
                switch (currentGameMode)
                {
                    case 1: // ship
                        effectiveGravity_fixed = SHIP_GRAVITY;
                        effectiveJumpVel_fixed = 0; // ship doesn't use the generic jump impulse
                        effectiveMaxFall_fixed = SHIP_MAX_FALLSPEED;
                        break;
                    case 2: // ball
                        effectiveGravity_fixed = BALL_GRAVITY;
                        effectiveJumpVel_fixed = BALL_IMMEDIATE_VEL;
                        effectiveMaxFall_fixed = BALL_MAX_FALLSPEED;
                        break;
                    case 3: // UFO
                        effectiveGravity_fixed = UFO_GRAVITY;
                        effectiveJumpVel_fixed = UFO_JUMP_VEL;
                        effectiveMaxFall_fixed = UFO_MAX_FALLSPEED;
                        break;
                    case 6: // Wave (no gravity)
                        effectiveGravity_fixed = 0;
                        effectiveJumpVel_fixed = 0;
                        effectiveMaxFall_fixed = 0;
                        break;
                    case 9: // Pogo (uses ball physics)
                        effectiveGravity_fixed = BALL_GRAVITY;
                        effectiveJumpVel_fixed = BALL_IMMEDIATE_VEL;
                        effectiveMaxFall_fixed = BALL_MAX_FALLSPEED;
                        break;
                    case 10: // Snake (uses wave physics - no gravity)
                        effectiveGravity_fixed = 0;
                        effectiveJumpVel_fixed = 0;
                        effectiveMaxFall_fixed = 0;
                        break;
                    case 11: // Football (uses cube physics)
                        effectiveGravity_fixed = CUBE_GRAVITY;
                        effectiveJumpVel_fixed = CUBE_JUMP_VEL;
                        effectiveMaxFall_fixed = CUBE_MAX_FALLSPEED;
                        break;
                    case 0: // cube (default)
                    default:
                        effectiveGravity_fixed = CUBE_GRAVITY;
                        effectiveJumpVel_fixed = CUBE_JUMP_VEL;
                        effectiveMaxFall_fixed = CUBE_MAX_FALLSPEED;
                        break;
                }

                // If logical gravity is reversed for numeric purposes, flip numeric signs
                // so integration moves in the opposite direction. Numeric inversion is
                // determined by the canonical `gravityReversed` flag OR the compatibility
                // `effectiveInvertedByW` which allows numeric-only inversion when the
                // editor's No-Death option requests it.
                bool numericInvert = gravityReversed || effectiveInvertedByW;
                if (numericInvert)
                {
                    effectiveGravity_fixed = -effectiveGravity_fixed;
                    effectiveJumpVel_fixed = -effectiveJumpVel_fixed;
                    effectiveMaxFall_fixed = -effectiveMaxFall_fixed;
                }

                // Debug: log effective values and flags so we can verify toggles work
                try { System.Diagnostics.Debug.WriteLine($"UpdateEffectiveGravity: gravityReversed={gravityReversed} effectiveGravity={effectiveGravity_fixed} effectiveJump={effectiveJumpVel_fixed} effectiveMaxFall={effectiveMaxFall_fixed}"); } catch { }
            }
            catch { }
        }

        private void UpdatePlayerSpeed()
        {
            try
            {
                // Update horizontal velocity based on speed setting
                // Speed indices: 0=0.5x, 1=1x, 2=2x, 3=3x, 4=4x
                int[] speedValues = { CUBE_SPEED_X05, CUBE_SPEED_X1, CUBE_SPEED_X2, CUBE_SPEED_X3, CUBE_SPEED_X4 };
                if (speed >= 0 && speed < speedValues.Length)
                {
                    playerVelX_fixed = speedValues[speed];
                }
            }
            catch { }
        }

        // NES drawcube_sprite_table: 24 entries mapping rotation frame to tile index (bits 0-2) + flip (bits 6-7).
        // NOFLIP=0x00, H_FLIP=0x40, V_FLIP=0x80, HVFLIP=0xC0
        private static readonly byte[] drawcube_sprite_table = new byte[24]
        {
            0x00, 0x01, 0x02, 0x03, 0x04, 0x05,   // Frames  0- 5: NOFLIP | tile 0-5
            0x06, 0x85, 0x84, 0x83, 0x82, 0x81,   // Frames  6-11: tile 6 noflip, then V_FLIP | tile 5-1
            0xC0, 0xC1, 0xC2, 0xC3, 0xC4, 0xC5,   // Frames 12-17: HVFLIP | tile 0-5
            0xC6, 0x45, 0x44, 0x43, 0x42, 0x41     // Frames 18-23: tile 6 HV, then H_FLIP | tile 5-1
        };

        // NES rounding table: snaps frame to nearest 90° (0, 6, 12, 18)
        private static readonly int[] drawcube_rounding_table = new int[13]
        {
            0, -1, -2, 3, 2, 1, 0, -1, -2, 3, 2, 1, -24
        };

        /// <summary>
        /// Update cube rotation state based on velocity and gravity.
        /// Implements the cube animation logic from nesdash.s drawplayerone/cube routine.
        /// Uses 24-frame cycle (0-23) matching NES, with drawcube_sprite_table for tile+flip.
        /// 
        /// When velocity == 0: snap to nearest 90° (0, 6, 12, 18)
        /// When velocity != 0: rotate based on gravity direction
        /// </summary>
        private void UpdateCubeRotation()
        {
            try
            {
                int frameIndex = (cubeRotate_fixed >> 8) & 0xFF;  // Extract high byte (frame 0-23)
                int subFrame = cubeRotate_fixed & 0xFF;            // Extract low byte (accumulator)
                
                // NES: BIT _cube_data; BMI @round — if on slope, ALWAYS use rounding
                // regardless of velocity (skip velocity-based rotation entirely)
                bool onSlope = (currplayer_slope_frames > 0 || currplayer_slope_type != 0);
                
                if (onSlope && currplayer_slope_type > 0 && currplayer_slope_type < slopeRotationFrame_24.Length)
                {
                    int slopeFrame = slopeRotationFrame_24[currplayer_slope_type];
                    cubeRotate_fixed = slopeFrame << 8;
                    AppendSimDebug($"[CUBE_ROT] Slope rotation: slope_type=0x{currplayer_slope_type:X2} -> frame {slopeFrame}");
                }
                else if (playerVelY_fixed == 0)
                {
                    // NES: round to nearest 90° using drawcube_rounding_table
                    int idx = frameIndex;
                    if (idx >= 12) idx -= 12;
                    if (idx < 0) idx = 0;
                    if (idx > 12) idx = 12;
                    int rounded = frameIndex + drawcube_rounding_table[idx];
                    if (rounded >= 24) rounded -= 24;
                    if (rounded < 0) rounded += 24;
                    cubeRotate_fixed = rounded << 8;
                }
                else
                {
                    // Velocity is non-zero: accumulate gravity increment
                    // NES: when gravity is inverted, subtract CUBE_GRAVITY (rotate backwards)
                    int gravityIncrement = GameModePhysics.CUBE_GRAVITY(currplayer_table_idx);
                    if (gravityFlipped) gravityIncrement = -gravityIncrement;
                    subFrame += gravityIncrement;
                    
                    AppendSimDebug($"[CUBE_ROT] gravityFlipped={gravityFlipped} increment={gravityIncrement:X2} subFrame={subFrame} frameIndex={frameIndex}");
                    
                    // Handle overflow/underflow in low byte
                    if (subFrame >= 256)
                    {
                        frameIndex++;
                        subFrame -= 256;
                        
                        // Wrap at 24 frames (0-23)
                        if (frameIndex >= 24)
                        {
                            frameIndex = 0;
                        }
                    }
                    else if (subFrame < 0)
                    {
                        frameIndex--;
                        subFrame += 256;
                        
                        // Wrap backward: 0 -> 23
                        if (frameIndex < 0)
                        {
                            frameIndex = 23;
                        }
                    }
                    
                    // Recombine into 16-bit value
                    cubeRotate_fixed = (frameIndex << 8) | subFrame;
                }
            }
            catch { }
        }

        /// <summary>
        /// Update ship rotation frame based on velocity.
        /// Similar to cube rotation, but uses direct velocity calculation with clamping.
        /// NOTE: playerVelY_fixed in the simulator uses a different scale than NES velocity,
        /// so we need to amplify it to get the full range of frame animations.
        /// </summary>
        private void UpdateShipRotation()
        {
            try
            {
                // Ship frame index: 0x0400 - playerVelY (from NES code)
                // The NES uses a simple formula: cube_rotate = 0x0400 - player_vel_y
                // where player_vel_y is the full 16-bit signed velocity
                // Add tolerance/dead zone around zero velocity to prevent flickering when grounded
                int adjustedVel = playerVelY_fixed;
                if (adjustedVel > -0x0080 && adjustedVel < 0x0080)
                {
                    adjustedVel = 0x0100;  // Snap to 0x0100 within ±128 units to keep straight frame
                }
                
                int cubeRotate = 0x0400 - adjustedVel;
                int hiBytes = (cubeRotate >> 8) & 0xFF;
                
                // Apply NES clamping: if high_byte >= 0x08, clamp the entire value
                // This prevents velocity extremes from wrapping around
                if (hiBytes >= 0x08)
                {
                    if (hiBytes < 0x80)
                    {
                        cubeRotate = 0x07FF;  // High byte becomes 0x07
                    }
                    else
                    {
                        cubeRotate = 0x0000;  // High byte becomes 0x00
                    }
                }
                
                shipRotate_fixed = cubeRotate;
                int frameIndex = (shipRotate_fixed >> 8) & 0xFF;
                
                AppendSimDebug($"[SHIP_ROT] velY=0x{playerVelY_fixed:X4}, rotate=0x{cubeRotate:X4}, frame={frameIndex}");
            }
            catch { }
        }

        /// <summary>
        /// Update swingcopter rotation frame based on velocity.
        /// Identical to ship animation logic - velocity-based 7 frames.
        /// </summary>
        private void UpdateSwingcopterRotation()
        {
            try
            {
                // Swingcopter frame index: 0x0400 - playerVelY (same as ship)
                // Add tolerance/dead zone around zero velocity to prevent flickering
                int adjustedVel = playerVelY_fixed;
                if (adjustedVel > -0x0080 && adjustedVel < 0x0080)
                {
                    adjustedVel = 0x0100;  // Snap to 0x0100 within ±128 units
                }
                
                int cubeRotate = 0x0400 - adjustedVel;
                int hiBytes = (cubeRotate >> 8) & 0xFF;
                
                // Apply NES clamping
                if (hiBytes >= 0x08)
                {
                    if (hiBytes < 0x80)
                    {
                        cubeRotate = 0x07FF;
                    }
                    else
                    {
                        cubeRotate = 0x0000;
                    }
                }
                
                swingcopterRotate_fixed = cubeRotate;
                int frameIndex = (swingcopterRotate_fixed >> 8) & 0xFF;
                
                AppendSimDebug($"[SWING_ROT] velY=0x{playerVelY_fixed:X4}, rotate=0x{cubeRotate:X4}, frame={frameIndex}");
            }
            catch { }
        }

        /// <summary>
        /// Update football rotation using cube-style rotation (0-23 frames) with flip table.
        /// This provides seamless 360-degree rotation for football mode.
        /// Uses same gravity-based accumulator system as cube rotation but with 24 frames.
        /// While charging on ground, rotates backwards based on charge power (like cube).
        /// </summary>
        private void UpdateFootballRotation()
        {
            try
            {
                // NES: BIT _cube_data; BMI @round — if on slope, ALWAYS use rounding
                bool onSlope_fb = (currplayer_slope_frames > 0 || currplayer_slope_type != 0);
                
                if (onSlope_fb && currplayer_slope_type > 0 && currplayer_slope_type < slopeRotationFrame_24.Length)
                {
                    int slopeFrame = slopeRotationFrame_24[currplayer_slope_type];
                    footballRotate_fixed = slopeFrame << 8;
                    AppendSimDebug($"[FOOTBALL_ROT] SLOPE: slope_type=0x{currplayer_slope_type:X2} -> frame {slopeFrame}");
                }
                else if (playerVelY_fixed == 0)
                {
                    // NES football: when vel==0, always reset high byte to 0 first (upright),
                    // then check chargepower for backward tilt override.
                    if (footballChargeFrames > 0)
                    {
                        // While charging on ground, rotate backwards based on charge power
                        int frameToSet;
                        if (footballChargeFrames < 10)
                            frameToSet = 23;
                        else if (footballChargeFrames < 20)
                            frameToSet = 22;
                        else if (footballChargeFrames < 30)
                            frameToSet = 21;
                        else if (footballChargeFrames < 38)
                            frameToSet = 20;
                        else if (footballChargeFrames < 50)
                            frameToSet = 20;
                        else
                            frameToSet = 6;
                        
                        footballRotate_fixed = frameToSet << 8;
                        AppendSimDebug($"[FOOTBALL_ROT] CHARGE: chargeFrames={footballChargeFrames} -> frame={frameToSet}");
                    }
                    else
                    {
                        // No charge — static at frame 0 (upright)
                        footballRotate_fixed = 0;
                    }
                }
                else
                {
                    // Velocity is non-zero (airborne): accumulate gravity like cube rotation
                    int frameIndex = (footballRotate_fixed >> 8) & 0xFF;
                    int subFrame = footballRotate_fixed & 0xFF;
                    
                    int gravityIncrement = GameModePhysics.CUBE_GRAVITY(currplayer_table_idx);
                    // NES: when gravity flipped, subtract instead of add (rotate backwards)
                    if (gravityFlipped) gravityIncrement = -gravityIncrement;
                    
                    subFrame += gravityIncrement;
                    
                    // Handle overflow (positive wrap)
                    if (subFrame >= 256)
                    {
                        frameIndex++;
                        subFrame -= 256;
                        if (frameIndex >= 24) frameIndex = 0;
                    }
                    // Handle underflow (negative wrap)
                    else if (subFrame < 0)
                    {
                        frameIndex--;
                        subFrame += 256;
                        if (frameIndex < 0) frameIndex = 23;
                    }
                    
                    footballRotate_fixed = (frameIndex << 8) | (subFrame & 0xFF);
                    AppendSimDebug($"[FOOTBALL_ROT] AIR: frame={frameIndex} sub={subFrame & 0xFF} (grav=0x{gravityIncrement:X2})");
                }
            }
            catch { }
        }

        /// <summary>
        /// Get the sprite frame and flip flags for the current cube rotation.
        /// Returns the NES drawcube_sprite_table entry:
        ///   bits 0-2: tile index (0-6), bits 6-7: flip flags (00=NONE, 40=H, 80=V, C0=HV).
        /// </summary>
        private int GetCubeSpriteFrame()
        {
            try
            {
                int frameIndex = (cubeRotate_fixed >> 8) & 0xFF;
                if (frameIndex < 0 || frameIndex >= 24)
                    frameIndex = 0;
                return drawcube_sprite_table[frameIndex];
            }
            catch { return 0; }
        }

        /// <summary>
        /// Update mini cube rotation based on velocity and gravity (same logic as full cube).
        /// Uses 16-bit fixed-point: high byte = frame index (0-23), low byte = accumulator (0-255).
        /// </summary>
        private void UpdateCubeRotationMini()
        {
            try
            {
                int frameIndex = (cubeRotateMini_fixed >> 8) & 0xFF;  // Extract high byte (frame 0-23)
                int subFrame = cubeRotateMini_fixed & 0xFF;            // Extract low byte (accumulator)
                
                // NES: BIT _cube_data; BMI @round — if on slope, ALWAYS use rounding
                bool onSlope = (currplayer_slope_frames > 0 || currplayer_slope_type != 0);
                
                if (onSlope && currplayer_slope_type > 0 && currplayer_slope_type < slopeRotationFrame_24.Length)
                {
                    int slopeFrame = slopeRotationFrame_24[currplayer_slope_type];
                    cubeRotateMini_fixed = slopeFrame << 8;
                }
                else if (playerVelY_fixed == 0)
                {
                    // Round to nearest 90° using drawcube_rounding_table
                    int idx = frameIndex;
                    if (idx >= 12) idx -= 12;
                    if (idx < 0) idx = 0;
                    if (idx > 12) idx = 12;
                    int rounded = frameIndex + drawcube_rounding_table[idx];
                    if (rounded >= 24) rounded -= 24;
                    if (rounded < 0) rounded += 24;
                    cubeRotateMini_fixed = rounded << 8;
                }
                else
                {
                    // Velocity is non-zero: accumulate gravity increment
                    int gravityIncrement = GameModePhysics.CUBE_GRAVITY(currplayer_table_idx);
                    if (gravityFlipped) gravityIncrement = -gravityIncrement;
                    subFrame += gravityIncrement;
                    
                    // Handle overflow/underflow in low byte
                    if (subFrame >= 256)
                    {
                        frameIndex++;
                        subFrame -= 256;
                        if (frameIndex >= 24) frameIndex = 0;
                    }
                    else if (subFrame < 0)
                    {
                        frameIndex--;
                        subFrame += 256;
                        if (frameIndex < 0) frameIndex = 23;
                    }
                    
                    // Recombine into 16-bit value
                    cubeRotateMini_fixed = (frameIndex << 8) | subFrame;
                }
            }
            catch { }
        }

        /// <summary>
        /// Get the sprite frame index for the current mini cube rotation.
        /// Maps the 24-frame rotation indices to the 5 available mini frames using drawcube_sprite_table.
        /// </summary>
        private int GetCubeSpriteMiniFrame()
        {
            try
            {
                int frameIndex = (cubeRotateMini_fixed >> 8) & 0xFF;
                if (frameIndex < 0 || frameIndex >= 24)
                    frameIndex = 0;
                // Get tile index (0-6) from sprite table
                int tileIdx = drawcube_sprite_table[frameIndex] & 0x07;
                // Map 7 tiles to 5 mini frames: 0->0, 1->1, 2->1, 3->2, 4->2, 5->3, 6->3
                if (tileIdx == 0) return 0;
                if (tileIdx <= 2) return 1;
                if (tileIdx <= 4) return 2;
                return 3;
            }
            catch { return 0; }
        }

        /// <summary>
        /// Get the ship frame index (0-7) based on stored rotation value.
        /// </summary>
        private int GetShipSpriteFrame()
        {
            try
            {
                // Use the stored shipRotate_fixed value that was updated in UpdateShipRotation()
                int frameIndex = (shipRotate_fixed >> 8) & 0xFF;
                
                // Clamp to 0-7 range
                if (frameIndex > 0x07) frameIndex = 0x07;
                if (frameIndex < 0x00) frameIndex = 0x00;
                
                // NES does 7 - frame at display time when gravity is flipped,
                // reversing the tilt direction to match inverted flight.
                if (currplayer_gravity != 0)
                {
                    frameIndex = 7 - frameIndex;
                }
                
                return frameIndex;
            }
            catch { return 0; }
        }

        /// <summary>
        /// Get the swingcopter frame index (0-7) based on stored rotation value.
        /// </summary>
        private int GetSwingcopterSpriteFrame()
        {
            try
            {
                // Use the stored swingcopterRotate_fixed value that was updated in UpdateSwingcopterRotation()
                int frameIndex = (swingcopterRotate_fixed >> 8) & 0xFF;
                
                // Clamp to 0-7 range
                if (frameIndex > 0x07) frameIndex = 0x07;
                if (frameIndex < 0x00) frameIndex = 0x00;
                
                // If gravity is inverted, reverse the frame
                if (currplayer_gravity != 0)
                {
                    frameIndex = 7 - frameIndex;
                }
                
                return frameIndex;
            }
            catch { return 0; }
        }

        // Static flip table for football rotation - avoids per-frame allocation
        private static readonly int[] s_footballFlipTable = new int[] {
            // Frames 0-5: No flip
            0x00, 0x01, 0x02, 0x03, 0x04, 0x05,
            // Frame 6 + Frames 5-1 with V_FLIP (0x80)
            0x06, 0x85, 0x84, 0x83, 0x82, 0x81,
            // Frames 0-5 with HV_FLIP (0xC0)
            0xC0, 0xC1, 0xC2, 0xC3, 0xC4, 0xC5,
            // Frame 6 + Frames 5-1 with H_FLIP (0x40)
            0xC6, 0x45, 0x44, 0x43, 0x42, 0x41
        };

        /// <summary>
        /// Get football sprite frame and flip flags (0-6 for frame, with flip bits).
        /// Uses the flip table from NES nesdash.s for 360-degree seamless rotation.
        /// Flip table: { 0,0,1,2,2,3,4,4, 6,5F,4F,3F,2F,1F, 0HV,1HV,2HV,3HV,4HV,5HV, 6H,5H,4H,3H,2H,1H }
        /// </summary>
        private int GetFootballSpriteFrameAndFlip()
        {
            try
            {
                // Extract frame index (0-23) from high byte
                int frameIndex = (footballRotate_fixed >> 8) & 0xFF;
                
                // Clamp to valid 24-frame range
                if (frameIndex > 0x17) frameIndex = 0x17;  // 0x17 = 23
                if (frameIndex < 0x00) frameIndex = 0x00;
                
                // Apply flip table based on frame index
                // Flip table maps 24 frames with flip flags for 360-degree rotation
                
                return s_footballFlipTable[frameIndex];  // Return frame (low 3 bits) + flip flags (high bits)
            }
            catch { return 0; }
        }

        // Map certain simulator tile indices to alternative indices for display.
        // This allows specific tile codes to render exactly like other tiles
        // (or be rendered as fully transparent by mapping to 0x00).
        private int ResolveSimulatorTileIndex(int idx)
        {
            try
            {
                if (idx >= 1000 || idx < 0) return idx;
                switch (idx)
                {
                    // Examples: render these special tiles as other tile graphics
                    case 0xD9:
                    case 0xDA:
                        return 0x11; // render like tile 0x11
                    case 0xDB:
                    case 0xDC:
                        return 0x1B; // render like tile 0x1B
                    case 0x8F:
                        return 0x2F; // render like tile 0x2F

                    // Render these indices as fully transparent (map to 0x00)
                    case 0xFC:
                    case 0xDF:
                    case 0xE3:
                    case 0xFE:
                    case 0xFF:
                        return 0x00;

                    // Add additional remaps here as needed.
                    default:
                        return idx;
                }
            }
            catch { return idx; }
        }

        // Sprite geometry tables (from user-provided data).
        // NES sentinel values: SPBH=0xFF, DECO=0xFE, COLR=0xFD, OUTL=0xFC
        // Sprites with height >= 0xFC are skipped during collision (matching NES sprite_collide).
        private static readonly int[] sprite_heights = new int[] {
            0x34,0x34,0x34,0x34,0x34,0x12,0x12,0xFF, // 00-07 (07:SPBH)
            0x28,0x28,0x03,0x12,0x03,0x03,0x03,0xFF, // 08-0F (0F:SPBH)
            0x0e,0x0e,0x0e,0x0e,0x24,0x24,0x24,0x34, // 10-17
            0x34,0x34,0xFF,0xFF,0xFF,0xFF,0xFF,0x12, // 18-1F (1A-1E:SPBH)
            0x24,0x24,0x34,0x34,0x34,0x03,0x03,0x12, // 20-27
            0x12,0x12,0xFE,0xFE,0xFE,0xFE,0xFE,0xFE, // 28-2F (2A-2F:DECO)
            0xFE,0xFE,0xFE,0xFE,0xFE,0xFE,0xFE,0xFE, // 30-37 (all DECO)
            0xFE,0xFE,0xFE,0xFE,0xFE,0xFE,0xFE,0xFE, // 38-3F (all DECO)
            0xFE,0xFE,0xFE,0xFE,0x12,0x12,0x12,0x28, // 40-47 (40-43:DECO)
            0x28,0xFE,0xFE,0x34,0x12,0x12,0x30,0xFF, // 48-4F (49-4A:DECO, 4F:SPBH)
            0x12,0x12,0x03,0x03,0x12,0x12,0x03,0x03, // 50-57
            0x34,0x10,0xFF,0x12,0x12,0x12,0x12,0x34, // 58-5F (5A:SPBH)
            0x34,0x34,0x34,0x34,0x34,0x02,0x10,0xFF, // 60-67 (0x65 = green pad 2px, 67:SPBH)
            0x10,0xFF,0x34,0x34,0x34,0x20,0x08,0xFF, // 68-6F (69,6F:SPBH)
            0xFF,0xFF,0xFF,0xFF,0xFF,0x10,0xFF,0x10, // 70-77 (70-74,76:SPBH)
            0xFF,0x12,0x12,0x12,0x12,0xFF,0xFF,0xFF, // 78-7F (78,7D-7F:SPBH)
            0xFD,0xFD,0xFD,0xFD,0xFD,0xFD,0xFD,0xFD, // 80-87 (all COLR)
            0xFD,0xFD,0xFD,0xFD,0xFD,0x00,0xFF,0xFD, // 88-8F (8E:SPBH)
            0xFD,0xFD,0xFD,0xFD,0xFD,0xFD,0xFD,0xFD, // 90-97 (all COLR)
            0xFD,0xFD,0xFD,0xFD,0xFD,0x00,0xFF,0xFD, // 98-9F (9E:SPBH)
            0xFD,0xFD,0xFD,0xFD,0xFD,0xFD,0xFD,0xFD, // A0-A7 (all COLR)
            0xFD,0xFD,0xFD,0xFD,0xFD,0x00,0xFD,0xFC, // A8-AF (AE:COLR, AF:OUTL)
            0xFC,0xFC,0xFC,0xFC,0xFC,0xFC,0xFC,0xFC, // B0-B7 (all OUTL)
            0xFC,0xFC,0xFC,0xFC,0xFC,0xFC,0xFC,0xFC, // B8-BF (all OUTL)
            0xFD,0xFD,0xFD,0xFD,0xFD,0xFD,0xFD,0xFD, // C0-C7 (all COLR)
            0xFD,0xFD,0xFD,0xFD,0xFD,0x00,0x00,0xFD, // C8-CF
            0xFD,0xFD,0xFD,0xFD,0xFD,0xFD,0xFD,0xFD, // D0-D7 (all COLR)
            0xFD,0xFD,0xFD,0xFD,0xFD,0xFF,0xFF,0xFF, // D8-DF (DD-DE:SPBH, DF:KNDO)
            0xFD,0xFD,0xFD,0xFD,0xFD,0xFD,0xFD,0xFD, // E0-E7 (all COLR)
            0xFD,0xFD,0xFD,0xFD,0xFD,0xFF,0xFF,0xFF, // E8-EF (ED:SPBH, EE-EF:KNDO)
            0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0x10,0x10, // F0-F7 (F0-F5:SPBH)
            0x10,0x10,0x1F,0x10,0x10,0x03,0x03,0x00  // F8-FF
        };

        private static readonly int[] sprite_widths = new int[] {
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 00-07
            0x0e,0x0e,0x0F,0x10,0x0F,0x0F,0x0F,0x10, // 08-0F
            0x28,0x28,0x28,0x28,0x10,0x10,0x10,0x10, // 10-17
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 18-1F
            0x10,0x10,0x10,0x10,0x10,0x0F,0x0F,0x10, // 20-27
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 28-2F
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 30-37
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 38-3F
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x0e, // 40-47
            0x0e,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 48-4F
            0x10,0x10,0x0F,0x0F,0x10,0x10,0x0F,0x0F, // 50-57
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 58-5F
            0x10,0x10,0x10,0x10,0x10,0x0E,0x30,0x30, // 60-67 (0x65 = green pad, 14px wide hitbox)
            0x30,0x30,0x10,0x10,0x10,0x10,0x08,0x10, // 68-6F
            0x10,0x10,0x10,0x10,0x10,0x30,0x30,0x30, // 70-77
            0x30,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 78-7F
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 80-87
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 88-8F
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 90-97
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // 98-9F
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // A0-A7
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // A8-AF
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // B0-B7
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // B8-BF
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // C0-C7
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // C8-CF
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // D0-D7
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // D8-DF
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // E0-E7
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // E8-EF
            0x10,0x10,0x10,0x10,0x10,0x10,0x10,0x10, // F0-F7
            0x10,0x08,0x1B,0x10,0x10,0x0E,0x0E,0x10  // F8-FF
        };

        private static readonly int[] sprite_x_offset = new int[] {
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 00-07
            0x01,0x01,0x00,0x00,0x00,0x00,0x00,0x00, // 08-0F
            0x04,0x04,0x04,0x04,0x00,0x00,0x00,0x00, // 10-17
            0x08,0x08,0x00,0x00,0x00,0x00,0x00,0x00, // 18-1F
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 20-27
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 28-2F
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 30-37
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 38-3F
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x01, // 40-47
            0x01,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 48-4F
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 50-57
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 58-5F
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 60-67
            0x00,0x00,0x00,0x00,0x00,0x00,0x04,0x00, // 68-6F
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 70-77
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 78-7F
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 80-87
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 88-8F
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 90-97
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 98-9F
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // A0-A7
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // A8-AF
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // B0-B7
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // B8-BF
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // C0-C7
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // C8-CF
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // D0-D7
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // D8-DF
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // E0-E7
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // E8-EF
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // F0-F7
            0x00,0x08,-0x07,0x00,0x00,0x00,0x00,0x00  // F8-FF
        };

        private static readonly int[] sprite_y_offset = new int[] {
            -0x02,-0x02,-0x02,-0x02,-0x02,-0x01,-0x01,0x00, // 00-07
            0x04,0x04,0x05,-0x01,0x00,0x05,0x00,0x00, // 08-0F
            0x01,0x01,0x01,0x01,-0x02,-0x02,-0x02,-0x02, // 10-17
            -0x02,-0x02,0x00,0x00,0x00,0x00,0x00,-0x01, // 18-1F
            -0x02,-0x02,-0x02,-0x02,-0x02,0x05,0x00,-0x01, // 20-27
            -0x01,-0x01,0x00,0x00,0x00,0x00,0x00,0x00, // 28-2F
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 30-37
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 38-3F
            0x00,0x00,0x00,0x00,-0x01,-0x01,-0x01,0x04, // 40-47
            0x04,0x00,0x00,-0x02,0x00,-0x01,-0x01,0x00, // 48-4F
            -0x01,-0x01,0x05,0x00,-0x01,-0x01,0x05,0x00, // 50-57
            -0x02,0x00,0x00,-0x01,-0x01,-0x01,-0x01,-0x02, // 58-5F
            -0x02,-0x02,-0x02,-0x02,-0x02,0x00,0x00,0x00, // 60-67
            0x00,0x00,-0x02,-0x02,-0x02,0x00,0x04,0x00, // 68-6F
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 70-77
            0x00,-0x01,-0x01,-0x01,-0x01,0x00,0x00,0x00, // 78-7F
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 80-87
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 88-8F
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 90-97
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // 98-9F
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // A0-A7
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // A8-AF
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // B0-B7
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // B8-BF
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // C0-C7
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // C8-CF
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // D0-D7
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // D8-DF
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // E0-E7
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // E8-EF
            0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00, // F0-F7
            0x00,0x00,-0x07,0x00,0x00,0x05,0x02,0x00  // F8-FF
        };
        // Pad / Orb velocity matrix (rows = pad/orb kind, cols = game mode)
        // Columns: 0=cube,1=ship,2=ball,3=ufo,4=robot,5=spider,6=wave,7=swing
        // Rows (by author matrix):
        // 0: yellow orb
        // 1: yellow pad
        // 2: pink orb
        // 3: pink pad
        // 4: red orb
        // 5: yellow orb bigger
        // 6: black orb
        // 7: yellow orb smaller
        // 8: red pad
        private static readonly int[][] PadOrbHeights = SharedPhysics.PadOrbHeights;
        private static readonly int[][] PadOrbHeights_Mini = SharedPhysics.PadOrbHeights_Mini;

        // Returns true when the sprite at storage index `idx` (with sprite id `sid`) overlaps
        // the player's axis-aligned hitbox in world pixel coordinates. This uses the
        // sprite geometry tables (`sprite_widths`, `sprite_heights`, `sprite_x_offset`, `sprite_y_offset`)
        // and any per-position `spritePixelOffsets`. If the sprite has an anchor, the anchor's
        // recorded offsets are used as a fallback so simulator rendering and collision match.
        private bool SpriteIntersectsPlayerTouching(int idx, int sid, int playerLeft_px, int playerRight_px, int playerTop_px, int playerBottom_px)
        {
            // For pads/orbs: trigger when edges TOUCH (adjacent pixels) instead of requiring overlap
            // This matches GD behavior where interactive objects activate on edge contact
            try
            {
                int storageTileX = idx % mapWidth;
                int storageTileY = idx / mapWidth;
                bool useRawNesRecord = IsSimulatorNesRawDispatchIndex(idx);

                // Geometry defaults to the instance SID. Portal classes must use
                // their own SID geometry (NES sprite_collide indexes tables by
                // activesprites_type[index], not an anchor owner's type).
                int sid8 = sid & 0xFF;
                int id_for_geom = sid8;
                int anchorKey = -1;
                bool allowAnchorGeom = !(SharedPhysics.IsSpeedPortal(sid8)
                                         || SharedPhysics.IsGameModePortal(sid8)
                                         || SharedPhysics.IsGravityPortal(sid8)
                                         || SharedPhysics.IsMiniGrowthPortal(sid8));
                if (!useRawNesRecord &&
                    spriteAnchors != null && spriteAnchors.TryGetValue(idx, out var anchor))
                {
                    anchorKey = anchor.anchorTileY * mapWidth + anchor.anchorTileX;
                    // Prefer the anchor's sprite id for geometry lookup when anchored
                    if (allowAnchorGeom && anchorKey >= 0 && anchorKey < sprites.Length)
                    {
                        int anchoredId = sprites[anchorKey];
                        if (anchoredId >= 0 && anchoredId < 256) id_for_geom = anchoredId & 0xFF;
                    }
                }

                // Raw NES records index the geometry tables with their exact SID.
                // Canonical portal geometry only applies to editor-grid sprites.
                if (!useRawNesRecord)
                    id_for_geom = SharedPhysics.NormalizePortalGeometrySid(id_for_geom);

                int hw = (id_for_geom >= 0 && id_for_geom < sprite_widths.Length) ? sprite_widths[id_for_geom] : TILE;
                int hh = (id_for_geom >= 0 && id_for_geom < sprite_heights.Length) ? sprite_heights[id_for_geom] : TILE;
                // NES sprite_collide() skips DECO/COLR/OUTL/SPBH sentinels (height >= 0xFC)
                if (hh >= 0xFC) return false;
                int hxoff = (id_for_geom >= 0 && id_for_geom < sprite_x_offset.Length) ? sprite_x_offset[id_for_geom] : 0;
                // Use the shared table so simulator and pathfinder consume the
                // exact NES runtime geometry.
                int hyoff = (id_for_geom >= 0 && id_for_geom < SharedPhysics.sprite_y_offset.Length) ? SharedPhysics.sprite_y_offset[id_for_geom] : 0;

                if (useRawNesRecord)
                {
                    int scrollX_px = SimulatorScrollX_px();
                    int playerLeft_screen_px = playerLeft_px - scrollX_px;
                    int playerTop_screen_px = playerTop_px - (cameraY_fixed >> 8);
                    int playerWidth = playerRight_px - playerLeft_px + 1;
                    int playerHeight = playerBottom_px - playerTop_px + 1;
                    int spriteLeft_screen_px =
                        SimulatorNesSaturatingOffset(SimulatorNesDispatchRealX(), hxoff);
                    int spriteTop_screen_px =
                        SimulatorNesSaturatingOffset(SimulatorNesDispatchRealY(), hyoff);
                    return SimulatorNesAxisOverlaps(
                               playerLeft_screen_px, playerWidth,
                               spriteLeft_screen_px, hw) &&
                           SimulatorNesAxisOverlaps(
                               playerTop_screen_px, playerHeight,
                               spriteTop_screen_px, hh);
                }

                // Per-position pixel offset (visual shift).
                int pxOff = 0; int pyOff = 0;
                if (!useRawNesRecord &&
                    anchorKey >= 0 && spritePixelOffsets != null && spritePixelOffsets.TryGetValue(anchorKey, out var aoffs2))
                {
                    pxOff = aoffs2.offsetX; pyOff = aoffs2.offsetY;
                }
                else if (!useRawNesRecord &&
                    spritePixelOffsets != null && spritePixelOffsets.TryGetValue(idx, out var offs2))
                {
                    pxOff = offs2.offsetX; pyOff = offs2.offsetY;
                }

                // Compute world-space sprite rectangle using NES-style exclusive bounds
                int groundRowsToReserve_local = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                int spriteLeft_world_px = (useRawNesRecord
                    ? simulatorNesSpriteWorldX[idx]
                    : storageTileX * TILE + pxOff) + hxoff;
                // NES check_spr_objects() applies -1 to sprite Y (clc;sbc intentionally subtracts 1 extra)
                int spriteTop_world_px = (useRawNesRecord
                    ? SimulatorNesDispatchWorldY()
                    : (storageTileY - groundRowsToReserve_local) * TILE + pyOff) + hyoff - 1;
                int spriteRight_world_px = spriteLeft_world_px + Math.Max(1, hw);   // exclusive (NES-style)
                int spriteBottom_world_px = spriteTop_world_px + Math.Max(1, hh);   // exclusive (NES-style)

                // If the hitbox table entry is the default TILE size but a larger sprite image
                // is available, prefer the image size for collision
                try
                {
                    if (!useRawNesRecord && hw == TILE && hh == TILE)
                    {
                        BitmapSource? bs = null;
                        int keyGeom = id_for_geom & 0xFF;
                        int keyInst = sid & 0xFF;
                        if (previewSpriteMap != null && previewSpriteMap.TryGetValue(keyGeom, out var pimgG) && pimgG is BitmapSource pbsG) bs = pbsG;
                        else if (previewSpriteMap != null && previewSpriteMap.TryGetValue(keyInst, out var pimgI) && pimgI is BitmapSource pbsI) bs = pbsI;
                        else if (spriteImages != null && keyGeom >= 0 && keyGeom < spriteImages.Length && spriteImages[keyGeom] is BitmapSource sbsG) bs = sbsG;
                        else if (spriteImages != null && keyInst >= 0 && keyInst < spriteImages.Length && spriteImages[keyInst] is BitmapSource sbsI) bs = sbsI;
                        if (bs != null)
                        {
                            hw = Math.Max(1, bs.PixelWidth);
                            hh = Math.Max(1, bs.PixelHeight);
                            spriteRight_world_px = spriteLeft_world_px + hw;   // exclusive (NES-style)
                            spriteBottom_world_px = spriteTop_world_px + hh;   // exclusive (NES-style)
                        }
                    }
                }
                catch { }

                // Check for "touching" - edges can be adjacent (1 pixel apart) without overlapping
                // Player bounds are inclusive (x + w - 1), sprite bounds are exclusive (x + w, NES-style).
                // NES overlap: !((x1+w1 < x2) || (x2+w2 < x1) || (y1+h1 < y2) || (y2+h2 < y1))
                // playerRight_excl = playerRight_px + 1 (convert inclusive to exclusive)
                // Touching adds 1px tolerance: use (pR+2) and (sR+1) / (sL-1) etc.
                bool touching = !((playerRight_px + 2) < spriteLeft_world_px || 
                                 (spriteRight_world_px + 1) < playerLeft_px || 
                                 (playerBottom_px + 2) < spriteTop_world_px || 
                                 (spriteBottom_world_px + 1) < playerTop_px);
                return touching;
            }
            catch { return false; }
        }

        private bool SpriteIntersectsPlayer(int idx, int sid, int playerLeft_px, int playerRight_px, int playerTop_px, int playerBottom_px, bool ignoreSentinels = false, bool requirePositiveXOverlap = false)
        {
            try
            {
                int storageTileX = idx % mapWidth;
                int storageTileY = idx / mapWidth;
                bool useRawNesRecord = IsSimulatorNesRawDispatchIndex(idx);

                // Geometry defaults to the instance SID. Portal classes must use
                // their own SID geometry (NES sprite_collide indexes tables by
                // activesprites_type[index], not an anchor owner's type).
                int sid8 = sid & 0xFF;
                int id_for_geom = sid8;
                int anchorKey = -1;
                bool allowAnchorGeom = !(SharedPhysics.IsSpeedPortal(sid8)
                                         || SharedPhysics.IsGameModePortal(sid8)
                                         || SharedPhysics.IsGravityPortal(sid8)
                                         || SharedPhysics.IsMiniGrowthPortal(sid8));
                if (!useRawNesRecord &&
                    spriteAnchors != null && spriteAnchors.TryGetValue(idx, out var anchor))
                {
                    anchorKey = anchor.anchorTileY * mapWidth + anchor.anchorTileX;
                    // Prefer the anchor's sprite id for geometry lookup when anchored
                    if (allowAnchorGeom && anchorKey >= 0 && anchorKey < sprites.Length)
                    {
                        int anchoredId = sprites[anchorKey];
                        if (anchoredId >= 0 && anchoredId < 256) id_for_geom = anchoredId & 0xFF;
                    }

                    // Do NOT change storageTileX/Y -- keep the sprite instance's own tile position
                    // as the base. The displayed sprite position uses the anchor + tileDelta offsets,
                    // and the hitbox offsets in the tables are applied relative to the sprite's
                    // displayed origin (so using the instance tile + anchor pixel offsets yields
                    // the correct world rect).
                }

                // Raw NES records index the geometry tables with their exact SID.
                // Canonical portal geometry only applies to editor-grid sprites.
                if (!useRawNesRecord)
                    id_for_geom = SharedPhysics.NormalizePortalGeometrySid(id_for_geom);

                int hw = (id_for_geom >= 0 && id_for_geom < sprite_widths.Length) ? sprite_widths[id_for_geom] : TILE;
                int hh = (id_for_geom >= 0 && id_for_geom < sprite_heights.Length) ? sprite_heights[id_for_geom] : TILE;
                // NES sprite_collide() skips DECO/COLR/OUTL/SPBH sentinels (height >= 0xFC)
                // But trigger/portal sprites (timewarp, trails, hide, freecam) are SPBH and
                // need collision detection — callers pass ignoreSentinels=true for those.
                if (!ignoreSentinels && hh >= 0xFC) return false;
                int hxoff = (id_for_geom >= 0 && id_for_geom < sprite_x_offset.Length) ? sprite_x_offset[id_for_geom] : 0;
                // Use the shared exact NES runtime geometry.
                int hyoff = (id_for_geom >= 0 && id_for_geom < SharedPhysics.sprite_y_offset.Length) ? SharedPhysics.sprite_y_offset[id_for_geom] : 0;

                // The NES collides against the cached 8-bit screen coordinates
                // produced by check_spr_objects, including offset saturation and
                // ADC carry behavior. Reconstructing a world rectangle loses
                // exact edge contacts when scroll_y has a fractional component.
                if (useRawNesRecord && hh < 0xFC)
                {
                    int scrollX_px = SimulatorScrollX_px();
                    int playerLeft_screen_px = playerLeft_px - scrollX_px;
                    int playerTop_screen_px = playerTop_px - (cameraY_fixed >> 8);
                    int playerWidth = playerRight_px - playerLeft_px + 1;
                    int playerHeight = playerBottom_px - playerTop_px + 1;
                    int spriteLeft_screen_px =
                        SimulatorNesSaturatingOffset(SimulatorNesDispatchRealX(), hxoff);
                    int spriteTop_screen_px =
                        SimulatorNesSaturatingOffset(SimulatorNesDispatchRealY(), hyoff);
                    bool xOverlap = requirePositiveXOverlap
                        ? SimulatorNesAxisOverlapsPositive(
                            playerLeft_screen_px, playerWidth,
                            spriteLeft_screen_px, hw)
                        : SimulatorNesAxisOverlaps(
                            playerLeft_screen_px, playerWidth,
                            spriteLeft_screen_px, hw);
                    return xOverlap &&
                           SimulatorNesAxisOverlaps(
                               playerTop_screen_px, playerHeight,
                               spriteTop_screen_px, hh);
                }

                // Per-position pixel offset (visual shift).
                // When anchored, prefer the anchor tile's pixel offset so collision and
                // overlay visuals match the anchored geometry base. Otherwise prefer
                // the sprite's own per-position offset.
                int pxOff = 0; int pyOff = 0;
                if (!useRawNesRecord &&
                    anchorKey >= 0 && spritePixelOffsets != null && spritePixelOffsets.TryGetValue(anchorKey, out var aoffs2))
                {
                    pxOff = aoffs2.offsetX; pyOff = aoffs2.offsetY;
                }
                else if (!useRawNesRecord &&
                    spritePixelOffsets != null && spritePixelOffsets.TryGetValue(idx, out var offs2))
                {
                    pxOff = offs2.offsetX; pyOff = offs2.offsetY;
                }

                // Compute world-space sprite rectangle using NES-style EXCLUSIVE right/bottom bounds.
                // NES check_collision() tests: (x + width >= other_x), where (x + width) is the
                // exclusive bound (one pixel past the last occupied pixel). Using inclusive
                // bounds (x + w - 1) makes the collision window 1px too narrow on each side,
                // totalling 2px narrower horizontally and 2px shorter vertically vs Famidash.
                // Rendering subtracts `groundRowsToReserve` from the displayed anchor Y to
                // reserve bottom ground rows. Adjust collision to match displayed origin
                // by applying the same vertical shift when computing world sprite rect.
                int groundRowsToReserve_local = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                int spriteLeft_world_px = (useRawNesRecord
                    ? simulatorNesSpriteWorldX[idx]
                    : storageTileX * TILE + pxOff) + hxoff;
                // NES check_spr_objects() applies -1 to sprite Y (clc;sbc intentionally subtracts 1 extra)
                int spriteTop_world_px = (useRawNesRecord
                    ? SimulatorNesDispatchWorldY()
                    : (storageTileY - groundRowsToReserve_local) * TILE + pyOff) + hyoff - 1;
                int spriteRight_world_px = spriteLeft_world_px + Math.Max(1, hw);   // exclusive bound (NES: x + width)
                int spriteBottom_world_px = spriteTop_world_px + Math.Max(1, hh);   // exclusive bound (NES: y + height)

                // Keep gameplay collision on NES geometry tables only. Expanding to bitmap
                // dimensions (for example 24x48 portal art) makes SIM portal collisions fire
                // earlier/later than PF and Famidash's table-driven check_collision logic.

                // CRITICAL: Hitbox cache disabled for determinism (rendering is async and non-deterministic)
                // The cache causes collision detection to vary between runs based on render timing
                /*
                try
                {
                    if (hitboxWorldCache != null && hitboxWorldCache.TryGetValue(idx, out var cached) && cached.frame == renderFrameCounter)
    QA                    {
                        int cLeft = cached.left; int cTop = cached.top; int cRight = cached.right; int cBottom = cached.bottom;
                        bool cov = !(playerRight_px < cLeft || playerLeft_px > cRight || playerBottom_px < cTop || playerTop_px > cBottom);
                        return cov;
                    }
                }
                catch { }
                */

                // NES check_collision() uses exclusive bounds: collision when (x1+w1 >= x2) && (x2+w2 >= x1).
                // Player bounds arrive as inclusive (x + w - 1), so playerRight_excl = playerRight_px + 1.
                // Sprite bounds are now exclusive (x + w).
                // NES no-collision: (x1+w1 < x2) || (x2+w2 < x1) || (y1+h1 < y2) || (y2+h2 < y1)
                // With our variables: ((pR+1) < sL) || (sR < pL) || ((pB+1) < sT) || (sB < pT)
                // Note: sR < pL is equivalent to pL > sR, using strict > because NES uses bcc (< unsigned)
                bool xOverlapWorld = requirePositiveXOverlap
                    ? !((playerRight_px + 1) <= spriteLeft_world_px || spriteRight_world_px <= playerLeft_px)
                    : !((playerRight_px + 1) < spriteLeft_world_px || spriteRight_world_px < playerLeft_px);
                bool yOverlapWorld = !((playerBottom_px + 1) < spriteTop_world_px || spriteBottom_world_px < playerTop_px);
                return xOverlapWorld && yOverlapWorld;
            }
            catch { return false; }
        }

        // Pool for hitbox rectangles
        private System.Collections.Generic.List<System.Windows.Shapes.Rectangle> hitboxPool = new System.Collections.Generic.List<System.Windows.Shapes.Rectangle>();
        private int hitboxesInUse = 0;
        // Pool of rectangle overlays used to draw per-tile hitboxes above the tile layer.
        private System.Collections.Generic.List<System.Windows.Shapes.Rectangle> tileHitboxPool = new System.Collections.Generic.List<System.Windows.Shapes.Rectangle>();
        private int tileHitboxesInUse = 0;
        // Cache of world-space hitbox rectangles populated during rendering so collision
        // can use the exact same geometry as the overlay (key = sprite storage idx).
        private System.Collections.Generic.Dictionary<int, (int left, int top, int right, int bottom, int frame)> hitboxWorldCache = new System.Collections.Generic.Dictionary<int, (int, int, int, int, int)>();
        private int renderFrameCounter = 0;
        
        // =====================================================================
        // CUBE ANIMATION SYSTEM - From nesdash.s drawcube_* tables
        // =====================================================================
        
        // Cube rotation state (16-bit: low byte = sub-frame accumulator, high byte = frame 0-23)
        private int cubeRotate_fixed = 0;  // 16-bit fixed point for sub-frame position
        private int shipRotate_fixed = 0;  // 16-bit fixed point for ship velocity-based animation
        private int swingcopterRotate_fixed = 0;  // 16-bit fixed point for swingcopter velocity-based animation
        private int footballRotate_fixed = 0;  // 16-bit fixed point for football cube-style rotation with flip table

        // Per-player rotation state for dual mode (index 0 = P1, 1 = P2)
        private int[] player_cubeRotate = new int[2];
        private int[] player_cubeRotateMini = new int[2];
        private int[] player_shipRotate = new int[2];
        private int[] player_swingRotate = new int[2];
        private int[] player_footballRotate = new int[2];
        
        // Rounding table for snapping cube to nearest 90° when velocity = 0
        // Maps rotation values to rounding adjustments
        private static readonly int[] DrawcubeRoundingTable = new int[]
        {
            0, -1, -2, 3, 2, 1,  // First half of table
            0, -1, -2, 3, 2, 1,  // Doubled to simplify routine
            -24                    // Extra entry for edge case
        };
        
        // Sprite frame table: maps cube_rotate index (0-23) to frame index (0-6) with flip flags
        // Bits 6-7 = flip flags (00=no, 01=H, 10=V, 11=HV)
        // Bits 0-2 = frame index (0-6)
        private static readonly int[] DrawcubeSpriteTable = new int[]
        {
            // Values 0-6: Frames 0-6 (NOFLIP = 0x00)
            0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06,
            // Values 7-11: Frames 5-1 (V_FLIP = 0x80)
            0x85, 0x84, 0x83, 0x82, 0x81,
            // Values 12-18: Frames 0-6 (HVFLIP = 0xC0)
            0xC0, 0xC1, 0xC2, 0xC3, 0xC4, 0xC5, 0xC6,
            // Values 19-23: Frames 5-1 (H_FLIP = 0x40)
            0x45, 0x44, 0x43, 0x42, 0x41
        };
        
        // Mini cube rotation state (same structure as full cube)
        private int cubeRotateMini_fixed = 0;
        
        // Mini cube frame lookup table: maps 24-frame rotation indices to 5 PNG frames
        // Frame mapping as specified: 0->0, 1->1, 2->1, 3->2, 4->2, 5->3, 6->3, 7->4, etc.
        private static readonly int[] DrawcubeMiniSpriteTable = new int[]
        {
            // Indices 0-6: rotations 0-6 (0->0, 1->1, 2->1, 3->2, 4->2, 5->3, 6->3)
            0, 1, 1, 2, 2, 3, 3,
            // Indices 7-11: rotations 7-11 (7->4, 8->4, 9->0, 10->1, 11->1)
            4, 4, 0, 1, 1,
            // Indices 12-18: rotations 12-18 (12->2, 13->2, 14->3, 15->3, 16->4, 17->4, 18->0)
            2, 2, 3, 3, 4, 4, 0,
            // Indices 19-23: rotations 19-23 (19->1, 20->1, 21->2, 22->2, 23->3)
            1, 1, 2, 2, 3
        };
        
        // Gravity constants from physics_table_defines.cmp.h
        // (actual values now come from GameModePhysics.CUBE_GRAVITY() function)
        
        // Experimental: record player world positions each rendered frame for editor overlay
        private System.Collections.Generic.List<(int x, int y)> recordedPlayerPath = new System.Collections.Generic.List<(int x, int y)>();
        private System.Collections.Generic.List<(int x, int y)> recordedPlayer2Path = new System.Collections.Generic.List<(int x, int y)>();  // Player 2 path for dual mode
        private bool _prevDualActiveForP2Path; // tracks dual-mode transitions for P2 path sentinel breaks
        private void RecordP2PathPoint(int x, int y)
        {
            if (!_prevDualActiveForP2Path && recordedPlayer2Path.Count > 0)
                recordedPlayer2Path.Add((-1, -1));
            recordedPlayer2Path.Add((x, y));
            _prevDualActiveForP2Path = true;
        }
        // Interaction line: player's center (fixed-point) where scrolling begins
        private const int INTERACTION_LINE_FIXED = 0x5000;

        // Player world X (fixed-point, 8 fractional bits)
        // Start the player on the leftmost tile (x = 0)
        private int playerX_fixed = 0;
        private int playerY_fixed = 0; // fixed-point (8 frac bits) world Y for player
        
        // Dual-mode player arrays (for two-player simultaneous)
        private int[] player_x_fixed = new int[2] { 0, 0 };  // Both players' X positions
        private int[] player_y_fixed = new int[2] { 0, 0 };  // Both players' Y positions
        private int[] player_vel_x_fixed = new int[2] { 0, 0 };  // Dormant P2 X velocity persists across single sections
        private int[] player_vel_y_fixed = new int[2] { 0, 0 };  // Both players' Y velocities
        private bool[] player_mini = new bool[2] { false, false };  // Mini mode for each player
        private byte[] player_gravity = new byte[2] { 0, 0 };  // Gravity state (0=down, 0xFF=up)
        
        // Per-player physics flags for dual mode (prevents P1↔P2 cross-contamination)
        private bool[] player_wasZeroed = new bool[2] { true, false };
        private bool[] player_onGround = new bool[2] { true, false };
        private int[] player_groundStabilize = new int[2] { 0, 0 };
        
        // Dual/single mode flags
        private bool dual = false;  // Is dual-mode active
        private bool singlePortalExitPending = false;  // Deferred single portal exit (sync after P2 physics)
        private bool applyPlayer2Colors = false;  // Should we apply player 2 colors to next icon load
        private System.Collections.Generic.Dictionary<string, System.Windows.Media.Imaging.BitmapSource> playerColorCache = new();  // Cache for all icon colors
        private System.Collections.Generic.Dictionary<string, System.Windows.Media.Imaging.BitmapSource> player2ColorCache = new();  // Separate cache for player 2 recolored icons
        private bool twoplayer = false;  // Are we in two-player mode already
        
        // Player visual size in pixels (set during initialization)
        private int playerVisualWidth = TILE;
        private int playerVisualHeight = TILE;
        // Prevent multiple simultaneous requests to start playback (clicks/keys)
        private bool playbackStartPending = false;
        private volatile bool restartInProgress = false;
        private volatile bool windowClosed = false; // Prevent simulation during restart

        // Visual player controls used for the player: image preferred, rectangle fallback
        private System.Windows.Controls.Image? playerImage = null;
        private System.Windows.Shapes.Rectangle? playerRect = null;
        private System.Windows.Controls.Image? player2Image = null;  // Player 2 visual for dual mode
        private System.Windows.Shapes.Rectangle? player2Rect = null;  // Player 2 rectangle fallback
        
        // Trail ghost images (3 ghost copies rendered behind the player)
        private System.Windows.Controls.Image?[] trailGhosts = new System.Windows.Controls.Image?[3];
        
        // When player crosses interaction line, remember the screen pixel offset where the crossing occurred
        // so the camera can follow the player while keeping them at that screen X.
        private int interactionScreenOffset_px = -1;

        private readonly int[] tiles;
        private readonly int[] sprites;
        private readonly int[] nonEmptySpriteIndices; // Pre-filtered: only indices where sprites[idx] >= 0
        private readonly int mapWidth;
        private readonly int mapHeight;
        private readonly ImageSource?[]? tileImages;
        private ImageSource?[]? tileTonedImages;
        private readonly ImageSource?[]? spriteImages;
        private readonly System.Collections.Generic.Dictionary<int, (int offsetX, int offsetY)> spritePixelOffsets;
        private readonly System.Collections.Generic.Dictionary<int, (int anchorTileX, int anchorTileY)> spriteAnchors;
        private Color backgroundTint;
        private Color groundTint;
        private bool backgroundForceSolidBlack = false; // when true, force background to solid black (0x8F)
        private readonly Color playerPlaceholderGreen = Color.FromArgb(255, 255, 50, 43); // #FF322B
        private Color tileTint;
        private readonly Color playerTint;
        private readonly bool playerTintEnabled;
        private readonly bool forcePreviewMode;
        private readonly bool hideColorTriggers;
        private readonly int gridRenderShiftYPx;
        private readonly System.Collections.Generic.Dictionary<int, ImageSource?>? previewSpriteMap;
        // Simulator-only cached upside-down chain image
        private ImageSource? chainUpsideSimImage = null;
        private readonly System.Collections.Generic.Dictionary<int, ImageSource?[]>? animationFrames;
        // Tile-level animated saw frames (tinted versions) passed from MainWindow
        private ImageSource?[]? sawFrame1TilesTinted;
        private ImageSource?[]? sawFrame2TilesTinted;
        private ImageSource?[]? smallSawFrame1TilesTinted;
        private ImageSource?[]? smallSawFrame2TilesTinted;
        private ImageSource?[]? largeSawFrame1TilesTinted;
        private ImageSource?[]? largeSawFrame2TilesTinted;
        // Keep originals so we can re-generate tinted versions when tile tint changes
        private ImageSource?[]? sawFrame1TilesOrig;
        private ImageSource?[]? sawFrame2TilesOrig;
        private ImageSource?[]? smallSawFrame1TilesOrig;
        private ImageSource?[]? smallSawFrame2TilesOrig;
        private ImageSource?[]? largeSawFrame1TilesOrig;
        private ImageSource?[]? largeSawFrame2TilesOrig;

        private const int NES_W = 16; // horizontal tiles (was 15)
        private const int NES_H = 15; // vertical tiles (was 16)
        private const int TILE = 16;

        // NES spawn/scroll Y config (set from MainWindow before use)
        private int? configSpawnYHi = null;
        private int? configSpawnYLo = null;
        private int? configScrollYHi = null;
        private int? configScrollYLo = null;

        /// <summary>
        /// Apply NES spawn Y / scroll Y config values.
        /// Call after construction and before ApplyStartPosMarker.
        /// These values are used when no START POS marker is set.
        /// </summary>
        public void SetSpawnScrollConfig(int? spawnHi, int? spawnLo, int? scrollHi, int? scrollLo)
        {
            configSpawnYHi = spawnHi;
            // Current compact NES headers no longer store either byte. Their
            // runtime values are fixed to $00 and $02 respectively.
            configSpawnYLo = null;
            configScrollYHi = null;
            configScrollYLo = scrollLo;
        }

        /// <summary>
        /// Convert NES spawn Y hi/lo bytes to TMX pixel Y (fixed-point).
        /// Returns null if no config is set.
        /// </summary>
        private int? ComputeSpawnYFixed()
        {
            if (!configSpawnYHi.HasValue) return null;
            int hi = configSpawnYHi.Value & 0xFF;
            int lo = 0;
            int nesSpawnY = (hi << 8) | lo; // NES 16-bit fixed-point (8 frac bits)
            // Match NES exactly: PF_top_px = nesSpawnHi + nesScrollLinear - nesYOffset.
            // Use NES default scroll (0x02EF -> linear 719) when not provided, matching
            // export_levels.py defaults.
            (int nesScrollHi, int nesScrollLo) = SharedPhysics.ResolveNesInitialScroll(configScrollYHi, configScrollYLo);
            int nesScrollLinear = nesScrollHi * 240 + nesScrollLo;
            int gRTR = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
            int nesYOffset = (57 - mapHeight + gRTR) * 16;
            return nesSpawnY + ((nesScrollLinear - nesYOffset) << 8);
        }

        /// <summary>
        /// Convert NES scroll Y hi/lo bytes to TMX camera Y (fixed-point).
        /// Returns null if no config is set.
        /// </summary>
        private int? ComputeScrollYFixed()
        {
            if (!configSpawnYHi.HasValue && !configSpawnYLo.HasValue &&
                !configScrollYHi.HasValue && !configScrollYLo.HasValue)
                return null;
            (int hi, int lo) = SharedPhysics.ResolveNesInitialScroll(configScrollYHi, configScrollYLo);
            // Linearize NES nametable scroll (each 0x100 block = 240 valid pixels, F0-FF skipped)
            int linearScroll = hi * 240 + lo;
            int linearMax = 2 * 240 + 239; // 719 = linearize(0x02EF), the default bottom scroll
            int pixelsFromBottom = linearMax - linearScroll;
            int maxCameraY = SimulatorNesMaxCameraY_px();
            int tmxCamY = Math.Max(0, maxCameraY - pixelsFromBottom);
            return Math.Min(maxCameraY, tmxCamY) << 8;
        }

        private int SimulatorNesMaxCameraY_px()
        {
            int mapMax = Math.Max(0, (mapHeight - NES_H) * TILE);
            int nesMax = 719 - _sim_nesCoordOffset;
            return Math.Max(0, Math.Min(mapMax, nesMax));
        }

        // Centralized helper for determining whether a collision category provides
        // a floor at a given local tile column (0..15). Returns true and sets
        // `topOffsetPx` to the Y offset (0..15) of the floor within the tile
        // when a floor exists; otherwise returns false.
        private static bool ProvidesFloorAtColumnStatic(MetatileCollision col, int localX, out int topOffsetPx)
        {
            topOffsetPx = int.MaxValue;
            bool inLeft = (localX >= 0 && localX <= 7);
            bool inRight = (localX >= 8 && localX <= 15);

            switch (col)
            {
                case MetatileCollision.COL_ALL:
                case MetatileCollision.COL_FLOOR_CEIL:
                    topOffsetPx = 0; return true;
                case MetatileCollision.COL_TOP:
                case MetatileCollision.COL_TOP_LEFT_STAIRS:
                case MetatileCollision.COL_TOP_RIGHT_STAIRS:
                case MetatileCollision.COL_TOP_CENTER_SPIKE:
                    topOffsetPx = 0; return true;
                case MetatileCollision.COL_BOTTOM:
                case MetatileCollision.COL_BOTTOM_CENTER_SPIKE:
                case MetatileCollision.COL_BOTTOM_SPIKES:
                    topOffsetPx = 8; return true;
                case MetatileCollision.COL_LEFT:
                case MetatileCollision.COL_BOTTOM_LEFT_STAIRS:
                    if (inLeft) { topOffsetPx = 0; return true; }
                    break;
                case MetatileCollision.COL_BOTTOM_LEFT_SPIKE:
                    if (inLeft) { topOffsetPx = 8; return true; }
                    break;
                case MetatileCollision.COL_RIGHT:
                case MetatileCollision.COL_BOTTOM_RIGHT_STAIRS:
                    if (inRight) { topOffsetPx = 0; return true; }
                    break;
                case MetatileCollision.COL_BOTTOM_RIGHT_SPIKE:
                    if (inRight) { topOffsetPx = 8; return true; }
                    break;
                case MetatileCollision.COL_UP_LEFT:
                    if (inLeft) { topOffsetPx = 0; return true; }
                    break;
                case MetatileCollision.COL_UP_RIGHT:
                    if (inRight) { topOffsetPx = 0; return true; }
                    break;
                case MetatileCollision.COL_DOWN_LEFT:
                case MetatileCollision.COL_LEFT_SPIKE_BLOCK:
                    if (inLeft) { topOffsetPx = 8; return true; }
                    break;
                case MetatileCollision.COL_DOWN_RIGHT:
                case MetatileCollision.COL_RIGHT_SPIKE_BLOCK:
                    if (inRight) { topOffsetPx = 8; return true; }
                    break;
                case MetatileCollision.COL_TOP_LEFT_BOTTOM_RIGHT:
                    if (inLeft) { topOffsetPx = 0; return true; }
                    if (inRight) { topOffsetPx = 8; return true; }
                    break;
                case MetatileCollision.COL_TOP_RIGHT_BOTTOM_LEFT:
                    if (inRight) { topOffsetPx = 0; return true; }
                    if (inLeft) { topOffsetPx = 8; return true; }
                    break;
                default:
                    break;
            }

            // Additional combined-stair behavior for bottom-left / bottom-right stairs
            if (col == MetatileCollision.COL_BOTTOM_LEFT_STAIRS)
            {
                if (inLeft) { topOffsetPx = 0; return true; }
                if (inRight) { topOffsetPx = 8; return true; }
            }
            if (col == MetatileCollision.COL_BOTTOM_RIGHT_STAIRS)
            {
                if (inRight) { topOffsetPx = 0; return true; }
                if (inLeft) { topOffsetPx = 8; return true; }
            }

            return false;
        }

        // Conservative ceiling blocking test: returns true when this metatile should block
        // upward movement for the given local X within the tile. We treat `COL_TOP` as
        // pass-through from below (top slabs), but otherwise consider non-none tiles
        // as blocking to prevent the player from moving through ceilings.
        private static bool BlocksCeilingAtColumn(MetatileCollision col, int localX)
        {
            if (col == MetatileCollision.COL_NONE) return false;
            if (col == MetatileCollision.COL_TOP) return false; // top slabs are pass-through from below
            if (SharedPhysics.IsDeathCollision(col)) return false;
            return true;
        }

        // Returns true when the tile's collision occupies the pixel at (localX, localY)
        // within the 16x16 metatile. `localX` and `localY` are 0..15 (local coords).
        // This helper understands death-categorized collisions (maps them to shape
        // components) so death-only tiles respect half-slab shapes instead of acting
        // as full 16x16 solids.
        private static bool TileOccupiesPixel(MetatileCollision col, int localX, int localY)
        {
            // Pure death tiles with no solid collision
            switch (col)
            {
                case MetatileCollision.COL_DEATH:
                case MetatileCollision.COL_DEATH_BOTTOM:
                case MetatileCollision.COL_DEATH_TOP:
                case MetatileCollision.COL_DEATH_LEFT:
                case MetatileCollision.COL_DEATH_RIGHT:
                case MetatileCollision.COL_DEATH_TOP_RIGHT:
                case MetatileCollision.COL_DEATH_TOP_LEFT:
                case MetatileCollision.COL_DEATH_BOTTOM_RIGHT:
                case MetatileCollision.COL_DEATH_BOTTOM_LEFT:
                case MetatileCollision.COL_DEATH_TOP_RIGHT_LEFT:
                case MetatileCollision.COL_DEATH_TOP_BOTTOM:
                case MetatileCollision.COL_DEATH_LEFT_RIGHT:
                case MetatileCollision.COL_DEATH_TOP_LEFT_BOTTOM:
                // Pure spike tiles (death only, no solid)
                case MetatileCollision.COL_UP_LEFT_SPIKE:
                case MetatileCollision.COL_UP_RIGHT_SPIKE:
                case MetatileCollision.COL_UP_BOTH_SPIKES:
                case MetatileCollision.COL_DOWN_LEFT_SPIKE:
                case MetatileCollision.COL_DOWN_RIGHT_SPIKE:
                case MetatileCollision.COL_DOWN_BOTH_SPIKES:
                    return false;
                default:
                    break;
            }

            // Handle top-oriented collision categories (top-half slabs and combos).
            // Top slabs occupy the top 8 pixels (localY 0..7). For combined tiles
            // that have top on one half and bottom on the other, choose behavior by
            // column.
            bool inLeft = (localX >= 0 && localX <= 7);
            bool inRight = (localX >= 8 && localX <= 15);

            switch (col)
            {
                case MetatileCollision.COL_TOP:
                case MetatileCollision.COL_TOP_CENTER_SPIKE:
                    return (localY <= 7);
                case MetatileCollision.COL_TOP_LEFT_STAIRS:
                    // Top 8 pixels + left 8 pixels (L-shape: top + left, bottom-right quadrant empty)
                    return (localY <= 7) || (localX <= 7);
                case MetatileCollision.COL_TOP_RIGHT_STAIRS:
                    // Top 8 pixels + right 8 pixels (L-shape: top + right, bottom-left quadrant empty)
                    return (localY <= 7) || (localX >= 8);
                case MetatileCollision.COL_BOTTOM_LEFT_STAIRS:
                    // Left 8 pixels (full height) + bottom-right quadrant (L-shape: left + bottom-right)
                    return (localX <= 7) || (localX >= 8 && localY >= 8);
                case MetatileCollision.COL_BOTTOM_RIGHT_STAIRS:
                    // Right 8 pixels (full height) + bottom-left quadrant (L-shape: right + bottom-left)
                    return (localX >= 8) || (localX <= 7 && localY >= 8);
                case MetatileCollision.COL_BOTTOM_LEFT_SPIKE:
                    // Bottom half full width for collision
                    return (localY >= 8);
                case MetatileCollision.COL_BOTTOM_RIGHT_SPIKE:
                    // Bottom half full width for collision
                    return (localY >= 8);
                case MetatileCollision.COL_LEFT_SPIKE_BLOCK:
                    // Bottom-left quadrant only
                    return (localX < 8 && localY >= 8);
                case MetatileCollision.COL_RIGHT_SPIKE_BLOCK:
                    // Bottom-right quadrant only
                    return (localX >= 8 && localY >= 8);
                case MetatileCollision.COL_UP_LEFT:
                    return (inLeft && localY <= 7);
                case MetatileCollision.COL_UP_RIGHT:
                    return (inRight && localY <= 7);
                case MetatileCollision.COL_RIGHT:
                    return inRight;  // right-half column solid, left half passable
                case MetatileCollision.COL_LEFT:
                    return inLeft;   // left-half column solid, right half passable
                case MetatileCollision.COL_DOWN_LEFT:
                    return (inLeft && localY >= 8);   // bottom-left quadrant
                case MetatileCollision.COL_DOWN_RIGHT:
                    return (inRight && localY >= 8);  // bottom-right quadrant
                case MetatileCollision.COL_TOP_LEFT_BOTTOM_RIGHT:
                    if (inLeft) return (localY <= 7);
                    if (inRight) return (localY >= 8);
                    break;
                case MetatileCollision.COL_TOP_RIGHT_BOTTOM_LEFT:
                    if (inRight) return (localY <= 7);
                    if (inLeft) return (localY >= 8);
                    break;
                default:
                    break;
            }

            // For other normal collision categories, attempt to derive a vertical
            // floor offset using the existing `ProvidesFloorAtColumnStatic` helper.
            // If that helper reports a floor offset for this column, treat any
            // localY >= topOffsetPx as occupied by solid (this captures bottom-half
            // slabs and floor-like shapes).
            if (ProvidesFloorAtColumnStatic(col, localX, out int topOffsetPx))
            {
                if (localY >= topOffsetPx) return true;
                return false;
            }

            // Slope tiles are NOT solid blocks - they are handled entirely by the
            // dedicated bg_coll_D_slopes() / bg_coll_U_slopes() slope collision system.
            // Returning true here would cause the forward collision check (CheckPixelCollision)
            // to treat slopes as walls and kill the player.
            if (IsSlopeTile(col))
            {
                return false;
            }

            // Pure death tiles have no solid collision - player passes through
            if (col == MetatileCollision.COL_DEATH)
                return false;

            // Fallback: for ceiling-like categories that don't provide floor offsets
            // treat non-none as fully blocking at this stage.
            if (col != MetatileCollision.COL_NONE) return true;
            return false;
        }
        
        /// <summary>
        /// Check if collision type is a slope
        /// </summary>
        private static bool IsSlopeTile(MetatileCollision col) => SharedPhysics.IsSlopeTile(col);
        
        /// <summary>
        /// Get the floor Y position for a slope tile at a given local X coordinate
        /// Returns the Y pixel (0-15) where the slope's floor is at this X
        /// </summary>
        private static int GetSlopeFloorAtX(MetatileCollision col, int localX)
        {
            // Clamp localX to 0-15
            localX = Math.Max(0, Math.Min(15, localX));
            
            switch (col)
            {
                // 45-degree slopes (1:1 ratio, 16px rise over 16px run)
                case MetatileCollision.COL_SLOPE_RD45: // Right-down 45° (rising left-to-right, falling)
                    return localX; // Y increases with X
                case MetatileCollision.COL_SLOPE_LD45: // Left-down 45° (rising right-to-left, falling)
                    return 15 - localX; // Y increases as X decreases
                case MetatileCollision.COL_SLOPE_RU45: // Right-up 45° (ceiling, rising left-to-right)
                    return 15 - localX; // Y decreases with X (ceiling)
                case MetatileCollision.COL_SLOPE_LU45: // Left-up 45° (ceiling, rising right-to-left)
                    return localX; // Y decreases as X decreases (ceiling)
                
                // 22.5-degree slopes (1:2 ratio, 8px rise over 16px run)
                case MetatileCollision.COL_SLOPE_RD22_RIGHT: // Right half, gentle slope
                    return localX / 2; // 0-7 Y for 0-15 X
                case MetatileCollision.COL_SLOPE_RD22_LEFT: // Left half, gentle slope
                    return 8 + (localX / 2); // 8-15 Y for 0-15 X
                case MetatileCollision.COL_SLOPE_LD22_RIGHT: // Left-down gentle, right half
                    return (15 - localX) / 2;
                case MetatileCollision.COL_SLOPE_LD22_LEFT: // Left-down gentle, left half
                    return 8 + ((15 - localX) / 2);
                case MetatileCollision.COL_SLOPE_RU22_RIGHT: // Right-up gentle (ceiling)
                    return 15 - (localX / 2);
                case MetatileCollision.COL_SLOPE_RU22_LEFT: // Right-up gentle (ceiling)
                    return 7 - (localX / 2);
                case MetatileCollision.COL_SLOPE_LU22_RIGHT: // Left-up gentle (ceiling)
                    return 15 - ((15 - localX) / 2);
                case MetatileCollision.COL_SLOPE_LU22_LEFT: // Left-up gentle (ceiling)
                    return 7 - ((15 - localX) / 2);
                
                // 63.4-degree slopes (2:1 ratio, 16px rise over 8px run)
                case MetatileCollision.COL_SLOPE_RD66_TOP: // Top half, steep slope
                    return Math.Min(15, localX * 2); // 0-15 Y for 0-7 X
                case MetatileCollision.COL_SLOPE_RD66_BOT: // Bottom half, steep slope  
                    return Math.Max(0, (localX * 2) - 16); // Continue from top half
                case MetatileCollision.COL_SLOPE_LD66_TOP: // Left-down steep, top
                    return Math.Min(15, (15 - localX) * 2);
                case MetatileCollision.COL_SLOPE_LD66_BOT: // Left-down steep, bottom
                    return Math.Max(0, ((15 - localX) * 2) - 16);
                case MetatileCollision.COL_SLOPE_RU66_TOP: // Right-up steep (ceiling)
                    return Math.Max(0, 15 - (localX * 2));
                case MetatileCollision.COL_SLOPE_RU66_BOT: // Right-up steep (ceiling)
                    return Math.Min(15, 31 - (localX * 2));
                case MetatileCollision.COL_SLOPE_LU66_TOP: // Left-up steep (ceiling)
                    return Math.Max(0, 15 - ((15 - localX) * 2));
                case MetatileCollision.COL_SLOPE_LU66_BOT: // Left-up steep (ceiling)
                    return Math.Min(15, 31 - ((15 - localX) * 2));
                
                default:
                    return 0; // Fallback
            }
        }

        /// <summary>
        /// Returns true when the given local pixel (0..15, 0..15) is inside the solid
        /// region of a slope tile.  Uses the same tmp7/tmp4 formula as the NES
        /// bg_coll_slope() routine so the visualisation exactly matches collision.
        /// </summary>
        private static bool IsSlopeSolidAtPixel(MetatileCollision col, int localX, int localY)
        {
            int tmp7, tmp4;
            switch (col)
            {
                // 45-degree
                case MetatileCollision.COL_SLOPE_RD45:
                    tmp7 = (localX & 0x0f) ^ 0x0f; tmp4 = localY & 0x0f; break;
                case MetatileCollision.COL_SLOPE_LD45:
                    tmp7 = localX & 0x0f; tmp4 = localY & 0x0f; break;
                case MetatileCollision.COL_SLOPE_RU45:
                    tmp7 = (localX & 0x0f) ^ 0x0f; tmp4 = (localY & 0x0f) ^ 0x0f; break;
                case MetatileCollision.COL_SLOPE_LU45:
                    tmp7 = localX & 0x0f; tmp4 = (localY & 0x0f) ^ 0x0f; break;

                // 22-degree
                case MetatileCollision.COL_SLOPE_RD22_RIGHT:
                    tmp7 = ((localX >> 1) & 0x07) ^ 0x0f; tmp4 = localY & 0x0f; break;
                case MetatileCollision.COL_SLOPE_RD22_LEFT:
                    tmp7 = (((localX >> 1) | 0x8) & 0x0f) ^ 0x0f; tmp4 = localY & 0x0f; break;
                case MetatileCollision.COL_SLOPE_LD22_RIGHT:
                    tmp7 = (localX >> 1) & 0x07; tmp4 = localY & 0x0f; break;
                case MetatileCollision.COL_SLOPE_LD22_LEFT:
                    tmp7 = ((localX >> 1) | 0x8) & 0x0f; tmp4 = localY & 0x0f; break;
                case MetatileCollision.COL_SLOPE_RU22_RIGHT:
                    tmp7 = ((localX >> 1) & 0x07) ^ 0x0f; tmp4 = (localY & 0x0f) ^ 0x0f; break;
                case MetatileCollision.COL_SLOPE_RU22_LEFT:
                    tmp7 = (((localX >> 1) | 0x8) & 0x0f) ^ 0x0f; tmp4 = (localY & 0x0f) ^ 0x0f; break;
                case MetatileCollision.COL_SLOPE_LU22_RIGHT:
                    tmp7 = (localX >> 1) & 0x07; tmp4 = (localY & 0x0f) ^ 0x0f; break;
                case MetatileCollision.COL_SLOPE_LU22_LEFT:
                    tmp7 = ((localX >> 1) | 0x8) & 0x0f; tmp4 = (localY & 0x0f) ^ 0x0f; break;

                // 66-degree (steep) — some columns are unconditionally empty or solid
                case MetatileCollision.COL_SLOPE_RD66_TOP:
                    if ((localX & 0x0f) < 8) return false;
                    tmp7 = (((localX & 0x07) << 1) & 0x0f) ^ 0x0f; tmp4 = localY & 0x0f; break;
                case MetatileCollision.COL_SLOPE_RD66_BOT:
                    if ((localX & 0x0f) >= 8) return true;
                    tmp7 = (((localX & 0x0f) << 1) & 0x0f) ^ 0x0f; tmp4 = localY & 0x0f; break;
                case MetatileCollision.COL_SLOPE_LD66_TOP:
                    if ((localX & 0x0f) >= 8) return false;
                    tmp7 = ((localX & 0x07) << 1) & 0x0f; tmp4 = localY & 0x0f; break;
                case MetatileCollision.COL_SLOPE_LD66_BOT:
                    if ((localX & 0x0f) < 8) return true;
                    tmp7 = ((localX & 0x0f) << 1) & 0x0f; tmp4 = localY & 0x0f; break;
                case MetatileCollision.COL_SLOPE_RU66_TOP:
                    if ((localX & 0x0f) < 8) return false;
                    tmp7 = (((localX & 0x07) << 1) & 0x0f) ^ 0x0f; tmp4 = (localY & 0x0f) ^ 0x0f; break;
                case MetatileCollision.COL_SLOPE_RU66_BOT:
                    if ((localX & 0x0f) >= 8) return true;
                    tmp7 = (((localX & 0x0f) << 1) & 0x0f) ^ 0x0f; tmp4 = (localY & 0x0f) ^ 0x0f; break;
                case MetatileCollision.COL_SLOPE_LU66_TOP:
                    if ((localX & 0x0f) >= 8) return false;
                    tmp7 = ((localX & 0x07) << 1) & 0x0f; tmp4 = (localY & 0x0f) ^ 0x0f; break;
                case MetatileCollision.COL_SLOPE_LU66_BOT:
                    if ((localX & 0x0f) < 8) return true;
                    tmp7 = ((localX & 0x0f) << 1) & 0x0f; tmp4 = (localY & 0x0f) ^ 0x0f; break;

                default: return false;
            }
            return (byte)tmp4 >= (byte)tmp7;
        }

        /// <summary>
        /// Check if a pixel at world coordinates collides with blocking tiles
        /// </summary>
        private bool CheckPixelCollision(int worldX_px, int worldY_px, int groundRowsToReserve)
        {
            int sampleTileX = worldX_px / TILE;
            int sampleTileY = worldY_px / TILE;
            int tileIndexY = sampleTileY + groundRowsToReserve;

            // Out of bounds horizontally = no collision
            if (sampleTileX < 0 || sampleTileX >= mapWidth)
                return false;
            
            // Above the map = solid ceiling (for spider scans)
            if (tileIndexY < 0)
                return true;
                
            // Ground layer is always solid - if tile index is beyond map height
            if (tileIndexY >= mapHeight)
                return true;

            int tid = tiles[tileIndexY * mapWidth + sampleTileX];
            int useTidForAnim = MapAnimatedTileIndex(tid);
            int collisionTid = useTidForAnim;
            
            if (useTidForAnim >= 1000)
            {
                if (useTidForAnim >= 1000 && useTidForAnim <= 1007)
                {
                    collisionTid = 0x08 + ((useTidForAnim - 1000) % 4);
                }
                else if (useTidForAnim >= 1010 && useTidForAnim <= 1015)
                {
                    int group = (useTidForAnim - 1010) % 3;
                    collisionTid = (group == 0) ? 0x04 : (group == 1) ? 0x7D : 0x7F;
                }
                else if (useTidForAnim >= 1020 && useTidForAnim <= 1037)
                {
                    collisionTid = 0x74 + ((useTidForAnim - 1020) % 9);
                }
                else
                {
                    collisionTid = tid;
                }
            }
            
            var collision = MetatileCollisionTable.GetCollision((byte)collisionTid);
            int tileStartX = sampleTileX * TILE;
            int localX = Math.Max(0, Math.Min(TILE - 1, worldX_px - tileStartX));
            int tileStartY = sampleTileY * TILE;
            int localY = Math.Max(0, Math.Min(TILE - 1, worldY_px - tileStartY));

            return TileOccupiesPixel(collision, localX, localY);
        }

        /// <summary>
        /// Helper: check if a single world-pixel coordinate hits a deadly spike tile.
        /// Handles animated tile mapping (saws, etc.) consistently with CheckDeathCollision.
        /// </summary>
        private bool PointHitsSpikeFloor(int px, int py, int groundRowsReserve)
        {
            int tileX = px / TILE;
            int tileY = py / TILE;
            int tileArrayY = tileY + groundRowsReserve;

            if (tileX < 0 || tileX >= mapWidth || tileArrayY < 0 || tileArrayY >= mapHeight) return false;
            int tileIdx = tileArrayY * mapWidth + tileX;
            if (tileIdx < 0 || tileIdx >= tiles.Length) return false;

            int tid = tiles[tileIdx];
            int useTidForAnim = MapAnimatedTileIndex(tid);
            int collisionTid = useTidForAnim;

            if (useTidForAnim >= 1000)
            {
                if (useTidForAnim >= 1000 && useTidForAnim <= 1007)
                    collisionTid = 0x08 + ((useTidForAnim - 1000) % 4);
                else if (useTidForAnim >= 1010 && useTidForAnim <= 1015)
                {
                    int group = (useTidForAnim - 1010) % 3;
                    collisionTid = (group == 0) ? 0x04 : (group == 1) ? 0x7D : 0x7F;
                }
                else if (useTidForAnim >= 1020 && useTidForAnim <= 1037)
                    collisionTid = 0x74 + ((useTidForAnim - 1020) % 9);
                else
                    collisionTid = tid;
            }

            var collision = MetatileCollisionTable.GetCollision((byte)collisionTid);
            if (collision == MetatileCollision.COL_NONE) return false;

            int tileStartX = tileX * TILE;
            int localX = Math.Max(0, Math.Min(TILE - 1, px - tileStartX));
            int tileStartY = tileY * TILE;
            int localY = Math.Max(0, Math.Min(TILE - 1, py - tileStartY));

            return MetatileCollisionTable.TileKillsAtPixel(collision, localX, localY);
        }

        /// <summary>
        /// 4-corner spike check matching NES bg_coll_floor_spikes (collision.h line 214).
        /// Checks 4 inset corners of the hitbox for ALL spike types via TileKillsAtPixel.
        /// NES runs this every frame in x_movement_coll at OLD X, post-eject Y.
        ///
        /// Normal cube (15×15): corners at (X+3, Y+13), (X+12, Y+13), (X+3, Y+2), (X+12, Y+2)
        /// Mini cube   (8×7):   corners at (X+3, Y+9),  (X+5, Y+9),  (X+3, Y+4), (X+5, Y+4)
        ///
        /// NES Y offsets:
        ///   commonly_used_store (bottom):    Y = playerY + (mini ? (0x10-h)>>1 : 0) + h - 2
        ///   commonly_stored_routine_2 (top): Y = playerY + (mini ? (0x10-h)>>1 : 2)
        /// X offsets: left = X+3, right = X+width-3
        /// </summary>
        private bool CheckFloorSpikes(int playerX_px, int playerY_px, out int deathX, out int deathY)
        {
            bool isMini = (currplayer_mini != 0);
            int hitboxW = isMini ? 8 : 15;
            int hitboxH = isMini ? 7 : 15;

            int groundRowsLocal = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
            var map = new SharedPhysics.CollisionMap(tiles, mapWidth, mapHeight, groundRowsLocal);

            bool killed = SharedPhysics.CheckFloorSpikes(in map, playerX_px, playerY_px, hitboxW, hitboxH, isMini,
                out deathX, out deathY);

            if (killed)
            {
                AppendSimDebug($"[FLOOR_SPIKE] Death at ({deathX},{deathY})");
            }

            return killed;
        }

        // Sprite/display probes use the ROM's fixed screen-space fractional bias
        // relative to simulator world Y.
        private int NesPlayerY_px(int playerYFixed)
        {
            return SharedPhysics.NesPlayerY_px(playerYFixed, cameraY_fixed);
        }

        private int NesPlayerBgCollisionY_px(int playerYFixed)
        {
            return SharedPhysics.NesPlayerBgCollisionY_px(playerYFixed, cameraY_fixed);
        }

        /// <summary>
        /// Check if the player's hitbox overlaps with any death tiles.
        /// Returns true if death collision is detected and NO DEATH mode is OFF.
        /// Returns false otherwise (safe or NO DEATH mode is ON).
        /// When death is detected, also triggers death state and returns the death location via out parameters.
        /// </summary>
        private bool CheckDeathCollision(out int deathX_px, out int deathY_px, int? playerXFixedOverride = null)
        {
            deathX_px = 0;
            deathY_px = 0;

            // If NO DEATH mode is on, never trigger death
            if (MainWindow.Option_NoDeath) return false;
            // NES runthecolls does not call bg_coll_death while the level-start
            // invincibility counter is non-zero.
            if (invincibleCounter != 0) return false;

            // If death already triggered, don't check again
            if (deathTriggered) return false;

            int playerX_px = (playerXFixedOverride ?? playerX_fixed) >> 8;
            int playerY_px = NesPlayerY_px(playerY_fixed);
            
            // Use player hitbox dimensions
            bool isMini = (currplayer_mini != 0);
            int width, height;
            // NES x_movement sets Generic.width/height = WAVE_WIDTH/WAVE_HEIGHT (8x8)
            // for wave/snake before bg_coll_death runs.  Mini wave still uses 8x8 there
            // (bg_coll_death uses Generic.width/height that x_movement just set).
            if (currentGameMode == 6 || currentGameMode == 10)  // wave or snake
            {
                width = 8;
                height = 8;
            }
            else
            {
                width = isMini ? 8 : 15;
                height = isMini ? 7 : 15;
            }
            
            // Apply mini mode centering offset to match NES bg_coll_death:
            // NES computes center as Y + (height>>1) + (mini ? (0x10-height)>>1 : 0)
            // The centering offset is applied unconditionally for mini players.
            // In particular, NES bg_coll_death also adds (0x10-8)>>1 for a mini
            // wave/snake even though those modes use the fixed 8x8 hitbox.
            if (miniMode)
            {
                playerY_px += (0x10 - height) >> 1;
            }
            
            int groundRowsToReserve_local = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
            
            // Center-point only death check — matches NES bg_coll_death.
            // The NES 4-corner check (bg_coll_floor_spikes) now runs separately
            // via CheckFloorSpikes in the forward collision section of SimulateNumericStep.
            // This method covers the center-point check at post-eject Y.
            int centerX = playerX_px + (width >> 1) - 1;
            int centerY = playerY_px + (height / 2);
            
            int sampleTileX = centerX / TILE;
            int sampleTileY = centerY / TILE;
            int tileIndexY = sampleTileY + groundRowsToReserve_local;

            if (tileIndexY < 0 || tileIndexY >= mapHeight || sampleTileX < 0 || sampleTileX >= mapWidth)
                return false;

            int tid = tiles[tileIndexY * mapWidth + sampleTileX];
            int useTidForAnim = MapAnimatedTileIndex(tid);
            int collisionTid = useTidForAnim;
            
            // Debug: log what we're checking
            try { AppendSimDebug($"[DEATH_CHECK] Point ({centerX},{centerY}) -> Tile({sampleTileX},{sampleTileY}) TID={tid}"); } catch { }

            if (useTidForAnim >= 1000)
            {
                if (useTidForAnim >= 1000 && useTidForAnim <= 1007)
                {
                    collisionTid = 0x08 + ((useTidForAnim - 1000) % 4);
                }
                else if (useTidForAnim >= 1010 && useTidForAnim <= 1015)
                {
                    int group = (useTidForAnim - 1010) % 3;
                    collisionTid = (group == 0) ? 0x04 : (group == 1) ? 0x7D : 0x7F;
                }
                else if (useTidForAnim >= 1020 && useTidForAnim <= 1037)
                {
                    collisionTid = 0x74 + ((useTidForAnim - 1020) % 9);
                }
                else
                {
                    collisionTid = tid;
                }
            }

            var collision = MetatileCollisionTable.GetCollision((byte)collisionTid);
            int tileStartX = sampleTileX * TILE;
            int localX = Math.Max(0, Math.Min(TILE - 1, centerX - tileStartX));
            int tileStartY = sampleTileY * TILE;
            int localY = Math.Max(0, Math.Min(TILE - 1, centerY - tileStartY));

            // Forced platformer bg_coll_death deliberately omits
            // bg_coll_top_bottom_slabs(). These remain floor/ceiling collision,
            // but their empty half is not an automatic center-point death.
            if (forcePlatformer && !(currentGameMode == 6 && dblocked) &&
                (collision == MetatileCollision.COL_TOP ||
                 collision == MetatileCollision.COL_BOTTOM))
                return false;

            // Check if this pixel causes death (bg_coll_spikes equivalent)
            if (MetatileCollisionTable.TileKillsAtPixel(collision, localX, localY))
            {
                deathX_px = centerX;
                deathY_px = centerY;
                return true;
            }

            // NES bg_coll_death also calls bg_coll_mini_blocks() at the center point.
            // mini_blocks kills (in bg_coll_death context) when the center pixel is in
            // the SOLID half of a partial-collision tile: COL_TOP/BOTTOM half-slabs,
            // COL_DOWN_*/UP_* quadrants, COL_LEFT/RIGHT half-walls, diagonals, stairs,
            // and SPIKE_BLOCK partials. Without this, walking horizontally into the
            // side of a half-slab is incorrectly survived.
            //
            // Includes full-block COL_ALL / COL_FLOOR_CEIL / COL_NO_SIDE: NES
            // bg_coll_U_D_checks returns 1 for these, so the center-in-solid-block
            // case kills on NES.  Now that bg_coll_D probe positions match NES
            // (collW instead of collW+1), eject snap distance also matches —
            // the previously-feared false positives no longer occur.
            switch (collision)
            {
                case MetatileCollision.COL_ALL:
                case MetatileCollision.COL_FLOOR_CEIL:
                case MetatileCollision.COL_NO_SIDE:
                case MetatileCollision.COL_BOTTOM:
                case MetatileCollision.COL_TOP:
                case MetatileCollision.COL_TOP_CENTER_SPIKE:
                case MetatileCollision.COL_LEFT:
                case MetatileCollision.COL_RIGHT:
                case MetatileCollision.COL_UP_LEFT:
                case MetatileCollision.COL_UP_RIGHT:
                case MetatileCollision.COL_DOWN_LEFT:
                case MetatileCollision.COL_DOWN_RIGHT:
                case MetatileCollision.COL_LEFT_SPIKE_BLOCK:
                case MetatileCollision.COL_RIGHT_SPIKE_BLOCK:
                case MetatileCollision.COL_TOP_LEFT_BOTTOM_RIGHT:
                case MetatileCollision.COL_TOP_RIGHT_BOTTOM_LEFT:
                case MetatileCollision.COL_TOP_LEFT_STAIRS:
                case MetatileCollision.COL_TOP_RIGHT_STAIRS:
                case MetatileCollision.COL_BOTTOM_LEFT_STAIRS:
                case MetatileCollision.COL_BOTTOM_RIGHT_STAIRS:
                    if (SharedPhysics.TileOccupiesPixel(collision, localX, localY))
                    {
                        deathX_px = centerX;
                        deathY_px = centerY;
                        return true;
                    }
                    break;
            }

            return false;
        }
        
        /// <summary>
        /// Stop music playback
        /// </summary>
        private async System.Threading.Tasks.Task StopMusicAsync()
        {
            try
            {
                if (this.Owner is MainWindow mw)
                {
                    AppendSimDebug("[MUSIC] Stopping music");
                    await mw.StopSimulatorPlaybackAsync();
                }
            }
            catch { }
        }

        /// <summary>
        /// Check for gravity portal collision and activate if touched
        /// Portals: 0x08,0x10,0x11,0xFC = normal gravity
        ///          0x09,0x12,0x13,0xFB = reversed gravity
        /// </summary>
        private void CheckGravityPortals()
        {
            try
            {
                // NES: Generic.x = high_byte(currplayer_x) + 1
                int playerX_px = (playerX_fixed >> 8) + 1;
                int playerY_px = NesPlayerY_px(playerY_fixed);
                
                // Use actual collision hitbox size, not visual size
                int hitboxW = miniMode ? 8 : 15;
                int hitboxH = miniMode ? 7 : 15;
                
                // Apply mini mode offset matching terrain collision conventions
                playerY_px += GetMiniSpriteOffsetY();
                
                // Player bounding box for collision using actual hitbox size
                int playerLeft_px = playerX_px;
                int playerRight_px = playerX_px + hitboxW - 1;
                int playerTop_px = playerY_px;
                int playerBottom_px = playerY_px + hitboxH - 1;
                
                AppendSimDebug($"[GRAV_PORTAL_CHECK] mini={miniMode} grav={gravityFlipped} playerBox=({playerLeft_px},{playerTop_px})-({playerRight_px},{playerBottom_px}) size={hitboxW}x{hitboxH}");
                
                // Iterate through ALL sprites and check for gravity portals
                // NOTE: Do NOT skip sub-tiles (spriteAnchors entries) — the PF
                // processes every sprite index including sub-tiles, and gravity
                // portals may only exist as sub-tiles of multi-tile sprites.
                for (int _si = 0; _si < SimulatorInteractionSpriteCount; _si++)
                {
                    int idx = SimulatorInteractionSpriteIndex(_si);
                    int sid = SimulatorInteractionSpriteId(idx);
                    if (sid < 0) continue;
                    
                    // Check if this sprite is a gravity portal
                    bool isNormalGravityPortal = (sid == 0x08 || sid == 0x10 || sid == 0x11 || sid == 0xFC);
                    bool isReversedGravityPortal = (sid == 0x09 || sid == 0x12 || sid == 0x13 || sid == 0xFB);
                    
                    if (!isNormalGravityPortal && !isReversedGravityPortal) continue;
                    
                    // Check if already activated
                    if (!forcePlatformer && processedGravityPortals.Contains(idx)) continue;
                    
                    // Use SpriteIntersectsPlayer to check sprite hitbox overlap
                    // NES sprite_collide/check_collision counts the edge-touch
                    // frame for gravity portals.  Using positive-only X overlap
                    // makes sim/PF hit these one frame late.
                    bool gravIntersects = SpriteIntersectsPlayer(idx, sid, playerLeft_px, playerRight_px, playerTop_px, playerBottom_px);
                    if (gravIntersects)
                    {
                        // Activate portal with proper conditional logic
                        bool activated = false;
                        
                        // Reverse portals: only activate if gravity is currently normal
                        if (isReversedGravityPortal && !gravityReversed)
                        {
                            gravityReversed = true;
                            gravityFlipped = true;
                            currplayer_gravity = 0xFF;
                            wasZeroedByCollisionLastFrame = false;  // Reset flag on gravity flip
                            activated = true;
                            
                            AppendSimDebug($"[GRAV_PORTAL] REVERSE ACTIVATED: mini={miniMode}/{currplayer_mini} grav={gravityFlipped}/{currplayer_gravity:X2} reversed={gravityReversed}");
                        }
                        // Normal portals: only activate if gravity is currently reversed
                        else if (isNormalGravityPortal && gravityReversed)
                        {
                            gravityReversed = false;
                            gravityFlipped = false;
                            currplayer_gravity = 0x00;
                            wasZeroedByCollisionLastFrame = false;  // Reset flag on gravity flip
                            activated = true;
                            
                            AppendSimDebug($"[GRAV_PORTAL] NORMAL ACTIVATED: mini={miniMode}/{currplayer_mini} grav={gravityFlipped}/{currplayer_gravity:X2} reversed={gravityReversed}");
                        }
                        
                        if (activated)
                        {
                            // Update player icon flip and checkbox on UI thread
                            try { Dispatcher?.BeginInvoke(new Action(() => { UpdatePlayerIconFlip(); InvertedCheckBox.IsChecked = gravityReversed; })); } catch { }
                            
                            // NES/cc65 lowers this gravity-portal halve to an
                            // arithmetic shift on the replayed path.  Odd negative
                            // velocities round down: -725 -> -363.
                            playerVelY_fixed >>= 1;
                            robotJumpTime[currplayer] = 0;
                            
                            AppendSimDebug($"[GRAV_PORTAL] POST-ACTIVATION: currplayer_gravity={currplayer_gravity:X2} gravityFlipped={gravityFlipped} gravityReversed={gravityReversed}");
                            
                            // Mark as activated
                            processedGravityPortals.Add(idx);
                            
                            // Only one portal per frame
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppendSimDebug($"[GRAVITY PORTAL] Error: {ex.Message}");
            }
        }

        private void CheckGameModePortals()
        {
            try
            {
                int playerX_px = (playerX_fixed >> 8) + 1;
                int playerY_px = NesPlayerY_px(playerY_fixed);
                // NES sprite_collide gives the 8x8 box only to wave. Snake is
                // intentionally in the cube-dimensions branch.
                bool isWaveGmp = currentGameMode == 6;
                int hitboxW = isWaveGmp ? 8 : (miniMode ? 8 : 15);
                int hitboxH = isWaveGmp ? 8 : (miniMode ? 7 : 15);

                if (isWaveGmp)
                    playerY_px += 4;  // (0x10 - 8) >> 1
                else
                    playerY_px += GetMiniSpriteOffsetY();

                int playerLeft_px = playerX_px;
                int playerRight_px = playerX_px + hitboxW - 1;
                int playerTop_px = playerY_px;
                int playerBottom_px = playerY_px + hitboxH - 1;

                for (int _si = 0; _si < SimulatorInteractionSpriteCount; _si++)
                {
                    int idx = SimulatorInteractionSpriteIndex(_si);
                    int sid = SimulatorInteractionSpriteId(idx);
                    if (sid < 0) continue;

                    if (sid == 0x00 || sid == 0x01 || sid == 0x02 || sid == 0x03 || sid == 0x04 || sid == 0x17 || sid == 0x24 || sid == 0x4B || sid == 0x58 || sid == 0x6A || sid == 0x6B || sid == 0x6C)
                    {
                        bool cameraRampPortal = sid == 0x00 || sid == 0x04;
                        // Cube/robot handlers reset exitPortalTimer on every
                        // overlap, including frames after activation.
                        if (!forcePlatformer && processedGameModePortals.Contains(idx) && !cameraRampPortal) continue;

                        if (!SpriteIntersectsPlayer(idx, sid, playerLeft_px, playerRight_px, playerTop_px, playerBottom_px))
                            continue;

                        if (cameraRampPortal)
                        {
                            _sim_exitPortalTimer = 10;
                            // spcl_cube/spcl_robot clear the two animation-frame
                            // counters on every overlap without clearing the robot
                            // jump-duration counters used by physics.
                            robotJumpFrame[0] = 0;
                            robotJumpFrame[1] = 0;
                        }

                        // Cube/robot portals never set activesprites_activated on
                        // NES; all other game-mode portals are one-shot.
                        if (!cameraRampPortal)
                            processedGameModePortals.Add(idx);

                        int oldMode = currentGameMode;
                        int newMode = sid switch {
                            0x00 => 0,
                            0x01 => 1,
                            0x02 => 2,
                            0x03 => 3,
                            0x04 => 4,
                            0x17 => 5,
                            0x24 => 6,
                            0x4B => 7,
                            0x58 => 8,
                            0x6A => 9,
                            0x6B => 10,
                            0x6C => 11,
                            _ => currentGameMode
                        };

                        if (newMode != oldMode)
                        {
                            currentGameMode = newMode;
                            pfHoldCounter = 0; // Reset ball-hold extension on mode change
                            p2BallHoldCounter = 0;
                            // Clear stale collision-zeroing flag so it doesn't leak
                            // across game modes.  E.g. ball eject sets the flag; UFO
                            // never clears it; wave then wrongly skips velY recalculation.
                            wasZeroedByCollisionLastFrame = false;
                            // Clear ball flip buffer — a buffered flip from a previous
                            // ball segment shouldn't leak through other modes.
                            ballFlipBuffer[0] = 0;
                            ballFlipBuffer[1] = 0;
                            // Clear ballToggleRequested — a press during the old ball mode
                            // that wasn't consumed must not carry into the next ball segment.
                            Interlocked.Exchange(ref ballToggleRequested, 0);
                            // NOTE: Do NOT clear keyXPressedCount here.
                            // PF_InjectInput already clears pressCount at the start
                            // of every frame, so all presses are fresh.  The PF's
                            // input decision accounts for the mode change — if it
                            // sends inp=1 on a frame where a portal changes mode,
                            // the press is intended for the NEW mode (e.g. ship
                            // frame with inp=1 → cube jump, ball inp=1 → ship orb).
                            // Clearing it here destroyed legitimate cross-mode input.
                            try { UpdateGameModeDisplay(); } catch { }
                            try { UpdateEffectiveGravity(); } catch { }
                            // NES portal velY rules (sprite_loading.h ~764):
                            //   ship(1)/ball(2)/UFO(3): halve velY
                            //   robot(4): halve only if prev was WAVE(6)/SNAKE(10)
                            //   cube(0)/spider(5)/swing(7)/ninja(8)/pogo(9): zero only if prev WAVE/SNAKE
                            //   wave(6)/snake(10)/football(11): no change
                            try
                            {
                                bool prevWaveSnake = (oldMode == 6 || oldMode == 10);
                                switch (newMode)
                                {
                                    case 1: case 2: case 3:
                                        // NES game-mode portal ASR semantics; odd negative
                                        // velocities round down (-545 -> -273).
                                        playerVelY_fixed >>= 1;
                                        break;
                                    case 4:
                                        if (prevWaveSnake) playerVelY_fixed >>= 1;
                                        break;
                                    case 0: case 5: case 7: case 8: case 9:
                                        if (prevWaveSnake) playerVelY_fixed = 0;
                                        break;
                                }
                            }
                            catch { }
                        }

                        // Set target_scroll_y for modes that use smooth camera scroll
                        // Matches Famidash: ship/UFO/ball/spider/wave/swing/snake/football/pogo all set target
                        // Cube(0x00) and Robot(0x04) and Ninja(0x58) do NOT set target_scroll_y
                        if ((!dual || twoplayer) && sid != 0x00 && sid != 0x04 && sid != 0x58)
                        {
                            try
                            {
                                int storageTileY = idx / mapWidth;
                                int groundRowsLocal = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                                int portalWorldY_px = IsSimulatorNesRawDispatchIndex(idx)
                                    ? SimulatorNesDispatchWorldY()
                                    : (storageTileY - groundRowsLocal) * TILE;
                                targetCameraY_fixed = NesNtCameraTarget_fixed(portalWorldY_px);
                            }
                            catch { }
                        }

                        try { Dispatcher?.BeginInvoke(new Action(() => { try { UpdatePlayerImageForMode(); } catch { } })); } catch { }
                        if (newMode != oldMode)
                            break;  // Mode changed — stop scanning
                        // Same mode — continue scanning for other portals at different Y
                        continue;
                    }

                    if (sid == 0x64 || sid == 0x7E)
                    {
                        if (!forcePlatformer && processedRandomPortals.Contains(idx)) continue;
                        if (!SpriteIntersectsPlayer(idx, sid, playerLeft_px, playerRight_px, playerTop_px, playerBottom_px))
                            continue;

                        int oldMode = currentGameMode;
                        int newMode = (sid == 0x64)
                            ? new System.Random().Next(0, 8)
                            : new System.Random().Next(0, 12);

                        if (newMode != oldMode)
                        {
                            currentGameMode = newMode;
                            pfHoldCounter = 0; // Reset ball-hold extension on mode change
                            p2BallHoldCounter = 0;
                            wasZeroedByCollisionLastFrame = false;
                            ballFlipBuffer[0] = 0;
                            ballFlipBuffer[1] = 0;
                            Interlocked.Exchange(ref ballToggleRequested, 0);
                            // NOTE: Do NOT clear keyXPressedCount — see main portal block.
                            try { UpdateGameModeDisplay(); } catch { }
                            try { UpdateEffectiveGravity(); } catch { }
                            // NES portal velY rules — see main portal block above.
                            try
                            {
                                bool prevWaveSnake = (oldMode == 6 || oldMode == 10);
                                switch (newMode)
                                {
                                    case 1: case 2: case 3:
                                        playerVelY_fixed >>= 1;  // cc65 ASR; see notes
                                        break;
                                    case 4:
                                        if (prevWaveSnake) playerVelY_fixed >>= 1;
                                        break;
                                    case 0: case 5: case 7: case 8: case 9:
                                        if (prevWaveSnake) playerVelY_fixed = 0;
                                        break;
                                }
                            }
                            catch { }
                        }

                        try { Dispatcher?.BeginInvoke(new Action(() => { try { UpdatePlayerImageForMode(); } catch { } })); } catch { }
                        processedRandomPortals.Add(idx);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                AppendSimDebug($"[GAMEMODE PORTAL] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Check for gravity modifier portal collision (0x5F-0x63)
        /// 0x5F = 1/3 gravity, 0x60 = 1/2 gravity, 0x61 = 2/3 gravity
        /// 0x62 = 2x gravity, 0x63 = 1x gravity (normal)
        /// </summary>
        private void CheckGravityModPortals()
        {
            try
            {
                AppendSimDebug($"[GRAV_MOD] Checking portals - playerX={playerX_fixed >> 8}, playerY={NesPlayerY_px(playerY_fixed)}, gravMult={gravityMultiplier:F3}");
                
                // NES: Generic.x = high_byte(currplayer_x) + 1
                int playerX_px = (playerX_fixed >> 8) + 1;
                int playerY_px = NesPlayerY_px(playerY_fixed);
                
                // Use actual collision hitbox size
                int hitboxW = miniMode ? 8 : 15;
                int hitboxH = miniMode ? 7 : 15;
                
                // Apply mini mode offset matching terrain collision conventions
                playerY_px += GetMiniSpriteOffsetY();
                
                // Player bounding box
                int playerLeft_px = playerX_px;
                int playerRight_px = playerX_px + hitboxW - 1;
                int playerTop_px = playerY_px;
                int playerBottom_px = playerY_px + hitboxH - 1;
                
                // Iterate through sprites and check for gravity mod portals
                for (int _si = 0; _si < SimulatorInteractionSpriteCount; _si++)
                {
                    int idx = SimulatorInteractionSpriteIndex(_si);
                    int sid = SimulatorInteractionSpriteId(idx);
                    if (sid < 0) continue;
                    
                    // Check if this sprite is a gravity mod portal (0x5F-0x63)
                    if (sid < 0x5F || sid > 0x63) continue;
                    
                    // Check if already activated
                    if (!forcePlatformer && processedGravityModPortals.Contains(idx)) continue;
                    
                    // Check sprite collision
                    if (SpriteIntersectsPlayer(idx, sid, playerLeft_px, playerRight_px, playerTop_px, playerBottom_px))
                    {
                        // Set gravity multiplier based on portal type
                        double newMultiplier = 1.0;
                        switch (sid)
                        {
                            case 0x5F: newMultiplier = 1.0 / 3.0; break;  // 1/3 gravity
                            case 0x60: newMultiplier = 0.5; break;         // 1/2 gravity
                            case 0x61: newMultiplier = 2.0 / 3.0; break;  // 2/3 gravity
                            case 0x62: newMultiplier = 2.0; break;         // 2x gravity
                            case 0x63: newMultiplier = 1.0; break;         // Normal gravity
                        }
                        
                        gravityMultiplier = newMultiplier;
                        
                        // Update effective gravity with the new multiplier
                        UpdateEffectiveGravity();
                        
                        AppendSimDebug($"[GRAV_MOD] Portal 0x{sid:X2} activated: multiplier set to {gravityMultiplier:F3}x, effectiveGravity={effectiveGravity_fixed}");
                        
                        // Mark as activated
                        processedGravityModPortals.Add(idx);
                        
                        // Only one portal per frame
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                AppendSimDebug($"[GRAV_MOD] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Check for gravity mod triggers (0x70-0x74) crossing the interaction line
        /// These are threshold-based (like color triggers) but control gravity multiplier
        /// </summary>
        private void CheckGravityModTriggers(int prevPlayerCenter_fixed, int attemptedPlayerCenter_fixed)
        {
            try
            {
                // Gravity mod triggers (0x70-0x74) activate when the NES sprite
                // becomes ON-SCREEN in `check_spr_objects`. Editor-stored anchor
                // is shifted -10 cols (-160 px) by `TmxHandler` for visual intent;
                // NES sees the trigger at anchor+160 px. NES activates when
                // sprite_x <= scroll_x + 256 (= player_x + 176, camera lag 80).
                // Therefore activate when editor anchor <= player_x + 16.
                int center_fixed = (playerX_fixed + (TILE << 8)) - 0; // player + 16 px (= 1 tile lookahead from editor anchor)
                bool crossedInteraction = prevPlayerCenter_fixed < INTERACTION_LINE_FIXED && attemptedPlayerCenter_fixed >= INTERACTION_LINE_FIXED;
                
                // Scan all sprites for gravity mod triggers
                for (int _si = 0; _si < SimulatorInteractionSpriteCount; _si++)
                {
                    int idx = SimulatorInteractionSpriteIndex(_si);
                    int sid = SimulatorInteractionSpriteId(idx);
                    if (sid < 0) continue;
                    
                    if (!IsGravityModTrigger(sid)) continue;
                    
                    // Determine anchor tile X for this trigger
                    int anchorTileX;
                    if (spriteAnchors != null && spriteAnchors.TryGetValue(idx, out var anchor))
                        anchorTileX = anchor.anchorTileX;
                    else
                        anchorTileX = idx % mapWidth;
                    
                    int anchorX_center_fixed = ((anchorTileX * TILE) + (TILE / 2)) << 8;
                    
                    if (crossedInteraction)
                    {
                        // During a player crossing, check if this trigger crossed the interaction line
                        if (anchorX_center_fixed > prevPlayerCenter_fixed && anchorX_center_fixed <= INTERACTION_LINE_FIXED)
                        {
                            if (!processedGravityModPortals.Contains(idx))
                            {
                                // Set gravity multiplier based on trigger type (same order as portals)
                                double newMultiplier = 1.0;
                                switch (sid)
                                {
                                    case 0x70: newMultiplier = 1.0 / 3.0; break;  // 1/3 gravity
                                    case 0x71: newMultiplier = 0.5; break;         // 1/2 gravity
                                    case 0x72: newMultiplier = 2.0 / 3.0; break;  // 2/3 gravity
                                    case 0x73: newMultiplier = 2.0; break;         // 2x gravity
                                    case 0x74: newMultiplier = 1.0; break;         // Normal gravity
                                }
                                
                                gravityMultiplier = newMultiplier;
                                UpdateEffectiveGravity();
                                
                                AppendSimDebug($"[GRAV_MOD_TRIG] Trigger 0x{sid:X2} activated at X={anchorTileX * TILE}: multiplier={gravityMultiplier:F3}x");
                                
                                processedGravityModPortals.Add(idx);
                            }
                        }
                        else
                        {
                            // Anchor is to the right of the interaction line; clear processed flag so it can trigger again when recrossed
                            if (processedGravityModPortals.Contains(idx) && anchorX_center_fixed > INTERACTION_LINE_FIXED)
                                processedGravityModPortals.Remove(idx);
                        }
                    }
                    else
                    {
                        // Camera-centered detection
                        if (anchorX_center_fixed <= center_fixed)
                        {
                            if (!processedGravityModPortals.Contains(idx))
                            {
                                double newMultiplier = 1.0;
                                switch (sid)
                                {
                                    case 0x70: newMultiplier = 1.0 / 3.0; break;
                                    case 0x71: newMultiplier = 0.5; break;
                                    case 0x72: newMultiplier = 2.0 / 3.0; break;
                                    case 0x73: newMultiplier = 2.0; break;
                                    case 0x74: newMultiplier = 1.0; break;
                                }
                                
                                gravityMultiplier = newMultiplier;
                                UpdateEffectiveGravity();
                                
                                AppendSimDebug($"[GRAV_MOD_TRIG] Trigger 0x{sid:X2} activated at X={anchorTileX * TILE}: multiplier={gravityMultiplier:F3}x");
                                
                                processedGravityModPortals.Add(idx);
                            }
                        }
                        else
                        {
                            // Anchor is left-of-center; clear processed flag
                            if (processedGravityModPortals.Contains(idx))
                                processedGravityModPortals.Remove(idx);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppendSimDebug($"[GRAV_MOD_TRIG] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Check for mini/growth portal collision and activate if touched
        /// Portals: 0x18 = mini portal, 0x19 = growth portal
        /// </summary>
        private void CheckMiniGrowthPortals()
        {
            try
            {
                // NES: Generic.x = high_byte(currplayer_x) + 1
                int playerX_px = (playerX_fixed >> 8) + 1;
                int playerY_px = NesPlayerY_px(playerY_fixed);
                
                // Use actual collision hitbox size (15x15 for normal, 8x7 for mini)
                bool isMini = (currplayer_mini != 0);
                int hitboxW = isMini ? 8 : 15;
                int hitboxH = isMini ? 7 : 15;
                
                // Apply mini mode offset matching terrain collision conventions
                playerY_px += GetMiniSpriteOffsetY();
                
                // Player bounding box for collision
                int playerLeft_px = playerX_px;
                int playerRight_px = playerX_px + hitboxW - 1;
                int playerTop_px = playerY_px;
                int playerBottom_px = playerY_px + hitboxH - 1;
                
                // Iterate through ALL sprites and check for mini/growth portals
                for (int _si = 0; _si < SimulatorInteractionSpriteCount; _si++)
                {
                    int idx = SimulatorInteractionSpriteIndex(_si);
                    int sid = SimulatorInteractionSpriteId(idx);
                    if (sid < 0) continue;
                    
                    // Check if this sprite is a mini or growth portal
                    bool isMiniPortal = (sid == 0x18);
                    bool isGrowthPortal = (sid == 0x19);
                    
                    if (!isMiniPortal && !isGrowthPortal) continue;
                    
                    // Check if already activated
                    if (!forcePlatformer && processedMiniPortals.Contains(idx)) continue;
                    
                    // Use SpriteIntersectsPlayer to check sprite hitbox overlap
                    if (SpriteIntersectsPlayer(idx, sid, playerLeft_px, playerRight_px, playerTop_px, playerBottom_px))
                    {
                        //bool newminiMode = isMiniPortal;  // ERROR CS0650/CS0270 fixed: removed invalid syntax
                        
                        if (miniMode != isMiniPortal)
                        {
                            miniMode = isMiniPortal;
                            currplayer_mini = (byte)(miniMode ? 1 : 0);
                            
                            // Apply to both players in dual mode
                            if (dual)
                            {
                                player_mini[0] = miniMode;
                                player_mini[1] = miniMode;
                            }
                            
                            // Update UI checkbox
                            try { Dispatcher.BeginInvoke(new Action(() => { if (MiniCheckBox != null) MiniCheckBox.IsChecked = miniMode; })); } catch { }
                            
                            // Update player visuals (must dispatch to UI thread — this runs on threadpool via SimulateNumericStep)
                            try { Dispatcher?.BeginInvoke(new Action(() => { try { UpdatePlayerImageForMode(); } catch { } })); } catch { }
                            try { Dispatcher?.BeginInvoke(new Action(() => { try { UpdatePlayerVisualSizeForMode(); } catch { } })); } catch { }
                            
                            // Mark as activated
                            processedMiniPortals.Add(idx);
                            
                            // Only one portal per frame
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppendSimDebug($"[MINI/GROWTH PORTAL] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Check for cam lock triggers and wrap portal collision.
        /// 0xDD = freecam ON (camera follows player Y freely)
        /// 0xED = freecam OFF (resume locked camera Y)
        /// Cam lock triggers use X-crossing detection (same as gravity mod triggers):
        ///   before the player reaches the interaction line, activate when trigger X
        ///   is at or behind the camera center; after crossing the interaction line,
        ///   activate when trigger X falls between prevCenter and the interaction line.
        /// Wrap portals (0x8E/0x9E) still use hitbox collision.
        /// </summary>
        private void CheckCamLockPortals(int prevPlayerCenter_fixed, int attemptedPlayerCenter_fixed)
        {
            try
            {
                // --- Cam lock triggers (0xDD / 0xED): X-crossing detection ---
                int center_fixed = cameraX_fixed + ((NES_W * TILE / 2) << 8);
                bool crossedInteraction = prevPlayerCenter_fixed < INTERACTION_LINE_FIXED && attemptedPlayerCenter_fixed >= INTERACTION_LINE_FIXED;

                for (int _si = 0; _si < SimulatorInteractionSpriteCount; _si++)
                {
                    int idx = SimulatorInteractionSpriteIndex(_si);
                    int sid = SimulatorInteractionSpriteId(idx);
                    if (sid < 0) continue;

                    bool isCamLockOn = (sid == 0xDD);
                    bool isCamLockOff = (sid == 0xED);
                    if (!isCamLockOn && !isCamLockOff) continue;

                    int anchorTileX;
                    if (spriteAnchors != null && spriteAnchors.TryGetValue(idx, out var anchor))
                        anchorTileX = anchor.anchorTileX;
                    else
                        anchorTileX = idx % mapWidth;

                    int anchorX_center_fixed = ((anchorTileX * TILE) + (TILE / 2)) << 8;

                    if (crossedInteraction)
                    {
                        // Player is crossing from pre-interaction to post-interaction this frame.
                        // Activate any cam lock trigger whose X is between prev center and the line.
                        if (anchorX_center_fixed > prevPlayerCenter_fixed && anchorX_center_fixed <= INTERACTION_LINE_FIXED)
                        {
                            if (!processedCamLockPortals.Contains(idx))
                            {
                                nocamlockforced = isCamLockOn;
                                processedCamLockPortals.Add(idx);
                                AppendSimDebug($"[CAM_LOCK] nocamlockforced={nocamlockforced} at idx={idx} (crossing)");
                            }
                        }
                        else if (anchorX_center_fixed > INTERACTION_LINE_FIXED)
                        {
                            // Trigger is ahead of the interaction line; clear so it can re-fire later
                            processedCamLockPortals.Remove(idx);
                        }
                    }
                    else
                    {
                        // Normal scrolling: activate when trigger X reaches camera center
                        if (anchorX_center_fixed <= center_fixed)
                        {
                            if (!processedCamLockPortals.Contains(idx))
                            {
                                nocamlockforced = isCamLockOn;
                                processedCamLockPortals.Add(idx);
                                AppendSimDebug($"[CAM_LOCK] nocamlockforced={nocamlockforced} at idx={idx} (center)");
                            }
                        }
                        else
                        {
                            processedCamLockPortals.Remove(idx);
                        }
                    }
                }

                // --- Wrap portals (0x8E / 0x9E): hitbox collision ---
                int playerX_px = (playerX_fixed >> 8) + 1;
                int playerY_px = NesPlayerY_px(playerY_fixed);

                bool isMini = (currplayer_mini != 0);
                int hitboxW = isMini ? 8 : 15;
                int hitboxH = isMini ? 7 : 15;
                playerY_px += GetMiniSpriteOffsetY();

                int playerLeft_px = playerX_px;
                int playerRight_px = playerX_px + hitboxW - 1;
                int playerTop_px = playerY_px;
                int playerBottom_px = playerY_px + hitboxH - 1;

                for (int _si = 0; _si < SimulatorInteractionSpriteCount; _si++)
                {
                    int idx = SimulatorInteractionSpriteIndex(_si);
                    int sid = SimulatorInteractionSpriteId(idx);
                    if (sid < 0) continue;

                    bool isWrapOn = (sid == 0x8E);
                    bool isWrapOff = (sid == 0x9E);
                    if (!isWrapOn && !isWrapOff) continue;

                    if (!forcePlatformer && processedWrapPortals.Contains(idx)) continue;

                    if (SpriteIntersectsPlayer(idx, sid, playerLeft_px, playerRight_px, playerTop_px, playerBottom_px, true))
                    {
                        wrapMode = isWrapOn;
                        processedWrapPortals.Add(idx);
                        AppendSimDebug($"[WRAP] wrapMode={wrapMode} at idx={idx}");
                    }
                }
            }
            catch (Exception ex)
            {
                AppendSimDebug($"[CAM_LOCK] Error: {ex.Message}");
            }
        }

        // Check for timewarp (0xF4=slowmode ON, 0xF5=slowmode OFF),
        // hide player (0x6F=hide, 0x7F=show), and trail triggers (0xF2=trails ON, 0xF3=trails OFF)
        private void CheckMiscTriggers()
        {
            try
            {
                int playerX_px = (playerX_fixed >> 8) + 1;
                int playerY_px = NesPlayerY_px(playerY_fixed);
                bool isMini = (currplayer_mini != 0);
                int hitboxW = isMini ? 8 : 15;
                int hitboxH = isMini ? 7 : 15;
                playerY_px += GetMiniSpriteOffsetY();
                int playerLeft_px = playerX_px;
                int playerRight_px = playerX_px + hitboxW - 1;
                int playerTop_px = playerY_px;
                int playerBottom_px = playerY_px + hitboxH - 1;

                for (int _si = 0; _si < SimulatorInteractionSpriteCount; _si++)
                {
                    int idx = SimulatorInteractionSpriteIndex(_si);
                    int sid = SimulatorInteractionSpriteId(idx);
                    if (sid < 0) continue;

                    // Timewarp
                    if (sid == 0xF4 || sid == 0xF5)
                    {
                        if (processedTimewarpTriggers.Contains(idx)) continue;
                        if (SpriteIntersectsPlayer(idx, sid, playerLeft_px, playerRight_px, playerTop_px, playerBottom_px, true))
                        {
                            slowMode = (sid == 0xF4);
                            processedTimewarpTriggers.Add(idx);
                            continue;
                        }
                    }
                    // Player trails
                    else if (sid == 0xF2 || sid == 0xF3)
                    {
                        if (processedTrailTriggers.Contains(idx)) continue;
                        if (SpriteIntersectsPlayer(idx, sid, playerLeft_px, playerRight_px, playerTop_px, playerBottom_px, true))
                        {
                            forcedTrails = (sid == 0xF2) ? 2 : 0;
                            processedTrailTriggers.Add(idx);
                            continue;
                        }
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Check for dual portal (sprite 0x22) collision and spawn player 2
        /// Dual Portal: Spawns a second player with:
        /// - Same X position
        /// - Same Y position as current player
        /// - Inverted gravity (gravity ^ 0xFF)
        /// - Negated Y velocity
        /// - Same mini mode
        /// </summary>
        private void CheckDualPortal()
        {
            try
            {
				// NES: Generic.x = high_byte(currplayer_x) + 1
                int playerX_px = (playerX_fixed >> 8) + 1;
                int playerY_px = NesPlayerY_px(playerY_fixed);
                
                // NES hitbox: CUBE_WIDTH x CUBE_HEIGHT
                int hitboxW = miniMode ? 8 : 15;
                int hitboxH = miniMode ? 7 : 15;
                playerY_px += GetMiniSpriteOffsetY();
                
                // Player bounding box for collision
                int playerLeft_px = playerX_px;
                int playerRight_px = playerX_px + hitboxW - 1;
                int playerTop_px = playerY_px;
                int playerBottom_px = playerY_px + hitboxH - 1;
                
                for (int _si = 0; _si < SimulatorInteractionSpriteCount; _si++)
                {
                    int idx = SimulatorInteractionSpriteIndex(_si);
                    int sid = SimulatorInteractionSpriteId(idx);
                    if (sid != 0x22) continue; // Only dual portal
                    
                    // Check if already activated
                    if (!forcePlatformer && processedMiniPortals.Contains(idx)) continue;
                    
                    // Check for collision
                    if (SpriteIntersectsPlayer(idx, sid, playerLeft_px, playerRight_px, playerTop_px, playerBottom_px))
                    {
                        // Activate dual mode
                        dual = true;
                        twoplayer = false; // Single mode to start dual
                        
                        // Save player 1 state to arrays
                        player_x_fixed[0] = playerX_fixed;
                        player_y_fixed[0] = playerY_fixed;
                        player_vel_y_fixed[0] = playerVelY_fixed;
                        player_mini[0] = miniMode;
                        player_gravity[0] = currplayer_gravity;
                        
                        // Spawn player 2 with inverted gravity and negated Y velocity
                        player_x_fixed[1] = playerX_fixed;
                        player_y_fixed[1] = playerY_fixed;
                        player_vel_y_fixed[1] = -playerVelY_fixed;
                        player_mini[1] = miniMode;
                        player_gravity[1] = (byte)(currplayer_gravity ^ 0xFF);
                        
                        // NES preserves dormant P2 speed, slope state, and
                        // mode-specific flags across a single-player section.
                        
                        // Initialize player 2 rotation state (start upright, same as P1 portal entry)
                        player_cubeRotate[1] = cubeRotate_fixed;
                        player_cubeRotateMini[1] = cubeRotateMini_fixed;
                        player_shipRotate[1] = shipRotate_fixed;
                        player_swingRotate[1] = swingcopterRotate_fixed;
                        player_footballRotate[1] = footballRotate_fixed;
                        
                        AppendSimDebug($"[DUAL_PORTAL] Activated! Player 2 spawned: X={player_x_fixed[1]>>8} Y={player_y_fixed[1]>>8} velY={player_vel_y_fixed[1]:X4} gravity={player_gravity[1]:X2}");
                        
                        // NES spcl_dual_pt: target_scroll_y = portal_y - PORTAL_TO_TOP_DIFF
                        // Always set regardless of dual/twoplayer (unlike gamemode portals).
                        try
                        {
                            int storageTileY = idx / mapWidth;
                            int groundRowsLocal = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                            int portalWorldY_px = IsSimulatorNesRawDispatchIndex(idx)
                                ? SimulatorNesDispatchWorldY()
                                : (storageTileY - groundRowsLocal) * TILE;
                            targetCameraY_fixed = NesNtCameraTarget_fixed(portalWorldY_px);
                            AppendSimDebug($"[DUAL_PORTAL] Set targetCameraY={targetCameraY_fixed >> 8}px from portal at tileY={storageTileY}");
                        }
                        catch { }

                        // Mark as activated
                        processedMiniPortals.Add(idx);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                AppendSimDebug($"[DUAL PORTAL] Error: {ex.Message}");
            }
        }

        private void CheckSinglePortal()
        {
            try
            {
                if (!dual) return; // Only in dual mode
                
                // Check only the current player's collision with single portal.
                // The sync is DEFERRED until after P2 runs physics for the current
                // frame (matching PF behavior where P2's StepFrame runs fully before
                // the caller syncs P1 to P2's post-physics state).
                int checkPlayer = currplayer;
                
                // NES: Generic.x = high_byte(currplayer_x) + 1
                int playerX_px = (player_x_fixed[checkPlayer] >> 8) + 1;
                int playerY_px = player_y_fixed[checkPlayer] >> 8;
                
                // NES hitbox: CUBE_WIDTH x CUBE_HEIGHT
                int hitboxW = miniMode ? 8 : 15;
                int hitboxH = miniMode ? 7 : 15;
                playerY_px += GetMiniSpriteOffsetY();
                
                // Player bounding box for collision
                int playerLeft_px = playerX_px;
                int playerRight_px = playerX_px + hitboxW - 1;
                int playerTop_px = playerY_px;
                int playerBottom_px = playerY_px + hitboxH - 1;
                
                for (int _si = 0; _si < SimulatorInteractionSpriteCount; _si++)
                {
                    int idx = SimulatorInteractionSpriteIndex(_si);
                    int sid = SimulatorInteractionSpriteId(idx);
                    if (sid != 0x23) continue; // Only single portal
                    
                    // Check if already activated
                    if (!forcePlatformer && processedMiniPortals.Contains(idx)) continue;
                    
                    // Check for collision
                    if (SpriteIntersectsPlayer(idx, sid, playerLeft_px, playerRight_px, playerTop_px, playerBottom_px))
                    {
                        // NES spcl_sngl_pt: dual=0, player_y[0]=currplayer_y,
                        // player_gravity[0]=currplayer_gravity, player_vel_y[0]=currplayer_vel_y.
                        // Whichever player hits the portal copies its state to player_*[0].

                        // Set target_scroll_y from the portal's Y position (matching dual portal behavior)
                        try
                        {
                            int storageTileY = idx / mapWidth;
                            int groundRowsLocal = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                            int portalWorldY_px = IsSimulatorNesRawDispatchIndex(idx)
                                ? SimulatorNesDispatchWorldY()
                                : (storageTileY - groundRowsLocal) * TILE;
                            targetCameraY_fixed = NesNtCameraTarget_fixed(portalWorldY_px);
                            AppendSimDebug($"[SINGLE_PORTAL] Set targetCameraY={targetCameraY_fixed >> 8}px from portal at tileY={storageTileY}");
                        }
                        catch { }

                        if (currplayer == 0)
                        {
                            // P1 hits portal: exit dual immediately.  NES sets dual=0
                            // during P1's sprite_collide; the unconditional player_*[0]
                            // save after physics means P1 keeps its own state.  P2 won't
                            // run because the dual block checks `if (dual)`.
                            dual = false;
                            _prevDualActiveForP2Path = false;
                            AppendSimDebug($"[SINGLE_PORTAL] P1 hit portal — immediate dual=false idx={idx}");
                        }
                        else
                        {
                            // P2 hits portal: NES copies currplayer's (P2's) Y/gravity/vel
                            // to player_*[0], overwriting P1's saved state.  P2 continues
                            // physics this frame.  Deferred exit sets dual=false after P2.
                            player_y_fixed[0] = playerY_fixed;
                            player_gravity[0] = currplayer_gravity;
                            player_vel_y_fixed[0] = playerVelY_fixed;
                            singlePortalExitPending = true;
                            AppendSimDebug($"[SINGLE_PORTAL] P2 hit portal — synced P2 state to P1: Y={playerY_fixed>>8} vel=0x{playerVelY_fixed:X4} grav={currplayer_gravity:X2} idx={idx}");
                        }
                        
                        processedMiniPortals.Add(idx);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                AppendSimDebug($"[SINGLE PORTAL] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Check for alphabet block collisions (S, D, H, J, F blocks)
        /// S_BLOCK (0xF9): Stops dashing, sets orbed, sets velocityY to 0
        /// D_BLOCK (0xFA): Sets dblocked (prevents wave from moving in one direction)
        /// H_BLOCK (0xF8): Sets hblocked (headbonk - causes instant ceiling ejection)
        /// J_BLOCK (0xF7): Sets jblocked and orbed (requires press instead of hold for next jump)
        /// F_BLOCK (0xF6): Sets fblocked (forces press-to-jump and flips gravity on ceiling/floor hit)
        /// </summary>
        private void CheckAlphabetBlocks()
        {
            try
            {
                // NES: Generic.x = high_byte(currplayer_x) + 1
                int playerX_px = (playerX_fixed >> 8) + 1;
                int playerY_px = NesPlayerY_px(playerY_fixed);
                
                // NES hitbox: CUBE_WIDTH x CUBE_HEIGHT
                int hitboxW = miniMode ? 8 : 15;
                int hitboxH = miniMode ? 7 : 15;
                playerY_px += GetMiniSpriteOffsetY();
                
                // Player bounding box for collision (inclusive bounds)
                int playerRight_px = playerX_px + hitboxW - 1;
                int playerTop_px = playerY_px;
                int playerBottom_px = playerY_px + hitboxH - 1;

                for (int _si = 0; _si < SimulatorInteractionSpriteCount; _si++)
                {
                    int idx = SimulatorInteractionSpriteIndex(_si);
                    int sid = SimulatorInteractionSpriteId(idx);
                    if (sid < 0) continue;
                    
                    // Check if this sprite is an alphabet block
                    bool isSBlock = (sid == 0xF9);
                    bool isDBlock = (sid == 0xFA);
                    bool isHBlock = (sid == 0xF8);
                    bool isJBlock = (sid == 0xF7);
                    bool isFBlock = (sid == 0xF6);
                    
                    if (!isSBlock && !isDBlock && !isHBlock && !isJBlock && !isFBlock) continue;
                    
                    if (SpriteIntersectsPlayer(
                        idx, sid, playerX_px, playerRight_px,
                        playerTop_px, playerBottom_px))
                    {
                        // S_BLOCK: Stop dashing, set orbed, zero velocity (sprite_loading.h line 935)
                        if (isSBlock && dashing[currplayer] != 0)
                        {
                            dashing[currplayer] = 0;
                            orbed[currplayer] = true;
                            playerVelY_fixed = 0;
                            velocityY = 0;
                            AppendSimDebug($"[S_BLOCK] Stopped dash, orbed=true, velocityY=0");
                        }
                        // D_BLOCK: Set dblocked (sprite_loading.h line 940)
                        else if (isDBlock)
                        {
                            dblocked = true;
                            AppendSimDebug($"[D_BLOCK] dblocked=true");
                        }
                        // H_BLOCK: Set hblocked (headbonk) (sprite_loading.h line 938)
                        else if (isHBlock)
                        {
                            hblocked = true;
                            AppendSimDebug($"[H_BLOCK] hblocked=true (headbonk)");
                        }
                        // J_BLOCK: Set jblocked and orbed (sprite_loading.h line 939)
                        else if (isJBlock)
                        {
                            jblocked = true;
                            orbed[currplayer] = true;
                            AppendSimDebug($"[J_BLOCK] jblocked=true, orbed=true");
                        }
                        // F_BLOCK: Set fblocked (sprite_loading.h line 941)
                        else if (isFBlock)
                        {
                            fblocked = true;
                            AppendSimDebug($"[F_BLOCK] fblocked=true");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppendSimDebug($"[ALPHABET BLOCKS] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if a sprite is a coin (0x07, 0x1A, 0x1B).
        /// </summary>
        private static bool IsCoinSprite(int sid) => SharedPhysics.IsCoinSprite(sid);

        /// <summary>
        /// Check for coin collision. Coins are collected once and disappear.
        /// </summary>
        private void CheckCoinCollision()
        {
            try
            {
                int playerX_px = (playerX_fixed >> 8) + 1;
                int playerY_px = NesPlayerY_px(playerY_fixed);

                int hitboxW = (currplayer_mini != 0) ? 8 : 15;
                int hitboxH = (currplayer_mini != 0) ? 7 : 15;

                // Apply mini mode offset matching terrain collision conventions
                playerY_px += GetMiniSpriteOffsetY();

                int playerLeft_px = playerX_px;
                int playerRight_px = playerX_px + hitboxW - 1;
                int playerTop_px = playerY_px;
                int playerBottom_px = playerY_px + hitboxH - 1;

                int groundRowsToReserve_coin = (hasGroundLayer && groundTileRows > 0)
                    ? Math.Min(3, groundTileRows)
                    : 0;

                for (int _si = 0; _si < SimulatorInteractionSpriteCount; _si++)
                {
                    int idx = SimulatorInteractionSpriteIndex(_si);
                    int sid = SimulatorInteractionSpriteId(idx);
                    if (sid < 0) continue;
                    int coinKind = SimulatorNesCoinKind(sid);
                    bool miniCoin = SharedPhysics.IsMiniCoinSprite(sid);
                    if (coinKind < 0 && !miniCoin) continue;
                    if (collectedCoins.Contains(idx)) continue;
                    bool useRawNesRecord = IsSimulatorNesRawDispatchIndex(idx);

                    // Full coins use a simple 16×16 hitbox (NES
                    // sprite_load_special_behavior returns 0x10 for SPBH coins).
                    // Mini coins are not SPBH; they use the normal NES sprite table
                    // geometry, then spcl_minicoi kills the active slot immediately.
                    int storageTileX_c = idx % mapWidth;
                    int storageTileY_c = idx / mapWidth;

                    // Apply per-position pixel offset if present
                    int pxOff_c = 0, pyOff_c = 0;
                    int anchorKey_c = -1;
                    if (!useRawNesRecord &&
                        spriteAnchors != null && spriteAnchors.TryGetValue(idx, out var anch_c))
                        anchorKey_c = anch_c.anchorTileY * mapWidth + anch_c.anchorTileX;
                    if (!useRawNesRecord &&
                        anchorKey_c >= 0 && spritePixelOffsets != null && spritePixelOffsets.TryGetValue(anchorKey_c, out var aoffsc))
                    { pxOff_c = aoffsc.offsetX; pyOff_c = aoffsc.offsetY; }
                    else if (!useRawNesRecord &&
                        spritePixelOffsets != null && spritePixelOffsets.TryGetValue(idx, out var offsc))
                    { pxOff_c = offsc.offsetX; pyOff_c = offsc.offsetY; }

                    int coinLeft = useRawNesRecord
                        ? SimulatorNesDispatchRealX()
                        : storageTileX_c * TILE + pxOff_c;
                    // NES check_spr_objects applies -1 to ALL sprite Y positions
                    // (clc;sbc intentionally subtracts 1 extra). Coins go through
                    // check_spr_objects like all sprites, so the -1 applies here too.
                    int coinTop = useRawNesRecord
                        ? SimulatorNesDispatchRealY()
                        : (storageTileY_c - groundRowsToReserve_coin) * TILE + pyOff_c - 1;
                    int coinW = 0x10;
                    int coinH = 0x10;
                    if (miniCoin)
                    {
                        int sid8 = sid & 0xFF;
                        int xOff = sid8 < sprite_x_offset.Length ? sprite_x_offset[sid8] : 0;
                        int yOff = sid8 < sprite_y_offset.Length ? sprite_y_offset[sid8] : 0;
                        coinW = sid8 < sprite_widths.Length ? sprite_widths[sid8] : 0x10;
                        coinH = sid8 < sprite_heights.Length ? sprite_heights[sid8] : 0x10;
                        if (useRawNesRecord)
                        {
                            coinLeft = SimulatorNesSaturatingOffset(coinLeft, xOff);
                            coinTop = SimulatorNesSaturatingOffset(coinTop, yOff);
                        }
                        else
                        {
                            coinLeft += xOff;
                            coinTop += yOff;
                        }
                    }
                    // NES uses exclusive bounds (edge-touching = collision): x1+w1 >= x2
                    int coinRight  = coinLeft + coinW; // exclusive
                    int coinBottom = coinTop  + coinH; // exclusive

                    bool xOv;
                    bool yOv;
                    if (useRawNesRecord)
                    {
                        int scrollX_px = SimulatorScrollX_px();
                        int playerLeft_screen_px = playerLeft_px - scrollX_px;
                        int playerTop_screen_px = playerTop_px - (cameraY_fixed >> 8);
                        xOv = SimulatorNesAxisOverlaps(
                            playerLeft_screen_px, hitboxW, coinLeft, coinW);
                        yOv = SimulatorNesAxisOverlaps(
                            playerTop_screen_px, hitboxH, coinTop, coinH);
                    }
                    else
                    {
                        xOv = !((playerRight_px + 1) < coinLeft || coinRight < playerLeft_px);
                        yOv = !((playerBottom_px + 1) < coinTop || coinBottom < playerTop_px);
                    }

                    if (xOv && yOv)
                    {
                        if (useRawNesRecord)
                        {
                            if (miniCoin)
                            {
                                collectedCoins.Add(idx);
                                collectedCoinInfo.Add((idx, sid));
                                simulatorNesSlotDead[simulatorNesDispatchSlot] = true;
                            }
                            else if (simulatorCoinTimer[coinKind] == 0)
                            {
                                if (IsRegularSimulatorNesCoin(sid))
                                {
                                    collectedCoins.Add(idx);
                                    collectedCoinInfo.Add((idx, sid));
                                }
                                simulatorCoinTimer[coinKind] = 1;
                                simulatorCoinSpeed[coinKind] = 0x0200;
                                simulatorCoinAnimating = true;
                            }
                        }
                        else
                        {
                            collectedCoins.Add(idx);
                            collectedCoinInfo.Add((idx, sid));
                            sprites[idx] = -1;
                        }
                        AppendSimDebug($"[COIN] Collected coin 0x{sid:X2} at sprite index {idx}");
                    }
                }
            }
            catch (Exception ex)
            {
                AppendSimDebug($"[COIN] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// Check for pad collision and apply velocity change
        /// Pads: 0x0A/0x0C = yellow pad (down/up), 0x25/0x26 = pink pad (down/up), 0x52/0x53 = red pad (down/up), 0x65 = green pad expanded
        /// </summary>
        private void CheckPadCollision()
        {
            try
            {
                // In famidash sprite_collide(), Generic.x = high_byte(currplayer_x) + 1
                // This means player X is offset +1 pixel to the RIGHT for sprite collision!
                int playerX_px = (playerX_fixed >> 8) + 1;
                if (currentGameMode == 4) AppendSimDebug($"[ROBOT_PAD_CHECK] Frame: playerX_px={playerX_px}, playerY_px={NesPlayerY_px(playerY_fixed)}");
                int playerY_px = NesPlayerY_px(playerY_fixed);
                
                // Use actual collision hitbox size (15x15 for normal, 8x7 for mini)
                int hitboxW = (currplayer_mini != 0) ? 8 : 15;
                int hitboxH = (currplayer_mini != 0) ? 7 : 15;
                
                playerY_px += GetMiniSpriteOffsetY();
                
                // Player bounding box for collision
                int playerLeft_px = playerX_px;
                int playerRight_px = playerX_px + hitboxW - 1;
                int playerTop_px = playerY_px;
                int playerBottom_px = playerY_px + hitboxH - 1;
                
                // Iterate through ALL sprites and check for pads
                for (int _si = 0; _si < SimulatorInteractionSpriteCount; _si++)
                {
                    int idx = SimulatorInteractionSpriteIndex(_si);
                    int sid = SimulatorInteractionSpriteId(idx);
                    if (sid < 0) continue;
                    
                    // Check if this sprite is a pad
                    // Yellow pads: 0x0A (down), 0x0C (up)
                    // Pink pads: 0x25 (down), 0x26 (up)
                    // Red pads: 0x52 (down), 0x53 (up)
                    // Green pad: 0x65 (expanded, 2 tiles tall)
                    int padRow = -1;
                    bool isDownPad = false;
                    
                    if (sid == 0x0A || sid == 0x0C)
                    {
                        padRow = 1; // yellow pad
                        isDownPad = (sid == 0x0A);
                    }
                    else if (sid == 0x25 || sid == 0x26)
                    {
                        padRow = 3; // pink pad
                        isDownPad = (sid == 0x25);
                    }
                    else if (sid == 0x52 || sid == 0x53)
                    {
                        padRow = 8; // red pad
                        isDownPad = (sid == 0x52);
                    }
                    else if (sid == 0x65)
                    {
                        // Green pad reverses gravity (one-time activation like orbs)
                        // Check if already activated
                        if (orbActivated.ContainsKey(idx) && orbActivated[idx])
                            continue;
                        
                        // Check collision with player BEFORE activating
                        if (!SpriteIntersectsPlayer(idx, sid, playerLeft_px, playerRight_px, playerTop_px, playerBottom_px))
                            continue;
                        
                        // Toggle gravity
                        if (!gravityReversed)
                        {
                            gravityReversed = true;
                            currplayer_gravity = 0xFF;
                            gravityFlipped = true;
                            AppendSimDebug($"[GREEN_PAD] REVERSE ACTIVATED at idx {idx}");
                        }
                        else
                        {
                            gravityReversed = false;
                            currplayer_gravity = 0x00;
                            gravityFlipped = false;
                            AppendSimDebug($"[GREEN_PAD] NORMAL ACTIVATED at idx {idx}");
                        }
                        
                        // Update effective gravity
                        UpdateEffectiveGravity();
                        
                        // Update UI
                        try { Dispatcher?.BeginInvoke(new Action(() => { UpdatePlayerIconFlip(); InvertedCheckBox.IsChecked = gravityReversed; })); } catch { }
                        
                        // NES pad_stuff() calls clear_slope_stuff() before applying velocity
                        ClearSlopeStuff();
                        
                        // Apply yellow orb velocity (padRow 0)
                        int modeCol = currentGameMode;
                        if (modeCol == 8) modeCol = 0; // Ninja uses cube values
                        if (modeCol == 9) modeCol = 7; // Pogo uses Swingcopter values
                        if (modeCol == 11) modeCol = 0; // Football uses cube values
                        if (modeCol >= 0 && modeCol <= 11)
                        {
                            bool isMini = (currplayer_mini != 0);
                            int baseVel = isMini 
                                ? PadOrbHeights_Mini[0][modeCol]  // Yellow orb row = 0
                                : PadOrbHeights[0][modeCol];
                            
                            // After gravity flip: launch against new gravity direction
                            int gravityMultiplier = gravityReversed ? 1 : -1;
                            int newVel = baseVel * gravityMultiplier;
                            
                            playerVelY_fixed = newVel;
                            orbhitonthisframe[currplayer] = true;
                            
                            AppendSimDebug($"[GREEN_PAD] Applied yellow orb velocity: baseVel=0x{baseVel:X4}, newVel=0x{newVel:X4}, gravityReversed={gravityReversed}");
                        }
                        
                        // Mark as activated
                        orbActivated[idx] = true;
                        
                        // Skip normal pad velocity logic
                        continue;
                    }
                    else
                    {
                        continue; // Not a pad
                    }
                    
                    // Use standard AABB collision for pads
                    if (SpriteIntersectsPlayer(idx, sid, playerLeft_px, playerRight_px, playerTop_px, playerBottom_px))
                    {
                        if (currentGameMode == 4) AppendSimDebug($"[ROBOT_PAD_HIT] sid=0x{sid:X2}, idx={idx}, padRow={padRow}");
                        // In famidash, pad activation tracking (idx8_inc(activesprites_activated, index)) is commented out
                        // This means pads activate EVERY frame while the player is touching them
                        // This is the correct behavior - don't add activation tracking for pads
                        
                        // NES pad_stuff() calls clear_slope_stuff() before applying velocity
                        // This prevents residual slope exit velocity from corrupting the pad velocity
                        ClearSlopeStuff();
                        
                        // Get pad position for debugging
                        int padTileX = idx % mapWidth;
                        int padTileY = idx / mapWidth;
                        int padWorldX = padTileX * TILE;
                        int padWorldY = padTileY * TILE;
                        
                        AppendSimDebug($"[PAD_PRE] Frame overlap detected! sid=0x{sid:X2}, padRow={padRow}, velY_before=0x{playerVelY_fixed:X4}");
                        AppendSimDebug($"[PAD_COLLISION] Player: L={playerLeft_px} R={playerRight_px} T={playerTop_px} B={playerBottom_px}, Pad: tileX={padTileX} tileY={padTileY} worldX={padWorldX} worldY={padWorldY}");
                        
                        // Get velocity from table based on game mode and mini state
                        int modeCol = currentGameMode;
                        if (modeCol == 8) modeCol = 0; // Ninja uses cube values
                        if (modeCol == 9) modeCol = 7; // Pogo uses Swingcopter values
                        if (modeCol == 11) modeCol = 0; // Football uses cube values
                        if (modeCol >= 0 && modeCol <= 11 && padRow >= 0 && padRow < 9)
                        {
                            bool isMini = (currplayer_mini != 0);
                            int baseVel = isMini 
                                ? PadOrbHeights_Mini[padRow][modeCol] 
                                : PadOrbHeights[padRow][modeCol];
                            
                            // Apply gravity direction
                            bool gravityInverted = (currplayer_gravity != 0);
                            
                            // Yellow/pink/red pads: always launch in gravity-opposite direction
                            // Normal gravity: launch upward (negative velocity)
                            // Inverted gravity: launch downward (positive velocity)
                            // The sprite orientation (up/down) doesn't matter for these pads
                            int gravityMultiplier = gravityInverted ? 1 : -1;
                            int newVel = baseVel * gravityMultiplier;
                            
                            playerVelY_fixed = newVel;
                            orbhitonthisframe[currplayer] = true; // Signal that a pad/orb was activated this frame (prevents bounce in Pogo)
                            
                            AppendSimDebug($"[PAD_SET] sid=0x{sid:X2}, row={padRow}, mode={modeCol}, mini={isMini}, baseVel=0x{baseVel:X4}, newVel=0x{newVel:X4}, velY_after=0x{playerVelY_fixed:X4}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppendSimDebug($"[PAD COLLISION] Error: {ex.Message}");
            }
        }
        
        private void CheckSpiderOrbPadCollision()
        {
            try
            {
                // Spider orbs/pads work in ALL game modes (they switch you to spider)
                
                // NES: Generic.x = high_byte(currplayer_x) + 1
                int playerX_px = (playerX_fixed >> 8) + 1;
                int playerY_px = NesPlayerY_px(playerY_fixed);
                
                // sprite_collide uses WAVE_WIDTH/HEIGHT only for wave. Snake
                // uses the cube sprite box, despite using 8x8 for background
                // collision later in x_movement.
                bool wave = currentGameMode == 6;
                int hitboxWidth = wave ? 8 : (miniMode ? 8 : 15);
                int hitboxHeight = wave ? 8 : (miniMode ? 7 : 15);

                playerY_px += wave ? 4 : GetMiniSpriteOffsetY();
                
                // Player bounding box for collision
                int playerLeft_px = playerX_px;
                int playerRight_px = playerX_px + hitboxWidth - 1;
                int playerTop_px = playerY_px;
                int playerBottom_px = playerY_px + hitboxHeight - 1;
                
                bool holdingJump = IsXDownAsync() || keyXHeld;
                int pressCount = Interlocked.CompareExchange(ref keyXPressedCount, 0, 0);
                bool pressedJump = pressCount > 0;
                
                int spiderOrbPadCount = 0;
                
                // Iterate through ALL sprites and check for spider orbs/pads
                for (int _si = 0; _si < SimulatorInteractionSpriteCount; _si++)
                {
                    int idx = SimulatorInteractionSpriteIndex(_si);
                    int sid = SimulatorInteractionSpriteId(idx);
                    if (sid < 0) continue;
                    
                    // Spider orb up: 0x54, Spider orb down: 0x55
                    // Spider pad up: 0x56, Spider pad down: 0x57
                    bool isSpiderOrbUp = (sid == 0x54);
                    bool isSpiderOrbDown = (sid == 0x55);
                    bool isSpiderPadUp = (sid == 0x56);
                    bool isSpiderPadDown = (sid == 0x57);
                    
                    if (!isSpiderOrbUp && !isSpiderOrbDown && !isSpiderPadUp && !isSpiderPadDown)
                        continue;
                    
                    spiderOrbPadCount++;
                    
                    // Check if already activated (ONLY for orbs, not pads - pads can trigger multiple times)
                    // Per-player tracking: each player can independently activate the same orb
                    bool isOrb = (isSpiderOrbUp || isSpiderOrbDown);
                    if (isOrb && !forcePlatformer && playerProcessedOrbs[currplayer].Contains(idx))
                        continue;
                    
                    // Use CheckOrbCollision for more reliable detection (same as regular orbs)
                    bool intersects = false;
                    try
                    {
                        intersects = CheckOrbCollision(idx, sid, playerLeft_px, playerRight_px, playerTop_px, playerBottom_px);
                    }
                    catch (Exception ex)
                    {
                        AppendSimDebug($"[SPIDER_ORB/PAD] CheckOrbCollision error for 0x{sid:X2} at idx {idx}: {ex.Message}");
                        continue;
                    }
                    
                    if (intersects)
                    {
                        AppendSimDebug($"[SPIDER_ORB/PAD] *** COLLISION DETECTED *** with 0x{sid:X2} at idx {idx}, isOrb={isOrb}, holdJump={holdingJump}, pressJump={pressedJump}");
                        
                        bool shouldActivate = false;
                        
                        // NES spider orbs require a fresh press or the cube_data&2-style
                        // buffered airborne press. A plain held input must not fire one.
                        bool bufferedJump = holdingJump && orbBufferActive[currplayer] && !orbHoldSuppressing[currplayer];
                        if (isOrb && (pressedJump || bufferedJump))
                        {
                            shouldActivate = true;
                        }
                        else if (!isOrb) // Pads
                        {
                            shouldActivate = true;
                        }
                        
                        if (shouldActivate)
                        {
                            AppendSimDebug($"[SPIDER_ORB/PAD] ACTIVATING 0x{sid:X2}");
                            
                            // NES sprite_gamemode_main() calls clear_slope_stuff() before spider orb/pad activation
                            ClearSlopeStuff();
                            
                            int groundRowsToReserve = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                            bool isMini = (currplayer_mini != 0);
                            int hitboxW = isMini ? 8 : 15;
                            int hitboxH = isMini ? 7 : 15;
                            int hitboxOffsetY = isMini ? ((0x10 - hitboxH) >> 1) : 0;
                            
                            if (isSpiderOrbUp || isSpiderPadUp)
                            {
                                // Teleport upward (flip to ceiling)
                                AppendSimDebug($"[SPIDER_ORB/PAD] Teleporting UP");
                                
                                // NES consumes the persistent eject_D global here; it does
                                // not perform a new floor collision in the sprite handler.
                                playerY_fixed = (((playerY_fixed >> 8) - eject_D) << 8) | (playerY_fixed & 0xFF);
                                AppendSimDebug($"[SPIDER_ORB/PAD] Applied stale eject_D={eject_D}");
                                playerVelY_fixed = 0;
                                
                                // Flip gravity
                                currplayer_gravity = 0xFF; // GRAVITY_UP
                                gravityReversed = true;
                                gravityFlipped = true;
                                wasZeroedByCollisionLastFrame = false;  // Reset flag on gravity flip
                                UpdateCurrplayerTableIdx_Fresh();
                                
                                // Scan upward for ceiling
                                // NES spider_up_wait() scans in 8px steps, bg_coll_U_spider()
                                // detects collision and sets eject_U. The scan applies the eject
                                // internally to position player at ceiling surface. The NES orb
                                // handler's "high_byte(y) -= eject_U" uses the SAME eject_U
                                // already consumed by the scan, so no second eject is needed.
                                SpiderUpWait_Fresh(playerXBias: 1);
								if (deathTriggered)
									return;
                                playerVelY_fixed = 0;
                                
                                // Set orbed flag
                                orbed[currplayer] = true;
                                
                                try { Dispatcher?.BeginInvoke(new Action(() => UpdatePlayerIconFlip())); } catch { }
                                AppendSimDebug($"[SPIDER_ORB/PAD] Teleported to ceiling Y={playerY_fixed >> 8}");
                            }
                            else // Down
                            {
                                // Teleport downward (flip to floor)
                                AppendSimDebug($"[SPIDER_ORB/PAD] Teleporting DOWN");
                                
                                // NES likewise uses the persistent signed eject_U value.
                                playerY_fixed = (((playerY_fixed >> 8) - (eject_U + 1)) << 8) | (playerY_fixed & 0xFF);
                                AppendSimDebug($"[SPIDER_ORB/PAD] Applied stale eject_U={eject_U} (+1)");
                                playerVelY_fixed = 0;
                                
                                // Flip gravity
                                currplayer_gravity = 0x00; // GRAVITY_DOWN
                                gravityReversed = false;
                                gravityFlipped = false;
                                wasZeroedByCollisionLastFrame = false;  // Reset flag on gravity flip
                                UpdateCurrplayerTableIdx_Fresh();
                                
                                // Scan downward for floor
                                // Same as UP: scan already positions player at floor surface.
                                SpiderDownWait_Fresh(playerXBias: 1);
								if (deathTriggered)
									return;
                                playerVelY_fixed = 0;
                                
                                // Set orbed flag
                                orbed[currplayer] = true;
                                
                                try { Dispatcher?.BeginInvoke(new Action(() => UpdatePlayerIconFlip())); } catch { }
                                AppendSimDebug($"[SPIDER_ORB/PAD] Teleported to floor Y={playerY_fixed >> 8}");
                            }
                            
                            // Mark orb as activated (pads don't get marked - they can trigger multiple times)
                            if (isOrb)
                            {
                                playerProcessedOrbs[currplayer].Add(idx);
                                if (!dual) orbActivated[idx] = true;
                                orbBufferActive[currplayer] = false;
                                ballInputBufferCountdown[currplayer] = 0;
                                orbHoldConsumedKeyStillDown[currplayer] = holdingJump;
                            }
                            
                            // Consume press if it was an orb activation
                            if (isOrb && pressedJump)
                            {
                                Interlocked.Exchange(ref keyXPressedCount, 0);
                            }
                            
                            return; // Only one per frame
                        }
                        else
                        {
                            AppendSimDebug($"[SPIDER_ORB/PAD] Did NOT activate - isOrb={isOrb}, pressJump={pressedJump}, holdJump={holdingJump}");
                        }
                    }
                }
                
                if (spiderOrbPadCount > 0)
                {
                    AppendSimDebug($"[SPIDER_ORB/PAD] Scanned {spiderOrbPadCount} spider orbs/pads in level");
                }
            }
            catch (Exception ex)
            {
                AppendSimDebug($"[SPIDER_ORB/PAD COLLISION] Error: {ex.Message}");
            }
        }

        // Helper: returns true when the player's head is overlapping a blocking ceiling
        // in the current world position. This mirrors the ceiling-collision check used
        // in the numeric integration path but does not modify player state.
        private bool IsTouchingCeiling()
        {
            try
            {
                const int HITBOX_W_LOCAL = 15;
                int playerCenter_px_local = (playerX_fixed >> 8) + (playerVisualWidth / 2);
                int playerLeft_px_local = playerCenter_px_local - (HITBOX_W_LOCAL / 2);
                int playerRight_px_local = playerLeft_px_local + (HITBOX_W_LOCAL - 1);
                int headWorldY_px_local = (playerY_fixed >> 8); // player's top

                int tileAboveY_world = headWorldY_px_local / TILE;
                int groundRowsToReserve_local = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                int tileIndexY = tileAboveY_world + groundRowsToReserve_local;

                if (tileIndexY < 0 || tileIndexY >= mapHeight) return false;

                for (int tx_local = playerLeft_px_local / TILE; tx_local <= playerRight_px_local / TILE; tx_local++)
                {
                    if (tx_local < 0 || tx_local >= mapWidth) continue;
                    int tid_local = tiles[tileIndexY * mapWidth + tx_local];
                    int useTidForAnim_local = MapAnimatedTileIndex(tid_local);
                    int collisionTid_local = useTidForAnim_local;
                    if (useTidForAnim_local >= 1000)
                    {
                        if (useTidForAnim_local >= 1000 && useTidForAnim_local <= 1007)
                        {
                            collisionTid_local = 0x08 + ((useTidForAnim_local - 1000) % 4);
                        }
                        else if (useTidForAnim_local >= 1010 && useTidForAnim_local <= 1015)
                        {
                            int group_local = (useTidForAnim_local - 1010) % 3;
                            collisionTid_local = (group_local == 0) ? 0x04 : (group_local == 1) ? 0x7D : 0x7F;
                        }
                        else if (useTidForAnim_local >= 1020 && useTidForAnim_local <= 1037)
                        {
                            collisionTid_local = 0x74 + ((useTidForAnim_local - 1020) % 9);
                        }
                        else
                        {
                            collisionTid_local = tid_local;
                        }
                    }
                    var col_local = MetatileCollisionTable.GetCollision((byte)collisionTid_local);

                    int tileStartX_local = tx_local * TILE;
                    int localLeft_local = Math.Max(0, playerLeft_px_local - tileStartX_local);
                    int localRight_local = Math.Min(TILE - 1, playerRight_px_local - tileStartX_local);

                    for (int lx_local = localLeft_local; lx_local <= localRight_local; lx_local++)
                    {
                        // Note: here we respect pass-through semantics elsewhere by default.
                        // This function is the permissive check used by most systems; for
                        // jump eligibility we also provide a strict variant below.
                        int worldPx = tileStartX_local + lx_local;
                        int playerCenter_px = (playerX_fixed >> 8) + (playerVisualWidth / 2);
                        if (!MainWindow.Option_NoDeath)
                        {
                            if (worldPx >= playerCenter_px) continue;
                        }
                        if (BlocksCeilingAtColumn(col_local, lx_local)) return true;
                    }
                }

                return false;
            }
            catch { return false; }
        }

        // Strict variant of ceiling check that ignores right-side pass-through.
        // Used for jump eligibility so a player who is overlapping a blocking
        // ceiling can still jump even when right-side pass-through is enabled.
        private bool IsTouchingCeilingStrict()
        {
            try
            {
                const int HITBOX_W_LOCAL = 15;
                int playerCenter_px_local = (playerX_fixed >> 8) + (playerVisualWidth / 2);
                int playerLeft_px_local = playerCenter_px_local - (HITBOX_W_LOCAL / 2);
                int playerRight_px_local = playerLeft_px_local + (HITBOX_W_LOCAL - 1);
                int headWorldY_px_local = (playerY_fixed >> 8); // player's top

                int tileAboveY_world = headWorldY_px_local / TILE;
                int groundRowsToReserve_local = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                int tileIndexY = tileAboveY_world + groundRowsToReserve_local;

                if (tileIndexY < 0 || tileIndexY >= mapHeight) return false;

                for (int tx_local = playerLeft_px_local / TILE; tx_local <= playerRight_px_local / TILE; tx_local++)
                {
                    if (tx_local < 0 || tx_local >= mapWidth) continue;
                    int tid_local = tiles[tileIndexY * mapWidth + tx_local];
                    int useTidForAnim_local = MapAnimatedTileIndex(tid_local);
                    int collisionTid_local = useTidForAnim_local;
                    if (useTidForAnim_local >= 1000)
                    {
                        if (useTidForAnim_local >= 1000 && useTidForAnim_local <= 1007)
                        {
                            collisionTid_local = 0x08 + ((useTidForAnim_local - 1000) % 4);
                        }
                        else if (useTidForAnim_local >= 1010 && useTidForAnim_local <= 1015)
                        {
                            int group_local = (useTidForAnim_local - 1010) % 3;
                            collisionTid_local = (group_local == 0) ? 0x04 : (group_local == 1) ? 0x7D : 0x7F;
                        }
                        else if (useTidForAnim_local >= 1020 && useTidForAnim_local <= 1037)
                        {
                            collisionTid_local = 0x74 + ((useTidForAnim_local - 1020) % 9);
                        }
                        else
                        {
                            collisionTid_local = tid_local;
                        }
                    }
                    var col_local = MetatileCollisionTable.GetCollision((byte)collisionTid_local);

                    int tileStartX_local = tx_local * TILE;
                    int localLeft_local = Math.Max(0, playerLeft_px_local - tileStartX_local);
                    int localRight_local = Math.Min(TILE - 1, playerRight_px_local - tileStartX_local);

                    // Compute the local Y within the tile corresponding to the player's head
                    int tileStartY_local = tileAboveY_world * TILE;
                    int localY_local = Math.Max(0, Math.Min(TILE - 1, headWorldY_px_local - tileStartY_local));

                    for (int lx_local = localLeft_local; lx_local <= localRight_local; lx_local++)
                    {
                        // Use per-pixel occupancy so top-half slabs (COL_TOP etc.) are
                        // considered blocking for the strict ceiling check used by jump
                        // eligibility. This treats any occupied pixel at the player's
                        // head Y as a blocking ceiling regardless of pass-through semantics.
                        try
                        {
                            if (TileOccupiesPixel(col_local, lx_local, localY_local)) return true;
                        }
                        catch { if (BlocksCeilingAtColumn(col_local, lx_local)) return true; }
                    }
                }

                return false;
            }
            catch { return false; }
        }

        // Parallax / ground data passed from the editor so simulator can mirror preview-mode
        private ImageSource?[]? parallaxImages;
        private ImageSource?[]? parallaxTonedImages;
        private ImageSource? parallaxBitmap = null;
        private ImageSource? parallaxBitmapToned = null;
        private double parallaxX = 1.0;
        private double parallaxY = 1.0;
        private bool parallaxRepeatX = true;
        private bool parallaxRepeatY = true;
        private bool hasParallaxLayer = false;

        private ImageSource?[]? groundImages;
        private ImageSource?[]? groundTonedImages;
        private double groundOffsetY = 0.0;
        private bool groundRepeatX = true;
        private bool hasGroundLayer = false;
        private int groundTileRows = 0;
        // Fixed-point camera X with 8 fractional bits
        private int cameraX_fixed = 0;
        // cameraY in fixed-point (8 fractional bits)
        private int cameraY_fixed = 0; // pixels * 256

        // speed per frame (0x02C4 fixed point with 8 fractional bits)
        private const int SPEED_FIXED = 0x02C4;
        // Dynamic current speed (fixed-point, 8 fractional bits). Starts at 1x.
        private int currentSpeed_fixed = 0x02C4;

        // Speed constants (fixed-point values, 8 fractional bits)
        private const int CUBE_SPEED_X05 = 0x23B;
        private const int CUBE_SPEED_X1  = 0x02C4;
        private const int CUBE_SPEED_X2  = 0x0371;
        private const int CUBE_SPEED_X3  = 0x0429;
        private const int CUBE_SPEED_X4  = 0x051E;
        private const int CUBE_SPEED_SLOW = 0x16E; // 0.1x special speed

        // Simple cube physics (fixed-point, 8 fractional bits)
        // Max downward velocity (fixed-point). Default 0x0600 (0x06 << 8).
        // This is now a runtime-configurable field so simulator can reflect level settings.
        private int CUBE_MAX_FALLSPEED = 0x600; // max downward velocity
        private const int CUBE_GRAVITY = 0x6B; // gravity added per frame
        private const int CUBE_JUMP_VEL = -0x590; // jump impulse (negative = upward)
        // Robot mode constants
        private const int ROBOT_JUMP_VEL = -0x2B0;
        // UFO mode constants (allow mid-air pulses / different gravity)
        private const int UFO_GRAVITY = 0x0032;
        private const int UFO_MAX_FALLSPEED = 0x0320;
        private const int UFO_JUMP_VEL = -0x0330;
        // Ship physics constants (fixed-point, 8 fractional bits)
        // Values provided by user in high-byte pixel / low-byte subpixel format
        private const int SHIP_MAX_FALLSPEED = 0x0369;
        private const int SHIP_MAX_FALLSPEED_HOLD = 0x0443;
        // Ball mode constants
        private const int BALL_GRAVITY = 0x0066;
        private const int BALL_MAX_FALLSPEED = 0x0733;
        private const int BALL_IMMEDIATE_VEL = 0x0266;
        private const int SHIP_GRAVITY_BASE = 0x003C;
        private const int SHIP_GRAVITY = 0x0030;
        private const int SHIP_GRAVITY_AFTER_HOLD = 0x0049;
        private const int SHIP_GRAVITY_HOLD_FALL = 0x004C;

        // Current player game mode: 0 = cube, 1 = ship, etc. Defaults to cube.
        private int currentGameMode = 0;
        // Level settings starting game mode (set once in constructor, used on restart)
        private int _levelStartGameMode = 0;
        
        // Unified physics state variables (shared across all modes)
        private int velocityY = 0;         // Vertical velocity (8.8 fixed point)
#pragma warning disable CS0414
        private int velocityX = 0x0300;    // Horizontal velocity (for wave mode)
#pragma warning restore CS0414
        private bool gravityFlipped = false; // True = up, False = down  
        private bool miniMode = false;     // Mini mode flag
        private int speed = 1;             // Speed mode: 0=0.5x, 1=1x, 2=2x, 3=3x, 4=4x, 5=0.1x
#pragma warning disable CS0414
        private bool previousJumpState = false; // Track jump button for press detection
#pragma warning restore CS0414
        
        // Runtime-effective physics values (adjusted when gravity is reversed)
        private int effectiveGravity_fixed;
        private int effectiveJumpVel_fixed;
        private int effectiveMaxFall_fixed;
        private bool gravityReversed = false;
        private double gravityMultiplier = 1.0;  // Gravity modifier from portals 0x5F-0x63 (1/3, 1/2, 2/3, 2x, 1x)
        // NOTE: `gravityReversed` is the canonical logical gravity direction used
        // by collision/landing logic. A separate flag `effectiveInvertedByW`
        // allows the editor's No-Death option to invert numeric physics (gravity,
        // jump, max-fall) without changing collision semantics. Numeric inversion
        // is computed as `gravityReversed || effectiveInvertedByW`.
        private bool effectiveInvertedByW = false;
        
#pragma warning disable CS0414
        // Track if gravity was flipped THIS frame by W key (for unsticking Robot/Ninja from ground)
        private bool gravityFlippedThisFrame = false;
#pragma warning restore CS0414
        
        // Track gravity portals we've already activated this pass so each
        // portal activates only once per crossing.
        private System.Collections.Generic.HashSet<int> processedGravityPortals = new System.Collections.Generic.HashSet<int>();
        private System.Collections.Generic.HashSet<int> processedGravityModPortals = new System.Collections.Generic.HashSet<int>();
        private System.Collections.Generic.HashSet<int> processedGameModePortals = new System.Collections.Generic.HashSet<int>();
        private System.Collections.Generic.HashSet<int> processedMiniPortals = new System.Collections.Generic.HashSet<int>();
        // Track random portals (0x64 and 0x7E) we've already activated so each only activates once
        private System.Collections.Generic.HashSet<int> processedRandomPortals = new System.Collections.Generic.HashSet<int>();
        // Legacy camera-mode speed tracking. Physics-mode sprite collisions use
        // the NES slot ring below and deliberately re-fire speed portals.
        private System.Collections.Generic.HashSet<int> processedSpeedPortals = new System.Collections.Generic.HashSet<int>();
        private readonly int[] simulatorNesSpriteIds = Array.Empty<int>();
        private readonly NesSpriteRecord[]? simulatorNesSpriteRecords;
        private int[] simulatorNesSpriteStream = Array.Empty<int>();
        private int[] simulatorNesSpriteWorldX = Array.Empty<int>();
        private int[] simulatorNesSpriteWorldY = Array.Empty<int>();
        private readonly int[] simulatorNesSlots = new int[16];
        private readonly bool[] simulatorNesSlotDead = new bool[16];
        private readonly bool[] simulatorNesSlotActive = new bool[16];
        private readonly int[] simulatorNesSlotWorldY = new int[16];
        private readonly int[] simulatorNesSlotRealX = new int[16];
        private readonly int[] simulatorNesSlotRealY = new int[16];
        private readonly int[] simulatorCoinTimer = new int[3];
        private readonly int[] simulatorCoinSpeed = new int[3];
        private bool simulatorCoinAnimating = false;
        private int simulatorNesSpriteDataPtr = 0;
        private bool simulatorNesSlotsPrimed = false;
        private int simulatorTeleportOutputY_px = 0;
        private bool simulatorNesDispatchActive = false;
        private int simulatorNesDispatchSlot = -1;
        private int simulatorNesDispatchIndex = -1;
        private int simulatorNesDispatchSpriteId = -1;
        private readonly bool[] simulatorOrbPassComplete = new bool[2];
        private readonly bool[] simulatorOrbResultConsumed = new bool[2];
        private readonly bool[] simulatorOrbActivatedPending = new bool[2];
        private readonly bool[] simulatorSkullDeathPending = new bool[2];
        private readonly bool[] simulatorSpiderBoundaryDeathPending = new bool[2];
        private bool SimulatorUsesExactNesRecords => simulatorNesSpriteRecords != null;
        private readonly int[] simulatorOrbTypePending = new int[] { -1, -1 };

        // Gameplay collision helpers normally expose every sprite. During the
        // NES sprite_collide pass they expose exactly the current hardware slot,
        // preserving the ROM's universal slot 0 -> 15 dispatch order while
        // allowing the existing type-specific handlers to remain focused.
        private int SimulatorInteractionSpriteCount =>
            simulatorNesDispatchActive ? 1 : nonEmptySpriteIndices.Length;

        private int SimulatorInteractionSpriteIndex(int scanIndex) =>
            simulatorNesDispatchActive ? simulatorNesDispatchIndex : nonEmptySpriteIndices[scanIndex];

        private int SimulatorInteractionSpriteId(int idx) =>
            simulatorNesDispatchActive && idx == simulatorNesDispatchIndex
                ? simulatorNesDispatchSpriteId
                : sprites[idx];

        private bool IsSimulatorNesRawDispatchIndex(int idx) =>
            simulatorNesDispatchActive &&
            idx == simulatorNesDispatchIndex &&
            (uint)simulatorNesDispatchSlot < (uint)simulatorNesSlotWorldY.Length;

        private int SimulatorNesDispatchWorldY() =>
            simulatorNesSlotWorldY[simulatorNesDispatchSlot];

        private int SimulatorNesDispatchRealX() =>
            simulatorNesSlotRealX[simulatorNesDispatchSlot];

        private int SimulatorNesDispatchRealY() =>
            simulatorNesSlotRealY[simulatorNesDispatchSlot];

        private static int SimulatorNesSaturatingOffset(int coordinate, int signedOffset) =>
            Math.Clamp((coordinate & 0xFF) + signedOffset, 0, 0xFF);

        private static bool SimulatorNesAxisOverlaps(int first, int firstSize, int second, int secondSize)
        {
            first &= 0xFF;
            second &= 0xFF;
            firstSize &= 0xFF;
            secondSize &= 0xFF;

            int firstEnd = first + firstSize;
            if (firstEnd <= 0xFF && firstEnd < second)
                return false;

            int secondEnd = second + secondSize;
            if (secondEnd <= 0xFF && secondEnd < first)
                return false;

            return true;
        }

        private static bool SimulatorNesAxisOverlapsPositive(int first, int firstSize, int second, int secondSize)
        {
            first &= 0xFF;
            second &= 0xFF;
            firstSize &= 0xFF;
            secondSize &= 0xFF;

            int firstEnd = first + firstSize;
            if (firstEnd <= 0xFF && firstEnd <= second)
                return false;

            int secondEnd = second + secondSize;
            if (secondEnd <= 0xFF && secondEnd <= first)
                return false;

            return true;
        }

        private static int SimulatorNesCoinKind(int sid) => sid switch
        {
            0x07 or 0x1C => 0,
            0x1A or 0x1D => 1,
            0x1B or 0x1E => 2,
            _ => -1
        };

        private static bool IsRegularSimulatorNesCoin(int sid) =>
            sid == 0x07 || sid == 0x1A || sid == 0x1B;

        private void BeginSimulatorNesSpritePass()
        {
            simulatorOrbPassComplete[currplayer] = false;
            simulatorOrbResultConsumed[currplayer] = false;
            simulatorOrbActivatedPending[currplayer] = false;
            simulatorOrbTypePending[currplayer] = -1;
            simulatorSkullDeathPending[currplayer] = false;
            simulatorSpiderBoundaryDeathPending[currplayer] = false;
        }

        private void EndSimulatorNesSpritePass()
        {
            simulatorNesDispatchActive = false;
            simulatorNesDispatchSlot = -1;
            simulatorNesDispatchIndex = -1;
            simulatorNesDispatchSpriteId = -1;
            simulatorOrbPassComplete[currplayer] = true;
        }

        private void SelectSimulatorNesSpriteSlot(int slot)
        {
            int idx = simulatorNesSlots[slot];
            simulatorNesDispatchActive = idx >= 0 &&
                simulatorNesSlotActive[slot] &&
                !simulatorNesSlotDead[slot];
            simulatorNesDispatchSlot = simulatorNesDispatchActive ? slot : -1;
            simulatorNesDispatchIndex = simulatorNesDispatchActive ? idx : -1;
            simulatorNesDispatchSpriteId = simulatorNesDispatchActive
                ? (simulatorNesSpriteIds[idx] & 0xFF)
                : -1;
        }

        private void CheckRegularOrbCollisionNesOrder()
        {
            if (!simulatorNesDispatchActive || simulatorNesDispatchSpriteId < 0)
                return;

            int mode = currentGameMode == 9 ? 7 : currentGameMode;
            bool wave = currentGameMode == 6;
            bool isMini = currplayer_mini != 0;
            int hitboxW = wave ? 8 : (isMini ? 8 : 15);
            int hitboxH = wave ? 8 : (isMini ? 7 : 15);
            int playerX = (playerX_fixed >> 8) + 1;
            int playerY = playerY_fixed >> 8;
            playerY += wave ? 4 : GetMiniSpriteOffsetY();

            bool pressed = Interlocked.CompareExchange(ref keyXPressedCount, 0, 0) > 0;
            bool held = IsXDownAsync() || keyXHeld;
            int tempVelocityY = playerVelY_fixed;
            var result = UpdateOrbSystem(
                mode, playerX, playerY, hitboxW, hitboxH, 0,
                pressed, held, currplayer_gravity != 0, isMini,
                ref tempVelocityY);

            if (result.activated)
            {
                playerVelY_fixed = tempVelocityY;
                simulatorOrbActivatedPending[currplayer] = true;
                simulatorOrbTypePending[currplayer] = result.orbType;
                orbhitonthisframe[currplayer] = true;
            }
        }

        private void CheckSkullOrbCollisionNesOrder()
        {
            if (!simulatorNesDispatchActive || simulatorNesDispatchSpriteId != 0x79)
                return;

            bool wave = currentGameMode == 6;
            bool isMini = currplayer_mini != 0;
            int hitboxW = wave ? 8 : (isMini ? 8 : 15);
            int hitboxH = wave ? 8 : (isMini ? 7 : 15);
            int playerLeft = (playerX_fixed >> 8) + 1;
            int playerTop = (playerY_fixed >> 8) + (wave ? 4 : GetMiniSpriteOffsetY());
            if (!SpriteIntersectsPlayerTouching(
                    simulatorNesDispatchIndex, 0x79,
                    playerLeft, playerLeft + hitboxW - 1,
                    playerTop, playerTop + hitboxH - 1))
                return;

            bool pressed = pathfinderEnabled
                ? pfPressEdgeThisFrame
                : Interlocked.CompareExchange(ref keyXPressedCount, 0, 0) > 0;
            bool held = IsXDownAsync() || keyXHeld;
            // spcl_skl_orb uses cube_data bit $02 in cube, ball, robot, spider,
            // swing, ninja, pogo, snake, and later modes. Ship, UFO, and wave
            // use only a fresh press. orbBufferActive is the simulator's existing
            // representation of that buffered airborne press.
            bool usesBufferedHold = currentGameMode != 1 && currentGameMode != 3 &&
                currentGameMode != 6 && orbBufferActive[currplayer];
            if (usesBufferedHold ? held : pressed)
            {
                simulatorSkullDeathPending[currplayer] = true;
                AppendSimDebug($"[SKULL_ORB] pending death p={currplayer} " +
                    $"press={pressed} hold={held} buffered={orbBufferActive[currplayer]}");
            }
        }

        private void TriggerSimulatorDeferredDeath(string reason, int deathX, int deathY)
        {
            if (deathTriggered || MainWindow.Option_NoDeath)
                return;

            AppendSimDebug($"[DEATH] {reason} at ({deathX},{deathY})");
            deathTriggered = true;
            deathTileX = deathX;
            deathTileY = deathY;
            paused = true;
            _ = StopMusicAsync();
            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { PauseOverlay.Visibility = System.Windows.Visibility.Collapsed; } catch { }
                    if (this.Owner is MainWindow mw)
                    {
                        try { mw.PauseSimulatorPlayback(); } catch { }
                        try { mw.AddDeathMarker(deathX, deathY); } catch { }
                    }
                }));
            }
            catch { }
        }

        private void BuildSimulatorNesSpriteStream()
        {
            if (simulatorNesSpriteRecords != null)
            {
                int count = simulatorNesSpriteRecords.Length;
                simulatorNesSpriteWorldX = new int[count];
                simulatorNesSpriteWorldY = new int[count];
                simulatorNesSpriteStream = new int[count];
                for (int i = 0; i < count; i++)
                {
                    NesSpriteRecord record = simulatorNesSpriteRecords[i];
                    simulatorNesSpriteStream[i] = i;
                    simulatorNesSpriteWorldX[i] = record.X;
                    simulatorNesSpriteWorldY[i] = record.Y - _sim_nesCoordOffset;
                }
                return;
            }

            var stream = new System.Collections.Generic.List<int>();
            simulatorNesSpriteWorldX = new int[sprites.Length];
            simulatorNesSpriteWorldY = new int[sprites.Length];
            int groundRowsToReserve = (hasGroundLayer && groundTileRows > 0)
                ? Math.Min(3, groundTileRows)
                : 0;
            for (int column = 0; column < mapWidth; column++)
            {
                for (int row = 0; row < mapHeight; row++)
                {
                    int idx = row * mapWidth + column;
                    if ((uint)idx >= (uint)simulatorNesSpriteIds.Length || simulatorNesSpriteIds[idx] < 0)
                        continue;
                    stream.Add(idx);
                    int worldX = column * TILE;
                    int worldY = (row - groundRowsToReserve) * TILE;
                    if (spritePixelOffsets.TryGetValue(idx, out var offset))
                    {
                        worldX += offset.offsetX;
                        worldY += offset.offsetY;
                    }
                    simulatorNesSpriteWorldX[idx] = worldX;
                    simulatorNesSpriteWorldY[idx] = worldY;
                }
            }
            simulatorNesSpriteStream = stream.ToArray();
        }

        private void InitializeSimulatorNesSlots()
        {
            Array.Fill(simulatorNesSlots, -1);
            Array.Clear(simulatorNesSlotDead, 0, simulatorNesSlotDead.Length);
            Array.Clear(simulatorNesSlotActive, 0, simulatorNesSlotActive.Length);
            Array.Clear(simulatorNesSlotWorldY, 0, simulatorNesSlotWorldY.Length);
            Array.Clear(simulatorNesSlotRealX, 0, simulatorNesSlotRealX.Length);
            Array.Clear(simulatorNesSlotRealY, 0, simulatorNesSlotRealY.Length);
            Array.Clear(simulatorCoinTimer, 0, simulatorCoinTimer.Length);
            Array.Clear(simulatorCoinSpeed, 0, simulatorCoinSpeed.Length);
            simulatorCoinAnimating = false;
            simulatorNesSpriteDataPtr = 0;
            simulatorNesSlotsPrimed = false;
            simulatorTeleportOutputY_px = 0;
            for (int slot = 15; slot >= 0 && simulatorNesSpriteDataPtr < simulatorNesSpriteStream.Length; slot--)
            {
                simulatorNesSlots[slot] = simulatorNesSpriteStream[simulatorNesSpriteDataPtr++];
                simulatorNesSlotWorldY[slot] =
                    simulatorNesSpriteWorldY[simulatorNesSlots[slot]];
                simulatorNesSlotRealX[slot] =
                    simulatorNesSpriteWorldX[simulatorNesSlots[slot]] & 0xFF;
                simulatorNesSlotRealY[slot] =
                    simulatorNesSlotWorldY[slot] & 0xFF;
            }
        }

        private void LoadNextSimulatorNesSprite(int slot)
        {
            simulatorNesSlots[slot] = simulatorNesSpriteDataPtr < simulatorNesSpriteStream.Length
                ? simulatorNesSpriteStream[simulatorNesSpriteDataPtr++]
                : -1;
            simulatorNesSlotWorldY[slot] = simulatorNesSlots[slot] >= 0
                ? simulatorNesSpriteWorldY[simulatorNesSlots[slot]]
                : 0;
            simulatorNesSlotRealX[slot] = simulatorNesSlots[slot] >= 0
                ? simulatorNesSpriteWorldX[simulatorNesSlots[slot]] & 0xFF
                : 0;
            simulatorNesSlotRealY[slot] = simulatorNesSlotWorldY[slot] & 0xFF;
            simulatorNesSlotDead[slot] = false;
            simulatorNesSlotActive[slot] = false;
        }

        private void UpdateSimulatorNesSlots()
        {
            int scrollX_px = SimulatorScrollX_px();
            int scrollY_px = cameraY_fixed >> 8;
            for (int slot = 15; slot >= 0; slot--)
            {
                int idx = simulatorNesSlots[slot];
                if (idx < 0 || simulatorNesSlotDead[slot])
                {
                    LoadNextSimulatorNesSprite(slot);
                    continue;
                }
                int relX = simulatorNesSpriteWorldX[idx] - scrollX_px;
                simulatorNesSlotRealX[slot] = relX & 0xFF;
                if (relX < 0)
                {
                    if (IsRegularSimulatorNesCoin(simulatorNesSpriteIds[idx] & 0xFF))
                        simulatorCoinAnimating = false;
                    LoadNextSimulatorNesSprite(slot);
                    continue;
                }
                if (relX >= 256)
                {
                    simulatorNesSlotActive[slot] = false;
                    continue;
                }

                // check_spr_objects clears carry before SBC, making this
                // raw sprite Y - scroll Y - 1.
                int relY = simulatorNesSlotWorldY[slot] - scrollY_px - 1;
                simulatorNesSlotRealY[slot] = relY & 0xFF;
                bool visible = relY >= 0 && relY < 256;
                if (!visible && simulatorCoinAnimating &&
                    IsRegularSimulatorNesCoin(simulatorNesSpriteIds[idx] & 0xFF))
                    visible = true;
                simulatorNesSlotActive[slot] = visible;
            }
        }

        private void ApplySimulatorNesCameraScroll()
        {
            if (!physicsEnabled || paused || (!forcePlatformer && !jumpedOnce))
                return;

            if (forcePlatformer)
                ApplySimulatorPlatformerXScroll();

            // NES process_y_scroll runs once per gameplay frame, after P1's
            // movement/collisions and before check_spr_objects/P2.
            bool camFollowsY =
                currentGameMode == 0 ||
                currentGameMode == 4 ||
                currentGameMode == 8 ||
                currentGameMode == 9 ||
                nocamlockforced;

            if ((!dual || twoplayer) && camFollowsY)
            {
                ApplySimulatorNesCubeRobotYScroll();
                return;
            }

            // Ship-style smooth scroll. NES performs two independent comparisons,
            // so a +2 step may overshoot and immediately take the -3 branch.
            int cameraY_px = cameraY_fixed >> 8;
            int targetCameraY_px = targetCameraY_fixed >> 8;
            int maxShipCameraY_fixed = SimulatorNesMaxCameraY_px() << 8;
            if (targetCameraY_px > cameraY_px)
            {
                cameraY_fixed += SHIP_SCROLL_SPEED_UP_FIXED;
                // Simulator stores world Y. NES screen currplayer_y moves -2px
                // while scroll_y moves +2px, so world Y is unchanged.
            }

            if (cameraY_fixed <= maxShipCameraY_fixed &&
                targetCameraY_px < (cameraY_fixed >> 8))
            {
                cameraY_fixed -= SHIP_SCROLL_SPEED_DOWN_FIXED;
                // NES adds SHIP_SCROLL_SPEED to screen currplayer_y here, while
                // scroll_y moves -3px on NTSC due the carry after subtracting 0.
                // World Y therefore moves by -1px.
                playerY_fixed -= SHIP_SCROLL_SPEED_DOWN_FIXED - SHIP_SCROLL_SPEED_UP_FIXED;
                if (dual && !twoplayer && currplayer == 0)
                    player_y_fixed[1] -= SHIP_SCROLL_SPEED_DOWN_FIXED - SHIP_SCROLL_SPEED_UP_FIXED;
            }

            int minShipCameraY_fixed =
                (_sim_minScrollYLin - _sim_nesCoordOffset) << 8;
            if (cameraY_fixed < minShipCameraY_fixed)
            {
                // NES moves screen Y and scroll by the same integer clamp
                // amount; simulator Y is world-space, so only fold subpixels.
                playerY_fixed -= _sim_scrollYSubpx;
                if (dual && !twoplayer && currplayer == 0)
                    player_y_fixed[1] -= _sim_scrollYSubpx;
                _sim_scrollYSubpx = 0;
                cameraY_fixed = minShipCameraY_fixed;
            }
            if (cameraY_fixed > maxShipCameraY_fixed || (cameraY_fixed == maxShipCameraY_fixed && _sim_scrollYSubpx != 0))
            {
                // The integer bottom-cap delta is already represented by
                // clamping cameraY_fixed; only fold in the NES fractional
                // scroll byte before clearing it.
                playerY_fixed += _sim_scrollYSubpx;
                if (dual && !twoplayer && currplayer == 0)
                    player_y_fixed[1] += _sim_scrollYSubpx;
                _sim_scrollYSubpx = 0;
                cameraY_fixed = maxShipCameraY_fixed;
            }
        }

        private int SimulatorScrollX_px()
        {
            return forcePlatformer
                ? cameraX_fixed >> 8
                : Math.Max(0, (playerX_fixed >> 8) - 0x50);
        }

        private void ApplySimulatorPlatformerXScroll()
        {
            if (currXScrollStop_fixed < targetXScrollStop_fixed)
                currXScrollStop_fixed += 0x200;
            else if (currXScrollStop_fixed > targetXScrollStop_fixed)
                currXScrollStop_fixed -= 0x200;

            int playerScreenX_fixed = playerX_fixed - cameraX_fixed;
            if (playerScreenX_fixed > currXScrollStop_fixed)
            {
                int delta = (playerScreenX_fixed - currXScrollStop_fixed) >> 8;
                cameraX_fixed += delta << 8;
            }
            else if (playerScreenX_fixed < 0x0200)
            {
                // Mirrors the NES unsigned-byte subtraction in process_x_scroll.
                int delta = (playerScreenX_fixed + 0x0200) >> 8;
                cameraX_fixed -= delta << 8;
            }

            int maxCamera_fixed = Math.Max(0, (mapWidth - NES_W) * TILE) << 8;
            if (cameraX_fixed < 0) cameraX_fixed = 0;
            if (cameraX_fixed > maxCamera_fixed) cameraX_fixed = maxCamera_fixed;
        }

        private int ResolveSimulatorPlatformerHorizontal(int oldX_fixed,
            int movementSpeed_fixed, sbyte direction, out bool lethal)
        {
            lethal = false;
            direction = direction < 0 ? (sbyte)-1 : direction > 0 ? (sbyte)1 : (sbyte)0;

            int hitboxW = (currentGameMode == 6 || currentGameMode == 10)
                ? 8 : (currplayer_mini != 0 ? 8 : 15);
            int hitboxH = (currentGameMode == 6 || currentGameMode == 10)
                ? 8 : (currplayer_mini != 0 ? 7 : 15);
            int playerX_px = oldX_fixed >> 8;
            int playerY_px = NesPlayerBgCollisionY_px(playerY_fixed);
            bool slopeActive = (currplayer_was_on_slope_counter | currplayer_slope_frames) != 0;

            // x_movement_coll probes right first; x_movement then probes both
            // directions even when no directional button is held.
            if (invincibleCounter == 0)
            {
                var pre = SharedPhysics.CheckPlatformerSideCollision(
                    in platformerCollisionMap, playerX_px, playerY_px,
                    hitboxW, hitboxH, currentGameMode, currplayer_mini != 0,
                    currplayer_gravity != 0, movingRight: true, slopeActive,
                    dblocked, currplayer_slope_type);
                lethal |= pre.lethal;
                if (pre.nudge != 0) playerY_fixed += pre.nudge << 8;
                if (pre.slopeType != 0) currplayer_slope_type = pre.slopeType;
            }

            playerY_px = NesPlayerBgCollisionY_px(playerY_fixed);
            var right = SharedPhysics.CheckPlatformerSideCollision(
                in platformerCollisionMap, playerX_px, playerY_px,
                hitboxW, hitboxH, currentGameMode, currplayer_mini != 0,
                currplayer_gravity != 0, movingRight: true, slopeActive,
                dblocked, currplayer_slope_type);
            lethal |= right.lethal;
            if (right.nudge != 0) playerY_fixed += right.nudge << 8;
            if (right.slopeType != 0) currplayer_slope_type = right.slopeType;

            playerY_px = NesPlayerBgCollisionY_px(playerY_fixed);
            var left = SharedPhysics.CheckPlatformerSideCollision(
                in platformerCollisionMap, playerX_px, playerY_px,
                hitboxW, hitboxH, currentGameMode, currplayer_mini != 0,
                currplayer_gravity != 0, movingRight: false, slopeActive,
                dblocked, currplayer_slope_type);
            lethal |= left.lethal;
            if (left.nudge != 0) playerY_fixed += left.nudge << 8;
            if (left.slopeType != 0) currplayer_slope_type = left.slopeType;

            int oldScreenX_fixed = oldX_fixed - cameraX_fixed;
            int result = oldX_fixed;
            bool moved = false;
            if (direction > 0 && !right.blocked)
            {
                result += movementSpeed_fixed;
                moved = true;
            }
            else if (direction < 0 && !left.blocked && oldScreenX_fixed > 0x1200)
            {
                result -= movementSpeed_fixed;
                moved = true;
            }

            if (direction > 0 && right.blocked)
            {
                int screenHigh = (oldX_fixed - cameraX_fixed) >> 8;
                int worldLow = (screenHigh + ((cameraX_fixed >> 8) & 0xFF)) & 0xFF;
                int correction = ((worldLow + 4) & 7) - 4 +
                    (currplayer_mini != 0 ? 1 : 0);
                result -= correction << 8;
            }
            else if (direction < 0 && left.blocked)
            {
                int screenHigh = (oldX_fixed - cameraX_fixed) >> 8;
                int worldLow = (screenHigh + ((cameraX_fixed >> 8) & 0xFF)) & 0xFF;
                int correction = ((worldLow + 4) & 7) - 4;
                result -= correction << 8;
            }

            int resultScreenX_fixed = result - cameraX_fixed;
            if (resultScreenX_fixed > 0xF000)
            {
                resultScreenX_fixed = oldScreenX_fixed >= 0xF000 ? 0xF000 : 0;
                result = cameraX_fixed + resultScreenX_fixed;
                moved = false;
            }

            playerVelX_fixed = moved ? currentSpeed_fixed : 0;
            return result;
        }

        private void ApplySimulatorNesCubeRobotYScroll()
        {
            // process_y_scroll decrements this global before applying its
            // cube/robot camera-step limit.
            if (_sim_exitPortalTimer != 0)
                _sim_exitPortalTimer--;

            int screenY_fixed = playerY_fixed - cameraY_fixed;
            int scrollYLinear = (cameraY_fixed >> 8) + _sim_nesCoordOffset;
            int minCameraY_fixed = (_sim_minScrollYLin - _sim_nesCoordOffset) << 8;
            int maxCameraY_fixed = SimulatorNesMaxCameraY_px() << 8;

            if (screenY_fixed < 0x4000)
            {
                if (scrollYLinear > _sim_minScrollYLin ||
                    (scrollYLinear == _sim_minScrollYLin && _sim_scrollYSubpx != 0))
                {
                    int needed_fixed = 0x4000 - screenY_fixed;
                    if (_sim_exitPortalTimer != 0)
                    {
                        int maxStepPx = 11 - _sim_exitPortalTimer;
                        if ((needed_fixed >> 8) >= maxStepPx)
                            needed_fixed = maxStepPx << 8;
                    }
                    int low = needed_fixed & 0xFF;
                    int high = (needed_fixed >> 8) & 0xFF;
                    int sub = _sim_scrollYSubpx - low;
                    int borrow = 0;
                    if (sub < 0)
                    {
                        sub += 256;
                        borrow = 1;
                    }
                    _sim_scrollYSubpx = sub;
                    int cameraMove_fixed = -((high + borrow) << 8);
                    int playerMove_fixed = needed_fixed + cameraMove_fixed;
                    cameraY_fixed += cameraMove_fixed;
                    playerY_fixed += playerMove_fixed;
                    if (cameraY_fixed < minCameraY_fixed)
                    {
                        // The integer top-cap compensation preserves world Y.
                        playerY_fixed -= _sim_scrollYSubpx;
                        _sim_scrollYSubpx = 0;
                        cameraY_fixed = minCameraY_fixed;
                    }
                }
            }
            else if ((screenY_fixed >> 8) >= 0xA0 && scrollYLinear < 719)
            {
                int needed_fixed = screenY_fixed - 0xA000;
                if (_sim_exitPortalTimer != 0)
                {
                    int maxStepPx = 11 - _sim_exitPortalTimer;
                    if ((needed_fixed >> 8) >= maxStepPx)
                        needed_fixed = maxStepPx << 8;
                }
                int low = needed_fixed & 0xFF;
                int high = (needed_fixed >> 8) & 0xFF;
                int sub = _sim_scrollYSubpx + low;
                int carry = 0;
                if (sub > 255)
                {
                    sub -= 256;
                    carry = 1;
                }
                _sim_scrollYSubpx = sub;
                int cameraMove_fixed = (high + carry) << 8;
                int playerMove_fixed = -needed_fixed + cameraMove_fixed;
                cameraY_fixed += cameraMove_fixed;
                playerY_fixed += playerMove_fixed;
                if (cameraY_fixed > maxCameraY_fixed || (cameraY_fixed == maxCameraY_fixed && _sim_scrollYSubpx != 0))
                {
                    // NES cap_scroll_y_at_bottom() folds the fractional scroll
                    // byte into currplayer_y even when PF/sim's integer linear
                    // camera value lands exactly on the bottom cap.
                    playerY_fixed += _sim_scrollYSubpx;
                    _sim_scrollYSubpx = 0;
                    cameraY_fixed = maxCameraY_fixed;
                }
            }
        }

        private bool PrepareSimulatorNesSpriteSlot()
        {
            if (!simulatorNesDispatchActive)
                return false;

            int idx = simulatorNesDispatchIndex;
            int slot = simulatorNesDispatchSlot;
            int sid = simulatorNesDispatchSpriteId & 0xFF;
            int height = sid < sprite_heights.Length ? sprite_heights[sid] : 0;

            // DECO records count and remain resident but never enter gameplay
            // collision. COLR/OUTL records count, execute, and are replaced on
            // the following check_spr_objects pass.
            if (height == 0xFE)
                return false;
            if (height == 0xFD)
            {
                if (IsBackgroundTrigger(sid))
                {
                    pendingBgIdx = idx;
                    pendingBgSid = sid;
                    pendingTintChange = true;
                }
                else if (IsTileTrigger(sid))
                {
                    pendingTileIdx = idx;
                    pendingTileSid = sid;
                    pendingTintChange = true;
                }
                else if (IsGroundTrigger(sid))
                {
                    pendingGroundIdx = idx;
                    pendingGroundSid = sid;
                    pendingTintChange = true;
                }
                processedColorTriggers.Add(idx);
                simulatorNesSlotDead[slot] = true;
                return false;
            }
            if (height == 0xFC)
            {
                simulatorNesSlotDead[slot] = true;
                return false;
            }
            if (height == 0)
                return false;
            if (height != 0xFF)
                return true;

            int coinKind = SimulatorNesCoinKind(sid);
            if (coinKind >= 0 && simulatorCoinTimer[coinKind] != 0)
            {
                int yLow = ((simulatorNesSlotWorldY[slot] & 0xFF) -
                    ((simulatorCoinSpeed[coinKind] >> 8) & 0xFF)) & 0xFF;
                simulatorNesSlotWorldY[slot] =
                    (simulatorNesSlotWorldY[slot] & ~0xFF) | yLow;
                simulatorCoinSpeed[coinKind] =
                    (simulatorCoinSpeed[coinKind] - 0x40) & 0xFFFF;
                simulatorCoinTimer[coinKind] =
                    (simulatorCoinTimer[coinKind] + 1) & 0xFF;
                if (simulatorCoinTimer[coinKind] == 40)
                {
                    simulatorNesSlotDead[slot] = true;
                    simulatorCoinAnimating = false;
                    return false;
                }
            }

            bool isTeleportExit = sid == 0x4F || sid == 0x5A ||
                sid == 0x67 || sid == 0x69 || sid == 0x76 || sid == 0x78;
            if (isTeleportExit)
            {
                int relY = simulatorNesSlotRealY[slot];
                simulatorTeleportOutputY_px = sid == 0x5A ? relY : relY + TILE;
                return false;
            }

            if (sid == 0x0F)
            {
                TriggerSimulatorLevelComplete(idx);
                return false;
            }

            // Coins are SPBH records which continue into normal collision.
            if (sid == 0x07 || sid == 0x1A || sid == 0x1B ||
                (sid >= 0x1C && sid <= 0x1E))
                return true;

            bool consumed = true;
            switch (sid)
            {
                case 0x70: gravityMultiplier = 1.0 / 3.0; UpdateEffectiveGravity(); break;
                case 0x71: gravityMultiplier = 0.5; UpdateEffectiveGravity(); break;
                case 0x72: gravityMultiplier = 2.0 / 3.0; UpdateEffectiveGravity(); break;
                case 0x73: gravityMultiplier = 2.0; UpdateEffectiveGravity(); break;
                case 0x74: gravityMultiplier = 1.0; UpdateEffectiveGravity(); break;
                case 0x6F: playerInvis = true; break;
                case 0x7F: playerInvis = false; break;
                case 0x8E: wrapMode = true; break;
                case 0x9E: wrapMode = false; break;
                case 0xDD: nocamlockforced = true; break;
                case 0xED: nocamlockforced = false; break;
                case 0xF2: forcedTrails = 2; break;
                case 0xF3: forcedTrails = 0; break;
                case 0xF4: slowMode = true; break;
                case 0xF5: slowMode = false; break;
                case 0xDE:
                    targetXScrollStop_fixed = (simulatorNesSlotRealY[slot] & 0xF0) << 8;
                    break;
                case 0x7D:
                case 0xDF:
                case 0xEE:
                case 0xEF:
                case 0xF0:
                case 0xF1:
                    break;
                default:
                    consumed = false;
                    break;
            }

            if (consumed)
                simulatorNesSlotDead[slot] = true;

            // Unknown SPBH records remain resident and have no collision.
            return false;
        }

        private void TriggerSimulatorLevelComplete(int idx)
        {
            if (levelCompleteTriggered || deathTriggered)
                return;

            processedEndLevelTriggers.Add(idx);
            levelCompleteTriggered = true;
            paused = true;
            AppendSimDebug($"[LEVEL_COMPLETE] NES active slot idx={idx}");
            try
            {
                var coinInfoSnapshot =
                    new System.Collections.Generic.List<(int spriteIndex, int spriteId)>(collectedCoinInfo);
                Dispatcher?.BeginInvoke(new Action(() =>
                {
                    try { PauseOverlay.Visibility = System.Windows.Visibility.Collapsed; } catch { }
                    try { LevelCompleteOverlay.Visibility = System.Windows.Visibility.Visible; } catch { }
                    try { PopulateCoinDisplay(coinInfoSnapshot); } catch { }
                }));
            }
            catch { }
        }
        // Cam lock portals: 0xDD = cam lock ON (freeze camera Y), 0xED = cam lock OFF (resume auto-follow)
        private System.Collections.Generic.HashSet<int> processedCamLockPortals = new System.Collections.Generic.HashSet<int>();
        private bool nocamlockforced = false;
        // Wrap mode: 0x8E = wrap ON, 0x9E = wrap OFF (NES x_movement wrap_mode)
        private bool wrapMode = false;
        private System.Collections.Generic.HashSet<int> processedWrapPortals = new System.Collections.Generic.HashSet<int>();
        // Timewarp: 0xF4 = slowmode ON (0.5x), 0xF5 = slowmode OFF (NES: slowmode / kandoframecnt)
        private bool slowMode = false;
        private System.Collections.Generic.HashSet<int> processedTimewarpTriggers = new System.Collections.Generic.HashSet<int>();
        // Hide player: 0x6F = hide, 0x7F = show (NES: player_invis)
        private bool playerInvis = false;
        private System.Collections.Generic.HashSet<int> processedPlayerInvisTriggers = new System.Collections.Generic.HashSet<int>();
        // Player trails: 0xF2 = trails ON (forced_trails=2), 0xF3 = trails OFF (NES: forced_trails)
        private int forcedTrails = 0;
        private System.Collections.Generic.HashSet<int> processedTrailTriggers = new System.Collections.Generic.HashSet<int>();
        private int[] playerOldPosY = new int[9]; // sliding window of old Y positions (index 0 = newest)
        // Simulation tick counter (incremented every SimulateNumericStep call), used for trails flicker
        private int simTickCount = 0;
        // Smooth camera Y target for non-cube modes (ship/ball/UFO/spider/wave/swing)
        // Matches Famidash target_scroll_y: camera scrolls smoothly toward this value
        private int targetCameraY_fixed = 0;
        private const int PORTAL_TO_TOP_DIFF_PX = 0x3A; // 58px offset from portal Y to screen top
        // NES NTSC ship-scroll speeds. See PathfinderEngine.cs for the cc65
        // do_if_carry asymmetry that makes these unequal (down=3, up=2 px/frame).
        private const int SHIP_SCROLL_SPEED_DOWN_FIXED = 0x0300; // 3 px/frame (NES down branch, NTSC)
        private const int SHIP_SCROLL_SPEED_UP_FIXED   = 0x0200; // 2 px/frame (NES up branch,   NTSC)
        private int _sim_nesCoordOffset; // PF→NES linear-Y offset for nametable distortion
        private int _sim_minScrollYLin;
        private int _sim_scrollYSubpx;
        private byte _sim_exitPortalTimer;

        private int NesNtCameraTarget_fixed(int portalWorldY_px)
        {
            int rawTarget = portalWorldY_px - PORTAL_TO_TOP_DIFF_PX;
            int nesLinear = rawTarget + _sim_nesCoordOffset;
            if (nesLinear < 0x100)
                return rawTarget << 8;
            if ((nesLinear & 0xFF) >= 0xF0) nesLinear += 0x10;
            int hi = nesLinear >> 8;
            int lo = nesLinear & 0xFF;
            int physicalNES = hi * 240 + lo;
            int effectivePF = physicalNES - _sim_nesCoordOffset;
            // NES nametable-space targets may legitimately map above editor
            // world Y=0.  Keep the signed PF-space target so ship-style camera
            // motion and min_scroll_y capping follow the ROM exactly.
            return effectivePF << 8;
        }
        private bool _suppressDebugBarEvents = false;
        // Track orbs that have been activated so they only fire once
        private System.Collections.Generic.HashSet<int> processedOrbs = new System.Collections.Generic.HashSet<int>();
        // Orb buffer: true when the player has pressed/held X in-air and is eligible
        // to activate orbs. This is cleared on ground, when X is released, when
        // the player jumps, or when an orb is activated.
        private bool[] orbBufferActive = new bool[2] { false, false };
        // Ball input buffer countdown: mirrors PF's BallInputBuffer counter.
        // When a press sets orbBufferActive in ball mode, this counts down from 8.
        // When it hits 0, orbBufferActive is cleared — prevents stale buffer from
        // triggering a flip many frames later (hold-continuation would otherwise
        // keep orbBufferActive alive indefinitely).
        private int[] ballInputBufferCountdown = new int[2];
        // Prevent multiple orb activations from a single UI press: set when an orb
        // was activated in response to the current pressed state and cleared when
        // X is released or player lands.
#pragma warning disable CS0414
        private bool[] orbActivationConsumedThisPress = new bool[2] { false, false };
#pragma warning restore CS0414
        // When a hold-based activation consumes the held X, set this so further
        // hold-based activations are suppressed until X is released and pressed again.
#pragma warning disable CS0414
        private bool[] orbHoldConsumed = new bool[2] { false, false };
#pragma warning restore CS0414
        // True when a hold-based activation consumed the currently-held X and
        // the key is still down; used to prevent re-priming from sustained
        // hardware-held state until an explicit release occurs.
        private bool[] orbHoldConsumedKeyStillDown = new bool[2] { false, false };
        // When true, suppress all orb-buffer priming and fresh-press activations
        // until an explicit KeyUp is observed. Set when a hold-based activation
        // consumes the currently-held X so further activations require release.
        private bool[] orbHoldSuppressing = new bool[2] { false, false };
        // True when an orb or pad was activated on THIS frame (set by sprite collision, checked by physics)
        // Prevents bounce from occurring while an orb/pad is active
        private bool[] orbhitonthisframe = new bool[2];  // Array for dual-mode support
        private int playerVelY_fixed = 0; // current vertical velocity (fixed-point)
        private bool physicsEnabled = false; // enable physics after first jump (for testing)
        // Landing epsilon in fixed-point (1 pixel)
        private const int LAND_EPS_FIXED = 1 << 8;
        // Whether the player is currently considered on the ground (true when snapped to ground)
    #pragma warning disable CS0414 // assigned but never used - keep for future use
        private bool onGround = true;
        // When landing, keep the player treated as grounded for a few physics frames
        // to avoid jitter between 0 and a small gravity increment.
        private int groundStabilizeCounter = 0;
        // When the cube is snapped to an inverted ceiling, hold grounded state
        // for a few frames to avoid rhythmic velocity oscillation while the
        // head overlaps blocking tiles. This is cleared only when no blocking
        // tile is detected for enough frames.
        private int invertedCeilingHoldCounter = 0;
    #pragma warning restore CS0414

        // Jump-buffer: when the player presses jump slightly before landing, store a small
        // frame window so the jump fires on landing. Timer_Tick sets this under `simLock`.
        private int jumpBufferCounter = 0;
        // Pogo bounce animation frame counter (shows pogo2.png for 8 frames after bounce)
        private int pogoBounceAnimationCounter = 0;
        private double pogoBounceAnimationFrameAccum = 0.0;
        // Ball icon animation frame counter (alternates between ball.png and ball2.png every 3 frames)
        private int ballAnimationFrameCounter = 0;
        private double ballAnimationFrameAccum = 0.0;
        // Robot icon animation frame counter (cycles through 4 frames, 5 frames each when grounded)
        private int robotAnimationFrameCounter = 0;
        private double robotAnimationFrameAccum = 0.0;
        // Spider icon animation frame counter (cycles through 4 frames, 5 frames each when grounded)
        private int spiderAnimationFrameCounter = 0;
        private double spiderAnimationFrameAccum = 0.0;

        // Toggle to show player's Y velocity in top-left when Shift+F12 is pressed
        private bool showYVelocityOverlay = false;
        private System.Windows.Controls.TextBlock? yVelTextBlock = null;
        // Simulation time scale (1.0 = normal). Adjusting this slows/speeds the simulation
        // in even 10% increments when the user presses +/-.
        private double simTimeScale = 1.0;
        private bool isFullSpeed = true; // Set at start of each frame: true when simTimeScale == 1.0 for deterministic integer math
        private const int JUMP_BUFFER_FRAMES = 6; // ~100ms @60Hz
        // Flag to enable refactored collision/physics system
        private bool useRefactoredPhysics = true;
        // Ball mode buffer: allow buffering an X press for ball gravity switch
        // Ball toggle tracking:
        // `ballToggleRequested` is set by UI KeyDown when X is pressed; it is consumed
        // by the physics code when landing or ball flip logic.
        private int ballToggleRequested = 0;
        private const int BALL_BUFFER_FRAMES = 6;
        private bool ballGoingDown = true; // true = downwards, false = upwards
        
        // Additional game mode state variables
        private bool[] ballSwitched = new bool[2];
        private int ballFlipCooldown = 0;
        private bool ballWasGroundedBeforeFlip = false;
        // Per-player ball flip state for dual mode save/restore
        private int[] player_ballFlipCooldown = new int[2];
        private bool[] player_ballWasGroundedBeforeFlip = new bool[2];
        // Countdown-based ball flip buffer (matches PF's BallInputBuffer: set to 8 when pressing while airborne, decremented each frame)
        private int[] ballFlipBuffer = new int[2];
        // Track PF input for the current frame (for dual mode P2 input re-injection)
        private bool pfInputThisFrame = false;
        // P2-specific ball hold counter (mirrors PF's per-player BallInputBuffer).
        // When P2 receives a raw True in ball mode, this is set to PF_BALL_HOLD_FRAMES
        // and decremented each frame, providing keyXHeld=true to bridge air-to-landing.
        private int p2BallHoldCounter = 0;
        private bool[] ufoOrbed = new bool[2];
        private bool[] orbed = new bool[2]; // Prevents jumps/teleports until X released (spider orbs/pads, teleport portals, S blocks, J blocks)
        private bool blackOrbed = false; // Spider black orb hold mechanic
        private int[] dashing = new int[2]; // 0=not dashing, 1=horizontal, 2=45deg up, 3=45deg down, 4=upward, 5=downward
        private bool hblocked = false; // H block - headbonk block: causes instant ceiling ejection instead of passthrough
        private bool jblocked = false; // J block - requires press instead of hold for next jump
        private bool dblocked = false; // D block - allows wave to walk on surfaces instead of going through/colliding/dying
        private bool fblocked = false; // F block - forces press-to-jump in cube mode and flips gravity on ceiling/floor hit
        private int invincibleCounter = 0; // NES invincible_counter: 8 frames of spawn protection
        private int[] ninjajumps = new int[2];
        private int[] robotJumpTime = new int[2];
#pragma warning disable CS0414
        private int[] robotJumpFrame = new int[2];
        private int[] chargepower = new int[2];  // Football charge accumulation
#pragma warning restore CS0414
        private bool[] robotJumpPressed = new bool[2];
#pragma warning disable CS0414
        private int ninjaJumps = 0;
#pragma warning restore CS0414
        private bool ninjaJumpedThisFrame = false;
#pragma warning disable CS0414
        private bool swingSwitched = false;
#pragma warning restore CS0414

        // Update the player image based on `currentGameMode`.
        private void UpdatePlayerImageForMode()
        {
            try
            {
                if (playerImage == null) return;
                string exeDir = AppDomain.CurrentDomain.BaseDirectory ?? ".";
                string choice = "cube.png";
                
                // Use mini images if in mini mode
                if (miniMode && currentGameMode == 0) choice = "cube-mini.png";
                else if (miniMode && currentGameMode == 1) choice = "ship-mini.png";
                else if (miniMode && currentGameMode == 2) choice = "ball-mini.png";
                else if (miniMode && currentGameMode == 3) choice = "ufo-mini.png";
                else if (miniMode && currentGameMode == 4) choice = "robot-mini.png";
                else if (miniMode && currentGameMode == 5) choice = "spider-mini.png";
                else if (miniMode && currentGameMode == 6) choice = "wave-mini.png";
                else if (miniMode && currentGameMode == 7) choice = "swingcopter-mini.png";
                else if (miniMode && currentGameMode == 8) choice = "ninja-mini.png";
                else if (miniMode && currentGameMode == 9) {
                    // Mini pogo bounce animation - show pogo-mini2.png for 8 frames after bounce
                    if (pogoBounceAnimationCounter > 0) {
                        choice = "pogo-mini2.png";
                    } else {
                        choice = "pogo-mini.png";
                    }
                }
                else if (miniMode && currentGameMode == 10) choice = "snake-mini.png";
                else if (miniMode && currentGameMode == 11) choice = "football-mini.png";
                else if (currentGameMode == 1) {
                    // Ship animation based on velocity
                    // Game frames 0-7 map to PNG frames 0-6 (7 unique frames from NES shipFrameTable)
                    // PNG frame mapping: 0(0/1) -> 1(2) -> 2(3) -> 3(4) -> 4(5) -> 5(6) -> 6(7)
                    int shipFrame = GetShipSpriteFrame();  // Returns 0-7
                    int[] shipFrameMap = { 0, 0, 1, 2, 3, 4, 5, 6 };  // Map game frame 0-7 to PNG frame index 0-6
                    int pngFrame = shipFrameMap[shipFrame & 0x07];  // Clamp to 0-7
                    
                    choice = miniMode ? (pngFrame switch {
                        0 => "ship-mini.png",
                        1 => "ship-mini1.png",
                        2 => "ship-mini2.png",
                        3 => "ship-mini3.png",
                        4 => "ship-mini4.png",
                        5 => "ship-mini5.png",
                        6 => "ship-mini6.png",
                        _ => "ship-mini.png"
                    }) : (pngFrame switch {
                        0 => "ship.png",
                        1 => "ship2.png",
                        2 => "ship3.png",
                        3 => "ship4.png",
                        4 => "ship5.png",
                        5 => "ship6.png",
                        6 => "ship7.png",
                        _ => "ship.png"
                    });
                }
                else if (currentGameMode == 2) {
                    // Ball animation alternates every 6 frames
                    if (ballAnimationFrameCounter < 6) {
                        choice = "ball.png";
                    } else {
                        choice = "ball2.png";
                    }
                }
                else if (currentGameMode == 3) choice = "ufo.png";
                else if (currentGameMode == 4) choice = "";  // Robot animation is handled in RenderFrame
                else if (currentGameMode == 5) choice = "";  // Spider animation is handled in RenderFrame
                else if (currentGameMode == 6) {
                    // Wave animation based on velocity
                    if (Math.Abs(playerVelY_fixed) <= 0x0300) {
                        choice = "wave2.png";  // Straight (with ±0x0300 tolerance to reduce flickering)
                    } else if (playerVelY_fixed < 0) {
                        choice = "wave.png";   // Going up (normal, will be flipped)
                    } else {
                        choice = "wave.png";   // Going down (normal, no flip)
                    }
                }
                else if (currentGameMode == 7) {
                    // Swingcopter animation based on velocity - same as ship
                    // Game frames 0-7 map to PNG frames 0-4 (5 unique frames)
                    int swingFrame = GetSwingcopterSpriteFrame();  // Returns 0-7
                    int[] swingFrameMap = { 0, 0, 1, 2, 2, 3, 4, 4 };  // Map game frame 0-7 to PNG frame index 0-4
                    int pngFrame = swingFrameMap[swingFrame & 0x07];  // Clamp to 0-7
                    
                    choice = miniMode ? (pngFrame switch {
                        0 => "swingcopter-mini.png",
                        1 => "swingcopter-mini1.png",
                        2 => "swingcopter-mini2.png",
                        3 => "swingcopter-mini3.png",
                        4 => "swingcopter-mini4.png",
                        _ => "swingcopter-mini.png"
                    }) : (pngFrame switch {
                        0 => "swingcopter.png",
                        1 => "swingcopter1.png",
                        2 => "swingcopter2.png",
                        3 => "swingcopter3.png",
                        4 => "swingcopter4.png",
                        _ => "swingcopter.png"
                    });
                }
                else if (currentGameMode == 8) choice = "ninja.png";
                else if (currentGameMode == 9) {
                    // Show pogo2.png for 8 frames after bounce
                    if (pogoBounceAnimationCounter > 0) {
                        choice = "pogo2.png";
                    } else {
                        choice = "pogo.png";
                    }
                }
                else if (currentGameMode == 10) choice = "snake.png";
                else if (currentGameMode == 11) {
                    // Football animation - cube-style rotation with flip table (7 frames across 24 rotation frames)
                    int footballFrameAndFlip = GetFootballSpriteFrameAndFlip();
                    int frameIndex = footballFrameAndFlip & 0x0F;  // Extract frame 0-6 from low byte
                    int flipFlags = footballFrameAndFlip & 0xC0;   // Extract flip bits
                    
                    choice = miniMode ? $"football-mini{(frameIndex > 0 ? frameIndex.ToString() : "")}.png" 
                                      : $"football{(frameIndex > 0 ? frameIndex.ToString() : "")}.png";
                }

                if (currentGameMode == 2)
                {
                    AppendSimDebug($"[BALL_MODE] gameMode=2, counter={ballAnimationFrameCounter}, choice={choice}");
                }

                // Robot/Spider normal mode animation is handled in RenderFrame;
                // skip image load here to avoid the magenta-rectangle fallback.
                if (string.IsNullOrEmpty(choice)) return;

                BitmapSource? bi = null;

                // 1) Prefer copy in output directory
                try
                {
                    string candidateOut = System.IO.Path.Combine(exeDir, choice);
                    if (System.IO.File.Exists(candidateOut))
                    {
                        var tmp = new BitmapImage();
                        tmp.BeginInit();
                        tmp.UriSource = new Uri(candidateOut);
                        tmp.CacheOption = BitmapCacheOption.OnLoad;
                        tmp.EndInit();
                        tmp.Freeze();
                        bi = tmp;
                    }
                }
                catch { bi = null; }

                // 2) Fallback: repo root relative (four levels up) for developer tree
                if (bi == null)
                {
                    try
                    {
                        string candidate = System.IO.Path.GetFullPath(System.IO.Path.Combine(exeDir, "..\\..\\..\\..\\" + choice));
                        if (System.IO.File.Exists(candidate))
                        {
                            var b2 = new BitmapImage();
                            b2.BeginInit();
                            b2.UriSource = new Uri(candidate);
                            b2.CacheOption = BitmapCacheOption.OnLoad;
                            b2.EndInit();
                            b2.Freeze();
                            bi = b2;
                        }
                    }
                    catch { }
                }

                // 3) Final fallback: embedded resource in the assembly (via cache)
                if (bi == null)
                {
                    try
                    {
                        // Use "." prefix to avoid partial matches (e.g. football.png matching ball.png)
                        var cached = LoadCachedResourceImage("." + choice);
                        if (currentGameMode == 2)
                        {
                            AppendSimDebug($"[BALL_RES] Looking for '{choice}', cached: {cached != null}");
                        }
                        if (cached != null)
                        {
                            bi = App.EnsureUnfrozenForRender(cached) ?? cached;
                        }
                    }
                    catch { }
                }

                if (bi != null)
                {
                    AppendSimDebug($"[IMAGE_LOAD] Loading image, applyPlayer2Colors={applyPlayer2Colors}, choice={choice}");
                    
                    // Determine which cache to use and apply colors if needed
                    if (applyPlayer2Colors)
                    {
                        // Check player 2 color cache first
                        string cacheKey = choice.ToString();
                        if (!player2ColorCache.ContainsKey(cacheKey))
                        {
                            AppendSimDebug($"[IMAGE_LOAD] Creating recolored P2 cache for {cacheKey}");
                            player2ColorCache[cacheKey] = ReplaceColorsForPlayer2(bi);
                        }
                        else
                        {
                            AppendSimDebug($"[IMAGE_LOAD] Using cached P2 colors for {cacheKey}");
                        }
                        bi = player2ColorCache[cacheKey];
                    }
                    else
                    {
                        // Check player 1 color cache
                        string cacheKey = choice.ToString();
                        if (!playerColorCache.ContainsKey(cacheKey))
                        {
                            AppendSimDebug($"[IMAGE_LOAD] Caching P1 original colors for {cacheKey}");
                            playerColorCache[cacheKey] = bi;
                        }
                        else
                        {
                            bi = playerColorCache[cacheKey];
                        }
                    }
                    
                    playerImage.Source = App.EnsureUnfrozenForRender(bi) ?? bi;
                    playerImage.Tag = choice;  // Track which image is loaded
                    playerImage.Width = bi.PixelWidth;
                    playerImage.Height = bi.PixelHeight;
                    playerImage.Visibility = Visibility.Visible;
                    if (playerRect != null) playerRect.Visibility = Visibility.Collapsed;
                    
                    // For mini mode, use the actual hitbox size (8x8) not the sprite image size
                    // Mini sprite images may be 16x16 with the icon in one quadrant
                    if (miniMode)
                    {
                        playerVisualWidth = 8;
                        playerVisualHeight = 8;
                    }
                    else
                    {
                        playerVisualWidth = (int)Math.Ceiling(playerImage.Width);
                        playerVisualHeight = (int)Math.Ceiling(playerImage.Height);
                    }
                    
                    try
                    {
                        // Flip game mode icons vertically when gravity is reversed (except cube and ninja modes)
                        if (gravityReversed && currentGameMode != 0 && currentGameMode != 8)
                        {
                            playerImage.RenderTransformOrigin = new Point(0.5, 0.5);
                            playerImage.RenderTransform = new ScaleTransform(1, -1);
                        }
                        else
                        {
                            // Ensure no transform remains when gravity is normal
                            playerImage.RenderTransform = Transform.Identity;
                        }
                    }
                    catch { }
                    return;
                }

                // Could not load image: fall back to magenta rectangle
                if (playerRect == null)
                {
                    playerRect = new System.Windows.Shapes.Rectangle { Width = TILE, Height = TILE, Fill = new SolidColorBrush(Colors.Magenta) };
                    System.Windows.Controls.Canvas.SetZIndex(playerRect, 1000);
                    RenderCanvas.Children.Add(playerRect);
                }
                playerRect.Visibility = Visibility.Visible;
                if (playerImage != null) playerImage.Visibility = Visibility.Collapsed;
                playerVisualWidth = (int)Math.Ceiling(playerRect.Width);
                playerVisualHeight = (int)Math.Ceiling(playerRect.Height);
            }
            catch { }
        }

        private void UpdatePlayerVisualSizeForMode()
        {
            try
            {
                // For mini mode, use the actual hitbox size (8x8) not the sprite image size
                // Mini sprite images may be 16x16 with the icon in one quadrant
                if (miniMode)
                {
                    playerVisualWidth = 8;
                    playerVisualHeight = 8;
                }
                else if (playerImage != null && playerImage.Source != null && playerImage.Visibility == Visibility.Visible)
                {
                    playerVisualWidth = (int)Math.Ceiling(playerImage.Width);
                    playerVisualHeight = (int)Math.Ceiling(playerImage.Height);
                }
                else if (playerRect != null && playerRect.Visibility == Visibility.Visible)
                {
                    playerVisualWidth = (int)Math.Ceiling(playerRect.Width);
                    playerVisualHeight = (int)Math.Ceiling(playerRect.Height);
                }
            }
            catch { }
        }

        private void UpdatePlayerIconFlip()
        {
            try
            {
                // Update checkbox to reflect current gravity state
                if (InvertedCheckBox != null)
                {
                    InvertedCheckBox.IsChecked = gravityReversed;
                }
                
                if (playerImage != null && playerImage.Source != null)
                {
                    // Cube/ninja/football modes handle flip via rotation sprite table — skip here
                    if (currentGameMode == 0 || currentGameMode == 4 || currentGameMode == 8 || currentGameMode == 11)
                    {
                        // Don't touch RenderTransform; the per-frame rotation rendering handles it
                        return;
                    }
                    
                    // Wave special handling: flip based on velocity direction (moving UP = flip)
                    if (currentGameMode == 6)
                    {
                        // Wave flips when moving upward (negative velocity)
                        if (playerVelY_fixed < 0)
                        {
                            playerImage.RenderTransformOrigin = new Point(0.5, 0.5);
                            playerImage.RenderTransform = new ScaleTransform(1, -1);
                        }
                        else
                        {
                            playerImage.RenderTransform = Transform.Identity;
                        }
                    }
                    else if (gravityReversed)
                    {
                        // Other modes flip based on gravity
                        playerImage.RenderTransformOrigin = new Point(0.5, 0.5);
                        playerImage.RenderTransform = new ScaleTransform(1, -1);
                    }
                    else
                    {
                        playerImage.RenderTransform = Transform.Identity;
                    }
                }
            }
            catch { }
        }

        // P/Invoke to check key state asynchronously from background threads
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        private static readonly uint currentProcessId = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;

        private bool IsXDownAsync() { 
            // During pathfinder speculative simulation OR pathfinder replay,
            // ignore real keyboard input. PF_InjectInput sets keyXHeld/keyXPressedCount
            // directly; real keyboard state must not bleed into physics.
            if (pfSimulating || pathfinderEnabled) return false;

            // Only poll keyboard when the current process owns the foreground window.
            // This prevents stray key state from OTHER applications (typing in notepad, etc.)
            // from causing non-deterministic physics in auto levels, while keeping controls
            // fully responsive when any editor window is active/focused.
            try
            {
                IntPtr fg = GetForegroundWindow();
                if (fg == IntPtr.Zero) return false;
                GetWindowThreadProcessId(fg, out uint fgPid);
                if (fgPid != currentProcessId) return false;
            }
            catch { return false; }

            // Check X (0x58), UP (0x26), or SPACE (0x20)
            return (GetAsyncKeyState(0x58) & 0x8000) != 0 || 
                   (GetAsyncKeyState(0x26) & 0x8000) != 0 || 
                   (GetAsyncKeyState(0x20) & 0x8000) != 0; 
        }

        private bool IsPlatformerDirectionDownAsync(bool right)
        {
            if (pfSimulating || pathfinderEnabled) return false;
            try
            {
                IntPtr fg = GetForegroundWindow();
                if (fg == IntPtr.Zero) return false;
                GetWindowThreadProcessId(fg, out uint fgPid);
                if (fgPid != currentProcessId) return false;
            }
            catch { return false; }

            // VK_RIGHT / VK_LEFT. NES gives right priority when both are held.
            return (GetAsyncKeyState(right ? 0x27 : 0x25) & 0x8000) != 0;
        }

        // Mapping from speed-portal sprite id -> speed value
        private readonly System.Collections.Generic.Dictionary<int, int> speedPortalMap = new System.Collections.Generic.Dictionary<int, int>
        {
            { 0x14, CUBE_SPEED_X05 },
            { 0x15, CUBE_SPEED_X1  },
            { 0x16, CUBE_SPEED_X2  },
            { 0x20, CUBE_SPEED_X3  },
            { 0x21, CUBE_SPEED_X4  },
            { 0x6D, CUBE_SPEED_SLOW } // 0.1x special speed
        };

        // Calculate music time (in seconds) to reach a given X position, accounting for speed portals
        private double CalculateMusicTimeToPosition(int targetX_px)
        {
            try
            {
                // Use player center position to match interaction line behavior
                int targetCenter_px = targetX_px + (playerVisualWidth / 2);
                
                // Default to 1x speed
                int currentSpeed_fixed = CUBE_SPEED_X1;
                int lastX_px = 0;
                double totalTime = 0.0;
                
                // Get all speed portals sorted by X position
                var speedPortals = new System.Collections.Generic.List<(int x, int speed)>();
                
                if (sprites != null && spriteAnchors != null)
                {
                    for (int _si = 0; _si < nonEmptySpriteIndices.Length; _si++)
                    {
                        int idx = nonEmptySpriteIndices[_si]; int sid = sprites[idx];
                        if (sid == -1) continue;
                        if (!speedPortalMap.ContainsKey(sid)) continue;
                        
                        // Get anchor position (center of sprite)
                        int anchorTileX = spriteAnchors.TryGetValue(idx, out var a) ? a.anchorTileX : idx % mapWidth;
                        int anchorX_px = anchorTileX * TILE + (TILE / 2);
                        
                        // Only consider portals before target center
                        if (anchorX_px <= targetCenter_px)
                        {
                            speedPortals.Add((anchorX_px, speedPortalMap[sid]));
                        }
                    }
                }
                
                // Sort by X position
                speedPortals.Sort((a, b) => a.x.CompareTo(b.x));
                
                // Calculate time for each segment
                foreach (var portal in speedPortals)
                {
                    int segmentDistance_px = portal.x - lastX_px;
                    if (segmentDistance_px > 0)
                    {
                        // Speed is in fixed-point (pixels per frame * 256)
                        // Convert to pixels per second: (speed_fixed / 256) * 60 fps
                        double speed_px_per_second = (currentSpeed_fixed / 256.0) * 60.0;
                        if (speed_px_per_second > 0)
                        {
                            totalTime += segmentDistance_px / speed_px_per_second;
                        }
                    }
                    
                    // Update speed for next segment
                    currentSpeed_fixed = portal.speed;
                    lastX_px = portal.x;
                }
                
                // Add final segment from last portal to target
                int finalDistance_px = targetCenter_px - lastX_px;
                if (finalDistance_px > 0)
                {
                    double speed_px_per_second = (currentSpeed_fixed / 256.0) * 60.0;
                    if (speed_px_per_second > 0)
                    {
                        totalTime += finalDistance_px / speed_px_per_second;
                    }
                }
                
                return totalTime;
            }
            catch
            {
                return 0.0;
            }
        }

        private void ApplyColorTriggersUpToPosition(int targetX_px)
        {
            try
            {
                if (sprites == null || spriteAnchors == null) return;
                
                int? lastBgIdx = null, lastTileIdx = null, lastGroundIdx = null;
                int? lastBgSid = null, lastTileSid = null, lastGroundSid = null;
                
                // Find the last trigger of each type before target position
                for (int _si = 0; _si < nonEmptySpriteIndices.Length; _si++)
                {
                    int idx = nonEmptySpriteIndices[_si]; int sid = sprites[idx];
                    if (sid == -1) continue;
                    
                    // Get anchor position
                    int anchorTileX = spriteAnchors.TryGetValue(idx, out var a) ? a.anchorTileX : idx % mapWidth;
                    int anchorX_px = anchorTileX * TILE + (TILE / 2);
                    
                    // Only consider triggers before target
                    if (anchorX_px <= targetX_px)
                    {
                        if (IsBackgroundTrigger(sid))
                        {
                            lastBgIdx = idx;
                            lastBgSid = sid;
                        }
                        else if (IsTileTrigger(sid))
                        {
                            lastTileIdx = idx;
                            lastTileSid = sid;
                        }
                        else if (IsGroundTrigger(sid))
                        {
                            lastGroundIdx = idx;
                            lastGroundSid = sid;
                        }
                    }
                }
                
                // Apply the triggers
                if (lastBgIdx.HasValue && lastBgSid.HasValue)
                {
                    var c = ColorFromTrigger(lastBgSid.Value);
                    backgroundTint = c;
                    backgroundForceSolidBlack = (lastBgSid.Value == 0x8F);
                    processedColorTriggers.Add(lastBgIdx.Value);
                }
                
                if (lastTileIdx.HasValue && lastTileSid.HasValue)
                {
                    var c = ColorFromTrigger(lastTileSid.Value);
                    tileTint = c;
                    processedColorTriggers.Add(lastTileIdx.Value);
                }
                
                if (lastGroundIdx.HasValue && lastGroundSid.HasValue)
                {
                    var c = ColorFromTrigger(lastGroundSid.Value);
                    if (lastGroundSid.Value == 0xCF)
                    {
                        groundTint = Color.FromArgb(255, 0, 0, 0);
                    }
                    else
                    {
                        groundTint = c;
                    }
                    processedColorTriggers.Add(lastGroundIdx.Value);
                }
            }
            catch { }
        }

        private readonly System.Windows.Threading.DispatcherTimer timer;
        // Dedicated background simulation timer to keep simulation at a steady 60Hz
        private System.Threading.Timer? simTimer;
        private int simTimerGeneration;
        private readonly object simLock = new object();
        // High-resolution timing for fixed-step simulation at 60Hz
        private System.Diagnostics.Stopwatch simStopwatch = new System.Diagnostics.Stopwatch();
        private double simLastMs = 0.0;
        private double simAccumulatedMs = 0.0;
        private const double SIM_STEP_MS = 1000.0 / 60.0; // 16.666... ms per fixed-step
        // UI animation accumulator for cases where numeric sim is paused or not running
        private double uiAnimLastMs = 0.0;
        private double uiAnimAccumulatedMs = 0.0;
        // Pending color trigger info populated by simulation thread and applied on UI thread
        // Use -1 to indicate 'none' rather than nullable/volatile types.
        private int pendingBgIdx = -1;
        private int pendingBgSid = -1;
        private int pendingTileIdx = -1;
        private int pendingTileSid = -1;
        private int pendingGroundIdx = -1;
        private int pendingGroundSid = -1;
        private bool pendingTintChange = false;
        private bool pendingTintChangeIsStartup = false;
        // Set to true when we applied starting-per-level tints so subsequent image regeneration
        // avoids applying object/outline tints at startup.
        private bool startupTintApplied = false;
        private System.Diagnostics.Stopwatch renderStopwatch = new System.Diagnostics.Stopwatch();

        private int animationFrame = 0;
        // If the simulator has an owner MainWindow, prefer its animation frame so
        // two-frame decorations (dash-orbs, spider-orbs, deco) pulse in exact sync.
        private int GetEditorAnimationFrameValue()
        {
            try
            {
                // Only use the editor's animation frame when the editor's preview-mode animations are active.
                if (this.Owner is MainWindow mw && mw.EditorPreviewMode) return mw.EditorAnimationFrame;
            }
            catch { }
            return animationFrame;
        }
        // Sprite IDs that should animate at half speed (coins, pads, orbs)
        private static readonly System.Collections.Generic.HashSet<int> slowAnimatedSpriteIds = new System.Collections.Generic.HashSet<int> { 0x07, 0x1A, 0x1B, 0x6E };
        private System.Collections.Generic.Dictionary<int, int> spriteFrameOffsets = new System.Collections.Generic.Dictionary<int, int>();
        private Random spriteAnimationRandom = new Random();

        // Tile-layer cache and sprite pooling for performance
        private RenderTargetBitmap? tileLayerCache = null;
        private int cachedStartTileX = int.MinValue;
        private int cachedStartTileY = int.MinValue;
        // Display scale chosen at launch (1–4). Sizes the viewport grid and 3D viewport.
        internal int _simulatorDisplayScale = 1;
        // 2-D zoom scale set by the zoom slider (1.0 = normal, <1 = zoomed out showing more world)
        internal double sim2DZoomScale = 1.0;
        // Actual tile coverage used by the last tile-cache build (may be wider/taller when zoomed out)
        private int _currentCacheTilesX = NES_W + 1;
        private int _currentCacheTilesY = NES_H + 1;
        private System.Windows.Controls.Image? tileLayerImage = null;
        private System.Windows.Controls.Image? spriteLayerImage = null;
        private System.Windows.Shapes.Rectangle? bgRectPersistent = null;
        private System.Windows.Shapes.Rectangle? groundRectPersistent = null;
        // Cached parallax brush — reuse per frame, only recreate when source image changes
        private ImageBrush? _cachedParallaxBrush = null;
        private TranslateTransform? _cachedParallaxTransform = null;
        private ImageSource? _cachedParallaxSource = null;
        // Cached background tint brush — reuse when color hasn't changed
        private SolidColorBrush? _cachedBgTintBrush = null;
        private System.Windows.Media.Color _cachedBgTintColor;
        private System.Collections.Generic.List<System.Windows.Controls.Image> spritePool = new System.Collections.Generic.List<System.Windows.Controls.Image>();
        private int spritesInUse = 0;
        private bool lastCacheHadAnimatedTiles = false;
        private int lastCacheAnimationFrame = -1;

        // Note: spider-orbs (0x54,0x55) are NOT decorations and should not receive player tint.
        private readonly System.Collections.Generic.HashSet<int> decorationSpriteIds = new System.Collections.Generic.HashSet<int> { 0x36, 0x32, 0x33, 0x34, 0x35, 0x37, 0x2C, 0x3C, 0x2D, 0x3D, 0x2E, 0x2F, 0x30, 0x31, 0x38, 0x39, 0x3E, 0x3F, 0x2B, 0x3B, 0x2A, 0x3A, 0x49, 0x4A };

        // Coin-like sprites should composite over exact tile pixels, but they must never
        // receive the player's tint. Keep a small set so we can exclude them from tinting.
        private readonly System.Collections.Generic.HashSet<int> nonPlayerTintSpriteIds = new System.Collections.Generic.HashSet<int> { 0x07, 0x1A, 0x1B, 0x6E };

        // Cache tinted decoration sprites keyed by (spriteId<<32)|ARGB
        private readonly System.Collections.Generic.Dictionary<long, ImageSource?> tintedSpriteCache = new System.Collections.Generic.Dictionary<long, ImageSource?>();

        // Cache to remember if a BitmapSource appears 'top-heavy' (non-transparent pixels concentrated near top)
        private readonly System.Collections.Generic.Dictionary<int, bool> imageTopHeavyCache = new System.Collections.Generic.Dictionary<int, bool>();
        // Cache composite of sprite over background/tile area: key = "srcHash:ARGB:destX:destY"
        private readonly System.Collections.Generic.Dictionary<string, ImageSource?> spriteBackgroundCompositeCache = new System.Collections.Generic.Dictionary<string, ImageSource?>();

        // Cache for ground-tinted tile images keyed by (tileIndex<<32)|ARGB
        private readonly System.Collections.Generic.Dictionary<long, ImageSource?> groundTintedTileCache = new System.Collections.Generic.Dictionary<long, ImageSource?>();

        // Cache for black-masked tile variants used when backgroundForceSolidBlack is true.
        private readonly System.Collections.Generic.Dictionary<long, ImageSource?> blackMaskedTileCache = new System.Collections.Generic.Dictionary<long, ImageSource?>();

        // Track color-trigger anchors that have already been processed (so we don't resample every frame)
        private System.Collections.Generic.HashSet<int> processedColorTriggers = new System.Collections.Generic.HashSet<int>();

        // Cache toned image arrays to avoid expensive regeneration on every color trigger
        private ImageSource?[]? cachedTileTonedImages = null;
        private ImageSource?[]? cachedParallaxTonedImages = null;
        private ImageSource?[]? cachedGroundTonedImages = null;
        private Color cachedBackgroundTint = Color.FromArgb(0, 0, 0, 0);
        private Color cachedTileTint = Color.FromArgb(0, 0, 0, 0);
        private Color cachedGroundTint = Color.FromArgb(0, 0, 0, 0);
        private bool cachedBackgroundForceSolidBlack = false;

        // Lightweight one-time debug logging sets to avoid spamming output repeatedly
        private System.Collections.Generic.HashSet<int> decoLogged = new System.Collections.Generic.HashSet<int>();
        private System.Collections.Generic.HashSet<int> triggerLogged = new System.Collections.Generic.HashSet<int>();
        private int tileSelectionLogCount = 0;
        private const int TILE_SELECTION_LOG_LIMIT = 64;
        // Track last selected decoration frame so we can log when it actually changes
        private System.Collections.Generic.Dictionary<int, int> decoLastSelectedFrame = new System.Collections.Generic.Dictionary<int, int>();
        private bool enableSimulatorDebugLogging = false; // set true to capture helpful messages during diagnosis

        // When true, print per-simulation-step diagnostics about the player's feet
        // including tile indices, animated remaps and resolved collision categories.
        // Disabled by default to avoid writing runtime logs.
        private bool runtimePerFrameLog = false;

        // Internal one-time simulator debug file (used only for local diagnosis when requested)
        // Use a deterministic, repo-root path so it's easy to find when running from VS/`dotnet run`.
        private readonly string simDebugFilePath = @"C:\Editor Test\native-windows\sim_debug.txt";
        private bool simDebugLoggedFirstFrame = false;

        // (Note: actual AppendSimDebug implementation exists earlier and writes to temp.)

        // Helper to append a temp log when `enableSimulatorDebugLogging` is enabled.
        // Disabled: do not write temporary logs to disk.
        private void WriteTempLog(string message)
        {
            // No-op
            return;
        }

        // Lightweight ball-mode event logging for diagnosis (disabled)
        private void LogBallEvent(string evt)
        {
            // No-op: ball event logging disabled.
            return;
        }

        // Start background simulation timer.
        public void StartSimulation()
        {
            try
            {
                if (windowClosed || simTimer != null) return;

                // Start high-resolution stopwatch and use an accumulator to run fixed 60Hz steps.
                simStopwatch.Restart();
                simLastMs = simStopwatch.Elapsed.TotalMilliseconds;
                simAccumulatedMs = 0.0;
                // Run a one-shot timer and let only that exact timer generation re-arm
                // itself. A disposed callback can finish after Restart creates a new
                // timer; referring to the shared simTimer field from the old callback
                // used to re-arm the replacement and create overlapping simulation loops.
                int generation = Interlocked.Increment(ref simTimerGeneration);
                System.Threading.Timer? newTimer = null;
                newTimer = new System.Threading.Timer(_ =>
                {
                    try
                    {
                        if (generation == Volatile.Read(ref simTimerGeneration) &&
                            ReferenceEquals(Volatile.Read(ref simTimer), newTimer) &&
                            !restartInProgress && !windowClosed)
                        {
                            TimerSimulationLoop();
                        }
                    }
                    catch { }
                    finally
                    {
                        try
                        {
                            if (generation == Volatile.Read(ref simTimerGeneration) &&
                                ReferenceEquals(Volatile.Read(ref simTimer), newTimer) &&
                                !restartInProgress && !windowClosed)
                            {
                                newTimer?.Change(10, System.Threading.Timeout.Infinite);
                            }
                        }
                        catch { }
                    }
                }, null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);

                if (Interlocked.CompareExchange(ref simTimer, newTimer, null) != null)
                {
                    newTimer.Dispose();
                    return;
                }
                newTimer.Change(0, System.Threading.Timeout.Infinite);

                // NOTE: Do NOT reset playerY_fixed here - RestartButton_Click and constructor handle position initialization

                // Ensure the sim debug file exists and print its resolved path only when
                // `enableSimulatorDebugLogging` is set. Normal runs should not create the file.
                try
                {
                    if (enableSimulatorDebugLogging)
                    {
                        var header = DateTime.UtcNow.ToString("o") + " SIM_LOG_START\n";
                        System.Console.WriteLine("SIM_DEBUG_PATH: " + simDebugFilePath);
                        System.IO.File.AppendAllText(simDebugFilePath, header);
                        simDebugLoggedFirstFrame = true;
                    }
                }
                catch { }
                // When opening the simulator, clear any previous player-path overlay in the editor
                try
                {
                    if (this.Owner is MainWindow mw)
                    {
                        try { mw.ClearSimulatorPathOnly(); } catch { }
                    }
                }
                catch { }
                // Clear any previously recorded path for a fresh run
                try { recordedPlayerPath.Clear(); } catch { }
                try { recordedPlayer2Path.Clear(); _prevDualActiveForP2Path = false; } catch { }
                // Don't clear processed portals here - RestartButton_Click handles that before ApplyPortalStatesUpToPosition
                // Respect the global Cam Mode option: when Cam Mode is enabled, disable physics
                try
                {
                    camModeActive = (this.Owner is MainWindow mw2) ? MainWindow.Option_CamMode : false;
                    if (camModeActive)
                    {
                        physicsEnabled = false;
                        // Keep jumpedOnce false so Up/Down act purely as camera pans
                        jumpedOnce = false;
                        // Hide settings panel in cam mode to prevent combo boxes from stealing keyboard focus
                        try { if (SettingsPanel != null) SettingsPanel.Visibility = System.Windows.Visibility.Collapsed; } catch { }
                    }
                    else
                    {
                        // Start physics immediately when Cam Mode is OFF
                        physicsEnabled = true;
                        // Prevent Up/Down from being camera-only
                        jumpedOnce = true;
                        // Show settings panel in normal mode
                        try { if (SettingsPanel != null) SettingsPanel.Visibility = System.Windows.Visibility.Visible; } catch { }
                    }
                }
                catch { }
                // Initialize per-simulator overlay flags from global editor options
                try { ShowTileHitboxes = (this.Owner is MainWindow mw3) ? MainWindow.Option_ShowTileHitboxes : false; } catch { }
                try { ShowSpriteHitboxes = (this.Owner is MainWindow mw4) ? MainWindow.Option_ShowSimulatorSpriteHitboxes : false; } catch { }
            }
            catch { }
        }

        // Stop the background simulation gracefully.
        public void StopSimulation()
        {
            // Invalidate first so an already-running callback cannot re-arm itself.
            Interlocked.Increment(ref simTimerGeneration);
            var timerToDispose = Interlocked.Exchange(ref simTimer, null);
            try { timerToDispose?.Dispose(); } catch { }
            try { simStopwatch.Stop(); } catch { }
        }

        // When the simulator window closes, send the recorded player path back to the editor
        protected override void OnClosed(EventArgs e)
        {
            try
            {
                base.OnClosed(e);
                try
                {
                    if (this.Owner is MainWindow mw)
                    {
                        // Send copies of recorded paths (both player 1 and player 2 if in dual mode)
                        var path1 = new System.Collections.Generic.List<(int x, int y)>(recordedPlayerPath);
                        var path2 = new System.Collections.Generic.List<(int x, int y)>(recordedPlayer2Path);
                        mw.ShowPlayerPathsFromSimulator(path1, path2, dual, completed: levelCompleteTriggered);
                    }
                }
                catch { }
            }
            catch { base.OnClosed(e); }
        }

        // Timer loop invoked on threadpool; accumulates elapsed time and runs fixed-step simulation.
        private void TimerSimulationLoop()
        {
            // Skip if restart is in progress or window is closed
            if (restartInProgress || windowClosed) return;
            
            try
            {
                double now = simStopwatch.Elapsed.TotalMilliseconds;
                double delta = Math.Max(0.0, now - simLastMs);
                // First tick: simLastMs is zero, treat delta as 0 to avoid a large initial jump
                if (simLastMs <= 0.0) delta = 0.0;
                simLastMs = now;
                // Apply time scale to delta - this makes simulation run faster/slower
                simAccumulatedMs += delta * simTimeScale;

                // Run one or more fixed 60Hz steps as needed
                int stepsThisTick = 0;
                pfTickGeneration++; // Advance PF tick generation so PF_GetInput allows one advance
                while (simAccumulatedMs >= SIM_STEP_MS)
                {
                    stepsThisTick++;
                    // Limit to one physics step per timer tick to prevent double-advancing
                    // on lag spikes. pathfinderEnabled was previously checked here but that
                    // field is written on the UI thread and read here on the threadpool;
                    // the non-volatile read could cache a stale 'false', disabling the guard
                    // and causing sporadic 2x/3x X advances that desync PF replay.
                    // Limiting to 1 step per tick is always correct for a 60Hz simulation.
                    if (stepsThisTick > 1)
                    {
                        simAccumulatedMs = 0;
                        break;
                    }
                    try { SimulateNumericStep(); } catch { }
                    try
                    {
                        if (!windowClosed)
                        {
                            Dispatcher?.BeginInvoke(new Action(() =>
                            {
                                if (!windowClosed && !pfSimulating)
                                    RenderFrame();
                            }), System.Windows.Threading.DispatcherPriority.Render);
                        }
                    }
                    catch { }
                    simAccumulatedMs -= SIM_STEP_MS;
                // Path recording now happens in physics routines (SimulateNumericStep)
                
                // OOB death / wrap mode: NES x_movement checks screen-relative Y.
                // NES has a bug where dual disables OOB death (!dual guard), but
                // for correctness we enforce OOB death even during dual so players
                // that fly out of the cam-locked viewport are killed.
                try
                {
                    if (physicsEnabled && jumpedOnce && !paused && !deathTriggered && !MainWindow.Option_NoDeath)
                    {
                        int screenRelY = playerY_fixed - cameraY_fixed;
                        if (!wrapMode)
                        {
                            if (screenRelY < 0x0600 || screenRelY > 0xF900)
                            {
                                AppendSimDebug($"[DEATH] OOB {(screenRelY < 0x0600 ? "top" : "bottom")}: screenRelY=0x{screenRelY:X4}");
                                deathTriggered = true;
                                paused = true;
                                _ = StopMusicAsync();
                                try
                                {
                                    Dispatcher?.BeginInvoke(new Action(() =>
                                    {
                                        try { PauseOverlay.Visibility = System.Windows.Visibility.Collapsed; } catch { }
                                        if (this.Owner is MainWindow mw)
                                        {
                                            try { mw.PauseSimulatorPlayback(); } catch { }
                                            try { mw.AddDeathMarker(playerX_fixed >> 8, playerY_fixed >> 8); } catch { }
                                        }
                                    }));
                                }
                                catch { }
                            }
                        }
                        else
                        {
                            // Wrap mode: wrap Y between 0x0600 and 0xF900 (screen-relative)
                            if (screenRelY < 0x0600)
                            {
                                playerY_fixed = cameraY_fixed + 0xF900;
                                AppendSimDebug($"[WRAP] top->bottom: newY=0x{playerY_fixed:X4}");
                            }
                            else if (screenRelY > 0xF900)
                            {
                                playerY_fixed = cameraY_fixed + 0x0600;
                                AppendSimDebug($"[WRAP] bottom->top: newY=0x{playerY_fixed:X4}");
                            }
                        }
                    }
                }
                catch { }
                }
            }
            catch { }
        }

        private bool upHeld = false;
        private bool downHeld = false;
        // Has the player performed their first jump? When false, Up/Down act as camera-only.
        private bool jumpedOnce = false;
        // Per-frame input polling state (set on UI thread, consumed by numeric sim)
        private bool prevKeyXDown = false;
        private int keyXPressedCount = 0; // edge-detected press counter (atomic)
        private bool keyXHeld = false;    // current held state
        // Atomic flags to indicate whether a recorded press/hold started while the player
        // was on the ground. These are set by the UI/polling on the press edge and
        // consumed by the numeric thread when it processes pending presses.
        private int keyXPressStartedOnGroundInt = 0; // 0 = false, 1 = true
        private int keyXHeldStartedOnGroundInt = 0; // 0 = false, 1 = true
        // Jump buffer frames (atomic). When >0, a landing will consume this and trigger a jump.
        // tabHeld was used previously; use tabSpeedMultiplier instead.
        // (removed unused field to silence build warning)
        // Pause state controlled by ESC. Start paused so simulator opens paused.
        private bool paused = true;
        // If a death has been triggered by collision, suppress further triggers until reset
        private bool deathTriggered = false;
        // If the end-level trigger (sprite 0x0F) has been reached
        private bool levelCompleteTriggered = false;
        // Track processed end-level triggers to avoid re-triggering
        private System.Collections.Generic.HashSet<int> processedEndLevelTriggers = new System.Collections.Generic.HashSet<int>();
        // Track collected coins (sprite indices that have been picked up)
        private System.Collections.Generic.HashSet<int> collectedCoins = new System.Collections.Generic.HashSet<int>();
        // Original sprite IDs for collected coins (for rendering on level complete screen)
        private System.Collections.Generic.List<(int spriteIndex, int spriteId)> collectedCoinInfo = new System.Collections.Generic.List<(int spriteIndex, int spriteId)>();
        // Death location (tile pixel coordinates)
        private int deathTileX = -1;
        private int deathTileY = -1;
        // Mouse hover ellipses for death debugging
        private System.Windows.Shapes.Ellipse? deathPlayerDot = null;
        private System.Windows.Shapes.Ellipse? deathTileDot = null;
        // Debug window instance
        private DebugInfoWindow? debugWindow = null;
        // When true, the simulator is in camera-only mode: Up/Down pan camera only and physics is disabled
        private bool camModeActive = false;
        // 2D mouse-drag camera panning state.
        private bool twoDDragActive = false;
        private System.Windows.Point twoDDragLastMouse;
        // Render-only pan offsets so simulation camera logic cannot snap drag panning back.
        private int twoDPanX_fixed = 0;
        private int twoDPanY_fixed = 0;
        // Multiplier applied while Tab (or Shift+Tab / Ctrl+Shift+Tab) is held.
        // Default 1 (no extra multiplier). While Tab is down this becomes 2/4/8 per modifiers.
        private int tabSpeedMultiplier = 1;

        // (debug overlay removed)

        public SimulatorWindow(
            int[] tiles,
            int[] sprites,
            int mapWidth,
            int mapHeight,
            ImageSource?[]? tileImages,
            ImageSource?[]? tileTonedImages,
            ImageSource?[]? spriteImages,
            System.Collections.Generic.Dictionary<int, (int offsetX, int offsetY)> spritePixelOffsets,
            System.Collections.Generic.Dictionary<int, (int anchorTileX, int anchorTileY)> spriteAnchors,
            Color backgroundTint,
            Color groundTint,
            Color tileTint,
            Color playerTint,
            bool playerTintEnabled,
            int gridRenderShiftYPx
            , bool forcePreviewMode = true,
            bool hideColorTriggers = false,
            System.Collections.Generic.Dictionary<int, ImageSource?>? previewSpriteMap = null,
            System.Collections.Generic.Dictionary<int, ImageSource?[]>? animationFrames = null,
            ImageSource?[]? sawFrame1TilesTinted = null,
            ImageSource?[]? sawFrame2TilesTinted = null,
            ImageSource?[]? smallSawFrame1TilesTinted = null,
            ImageSource?[]? smallSawFrame2TilesTinted = null,
            ImageSource?[]? largeSawFrame1TilesTinted = null,
            ImageSource?[]? largeSawFrame2TilesTinted = null
            ,
            ImageSource? parallaxBitmap = null,
            ImageSource?[]? parallaxImages = null,
            ImageSource?[]? parallaxTonedImages = null,
            double parallaxX = 1.0,
            double parallaxY = 1.0,
            bool parallaxRepeatX = true,
            bool parallaxRepeatY = true,
            bool hasParallaxLayer = false,
            ImageSource?[]? groundImages = null,
            ImageSource?[]? groundTonedImages = null,
            double groundOffsetY = 0.0,
            bool groundRepeatX = true,
            bool hasGroundLayer = false,
            int groundTileRows = 0,
            int? startingBackgroundColorCode = null,
            int? startingGroundColorCode = null,
            int simulatorScale = 1,
            int? maxFallSpeed = null,
            int startingGameMode = 0,
            int[]? nesSpriteLayer = null,
			NesSpriteRecord[]? nesSpriteRecords = null,
			bool forcePlatformer = false
            )
        {
            InitializeComponent();
            
            // Reset simulator speed to 100% on window open (don't carry over from previous session)
            simTimeScale = 1.0;
            
            // Clear ball toggle request on window creation
            Interlocked.Exchange(ref ballToggleRequested, 0);
            
            try { baseWindowTitle = this.Title ?? "Simulator"; } catch { baseWindowTitle = "Simulator"; }
            // Ensure pause overlay reflects initial paused state
            try { PauseOverlay.Visibility = paused ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed; } catch { }
            this.tiles = tiles.ToArray();
            this.sprites = sprites.ToArray();
            this.simulatorNesSpriteRecords = nesSpriteRecords?.ToArray();
            this.simulatorNesSpriteIds = this.simulatorNesSpriteRecords != null
                ? this.simulatorNesSpriteRecords.Select(record => record.SpriteId).ToArray()
                : nesSpriteLayer != null && nesSpriteLayer.Length == this.sprites.Length
                    ? nesSpriteLayer.ToArray()
                    : this.sprites.ToArray();
            // Build non-empty sprite index for O(N_sprites) scanning instead of O(mapW*mapH)
            var _neList = new System.Collections.Generic.List<int>();
            for (int i = 0; i < this.sprites.Length; i++)
                if (this.sprites[i] >= 0) _neList.Add(i);
            this.nonEmptySpriteIndices = _neList.ToArray();
            this.mapWidth = mapWidth;
            this.mapHeight = mapHeight;
            this.hasGroundLayer = hasGroundLayer;
            this.groundTileRows = groundTileRows;
			this.forcePlatformer = forcePlatformer;
			platformerCollisionMap = new SharedPhysics.CollisionMap(this.tiles,
				mapWidth, mapHeight,
				(hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0);
            {
                int gRTR = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                _sim_nesCoordOffset = (57 - mapHeight + gRTR) * 16;
                int emptyTopRows = 57 - mapHeight;
                if (emptyTopRows < 0)
                    emptyTopRows = 0;
                int minScrollHi = 0;
                int minScrollRow = emptyTopRows;
                while (minScrollRow >= 15)
                {
                    minScrollRow -= 15;
                    minScrollHi++;
                }
                _sim_minScrollYLin = minScrollHi * 240 + ((minScrollRow * 16) | 8);
            }
            this.tileImages = tileImages;
            this.tileTonedImages = tileTonedImages;
            this.spriteImages = spriteImages;
            this.spritePixelOffsets = new System.Collections.Generic.Dictionary<int, (int, int)>(spritePixelOffsets);
            this.spriteAnchors = new System.Collections.Generic.Dictionary<int, (int, int)>(spriteAnchors);
            BuildSimulatorNesSpriteStream();
            InitializeSimulatorNesSlots();
            // Store saw frame originals and initial tinted copies
            this.sawFrame1TilesOrig = sawFrame1TilesTinted != null ? (ImageSource[])sawFrame1TilesTinted.Clone() : null;
            this.sawFrame2TilesOrig = sawFrame2TilesTinted != null ? (ImageSource[])sawFrame2TilesTinted.Clone() : null;
            this.smallSawFrame1TilesOrig = smallSawFrame1TilesTinted != null ? (ImageSource[])smallSawFrame1TilesTinted.Clone() : null;
            this.smallSawFrame2TilesOrig = smallSawFrame2TilesTinted != null ? (ImageSource[])smallSawFrame2TilesTinted.Clone() : null;
            this.largeSawFrame1TilesOrig = largeSawFrame1TilesTinted != null ? (ImageSource[])largeSawFrame1TilesTinted.Clone() : null;
            this.largeSawFrame2TilesOrig = largeSawFrame2TilesTinted != null ? (ImageSource[])largeSawFrame2TilesTinted.Clone() : null;
            // Initialize tinted copies based on provided tileTint
            // Saws should be tinted by the background color triggers, not the tile tint.
            if (backgroundTint.A == 255 && backgroundTint.R == 0 && backgroundTint.G == 0 && backgroundTint.B == 0)
            {
                this.sawFrame1TilesTinted = CreateSolidBlackImages(this.sawFrame1TilesOrig);
                this.sawFrame2TilesTinted = CreateSolidBlackImages(this.sawFrame2TilesOrig);
                this.smallSawFrame1TilesTinted = CreateSolidBlackImages(this.smallSawFrame1TilesOrig);
                this.smallSawFrame2TilesTinted = CreateSolidBlackImages(this.smallSawFrame2TilesOrig);
                this.largeSawFrame1TilesTinted = CreateSolidBlackImages(this.largeSawFrame1TilesOrig);
                this.largeSawFrame2TilesTinted = CreateSolidBlackImages(this.largeSawFrame2TilesOrig);
            }
            else
            {
                this.sawFrame1TilesTinted = CreateHslShiftedImages(this.sawFrame1TilesOrig, backgroundTint, tileTint);
                this.sawFrame2TilesTinted = CreateHslShiftedImages(this.sawFrame2TilesOrig, backgroundTint, tileTint);
                this.smallSawFrame1TilesTinted = CreateHslShiftedImages(this.smallSawFrame1TilesOrig, backgroundTint, tileTint);
                this.smallSawFrame2TilesTinted = CreateHslShiftedImages(this.smallSawFrame2TilesOrig, backgroundTint, tileTint);
                this.largeSawFrame1TilesTinted = CreateHslShiftedImages(this.largeSawFrame1TilesOrig, backgroundTint, tileTint);
                this.largeSawFrame2TilesTinted = CreateHslShiftedImages(this.largeSawFrame2TilesOrig, backgroundTint, tileTint);
            }
            // Simulator-specific tweak: shift sprite 0x2B and 0x2C up 8 pixels to match editor preview
            // Initialize starting game mode
            try { currentGameMode = startingGameMode; _levelStartGameMode = startingGameMode; } catch { currentGameMode = 0; }
            // try { ModeDispatch_ApplyModeState(); } catch { } // REMOVED - fresh port
            try { UpdatePlayerImageForMode(); } catch { }
            try
            {
                const int SPRITE_ID_SHIFT_A = 0x2B;
                const int SPRITE_ID_SHIFT_B = 0x2C;
                if (this.spritePixelOffsets.ContainsKey(SPRITE_ID_SHIFT_A))
                {
                    var prev = this.spritePixelOffsets[SPRITE_ID_SHIFT_A];
                    this.spritePixelOffsets[SPRITE_ID_SHIFT_A] = (prev.Item1, prev.Item2 - 8);
                }
                else
                {
                    this.spritePixelOffsets[SPRITE_ID_SHIFT_A] = (0, -8);
                }

                if (this.spritePixelOffsets.ContainsKey(SPRITE_ID_SHIFT_B))
                {
                    var prev = this.spritePixelOffsets[SPRITE_ID_SHIFT_B];
                    this.spritePixelOffsets[SPRITE_ID_SHIFT_B] = (prev.Item1, prev.Item2 - 8);
                }
                else
                {
                    this.spritePixelOffsets[SPRITE_ID_SHIFT_B] = (0, -8);
                }
            }
            catch { }

            // Configure max fall speed from optional parameter. If the passed value is
            // a small numeric code (e.g. 0x06 or 0x07) convert to fixed-point by
            // shifting left 8; if the value already appears to be a fixed-point
            // value (>= 0x100) use it directly.
            try
            {
                if (maxFallSpeed.HasValue)
                {
                    int v = maxFallSpeed.Value;
                    if (v >= 0x100)
                    {
                        CUBE_MAX_FALLSPEED = v;
                    }
                    else
                    {
                        CUBE_MAX_FALLSPEED = (v << 8);
                    }
                }
                // Initialize effective physics values based on current reversal flag
                UpdateEffectiveGravity();
            }
            catch { }
            this.backgroundTint = backgroundTint;
            this.groundTint = groundTint;
            this.tileTint = tileTint;
            this.playerTint = playerTint;
            this.playerTintEnabled = playerTintEnabled;
            this.gridRenderShiftYPx = gridRenderShiftYPx;
            this.forcePreviewMode = forcePreviewMode;
            this.hideColorTriggers = hideColorTriggers;
            this.previewSpriteMap = previewSpriteMap ?? new System.Collections.Generic.Dictionary<int, ImageSource?>();
            this.animationFrames = animationFrames ?? new System.Collections.Generic.Dictionary<int, ImageSource?[]>();
            // Ensure blue-pad alias IDs 0xFD/0xFE animate the same as canonical 0x0D/0x0E when editor didn't provide aliases
            try
            {
                if (this.animationFrames != null)
                {
                    if (!this.animationFrames.ContainsKey(0xFD) && this.animationFrames.ContainsKey(0x0D))
                    {
                        this.animationFrames[0xFD] = this.animationFrames[0x0D];
                    }
                    if (!this.animationFrames.ContainsKey(0xFE) && this.animationFrames.ContainsKey(0x0E))
                    {
                        this.animationFrames[0xFE] = this.animationFrames[0x0E];
                    }
                }
            }
            catch { }
            this.sawFrame1TilesTinted = sawFrame1TilesTinted;
            this.sawFrame2TilesTinted = sawFrame2TilesTinted;
            this.smallSawFrame1TilesTinted = smallSawFrame1TilesTinted;
            this.smallSawFrame2TilesTinted = smallSawFrame2TilesTinted;
            this.largeSawFrame1TilesTinted = largeSawFrame1TilesTinted;
            this.largeSawFrame2TilesTinted = largeSawFrame2TilesTinted;

            // parallax / ground
            this.parallaxBitmap = parallaxBitmap;
            this.parallaxImages = parallaxImages;
            this.parallaxTonedImages = parallaxTonedImages;
            this.parallaxX = parallaxX;
            this.parallaxY = parallaxY;
            this.parallaxRepeatX = parallaxRepeatX;
            this.parallaxRepeatY = parallaxRepeatY;
            this.hasParallaxLayer = hasParallaxLayer;

            this.groundImages = groundImages;
            this.groundTonedImages = groundTonedImages;
            this.groundOffsetY = groundOffsetY;
            this.groundRepeatX = groundRepeatX;
            this.hasGroundLayer = hasGroundLayer;
            this.groundTileRows = groundTileRows;

            // If caller supplied per-level starting color codes, map them to trigger IDs and
            // apply the resulting tints immediately so the simulator starts with those colors.
            try
            {
                if (startingBackgroundColorCode.HasValue)
                {
                    int sc = startingBackgroundColorCode.Value;
                    int trigger = MapStartingCodeToTrigger(sc, false);
                    // Simulate activation of the background color trigger so the pending-tint
                    // path runs (same as when the player crosses a trigger in-game).
                    try {
                        // Workaround: queue the exact two-trigger runtime sequence on the UI Dispatcher
                        // instead of applying synchronously. This better reproduces the timing of
                        // an in-game crossing (first a different trigger, then the real one).
                        int firstTrigger = trigger + 1;
                        if ((trigger & 0x0F) == 0x0C) firstTrigger = trigger + 4;

                        // Queue first trigger at normal priority
                        try
                        {
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                try
                                {
                                    pendingBgIdx = 0; pendingBgSid = firstTrigger; pendingTintChange = true; pendingTintChangeIsStartup = false;
                                    ApplyPendingTints();
                                }
                                catch { }
                            }), System.Windows.Threading.DispatcherPriority.Normal);
                        }
                        catch { }

                        // Queue the real trigger at ApplicationIdle so it runs after other UI work
                        try
                        {
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                try
                                {
                                    pendingBgIdx = 0; pendingBgSid = trigger; pendingTintChange = true; pendingTintChangeIsStartup = false;
                                    ApplyPendingTints();
                                }
                                catch { }
                            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                        }
                        catch { }
                    } catch { }
                }
            }
            catch { }

            // Diagnostic: report whether spider orb animation frames were provided by the editor
            // (no-op) removed diagnostic logging

            try
            {
                if (startingGroundColorCode.HasValue)
                {
                    int sc = startingGroundColorCode.Value;
                    int trigger = MapStartingCodeToTrigger(sc, true);
                    // Simulate activation of the ground color trigger (special-casing is handled
                    // by ApplyPendingTints when it sees sid==0xCF). Use pending fields so the
                    // UI-thread path applies tints and regenerates toned images.
                    try { pendingGroundIdx = 0; pendingGroundSid = trigger; pendingTintChange = true; pendingTintChangeIsStartup = true; } catch { }
                }
            }
            catch { }

            // Starting tint values have been set above; we'll regenerate toned images later
            // after assets/parallax/ground images are loaded and persistent UI elements exist.

            // If we scheduled pending tint changes from starting codes, apply them now on the UI thread
            // so the toned images and caches are generated immediately.
            try
            {
                if (pendingTintChange)
                {
                    // We're on the UI thread in the constructor; apply immediately.
                    try { ApplyPendingTints(); } catch { }
                    try { EnsureInitialRender(); } catch { }
                    // Keep the simulator paused (PauseOverlay handled earlier via `paused`).
                }
            }
            catch { }

            // Robust fallback: if editor didn't provide parallax/ground data, try to load embedded project assets
            // or synthesize a transparent tile so the simulator always has something to render as a background.
            try
            {
                // (leave loading to the conditional fallbacks below so we don't overwrite editor-provided images)
                if ((!this.hasParallaxLayer) || this.parallaxImages == null || this.parallaxImages.Length == 0)
                {
                    var asm_plx = LoadCachedResourceImage("Assets.parallax.bmp")
                              ?? LoadCachedResourceImage("parallax.bmp");
                    if (asm_plx != null)
                    {
                        var imgs = new ImageSource[] { asm_plx };
                        this.parallaxImages = imgs;
                        this.hasParallaxLayer = true;
                    }

                    if (this.groundImages == null || this.groundImages.Length == 0)
                    {
                        var wb = new WriteableBitmap(TILE, TILE, 96, 96, PixelFormats.Pbgra32, null);
                        try { wb.Lock(); wb.AddDirtyRect(new Int32Rect(0, 0, TILE, TILE)); } finally { try { wb.Unlock(); } catch { } }
                        // Do not freeze WriteableBitmap; it must remain writable for Lock/WritePixels.
                        this.groundImages = new ImageSource[] { wb };
                        this.groundTonedImages = null;
                        this.hasGroundLayer = true;
                    }
                }
            }
            catch { }

            // Ensure decoration sprites pulse even when editor didn't provide animation frames.
            try
            {
                // Use the simulator's effective animationFrames / previewSpriteMap so
                // this works even when the caller passed null and we created defaults.
                if (this.animationFrames != null && this.previewSpriteMap != null)
                {
                    foreach (var id in decorationSpriteIds)
                    {
                        if (!this.animationFrames.ContainsKey(id))
                        {
                            ImageSource? sourceImg = null;
                            if (this.previewSpriteMap.TryGetValue(id, out var pimg) && pimg != null) sourceImg = pimg;
                            else if (this.spriteImages != null && id >= 0 && id < this.spriteImages.Length && this.spriteImages[id] != null) sourceImg = this.spriteImages[id];
                            if (sourceImg != null)
                            {
                                var frames = CreateTwoFramePulse(sourceImg);
                                if (frames != null) this.animationFrames[id] = frames;
                            }
                        }
                    }
                    // Also ensure dash-orbs pulse even when the editor didn't provide two-frame images.
                    int[] dashOrbIds = new int[] { 0x45, 0x46, 0x4C, 0x4D, 0x50, 0x51, 0x5B, 0x5C, 0x5D, 0x5E, 0x59, 0x5A };
                    foreach (var id in dashOrbIds)
                    {
                        if (!this.animationFrames.ContainsKey(id))
                        {
                            ImageSource? sourceImg = null;
                            if (this.previewSpriteMap.TryGetValue(id, out var pimg) && pimg != null) sourceImg = pimg;
                            else if (this.spriteImages != null && id >= 0 && id < this.spriteImages.Length && this.spriteImages[id] != null) sourceImg = this.spriteImages[id];
                            if (sourceImg != null)
                            {
                                var frames = CreateTwoFramePulse(sourceImg);
                                if (frames != null) this.animationFrames[id] = frames;
                            }
                        }
                    }
                }
            }
            catch { }

            // (debug reporting removed)

            // Clamp initial cameraY so visible region fits (fixed-point)
            int maxY_fixed = Math.Max(0, (mapHeight - NES_H) * TILE) << 8;
            // Start the simulator from the bottom of the map by default (unless START POS overrides it)
            if (!hasAppliedStartPos)
            {
                cameraY_fixed = maxY_fixed;
            }

            // If starting in a camlock game mode (ship/ball/UFO/spider/wave/swing/snake),
            // initialize targetCameraY to match the initial camera Y so the camera doesn't
            // snap wildly to 0 on the first frame.
            if (currentGameMode != 0 && currentGameMode != 4 && currentGameMode != 8 && currentGameMode != 9 && currentGameMode != 11)
            {
                targetCameraY_fixed = cameraY_fixed;
            }

            // Setup a high-precision render loop using CompositionTarget and a stopwatch
            timer = new System.Windows.Threading.DispatcherTimer(DispatcherPriority.Render);
            renderStopwatch.Start();
            uiAnimLastMs = renderStopwatch.Elapsed.TotalMilliseconds;
            System.Windows.Media.CompositionTarget.Rendering += CompositionTarget_Rendering;

            this.PreviewKeyDown += SimulatorWindow_KeyDown;
            this.KeyUp += SimulatorWindow_KeyUp;
            this.MouseMove += SimulatorWindow_MouseMove;
            this.Closing += SimulatorWindow_Closing;
            this.Closed += (s, e) =>
            {
                windowClosed = true;
                try { timer.Stop(); } catch { }
                try { StopSimulation(); } catch { }
                try { System.Windows.Media.CompositionTarget.Rendering -= CompositionTarget_Rendering; } catch { }
            };

            RenderCanvas.Width = NES_W * TILE;
            RenderCanvas.Height = NES_H * TILE;

            // Apply simulator scale: size the viewport grid and 3D viewport; canvas stays NES size
            // and LayoutTransform handles pixel-doubling so the image fills the window correctly.
            try
            {
                simulatorScale = Math.Max(1, Math.Min(4, simulatorScale));
                _simulatorDisplayScale = simulatorScale;
                double scaledW = (NES_W * TILE) * simulatorScale;
                double scaledH = (NES_H * TILE) * simulatorScale;

                // Size the containing Grid so it exactly matches the scaled gameplay area.
                if (GameViewportSurface != null)
                {
                    GameViewportSurface.Width  = scaledW;
                    GameViewportSurface.Height = scaledH;
                }
                // Size the 3D viewport to match.
                if (FirstPersonViewport != null)
                {
                    FirstPersonViewport.Width  = scaledW;
                    FirstPersonViewport.Height = scaledH;
                }

                // Scale the 2D canvas via LayoutTransform so content is pixel-doubled.
                var scaleTransform = new System.Windows.Media.ScaleTransform(simulatorScale, simulatorScale);
                RenderCanvas.LayoutTransform = scaleTransform;
                try { PauseOverlay.LayoutTransform = scaleTransform; } catch { }
                try { LevelCompleteOverlay.LayoutTransform = scaleTransform; } catch { }

                // Adjust window size so the scaled canvas fits comfortably.
                // Account for window chrome (~40px) + SettingsPanel debug bar (~60px).
                double widthPadding = 32;
                double heightPadding = 110;
                try { this.Width  = scaledW + widthPadding; } catch { }
                try { this.Height = scaledH + heightPadding; } catch { }
            }
            catch { }
            // Keep nearest-neighbor sampling for bitmaps, but avoid forcing layout rounding/snapping
            try
            {
                System.Windows.Media.RenderOptions.SetBitmapScalingMode(RenderCanvas, BitmapScalingMode.NearestNeighbor);
            }
            catch { }

            // Update window title to include sim time initially
            try { UpdateSimTitle(); } catch { }

            // Ensure the window receives keyboard input for panning

            // Create player visual: try to load `cube.png` from repo root, fall back to magenta rectangle
            try
            {
                // create image control and add to canvas
                playerImage = new System.Windows.Controls.Image { Stretch = Stretch.None };
                System.Windows.Media.RenderOptions.SetBitmapScalingMode(playerImage, BitmapScalingMode.NearestNeighbor);
                System.Windows.Controls.Canvas.SetZIndex(playerImage, 1000);
                RenderCanvas.Children.Add(playerImage);

                // Create trail ghost images (rendered behind player at lower Z-index)
                for (int tg = 0; tg < 3; tg++)
                {
                    trailGhosts[tg] = new System.Windows.Controls.Image { Stretch = Stretch.None, Opacity = 0.5 };
                    System.Windows.Media.RenderOptions.SetBitmapScalingMode(trailGhosts[tg]!, BitmapScalingMode.NearestNeighbor);
                    System.Windows.Controls.Canvas.SetZIndex(trailGhosts[tg]!, 997 - tg); // behind player (1000) and P2 (999)
                    trailGhosts[tg]!.Visibility = Visibility.Collapsed;
                    RenderCanvas.Children.Add(trailGhosts[tg]!);
                }

                // Resolve cube.png relative to executable directory (project root is four levels up from bin)
                try
                {
                    bool loaded = false;
                    string exeDir = AppDomain.CurrentDomain.BaseDirectory ?? ".";
                    string candidate = System.IO.Path.GetFullPath(System.IO.Path.Combine(exeDir, "..\\..\\..\\..\\cube.png"));
                    if (System.IO.File.Exists(candidate))
                    {
                        var bi = new BitmapImage();
                        bi.BeginInit();
                        bi.UriSource = new Uri(candidate);
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.EndInit();
                        bi.Freeze();
                        playerImage.Source = App.EnsureUnfrozenForRender(bi) ?? bi;
                        playerImage.Width = bi.PixelWidth;
                        playerImage.Height = bi.PixelHeight;
                        loaded = true;
                    }

                    // If file path didn't work, attempt to load as an embedded resource from the executing assembly.
                    if (!loaded)
                    {
                        try
                        {
                            var bi = LoadCachedResourceImage("cube.png");
                            if (bi != null)
                            {
                                playerImage.Source = App.EnsureUnfrozenForRender(bi) ?? bi;
                                playerImage.Width = bi.PixelWidth;
                                playerImage.Height = bi.PixelHeight;
                                loaded = true;
                            }
                        }
                        catch { /* ignore embedded load errors */ }
                    }

                    if (!loaded)
                    {
                        // fallback rectangle if image not found
                        playerRect = new System.Windows.Shapes.Rectangle { Width = TILE, Height = TILE, Fill = new SolidColorBrush(Colors.Magenta) };
                        System.Windows.Controls.Canvas.SetZIndex(playerRect, 1000);
                        RenderCanvas.Children.Add(playerRect);
                        playerRect.Visibility = Visibility.Visible;
                        // hide the image control if it's unused
                        playerImage.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        playerImage.Visibility = Visibility.Visible;
                        if (playerRect != null) playerRect.Visibility = Visibility.Collapsed;
                    }
                }
                catch
                {
                    // image load failed; use rectangle fallback
                    playerRect = new System.Windows.Shapes.Rectangle { Width = TILE, Height = TILE, Fill = new SolidColorBrush(Colors.Magenta) };
                    System.Windows.Controls.Canvas.SetZIndex(playerRect, 1000);
                    RenderCanvas.Children.Add(playerRect);
                    playerRect.Visibility = Visibility.Visible;
                    if (playerImage != null) playerImage.Visibility = Visibility.Collapsed;
                }
            }
            catch { }

            // Create player 2 visual for dual mode
            try
            {
                // Create image control for player 2
                player2Image = new System.Windows.Controls.Image { Stretch = Stretch.None };
                System.Windows.Media.RenderOptions.SetBitmapScalingMode(player2Image, BitmapScalingMode.NearestNeighbor);
                System.Windows.Controls.Canvas.SetZIndex(player2Image, 999);  // Slightly lower than player 1
                RenderCanvas.Children.Add(player2Image);

                // Copy the source from player 1 if available
                if (playerImage != null && playerImage.Source != null)
                {
                    player2Image.Source = playerImage.Source;
                    player2Image.Width = playerImage.Width;
                    player2Image.Height = playerImage.Height;
                    player2Image.Visibility = Visibility.Collapsed;  // Hidden until dual mode activates
                }
                else
                {
                    // Fallback rectangle for player 2
                    player2Rect = new System.Windows.Shapes.Rectangle { Width = TILE, Height = TILE, Fill = new SolidColorBrush(Colors.Cyan) };
                    System.Windows.Controls.Canvas.SetZIndex(player2Rect, 999);
                    RenderCanvas.Children.Add(player2Rect);
                    player2Rect.Visibility = Visibility.Collapsed;  // Hidden until dual mode activates
                    player2Image.Visibility = Visibility.Collapsed;
                }
            }
            catch { }
            // Ensure player image matches starting game mode
            try { UpdatePlayerImageForMode(); } catch { }
            // Ensure player image matches starting game mode
            try { UpdatePlayerImageForMode(); } catch { }
            // Ensure the player starts offscreen with just the first outline pixels visible.
            try
            {
                playerVisualWidth = TILE;
                playerVisualHeight = TILE;
                if (playerImage != null && playerImage.Source != null && playerImage.Width > 0)
                {
                    playerVisualWidth = (int)Math.Ceiling(playerImage.Width);
                    playerVisualHeight = (int)Math.Ceiling(playerImage.Height);
                }
                else if (playerRect != null)
                {
                    playerVisualWidth = (int)Math.Ceiling(playerRect.Width);
                    playerVisualHeight = (int)Math.Ceiling(playerRect.Height);
                }

                // reset_level.h starts forced-platformer levels at $1110.
                playerX_fixed = forcePlatformer ? 0x1110 : 0;
                interactionScreenOffset_px = -1;
                invincibleCounter = 8; // NES: invincible_counter = 8 in reset_level
                
                // Initialize Y position on ground (unless START POS overrides this later)
                // Use physics resting position: groundSurface - hitboxH.
                // Normal cube hitbox is 15px; ground surface is at (mapHeight-groundRows)*TILE.
                // This gives Y=369 for a 27-row map with 3 ground rows, so the hitbox
                // bottom (369+15=384) sits exactly ON the implicit ground floor.
                try
                {
                    int groundRowsToReserve = 0;
                    try { if (hasGroundLayer && groundTileRows > 0) groundRowsToReserve = Math.Min(3, groundTileRows); } catch { groundRowsToReserve = 0; }
                    int groundSurface_px = (mapHeight - groundRowsToReserve) * TILE;
                    int cubeHitboxH = 15; // normal cube hitbox height (always starts as normal cube)
                    playerY_fixed = Math.Max(0, groundSurface_px - cubeHitboxH) << 8;
                    int maxPlayerY_fixed = Math.Max(0, (mapHeight * TILE - playerVisualHeight)) << 8;
                    if (playerY_fixed > maxPlayerY_fixed) playerY_fixed = maxPlayerY_fixed;
                }
                catch { playerY_fixed = 0; }
                
                // Reset orb activation state at start
                try { ResetOrbSystem(); } catch { }
                
                // Reset blue pad activation state at start
                try { ResetBluePadSystem(); } catch { }
            }
            catch { }

            this.Loaded += (s, e) => { 
                try { this.Focus(); Keyboard.Focus(this); } catch { }
                // Update UI to reflect starting game mode
                try { UpdateGameModeDisplay(); } catch { }
                try { UpdateSpeedDisplay(); } catch { }
                // Auto-enable pathfinder when precomputed inputs exist
                try
                {
                    bool hasInputs = this.Owner is MainWindow mw && mw.PrecomputedPathfinderInputs != null && mw.PrecomputedPathfinderInputs.Count > 0;
                    // Use saved user preference if set, otherwise auto-enable when inputs exist
                    bool shouldEnable = _pathfinderUserPref.HasValue ? _pathfinderUserPref.Value : hasInputs;
                    if (shouldEnable && hasInputs)
                    {
                        pathfinderEnabled = true;
                        PF_LoadPrecomputedInputs();
                        try { PathfinderCheckBox.IsChecked = true; } catch { }
                        AppendSimDebug($"[PATHFINDER] Auto-enabled with {((MainWindow)this.Owner!).PrecomputedPathfinderInputs!.Count} inputs");
                    }
                    else
                    {
                        pathfinderEnabled = false;
                        try { PathfinderCheckBox.IsChecked = false; } catch { }
                    }
                }
                catch { }
            };
            // Create persistent background / tile-layer / ground children to avoid re-allocating each frame
            try
            {
                bgRectPersistent = new System.Windows.Shapes.Rectangle
                {
                    Width = RenderCanvas.Width,
                    Height = RenderCanvas.Height,
                    Fill = new SolidColorBrush(backgroundTint)
                };
                System.Windows.Controls.Canvas.SetLeft(bgRectPersistent, 0);
                System.Windows.Controls.Canvas.SetTop(bgRectPersistent, 0);
                RenderCanvas.Children.Add(bgRectPersistent);

                // Tile layer image: cache full tile block into a RenderTargetBitmap and blit
                tileLayerImage = new System.Windows.Controls.Image
                {
                    Width = (NES_W + 1) * TILE,
                    Height = (NES_H + 1) * TILE,
                    Stretch = Stretch.None
                };
                System.Windows.Media.RenderOptions.SetBitmapScalingMode(tileLayerImage, BitmapScalingMode.NearestNeighbor);
                // Snap to device pixels and use aliased edge mode to avoid 1-px seams when translating
                tileLayerImage.SnapsToDevicePixels = true;
                System.Windows.Media.RenderOptions.SetEdgeMode(tileLayerImage, EdgeMode.Aliased);
                RenderCanvas.Children.Add(tileLayerImage);
                try { System.Windows.Controls.Canvas.SetZIndex(tileLayerImage, 0); } catch { }

                // Sprite layer image: all sprites rendered into a single RTB each frame
                spriteLayerImage = new System.Windows.Controls.Image
                {
                    Width = (NES_W + 1) * TILE,
                    Height = (NES_H + 1) * TILE,
                    Stretch = Stretch.None,
                    IsHitTestVisible = false
                };
                System.Windows.Media.RenderOptions.SetBitmapScalingMode(spriteLayerImage, BitmapScalingMode.NearestNeighbor);
                spriteLayerImage.SnapsToDevicePixels = true;
                System.Windows.Media.RenderOptions.SetEdgeMode(spriteLayerImage, EdgeMode.Aliased);
                RenderCanvas.Children.Add(spriteLayerImage);
                try { System.Windows.Controls.Canvas.SetZIndex(spriteLayerImage, 1); } catch { }

                groundRectPersistent = new System.Windows.Shapes.Rectangle
                {
                    Width = RenderCanvas.Width,
                    Height = TILE * Math.Min(NES_H, 2),
                    Fill = new SolidColorBrush(groundTint) { Opacity = 0.25 },
                    Visibility = Visibility.Collapsed // hide ground overlay temporarily until ground rendering is fixed
                };
                System.Windows.Controls.Canvas.SetLeft(groundRectPersistent, 0);
                System.Windows.Controls.Canvas.SetTop(groundRectPersistent, (NES_H * TILE) - (TILE * Math.Min(NES_H, 2)));
                RenderCanvas.Children.Add(groundRectPersistent);
            }
            catch { }

            // Regenerate toned images now that parallax/ground images and persistent UI elements exist.
            try
            {
                // Emulate triggering object trigger 0xB0 before the initial render so
                // outlines default to white the same way an in-map trigger would.
                try
                {
                    // Use pending fields the same way the numeric sim does when it finds a trigger.
                    pendingTileIdx = 0;             // synthetic index used to mark we've set a trigger
                    pendingTileSid = 0xB0;         // sprite id to emulate
                    pendingTintChange = true;
                    pendingTintChangeIsStartup = true;
                    // Apply immediately (we're on the UI thread in the constructor) so toned images
                    // are generated with the triggered outline color before the renderer runs.
                    try { ApplyPendingTints(); } catch { }
                    try { EnsureInitialRender(); } catch { }
                }
                catch { }
                // Ensure UpdateTonedImagesForTileTint still runs to build any remaining caches.
                try { UpdateTonedImagesForTileTint(tileTint, startupTintApplied ? (Color?)Color.FromArgb(0, 0, 0, 0) : null); } catch { }

                try {
                    if (backgroundTint.A == 255 && backgroundTint.R == 0 && backgroundTint.G == 0 && backgroundTint.B == 0)
                    {
                        parallaxTonedImages = CreateBlackMaskedImages(parallaxImages);
                    }
                    else
                    {
                        parallaxTonedImages = CreateHueShiftedImages(parallaxImages, backgroundTint);
                    }
                } catch { parallaxTonedImages = parallaxImages; }

                try {
                    if (groundTint.A == 255 && groundTint.R == 0 && groundTint.G == 0 && groundTint.B == 0)
                    {
                        // For pure-black ground, produce black-masked images that only affect
                        // non-white/non-player-green pixels; do not recolor the thin seam.
                        try { groundTonedImages = CreateBlackMaskedExceptColorArray(groundImages, playerPlaceholderGreen, Color.FromArgb(0,0,0,0), false); }
                        catch { groundTonedImages = CreateTwoToneTileImages(groundImages, Color.FromArgb(255, 0, 0, 0), Color.FromArgb(255, 0, 0, 0), Color.FromArgb(0,0,0,0)); }
                    }
                    else
                    {
                        groundTonedImages = CreateHueShiftedImages(groundImages, groundTint, tileTint);
                    }
                } catch { groundTonedImages = groundImages; }

                // Forced-outline recolor fallback: if a tile/object tint is present at startup,
                // ensure the ground seam gets recolored to match other tiles.
                try
                {
                    if (tileTint.A > 0 && groundTonedImages != null)
                    {
                        var recol = CreateOutlineTintedTileImages(groundTonedImages, tileTint);
                        if (recol != null) groundTonedImages = recol;
                    }
                }
                catch { }

                // If ground tint is pure black, also create black-masked tile images so
                // tiles that visually represent ground (including common index 0) appear
                // black immediately when the simulator opens paused.
                try
                {
                    if (groundTint.A == 255 && groundTint.R == 0 && groundTint.G == 0 && groundTint.B == 0)
                    {
                        try { tileTonedImages = CreateTwoToneTileImages(tileImages, Color.FromArgb(255, 0, 0, 0), Color.FromArgb(255, 0, 0, 0), Color.FromArgb(0, 0, 0, 0)); } catch { }
                    }
                }
                catch { }

                // Invalidate cached tile layer so the initial render uses new toned images
                tileLayerCache = null;
                try { groundTintedTileCache.Clear(); } catch { }
                try { AppendSimDebug($"Constructor: parallaxToned={(parallaxTonedImages!=null?parallaxTonedImages.Length:0)} groundToned={(groundTonedImages!=null?groundTonedImages.Length:0)} tileToned={(tileTonedImages!=null?tileTonedImages.Length:0)}"); } catch { }
            }
            catch { }

            // Initial background override: if parallax images or bitmap are available, set the persistent
            // background rectangle to use an ImageBrush immediately so the UI shows a background even
            // before the render loop runs.
            try
            {
                ImageSource? src = null;
                if (parallaxBitmapToned != null) src = parallaxBitmapToned;
                else if (parallaxBitmap != null) src = parallaxBitmap;
                else if (parallaxTonedImages != null && parallaxTonedImages.Length > 0 && parallaxTonedImages[0] != null) src = parallaxTonedImages[0];
                else if (parallaxImages != null && parallaxImages.Length > 0 && parallaxImages[0] != null) src = parallaxImages[0];

                if (bgRectPersistent != null && src is BitmapSource pbs)
                {
                    var brushImg = App.EnsureUnfrozenForRender(src) ?? src;
                    var brush = new ImageBrush(brushImg)
                    {
                        TileMode = TileMode.Tile,
                        ViewportUnits = BrushMappingMode.Absolute,
                        Viewport = new Rect(0, 0, Math.Max(1.0, pbs.PixelWidth), Math.Max(1.0, pbs.PixelHeight)),
                        Stretch = Stretch.None
                    };
                    bgRectPersistent.Fill = brush;
                }
            }
            catch { }

            // Write an initial diagnostic snapshot of parallax/ground state for local debugging (Option A)
            try
            {
                AppendSimDebug($"Constructor: hasParallaxLayer={this.hasParallaxLayer}, parallaxBitmap={(this.parallaxBitmap!=null)}, parallaxImagesLen={(this.parallaxImages==null?0:this.parallaxImages.Length)}, parallaxTonedLen={(this.parallaxTonedImages==null?0:this.parallaxTonedImages.Length)}, hasGroundLayer={this.hasGroundLayer}, groundImagesLen={(this.groundImages==null?0:this.groundImages.Length)}, backgroundTint={this.backgroundTint}");
            }
            catch { }

            // (debug overlay removed)

            // (debug overlay removed)

            // Initialize cube physics state
            try { EnableCubePhysics_Fresh(); } catch { }

            // Background simulation timer will be started when the simulator is shown via StartSimulation().
        }

        // Map a tile index to its animated version based on current animation frame
        // Mirrors MainWindow.GetAnimatedTileIndex for saw tiles so simulator can animate tile-based saws
        private int MapAnimatedTileIndex(int originalIndex)
        {
            // Empty/sentinel: tile arrays use -1 (and a few visual-only IDs) for
            // "no collision".  Without this, (byte)(-1) becomes 0xFF and resolves
            // to phantom COL_DOWN_LEFT, breaking forward/floor/death checks.
            if (originalIndex < 0) return 0x00;

            // Simulator forces preview-like behavior when requested
            int mapped = originalIndex;

            // Apply same preview remaps as editor (subset relevant to saws)
            switch (originalIndex)
            {
                case 0xFC:
                case 0xDF:
                case 0xE3:
                case 0xFE:
                case 0xFF: mapped = 0x00; break;
                case 0xFD: mapped = 0x26; break;
            }

            if (mapped >= 0x08 && mapped <= 0x0B)
            {
                // Use the same rhythm as sprite animations (frame math below)
                bool showFrame2 = (((GetEditorAnimationFrameValue() * 9) / 20) % 2) == 1;
                int tileOffset = mapped - 0x08;
                return showFrame2 ? 1004 + tileOffset : 1000 + tileOffset;
            }

            if (mapped == 0x04 || mapped == 0x7D || mapped == 0x7F)
            {
                bool showFrame2 = (((animationFrame * 9) / 20) % 2) == 1;
                int tileOffset = (mapped == 0x04) ? 0 : (mapped == 0x7D) ? 1 : 2;
                return showFrame2 ? 1013 + tileOffset : 1010 + tileOffset;
            }

            if (mapped >= 0x74 && mapped <= 0x7C)
            {
                bool showFrame2 = (((animationFrame * 9) / 20) % 2) == 1;
                int tileOffset = mapped - 0x74;
                return showFrame2 ? 1029 + tileOffset : 1020 + tileOffset;
            }

            return originalIndex;
        }

        private async void SimulatorWindow_KeyDown(object sender, KeyEventArgs e)
        {
            // If an unpause/start request is pending, ignore all additional input
            if (playbackStartPending)
            {
                try { e.Handled = true; } catch { }
                return;
            }
            if (e.Key == Key.Up) upHeld = true;
            if (e.Key == Key.Down) downHeld = true;
            if (e.Key == Key.X || (!MainWindow.Option_CamMode && (e.Key == Key.Up || e.Key == Key.Space)))
            {
                // Only trigger on the initial KeyDown (ignore OS key-repeat)
                try
                {
                    if (!e.IsRepeat)
                    {
                        // When pathfinder is active, PF_InjectInput exclusively controls
                        // keyXPressedCount.  Physical key presses must not corrupt it.
                        if (pathfinderEnabled)
                        {
                            AppendSimDebug($"[KEYDOWN_X] IGNORED (pathfinder active)");
                        }
                        else
                        {
                            AppendSimDebug($"[KEYDOWN_X] Key={e.Key} IsRepeat={e.IsRepeat} CamMode={MainWindow.Option_CamMode} currentGameMode={currentGameMode}");
                            lock (simLock)
                            {
                                // Increment press counter atomically for cube physics
                                // Physics will be ignored if physicsEnabled is false (cam mode)
                                int newCount = Interlocked.Increment(ref keyXPressedCount);
                                AppendSimDebug($"[KEYDOWN_X] Incremented keyXPressedCount to {newCount}");
                                
                                // For ball mode, queue a toggle request
                                if (currentGameMode == 2)
                                {
                                    try { Interlocked.Exchange(ref ballToggleRequested, 1); } catch { }
                                }
                            }
                        }
                    }
                }
                catch { }
            }
            
            // Q key: toggle mini/normal
            if (e.Key == Key.Q)
            {
                AppendSimDebug("[Q_HANDLER] Q key detected");
                try
                {
                    if (!e.IsRepeat)
                    {
                        AppendSimDebug("[Q_HANDLER] Not a repeat");
                        lock (simLock)
                        {
                            AppendSimDebug("[Q_HANDLER] In lock");
                            // Toggle miniMode and sync with currplayer_mini
                            miniMode = !miniMode;
                            currplayer_mini = (byte)(miniMode ? 1 : 0);
                            AppendSimDebug($"[Q_HANDLER] Set miniMode to {miniMode}, currplayer_mini to {currplayer_mini}");
                            currplayer_table_idx = (currplayer_gravity != 0 ? 1 : 0) | (currplayer_mini != 0 ? 4 : 0);
                            try { UpdatePlayerIconFlip(); } catch { AppendSimDebug("[Q_HANDLER] UpdatePlayerIconFlip exception"); }
                            try { UpdatePlayerImageForMode(); } catch { AppendSimDebug("[Q_HANDLER] UpdatePlayerImageForMode exception"); }
                            try { UpdatePlayerVisualSizeForMode(); } catch { AppendSimDebug("[Q_HANDLER] UpdatePlayerVisualSizeForMode exception"); }
                            // Sync checkbox state
                            try { MiniCheckBox.IsChecked = miniMode; } catch { AppendSimDebug("[Q_HANDLER] MiniCheckBox update exception"); }
                            AppendSimDebug($"[MINI] Mode now: {(miniMode ? "MINI" : "NORMAL")}");
                            e.Handled = true;
                        }
                    }
                }
                catch (Exception ex) { AppendSimDebug($"[Q_HANDLER] Exception: {ex.Message}"); }
            }
            
            // W key: toggle gravity
            if (e.Key == Key.W)
            {
                try
                {
                    if (!e.IsRepeat)
                    {
                        lock (simLock)
                        {
                            AppendSimDebug("[INPUT] W key pressed - toggling gravity!");
                            // Always toggle gravity regardless of No-Death mode
                            gravityReversed = !gravityReversed;
                            gravityFlipped = gravityReversed;
                            effectiveInvertedByW = gravityReversed;
                            gravityFlippedThisFrame = true;  // Mark that gravity flipped this frame
                            
                            // CRITICAL: Reset collision zeroing flag when gravity flips
                            // This allows gravity to apply on the next frame even if velocity was zeroed
                            wasZeroedByCollisionLastFrame = false;
                            
                            AppendSimDebug($"[GRAVITY_FLIP_FLAG] Set gravityFlippedThisFrame=true for modes that need unsticking");
                            currplayer_gravity = (byte)(gravityReversed ? 0xFF : 0x00);
                            currplayer_table_idx = (currplayer_gravity != 0 ? 1 : 0) | (currplayer_mini != 0 ? 4 : 0);
                            UpdatePlayerIconFlip();
                            try { UpdateEffectiveGravity(); } catch { }
                            onGround = false;
                            groundStabilizeCounter = 0;
                            try { if (currentGameMode == 2) ballGoingDown = !gravityReversed; } catch { }
                            AppendSimDebug($"[GRAVITY] Gravity now: {(gravityReversed ? "INVERTED" : "NORMAL")}");
                        }
                    }
                }
                catch { }
            }
            
            // Shift+F12: toggle Y-velocity overlay for debugging
            if (e.Key == Key.F12 && (Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            {
                try
                {
                    showYVelocityOverlay = !showYVelocityOverlay;
                    if (showYVelocityOverlay)
                    {
                        try
                        {
                            if (yVelTextBlock == null)
                            {
                                yVelTextBlock = new System.Windows.Controls.TextBlock();
                                yVelTextBlock.Foreground = new SolidColorBrush(Colors.Yellow);
                                yVelTextBlock.FontWeight = FontWeights.Bold;
                                yVelTextBlock.FontSize = 14;
                                yVelTextBlock.IsHitTestVisible = false;
                                RenderCanvas.Children.Add(yVelTextBlock);
                                try { System.Windows.Controls.Canvas.SetZIndex(yVelTextBlock, 2000); } catch { }
                            }
                        }
                        catch { }
                    }
                    else
                    {
                        try { if (yVelTextBlock != null) yVelTextBlock.Visibility = Visibility.Collapsed; } catch { }
                    }
                    try { RenderFrame(); } catch { }
                }
                catch { }
            }

            // Shift+F11: toggle debug info window
            if (e.Key == Key.F11 && (Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            {
                try
                {
                    if (debugWindow == null || !debugWindow.IsLoaded)
                    {
                        // Create and show debug window
                        debugWindow = new DebugInfoWindow();
                        
                        // Position to the right of the simulator window
                        debugWindow.Owner = this;
                        debugWindow.Left = this.Left + this.ActualWidth;
                        debugWindow.Top = this.Top;
                        
                        debugWindow.Closed += (s, args) => debugWindow = null;
                        debugWindow.Show();
                    }
                    else
                    {
                        // Close debug window
                        debugWindow.Close();
                        debugWindow = null;
                    }
                }
                catch { }
            }

            // Speed adjustment: '-' decrease sim time scale by 10%, '+' increase by 10% (even 10% steps)
            if (e.Key == Key.OemMinus || e.Key == Key.Subtract)
            {
                try
                {
                    simTimeScale = Math.Max(0.1, Math.Round((simTimeScale - 0.1) * 10.0) / 10.0);
                    try { UpdateSimTitle(); } catch { }
                    try { if (this.Owner is MainWindow mw) { mw.SetSimulatorPlaybackRate(simTimeScale); } } catch { }
                }
                catch { }
            }
            if (e.Key == Key.OemPlus || e.Key == Key.Add)
            {
                try
                {
                    simTimeScale = Math.Min(4.0, Math.Round((simTimeScale + 0.1) * 10.0) / 10.0);
                    try { UpdateSimTitle(); } catch { }
                    try { if (this.Owner is MainWindow mw) { mw.SetSimulatorPlaybackRate(simTimeScale); } } catch { }
                }
                catch { }
            }
            if (e.Key == Key.W)
            {
                try
                {
                    if (!e.IsRepeat)
                    {
                        lock (simLock)
                        {
                            // Already handled above in main key input section
                        }
                    }
                }
                catch { }
            }
            if (e.Key == Key.F2)
            {
                try
                {
                    if (!e.IsRepeat)
                    {
                        bool newState = !ShowTileHitboxes;
                        ShowTileHitboxes = newState;
                        try
                        {
                            MainWindow.Option_ShowTileHitboxes = newState;
                            if (Application.Current != null)
                            {
                                try { if (Application.Current.MainWindow is MainWindow mw && mw.MenuOptionTileHitboxes != null) mw.MenuOptionTileHitboxes.IsChecked = newState; } catch { }
                                foreach (Window w2 in Application.Current.Windows)
                                {
                                    try { if (w2 is SimulatorWindow sw2) sw2.ShowTileHitboxes = newState; } catch { }
                                }
                            }
                        }
                        catch { }
                        try { MainWindow.ShowTransientInfo($"Show Tile Hitboxes: {(newState ? "ON" : "OFF")}", this, 1500); } catch { }
                    }
                }
                catch { }
            }
            if (e.Key == Key.F3)
            {
                try
                {
                    if (!e.IsRepeat)
                    {
                        ShowSpriteHitboxes = !ShowSpriteHitboxes;
                        try
                        {
                            MainWindow.Option_ShowSimulatorSpriteHitboxes = ShowSpriteHitboxes;
                            if (Application.Current != null)
                            {
                                try { if (Application.Current.MainWindow is MainWindow mw && mw.MenuOptionShowSpriteHitboxes != null) mw.MenuOptionShowSpriteHitboxes.IsChecked = ShowSpriteHitboxes; } catch { }
                                foreach (Window w2 in Application.Current.Windows)
                                {
                                    try { if (w2 is SimulatorWindow sw2) sw2.ShowSpriteHitboxes = ShowSpriteHitboxes; } catch { }
                                }
                            }
                        }
                        catch { }
                        try { MainWindow.ShowTransientInfo($"Show Sprite Hitboxes: {(ShowSpriteHitboxes ? "ON" : "OFF")}", this, 1500); } catch { }
                    }
                }
                catch { }
            }
            if (e.Key == Key.Tab)
            {
                // compute tab multiplier based on modifiers: Tab=2x, Shift+Tab=4x, Ctrl+Shift+Tab=8x
                bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
                if (shift && ctrl) tabSpeedMultiplier = 8;
                else if (shift) tabSpeedMultiplier = 4;
                else tabSpeedMultiplier = 2;
                // Tab speed multiplier previously notified the owner to change audio playback rate.
                // Reverted: do not affect music from simulator key presses.
            }
            if (e.Key == Key.Escape)
            {
                // Don't allow ESC toggle when level is complete — only restart can clear it
                if (levelCompleteTriggered) return;

                // toggle pause. When unpausing, request the owner to start music so music
                // and gameplay begin on the same frame.
                bool wasPaused = paused;
                bool willBePaused = !paused;

                if (wasPaused && !willBePaused)
                {
                    // Unpausing: request the owner to start playback and wait briefly for audio
                    // to begin so audio and gameplay are (more) in sync, then advance one
                    // numeric step and render a frame so the simulator visibly starts.
                    if (!playbackStartPending)
                    {
                        playbackStartPending = true;
                        try
                        {
                            try { if (this.Owner is MainWindow mw) { var t = mw.StartSimulatorPlaybackAsync(); if (t != null) await t; } } catch { }
                            try { SimulateNumericStep(); } catch { }
                            try { RenderFrame(); } catch { }
                            // Reset time accumulator so the timer loop doesn't double-step
                            simAccumulatedMs = 0;
                            simLastMs = simStopwatch.Elapsed.TotalMilliseconds;
                        }
                        finally
                        {
                            playbackStartPending = false;
                        }
                    }
                }

                // Pausing: request the owner to pause music so audio stops when simulator pauses.
                if (!wasPaused && willBePaused)
                {
                    try { if (this.Owner is MainWindow mw) { mw.PauseSimulatorPlayback(); } } catch { }
                }

                paused = willBePaused;

                // Update overlay visibility
                try { PauseOverlay.Visibility = paused ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed; } catch { }
            }
        }

        private void SimulatorWindow_MouseMove(object sender, MouseEventArgs e)
        {
            try
            {
                // Show red death dots when hovering over player after death
                if (!deathTriggered || deathTileX < 0 || deathTileY < 0)
                {
                    // Hide dots if not in death state
                    if (deathPlayerDot != null) deathPlayerDot.Visibility = Visibility.Collapsed;
                    if (deathTileDot != null) deathTileDot.Visibility = Visibility.Collapsed;
                    return;
                }

                // Get mouse position relative to RenderCanvas
                Point mousePos = e.GetPosition(RenderCanvas);
                
                // Get player screen position
                int playerScreenX = (playerX_fixed >> 8) - (cameraX_fixed >> 8);
                int playerScreenY = (playerY_fixed >> 8) - (cameraY_fixed >> 8);
                
                // Check if hovering over player (within player visual bounds)
                bool hoveringPlayer = mousePos.X >= playerScreenX && mousePos.X < playerScreenX + playerVisualWidth &&
                                     mousePos.Y >= playerScreenY && mousePos.Y < playerScreenY + playerVisualHeight;
                
                if (hoveringPlayer)
                {
                    // Create death dots if they don't exist
                    if (deathPlayerDot == null)
                    {
                        deathPlayerDot = new System.Windows.Shapes.Ellipse
                        {
                            Width = 8,
                            Height = 8,
                            Fill = new SolidColorBrush(Colors.Red),
                            IsHitTestVisible = false
                        };
                        RenderCanvas.Children.Add(deathPlayerDot);
                        System.Windows.Controls.Canvas.SetZIndex(deathPlayerDot, 3000);
                    }

                    if (deathTileDot == null)
                    {
                        deathTileDot = new System.Windows.Shapes.Ellipse
                        {
                            Width = 8,
                            Height = 8,
                            Fill = new SolidColorBrush(Colors.Red),
                            IsHitTestVisible = false
                        };
                        RenderCanvas.Children.Add(deathTileDot);
                        System.Windows.Controls.Canvas.SetZIndex(deathTileDot, 3000);
                    }
                    
                    // Position player dot at center of player
                    double playerDotX = playerScreenX + (playerVisualWidth / 2.0) - 4;
                    double playerDotY = playerScreenY + (playerVisualHeight / 2.0) - 4;
                    System.Windows.Controls.Canvas.SetLeft(deathPlayerDot, playerDotX);
                    System.Windows.Controls.Canvas.SetTop(deathPlayerDot, playerDotY);
                    deathPlayerDot.Visibility = Visibility.Visible;
                    
                    // Position tile dot at death tile location (screen coordinates)
                    int deathScreenX = deathTileX - (cameraX_fixed >> 8);
                    int deathScreenY = deathTileY - (cameraY_fixed >> 8);
                    System.Windows.Controls.Canvas.SetLeft(deathTileDot, deathScreenX - 4);
                    System.Windows.Controls.Canvas.SetTop(deathTileDot, deathScreenY - 4);
                    deathTileDot.Visibility = Visibility.Visible;
                }
                else
                {
                    // Hide dots when not hovering
                    if (deathPlayerDot != null) deathPlayerDot.Visibility = Visibility.Collapsed;
                    if (deathTileDot != null) deathTileDot.Visibility = Visibility.Collapsed;
                }
            }
            catch { }
        }

        private void SimulatorWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                windowClosed = true;
                // Stop simulation to prevent render thread errors
                StopSimulation();
                
                // Cleanup RenderCanvas on UI thread
                Dispatcher.Invoke(() =>
                {
                    try { RenderCanvas.Children.Clear(); } catch { }
                }, System.Windows.Threading.DispatcherPriority.Normal);
            }
            catch { }
        }

        private void SimulatorWindow_KeyUp(object sender, KeyEventArgs e)
        {
            // Ignore key-up events while start playback is pending to avoid changing held state.
            if (playbackStartPending)
            {
                try { e.Handled = true; } catch { }
                return;
            }
            if (e.Key == Key.Up) upHeld = false;
            if (e.Key == Key.Down) downHeld = false;
            if (e.Key == Key.X || (!MainWindow.Option_CamMode && (e.Key == Key.Up || e.Key == Key.Space)))
            {
                // Clear any buffered X presses when key is released
                try
                {
                    // When pathfinder is active, PF_InjectInput exclusively controls
                    // keyXPressedCount.  Physical key releases must not wipe it.
                    if (!pathfinderEnabled)
                    {
                        lock (simLock)
                        {
                            try { Interlocked.Exchange(ref keyXPressedCount, 0); } catch { }
                        }
                    }
                }
                catch { }
                // Don't clear ballToggleRequested here - let BallPhysics_Fresh consume it
                // (KeyUp can fire before physics processes the flag, causing it to be lost)
                try { Interlocked.Exchange(ref keyXHeldStartedOnGroundInt, 0); } catch { }
                try { Interlocked.Exchange(ref keyXPressStartedOnGroundInt, 0); } catch { }
                // Clear orb buffer immediately on UI release so holds cannot persist.
                try { orbBufferActive[currplayer] = false; } catch { }
                try { ballInputBufferCountdown[currplayer] = 0; } catch { }
                try { orbHoldConsumed[currplayer] = false; } catch { }
                try { orbHoldConsumedKeyStillDown[currplayer] = false; } catch { }
                try { orbHoldSuppressing[currplayer] = false; } catch { }
            }
            if (e.Key == Key.Tab)
            {
                tabSpeedMultiplier = 1;
            }
        }

        private async void PauseOverlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            try
            {
                // Only respond when currently paused.
                if (!paused)
                    return;

                // If another start is already pending, ignore repeated clicks.
                if (!playbackStartPending)
                {
                    playbackStartPending = true;
                    try
                    {
                        // Start/resume music at correct position
                        if (this.Owner is MainWindow mw)
                        {
                            bool isPlaying = mw.IsMusicPlaying();
                            bool isPaused = mw.IsMusicPaused();
                            AppendSimDebug($"[UNPAUSE] Music state: playing={isPlaying}, paused={isPaused}, hasAppliedStartPos={hasAppliedStartPos}");
                            
                            // If music is not playing (either paused or stopped), start it with proper seeking
                            if (!isPlaying)
                            {
                                double musicTime = 0.0;
                                if (hasAppliedStartPos)
                                {
                                    musicTime = CalculateMusicTimeToPosition(startPosX_forMusicSeek + (playerVisualWidth / 2));
                                    AppendSimDebug($"[UNPAUSE] Starting music with seek to START POS musicTime={musicTime:F3}s");
                                }
                                else
                                {
                                    AppendSimDebug($"[UNPAUSE] Starting music with seek to beginning");
                                }
                                
                                // Stop any existing playback first
                                await mw.StopSimulatorPlaybackAsync();
                                
                                // Start fresh with proper seeking
                                var startTask = mw.ForceStartAndSeekSimulatorPlaybackAsync(musicTime);
                                if (startTask != null) await startTask;
                            }
                            else
                            {
                                AppendSimDebug($"[UNPAUSE] Music already playing, no action needed");
                            }
                        }
                        try { SimulateNumericStep(); } catch { }
                        try { RenderFrame(); } catch { }
                        // Reset time accumulator so the timer loop doesn't double-step
                        simAccumulatedMs = 0;
                        simLastMs = simStopwatch.Elapsed.TotalMilliseconds;
                    }
                    finally
                    {
                        playbackStartPending = false;
                    }
                }

                paused = false;
                try { PauseOverlay.Visibility = System.Windows.Visibility.Collapsed; } catch { }
                
                // Grab focus to enable keyboard input (W, up/down) after unpausing
                try { this.Focus(); } catch { }

                // Mark event handled so underlying canvas doesn't also receive it.
                e.Handled = true;
            }
            catch { }
        }

        private void RenderCanvas_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Grab focus on the window to enable keyboard input (W, up/down)
            // Focus the window itself, not the canvas, so keyboard events reach the window handlers
            this.Focus();

            // 2D camera panning with drag (disabled while 3D mode is active).
            if (!firstPerson3DEnabled && RenderCanvas != null)
            {
                twoDDragActive = true;
                twoDDragLastMouse = e.GetPosition(RenderCanvas);
                try { RenderCanvas.CaptureMouse(); } catch { }
            }
            e.Handled = true;
        }

        private void RenderCanvas_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (RenderCanvas != null)
            {
                twoDDragActive = false;
                try { RenderCanvas.ReleaseMouseCapture(); } catch { }
            }
            e.Handled = true;
        }

        private void RenderCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!twoDDragActive || firstPerson3DEnabled || RenderCanvas == null) return;

            var now = e.GetPosition(RenderCanvas);
            double dx = now.X - twoDDragLastMouse.X;
            double dy = now.Y - twoDDragLastMouse.Y;
            twoDDragLastMouse = now;

            if (Math.Abs(dx) < 0.01 && Math.Abs(dy) < 0.01) return;

            // Convert mouse delta from screen-space to world pixels under current render scale.
            double totalScale = Math.Max(0.01, sim2DZoomScale * Math.Max(1, _simulatorDisplayScale));
            int deltaX_fixed = (int)Math.Round((dx / totalScale) * 256.0);
            int deltaY_fixed = (int)Math.Round((dy / totalScale) * 256.0);

            // Dragging moves the world with the cursor, so render camera moves opposite the mouse.
            int baseCameraX_fixed = cameraX_fixed + twoDPanX_fixed;
            int baseCameraY_fixed = cameraY_fixed + twoDPanY_fixed;
            int newCameraX_fixed = baseCameraX_fixed - deltaX_fixed;
            int newCameraY_fixed = baseCameraY_fixed - deltaY_fixed;

            int maxCameraX_fixed = Math.Max(0, (mapWidth - NES_W) * TILE) << 8;
            int maxCameraY_fixed = Math.Max(0, (mapHeight - NES_H) * TILE) << 8;
            int minCameraY_fixed = 0;

            if (newCameraX_fixed < 0) newCameraX_fixed = 0;
            if (newCameraX_fixed > maxCameraX_fixed) newCameraX_fixed = maxCameraX_fixed;
            if (newCameraY_fixed < minCameraY_fixed) newCameraY_fixed = minCameraY_fixed;
            if (newCameraY_fixed > maxCameraY_fixed) newCameraY_fixed = maxCameraY_fixed;

            // Keep player center within one tile of each viewport edge while dragging.
            int minPlayerScreenX = TILE;
            int maxPlayerScreenX = (NES_W * TILE) - TILE;
            int minPlayerScreenY = TILE;
            int maxPlayerScreenY = (NES_H * TILE) - TILE;

            int playerCenterWorldX = (playerX_fixed >> 8) + (playerVisualWidth / 2);
            int playerCenterWorldY = (playerY_fixed >> 8) + (playerVisualHeight / 2) + gridRenderShiftYPx;

            int newCameraX_px = newCameraX_fixed >> 8;
            int playerCenterScreenX = playerCenterWorldX - newCameraX_px;
            if (playerCenterScreenX < minPlayerScreenX) newCameraX_px = playerCenterWorldX - minPlayerScreenX;
            if (playerCenterScreenX > maxPlayerScreenX) newCameraX_px = playerCenterWorldX - maxPlayerScreenX;

            int newCameraY_px = newCameraY_fixed >> 8;
            int playerCenterScreenY = playerCenterWorldY - newCameraY_px;
            if (playerCenterScreenY < minPlayerScreenY) newCameraY_px = playerCenterWorldY - minPlayerScreenY;
            if (playerCenterScreenY > maxPlayerScreenY) newCameraY_px = playerCenterWorldY - maxPlayerScreenY;

            newCameraX_fixed = newCameraX_px << 8;
            newCameraY_fixed = newCameraY_px << 8;

            if (newCameraX_fixed < 0) newCameraX_fixed = 0;
            if (newCameraX_fixed > maxCameraX_fixed) newCameraX_fixed = maxCameraX_fixed;
            if (newCameraY_fixed < minCameraY_fixed) newCameraY_fixed = minCameraY_fixed;
            if (newCameraY_fixed > maxCameraY_fixed) newCameraY_fixed = maxCameraY_fixed;

            // Persist as render offsets relative to live simulation camera.
            twoDPanX_fixed = newCameraX_fixed - cameraX_fixed;
            twoDPanY_fixed = newCameraY_fixed - cameraY_fixed;
        }

        private void GameModeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_suppressDebugBarEvents) return;
            try
            {
                if (GameModeComboBox.SelectedIndex >= 0)
                {
                    currentGameMode = GameModeComboBox.SelectedIndex;
                    // If switching to a camlock mode, sync targetCameraY to current
                    // camera so the camera stays stable instead of scrolling to 0.
                    if (currentGameMode != 0 && currentGameMode != 4 && currentGameMode != 8 && currentGameMode != 9 && currentGameMode != 11)
                        targetCameraY_fixed = cameraY_fixed;
                    try { UpdatePlayerSpeed(); } catch { }
                    try { UpdateEffectiveGravity(); } catch { }
                    try { UpdatePlayerImageForMode(); } catch { }
                }
            }
            catch { }
        }

        private void MiniCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressDebugBarEvents) return;
            try
            {
                miniMode = MiniCheckBox.IsChecked == true;
                currplayer_mini = (byte)(miniMode ? 1 : 0);
                try { UpdateEffectiveGravity(); } catch { }
                try { UpdatePlayerImageForMode(); } catch { }
                try { UpdatePlayerVisualSizeForMode(); } catch { }
            }
            catch { }
        }

        private void InvertedCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_suppressDebugBarEvents) return;
            try
            {
                // Fix 21: Don't let UI-thread checkbox events modify physics state while
                // the sim is running. Dispatcher callbacks (e.g. InvertedCheckBox.IsChecked = ...)
                // fire asynchronously on the UI thread and race with the background sim thread,
                // corrupting gravityFlipped/currplayer_gravity during dual mode.
                if (physicsEnabled) return;
                
                bool wasInverted = gravityReversed;
                gravityReversed = InvertedCheckBox.IsChecked == true;
                gravityFlipped = gravityReversed;
                currplayer_gravity = (byte)(gravityReversed ? 0xFF : 0x00);
                wasZeroedByCollisionLastFrame = false;  // Reset flag on gravity flip
                
                if (wasInverted != gravityReversed)
                {
                    try { UpdateEffectiveGravity(); } catch { }
                    try { UpdatePlayerIconFlip(); } catch { }
                }
            }
            catch { }
        }

        private void PathfinderCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            lock (simLock)
            {
                pathfinderEnabled = PathfinderCheckBox.IsChecked == true;
                _pathfinderUserPref = pathfinderEnabled;
                if (pathfinderEnabled)
                {
                    // Load precomputed inputs from editor
                    PF_LoadPrecomputedInputs();
                    if (!PF_HasInputData())
                    {
                        AppendSimDebug("[PATHFINDER] No precomputed path data! Use 'Calculate Path' in editor first.");
                    }
                }
                else
                {
                    // Release any injected input
                    Interlocked.Exchange(ref keyXPressedCount, 0);
                    keyXHeld = false;
                    pfInputSequence = null;
                    pfFrameIndex = 0;
                    pfLastAdvancedTick = -1;
                }
            }
        }

        private void SpeedComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_suppressDebugBarEvents) return;
            try
            {
                if (SpeedComboBox.SelectedItem is ComboBoxItem item && item.Tag != null)
                {
                    if (int.TryParse(item.Tag.ToString(), out int speedIndex))
                    {
                        // Speed indices: 0=0.5x, 1=1x, 2=2x, 3=3x, 4=4x, 5=0.1x
                        speed = speedIndex;
                        // Update horizontal movement speed to match speed portal values
                        int[] speedValues = { CUBE_SPEED_X05, CUBE_SPEED_X1, CUBE_SPEED_X2, CUBE_SPEED_X3, CUBE_SPEED_X4, CUBE_SPEED_SLOW };
                        if (speedIndex >= 0 && speedIndex < speedValues.Length)
                        {
                            currentSpeed_fixed = speedValues[speedIndex];
                            playerVelX_fixed = speedValues[speedIndex];
                        }
                        // Update visual display immediately
                        UpdateSpeedDisplay();
                    }
                }
            }
            catch { }
        }
        
        private void UpdateSpeedDisplay()
        {
            try
            {
                int speedIndex = speed;
                if (SpeedComboBox != null && speedIndex >= 0 && speedIndex < SpeedComboBox.Items.Count)
                {
                    if (SpeedComboBox.SelectedIndex != speedIndex)
                    {
                        SpeedComboBox.SelectedIndex = speedIndex;
                    }
                }
            }
            catch { }
        }

        private void ApplyNesIntroFreezePrestepForSimulator()
        {
            // Match PathfinderEngine's verified NES intro-freeze final pre-step.
            if (dual) return;

            if (currentGameMode == 5)
            {
                int tableIdx = miniMode ? 4 : 0;
                int spiderGravityStep = GameModePhysics.SPIDER_GRAVITY(tableIdx);
                int maxFallSpeed = GameModePhysics.SPIDER_MAX_FALLSPEED(tableIdx);
                if (gravityFlipped)
                {
                    spiderGravityStep = -spiderGravityStep;
                    maxFallSpeed = -maxFallSpeed;
                }

                int clampMaxY = Math.Max(0, (mapHeight * TILE - playerVisualHeight)) << 8;
                SharedPhysics.CommonGravityRoutine(
                    ref playerVelY_fixed,
                    ref playerY_fixed,
                    spiderGravityStep,
                    maxFallSpeed,
                    currplayer_gravity,
                    dashing[currplayer],
                    gravityMultiplier,
                    simTimeScale,
                    isFullSpeed,
                    playerVelX_fixed,
                    clampMaxY);
                playerX_fixed += playerVelX_fixed;
                ApplySimulatorNesCameraScroll();

                if (invincibleCounter > 0)
                    invincibleCounter--;
                return;
            }

            if (currentGameMode != 0 && currentGameMode != 4) return;

            int gravityStep = SharedPhysics.GetCubeGravity(miniMode);
            if (gravityFlipped)
            {
                gravityStep = -gravityStep;
            }

            playerVelY_fixed += gravityStep;
            playerY_fixed += playerVelY_fixed;
            playerX_fixed += playerVelX_fixed;
            ApplySimulatorNesCubeRobotYScroll();

            // This is a complete NES everything_else tick. reset_level starts
            // invincible_counter at 8 and the tick decrements it after runthecolls.
            if (invincibleCounter > 0)
                invincibleCounter--;
        }
        
        private void UpdateGameModeDisplay()
        {
            try
            {
                if (GameModeComboBox != null && currentGameMode >= 0 && currentGameMode < GameModeComboBox.Items.Count)
                {
                    if (GameModeComboBox.SelectedIndex != currentGameMode)
                    {
                        GameModeComboBox.SelectedIndex = currentGameMode;
                    }
                }
            }
            catch { }
        }

        private async void RestartButton_Click(object sender, RoutedEventArgs e)
        {
            if (restartInProgress || windowClosed) return;
            restartInProgress = true;
            try { RestartButton.IsEnabled = false; } catch { }

            try
            {
                // Stop current simulation
                StopSimulation();
                paused = true;

                // Dispose does not wait for a callback that already entered the
                // numeric step. Wait for that callback to leave simLock; the restart
                // flag prevents it (and every later callback) from entering again.
                lock (simLock) { }

                // Check for START POS marker
                int startX_px = forcePlatformer ? 0x11 : 0;
                int startY_px = 0;
                bool hasStartPos = false;

                try
                {
                    if (this.Owner is MainWindow mw)
                    {
                        if (mw.StartPosMarkerX.HasValue && mw.StartPosMarkerY.HasValue)
                        {
                            startX_px = mw.StartPosMarkerX.Value;
                            startY_px = mw.StartPosMarkerY.Value;
                            hasStartPos = true;
                            startPosX_forMusicSeek = startX_px;
                            hasAppliedStartPos = true; // Update flag for music seeking
                        }
                        else
                        {
                            hasAppliedStartPos = false; // No START POS
                        }
                    }
                }
                catch { }

                // Reset player position to start or START POS marker
                playerX_fixed = (startX_px << 8) |
                    (forcePlatformer && !hasStartPos ? 0x10 : 0);
                currXScrollStop_fixed = 0x5000;
                targetXScrollStop_fixed = 0x5000;
                eject_U = 0;
                eject_D = 0;

                // Calculate interaction screen offset based on START POS
                if (hasStartPos)
                {
                    int playerCenter_fixed = playerX_fixed + ((playerVisualWidth / 2) << 8);
                    // If player is past interaction line, calculate the proper screen offset
                    if (playerCenter_fixed >= INTERACTION_LINE_FIXED)
                    {
                        // In standard GD, the interaction line appears at screen pixel 80
                        // when camera is at X=0. The offset is where interaction line appears
                        // on screen when it's crossed.
                        interactionScreenOffset_px = 80; // Standard GD interaction line screen position
                    }
                    else
                    {
                        // Player is before interaction line - camera stays at 0
                        interactionScreenOffset_px = -1;
                    }
                }
                else
                {
                    interactionScreenOffset_px = -1;
                }

                // Reset player to starting Y position (one tile above ground rows) or marker Y
                if (hasStartPos)
                {
                    playerY_fixed = startY_px << 8;
                    
                    // Clamp to map bounds
                    int maxPlayerY_fixed = Math.Max(0, (mapHeight * TILE - playerVisualHeight)) << 8;
                    if (playerY_fixed > maxPlayerY_fixed) playerY_fixed = maxPlayerY_fixed;
                }
                else
                {
                    // Try NES spawn Y config first, fall back to ground-based default
                    int? cfgSpawnY = ComputeSpawnYFixed();
                    if (cfgSpawnY.HasValue)
                    {
                        playerY_fixed = cfgSpawnY.Value;
                        int maxPlayerY_fixed = Math.Max(0, (mapHeight * TILE - playerVisualHeight)) << 8;
                        if (playerY_fixed > maxPlayerY_fixed) playerY_fixed = maxPlayerY_fixed;
                        if (playerY_fixed < 0) playerY_fixed = 0;
                    }
                    else
                    {
                        try
                        {
                            int groundRowsToReserve = 0;
                            try { if (hasGroundLayer && groundTileRows > 0) groundRowsToReserve = Math.Min(3, groundTileRows); } catch { groundRowsToReserve = 0; }
                            int groundSurface_px = (mapHeight - groundRowsToReserve) * TILE;
                            int cubeHitboxH = 15; // normal cube hitbox height
                            playerY_fixed = Math.Max(0, groundSurface_px - cubeHitboxH) << 8;
                            int maxPlayerY_fixed = Math.Max(0, (mapHeight * TILE - playerVisualHeight)) << 8;
                            if (playerY_fixed > maxPlayerY_fixed) playerY_fixed = maxPlayerY_fixed;
                        }
                        catch { playerY_fixed = 0; }
                    }
                }

                // Reset velocity and physics state
                playerVelY_fixed = 0;
                cubeRotate_fixed = 0;  // Reset cube rotation to frame 0 (upright)
                cubeRotateMini_fixed = 0;  // Reset mini cube rotation to frame 0 (upright)

                // Save current game settings from options BEFORE resetting
                int savedGameMode = currentGameMode;
                bool savedMiniMode = miniMode;
                byte savedGravity = currplayer_gravity;
                bool savedGravityReversed = gravityReversed;
                int savedSpeed = speed;
                int savedPlayerVelX = playerVelX_fixed;

                // Reset gamemode/mini/gravity/speed based on whether we have a START POS
                if (hasStartPos)
                {
                    // With START POS: reset to defaults (Cube, normal gravity, 1x speed)
                    currentGameMode = 0; // Cube
                    miniMode = false;
                    currplayer_mini = 0;
                    currplayer_gravity = 0;
                    gravityReversed = false;
                    gravityFlipped = false;
                    gravityMultiplier = 1.0;  // Reset gravity modifier
                    speed = 1; // 1x speed
                    playerVelX_fixed = CUBE_SPEED_X1;

                    // Update UI to match defaults
                    try { UpdateGameModeDisplay(); } catch { }
                    try { UpdateSpeedDisplay(); } catch { }
#pragma warning disable CS4014
                    try { Dispatcher.BeginInvoke(new Action(() => { 
                        if (MiniCheckBox != null) MiniCheckBox.IsChecked = false;
                        if (InvertedCheckBox != null) InvertedCheckBox.IsChecked = false;
                    })); } catch { }
#pragma warning restore CS4014
                    try { UpdatePlayerImageForMode(); } catch { }
                    try { UpdatePlayerVisualSizeForMode(); } catch { }
                    try { UpdatePlayerIconFlip(); } catch { }
                    try { UpdateEffectiveGravity(); } catch { }
                }
                else
                {
                    // Without START POS: reset to level settings starting game mode
                    // and clear mini/inverted to match initial level state.
                    currentGameMode = _levelStartGameMode;
                    miniMode = false;
                    currplayer_mini = 0;
                    currplayer_gravity = 0;
                    gravityReversed = false;
                    gravityFlipped = false;
                    gravityMultiplier = 1.0;  // Reset gravity modifier
                    speed = savedSpeed;
                    playerVelX_fixed = savedPlayerVelX;

                    // Update UI to match reset settings
                    try { UpdateGameModeDisplay(); } catch { }
                    try { UpdatePlayerImageForMode(); } catch { }
                    try { UpdatePlayerVisualSizeForMode(); } catch { }
                    try { UpdatePlayerIconFlip(); } catch { }
                    try { UpdateEffectiveGravity(); } catch { }
#pragma warning disable CS4014
                    try { Dispatcher.BeginInvoke(new Action(() => { 
                        if (MiniCheckBox != null) MiniCheckBox.IsChecked = false;
                        if (InvertedCheckBox != null) InvertedCheckBox.IsChecked = false;
                    })); } catch { }
#pragma warning restore CS4014
                }

                // Reset camera to starting position or START POS marker
                if (hasStartPos)
                {
                    // Position camera based on interaction offset
                    int playerCenter_fixed_restart = playerX_fixed + ((playerVisualWidth / 2) << 8);
                    if (playerCenter_fixed_restart >= INTERACTION_LINE_FIXED && interactionScreenOffset_px >= 0)
                    {
                        cameraX_fixed = playerX_fixed - (interactionScreenOffset_px << 8);
                    }
                    else
                    {
                        cameraX_fixed = 0;
                    }
                    int maxCameraY_fixed = Math.Max(0, (mapHeight - NES_H) * TILE) << 8;
                    int grReserved_sp = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                    int minCameraY_fixed = -(grReserved_sp * TILE) << 8;
                    cameraY_fixed = Math.Max(minCameraY_fixed, Math.Min(maxCameraY_fixed, playerY_fixed - ((NES_H * TILE / 2) << 8)));
                }
                else
                {
                    cameraX_fixed = 0;
                    // Try NES scroll Y config first, fall back to centering on player
                    int? cfgScrollY = ComputeScrollYFixed();
                    if (cfgScrollY.HasValue)
                    {
                        cameraY_fixed = cfgScrollY.Value;
                    }
                    else
                    {
                        int maxY_fixed = Math.Max(0, (mapHeight - NES_H) * TILE) << 8;
                        int grReserved = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                        int minY_fixed = -(grReserved * TILE) << 8;
                        cameraY_fixed = Math.Max(minY_fixed, Math.Min(maxY_fixed, playerY_fixed - ((NES_H * TILE / 2) << 8)));
                    }
                }

                // Clear paths and processed portals
                try { recordedPlayerPath.Clear(); } catch { }
                try { recordedPlayer2Path.Clear(); _prevDualActiveForP2Path = false; } catch { }
                try { recordedPlayer2Path.Clear(); _prevDualActiveForP2Path = false; } catch { }
                try { processedGravityPortals.Clear(); } catch { }
                try { processedGravityModPortals.Clear(); } catch { }
                try { processedSpeedPortals.Clear(); } catch { }
                try { InitializeSimulatorNesSlots(); } catch { }
                try { processedRandomPortals.Clear(); } catch { }
                try { processedGameModePortals.Clear(); } catch { }
                try { processedMiniPortals.Clear(); } catch { }  // Reset dual/single portal tracking
                try { processedTeleportPortals.Clear(); } catch { }  // Reset teleport portal tracking
                try { processedCamLockPortals.Clear(); } catch { }
                try { processedWrapPortals.Clear(); } catch { }
                nocamlockforced = false;
                wrapMode = false;
                // For camlock game modes, initialize targetCameraY to match current
                // cameraY so the camera doesn't snap wildly on the first frame.
                if (currentGameMode != 0 && currentGameMode != 4 && currentGameMode != 8 && currentGameMode != 9 && currentGameMode != 11)
                    targetCameraY_fixed = cameraY_fixed;
                else
                    targetCameraY_fixed = 0;
                
                // Reset timewarp, player visibility, and trail state
                slowMode = false;
                playerInvis = false;
                forcedTrails = 0;
                simTickCount = 0;
                invincibleCounter = 8;
                Array.Clear(playerOldPosY, 0, playerOldPosY.Length);
                try { processedTimewarpTriggers.Clear(); } catch { }
                try { processedPlayerInvisTriggers.Clear(); } catch { }
                try { processedTrailTriggers.Clear(); } catch { }
                // Hide trail ghosts on restart
                try { for (int tg = 0; tg < 3; tg++) { if (trailGhosts[tg] != null) trailGhosts[tg]!.Visibility = Visibility.Collapsed; } } catch { }
                
                // Reset dual mode state
                dual = false;
                Array.Clear(player_vel_x_fixed, 0, player_vel_x_fixed.Length);
                _prevDualActiveForP2Path = false;
                singlePortalExitPending = false;
                currplayer = 0;
                twoplayer = false;

                // Apply color triggers if using START POS
                if (hasStartPos)
                {
                    try { processedColorTriggers.Clear(); } catch { }
                    try { ApplyColorTriggersUpToPosition(startX_px); } catch { }
                }
                else
                {
                    // No START POS - clear color triggers to use defaults
                    try { processedColorTriggers.Clear(); } catch { }
                }

                try { ResetOrbSystem(); } catch { }
                Array.Clear(simulatorSkullDeathPending, 0, simulatorSkullDeathPending.Length);
                Array.Clear(simulatorSpiderBoundaryDeathPending, 0,
                    simulatorSpiderBoundaryDeathPending.Length);
                try { ResetBluePadSystem(); } catch { }
                try { ResetSlopeState(); } catch { }
                Array.Clear(ninjajumps, 0, ninjajumps.Length);

                // Clear input buffers and reset key state tracking
                try { Interlocked.Exchange(ref keyXPressedCount, 0); } catch { }
                keyXHeld = false;
                prevKeyXDown = false;  // Must reset this too, otherwise edge detection breaks if user is holding key during restart
                upHeld = false;
                downHeld = false;

                // Reset grounded physics state — player starts on the ground, so signal
                // gravity not to apply on the first frame (matching PathfinderEngine).
                wasZeroedByCollisionLastFrame = true;
                onGround = true;
                _sim_scrollYSubpx = 0;
                _sim_exitPortalTimer = 0;

                try { ApplyNesIntroFreezePrestepForSimulator(); } catch { }

                // Restore coins before reset so PF_LoadPrecomputedInputs can
                // re-preseed them from scratch (sprites[idx] must be >= 0).
                foreach (var (spriteIndex, spriteId) in collectedCoinInfo)
                {
                    if (!SimulatorUsesExactNesRecords &&
                        spriteIndex >= 0 && spriteIndex < sprites.Length)
                        sprites[spriteIndex] = spriteId;
                }
                collectedCoins.Clear();
                collectedCoinInfo.Clear();

                // Reset pathfinder frame counter so inputs replay from the beginning
                pfFrameIndex = 0;
                pfLastAdvancedTick = -1;
                if (pathfinderEnabled) PF_LoadPrecomputedInputs();

                // Reset ball/swing state
                ballSwitched[0] = false;
                ballFlipCooldown = 0;
                p2BallHoldCounter = 0;
                Array.Clear(ufoOrbed, 0, ufoOrbed.Length);

                // Return all visible/control state to the paused starting snapshot
                // before any replacement timer is allowed to run.
                paused = true;
                try { PauseOverlay.Visibility = System.Windows.Visibility.Visible; } catch { }
                deathTriggered = false;
                levelCompleteTriggered = false;
                processedEndLevelTriggers.Clear();
                try { LevelCompleteOverlay.Visibility = System.Windows.Visibility.Collapsed; } catch { }

                // Stop music first (same as death) before restarting simulation
                try
                {
                    AppendSimDebug($"[RESTART] Stopping music (isPlaying={this.Owner is MainWindow mw2 && mw2.IsMusicPlaying()})");
                    await StopMusicAsync();
                    AppendSimDebug($"[RESTART] Music stopped (isPlaying={this.Owner is MainWindow mw3 && mw3.IsMusicPlaying()})");
                }
                catch { }
            }
            catch { }
            finally
            {
                restartInProgress = false;
                if (!windowClosed)
                {
                    StartSimulation();
                    try { RestartButton.IsEnabled = true; } catch { }
                }
            }
        }

        public async System.Threading.Tasks.Task StartAndSeekMusicAsync()
        {
            try
            {
                if (this.Owner is MainWindow mw && hasAppliedStartPos)
                {
                    // Start playback
                    var t = mw.StartSimulatorPlaybackAsync();
                    if (t != null)
                    {
                        await t;
                        // Wait for audio to be fully initialized
                        await System.Threading.Tasks.Task.Delay(200).ConfigureAwait(false);
                        // Seek to calculated position
                        double musicTimeSeconds = CalculateMusicTimeToPosition(startPosX_forMusicSeek);
                        mw.SeekSimulatorPlayback(musicTimeSeconds);
                        // Small delay to ensure seek completes
                        await System.Threading.Tasks.Task.Delay(100).ConfigureAwait(false);
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Apply spawn/scroll Y from NES level config when no START POS
        /// marker is active. Called after ApplyStartPosMarker().
        /// </summary>
        public void ApplySpawnScrollIfNoStartPos()
        {
            if (hasAppliedStartPos) return;
            try
            {
                _sim_scrollYSubpx = 0;
                int? spawnY = ComputeSpawnYFixed();
                if (spawnY.HasValue)
                {
                    playerY_fixed = spawnY.Value;
                    int maxPlayerY_fixed = Math.Max(0, (mapHeight * TILE - playerVisualHeight)) << 8;
                    if (playerY_fixed > maxPlayerY_fixed) playerY_fixed = maxPlayerY_fixed;
                    if (playerY_fixed < 0) playerY_fixed = 0;
                }

                int? scrollY = ComputeScrollYFixed();
                if (scrollY.HasValue)
                {
                    cameraY_fixed = scrollY.Value;
                }
                else if (spawnY.HasValue)
                {
                    // Center camera on spawn position when no scroll config
                    int maxCamY_fixed = Math.Max(0, (mapHeight - NES_H) * TILE) << 8;
                    int grReserved = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                    int minCamY_fixed = -(grReserved * TILE) << 8;
                    cameraY_fixed = Math.Max(minCamY_fixed, Math.Min(maxCamY_fixed, playerY_fixed - ((NES_H * TILE / 2) << 8)));
                }
                try { ApplyNesIntroFreezePrestepForSimulator(); } catch { }
            }
            catch { }
        }

        // Public API: start the simulator running and request playback (used by MainWindow to auto-start)
        public async System.Threading.Tasks.Task StartRunningAsync()
        {
            try
            {
                // Only act when currently paused.
                if (!paused) return;

                // If another start is already pending, avoid duplicate starts.
                if (!playbackStartPending)
                {
                    playbackStartPending = true;
                    try
                    {
                        if (this.Owner is MainWindow mw)
                        {
                            // Calculate seek position
                            double musicTime = 0.0;
                            if (hasAppliedStartPos)
                            {
                                musicTime = CalculateMusicTimeToPosition(startPosX_forMusicSeek + (playerVisualWidth / 2));
                                AppendSimDebug($"[START] Starting and seeking to START POS musicTime={musicTime:F3}s");
                            }
                            else
                            {
                                AppendSimDebug($"[START] Starting and seeking to beginning");
                            }
                            
                            // Force start playback with immediate seek (before music plays)
                            var startTask = mw.ForceStartAndSeekSimulatorPlaybackAsync(musicTime);
                            if (startTask != null) await startTask;
                            
                            // Now unpause gameplay (music already playing)
                            paused = false;
                        }
                        else
                        {
                            paused = false;
                            try { if (this.Owner is MainWindow mw2) { var t = mw2.ResumeSimulatorPlaybackAsync(); if (t != null) await t; } } catch { }
                        }
                        
                        // NOTE: removed extra SimulateNumericStep() here — the timer loop
                        // handles all fixed-step simulation. Calling it here caused ±1 frame
                        // jitter depending on timer thread scheduling, breaking determinism.
                        try { RenderFrame(); } catch { }
                    }
                    finally
                    {
                        playbackStartPending = false;
                    }
                }
                try { PauseOverlay.Visibility = System.Windows.Visibility.Collapsed; } catch { }
            }
            catch { }
        }

        // Apply START POS marker position (called after Owner is set)
        private bool hasAppliedStartPos = false;
        private int startPosX_forMusicSeek = 0;

        public bool HasStartPosApplied() => hasAppliedStartPos;

        public void ApplyStartPosMarker()
        {
            try
            {
                if (this.Owner is MainWindow mw)
                {
                    if (mw.StartPosMarkerX.HasValue && mw.StartPosMarkerY.HasValue)
                    {
                        int startX_px = mw.StartPosMarkerX.Value;
                        int startY_px = mw.StartPosMarkerY.Value;
                        
                        playerX_fixed = startX_px << 8;
                        playerY_fixed = startY_px << 8;
                        
                        // Clamp to map bounds
                        int maxPlayerY_fixed = Math.Max(0, (mapHeight * TILE - playerVisualHeight)) << 8;
                        if (playerY_fixed > maxPlayerY_fixed) playerY_fixed = maxPlayerY_fixed;
                        
                        // Calculate proper camera and interaction offset for START POS
                        int playerCenter_fixed = playerX_fixed + ((playerVisualWidth / 2) << 8);
                        if (playerCenter_fixed >= INTERACTION_LINE_FIXED)
                        {
                            // Player is past interaction line - use standard GD offset (80 pixels)
                            interactionScreenOffset_px = 80;
                            // Position camera so player appears at screen X = 80
                            cameraX_fixed = playerX_fixed - (interactionScreenOffset_px << 8);
                        }
                        else
                        {
                            // Player is before interaction line - camera at 0
                            cameraX_fixed = 0;
                            interactionScreenOffset_px = -1;
                        }
                        
                        // Position camera Y centered on player
                        int maxCameraY_fixed = Math.Max(0, (mapHeight - NES_H) * TILE) << 8;
                        int grReserved_spm = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                        int minCameraY_fixed = -(grReserved_spm * TILE) << 8;
                        cameraY_fixed = Math.Max(minCameraY_fixed, Math.Min(maxCameraY_fixed, playerY_fixed - ((NES_H * TILE / 2) << 8)));
                        
                        hasAppliedStartPos = true;
                        startPosX_forMusicSeek = startX_px;
                        
                        // Apply starting speed from level config
                        try
                        {
                            int speedFixed = startingSpeedUiIndex switch
                            {
                                0 => CUBE_SPEED_X05, // 0.5x
                                1 => CUBE_SPEED_X1,   // 1x
                                2 => CUBE_SPEED_X2,   // 2x
                                3 => CUBE_SPEED_X3,   // 3x
                                4 => CUBE_SPEED_X4,   // 4x
                                5 => CUBE_SPEED_SLOW, // 0.1x
                                _ => CUBE_SPEED_X1
                            };
                            playerVelX_fixed = speedFixed;
                            speed = startingSpeedUiIndex;
                            try { UpdateSpeedDisplay(); } catch { }
                        }
                        catch { }
                        
                        // Apply color triggers up to this position
                        ApplyColorTriggersUpToPosition(startX_px);

                        _sim_scrollYSubpx = 0;
                        try { ApplyNesIntroFreezePrestepForSimulator(); } catch { }
                        
                        // Cache the music time for when playback starts
                        // Don't seek here - will seek when playback actually starts
                        try
                        {
                            double musicTime = CalculateMusicTimeToPosition(startX_px + (playerVisualWidth / 2));
                            AppendSimDebug($"[START POS] Applied: X={startX_px}, musicTime={musicTime:F3}s (will seek on unpause)");
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            // Skip ALL Timer_Tick work during pathfinder precomputation to prevent
            // racing with the background thread (Timer_Tick advances playerX_fixed,
            // checks ground support, and polls input — all conflicting with pathfinder).
            if (pfSimulating) return;

            // Poll input on UI thread to generate stable per-frame pressed/held flags for numeric sim
            try
            {
                // Use IsXDownAsync() to get global keyboard state (works even when window is unfocused)
                // Also poll X key directly via WPF so held state works even when IsXDownAsync is disabled
                bool curX = IsXDownAsync() || 
                            System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.X) ||
                            (!MainWindow.Option_CamMode && (System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.Up) || 
                                                             System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.Space)));
                bool curUp = System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.Up);
                bool curDown = System.Windows.Input.Keyboard.IsKeyDown(System.Windows.Input.Key.Down);
                lock (simLock)
                {
                    // When pathfinder is active, PF_InjectInput exclusively controls
                    // keyXHeld / keyXPressedCount inside SimulateNumericStep. 
                    // Do NOT overwrite them here to avoid the race where Timer_Tick
                    // clears the injected input before the jump check reads it.
                    if (pathfinderEnabled)
                    {
                        upHeld = curUp;
                        downHeld = curDown;
                    }
                    else
                    {
                    // Only register an X press edge if the player is currently on the ground (no queued mid-air presses)
                    int maxPlayerY_fixed_poll = Math.Max(0, (mapHeight * TILE - playerVisualHeight)) << 8;
                    // Edge detect X regardless of ground so mid-air jumps (for testing) are possible.
                    // Make the edge sticky until the numeric/UI physics code consumes it so
                    // the background fixed-step sim cannot miss a short UI-frame edge.
                    if (curX && !prevKeyXDown)
                    {
                        // record an edge atomically so numeric sim cannot miss it
                        // ProcessModeXEdge(); // REMOVED - fresh port
                    }
                    prevKeyXDown = curX;
                    keyXHeld = curX;

                    // Also poll Up/Down to avoid missing key events; these set the held flags used by movement logic
                    upHeld = curUp;
                    downHeld = curDown;
                    } // end else (!pathfinderEnabled)
                }
            }
            catch { }

            // When numeric simulation is active, it is the sole authority for
            // movement/collision/camera state. Timer_Tick should only poll input
            // to avoid a second legacy path mutating Y/collision between steps.
            if (simTimer != null)
                return;

            // Keep previous camera center for later anchor detection
            int prevCameraCenter_fixed = cameraX_fixed + ((NES_W * TILE / 2) << 8);

            // Advance player X by current dynamic speed (fixed-point)
            // Holding TAB or modifiers change horizontal movement speed via tabSpeedMultiplier.
            int speedMultiplier = tabSpeedMultiplier;
            int centerOffset_fixed = (TILE / 2) << 8;
            int prevPlayerCenter_fixed = playerX_fixed + centerOffset_fixed;
            // When pathfinder is active, SimulateNumericStep is the sole driver of X advancement.
            // Without this guard, Timer_Tick ALSO advances X, causing double horizontal speed
            // during playback (precompute only advances X once per frame).
            int attemptedPlayerX_fixed = pathfinderEnabled
                ? playerX_fixed  // Don't advance — SimulateNumericStep handles it
                : playerX_fixed + (int)Math.Round((currentSpeed_fixed * speedMultiplier) * simTimeScale);
            int attemptedPlayerCenter_fixed = attemptedPlayerX_fixed + centerOffset_fixed;

            // Move the player forward in world coordinates first
            // CRITICAL: Skip the write-back when pathfinder is active. Even though
            // attemptedPlayerX_fixed == playerX_fixed in that case, the read above
            // is NOT under simLock, so SimulateNumericStep (threadpool) can change
            // playerX_fixed between the read and this write, and writing back the
            // stale value reverts one frame of X advancement, causing a double-step
            // divergence on the next physics frame.
            if (!pathfinderEnabled)
                playerX_fixed = attemptedPlayerX_fixed;
            // When player moves horizontally while considered grounded, clear the
            // stabilization counter so walking off platforms causes immediate fall.
            // Skip this when pathfinder is active — SimulateNumericStep handles
            // ground support inside the lock, avoiding data races.
            if (onGround && !pathfinderEnabled)
            {
                groundStabilizeCounter = 0;
                // Immediately verify the player still has supporting surface underfoot
                // (or overhead when gravity is reversed). If not, clear `onGround`
                // so gravity resumes on the same frame instead of persisting.
                try
                {
                    bool stillSupported = false;
                    if (gravityReversed)
                    {
                        // When gravity is reversed, the ceiling acts like the ground.
                        stillSupported = IsTouchingCeiling();
                    }
                    else
                    {
                        const int HITBOX_W_LOCAL = 15;
                        // Always center on TILE/2 (player pos = 16x16 tile space)
                        int playerCenter_px = (playerX_fixed >> 8) + (TILE / 2);
                        int playerLeft_px = playerCenter_px - (HITBOX_W_LOCAL / 2);
                        int playerRight_px = playerLeft_px + (HITBOX_W_LOCAL - 1);
                        int probeCenter_px = playerLeft_px + (HITBOX_W_LOCAL / 2);
                        int[] probeXs = new int[] { playerLeft_px, probeCenter_px, playerRight_px };
                        // Foot = bottom of actual hitbox (hitboxOffset + hitboxH)
                        int hitboxH_gs = miniMode ? 7 : 15;
                        int hitboxOffY_gs = 0;
                        if (miniMode)
                            hitboxOffY_gs = (currentGameMode == 2) ? 4 : 9;
                        int footWorldY_px = (playerY_fixed >> 8) + hitboxOffY_gs + hitboxH_gs;
                        int tileBelowY_world = footWorldY_px / TILE;
                        int groundRowsToReserve_local = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                        int tileIndexY = tileBelowY_world + groundRowsToReserve_local;
                        if (tileIndexY >= mapHeight)
                        {
                            // Player is over the implicit ground layer — always supported
                            stillSupported = true;
                        }
                        else if (tileIndexY >= 0)
                        {
                            int localY = ((footWorldY_px % TILE) + TILE) % TILE;
                            foreach (int px in probeXs)
                            {
                                int tx = px / TILE;
                                if (tx < 0 || tx >= mapWidth) continue;
                                int tid = tiles[tileIndexY * mapWidth + tx];
                                int useTidForAnim = MapAnimatedTileIndex(tid);
                                int collisionTid = useTidForAnim;
                                if (useTidForAnim >= 1000)
                                {
                                    if (useTidForAnim >= 1000 && useTidForAnim <= 1007)
                                        collisionTid = 0x08 + ((useTidForAnim - 1000) % 4);
                                    else if (useTidForAnim >= 1010 && useTidForAnim <= 1015)
                                    {
                                        int group = (useTidForAnim - 1010) % 3;
                                        collisionTid = (group == 0) ? 0x04 : (group == 1) ? 0x7D : 0x7F;
                                    }
                                    else if (useTidForAnim >= 1020 && useTidForAnim <= 1037)
                                        collisionTid = 0x74 + ((useTidForAnim - 1020) % 9);
                                    else
                                        collisionTid = tid;
                                }
                                var col = MetatileCollisionTable.GetCollision((byte)collisionTid);
                                int tileStartX = tx * TILE;
                                int localX = Math.Max(0, Math.Min(TILE - 1, px - tileStartX));
                                // Require the foot probe pixel to actually lie inside
                                // the tile's solid region. Half-slabs (COL_TOP) only
                                // occupy localY 0..7; without this check the player
                                // would falsely retain support after falling through.
                                if (ProvidesFloorAtColumnStatic(col, localX, out int _) &&
                                    SharedPhysics.TileOccupiesPixel(col, localX, localY))
                                { stillSupported = true; break; }
                            }
                        }
                    }

                    if (!stillSupported)
                    {
                        onGround = false;
                        wasZeroedByCollisionLastFrame = false;  // Clear flag so gravity resumes immediately
                    }
                }
                catch { onGround = false; }
            }

            // If the player just crossed the interaction line this step, capture the
            // screen X (in pixels) where the interaction line appeared so the camera
            // can keep the player anchored there while the player continues moving.
            bool crossedInteraction = prevPlayerCenter_fixed < INTERACTION_LINE_FIXED && attemptedPlayerCenter_fixed >= INTERACTION_LINE_FIXED;
            if (crossedInteraction)
            {
                interactionScreenOffset_px = (INTERACTION_LINE_FIXED >> 8) - (cameraX_fixed >> 8);
            }

            // If the player's center is at/after the interaction line, make the camera
            // follow the player's world movement such that the player's screen X stays
            // at the recorded interaction offset. If no offset recorded, fall back to
            // keeping the player at the interaction line world X.
            int playerCenter_fixed_now = playerX_fixed + centerOffset_fixed;
            if (playerCenter_fixed_now >= INTERACTION_LINE_FIXED)
            {
                if (interactionScreenOffset_px >= 0)
                {
                    // camera = playerX - interactionScreenOffset
                    cameraX_fixed = playerX_fixed - (interactionScreenOffset_px << 8);
                }
                else
                {
                    // No recorded offset (edge case): keep player's center at interaction line
                    cameraX_fixed += attemptedPlayerCenter_fixed - INTERACTION_LINE_FIXED;
                }

                // clamp cameraX to map bounds
                int maxCamera_fixed = Math.Max(0, (mapWidth - NES_W) * TILE) << 8;
                if (cameraX_fixed < 0) cameraX_fixed = 0;
                if (cameraX_fixed > maxCamera_fixed) cameraX_fixed = maxCamera_fixed;
            }
            else
            {
                // player moved left of interaction line: clear recorded offset so crossing will re-capture
                interactionScreenOffset_px = -1;
            }

            // Advance animation frame counter - handled by UI render loop now

            // Vertical player movement & camera-follow behavior
            // Vertical step (2 pixels/frame) in fixed-point (8 fractional bits)
            const int vStep_fixed = 512; // 2 px/frame

            int maxCameraY_fixed = Math.Max(0, (mapHeight - NES_H) * TILE) << 8;
            int maxPlayerY_fixed = Math.Max(0, (mapHeight * TILE - playerVisualHeight)) << 8; // player cannot go below last tile row

            // Compute player's screen Y before movement (pixels)
            int playerScreenY_before = (playerY_fixed >> 8) - (cameraY_fixed >> 8);

            if (upHeld)
            {
                // In cam mode, only pan camera - no player movement or physics
                if (camModeActive)
                {
                    if (cameraY_fixed > 0)
                    {
                        cameraY_fixed -= vStep_fixed;
                        if (cameraY_fixed < 0) cameraY_fixed = 0;
                    }
                }
                else
                {
                    // Normal mode: player movement and camera following
                    // Use player's screen center Y for scrolling decisions to match camera panning behavior
                    int playerCenterScreenY = (playerY_fixed >> 8) + (playerVisualHeight / 2) - (cameraY_fixed >> 8);
                    int topThreshold = 5 * TILE; // 5 tiles from top
                    if (!jumpedOnce && camModeActive)
                    {
                        // Before first jump: Up is camera-only (do not move player)
                        if (cameraY_fixed > 0)
                        {
                            cameraY_fixed -= vStep_fixed;
                            if (cameraY_fixed < 0) cameraY_fixed = 0;
                        }
                    }
                    else
                    {
                        if (!physicsEnabled)
                        {
                            // Try move player up (only if cam mode active)
                            if (camModeActive)
                            {
                                playerY_fixed -= vStep_fixed;
                                if (playerY_fixed < 0) playerY_fixed = 0;

                                // Recompute center after moving the player
                                playerCenterScreenY = (playerY_fixed >> 8) + (playerVisualHeight / 2) - (cameraY_fixed >> 8);
                                // If player's center is at or above the threshold, scroll camera up to follow
                                if (playerCenterScreenY <= topThreshold)
                                {
                                    int need = topThreshold - playerCenterScreenY; // pixels camera should move up
                                    int camMove = Math.Min(need, (cameraY_fixed >> 8));
                                    cameraY_fixed -= (camMove << 8);
                                    if (cameraY_fixed < 0) cameraY_fixed = 0;
                                }
                            }
                        }
                        else
                        {
                            // Physics active: do not move player Y directly, but allow camera to scroll up (only if cam mode active)
                            if (camModeActive && playerCenterScreenY <= topThreshold)
                            {
                                int need = topThreshold - playerCenterScreenY;
                                int camMove = Math.Min(need, (cameraY_fixed >> 8));
                                cameraY_fixed -= (camMove << 8);
                                if (cameraY_fixed < 0) cameraY_fixed = 0;
                            }
                            else
                            {
                                // Manual camera pan while physics is enabled: allow small step when holding Up
                                if (camModeActive && cameraY_fixed > 0)
                                {
                                    cameraY_fixed -= vStep_fixed;
                                    if (cameraY_fixed < 0) cameraY_fixed = 0;
                                }
                            }
                        }
                    }
                }
            }

            if (downHeld)
            {
                // In cam mode, only pan camera - no player movement or physics
                if (camModeActive)
                {
                    if (cameraY_fixed < maxCameraY_fixed)
                    {
                        cameraY_fixed += vStep_fixed;
                        if (cameraY_fixed > maxCameraY_fixed) cameraY_fixed = maxCameraY_fixed;
                    }
                }
                else
                {
                    // Normal mode: player movement and camera following
                    // Attempt to move player down, but if within bottom threshold, prefer to scroll camera first
                    int bottomThreshold = NES_H * TILE - 5 * TILE; // 5 tiles from bottom measured against player center
                    int playerCenterScreenY_down = (playerY_fixed >> 8) + (playerVisualHeight / 2) - (cameraY_fixed >> 8);
                    // If before first jump, Down should pan camera only
                    if (!jumpedOnce && camModeActive)
                    {
                        if (cameraY_fixed < maxCameraY_fixed)
                        {
                            cameraY_fixed += vStep_fixed;
                            if (cameraY_fixed > maxCameraY_fixed) cameraY_fixed = maxCameraY_fixed;
                        }
                    }
                    else
                    {
                        // If player's center is above the bottom threshold, allow moving player down.
                        // If player's center has reached or passed the threshold, scroll the camera instead.
                        if (playerCenterScreenY_down < bottomThreshold)
                        {
                            if (!physicsEnabled && camModeActive)
                            {
                                // Safe to move player down without scrolling
                                // Advance player, then clamp to ground. Keep ordering consistent
                                playerY_fixed += vStep_fixed;
                                if (playerY_fixed > maxPlayerY_fixed) playerY_fixed = maxPlayerY_fixed;
                            }
                            else
                            {
                                // Physics active: do not move player Y directly when using down; let physics control position
                            }
                        }
                        else
                        {
                            // Player within 5 tiles of bottom: scroll camera down first until bottom reached
                            if (camModeActive && cameraY_fixed < maxCameraY_fixed)
                            {
                                // Scroll camera down and advance player world Y by same amount so
                                // Mark that we've jumped at least once
                                jumpedOnce = true;
                                // visually stationary while camera scrolls.
                                cameraY_fixed += vStep_fixed;
                                if (cameraY_fixed > maxCameraY_fixed) cameraY_fixed = maxCameraY_fixed;
                                // If physics not active, advance player so perceived speed matches upward movement
                                if (!physicsEnabled)
                                {
                                    playerY_fixed += vStep_fixed;
                                    if (playerY_fixed > maxPlayerY_fixed) playerY_fixed = maxPlayerY_fixed;
                                }
                            }
                            else
                            {
                                // Camera at bottom: allow player to move down to ground and clamp
                                if (!physicsEnabled)
                                {
                                    playerY_fixed += vStep_fixed;
                                    if (playerY_fixed > maxPlayerY_fixed) playerY_fixed = maxPlayerY_fixed;
                                }
                            }
                        }
                    }
                }
            }

                // Automatic camera-follow while physics is active in numeric path
                // Match Famidash process_y_scroll: cam follows Y for cube(0)/robot(4)/ninja(8)/pogo(9)/football(11), or when nocamlockforced
                try
                {
                    // NES scroll.h cube branch (see camFollowsY above).
                    bool camFollowsY_2 = (currentGameMode == 0 || currentGameMode == 4 || currentGameMode == 8 || currentGameMode == 9 || nocamlockforced);
                    // This UI-tick camera follow is fallback-only. When the numeric timer
                    // is active, running this in parallel races simulation state and can
                    // desync PF replay.
                    if (physicsEnabled && jumpedOnce && !paused && simTimer == null)
                    {
                        if ((!dual || twoplayer) && camFollowsY_2)
                        {
                            ApplySimulatorNesCubeRobotYScroll();
                        }
                        else
                        {
                            // Ship-style smooth scroll: see other site for derivation.
                            // INTEGER-PIXEL comparison (NES `scroll_y` byte-only).
                            int _camPx2 = cameraY_fixed >> 8;
                            int _tgtPx2 = targetCameraY_fixed >> 8;
                            int maxCamY_2 = SimulatorNesMaxCameraY_px() << 8;
                            if (_tgtPx2 > _camPx2)
                            {
                                cameraY_fixed += SHIP_SCROLL_SPEED_UP_FIXED;
                                // Simulator stores world Y. NES screen currplayer_y moves -2px
                                // while scroll_y moves +2px, so world Y is unchanged.
                            }
                            if (cameraY_fixed <= maxCamY_2 && _tgtPx2 < (cameraY_fixed >> 8))
                            {
                                cameraY_fixed -= SHIP_SCROLL_SPEED_DOWN_FIXED;
                                // NES adds SHIP_SCROLL_SPEED to screen currplayer_y here;
                                // camera still takes the 3px NTSC carry path, so world
                                // Y moves by -1px.
                                playerY_fixed -= SHIP_SCROLL_SPEED_DOWN_FIXED - SHIP_SCROLL_SPEED_UP_FIXED;
                                if (dual && !twoplayer && currplayer == 0)
                                    player_y_fixed[1] -= SHIP_SCROLL_SPEED_DOWN_FIXED - SHIP_SCROLL_SPEED_UP_FIXED;
                            }
                            int minCamY_2 = (_sim_minScrollYLin - _sim_nesCoordOffset) << 8;
                            if (cameraY_fixed < minCamY_2)
                            {
                                // The integer top-cap compensation preserves world Y.
                                playerY_fixed -= _sim_scrollYSubpx;
                                if (dual && !twoplayer && currplayer == 0)
                                    player_y_fixed[1] -= _sim_scrollYSubpx;
                                _sim_scrollYSubpx = 0;
                                cameraY_fixed = minCamY_2;
                            }
                            if (cameraY_fixed > maxCamY_2 || (cameraY_fixed == maxCamY_2 && _sim_scrollYSubpx != 0))
                            {
                                // The integer bottom-cap delta is already represented
                                // by clamping cameraY_fixed; only fold in the NES
                                // fractional scroll byte before clearing it.
                                playerY_fixed += _sim_scrollYSubpx;
                                if (dual && !twoplayer && currplayer == 0)
                                    player_y_fixed[1] += _sim_scrollYSubpx;
                                _sim_scrollYSubpx = 0;
                                cameraY_fixed = maxCamY_2;
                            }
                        }
                    }
                }
                catch { }

            // Apply simple physics if enabled: jump -> gravity -> cap -> integrate -> ground collision
            // Only run physics here when the numeric sim timer is not running to avoid double-applying
            if (physicsEnabled && simTimer == null)
            {
                try
                {
                    // Decrement grounded-stabilization counter each numeric step
                    if (groundStabilizeCounter > 0) groundStabilizeCounter--;
                    // Consume UI-frame jump press if present and on-ground (do this before gravity)
                    bool effectiveOnGround_local = onGround || groundStabilizeCounter > 0 || invertedCeilingHoldCounter > 0;
                    bool jumpAppliedThisFrame = false;
                    // NOTE: Fresh physics handlers consume keyXPressedCount themselves.
                    // Do NOT consume it here - just peek for any legacy code paths.
                    int pendingPress = Interlocked.CompareExchange(ref keyXPressedCount, 0, 0); // peek only

                    // Do not age the jump-buffer here (would race with numeric sim).
                    // We'll atomically consume the buffer at the moment of landing below.

                    // Apply gravity only if we did not just apply a jump this frame and if moving vertically
                    // or sufficiently above ground (use epsilon). Also skip gravity while on a grounded surface
                    // to prevent small oscillations between 0 and a gravity increment.
                    if (!jumpAppliedThisFrame && !effectiveOnGround_local && (playerVelY_fixed != 0 || playerY_fixed < maxPlayerY_fixed - LAND_EPS_FIXED))
                    {
                        if (currentGameMode == 1)
                        {
                            try
                            {
                                // Determine whether gravity is "downwards" (positive Y) for numeric sign
                                int gravitySign = gravityReversed ? -1 : 1;
                                // canonical gravity flag already applied via gravityReversed

                                bool movingUpRelative = gravitySign > 0 ? (playerVelY_fixed < 0) : (playerVelY_fixed > 0);
                                bool xheld = IsXDownAsync() || keyXHeld;

                                int tmpMag = movingUpRelative ? (xheld ? SHIP_GRAVITY_HOLD_FALL : SHIP_GRAVITY_BASE)
                                                               : (xheld ? SHIP_GRAVITY_AFTER_HOLD : SHIP_GRAVITY);

                                int tmpgravity = tmpMag * gravitySign;
                                if (xheld) tmpgravity = -tmpgravity; // X = thrust opposite to gravity

                                try { playerVelY_fixed += (int)Math.Round(tmpgravity * simTimeScale * simTimeScale); } catch { playerVelY_fixed += tmpgravity; }

                                try
                                {
                                    // Enforce frame clamps per design: choose clamping bounds depending on gravity direction
                                    if (gravitySign < 0)
                                    {
                                        if (playerVelY_fixed < -SHIP_MAX_FALLSPEED) playerVelY_fixed = -SHIP_MAX_FALLSPEED;
                                        if (playerVelY_fixed > SHIP_MAX_FALLSPEED_HOLD) playerVelY_fixed = SHIP_MAX_FALLSPEED_HOLD;
                                    }
                                    else
                                    {
                                        if (playerVelY_fixed < -SHIP_MAX_FALLSPEED_HOLD) playerVelY_fixed = -SHIP_MAX_FALLSPEED_HOLD;
                                        if (playerVelY_fixed > SHIP_MAX_FALLSPEED) playerVelY_fixed = SHIP_MAX_FALLSPEED;
                                    }
                                }
                                catch { }
                            }
                            catch { try { playerVelY_fixed += (int)Math.Round(effectiveGravity_fixed * gravityMultiplier * simTimeScale * simTimeScale); } catch { playerVelY_fixed += (int)(effectiveGravity_fixed * gravityMultiplier); } }
                        }
                        else if (currentGameMode == 2)
                        {
                            try
                            {
                                // For ball mode, gravity direction is controlled by `ballGoingDown`.
                                int gravitySign = ballGoingDown ? 1 : -1;
                                if (gravityReversed) gravitySign = -gravitySign;
                                // canonical gravity flag already applied via gravityReversed

                                int tmpMag = BALL_GRAVITY;
                                int tmpgravity = tmpMag * gravitySign;

                                try { playerVelY_fixed += (int)Math.Round(tmpgravity * simTimeScale * simTimeScale); } catch { playerVelY_fixed += tmpgravity; }

                                try
                                {
                                    // Enforce sign-aware max-fall similar to other modes:
                                    // compute an effectiveMaxFall (positive when gravity pushes down, negative when up)
                                    int effectiveBallMaxFall = gravitySign >= 0 ? BALL_MAX_FALLSPEED : -BALL_MAX_FALLSPEED;
                                    if (effectiveBallMaxFall >= 0)
                                    {
                                        if (playerVelY_fixed > effectiveBallMaxFall) playerVelY_fixed = effectiveBallMaxFall;
                                    }
                                    else
                                    {
                                        if (playerVelY_fixed < effectiveBallMaxFall) playerVelY_fixed = effectiveBallMaxFall;
                                    }
                                }
                                catch { }
                            }
                            catch { try { playerVelY_fixed += (int)Math.Round(effectiveGravity_fixed * gravityMultiplier * simTimeScale * simTimeScale); } catch { playerVelY_fixed += (int)(effectiveGravity_fixed * gravityMultiplier); } }
                        }
                        else
                        {
                            try { playerVelY_fixed += (int)Math.Round(effectiveGravity_fixed * gravityMultiplier * simTimeScale * simTimeScale); } catch { playerVelY_fixed += (int)(effectiveGravity_fixed * gravityMultiplier); }
                            // cap velocity according to the sign of effectiveMaxFall_fixed
                            try
                            {
                                if (effectiveMaxFall_fixed >= 0)
                                {
                                    if (playerVelY_fixed > effectiveMaxFall_fixed) playerVelY_fixed = effectiveMaxFall_fixed;
                                }
                                else
                                {
                                    if (playerVelY_fixed < effectiveMaxFall_fixed) playerVelY_fixed = effectiveMaxFall_fixed;
                                }
                            }
                            catch { }
                        }
                    }

                    // integrate velocity
                    playerY_fixed += playerVelY_fixed;
                    // Prevent the player's world Y from going above the reserved rows.
                    // With groundRowsToReserve, game Y can be negative (TMX rows 0-2 = Y -48 to -1).
                    // Minimum valid Y = -(groundRowsToReserve * TILE) in fixed-point.
                    {
                        int grToReserve = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                        int minY_fixed = -(grToReserve * TILE) << 8;
                        if (playerY_fixed < minY_fixed) playerY_fixed = minY_fixed;
                    }

                    // Ceiling collision (UI-path): if moving up, check for tiles above player's head that should block upward movement.
                    try
                    {
                        if (playerVelY_fixed < 0)
                        {
                            const int HITBOX_W = 15;
                            int playerCenter_px = (playerX_fixed >> 8) + (playerVisualWidth / 2);
                            int playerLeft_px = playerCenter_px - (HITBOX_W / 2);
                            int playerRight_px = playerLeft_px + (HITBOX_W - 1);
                            int headWorldY_px = (playerY_fixed >> 8);

                            int tileAboveY_world = headWorldY_px / TILE;
                            int groundRowsToReserve_calc = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                            int tileIndexY = tileAboveY_world + groundRowsToReserve_calc;
                            if (tileIndexY >= 0 && tileIndexY < mapHeight)
                            {
                                int leftTileX = playerLeft_px / TILE;
                                int rightTileX = playerRight_px / TILE;
                                bool blocked = false;
                                int blockingTileWorldBottom_px = int.MaxValue;
                                for (int tx = leftTileX; tx <= rightTileX; tx++)
                                {
                                    if (tx < 0 || tx >= mapWidth) continue;
                                    int tid = tiles[tileIndexY * mapWidth + tx];
                                    int useTidForAnim = MapAnimatedTileIndex(tid);
                                    int collisionTid = useTidForAnim;
                                    if (useTidForAnim >= 1000)
                                    {
                                        if (useTidForAnim >= 1000 && useTidForAnim <= 1007)
                                        {
                                            collisionTid = 0x08 + ((useTidForAnim - 1000) % 4);
                                        }
                                        else if (useTidForAnim >= 1010 && useTidForAnim <= 1015)
                                        {
                                            int group = (useTidForAnim - 1010) % 3;
                                            collisionTid = (group == 0) ? 0x04 : (group == 1) ? 0x7D : 0x7F;
                                        }
                                        else if (useTidForAnim >= 1020 && useTidForAnim <= 1037)
                                        {
                                            collisionTid = 0x74 + ((useTidForAnim - 1020) % 9);
                                        }
                                        else
                                        {
                                            collisionTid = tid;
                                        }
                                    }
                                    var col = MetatileCollisionTable.GetCollision((byte)collisionTid);
                                    int tileStartX = tx * TILE;
                                    int localLeft = Math.Max(0, playerLeft_px - tileStartX);
                                    int localRight = Math.Min(TILE - 1, playerRight_px - tileStartX);
                                    for (int lx = localLeft; lx <= localRight; lx++)
                                    {
                                        // When NoDeath is OFF, allow the player's right side to pass through tiles.
                                        // Skip checking any columns that are on or to the right of the player's visual center.
                                        if (!MainWindow.Option_NoDeath)
                                        {
                                            int worldPx = tileStartX + lx;
                                            if (worldPx >= playerCenter_px) continue;
                                        }
                                        if (BlocksCeilingAtColumn(col, lx))
                                        {
                                            blocked = true;
                                            int tileWorldBottom_px = (tileAboveY_world + 1) * TILE;
                                            if (tileWorldBottom_px < blockingTileWorldBottom_px) blockingTileWorldBottom_px = tileWorldBottom_px;
                                            break;
                                        }
                                    }
                                    if (blocked) break;
                                }

                                            if (blocked && blockingTileWorldBottom_px < int.MaxValue)
                                            {
                                                // try { Cube_HandleCeilingCollision_NoLocals(); } catch { } // REMOVED - fresh port
                                            }
                            }
                        }
                    }
                    catch { }

                    // ground collision: try tile-based floor collision first, otherwise fall back to map bottom
                    try
                    {
                        // Hitbox dimensions based on mini mode
                        int HITBOX_W = miniMode ? 8 : 15;
                        int HITBOX_H = miniMode ? 7 : 15;

                        int playerCenter_px = (playerX_fixed >> 8) + (playerVisualWidth / 2);
                        int playerLeft_px = playerCenter_px - (HITBOX_W / 2);
                        int playerRight_px = playerLeft_px + (HITBOX_W - 1);
                        
                        // Calculate foot position accounting for mini mode offset
                        int footWorldY_px = (playerY_fixed >> 8);
                        if (miniMode && !gravityFlipped)
                        {
                            footWorldY_px += 9; // Mini hitbox starts at +9 in normal gravity
                        }
                        footWorldY_px += HITBOX_H; // Add height to get bottom edge

                        int tileBelowY = footWorldY_px / TILE;
                        // If a ground image layer is present we reserve a few bottom rows for ground
                        // rendering. The visible map Y is offset by those reserved rows, so when
                        // converting a world tile Y to the tiles[] map index we must add the
                        // reserved ground rows. Compute the same reservation used by the renderer.
                        try
                        {
                            int groundRowsToReserve_calc = 0;
                            if (hasGroundLayer && groundTileRows > 0) groundRowsToReserve_calc = Math.Min(3, groundTileRows);
                            tileBelowY = tileBelowY + groundRowsToReserve_calc;
                        }
                        catch { }
                        int floorDetected = 0; // 0=false, 1=true
                        int floorTopWorldY_px = (mapHeight * TILE); // default to map bottom

                        // Delegate to centralized helper for floor checks.
                        bool ProvidesFloorAtColumn(MetatileCollision col, int localX, out int topOffsetPx)
                        {
                            return ProvidesFloorAtColumnStatic(col, localX, out topOffsetPx);
                        }

                            if (tileBelowY >= 0 && tileBelowY < mapHeight)
                        {
                            // For all tiles under the player's horizontal span, check a small vertical
                            // window of tiles (the tile under the feet and the one above) so slabs
                            // that occupy the top half of a tile are detected correctly.
                            int leftTileX = playerLeft_px / TILE;
                            int rightTileX = playerRight_px / TILE;

                            int groundRowsToReserve_calc = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                            int startRow = Math.Max(0, tileBelowY - 1);
                            int endRow = Math.Min(mapHeight - 1, tileBelowY);

                            for (int ty = startRow; ty <= endRow; ty++)
                            {
                                for (int tx = leftTileX; tx <= rightTileX; tx++)
                                {
                                    if (tx < 0 || tx >= mapWidth) continue;
                                    int tid = tiles[ty * mapWidth + tx];

                                    int useTidForAnim = MapAnimatedTileIndex(tid);
                                    int collisionTid = useTidForAnim;
                                    if (useTidForAnim >= 1000)
                                    {
                                        if (useTidForAnim >= 1000 && useTidForAnim <= 1007)
                                        {
                                            collisionTid = 0x08 + ((useTidForAnim - 1000) % 4);
                                        }
                                        else if (useTidForAnim >= 1010 && useTidForAnim <= 1015)
                                        {
                                            int group = (useTidForAnim - 1010) % 3; // 0 -> 0x04, 1 -> 0x7D, 2 -> 0x7F
                                            collisionTid = (group == 0) ? 0x04 : (group == 1) ? 0x7D : 0x7F;
                                        }
                                        else if (useTidForAnim >= 1020 && useTidForAnim <= 1037)
                                        {
                                            collisionTid = 0x74 + ((useTidForAnim - 1020) % 9);
                                        }
                                        else
                                        {
                                            collisionTid = tid;
                                        }
                                    }
                                    var col = MetatileCollisionTable.GetCollision((byte)collisionTid);

                                    int tileStartX = tx * TILE;
                                    int localLeft = Math.Max(0, playerLeft_px - tileStartX);
                                    int localRight = Math.Min(TILE - 1, playerRight_px - tileStartX);

                                    int bestTopOffset = int.MaxValue;
                                    bool any = false;
                                    for (int lx = localLeft; lx <= localRight; lx++)
                                    {
                                        // Prefer detecting a local floor first (so bottom-half slabs are honored).
                                        if (ProvidesFloorAtColumn(col, lx, out int off))
                                        {
                                            // FIX: gate by foot pixel actually being inside the slab.
                                            // Without this, a COL_TOP slab in the row above the foot
                                            // is treated as a valid landing surface, teleporting the
                                            // cube up onto it (Famidash kills, sim survived).
                                            // The gate matches SharedPhysics.CheckFloor's NES-correct
                                            // semantics: only register a floor when the probe pixel
                                            // lies inside the tile's solid region.
                                            int tileWorldYTop = (ty - groundRowsToReserve_calc) * TILE;
                                            int footLocalY = footWorldY_px - tileWorldYTop;
                                            if (footLocalY < 0 || footLocalY > 15) continue;
                                            if (!TileOccupiesPixel(col, lx, footLocalY)) continue;

                                            any = true;
                                            if (off < bestTopOffset) bestTopOffset = off;
                                            continue;
                                        }

                                        // If no local floor found at this column and deaths are enabled,
                                        // skip columns on/after the player's visual center to allow
                                        // right-edge passthrough. This avoids ignoring actual floors.
                                        if (!MainWindow.Option_NoDeath)
                                        {
                                            int worldPx = tileStartX + lx;
                                            if (worldPx >= playerCenter_px) continue;
                                        }
                                    }
                                    if (any)
                                    {
                                        int candidateTop = (ty - groundRowsToReserve_calc) * TILE + bestTopOffset;
                                        if (candidateTop < floorTopWorldY_px) floorTopWorldY_px = candidateTop;
                                        floorDetected = 1;
                                    }
                                }
                            }
                        }

                        // If no tile-based floor found but a ground layer exists, treat the top
                        // of the reserved ground rows as a solid floor so landing on ground counts.
                        if (floorDetected == 0)
                        {
                            int groundRowsToReserve_calc2 = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                            if (groundRowsToReserve_calc2 > 0)
                            {
                                int topOfGround_px = (mapHeight - groundRowsToReserve_calc2) * TILE;
                                if (topOfGround_px < floorTopWorldY_px) floorTopWorldY_px = topOfGround_px;
                                floorDetected = 1;
                            }
                        }

                        int floorTop_fixed = (floorDetected == 1) ? ((floorTopWorldY_px - HITBOX_H) << 8) : maxPlayerY_fixed;

                        // Per-frame runtime diagnostics: print the tiles under the player's feet
                        if (runtimePerFrameLog)
                        {
                            try
                            {
                                var sb = new System.Text.StringBuilder();
                                sb.AppendFormat("SIM_FRAME: playerX_px={0} playerY_px={1} footY_px={2} tileBelowY={3}; ", (playerX_fixed >> 8), (playerY_fixed >> 8), footWorldY_px, tileBelowY);
                                int dbgLeft = playerLeft_px / TILE;
                                int dbgRight = playerRight_px / TILE;
                                if (tileBelowY < 0 || tileBelowY >= mapHeight)
                                {
                                    sb.AppendFormat("tileBelowY={0}:out_of_bounds; ", tileBelowY);
                                }
                                else
                                {
                                    for (int tx_dbg = dbgLeft; tx_dbg <= dbgRight; tx_dbg++)
                                    {
                                        if (tx_dbg < 0 || tx_dbg >= mapWidth) { sb.AppendFormat("tx={0}:out_of_bounds; ", tx_dbg); continue; }
                                        int tid_dbg = tiles[tileBelowY * mapWidth + tx_dbg];
                                        int useTid_dbg = MapAnimatedTileIndex(tid_dbg);
                                        int collisionTid_dbg = useTid_dbg;
                                        if (useTid_dbg >= 1000)
                                        {
                                            if (useTid_dbg >= 1000 && useTid_dbg <= 1007)
                                            {
                                                collisionTid_dbg = 0x08 + ((useTid_dbg - 1000) % 4);
                                            }
                                            else if (useTid_dbg >= 1010 && useTid_dbg <= 1015)
                                            {
                                                int group_dbg = (useTid_dbg - 1010) % 3;
                                                collisionTid_dbg = (group_dbg == 0) ? 0x04 : (group_dbg == 1) ? 0x7D : 0x7F;
                                            }
                                            else if (useTid_dbg >= 1020 && useTid_dbg <= 1037)
                                            {
                                                collisionTid_dbg = 0x74 + ((useTid_dbg - 1020) % 9);
                                            }
                                            else
                                            {
                                                collisionTid_dbg = tid_dbg;
                                            }
                                        }
                                        var col_dbg = MetatileCollisionTable.GetCollision((byte)collisionTid_dbg);
                                        sb.AppendFormat("tx={0} tid={1} animRemap={2} collTid=0x{3:X2} coll={4}; ", tx_dbg, tid_dbg, useTid_dbg, collisionTid_dbg, col_dbg);
                                    }
                                }
                                var line = sb.ToString();
                                System.Console.WriteLine(line);
                                try
                                {
                                    System.IO.File.AppendAllText(simDebugFilePath, DateTime.UtcNow.ToString("o") + " " + line + Environment.NewLine);
                                }
                                catch { }
                            }
                            catch { }
                        }
                        else
                        {
                            // If not running runtime diagnostics, perform landing resolution here
                            // by atomically consuming any jump buffer and snapping the player to floor.
                            int buffered_ui = Interlocked.Exchange(ref jumpBufferCounter, 0);
                            // Also read the raw edge-press counter without clearing it so held/edge presses
                            // recorded by the UI poll are considered when landing.
                            int pendingPressesNow = Interlocked.CompareExchange(ref keyXPressedCount, 0, 0);
                            if (!gravityReversed)
                            {
                                if (playerY_fixed >= floorTop_fixed - LAND_EPS_FIXED && playerVelY_fixed >= 0)
                                {
                                    int nudged = floorTop_fixed - (1 << 8);
                                    if (nudged < 0) nudged = 0;
                                    playerY_fixed = nudged;
                                    playerVelY_fixed = 0;
                                    onGround = true;
                                    
                                    groundStabilizeCounter = 2;

                                            // try { Cube_HandleUILanding_NoLocals(); } catch { } // REMOVED - fresh port

                                            // Ball handling (non-cube modes)
                                            if (currentGameMode != 0)
                                            {
                                                int buffered_ball_ui = Interlocked.Exchange(ref ballToggleRequested, 0);
                                                if (currentGameMode == 2 && buffered_ball_ui > 0)
                                                {
                                                    ballGoingDown = !ballGoingDown;
                                                    try { playerVelY_fixed = (int)Math.Round((ballGoingDown ? BALL_IMMEDIATE_VEL : -BALL_IMMEDIATE_VEL) * simTimeScale); } catch { playerVelY_fixed = ballGoingDown ? BALL_IMMEDIATE_VEL : -BALL_IMMEDIATE_VEL; }
                                                    onGround = false;
                                                    jumpedOnce = true;
                                                    LogBallEvent($"UI-Landing: consumed queued toggle; ballGoingDown={ballGoingDown}");
                                                }
                                            }
                                            
                                }
                                else
                                {
                                    onGround = false;
                                    groundStabilizeCounter = 0;
                                }
                            }
                            else
                            {
                                // Reversed gravity: landing occurs when moving upward and reaching the floor from below
                                if (playerY_fixed <= floorTop_fixed + LAND_EPS_FIXED && playerVelY_fixed <= 0)
                                {
                                    int nudged = floorTop_fixed + (1 << 8);
                                    playerY_fixed = nudged;
                                    playerVelY_fixed = 0;
                                    onGround = true;

                                    // try { Cube_HandleUILanding_NoLocals(); } catch { } // REMOVED - fresh port

                                    if (currentGameMode != 0)
                                    {
                                        int buffered_ball_ui = Interlocked.Exchange(ref ballToggleRequested, 0);
                                        if (currentGameMode == 2 && buffered_ball_ui > 0)
                                        {
                                            ballGoingDown = !ballGoingDown;
                                            try { playerVelY_fixed = (int)Math.Round((ballGoingDown ? BALL_IMMEDIATE_VEL : -BALL_IMMEDIATE_VEL) * simTimeScale); } catch { playerVelY_fixed = ballGoingDown ? BALL_IMMEDIATE_VEL : -BALL_IMMEDIATE_VEL; }
                                            onGround = false;
                                            jumpedOnce = true;
                                            LogBallEvent($"UI-ReversedLanding: consumed queued toggle; ballGoingDown={ballGoingDown}");
                                        }
                                    }
                                }
                                else
                                {
                                    onGround = false;
                                }
                            }
                        }
                    }
                    catch { /* keep previous behavior on error */ }
                }
                catch { }
            }

            // Clamp cameraY to valid range after adjustments
            if (cameraY_fixed < 0) cameraY_fixed = 0;
            if (cameraY_fixed > maxCameraY_fixed) cameraY_fixed = maxCameraY_fixed;

            // Final safety clamp: ensure player remains above ground after camera moves
            if (playerY_fixed > maxPlayerY_fixed) { playerY_fixed = maxPlayerY_fixed; playerVelY_fixed = 0; }

            // Additional screen-space enforcement: ensure at least 3 rows of ground remain visible
            // Skip this enforcement when gravity is inverted (allow jumping into ground rows from ceiling)
            try
            {
                if (currplayer_gravity == 0) // Only enforce for normal gravity
                {
                    int playerScreenY_now = (playerY_fixed >> 8) - (cameraY_fixed >> 8) + gridRenderShiftYPx;
                    int allowedBottom_px = (NES_H * TILE) - (3 * TILE); // require 3 rows visible
                    int playerScreenBottom = playerScreenY_now + playerVisualHeight;
                    if (playerScreenBottom > allowedBottom_px)
                    {
                        int desiredPlayerScreenY = allowedBottom_px - playerVisualHeight;
                        int desiredPlayerWorldY = desiredPlayerScreenY + (cameraY_fixed >> 8) - gridRenderShiftYPx;
                        if (desiredPlayerWorldY < 0) desiredPlayerWorldY = 0;
                        int desiredPlayerY_fixed = desiredPlayerWorldY << 8;
                        if (desiredPlayerY_fixed > maxPlayerY_fixed) desiredPlayerY_fixed = maxPlayerY_fixed;
                        playerY_fixed = desiredPlayerY_fixed;
                        // When the UI enforces a screen-space clamp we should treat the player as effectively grounded
                        // (prevent further gravity) and zero vertical velocity so the player doesn't sink while camera constraints apply.
                        // NOTE: Only zero velocity when Fresh physics (simTimer) is NOT running.
                        // Fresh physics handles grounding via CubeEject_Fresh. Zeroing velocity here
                        // kills jump velocity before it can be integrated on the next physics frame.
                        if (simTimer == null)
                        {
                            playerVelY_fixed = 0;
                            onGround = true;
                        }
                    }
                }
            }
            catch { }

            // After moving camera X, check for speed-portal anchor crossings
            try
            {
                // remember previous tints so we can detect changes
                var prevBackgroundTint = backgroundTint;
                var prevTileTint = tileTint;
                var prevGroundTint = groundTint;
                // remember previous forced-black flag so we don't regenerate parallax
                // on unrelated ground-only changes unless the forced-black state toggles
                var prevBackgroundForceSolidBlack = backgroundForceSolidBlack;
                int center_fixed = cameraX_fixed + ((NES_W * TILE / 2) << 8);
                // We moved right relative to world; detect triggers either from a player crossing
                // the fixed interaction line, or from camera movement when the player is already past it.
                int bestAnchor_fixed = int.MaxValue;
                int? newSpeed_fixed = null;

                if (!physicsEnabled && crossedInteraction)
                {
                    // Player crossed the interaction line this step: consider anchors between the
                    // previous player center and the fixed interaction line.
                    for (int _si = 0; _si < nonEmptySpriteIndices.Length; _si++)
                    {
                        int idx = nonEmptySpriteIndices[_si]; int sid = sprites[idx];
                        if (sid < 0) continue;
                        // Portal handling: All 9 gamemode portals
                        try
                        {
                            if (sid == 0x00 || sid == 0x01 || sid == 0x02 || sid == 0x03 || sid == 0x04 || sid == 0x17 || sid == 0x24 || sid == 0x4B || sid == 0x58 || sid == 0x6A || sid == 0x6B || sid == 0x6C)
                            {
                                // NES sprite_collide: Generic.x = high_byte(currplayer_x) + 1, hitbox = CUBE_WIDTH x CUBE_HEIGHT
                                int hitboxW = miniMode ? 8 : 15;
                                int hitboxH = miniMode ? 7 : 15;
                                int playerLeft_px_now = (playerX_fixed >> 8) + 1;
                                int playerRight_px_now = playerLeft_px_now + hitboxW - 1;
                                int playerTop_px_now = (playerY_fixed >> 8);
                                playerTop_px_now += GetMiniSpriteOffsetY();
                                int playerBottom_px_now = playerTop_px_now + hitboxH - 1;

                                if (SpriteIntersectsPlayer(idx, sid, playerLeft_px_now, playerRight_px_now, playerTop_px_now, playerBottom_px_now))
                                {
                                    try
                                    {
                                        int oldMode = currentGameMode;
                                        int newMode = sid switch {
                                            0x00 => 0, // Cube
                                            0x01 => 1, // Ship
                                            0x02 => 2, // Ball
                                            0x03 => 3, // UFO
                                            0x04 => 4, // Robot
                                            0x17 => 5, // Spider
                                            0x24 => 6, // Wave
                                            0x4B => 7, // Swing
                                            0x58 => 8, // Ninja
                                            0x6A => 9, // Pogo
                                            0x6B => 10, // Snake
                                            0x6C => 11, // Football
                                            _ => currentGameMode
                                        };
                                        if (newMode != oldMode)
                                        {
                                            currentGameMode = newMode;
                                            wasZeroedByCollisionLastFrame = false;
                                            try { UpdateGameModeDisplay(); } catch { }
                                            try { UpdateEffectiveGravity(); } catch { }
                                            try { playerVelY_fixed >>= 1; } catch { }
                                        }
                                        try { UpdatePlayerImageForMode(); } catch { }
                                    }
                                    catch { }
                                    break;
                                }
                            }
                        }
                        catch { }
                        
                        // Speed portal handling
                        if (!speedPortalMap.ContainsKey(sid)) continue;

                        // When physics is active, SPEED_P1/SPEED_P2 in sprite
                        // interactions handle speed portals at the correct NES
                        // timing (OLD X, before X advance).  Skip the legacy
                        // camera-path detection to avoid double-application and
                        // wrong-timing speed changes.
                        if (physicsEnabled) continue;

                        int anchorTileX = (spriteAnchors != null && spriteAnchors.TryGetValue(idx, out var a)) ? a.anchorTileX : idx % mapWidth;
                        int anchorX_center_fixed = ((anchorTileX * TILE) + (TILE / 2)) << 8;

                        // When cam mode is OFF, use collision-based activation like gamemode/gravity portals
                        if (!camModeActive)
                        {
                            // NES sprite_collide: Generic.x = high_byte(currplayer_x) + 1
                            int hitboxW_speed = miniMode ? 8 : 15;
                            int hitboxH_speed = miniMode ? 7 : 15;
                            int playerLeft_px_speed = (playerX_fixed >> 8) + 1;
                            int playerRight_px_speed = playerLeft_px_speed + hitboxW_speed - 1;
                            int playerTop_px_speed = (playerY_fixed >> 8);
                            playerTop_px_speed += GetMiniSpriteOffsetY();
                            int playerBottom_px_speed = playerTop_px_speed + hitboxH_speed - 1;
                            
                            if (SpriteIntersectsPlayer(idx, sid, playerLeft_px_speed, playerRight_px_speed, playerTop_px_speed, playerBottom_px_speed))
                            {
                                if (!processedSpeedPortals.Contains(idx))
                                {
                                    if (anchorX_center_fixed < bestAnchor_fixed)
                                    {
                                        bestAnchor_fixed = anchorX_center_fixed;
                                        newSpeed_fixed = speedPortalMap[sid];
                                        processedSpeedPortals.Add(idx);
                                    }
                                }
                            }
                            // No else: once processed, speed portals stay in the set
                            // to prevent SPEED_PRE_P2 from re-triggering old portals.
                        }
                        // When cam mode is ON, use screen threshold logic
                        else if (crossedInteraction)
                        {
                            if (anchorX_center_fixed > prevPlayerCenter_fixed && anchorX_center_fixed <= INTERACTION_LINE_FIXED)
                            {
                                if (!processedSpeedPortals.Contains(idx))
                                {
                                    if (anchorX_center_fixed < bestAnchor_fixed)
                                    {
                                        bestAnchor_fixed = anchorX_center_fixed;
                                        newSpeed_fixed = speedPortalMap[sid];
                                        processedSpeedPortals.Add(idx);
                                    }
                                }
                            }
                            // No else: once processed, speed portals stay in the set.
                        }
                        else
                        {
                            if (center_fixed >= prevCameraCenter_fixed && anchorX_center_fixed > prevCameraCenter_fixed && anchorX_center_fixed <= center_fixed)
                            {
                                if (!processedSpeedPortals.Contains(idx))
                                {
                                    if (anchorX_center_fixed < bestAnchor_fixed)
                                    {
                                        bestAnchor_fixed = anchorX_center_fixed;
                                        newSpeed_fixed = speedPortalMap[sid];
                                        processedSpeedPortals.Add(idx);
                                    }
                                }
                            }
                            // No else: once processed, speed portals stay in the set.
                        }
                    }
                }
                else if (!physicsEnabled)
                {
                    // Player already past interaction line
                    // When cam mode is ON: use camera-centered detection (screen threshold)
                    // When cam mode is OFF: still use collision detection
                    if (camModeActive)
                    {
                        for (int _si = 0; _si < nonEmptySpriteIndices.Length; _si++)
                        {
                            int idx = nonEmptySpriteIndices[_si]; int sid = sprites[idx];
                            if (sid < 0) continue;
                            if (!speedPortalMap.ContainsKey(sid)) continue;

                            int anchorTileX = (spriteAnchors != null && spriteAnchors.TryGetValue(idx, out var a)) ? a.anchorTileX : idx % mapWidth;
                            int anchorX_center_fixed = ((anchorTileX * TILE) + (TILE / 2)) << 8;

                            // NES sprite_collide: Generic.x = high_byte(currplayer_x) + 1
                            int hitboxW_local = miniMode ? 8 : 15;
                            int hitboxH_local = miniMode ? 7 : 15;
                            int playerLeft_px_local = (playerX_fixed >> 8) + 1;
                            int playerRight_px_local = playerLeft_px_local + hitboxW_local - 1;
                            int playerTop_px_local = (playerY_fixed >> 8);
                            playerTop_px_local += GetMiniSpriteOffsetY();
                            int playerBottom_px_local = playerTop_px_local + hitboxH_local - 1;

                            if (SpriteIntersectsPlayer(idx, sid, playerLeft_px_local, playerRight_px_local, playerTop_px_local, playerBottom_px_local))
                            {
                                if (anchorX_center_fixed < bestAnchor_fixed)
                                {
                                    bestAnchor_fixed = anchorX_center_fixed;
                                    newSpeed_fixed = speedPortalMap[sid];
                                }
                            }
                        }
                    }
                }

                // Also detect color-trigger crossings. To reduce sampling cost, only sample a trigger
                // once when it first moves past the interaction/center line. Keep a set of processed anchors so
                // we don't resample every frame while the trigger remains past center.
                int bestBg_fixed = int.MaxValue; int? bgIdx = null; int? bgSid = null;
                int bestTile_fixed = int.MaxValue; int? tileIdx = null; int? tileSid = null;
                int bestGround_fixed = int.MaxValue; int? groundIdx = null; int? groundSid = null;

                for (int _si = 0; !physicsEnabled && _si < nonEmptySpriteIndices.Length; _si++)
                {
                    int idx = nonEmptySpriteIndices[_si]; int sid = sprites[idx];
                    if (sid < 0) continue;

                    // Check if this sprite is a color trigger we care about
                    if (!IsColorTriggerSprite(sid)) continue;

                    // Determine anchor tile X for this sprite: prefer explicit anchor, otherwise use tile position
                    int anchorTileX;
                    if (spriteAnchors != null && spriteAnchors.TryGetValue(idx, out var a2))
                        anchorTileX = a2.anchorTileX;
                    else
                        anchorTileX = idx % mapWidth;

                    int anchorX_center_fixed = ((anchorTileX * TILE) + (TILE / 2)) << 8;

                    if (crossedInteraction)
                    {
                        // During a player crossing, only consider anchors between previous player center and the fixed interaction line.
                        if (anchorX_center_fixed > prevPlayerCenter_fixed && anchorX_center_fixed <= INTERACTION_LINE_FIXED)
                        {
                            if (IsBackgroundTrigger(sid))
                            {
                                if (anchorX_center_fixed < bestBg_fixed) { bestBg_fixed = anchorX_center_fixed; bgIdx = idx; bgSid = sid; }
                            }
                            else if (IsTileTrigger(sid))
                            {
                                if (anchorX_center_fixed < bestTile_fixed) { bestTile_fixed = anchorX_center_fixed; tileIdx = idx; tileSid = sid; }
                            }
                            else if (IsGroundTrigger(sid))
                            {
                                if (anchorX_center_fixed < bestGround_fixed) { bestGround_fixed = anchorX_center_fixed; groundIdx = idx; groundSid = sid; }
                            }
                        }
                        else
                        {
                            // Anchor is to the right of the interaction line; clear processed flags so they can trigger again when recrossed
                            if (processedColorTriggers.Contains(idx) && anchorX_center_fixed > INTERACTION_LINE_FIXED) processedColorTriggers.Remove(idx);
                            if (processedGravityPortals.Contains(idx) && anchorX_center_fixed > INTERACTION_LINE_FIXED) processedGravityPortals.Remove(idx);
                            if (processedGravityModPortals.Contains(idx) && anchorX_center_fixed > INTERACTION_LINE_FIXED) processedGravityModPortals.Remove(idx);
                            if (processedGameModePortals.Contains(idx) && anchorX_center_fixed > INTERACTION_LINE_FIXED) processedGameModePortals.Remove(idx);
                            if (processedOrbs.Contains(idx) && anchorX_center_fixed > INTERACTION_LINE_FIXED) processedOrbs.Remove(idx);
                            // Speed portals stay permanently processed (no Remove) to prevent re-triggering.
                            if (processedTeleportPortals.Contains(idx) && anchorX_center_fixed > INTERACTION_LINE_FIXED) processedTeleportPortals.Remove(idx);
                        }
                    }
                    else
                    {
                        // Camera-centered detection (player already past interaction line)
                        if (center_fixed >= prevCameraCenter_fixed && anchorX_center_fixed > prevCameraCenter_fixed && anchorX_center_fixed <= center_fixed)
                        {
                            if (processedColorTriggers.Contains(idx))
                            {
                                // already handled previously
                            }
                            else
                            {
                                if (IsBackgroundTrigger(sid))
                                {
                                    if (anchorX_center_fixed < bestBg_fixed) { bestBg_fixed = anchorX_center_fixed; bgIdx = idx; bgSid = sid; }
                                }
                                else if (IsTileTrigger(sid))
                                {
                                    if (anchorX_center_fixed < bestTile_fixed) { bestTile_fixed = anchorX_center_fixed; tileIdx = idx; tileSid = sid; }
                                }
                                else if (IsGroundTrigger(sid))
                                {
                                    if (anchorX_center_fixed < bestGround_fixed) { bestGround_fixed = anchorX_center_fixed; groundIdx = idx; groundSid = sid; }
                                }
                            }
                        }
                        else
                        {
                            // Anchor is left-of-center; clear any processed flag so it can be processed again if recrossed
                            if (processedColorTriggers.Contains(idx)) processedColorTriggers.Remove(idx);
                        }
                    }
                }

                // Apply detected color triggers: compute color and set tints accordingly. Mark anchors processed
                try
                {
                        if (bgIdx.HasValue && bgSid.HasValue)
                    {
                        var c = ColorFromTrigger(bgSid.Value);
                            backgroundTint = c; // update field used for drawing background
                            // Force solid-black background for 0x8F (explicit black background trigger)
                            backgroundForceSolidBlack = (bgSid.Value == 0x8F);
                        processedColorTriggers.Add(bgIdx.Value);
                        if (enableSimulatorDebugLogging && !triggerLogged.Contains(bgIdx.Value))
                        {
                            WriteTempLog($"Simulator: Applied background trigger at idx={bgIdx.Value} sid=0x{bgSid.Value:X} color={c}");
                            triggerLogged.Add(bgIdx.Value);
                        }
                    }
                    if (tileIdx.HasValue && tileSid.HasValue)
                    {
                        var c = ColorFromTrigger(tileSid.Value);
                        tileTint = c;
                        processedColorTriggers.Add(tileIdx.Value);
                        if (enableSimulatorDebugLogging && !triggerLogged.Contains(tileIdx.Value))
                        {
                            WriteTempLog($"Simulator: Applied tile trigger at idx={tileIdx.Value} sid=0x{tileSid.Value:X} color={c}");
                            triggerLogged.Add(tileIdx.Value);
                        }
                    }
                    if (groundIdx.HasValue && groundSid.HasValue)
                    {
                        // Compute ground tint from trigger. Special-case: when the trigger
                        // is the black-ground trigger (0xCF) we want the ground to become
                        // solid black (preserving the top white seam). We avoid forcing
                        // 0xCF globally in ColorFromTrigger so sprites/icons remain correct;
                        // here only the ground tint is made pure black to produce the
                        // expected visual (CreateBlackMaskedImages will be used below).
                        var c = ColorFromTrigger(groundSid.Value);
                        if (groundSid.Value == 0xCF)
                        {
                            // Special-case 0xCF: force the ground tint to pure black so the
                            // two-tone/ground mapping produces solid black bodies while
                            // allowing near-white seams to be recolored by object tints.
                            groundTint = Color.FromArgb(255, 0, 0, 0);
                        }
                        else
                        {
                            groundTint = c;
                        }
                        processedColorTriggers.Add(groundIdx.Value);
                        if (enableSimulatorDebugLogging && !triggerLogged.Contains(groundIdx.Value))
                        {
                            WriteTempLog($"Simulator: Applied ground trigger at idx={groundIdx.Value} sid=0x{groundSid.Value:X} color={c}");
                            triggerLogged.Add(groundIdx.Value);
                        }
                    }
                }
                catch { }

                // If any tint changed, regenerate toned tile images and invalidate tile-layer cache
                try
                {
                    // Compute an outline tint parameter for this regeneration. If a startup-origin
                    // tint was applied, keep outline tint transparent so outlines are not recolored.
                    var outlineTintParam = startupTintApplied ? Color.FromArgb(0, 0, 0, 0) : tileTint;

                    bool bgChanged = !AreColorsEqual(prevBackgroundTint, backgroundTint);
                    bool tileChanged = !AreColorsEqual(prevTileTint, tileTint);
                    bool grdChanged = !AreColorsEqual(prevGroundTint, groundTint);

                    if (bgChanged || tileChanged || grdChanged)
                    {
                        // Compute an effective outline tint for this regeneration so startup-applied
                        // tints do not recolor object outlines.
                        var outlineTintLocal = startupTintApplied ? Color.FromArgb(0, 0, 0, 0) : tileTint;
                        
                        // OPTIMIZATION: Only regenerate tile tint if tile actually changed
                        // This prevents expensive tile regeneration when only background or ground changed
                        if (tileChanged)
                        {
                            UpdateTonedImagesForTileTint(tileTint, outlineTintLocal);
                        }

                        // Only regenerate parallax when the background actually changed or when
                        // the forced-black flag toggled. This prevents ground-only triggers from
                        // accidentally replacing two-tone/parallax backgrounds.
                        try
                        {
                            if (bgChanged || prevBackgroundForceSolidBlack != backgroundForceSolidBlack)
                            {
                                if (backgroundForceSolidBlack)
                                {
                                    // When explicitly forced by 0x8F, do not use image-derived parallax; render solid black instead
                                    parallaxTonedImages = null;
                                }
                                else if (backgroundTint.A == 255 && backgroundTint.R == 0 && backgroundTint.G == 0 && backgroundTint.B == 0)
                                {
                                    parallaxTonedImages = CreateBlackMaskedImages(parallaxImages);
                                }
                                else
                                {
                                    parallaxTonedImages = CreateHueShiftedImages(parallaxImages, backgroundTint);
                                }
                            }
                        }
                        catch { parallaxTonedImages = parallaxImages; }

                        // Regenerate ground visuals when the ground tint changed OR when
                        // an object/tile tint changed (object triggers recolor the seam outline).
                        try
                        {
                            if (grdChanged || tileChanged)
                            {
                                if (groundTint.A == 255 && groundTint.R == 0 && groundTint.G == 0 && groundTint.B == 0)
                                {
                                    // For 0xCF (black-ground) produce black-masked images so
                                    // non-white/non-player-green pixels become black while
                                    // leaving the seam unaffected (outline recolor is disabled).
                                    try { groundTonedImages = CreateBlackMaskedExceptColorArray(groundImages, playerPlaceholderGreen, outlineTintLocal, false); }
                                    catch { groundTonedImages = CreateTwoToneTileImages(groundImages, Color.FromArgb(255, 0, 0, 0), Color.FromArgb(255, 0, 0, 0), outlineTintLocal); }
                                }
                                else
                                {
                                    groundTonedImages = CreateHueShiftedImages(groundImages, groundTint, outlineTintLocal);
                                }
                            }
                        }
                        catch { groundTonedImages = groundImages; }

                        // Forced-outline recolor fallback: if an outline tint is active for this regeneration,
                        // ensure thin/anti-aliased ground seams are recolored to match other tiles.
                        try
                        {
                            if (outlineTintParam.A > 0 && groundTonedImages != null)
                            {
                                var recol = CreateOutlineTintedTileImages(groundTonedImages, outlineTintParam);
                                if (recol != null) groundTonedImages = recol;
                            }
                        }
                        catch { }

                        // Also create/update a tinted full-parallax bitmap if a full parallax bitmap was provided
                        // Only update the full parallax bitmap when the background actually changed or when
                        // there is an explicit palette mapping for background, or when forced-black toggled.
                        try
                        {
                            if (parallaxBitmap != null && (bgChanged || prevBackgroundForceSolidBlack != backgroundForceSolidBlack))
                            {
                                ImageSource?[]? arr = null;
                                if (backgroundTint.A == 255 && backgroundTint.R == 0 && backgroundTint.G == 0 && backgroundTint.B == 0)
                                    arr = CreateBlackMaskedImages(new ImageSource?[] { parallaxBitmap! });
                                else
                                    arr = CreateHueShiftedImages(new ImageSource?[] { parallaxBitmap! }, backgroundTint);

                                if (arr != null && arr.Length > 0 && arr[0] != null) parallaxBitmapToned = arr[0];
                                else parallaxBitmapToned = parallaxBitmap;
                            }
                            else
                            {
                                // If we are not updating the parallax bitmap due to an unrelated ground change,
                                // leave the previous toned bitmap as-is so background visuals remain stable.
                            }
                        }
                        catch { parallaxBitmapToned = parallaxBitmap; }

                        // OPTIMIZATION: Only regenerate saw frames when background tint actually changed
                        // Saws are tinted by background color, not tile/ground color
                        if (bgChanged)
                        {
                            try
                            {
                                if (backgroundTint.A == 255 && backgroundTint.R == 0 && backgroundTint.G == 0 && backgroundTint.B == 0)
                                {
                                    this.sawFrame1TilesTinted = CreateSolidBlackImages(this.sawFrame1TilesOrig);
                                    this.sawFrame2TilesTinted = CreateSolidBlackImages(this.sawFrame2TilesOrig);
                                    this.smallSawFrame1TilesTinted = CreateSolidBlackImages(this.smallSawFrame1TilesOrig);
                                    this.smallSawFrame2TilesTinted = CreateSolidBlackImages(this.smallSawFrame2TilesOrig);
                                    this.largeSawFrame1TilesTinted = CreateSolidBlackImages(this.largeSawFrame1TilesOrig);
                                    this.largeSawFrame2TilesTinted = CreateSolidBlackImages(this.largeSawFrame2TilesOrig);
                                }
                                else
                                {
                                    this.sawFrame1TilesTinted = CreateHslShiftedImages(this.sawFrame1TilesOrig, backgroundTint, outlineTintLocal);
                                    this.sawFrame2TilesTinted = CreateHslShiftedImages(this.sawFrame2TilesOrig, backgroundTint, outlineTintLocal);
                                    this.smallSawFrame1TilesTinted = CreateHslShiftedImages(this.smallSawFrame1TilesOrig, backgroundTint, outlineTintLocal);
                                    this.smallSawFrame2TilesTinted = CreateHslShiftedImages(this.smallSawFrame2TilesOrig, backgroundTint, outlineTintLocal);
                                    this.largeSawFrame1TilesTinted = CreateHslShiftedImages(this.largeSawFrame1TilesOrig, backgroundTint, outlineTintLocal);
                                    this.largeSawFrame2TilesTinted = CreateHslShiftedImages(this.largeSawFrame2TilesOrig, backgroundTint, outlineTintLocal);
                                }
                            }
                            catch { }
                        }

                        tileLayerCache = null;
                        try { groundTintedTileCache.Clear(); } catch { }
                        try { spriteBackgroundCompositeCache.Clear(); } catch { }
                    }
                }
                catch { }
                if (newSpeed_fixed.HasValue)
                {
                    currentSpeed_fixed = newSpeed_fixed.Value;
                    playerVelX_fixed = newSpeed_fixed.Value; // Update Wave horizontal velocity for current speed
                    
                    // Update speed variable for debug display
                    if (newSpeed_fixed.Value == CUBE_SPEED_X05) speed = 0;
                    else if (newSpeed_fixed.Value == CUBE_SPEED_X1) speed = 1;
                    else if (newSpeed_fixed.Value == CUBE_SPEED_X2) speed = 2;
                    else if (newSpeed_fixed.Value == CUBE_SPEED_X3) speed = 3;
                    else if (newSpeed_fixed.Value == CUBE_SPEED_X4) speed = 4;
                    
                    // Update combo box selection to match
                    try
                    {
                        Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            if (SpeedComboBox != null && speed >= 0 && speed < SpeedComboBox.Items.Count)
                            {
                                SpeedComboBox.SelectedIndex = speed;
                            }
                        }));
                    }
                    catch { }
                }
            }
            catch { }

            RenderFrame();
        }

        private void RenderFrame()
        {
            // Prevent rendering if window is closed
            if (windowClosed) return;

            try { UpdateFirstPerson3DPrototype(); } catch { }
            
            // Update wave icon based on velocity (every frame)
            if (currentGameMode == 6)
            {
                try
                {
                    string waveChoice = miniMode ? "wave-mini.png" : "wave.png";
                    // Choose icon based on velocity magnitude
                    // wave2.png when nearly stationary, wave.png for movement
                    if (playerVelY_fixed == 0)
                    {
                        waveChoice = miniMode ? "wave-mini2.png" : "wave2.png";  // Straight/stationary
                    }
                    
                    // Check current image
                    string currentImageName = playerImage?.Tag as string ?? "";
                    
                    // Only reload if different
                    if (!currentImageName.Equals(waveChoice, StringComparison.OrdinalIgnoreCase))
                    {
                        var newImg = LoadCachedResourceImage(waveChoice);
                        if (newImg != null && playerImage != null)
                        {
                            playerImage.Source = App.EnsureUnfrozenForRender(newImg) ?? newImg;
                            playerImage.Tag = waveChoice;
                        }
                    }
                    
                    // Update flip every frame for wave
                    UpdatePlayerIconFlip();
                }
                catch { }
            }
            
            // Update ship icon based on rotation animation (every frame)
            if (currentGameMode == 1)
            {
                try
                {
                    // Ship animation: velocity-based 7 frames mapped to 7 PNG files
                    int shipFrame = GetShipSpriteFrame();  // Returns 0-7
                    int[] shipFrameMap = { 0, 0, 1, 2, 3, 4, 5, 6 };  // Map game frame 0-7 to PNG frame index 0-6
                    int pngFrame = shipFrameMap[shipFrame & 0x07];  // Clamp to 0-7
                    
                    // Use mini ship images if in mini mode
                    string shipChoice = miniMode ? (pngFrame switch {
                        0 => "ship-mini.png",
                        1 => "ship-mini1.png",
                        2 => "ship-mini2.png",
                        3 => "ship-mini3.png",
                        4 => "ship-mini4.png",
                        5 => "ship-mini5.png",
                        6 => "ship-mini6.png",
                        _ => "ship-mini.png"
                    }) : (pngFrame switch {
                        0 => "ship.png",
                        1 => "ship2.png",
                        2 => "ship3.png",
                        3 => "ship4.png",
                        4 => "ship5.png",
                        5 => "ship6.png",
                        6 => "ship7.png",
                        _ => "ship.png"
                    });
                    
                    // Check current image
                    string currentImageName = playerImage?.Tag as string ?? "";
                    
                    // Only reload if different
                    if (!currentImageName.Equals(shipChoice, StringComparison.OrdinalIgnoreCase))
                    {
                        var newImg = LoadCachedResourceImage(shipChoice);
                        if (newImg != null && playerImage != null)
                        {
                            playerImage.Source = App.EnsureUnfrozenForRender(newImg) ?? newImg;
                            playerImage.Tag = shipChoice;
                        }
                    }
                    
                    // Update flip every frame for ship based on gravity
                    UpdatePlayerIconFlip();
                }
                catch { }
            }

            // Update UFO icon flip (every frame for gravity changes)
            if (currentGameMode == 3)
            {
                try
                {
                    UpdatePlayerIconFlip();
                }
                catch { }
            }

            // Update swingcopter icon based on rotation animation (every frame)
            if (currentGameMode == 7)
            {
                try
                {
                    // Swingcopter animation: velocity-based 5 frames mapped to 8 game frames
                    int swingFrame = GetSwingcopterSpriteFrame();  // Returns 0-7
                    int[] swingFrameMap = { 0, 0, 1, 2, 2, 3, 4, 4 };  // Map game frame 0-7 to PNG frame index 0-4
                    int pngFrame = swingFrameMap[swingFrame & 0x07];  // Clamp to 0-7
                    
                    // Use mini swingcopter images if in mini mode
                    string swingChoice = miniMode ? (pngFrame switch {
                        0 => "swingcopter-mini.png",
                        1 => "swingcopter-mini1.png",
                        2 => "swingcopter-mini2.png",
                        3 => "swingcopter-mini3.png",
                        4 => "swingcopter-mini4.png",
                        _ => "swingcopter-mini.png"
                    }) : (pngFrame switch {
                        0 => "swingcopter.png",
                        1 => "swingcopter1.png",
                        2 => "swingcopter2.png",
                        3 => "swingcopter3.png",
                        4 => "swingcopter4.png",
                        _ => "swingcopter.png"
                    });
                    
                    // Check current image via Tag
                    string currentImageName = playerImage?.Tag as string ?? "";
                    
                    // Only reload if different
                    if (!currentImageName.Equals(swingChoice, StringComparison.OrdinalIgnoreCase))
                    {
                        var newImg = LoadCachedResourceImage(swingChoice);
                        if (newImg != null && playerImage != null)
                        {
                            playerImage.Source = App.EnsureUnfrozenForRender(newImg) ?? newImg;
                            playerImage.Tag = swingChoice;
                        }
                    }
                    
                    // Update flip every frame for swingcopter based on gravity
                    UpdatePlayerIconFlip();
                }
                catch { }
            }
            
            // Update snake icon flip (every frame for gravity changes)
            if (currentGameMode == 10)
            {
                try
                {
                    UpdatePlayerIconFlip();
                }
                catch { }
            }

            // Update football icon based on rotation animation (every frame)
            if (currentGameMode == 11)
            {
                try
                {
                    // Football uses cube-style rotation with flip table (24 frames total)
                    int footballFrameAndFlip = GetFootballSpriteFrameAndFlip();
                    int frameIndex = footballFrameAndFlip & 0x0F;  // Extract frame 0-6 from low byte
                    int flipFlags = footballFrameAndFlip & 0xC0;   // Extract flip bits
                    
                    // Construct image name based on frame
                    string footballChoice = miniMode ? $"football-mini{(frameIndex > 0 ? frameIndex.ToString() : "")}.png"
                                                     : $"football{(frameIndex > 0 ? frameIndex.ToString() : "")}.png";
                    
                    // Check current image via Tag
                    string currentImageName = playerImage?.Tag as string ?? "";
                    
                    // Only reload if different
                    if (!currentImageName.Equals(footballChoice, StringComparison.OrdinalIgnoreCase))
                    {
                        var newImg = LoadCachedResourceImage(footballChoice);
                        if (newImg != null && playerImage != null)
                        {
                            playerImage.Source = App.EnsureUnfrozenForRender(newImg) ?? newImg;
                            playerImage.Tag = footballChoice;
                        }
                    }
                    
                    // Apply flip flags (rotation + gravity)
                    if (playerImage != null)
                    {
                        bool hFlip = (flipFlags & 0x40) != 0;
                        bool vFlip = (flipFlags & 0x80) != 0;
                        // NES: gravity flip is handled by the flip table (cube uses drawcube_sprite_table
                        // which already encodes the rotation direction via H/V flags).
                        // But we also need to visually flip when gravity is reversed.
                        if (gravityReversed) vFlip = !vFlip;
                        playerImage.RenderTransformOrigin = new Point(0.5, 0.5);
                        playerImage.RenderTransform = new ScaleTransform(
                            hFlip ? -1 : 1,
                            vFlip ? -1 : 1
                        );
                    }
                }
                catch { }
            }

            // Update pogo icon flip (every frame for gravity changes)
            if (currentGameMode == 9)
            {
                try
                {
                    UpdatePlayerIconFlip();
                }
                catch { }
            }
            
            // Update cube icon based on rotation animation (every frame for modes 0, 4, 8)
            if (currentGameMode == 0 || currentGameMode == 4 || currentGameMode == 8)
            {
                try
                {
                    if (!miniMode)  // Only animate non-mini
                    {
                        // Get sprite table entry: bits 0-2 = tile index (0-6), bits 6-7 = flip flags
                        int spriteEntry = GetCubeSpriteFrame();
                        int tileIdx = spriteEntry & 0x07;
                        bool cubeHFlip = (spriteEntry & 0x40) != 0;
                        bool cubeVFlip = (spriteEntry & 0x80) != 0;
                        
                        // Choose frame names based on mode (static to avoid per-frame allocation)
                        string[] frameNames = (currentGameMode == 8) ? s_ninjaFrameNames : s_cubeFrameNames;
                        if (tileIdx >= frameNames.Length) tileIdx = 0;
                        string chosenFrame = frameNames[tileIdx];
                        
                        // Track current image name via a tag property
                        string currentImageName = playerImage?.Tag as string ?? "";
                        
                        // Only reload if different
                        if (!currentImageName.Equals(chosenFrame, StringComparison.OrdinalIgnoreCase) && playerImage != null)
                        {
                            // Try cached embedded resource first
                            var newImg = LoadCachedResourceImage(chosenFrame);
                            if (newImg != null)
                            {
                                playerImage.Source = App.EnsureUnfrozenForRender(newImg) ?? newImg;
                                playerImage.Tag = chosenFrame;
                            }
                            else
                            {
                                // Fallback to file system
                                string exeDir = AppDomain.CurrentDomain.BaseDirectory ?? ".";
                                string candidateOut = System.IO.Path.Combine(exeDir, chosenFrame);
                                if (System.IO.File.Exists(candidateOut))
                                {
                                    var fsImg = new BitmapImage();
                                    fsImg.BeginInit();
                                    fsImg.UriSource = new Uri(candidateOut);
                                    fsImg.CacheOption = BitmapCacheOption.OnLoad;
                                    fsImg.EndInit();
                                    fsImg.Freeze();
                                    playerImage.Source = App.EnsureUnfrozenForRender(fsImg) ?? fsImg;
                                    playerImage.Tag = chosenFrame;
                                }
                                else
                                {
                                    // Try relative path
                                    string candidate = System.IO.Path.GetFullPath(System.IO.Path.Combine(exeDir, "..\\..\\..\\..\\" + chosenFrame));
                                    if (System.IO.File.Exists(candidate))
                                    {
                                        var fsImg = new BitmapImage();
                                        fsImg.BeginInit();
                                        fsImg.UriSource = new Uri(candidate);
                                        fsImg.CacheOption = BitmapCacheOption.OnLoad;
                                        fsImg.EndInit();
                                        fsImg.Freeze();
                                        playerImage.Source = App.EnsureUnfrozenForRender(fsImg) ?? fsImg;
                                        playerImage.Tag = chosenFrame;
                                    }
                                }
                            }
                        }
                        
                        // Apply flip flags from sprite table (handles all 4 quadrants of rotation)
                        if (playerImage != null)
                        {
                            if (cubeHFlip || cubeVFlip)
                            {
                                playerImage.RenderTransformOrigin = new Point(0.5, 0.5);
                                playerImage.RenderTransform = new ScaleTransform(cubeHFlip ? -1 : 1, cubeVFlip ? -1 : 1);
                            }
                            else
                            {
                                playerImage.RenderTransform = Transform.Identity;
                            }
                        }
                    }
                    else  // Mini mode animation
                    {
                        // Extract actual frame index (high byte of cubeRotateMini_fixed)
                        int miniFrameIndex = GetCubeSpriteMiniFrame();  // Returns 0-3
                        
                        // Get flip flags from sprite table for mini rotation
                        int miniRawFrame = (cubeRotateMini_fixed >> 8) & 0xFF;
                        if (miniRawFrame < 0 || miniRawFrame >= 24) miniRawFrame = 0;
                        int miniSpriteEntry = drawcube_sprite_table[miniRawFrame];
                        bool miniHFlip = (miniSpriteEntry & 0x40) != 0;
                        bool miniVFlip = (miniSpriteEntry & 0x80) != 0;
                        
                        // Choose frame names based on mode
                        string[] miniFrameNames = (currentGameMode == 8) ? new string[]
                        {
                            "ninja_mini_00_frame_0.png",
                            "ninja_mini_01_frame_1.png",
                            "ninja_mini_02_frame_2.png",
                            "ninja_mini_03_frame_3.png",
                            "ninja_mini_04_frame_4.png"
                        }
                        : new string[]
                        {
                            "cube_mini_00_frame_0.png",
                            "cube_mini_01_frame_1.png",
                            "cube_mini_02_frame_2.png",
                            "cube_mini_03_frame_3.png",
                            "cube_mini_04_frame_4.png"
                        };
                        string chosenFrame = miniFrameNames[miniFrameIndex];
                        
                        // Track current image name via a tag property
                        string currentImageName = playerImage?.Tag as string ?? "";
                        
                        // Only reload if different
                        if (!currentImageName.Equals(chosenFrame, StringComparison.OrdinalIgnoreCase) && playerImage != null)
                        {
                            // Try cached embedded resource first
                            var newImg = LoadCachedResourceImage(chosenFrame);
                            if (newImg != null)
                            {
                                playerImage.Source = App.EnsureUnfrozenForRender(newImg) ?? newImg;
                                playerImage.Tag = chosenFrame;
                            }
                            else
                            {
                                // Fallback to file system
                                string exeDir = AppDomain.CurrentDomain.BaseDirectory ?? ".";
                                string candidateOut = System.IO.Path.Combine(exeDir, chosenFrame);
                                if (System.IO.File.Exists(candidateOut))
                                {
                                    var fsImg = new BitmapImage();
                                    fsImg.BeginInit();
                                    fsImg.UriSource = new Uri(candidateOut);
                                    fsImg.CacheOption = BitmapCacheOption.OnLoad;
                                    fsImg.EndInit();
                                    fsImg.Freeze();
                                    playerImage.Source = App.EnsureUnfrozenForRender(fsImg) ?? fsImg;
                                    playerImage.Tag = chosenFrame;
                                }
                                else
                                {
                                    // Try relative path
                                    string candidate = System.IO.Path.GetFullPath(System.IO.Path.Combine(exeDir, "..\\..\\..\\..\\" + chosenFrame));
                                    if (System.IO.File.Exists(candidate))
                                    {
                                        var fsImg = new BitmapImage();
                                        fsImg.BeginInit();
                                        fsImg.UriSource = new Uri(candidate);
                                        fsImg.CacheOption = BitmapCacheOption.OnLoad;
                                        fsImg.EndInit();
                                        fsImg.Freeze();
                                        playerImage.Source = App.EnsureUnfrozenForRender(fsImg) ?? fsImg;
                                        playerImage.Tag = chosenFrame;
                                    }
                                }
                            }
                        }
                        
                        // Apply flip flags from sprite table for mini cube
                        if (playerImage != null)
                        {
                            if (miniHFlip || miniVFlip)
                            {
                                playerImage.RenderTransformOrigin = new Point(0.5, 0.5);
                                playerImage.RenderTransform = new ScaleTransform(miniHFlip ? -1 : 1, miniVFlip ? -1 : 1);
                            }
                            else
                            {
                                playerImage.RenderTransform = Transform.Identity;
                            }
                        }
                    }
                }
                catch { }
            }
            
            // Update pogo icon based on bounce animation counter (every frame)
            if (currentGameMode == 9)
            {
                try
                {
                    // Determine which images to use based on mini mode
                    string bounceImg = miniMode ? "pogo-mini2.png" : "pogo2.png";
                    string normalImg = miniMode ? "pogo-mini.png" : "pogo.png";
                    string pogoChoice = (pogoBounceAnimationCounter > 0) ? bounceImg : normalImg;
                    AppendSimDebug($"[POGO_ICON] bounceCounter={pogoBounceAnimationCounter}, choice={pogoChoice}");
                    
                    // Decrement counter in RenderFrame, accounting for simulation timescale
                    // Only decrement if not paused
                    if (!paused && pogoBounceAnimationCounter > 0)
                    {
                        // Use a sub-frame accumulator to handle fractional decrements based on timescale
                        pogoBounceAnimationFrameAccum += simTimeScale;
                        if (pogoBounceAnimationFrameAccum >= 1.0)
                        {
                            pogoBounceAnimationCounter--;
                            pogoBounceAnimationFrameAccum -= 1.0;
                        }
                    }
                    else if (paused)
                    {
                        // Do nothing, wait until unpaused
                    }
                    else
                    {
                        pogoBounceAnimationFrameAccum = 0.0;  // Reset accumulator when animation ends
                    }
                    
                    // Track current image name via a tag property
                    string currentImageName = playerImage?.Tag as string ?? "";
                    
                    // Only reload if different
                    if (!currentImageName.Equals(pogoChoice, StringComparison.OrdinalIgnoreCase) && playerImage != null)
                    {
                        // Try cached embedded resource first
                        var newImg = LoadCachedResourceImage(pogoChoice);
                        if (newImg != null)
                        {
                            playerImage.Source = App.EnsureUnfrozenForRender(newImg) ?? newImg;
                            playerImage.Tag = pogoChoice;
                        }
                        else
                        {
                            // Fallback to file system
                            string exeDir = AppDomain.CurrentDomain.BaseDirectory ?? ".";
                            string candidateOut = System.IO.Path.Combine(exeDir, pogoChoice);
                            if (System.IO.File.Exists(candidateOut))
                            {
                                var fsImg = new BitmapImage();
                                fsImg.BeginInit();
                                fsImg.UriSource = new Uri(candidateOut);
                                fsImg.CacheOption = BitmapCacheOption.OnLoad;
                                fsImg.EndInit();
                                fsImg.Freeze();
                                playerImage.Source = App.EnsureUnfrozenForRender(fsImg) ?? fsImg;
                                playerImage.Tag = pogoChoice;
                            }
                            else
                            {
                                // Try relative path
                                string candidate = System.IO.Path.GetFullPath(System.IO.Path.Combine(exeDir, "..\\..\\..\\..\\" + pogoChoice));
                                if (System.IO.File.Exists(candidate))
                                {
                                    var fsImg = new BitmapImage();
                                    fsImg.BeginInit();
                                    fsImg.UriSource = new Uri(candidate);
                                    fsImg.CacheOption = BitmapCacheOption.OnLoad;
                                    fsImg.EndInit();
                                    fsImg.Freeze();
                                    playerImage.Source = App.EnsureUnfrozenForRender(fsImg) ?? fsImg;
                                    playerImage.Tag = pogoChoice;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex) { AppendSimDebug($"[POGO_ICON] Exception: {ex.Message}"); }
            }
            
            // Update ball icon animation counter (alternates every 3 frames, accounting for timescale)
            // Only animate for normal ball mode, not mini ball
            if (currentGameMode == 2 && !miniMode)
            {
                try
                {
                    // Only animate if not paused
                    if (!paused)
                    {
                        // Increment accumulator and cycle counter (0-11 cycle: 6 frames ball.png, 6 frames ball2.png)
                        ballAnimationFrameAccum += simTimeScale;
                        if (ballAnimationFrameAccum >= 1.0)
                        {
                            ballAnimationFrameCounter++;
                            if (ballAnimationFrameCounter >= 12)  // 12-frame cycle (0-11)
                            {
                                ballAnimationFrameCounter = 0;
                            }
                            ballAnimationFrameAccum -= 1.0;
                        }
                    }
                    
                    string ballChoice = (ballAnimationFrameCounter < 6) ? "ball.png" : "ball2.png";
                    AppendSimDebug($"[BALL_ANIM] counter={ballAnimationFrameCounter}, accum={ballAnimationFrameAccum:F2}, simTimeScale={simTimeScale}, choice={ballChoice}");
                    
                    // Track current image name via a tag property
                    string currentImageName = playerImage?.Tag as string ?? "";
                    
                    // Only reload if different
                    if (!currentImageName.Equals(ballChoice, StringComparison.OrdinalIgnoreCase))
                    {
                        // Use "." prefix to avoid partial match (e.g. football.png matching ball.png)
                        var newImg = LoadCachedResourceImage("." + ballChoice);
                        if (newImg != null && playerImage != null)
                        {
                            playerImage.Source = App.EnsureUnfrozenForRender(newImg) ?? newImg;
                            playerImage.Tag = ballChoice;
                        }
                        else
                        {
                            // Fallback to file system
                            string exeDir = AppDomain.CurrentDomain.BaseDirectory ?? ".";
                            string candidateOut = System.IO.Path.Combine(exeDir, ballChoice);
                            if (System.IO.File.Exists(candidateOut))
                            {
                                var fsImg = new BitmapImage();
                                fsImg.BeginInit();
                                fsImg.UriSource = new Uri(candidateOut);
                                fsImg.CacheOption = BitmapCacheOption.OnLoad;
                                fsImg.EndInit();
                                fsImg.Freeze();
                                if (playerImage != null) playerImage.Source = App.EnsureUnfrozenForRender(fsImg) ?? fsImg;
                                if (playerImage != null) playerImage.Tag = ballChoice;
                            }
                            else
                            {
                                // Try relative path
                                string candidate = System.IO.Path.GetFullPath(System.IO.Path.Combine(exeDir, "..\\..\\..\\..\\" + ballChoice));
                                if (System.IO.File.Exists(candidate))
                                {
                                    var fsImg = new BitmapImage();
                                    fsImg.BeginInit();
                                    fsImg.UriSource = new Uri(candidate);
                                    fsImg.CacheOption = BitmapCacheOption.OnLoad;
                                    fsImg.EndInit();
                                    fsImg.Freeze();
                                    if (playerImage != null) playerImage.Source = App.EnsureUnfrozenForRender(fsImg) ?? fsImg;
                                    if (playerImage != null) playerImage.Tag = ballChoice;
                                }
                            }
                        }
                    }
                }
                catch { }
            }
            else
            {
                // Reset counter when not in ball mode
                ballAnimationFrameCounter = 0;
                ballAnimationFrameAccum = 0.0;
            }
            
            // Update robot icon animation (4 frames x 5 frames each when grounded)
            if (currentGameMode == 4)
            {
                try
                {
                    // Use velocity to determine animation state (not onGround flag)
                    // Tolerance: 0x0100 (same as wave animation uses for near-zero)
                    bool robotHasVerticalVelocity = Math.Abs(playerVelY_fixed) > 0x0100;
                    bool robotIsStationary = !robotHasVerticalVelocity;
                    
                    if (robotIsStationary && !paused)
                    {
                        // Increment accumulator and cycle counter (0-79 cycle: 20 frames per frame state, 4 frames total — half speed)
                        robotAnimationFrameAccum += simTimeScale;
                        if (robotAnimationFrameAccum >= 1.0)
                        {
                            robotAnimationFrameCounter++;
                            if (robotAnimationFrameCounter >= 80)  // 80-frame cycle (0-79)
                            {
                                robotAnimationFrameCounter = 0;
                            }
                            robotAnimationFrameAccum -= 1.0;
                        }
                    }
                    else if (robotHasVerticalVelocity)
                    {
                        // Reset counter when velocity becomes non-zero
                        robotAnimationFrameCounter = 0;
                        robotAnimationFrameAccum = 0.0;
                    }
                    
                    // Determine which image to show based on animation counter or velocity
                    string robotChoice;
                    
                    if (robotIsStationary)
                    {
                        // Frame mapping: 0-19=robot.png, 20-39=robot2.png, 40-59=robot3.png, 60-79=robot4.png
                        int frameIndex = robotAnimationFrameCounter / 20;
                        robotChoice = frameIndex switch
                        {
                            0 => miniMode ? "robot-mini.png" : "robot.png",
                            1 => miniMode ? "robot-mini2.png" : "robot2.png",
                            2 => miniMode ? "robot-mini3.png" : "robot3.png",
                            3 => miniMode ? "robot-mini4.png" : "robot4.png",
                            _ => miniMode ? "robot-mini.png" : "robot.png"
                        };
                    }
                    else
                    {
                        robotChoice = miniMode ? "robot-mini-jump.png" : "robotjump.png";
                    }
                    
                    // Track current image name via a tag property
                    string currentImageName = playerImage?.Tag as string ?? "";
                    
                    // Only reload if different
                    if (!currentImageName.Equals(robotChoice, StringComparison.OrdinalIgnoreCase))
                    {
                        // Try cached embedded resource first (use "." prefix to avoid partial matches)
                        BitmapImage? newImg = LoadCachedResourceImage("." + robotChoice);
                        if (newImg == null)
                        {
                            // Fallback to file system
                            string exeDir = AppDomain.CurrentDomain.BaseDirectory ?? ".";
                            string candidateOut = System.IO.Path.Combine(exeDir, robotChoice);
                            if (System.IO.File.Exists(candidateOut))
                            {
                                newImg = new BitmapImage();
                                newImg.BeginInit();
                                newImg.UriSource = new Uri(candidateOut);
                                newImg.CacheOption = BitmapCacheOption.OnLoad;
                                newImg.EndInit();
                                newImg.Freeze();
                            }
                            else
                            {
                                // Try relative path
                                string candidate = System.IO.Path.GetFullPath(System.IO.Path.Combine(exeDir, "..\\..\\..\\..\\" + robotChoice));
                                if (System.IO.File.Exists(candidate))
                                {
                                    newImg = new BitmapImage();
                                    newImg.BeginInit();
                                    newImg.UriSource = new Uri(candidate);
                                    newImg.CacheOption = BitmapCacheOption.OnLoad;
                                    newImg.EndInit();
                                    newImg.Freeze();
                                }
                            }
                        }
                        
                        if (newImg != null && playerImage != null)
                        {
                            playerImage.Source = App.EnsureUnfrozenForRender(newImg) ?? newImg;
                            playerImage.Tag = robotChoice;  // Track which image is loaded
                            playerImage.Width = newImg.PixelWidth;
                            playerImage.Height = newImg.PixelHeight;
                            
                            // Handle special sizing for robot2 and robot4 (24x16 - wider, right-aligned)
                            // For these frames, the extra width extends to the left
                            if ((robotChoice == "robot2.png" || robotChoice == "robot4.png") && playerImage != null)
                            {
                                // 24 pixels wide image, but hitbox is still 16x16
                                // Shift left by 8 pixels so right edge aligns, extra comes out left
                                // Do NOT change playerVisualWidth/Height - those are used for collision detection
                                var translateTransform = new System.Windows.Media.TransformGroup();
                                var translate = new System.Windows.Media.TranslateTransform(-8, 0);
                                var scaleTransform = gravityReversed ? new System.Windows.Media.ScaleTransform(1, -1) : new System.Windows.Media.ScaleTransform(1, 1);
                                translateTransform.Children.Add(translate);
                                translateTransform.Children.Add(scaleTransform);
                                playerImage.RenderTransformOrigin = new Point(0.5, 0.5);
                                playerImage.RenderTransform = translateTransform;
                            }
                            else if (playerImage != null)
                            {
                                // Normal 16x16
                                // Apply gravity flip only for normal images
                                if (gravityReversed)
                                {
                                    playerImage.RenderTransformOrigin = new Point(0.5, 0.5);
                                    playerImage.RenderTransform = new System.Windows.Media.ScaleTransform(1, -1);
                                }
                                else
                                {
                                    playerImage.RenderTransform = System.Windows.Media.Transform.Identity;
                                }
                            }
                        }
                    }
                }
                catch { }
            }
            else
            {
                // Reset counter when not in robot mode
                robotAnimationFrameCounter = 0;
                robotAnimationFrameAccum = 0.0;
            }
            
            // Update spider icon animation (4 frames x 5 frames each when grounded, same as robot)
            if (currentGameMode == 5)
            {
                try
                {
                    // Determine if spider is in "grounded" state (using onGround or ground stabilize)
                    bool spiderIsGrounded = onGround || groundStabilizeCounter > 0;
                    
                    if (spiderIsGrounded && !paused)
                    {
                        // Increment accumulator and cycle counter (0-79 cycle: 20 frames per frame state, 4 frames total — half speed)
                        spiderAnimationFrameAccum += simTimeScale;
                        if (spiderAnimationFrameAccum >= 1.0)
                        {
                            spiderAnimationFrameCounter++;
                            if (spiderAnimationFrameCounter >= 80)  // 80-frame cycle (0-79)
                            {
                                spiderAnimationFrameCounter = 0;
                            }
                            spiderAnimationFrameAccum -= 1.0;
                        }
                    }
                    else if (!spiderIsGrounded)
                    {
                        // Reset counter when leaving ground
                        spiderAnimationFrameCounter = 0;
                        spiderAnimationFrameAccum = 0.0;
                    }
                    
                    // Determine which image to show based on animation counter or jump state
                    string spiderChoice;
                    // Use spiderjump.png if velocity is outside a small tolerance (not standing still)
                    // Tolerance: 0x0100 (same as wave animation uses for near-zero)
                    bool spiderHasVerticalVelocity = Math.Abs(playerVelY_fixed) > 0x0100;
                    
                    if (spiderIsGrounded && !spiderHasVerticalVelocity)
                    {
                        // Frame mapping: 0-19=spider.png, 20-39=spider2.png, 40-59=spider3.png, 60-79=spider4.png
                        int frameIndex = spiderAnimationFrameCounter / 20;
                        spiderChoice = frameIndex switch
                        {
                            0 => miniMode ? "spider-mini.png" : "spider.png",
                            1 => miniMode ? "spider-mini2.png" : "spider2.png",
                            2 => miniMode ? "spider-mini3.png" : "spider3.png",
                            3 => miniMode ? "spider-mini4.png" : "spider4.png",
                            _ => miniMode ? "spider-mini.png" : "spider.png"
                        };
                    }
                    else
                    {
                        spiderChoice = miniMode ? "spider-mini-jump.png" : "spiderjump.png";
                    }
                    
                    // Load the spider image from embedded resources (cached)
                    try
                    {
                        if (playerImage != null)
                        {
                            string currentTag = playerImage.Tag as string ?? "";
                            if (!currentTag.Equals(spiderChoice, StringComparison.OrdinalIgnoreCase))
                            {
                                var cachedImg = LoadCachedResourceImage("." + spiderChoice);
                                if (cachedImg != null)
                                {
                                    playerImage.Source = App.EnsureUnfrozenForRender(cachedImg) ?? cachedImg;
                                    playerImage.Tag = spiderChoice;
                                    playerImage.Width = cachedImg.PixelWidth;
                                    playerImage.Height = cachedImg.PixelHeight;
                                }
                            }
                        }
                    }
                    catch { }
                    
                    // Apply visual offset for spider/spider2/spider3 (24x16 images, right-aligned)
                    // spider4 is 16x16 so it needs no offset
                    if (playerImage != null && (spiderChoice == "spider.png" || spiderChoice == "spider2.png" || spiderChoice == "spider3.png"))
                    {
                        // These are 24x16 images, offset -8px to right-align them to 16x16 hitbox
                        playerImage.RenderTransformOrigin = new Point(0, 0.5);
                        var transform = new System.Windows.Media.TransformGroup();
                        transform.Children.Add(new System.Windows.Media.TranslateTransform(-8, 0));
                        if (gravityFlipped)
                        {
                            transform.Children.Add(new System.Windows.Media.ScaleTransform(1, -1));
                        }
                        playerImage.RenderTransform = transform;
                    }
                    else if (playerImage != null && gravityFlipped)
                    {
                        // Standard images with gravity flip only
                        playerImage.RenderTransformOrigin = new Point(0.5, 0.5);
                        playerImage.RenderTransform = new System.Windows.Media.ScaleTransform(1, -1);
                    }
                    else if (playerImage != null)
                    {
                        playerImage.RenderTransform = System.Windows.Media.Transform.Identity;
                    }
                }
                catch { }
            }
            else
            {
                // Reset counter when not in spider mode
                spiderAnimationFrameCounter = 0;
                spiderAnimationFrameAccum = 0.0;
            }
            
            // Advance per-frame counter used for caching overlay-computed hitboxes
            try { renderFrameCounter++; } catch { renderFrameCounter = 1; }
            // One-time first-frame diagnostic snapshot (Option A)
            try
            {
                if (!simDebugLoggedFirstFrame)
                {
                    simDebugLoggedFirstFrame = true;
                    // Determine which ImageSource would be used as the brush source
                    string chosenSrc = "none";
                    int srcW = 0, srcH = 0;
                    try
                    {
                        ImageSource? src = null;
                        if (parallaxBitmapToned != null) src = parallaxBitmapToned;
                        else if (parallaxBitmap != null) src = parallaxBitmap;
                        else if (parallaxTonedImages != null && parallaxTonedImages.Length == (parallaxImages==null?0:parallaxImages.Length) && parallaxTonedImages.Length>0) src = parallaxTonedImages[0];
                        else if (parallaxImages != null && parallaxImages.Length > 0) src = parallaxImages[0];
                        if (src is BitmapSource bs)
                        {
                            chosenSrc = bs.GetType().Name;
                            srcW = bs.PixelWidth;
                            srcH = bs.PixelHeight;
                        }
                        else if (src != null) chosenSrc = src.GetType().Name;
                    }
                    catch { }

                    // Compute parallax offsets as used when creating the brush
                    double logParallaxOffsetX = -(cameraX_fixed >> 8) * (1.0 - parallaxX);
                    double logParallaxOffsetY = -(cameraY_fixed >> 8) * (1.0 - parallaxY);

                    string tileLayerSrc = (tileLayerImage?.Source == null) ? "null" : tileLayerImage.Source.GetType().Name;

                    AppendSimDebug($"RenderFrame: hasParallaxLayer={hasParallaxLayer}, chosenSrc={chosenSrc}, srcW={srcW}, srcH={srcH}, parallaxImagesLen={(parallaxImages==null?0:parallaxImages.Length)}, parallaxTonedLen={(parallaxTonedImages==null?0:parallaxTonedImages.Length)}, bgRectFill={(bgRectPersistent?.Fill==null?"null":bgRectPersistent.Fill.GetType().Name)}, backgroundTint={backgroundTint}, parallaxOffsetX={logParallaxOffsetX}, parallaxOffsetY={logParallaxOffsetY}, tileLayerImageSource={tileLayerSrc}");

                    try
                    {
                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine("RenderCanvas children:");
                        for (int i = 0; i < RenderCanvas.Children.Count; i++)
                        {
                            var child = RenderCanvas.Children[i];
                            int z = 0;
                            try { z = System.Windows.Controls.Canvas.GetZIndex(child); } catch { }
                            string tname = child?.GetType().Name ?? "null";
                            double w = 0, h = 0;
                            try { w = (child as System.Windows.FrameworkElement)?.Width ?? Double.NaN; h = (child as System.Windows.FrameworkElement)?.Height ?? Double.NaN; } catch { }
                            sb.AppendLine($"  [{i}] Type={tname} Z={z} Width={w} Height={h} Visible={(child is System.Windows.FrameworkElement fe? (fe.Visibility==Visibility.Visible):true)}");
                        }
                        AppendSimDebug(sb.ToString());
                    }
                    catch { }
                }
            }
            catch { }

            // Snapshot camera state under lock so tiles and player use the
            // same camera position.  Without this, a physics step could fire
            // between the tile-layer render (which reads cameraX_fixed) and
            // the player-position render, causing the player to appear offset
            // from tiles by a few pixels — making it look like it penetrates
            // spikes/blocks.
            int snapCameraX, snapCameraY, snapPlayerX, snapPlayerY, snapPlayerVelY;
            bool snapMiniMode, snapGravFlipped;
            lock (simLock)
            {
                snapCameraX = cameraX_fixed;
                snapCameraY = cameraY_fixed;
                snapPlayerX = playerX_fixed;
                snapPlayerY = playerY_fixed;
                snapPlayerVelY = playerVelY_fixed;
                snapMiniMode = miniMode;
                snapGravFlipped = gravityFlipped;
            }

            int renderCameraX_fixed = snapCameraX + twoDPanX_fixed;
            int renderCameraY_fixed = snapCameraY + twoDPanY_fixed;

            int renderMaxCameraX_fixed = Math.Max(0, (mapWidth - NES_W) * TILE) << 8;
            int renderMaxCameraY_fixed = Math.Max(0, (mapHeight - NES_H) * TILE) << 8;
            if (renderCameraX_fixed < 0) renderCameraX_fixed = 0;
            if (renderCameraX_fixed > renderMaxCameraX_fixed) renderCameraX_fixed = renderMaxCameraX_fixed;
            if (renderCameraY_fixed < 0) renderCameraY_fixed = 0;
            if (renderCameraY_fixed > renderMaxCameraY_fixed) renderCameraY_fixed = renderMaxCameraY_fixed;

            // When zoomed in, compute a render-only camera that keeps the player anchored
            // at its 1x screen position so zooming always centers around player motion.
            if (sim2DZoomScale > 1.0 + 0.001)
            {
                int visibleW_pre = (int)Math.Round((NES_W * TILE) / sim2DZoomScale);
                int visibleH_pre = (int)Math.Round((NES_H * TILE) / sim2DZoomScale);
                int miniVisualShiftY = snapMiniMode ? 4 : 0;
                int desiredTopX = (visibleW_pre - playerVisualWidth) / 2;
                int desiredTopY = (visibleH_pre - playerVisualHeight) / 2;

                // Keep the player's visual centered in the zoomed viewport.
                int centeredCameraX_fixed = snapPlayerX - (desiredTopX << 8);
                int centeredCameraY_fixed = snapPlayerY - ((desiredTopY - gridRenderShiftYPx - miniVisualShiftY) << 8);
                renderCameraX_fixed = centeredCameraX_fixed + twoDPanX_fixed;
                renderCameraY_fixed = centeredCameraY_fixed + twoDPanY_fixed;

                int maxCameraX_fixed = Math.Max(0, (mapWidth - NES_W) * TILE) << 8;
                int maxCameraY_fixed = Math.Max(0, (mapHeight - NES_H) * TILE) << 8;
                if (renderCameraX_fixed < 0) renderCameraX_fixed = 0;
                if (renderCameraX_fixed > maxCameraX_fixed) renderCameraX_fixed = maxCameraX_fixed;
                if (renderCameraY_fixed < 0) renderCameraY_fixed = 0;
                if (renderCameraY_fixed > maxCameraY_fixed) renderCameraY_fixed = maxCameraY_fixed;
            }

            // Compute pixel offset and starting tile index from the render camera.
            int pixelX = renderCameraX_fixed >> 8; // full pixels
            int subPixelX = renderCameraX_fixed & 0xFF; // fractional
            int startTileX = pixelX / TILE;
            int offsetX = pixelX % TILE;
            double subPixelOffsetX = subPixelX / 256.0; // Convert to fractional pixels

            // When zoomed out, bias the render camera upward so more sky is visible and less
            // ground fills the view. This render-only offset must be used consistently for tiles,
            // sprites, and player drawing so everything stays aligned.
            if (sim2DZoomScale < 1.0 - 0.001)
            {
                int cacheTilesYForZoom = (int)Math.Ceiling(NES_H / sim2DZoomScale) + 1;
                int extraTilesY = Math.Max(0, cacheTilesYForZoom - (NES_H + 1));
                int pullUpTiles = Math.Max(0, extraTilesY - 1);
                renderCameraY_fixed -= pullUpTiles * TILE * 256;
            }

            int pixelY = renderCameraY_fixed >> 8;
            int subPixelY = renderCameraY_fixed & 0xFF; // fractional Y
            double subPixelOffsetY = subPixelY / 256.0; // Convert to fractional pixels

            // If ground is present in the preview, reserve up to three ground rows at the bottom
            int groundRowsToReserve = 0;
            if (hasGroundLayer && groundTileRows > 0)
            {
                // Reserve exactly 3 rows when a ground layer exists (or fewer if ground bitmap has <3 rows)
                groundRowsToReserve = Math.Min(3, groundTileRows);
            }
            int groundPixels = groundRowsToReserve * TILE;

            // Keep camera-aligned start/offset based on render camera Y so sub-pixel translation
            // remains consistent with the editor. We'll subtract ground rows when sampling map tiles.
            int startTileY = pixelY / TILE;
            int offsetY = pixelY % TILE;

            // Update persistent background: draw parallax tiled image when available
            try
            {
                if (bgRectPersistent != null && hasParallaxLayer && backgroundForceSolidBlack)
                {
                    // Force full solid-black background when 0x8F activated (don't use parallax images)
                    try { bgRectPersistent.Fill = new SolidColorBrush(Color.FromArgb(255, 0, 0, 0)); }
                    catch { bgRectPersistent.Fill = Brushes.Black; }
                }
                else if (bgRectPersistent != null && hasParallaxLayer && ((parallaxImages != null && parallaxImages.Length > 0) || parallaxBitmap != null || parallaxBitmapToned != null))
                {
                    // Prefer using the full parallax bitmap (if provided) so the entire image repeats.
                    ImageSource? src = null;
                    if (parallaxBitmapToned != null) src = parallaxBitmapToned;
                    else if (parallaxBitmap != null) src = parallaxBitmap;
                    else if (parallaxTonedImages != null && parallaxTonedImages.Length == parallaxImages.Length && parallaxTonedImages[0] != null) src = parallaxTonedImages[0];
                    else if (parallaxImages != null && parallaxImages.Length > 0) src = parallaxImages[0];

                    if (src is BitmapSource pbs)
                    {
                        // Parallax translation: background moves slower than camera based on parallaxX/Y.
                        double parallaxOffsetX = -(pixelX) * (1.0 - parallaxX);
                        double parallaxOffsetY = -(renderCameraY_fixed >> 8) * (1.0 - parallaxY);

                        // Reuse cached parallax brush — only recreate when the source image changes
                        if (_cachedParallaxBrush == null || _cachedParallaxSource != src)
                        {
                            double imgW = Math.Max(1.0, pbs.PixelWidth);
                            double imgH = Math.Max(1.0, pbs.PixelHeight);
                            var brushImg = App.EnsureUnfrozenForRender(src) ?? src;
                            _cachedParallaxTransform = new TranslateTransform(parallaxOffsetX, parallaxOffsetY);
                            _cachedParallaxBrush = new ImageBrush(brushImg)
                            {
                                TileMode = TileMode.Tile,
                                ViewportUnits = BrushMappingMode.Absolute,
                                Viewport = new Rect(0, 0, imgW, imgH),
                                Stretch = Stretch.None,
                                Transform = _cachedParallaxTransform
                            };
                            _cachedParallaxSource = src;
                            bgRectPersistent.Fill = _cachedParallaxBrush;
                        }
                        else
                        {
                            // Just update transform offsets — no allocation
                            _cachedParallaxTransform!.X = parallaxOffsetX;
                            _cachedParallaxTransform.Y = parallaxOffsetY;
                        }
                    }
                    else
                    {
                        // If no src was selectable, attempt an on-the-spot load of the embedded project parallax
                        try
                        {
                            var bi = LoadCachedResourceImage("Assets.parallax.bmp")
                                  ?? LoadCachedResourceImage("parallax Blue.bmp")
                                  ?? LoadCachedResourceImage("parallax.bmp");
                            if (bi != null)
                            {
                                // set as runtime parallax bitmap and create a brush
                                this.parallaxBitmap = bi;
                                this.parallaxImages = new ImageSource[] { bi };
                                this.parallaxTonedImages = null;
                                this.hasParallaxLayer = true;

                                var brush2Img = App.EnsureUnfrozenForRender(bi) ?? bi;
                                var brush2 = new ImageBrush(brush2Img)
                                {
                                    TileMode = TileMode.Tile,
                                    ViewportUnits = BrushMappingMode.Absolute,
                                    Viewport = new Rect(0, 0, Math.Max(1.0, bi.PixelWidth), Math.Max(1.0, bi.PixelHeight)),
                                    Stretch = Stretch.None
                                };
                                double parallaxOffsetX2 = -(pixelX) * (1.0 - parallaxX);
                                double parallaxOffsetY2 = -(renderCameraY_fixed >> 8) * (1.0 - parallaxY);
                                brush2.Transform = new TranslateTransform(parallaxOffsetX2, parallaxOffsetY2);
                                if (bgRectPersistent != null) bgRectPersistent.Fill = brush2;
                            }
                        }
                        catch { }
                    }
                }
                else
                {
                    if (bgRectPersistent != null)
                    {
                        if (_cachedBgTintBrush == null || _cachedBgTintColor != backgroundTint)
                        {
                            _cachedBgTintColor = backgroundTint;
                            _cachedBgTintBrush = new SolidColorBrush(backgroundTint);
                            bgRectPersistent.Fill = _cachedBgTintBrush;
                        }
                    }
                }
            }
            catch { }

            // Rebuild tile-layer cache when integer tile origin changes
            try
            {
                if (tileLayerImage != null && (tileLayerCache == null || cachedStartTileX != startTileX || cachedStartTileY != startTileY || (lastCacheHadAnimatedTiles && lastCacheAnimationFrame != animationFrame)))
                {
                    // When zoomed out, expand the tile coverage so more world is visible.
                    int cacheTilesX = (sim2DZoomScale < 1.0 - 0.001)
                        ? (int)Math.Ceiling(NES_W / sim2DZoomScale) + 1
                        : NES_W + 1;
                    int cacheTilesY = (sim2DZoomScale < 1.0 - 0.001)
                        ? (int)Math.Ceiling(NES_H / sim2DZoomScale) + 1
                        : NES_H + 1;
                    int pxW = cacheTilesX * TILE;
                    int pxH = cacheTilesY * TILE;

                    var dv = new DrawingVisual();
                    bool hadAnimated = false;
                    using (var dc = dv.RenderOpen())
                    {
                        // Pre-create a semi-transparent red brush for tile hitbox overlay
                        Brush? tileHitBrush = null;
                        try
                        {
                            if (ShowTileHitboxes)
                            {
                                tileHitBrush = new SolidColorBrush(Color.FromArgb(160, 0xFF, 0x00, 0x00));
                                try { tileHitBrush.Freeze(); } catch { }
                            }
                        }
                        catch { tileHitBrush = null; }
                        int cacheTilesY_local = cacheTilesY;
                        int groundRowsToReserve_local = groundRowsToReserve;
                        int groundStartRow_local = cacheTilesY_local - groundRowsToReserve_local;

                        // Determine ground layout columns if ground images are available
                        int groundCols = 1;
                        if (groundImages != null && groundTileRows > 0)
                        {
                            groundCols = Math.Max(1, groundImages.Length / groundTileRows);
                        }

                        for (int vx = 0; vx < cacheTilesX; vx++)
                        {
                            int mapX = startTileX + vx;
                            for (int vy = 0; vy < cacheTilesY; vy++)
                            {
                                Rect dest = new Rect(vx * TILE, vy * TILE, TILE, TILE);

                                // Compute the map Y corresponding to this dest row, where we treat the
                                // visible rows as starting from startTileY - groundRowsToReserve
                                int mapY = startTileY + groundRowsToReserve_local + vy;

                                // If mapY is beyond the bottom of the map, and we have a ground layer,
                                // draw the appropriate ground slice row instead of map tiles.
                                if (mapY >= mapHeight)
                                {
                                    if (hasGroundLayer && groundImages != null && groundImages.Length > 0)
                                    {
                                        int pyGround = mapY - mapHeight; // 0..groundRowsToReserve-1
                                        int pxGround = ((mapX % groundCols) + groundCols) % groundCols; // wrap
                                        int rowIndex = Math.Max(0, Math.Min(groundTileRows - 1, pyGround));
                                        int arrIdx = rowIndex * groundCols + pxGround;
                                        ImageSource? gimg = null;
                                        if (groundTonedImages != null && arrIdx >= 0 && arrIdx < groundTonedImages.Length) gimg = groundTonedImages[arrIdx];
                                        if (gimg == null && groundImages != null && arrIdx >= 0 && arrIdx < groundImages.Length) gimg = groundImages[arrIdx];
                                        if (gimg != null)
                                        {
                                            if (gimg is BitmapSource gbs)
                                            {
                                                double imgW = Math.Max(1.0, gbs.PixelWidth);
                                                double imgH = Math.Max(1.0, gbs.PixelHeight);
                                                if (imgW <= TILE && imgH <= TILE)
                                                {
                                                    double x = dest.X + (TILE - imgW) / 2.0;
                                                    double y = dest.Y + (TILE - imgH);
                                                    dc.DrawImage(gimg, new Rect(x, y, imgW, imgH));
                                                }
                                                else
                                                {
                                                    dc.DrawImage(gimg, dest);
                                                }
                                            }
                                            else
                                            {
                                                dc.DrawImage(gimg, dest);
                                            }
                                        }
                                        else
                                        {
                                            dc.DrawRectangle(Brushes.Black, null, dest);
                                        }
                                        continue;
                                    }
                                    else
                                    {
                                        dc.DrawRectangle(Brushes.Black, null, dest);
                                        continue;
                                    }
                                }

                                // Normal map tile sampling
                                if (mapX < 0 || mapX >= mapWidth || mapY < 0 || mapY >= mapHeight)
                                {
                                    dc.DrawRectangle(Brushes.Black, null, dest);
                                    continue;
                                }
                                int idx = mapY * mapWidth + mapX;
                                int t = tiles[idx];
                                int animatedTileIndex = MapAnimatedTileIndex(t);
                                int useTileIndex = animatedTileIndex;
                                // Apply simulator-specific remapping so certain tile codes
                                // render exactly like other tile indices (or transparent).
                                useTileIndex = ResolveSimulatorTileIndex(useTileIndex);
                                if (animatedTileIndex != t || useTileIndex >= 1000) hadAnimated = true;
                                ImageSource? chosenTile = null;
                                try
                                {
                                    if (useTileIndex >= 1000)
                                    {
                                        // Use the same animation cadence as sprite frames so saws flip in sync
                                        bool frame2 = (((animationFrame * 9) / 20) % 2) != 0;
                                        int ut = useTileIndex;
                                        // Prefer explicit tileImages/toned images for animated tile indices when available
                                        if (tileTonedImages != null && useTileIndex >= 0 && useTileIndex < tileTonedImages.Length && tileTonedImages[useTileIndex] != null)
                                        {
                                            chosenTile = tileTonedImages[useTileIndex];
                                        }
                                        else if (tileImages != null && useTileIndex >= 0 && useTileIndex < tileImages.Length && tileImages[useTileIndex] != null)
                                        {
                                            chosenTile = tileImages[useTileIndex];
                                        }
                                        else if (ut >= 1000 && ut <= 1007)
                                        {
                                            int off = (ut - 1000) % 4;
                                            int len1 = sawFrame1TilesTinted != null ? sawFrame1TilesTinted.Length : 0;
                                            int len2 = sawFrame2TilesTinted != null ? sawFrame2TilesTinted.Length : 0;
                                            int tileCount = 4;
                                            int nFrames1 = len1 >= tileCount && len1 % tileCount == 0 ? len1 / tileCount : 1;
                                            int nFrames2 = len2 >= tileCount && len2 % tileCount == 0 ? len2 / tileCount : 1;
                                            int frameCount = Math.Max(1, Math.Max(nFrames1, nFrames2));
                                            int frameIdx = (((animationFrame * 9) / 20)) % frameCount;
                                            bool useFrame2 = (((animationFrame * 9) / 20) % 2) == 1;

                                            if (useFrame2 && sawFrame2TilesTinted != null)
                                            {
                                                int arrIdx = (nFrames2 > 1 ? (frameIdx % nFrames2) * tileCount + off : off);
                                                if (arrIdx >= 0 && arrIdx < len2) chosenTile = sawFrame2TilesTinted[arrIdx];
                                            }
                                            if (chosenTile == null && sawFrame1TilesTinted != null)
                                            {
                                                int arrIdx = (nFrames1 > 1 ? (frameIdx % nFrames1) * tileCount + off : off);
                                                if (arrIdx >= 0 && arrIdx < len1) chosenTile = sawFrame1TilesTinted[arrIdx];
                                            }
                                        }
                                        else if (ut >= 1010 && ut <= 1015)
                                        {
                                            int off = (ut - 1010) % 3;
                                            int len1 = smallSawFrame1TilesTinted != null ? smallSawFrame1TilesTinted.Length : 0;
                                            int len2 = smallSawFrame2TilesTinted != null ? smallSawFrame2TilesTinted.Length : 0;
                                            int tileCount = 3;
                                            int nFrames1 = len1 >= tileCount && len1 % tileCount == 0 ? len1 / tileCount : 1;
                                            int nFrames2 = len2 >= tileCount && len2 % tileCount == 0 ? len2 / tileCount : 1;
                                            int frameCount = Math.Max(1, Math.Max(nFrames1, nFrames2));
                                            int frameIdx = (((animationFrame * 9) / 20)) % frameCount;
                                            bool useFrame2 = (((animationFrame * 9) / 20) % 2) == 1;

                                            if (useFrame2 && smallSawFrame2TilesTinted != null)
                                            {
                                                int arrIdx = (nFrames2 > 1 ? (frameIdx % nFrames2) * tileCount + off : off);
                                                if (arrIdx >= 0 && arrIdx < len2) chosenTile = smallSawFrame2TilesTinted[arrIdx];
                                            }
                                            if (chosenTile == null && smallSawFrame1TilesTinted != null)
                                            {
                                                int arrIdx = (nFrames1 > 1 ? (frameIdx % nFrames1) * tileCount + off : off);
                                                if (arrIdx >= 0 && arrIdx < len1) chosenTile = smallSawFrame1TilesTinted[arrIdx];
                                            }
                                        }
                                        else if (ut >= 1020 && ut <= 1037)
                                        {
                                            int off = (ut - 1020) % 9;
                                            int len1 = largeSawFrame1TilesTinted != null ? largeSawFrame1TilesTinted.Length : 0;
                                            int len2 = largeSawFrame2TilesTinted != null ? largeSawFrame2TilesTinted.Length : 0;
                                            int tileCount = 9;
                                            int nFrames1 = len1 >= tileCount && len1 % tileCount == 0 ? len1 / tileCount : 1;
                                            int nFrames2 = len2 >= tileCount && len2 % tileCount == 0 ? len2 / tileCount : 1;
                                            int frameCount = Math.Max(1, Math.Max(nFrames1, nFrames2));
                                            int frameIdx = (((animationFrame * 9) / 20)) % frameCount;
                                            bool useFrame2 = (((animationFrame * 9) / 20) % 2) == 1;

                                            if (useFrame2 && largeSawFrame2TilesTinted != null)
                                            {
                                                int arrIdx = (nFrames2 > 1 ? (frameIdx % nFrames2) * tileCount + off : off);
                                                if (arrIdx >= 0 && arrIdx < len2) chosenTile = largeSawFrame2TilesTinted[arrIdx];
                                            }
                                            if (chosenTile == null && largeSawFrame1TilesTinted != null)
                                            {
                                                int arrIdx = (nFrames1 > 1 ? (frameIdx % nFrames1) * tileCount + off : off);
                                                if (arrIdx >= 0 && arrIdx < len1) chosenTile = largeSawFrame1TilesTinted[arrIdx];
                                            }
                                        }

                                        // (no diagnostics) intentionally left blank
                                    }
                                }
                                catch { }

                                if (chosenTile == null && useTileIndex >= 0)
                                {
                                    // Use the animated tile index (useTileIndex) consistently when selecting the image.
                                    // For a few ground-related tile indices, prefer applying the ground tint
                                    // (these tiles should respond to ground tint, not tile tint).
                                    // These specific tiles should behave exactly like the ground image.
                                    var groundAffected = (useTileIndex == 0x01 || useTileIndex == 0x02 || useTileIndex == 0x05 || useTileIndex == 0x06 || useTileIndex == 0x88 || useTileIndex == 0x89);

                                    if (groundAffected)
                                    {
                                        try
                                        {
                                            ImageSource? gt = null;
                                            // Include both groundTint and current tileTint in the cache key so
                                            // per-tile ground-tinted copies respond to later tile/object tints.
                                            long key = (((long)useTileIndex) << 48)
                                                       | (((long)groundTint.A & 0xFF) << 40) | (((long)groundTint.R & 0xFF) << 32) | (((long)groundTint.G & 0xFF) << 24) | (((long)groundTint.B & 0xFF) << 16)
                                                       | (((long)tileTint.A & 0xFF) << 8) | (((long)tileTint.R & 0xFF));
                                            if (!groundTintedTileCache.TryGetValue(key, out gt))
                                            {
                                                // create a ground-tinted copy from original tileImages when available
                                                if (tileImages != null && useTileIndex >= 0 && useTileIndex < tileImages.Length && tileImages[useTileIndex] != null)
                                                {
                                                    try
                                                    {
                                                            // If background was forced to solid black (0x8F), ensure
                                                            // ground-affected tiles also observe the black-mask so
                                                            // non-white/non-green pixels become black. Preserve seams.
                                                            if (backgroundForceSolidBlack)
                                                            {
                                                                try { gt = CreateBlackMaskedExceptColor(tileImages[useTileIndex], playerPlaceholderGreen, Color.FromArgb(0,0,0,0), false); }
                                                                catch { /* fallthrough to other logic below */ }
                                                            }
                                                            if (gt == null)
                                                            {
                                                        if (groundTint.A == 255 && groundTint.R == 0 && groundTint.G == 0 && groundTint.B == 0)
                                                        {
                                                            // Pure black ground tint: make a black-masked copy (preserve/recolor white lines using tileTint)
                                                            // Use two-tone mapping for this single tile so non-white pixels
                                                            // become pure black while near-white outlines remain for
                                                            // object tinting control.
                                                            try
                                                            {
                                                                // Preserve white seam outlines rather than recoloring them to tileTint
                                                                var arrGt = CreateTwoToneTileImages(new ImageSource?[] { tileImages[useTileIndex]! }, Color.FromArgb(255, 0, 0, 0), Color.FromArgb(255, 0, 0, 0), Color.FromArgb(0, 0, 0, 0));
                                                                if (arrGt != null && arrGt.Length > 0) gt = arrGt[0];
                                                            }
                                                            catch { gt = CreateBlackMaskedImage(tileImages[useTileIndex], tileTint, false); }
                                                        }
                                                        else
                                                        {
                                                            // Use two-tone mapping like ground: lighter = selected ground tint,
                                                            // darker = palette row-up color. If an object/tile outline tint is active
                                                            // for this regeneration, pass it so seam pixels recolor to the object
                                                            // color. Otherwise preserve seams by passing transparent outline.
                                                            try
                                                            {
                                                                var darker = PaletteHelper.RowUpColor(Color.FromArgb(groundTint.A, groundTint.R, groundTint.G, groundTint.B));
                                                                var outlineForThisParam = startupTintApplied ? Color.FromArgb(0, 0, 0, 0) : tileTint;
                                                                var outlineForThis = (outlineForThisParam.A > 0) ? outlineForThisParam : Color.FromArgb(0, 0, 0, 0);
                                                                var arr = CreateTwoToneTileImages(new ImageSource?[] { tileImages[useTileIndex]! }, groundTint, darker, outlineForThis);
                                                                if (arr != null && arr.Length > 0) gt = arr[0];
                                                            }
                                                            catch
                                                            {
                                                                var arr = CreateHslShiftedImages(new ImageSource?[] { tileImages[useTileIndex]! }, groundTint, tileTint);
                                                                if (arr != null && arr.Length > 0) gt = arr[0];
                                                            }
                                                        }
                                                            }
                                                    }
                                                    catch { gt = tileImages[useTileIndex]; }
                                                }
                                                groundTintedTileCache[key] = gt;
                                            }
                                            if (gt != null) chosenTile = gt;

                                            // Ensure object (outline) tint recolors the seam for these ground-affected
                                            // tiles exactly like normal tiles: when an object/tile outline tint is
                                            // active, regenerate from the ORIGINAL tile image using a two-step
                                            // process: (1) two-tone ground mapping that PRESERVES seams (transparent
                                            // outline param), then (2) apply the outline recolor onto that result.
                                            // This bypasses any cached ground-only images and guarantees parity.
                                            try
                                            {
                                                if (tileImages != null && useTileIndex >= 0 && useTileIndex < tileImages.Length && tileImages[useTileIndex] != null)
                                                {
                                                    var darker2 = PaletteHelper.RowUpColor(Color.FromArgb(groundTint.A, groundTint.R, groundTint.G, groundTint.B));
                                                    if (tileTint.A > 0)
                                                    {
                                                        // Active object tint: preserve seams during two-tone, then recolor outlines to object tint
                                                        var twoToneArr = CreateTwoToneTileImages(new ImageSource?[] { tileImages[useTileIndex]! }, groundTint, darker2, Color.FromArgb(0, 0, 0, 0));
                                                        ImageSource? twoToneBase = (twoToneArr != null && twoToneArr.Length > 0) ? twoToneArr[0] : tileImages[useTileIndex];
                                                        var finalArr = CreateOutlineTintedTileImages(new ImageSource?[] { twoToneBase }, tileTint);
                                                        if (finalArr != null && finalArr.Length > 0 && finalArr[0] != null)
                                                        {
                                                            chosenTile = finalArr[0];
                                                        }
                                                    }
                                                    else
                                                    {
                                                        // No object tint yet: force seams to opaque white in the two-tone pass
                                                        var twoToneArr = CreateTwoToneTileImages(new ImageSource?[] { tileImages[useTileIndex]! }, groundTint, darker2, Color.FromArgb(255, 255, 255, 255));
                                                        if (twoToneArr != null && twoToneArr.Length > 0 && twoToneArr[0] != null)
                                                        {
                                                            chosenTile = twoToneArr[0];
                                                        }
                                                    }
                                                }
                                            }
                                            catch { }
                                        }
                                        catch { }
                                    }

                                    if (chosenTile == null)
                                    {
                                        if (useTileIndex >= 1000)
                                        {
                                            if (tileTonedImages != null && useTileIndex >= 0 && useTileIndex < tileTonedImages.Length && tileTonedImages[useTileIndex] != null)
                                                chosenTile = tileTonedImages[useTileIndex];
                                            else if (tileImages != null && useTileIndex >= 0 && useTileIndex < tileImages.Length && tileImages[useTileIndex] != null)
                                                chosenTile = tileImages[useTileIndex];
                                        }
                                        else
                                        {
                                            if (tileTonedImages != null && useTileIndex >= 0 && useTileIndex < tileTonedImages.Length && tileTonedImages[useTileIndex] != null)
                                                chosenTile = tileTonedImages[useTileIndex];
                                            else if (tileImages != null && useTileIndex >= 0 && useTileIndex < tileImages.Length && tileImages[useTileIndex] != null)
                                                chosenTile = tileImages[useTileIndex];
                                        }
                                    }
                                }

                                // Safety: ensure non-ground tiles receive outline recolor when an object tint is active.
                                try
                                {
                                    if ((useTileIndex != 0x01 && useTileIndex != 0x02 && useTileIndex != 0x05 && useTileIndex != 0x06 && useTileIndex != 0x88 && useTileIndex != 0x89) && tileTint.A > 0 && useTileIndex >= 0)
                                    {
                                        ImageSource? baseSrc = null;
                                        if (tileTonedImages != null && useTileIndex >= 0 && useTileIndex < tileTonedImages.Length)
                                            baseSrc = tileTonedImages[useTileIndex];
                                        if (baseSrc == null && tileImages != null && useTileIndex >= 0 && useTileIndex < tileImages.Length)
                                            baseSrc = tileImages[useTileIndex];

                                        if (baseSrc != null)
                                        {
                                            try
                                            {
                                                var trecol = CreateOutlineTintedTileImages(new ImageSource?[] { baseSrc }, tileTint);
                                                if (trecol != null && trecol.Length > 0 && trecol[0] != null)
                                                    chosenTile = trecol[0];
                                            }
                                            catch { }
                                        }
                                    }
                                }
                                catch { }

                                if (chosenTile != null)
                                {
                                    try
                                    {
                                        if (tileSelectionLogCount < TILE_SELECTION_LOG_LIMIT)
                                        {
                                            string src = "unknown";
                                            try
                                            {
                                                if (tileTonedImages != null && useTileIndex >= 0 && useTileIndex < tileTonedImages.Length && tileTonedImages[useTileIndex] == chosenTile)
                                                    src = "tileTonedImages";
                                                else if (tileImages != null && useTileIndex >= 0 && useTileIndex < tileImages.Length && tileImages[useTileIndex] == chosenTile)
                                                    src = "tileImages";
                                                else
                                                {
                                                    foreach (var kv in groundTintedTileCache)
                                                    {
                                                        if (kv.Value == chosenTile) { src = "groundTintedTileCache"; break; }
                                                    }
                                                }
                                            }
                                            catch { }
                                            AppendSimDebug($"GetOrRenderCachedTile idx={useTileIndex} src={src} mapIdx={idx} tileVal=0x{t:X}");
                                            tileSelectionLogCount++;
                                        }
                                    }
                                    catch { }
                                    // If background is forced to solid black, ensure the chosen tile
                                    // used for drawing is the black-masked variant (preserving exact
                                    // player-green and white seams). Cache masked variants to avoid
                                    // repeated pixel processing.
                                    try
                                    {
                                        // If this tile contains the special-deco green, ensure those
                                        // pixels are recolored to `playerTint` now so masking uses
                                        // the player-colored pixels and doesn't accidentally black them.
                                        try
                                        {
                                            int[] special = new int[] { 0x0C, 0x0D, 0x0E, 0x0F, 0x13, 0x14, 0x80, 0x81, 0x84, 0x85, 0x86, 0x87 };
                                            if (chosenTile != null && tileImages != null && useTileIndex >= 0 && System.Array.IndexOf(special, useTileIndex) >= 0)
                                            {
                                                try { chosenTile = ApplyPlayerTintToGreenPixels(chosenTile, tileImages[useTileIndex], playerTint); } catch { }
                                            }
                                        }
                                        catch { }

                                        if (backgroundForceSolidBlack && chosenTile != null)
                                        {
                                            // Choose an exclude color so the black-mask preserves important pixels:
                                            // - If this tile contains player-green deco, preserve the player-tinted color for those
                                            //   pixels. Otherwise preserve outline-colored pixels (tileTint) or placeholder green.
                                            Color excludeColor;
                                            int[] special2 = new int[] { 0x0C, 0x0D, 0x0E, 0x0F, 0x13, 0x14, 0x80, 0x81, 0x84, 0x85, 0x86, 0x87 };
                                            if (tileImages != null && useTileIndex >= 0 && System.Array.IndexOf(special2, useTileIndex) >= 0)
                                            {
                                                excludeColor = (playerTint.A > 0) ? playerTint : Color.FromArgb(255, 0x5A, 0xCE, 0x52);
                                            }
                                            else
                                            {
                                                excludeColor = (tileTint.A > 0) ? Color.FromArgb(255, tileTint.R, tileTint.G, tileTint.B) : playerPlaceholderGreen;
                                            }

                                            // Include the excludeColor in the cache key so different exclude colors
                                            // produce different masked variants.
                                            // Use tile index + exclude color as the cache key to avoid sharing
                                            // masked ImageSource instances between different tile indices.
                                            long compositeKey = (((long)useTileIndex & 0xFFFFL) << 32)
                                                                | (((long)excludeColor.A & 0xFFL) << 24)
                                                                | (((long)excludeColor.R & 0xFFL) << 16)
                                                                | (((long)excludeColor.G & 0xFFL) << 8)
                                                                | (((long)excludeColor.B & 0xFFL));

                                            if (!blackMaskedTileCache.TryGetValue(compositeKey, out var masked))
                                            {
                                                try { masked = CreateBlackMaskedExceptColor(chosenTile, excludeColor, Color.FromArgb(0, 0, 0, 0), false); }
                                                catch { masked = chosenTile; }
                                                try { blackMaskedTileCache[compositeKey] = masked; } catch { }
                                            }
                                            if (masked != null) chosenTile = masked;
                                        }
                                    }
                                    catch { }
                                    // If the tile image is smaller than the canonical TILE size (e.g.
                                    // saw halves that are half-height PNGs), draw it at its natural
                                    // pixel size instead of scaling to fill the full tile. Align
                                    // smaller images to the bottom of the tile so transparent
                                    // padding sits above as expected.
                                    if (chosenTile is BitmapSource bs)
                                    {
                                        // Use the bitmap's pixel dimensions to decide if it should be
                                        // drawn at natural size. Device-independent units in this
                                        // renderer correspond to pixels (RTB created at 96 DPI), so
                                        // bs.PixelWidth/Height work directly.
                                        double imgW = Math.Max(1.0, bs.PixelWidth);
                                        double imgH = Math.Max(1.0, bs.PixelHeight);

                                        if (imgW <= TILE && imgH <= TILE)
                                        {
                                            // bottom-align within the tile cell
                                            double x = dest.X + (TILE - imgW) / 2.0;
                                            double y = dest.Y + (TILE - imgH);

                                            // Nudge small-saw bottom halves up slightly so they align with the editor preview.
                                            // Small saw animated tile indices live in the 1010..1015 range. Only apply
                                            // for very short images (half-height) to avoid disturbing other tiles.
                                                try
                                                {
                                                    if (useTileIndex >= 1010 && useTileIndex <= 1015 && imgH <= (TILE / 2.0))
                                                    {
                                                        try
                                                        {
                                                            if (bs != null && IsImageTopHeavy(bs))
                                                                y -= 8; // move up 8 pixels for upside-down half bottoms only
                                                        }
                                                        catch { }
                                                    }
                                                }
                                                catch { }

                                            dc.DrawImage(chosenTile, new Rect(x, y, imgW, imgH));
                                        }
                                        else
                                        {
                                            // image larger than tile: fall back to scaling to tile
                                            dc.DrawImage(chosenTile, dest);
                                        }
                                    }
                                    else
                                    {
                                        dc.DrawImage(chosenTile, dest);
                                    }
                                }
                                else
                                {
                                    dc.DrawRectangle(Brushes.Black, null, dest);
                                }
                            }
                        }
                    }

                    var rtb = new RenderTargetBitmap(pxW, pxH, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(dv);
                    try { rtb.Freeze(); } catch { }
                    tileLayerCache = rtb;
                    try { spriteBackgroundCompositeCache.Clear(); } catch { }
                    try
                    {
                        AppendSimDebug($"Built tileLayerCache pxW={pxW} pxH={pxH} hadAnimated={hadAnimated} startTileX={startTileX} startTileY={startTileY} tilesLen={(tiles==null?0:tiles.Length)} mapWidth={mapWidth} mapHeight={mapHeight}");
                    }
                    catch { }
                    lastCacheHadAnimatedTiles = hadAnimated;
                    lastCacheAnimationFrame = animationFrame;
                    cachedStartTileX = startTileX;
                    cachedStartTileY = startTileY;
                    _currentCacheTilesX = cacheTilesX;
                    _currentCacheTilesY = cacheTilesY;
                    tileLayerImage!.Source = tileLayerCache;
                    try { AppendSimDebug($"Assigned tileLayerImage.Source={(tileLayerImage.Source==null?"null":tileLayerImage.Source.GetType().Name)}"); } catch { }
                    tileLayerImage!.Width = pxW;
                    tileLayerImage!.Height = pxH;
                }
            }
            catch { }

                // Position the tile layer to account for fractional pixel offset (including sub-pixel precision)
            try { if (tileLayerImage != null) { System.Windows.Controls.Canvas.SetLeft(tileLayerImage, -offsetX - subPixelOffsetX); System.Windows.Controls.Canvas.SetTop(tileLayerImage, -offsetY - subPixelOffsetY); } } catch { }

            // Update per-tile hitbox overlays so they render above the tile layer but below sprites.
            try
            {
                tileHitboxesInUse = 0;
                if (ShowTileHitboxes)
                {
                    // Reuse visible tile rects: iterate the same visible tile grid used for cache
                    for (int vx = 0; vx < _currentCacheTilesX; vx++)
                    {
                        int mapX = startTileX + vx;
                        for (int vy = 0; vy < _currentCacheTilesY; vy++)
                        {
                            int mapY = startTileY + groundRowsToReserve + vy;
                            if (mapX < 0 || mapX >= mapWidth || mapY < 0 || mapY >= mapHeight) continue;

                            // Determine collision type for this visible tile and skip COL_NONE tiles
                            MetatileCollision col = MetatileCollision.COL_NONE;
                            bool hasDeath = false;
                            try
                            {
                                if (tiles == null) continue;
                                int tid = tiles[mapY * mapWidth + mapX];
                                int useTidForAnim = MapAnimatedTileIndex(tid);
                                int collisionTid = useTidForAnim;
                                if (useTidForAnim >= 1000)
                                {
                                    if (useTidForAnim >= 1000 && useTidForAnim <= 1007)
                                    {
                                        collisionTid = 0x08 + ((useTidForAnim - 1000) % 4);
                                    }
                                    else if (useTidForAnim >= 1010 && useTidForAnim <= 1015)
                                    {
                                        int group = (useTidForAnim - 1010) % 3;
                                        collisionTid = (group == 0) ? 0x04 : (group == 1) ? 0x7D : 0x7F;
                                    }
                                    else if (useTidForAnim >= 1020 && useTidForAnim <= 1037)
                                    {
                                        collisionTid = 0x74 + ((useTidForAnim - 1020) % 9);
                                    }
                                    else
                                    {
                                        collisionTid = tid;
                                    }
                                }
                                col = MetatileCollisionTable.GetCollision((byte)collisionTid);
                                if (col == MetatileCollision.COL_NONE) continue; // skip transparent/no-collision tiles
                                
                                // Check if this tile has death collision
                                for (int ly = 0; ly < TILE && !hasDeath; ly++)
                                {
                                    for (int lx = 0; lx < TILE && !hasDeath; lx++)
                                    {
                                        if (MetatileCollisionTable.TileKillsAtPixel(col, lx, ly))
                                        {
                                            hasDeath = true;
                                        }
                                    }
                                }
                            }
                            catch { }

                            // Determine if this requires pixel-by-pixel rendering:
                            // - Complex collision shapes (L-shapes, diagonals)
                            // - Tiles with death collision (to show death area distinctly)
                            bool isComplex = col == MetatileCollision.COL_TOP_LEFT_STAIRS ||
                                           col == MetatileCollision.COL_TOP_RIGHT_STAIRS ||
                                           col == MetatileCollision.COL_BOTTOM_LEFT_STAIRS ||
                                           col == MetatileCollision.COL_BOTTOM_RIGHT_STAIRS ||
                                           col == MetatileCollision.COL_TOP_LEFT_BOTTOM_RIGHT ||
                                           col == MetatileCollision.COL_TOP_RIGHT_BOTTOM_LEFT ||
                                           col == MetatileCollision.COL_DEATH_TOP_RIGHT ||
                                           col == MetatileCollision.COL_DEATH_TOP_LEFT ||
                                           col == MetatileCollision.COL_DEATH_BOTTOM_RIGHT ||
                                           col == MetatileCollision.COL_DEATH_BOTTOM_LEFT ||
                                           col == MetatileCollision.COL_DEATH_TOP_RIGHT_LEFT ||
                                           col == MetatileCollision.COL_DEATH_TOP_BOTTOM ||
                                           col == MetatileCollision.COL_DEATH_LEFT_RIGHT ||
                                           col == MetatileCollision.COL_DEATH_TOP_LEFT_BOTTOM ||
                                           col == MetatileCollision.COL_TOP_CENTER_SPIKE ||
                                           col == MetatileCollision.COL_BOTTOM_CENTER_SPIKE ||
                                           col == MetatileCollision.COL_LEFT_SPIKE_BLOCK ||
                                           col == MetatileCollision.COL_RIGHT_SPIKE_BLOCK ||
                                           col == MetatileCollision.COL_BOTTOM_LEFT_SPIKE ||
                                           col == MetatileCollision.COL_BOTTOM_RIGHT_SPIKE ||
                                           col == MetatileCollision.COL_BOTTOM_SPIKES;

                            // Slopes need pixel-by-pixel rendering for their diagonal surfaces
                            bool isSlope = IsSlopeTile(col);

                            // Also do pixel-by-pixel for any tile with death collision or slopes
                            bool needsPixelByPixel = isComplex || hasDeath || isSlope;

                            if (needsPixelByPixel)
                            {
                                // Determine if this is a pure death tile (no solid collision) vs mixed
                                bool isPureDeathTile = col == MetatileCollision.COL_UP_LEFT_SPIKE ||
                                                      col == MetatileCollision.COL_UP_RIGHT_SPIKE ||
                                                      col == MetatileCollision.COL_UP_BOTH_SPIKES ||
                                                      col == MetatileCollision.COL_DOWN_LEFT_SPIKE ||
                                                      col == MetatileCollision.COL_DOWN_RIGHT_SPIKE ||
                                                      col == MetatileCollision.COL_DOWN_BOTH_SPIKES ||
                                                      col == MetatileCollision.COL_DEATH ||
                                                      col == MetatileCollision.COL_DEATH_TOP ||
                                                      col == MetatileCollision.COL_DEATH_BOTTOM ||
                                                      col == MetatileCollision.COL_DEATH_LEFT ||
                                                      col == MetatileCollision.COL_DEATH_RIGHT ||
                                                      col == MetatileCollision.COL_DEATH_BOTTOM_LEFT ||
                                                      col == MetatileCollision.COL_DEATH_BOTTOM_RIGHT ||
                                                      col == MetatileCollision.COL_DEATH_TOP_LEFT ||
                                                      col == MetatileCollision.COL_DEATH_TOP_RIGHT ||
                                                      col == MetatileCollision.COL_DEATH_TOP_RIGHT_LEFT ||
                                                      col == MetatileCollision.COL_DEATH_TOP_BOTTOM ||
                                                      col == MetatileCollision.COL_DEATH_LEFT_RIGHT ||
                                                      col == MetatileCollision.COL_DEATH_TOP_LEFT_BOTTOM;

                                // For complex shapes, render individual pixels that have collision
                                for (int ly = 0; ly < TILE; ly++)
                                {
                                    for (int lx = 0; lx < TILE; lx++)
                                    {
                                        bool hasCollision = TileOccupiesPixel(col, lx, ly);
                                        bool isDeathPixel = MetatileCollisionTable.TileKillsAtPixel(col, lx, ly);
                                        bool isSlopePixel = isSlope && IsSlopeSolidAtPixel(col, lx, ly);
                                        
                                        // Skip pixels that have neither collision, death, nor slope
                                        if (!hasCollision && !isDeathPixel && !isSlopePixel) continue;
                                        
                                        // For pure death tiles, only render death pixels
                                        // For mixed tiles, render both collision (red) and death (pink)
                                        if (isPureDeathTile && !isDeathPixel) continue;

                                        System.Windows.Shapes.Rectangle r;
                                        if (tileHitboxesInUse < tileHitboxPool.Count)
                                        {
                                            r = tileHitboxPool[tileHitboxesInUse];
                                            r.Visibility = Visibility.Visible;
                                        }
                                        else
                                        {
                                            r = new System.Windows.Shapes.Rectangle();
                                            r.IsHitTestVisible = false;
                                            r.Stroke = null;
                                            tileHitboxPool.Add(r);
                                            RenderCanvas.Children.Add(r);
                                            try { System.Windows.Controls.Canvas.SetZIndex(r, 100); } catch { }
                                        }

                                        r.Fill = isDeathPixel
                                                ? new SolidColorBrush(Color.FromArgb(160, 0xFF, 0x50, 0xC8)) // Magenta/pink for death
                                                : new SolidColorBrush(Color.FromArgb(160, 0xFF, 0x00, 0x00)); // Red for collision/slope

                                        r.Width = 1;
                                        r.Height = 1;
                                        System.Windows.Controls.Canvas.SetLeft(r, vx * TILE + lx - offsetX);
                                        System.Windows.Controls.Canvas.SetTop(r, vy * TILE + ly - offsetY + gridRenderShiftYPx);
                                        tileHitboxesInUse++;
                                    }
                                }
                            }
                            else
                            {
                                // For simple shapes, get collision bounds and draw a single rectangle
                                var (colLeft, colTop, colRight, colBottom) = GetCollisionBounds(col);
                                if (colRight <= colLeft || colBottom <= colTop) continue; // Invalid bounds

                                int rectWidth = colRight - colLeft;
                                int rectHeight = colBottom - colTop;

                                System.Windows.Shapes.Rectangle r;
                                if (tileHitboxesInUse < tileHitboxPool.Count)
                                {
                                    r = tileHitboxPool[tileHitboxesInUse];
                                    r.Visibility = Visibility.Visible;
                                }
                                else
                                {
                                    r = new System.Windows.Shapes.Rectangle();
                                    r.IsHitTestVisible = false;
                                    r.Stroke = null;
                                    tileHitboxPool.Add(r);
                                    RenderCanvas.Children.Add(r);
                                    try { System.Windows.Controls.Canvas.SetZIndex(r, 100); } catch { }
                                }

                                // Set color based on whether it has death collision
                                r.Fill = hasDeath
                                    ? new SolidColorBrush(Color.FromArgb(160, 0x80, 0x00, 0x80)) // Purple for death
                                    : new SolidColorBrush(Color.FromArgb(160, 0xFF, 0x00, 0x00)); // Red for collision

                                r.Width = rectWidth;
                                r.Height = rectHeight;
                                System.Windows.Controls.Canvas.SetLeft(r, vx * TILE + colLeft - offsetX);
                                System.Windows.Controls.Canvas.SetTop(r, vy * TILE + colTop - offsetY + gridRenderShiftYPx);
                                tileHitboxesInUse++;
                            }
                        }
                    }
                }

                // Hide unused pooled rectangles
                for (int i = tileHitboxesInUse; i < tileHitboxPool.Count; i++)
                {
                    try { tileHitboxPool[i].Visibility = Visibility.Collapsed; } catch { }
                }
            }
            catch { }

            // Update ground rectangle (render tiled ground image if available)
            try
            {
                if (groundRectPersistent != null)
                {
                    // Ground is drawn into the tile-layer cache, so the persistent rect
                    // is always collapsed. Skip brush creation to avoid wasted allocations.
                    if (hasGroundLayer && groundImages != null && groundImages.Length > 0)
                    {
                        int groundHeight = TILE * Math.Min(NES_H, Math.Max(groundTileRows, 2));
                        // Use reserved rows (up to 2) as visual ground height
                        groundHeight = TILE * Math.Min(NES_H, Math.Min(groundTileRows, 2));
                        groundRectPersistent.Height = groundHeight;
                        System.Windows.Controls.Canvas.SetTop(groundRectPersistent, (NES_H * TILE) - groundHeight);
                        groundRectPersistent.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        int groundHeight = TILE * Math.Min(NES_H, 2);
                        groundRectPersistent.Height = groundHeight;
                        System.Windows.Controls.Canvas.SetTop(groundRectPersistent, (NES_H * TILE) - groundHeight);
                        groundRectPersistent.Visibility = Visibility.Collapsed;
                    }
                }
            }
            catch { }

            // Render sprites into a single RenderTargetBitmap instead of individual Image
            // controls. This avoids WPF compositor issues with many Image children.
            spritesInUse = 0;
            var spriteDv = new DrawingVisual();
            var spriteDc = spriteDv.RenderOpen();
            for (int vx = 0; vx < _currentCacheTilesX; vx++)
            {
                int mapX = startTileX + vx;
                for (int vy = 0; vy < _currentCacheTilesY; vy++)
                {
                    int mapY = startTileY + groundRowsToReserve + vy;
                    if (mapX < 0 || mapX >= mapWidth || mapY < 0 || mapY >= mapHeight) continue;
                    int idx = mapY * mapWidth + mapX;
                    int s = sprites[idx];
                    if (s < 0) continue;
                    
                    // OPTIMIZATION: Fast screen-space cull - skip if sprite is completely off-screen
                    // Check approximate position early to avoid expensive sprite lookups for off-screen sprites
                    double cullPx = (vx * TILE) - offsetX;
                    double cullPy = (vy * TILE) - offsetY + gridRenderShiftYPx;
                    // Conservative bounds: assume max sprite size of 32x32 pixels
                    if (cullPx + 32 < 0 || cullPx > _currentCacheTilesX * TILE || cullPy + 32 < 0 || cullPy > _currentCacheTilesY * TILE)
                    {
                        // Off-screen, skip all expensive processing for this sprite
                        continue;
                    }

                    // Visual alias: make sprite 0x7B render identically to 0x05
                    // Visual alias: make sprite 0x7C (multi-hit green orb) render identically to 0x27 (green orb)
                    int s_vis = (s == 0x7B) ? 0x05 : (s == 0x7C) ? 0x27 : s;

                    // Always hide freecam portal sprites (invisible triggers)
                    if (s == 0xDD || s == 0xED) continue;
                    if (s == 0x8E || s == 0x9E) continue;
                    // Always hide timewarp, player visibility, and trail triggers
                    if (s == 0xF4 || s == 0xF5) continue;
                    if (s == 0x6F || s == 0x7F) continue;
                    if (s == 0xF2 || s == 0xF3) continue;
                    // Hide gravity portals (normal/reversed) that are invisible triggers
                    if (s == 0xEE || s == 0xEF || s == 0xFB || s == 0xFC) continue;

                    // If the global 'hide trigger sprites' option is enabled, skip drawing
                    // these specific trigger sprite images while still allowing them to
                    // function (triggers remain active in the simulation logic).
                    try { if (hideTriggerSprites && IsHiddenTriggerSprite(s)) continue; } catch { }

                    ImageSource? chosenSprite = null;
                    // Simulator-only special-case: prefer an embedded upside-down chain for sprite 0x3D
                    if (s == 0x3D)
                    {
                        if (forcePreviewMode && previewSpriteMap != null && previewSpriteMap.TryGetValue(s, out var pimg) && pimg != null)
                        {
                            chosenSprite = pimg;
                        }
                        else
                        {
                            if (chainUpsideSimImage == null)
                            {
                                try
                                {
                                    BitmapImage? bi = LoadCachedResourceImage("chain-upsidedown.png");
                                    if (bi == null)
                                    {
                                        var p = System.IO.Path.Combine(AppContext.BaseDirectory ?? ".", "chain-upsidedown.png");
                                        if (System.IO.File.Exists(p))
                                        {
                                            bi = new BitmapImage();
                                            bi.BeginInit();
                                            bi.CacheOption = BitmapCacheOption.OnLoad;
                                            bi.UriSource = new Uri(p);
                                            bi.EndInit();
                                            bi.Freeze();
                                        }
                                    }
                                    if (bi != null)
                                    {
                                        chainUpsideSimImage = new FormatConvertedBitmap(bi, PixelFormats.Pbgra32, null, 0);
                                    }
                                }
                                catch { }
                            }
                            if (chainUpsideSimImage != null) chosenSprite = chainUpsideSimImage;
                        }
                    }
                    if (animationFrames != null && animationFrames.TryGetValue(s_vis, out var frames) && frames != null && frames.Length > 0)
                    {
                        // For 2-frame decoration sprites we want a uniform cadence across all anchors
                        // so do not apply a per-anchor random offset. For other sprites/lengths, preserve
                        // per-anchor randomness so placements don't always animate in lockstep.
                        int offset = 0;
                        if (!(decorationSpriteIds.Contains(s) && frames.Length == 2))
                        {
                            if (!spriteFrameOffsets.ContainsKey(idx)) spriteFrameOffsets[idx] = spriteAnimationRandom.Next(0, Math.Max(1, frames.Length));
                            offset = spriteFrameOffsets[idx];
                        }
                        int frame = 0;
                        if (frames.Length == 2 && (decorationSpriteIds.Contains(s)
                                                     || s == 0x54 || s == 0x55
                                                     // Dash-orbs: sync them to the same two-frame decoration cadence
                                                     || s == 0x45 || s == 0x46 || s == 0x4C || s == 0x4D
                                                     || s == 0x50 || s == 0x51 || s == 0x5B || s == 0x5C
                                                     || s == 0x5D || s == 0x5E || s == 0x59 || s == 0x5A))
                        {
                            // Match editor preview two-frame cadence used by dash-orbs and decorations
                            // Make spider-orbs (0x54/0x55) pulse exactly like the dash-orb routine,
                            // but do NOT treat them as decorations (they are excluded from tinting).
                            frame = (((GetEditorAnimationFrameValue() * 3) / 40) % 2 + 2) % 2;
                        }
                        else
                        {
                            // Orbs include: 0x08-0x0B (yellow/pink/red), plus dash-orbs 0x45,0x46,0x4C,0x4D,0x50,0x51,0x5B,0x5C,0x5D,0x5E
                            bool isOrb = (s >= 0x08 && s <= 0x0B) 
                                      || s == 0x45 || s == 0x46 || s == 0x4C || s == 0x4D
                                      || s == 0x50 || s == 0x51 || s == 0x5B || s == 0x5C
                                      || s == 0x5D || s == 0x5E;
                            
                            // Slow down coins/pads/orbs and decorations to half speed
                            if (slowAnimatedSpriteIds.Contains(s) || decorationSpriteIds.Contains(s))
                            {
                                frame = (((GetEditorAnimationFrameValue() * 9) / 40) + offset) % Math.Max(1, frames.Length);
                            }
                            else if (isOrb)
                            {
                                // Orbs use slowest animation speed
                                frame = (((GetEditorAnimationFrameValue() * 9) / 60) + offset) % Math.Max(1, frames.Length);
                            }
                            else
                            {
                                // Pads and other sprites - normal speed
                                frame = (((GetEditorAnimationFrameValue() * 9) / 20) + offset) % Math.Max(1, frames.Length);
                            }
                        }
                        chosenSprite = frames[frame];
                        // Debug: log decoration frames presence/selection when the selected frame changes
                        try
                        {
                            if (enableSimulatorDebugLogging && decorationSpriteIds.Contains(s) && frames.Length == 2)
                            {
                                int prevFrame = -1;
                                decoLastSelectedFrame.TryGetValue(idx, out prevFrame);
                                if (prevFrame != frame)
                                {
                                    string h0 = frames[0] != null ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(frames[0]!).ToString("X8") : "null";
                                    string h1 = frames[1] != null ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(frames[1]!).ToString("X8") : "null";
                                    WriteTempLog($"Simulator: Decoration sprite idx={idx} id=0x{s:X} frames=[{(frames[0]!=null?"ok":"null")},{(frames[1]!=null?"ok":"null")}] selectedFrame={frame} hashes=[{h0},{h1}]");
                                    decoLastSelectedFrame[idx] = frame;
                                }
                            }
                        }
                        catch { }
                            // (no-op) removed per-request debug logging
                        if (chosenSprite == null)
                        {
                            if (forcePreviewMode && previewSpriteMap != null && previewSpriteMap.TryGetValue(s_vis, out var pimg) && pimg != null)
                                chosenSprite = pimg;
                            else if (spriteImages != null && s_vis < spriteImages.Length && spriteImages[s_vis] != null)
                                chosenSprite = spriteImages[s_vis];
                        }
                    }
                    else
                    {
                        // Special-case rainbow portals: cycle through the ordered portal sprites
                        // 0x64 = Limited random (modes 0-7: Cube through Swingcopter)
                        // 0x7E = Super random (modes 0-11: all modes including Ninja, Pogo, Snake, Football)
                        if (s == 0x64 || s == 0x7E)
                        {
                            try
                            {
                                if (previewSpriteMap != null)
                                {
                                    // For 0x64: limited to Swingcopter (8 modes)
                                    // For 0x7E: all modes (12 modes)
                                    int[] orderIds = (s == 0x64) 
                                        ? new int[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x24, 0x17, 0x4B }  // Cube, Ship, Ball, UFO, Robot, Wave, Spider, Swing
                                        : new int[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x24, 0x17, 0x4B, 0x58, 0x6A, 0x6B, 0x6C };  // + Ninja, Pogo, Snake, Football
                                    
                                    var list = new System.Collections.Generic.List<ImageSource?>();
                                    foreach (var id in orderIds)
                                    {
                                        if (previewSpriteMap.TryGetValue(id, out var img) && img != null) list.Add(img);
                                    }
                                    if (list.Count > 0)
                                    {
                                        int len = list.Count;
                                        int frameAdvance = 0;
                                        try { frameAdvance = (animationFrame / 30) % Math.Max(1, len); } catch { frameAdvance = 0; }
                                        uint seed = (uint)idx; // deterministic per-position offset (matches editor behavior)
                                        uint offset = (uint)((seed * 2654435761u) % (uint)len);
                                        int sel = (int)((offset + (uint)frameAdvance) % (uint)len);
                                        chosenSprite = list[sel];
                                    }
                                }
                            }
                            catch { }
                        }

                        if (chosenSprite == null)
                        {
                            if (forcePreviewMode && previewSpriteMap != null && previewSpriteMap.TryGetValue(s_vis, out var previewImg) && previewImg != null)
                            {
                                chosenSprite = previewImg;
                            }
                            else if (spriteImages != null && s_vis < spriteImages.Length && spriteImages[s_vis] != null)
                            {
                                chosenSprite = spriteImages[s_vis];
                            }
                        }
                    }

                    if (chosenSprite == null) continue;
                    // Always hide black color-trigger sprites (they should activate but not be visible in simulator)
                    if (s == 0x8F || s == 0xCF) continue;
                    // Always hide gravity mod triggers (0x70-0x74) - they activate but don't render
                    if (s >= 0x70 && s <= 0x74) continue;
                    if (hideColorTriggers && IsColorTriggerSprite(s)) continue;

                    // Calculate position in RTB-local coordinates (spriteLayerImage positioned at -offsetX,-offsetY)
                    double px = (mapX - startTileX) * TILE;
                    double py = (vy * TILE) + gridRenderShiftYPx;
                    if (spriteAnchors != null && spriteAnchors.TryGetValue(idx, out var anchor))
                    {
                        int storageTileX = idx % mapWidth;
                        int storageTileY = idx / mapWidth;
                        int tileDeltaX = storageTileX - anchor.anchorTileX;
                        int tileDeltaY = storageTileY - anchor.anchorTileY;
                        double anchorDisplayX = (anchor.anchorTileX - startTileX) * TILE;
                        double anchorDisplayY = (anchor.anchorTileY - (startTileY + groundRowsToReserve)) * TILE;
                        px = anchorDisplayX + tileDeltaX * TILE;
                        py = anchorDisplayY + tileDeltaY * TILE + gridRenderShiftYPx;
                    }
                    
                    // OPTIMIZATION: Skip expensive tinting/compositing for off-screen sprites
                    // Get sprite dimensions to do proper bounds checking before expensive operations
                    int spriteWidth = 16, spriteHeight = 16;
                    if (chosenSprite is BitmapSource bs)
                    {
                        spriteWidth = bs.PixelWidth;
                        spriteHeight = bs.PixelHeight;
                    }
                    // Check if completely off the RTB (with margin for sprites that extend beyond)
                    if (px + spriteWidth < -16 || px > _currentCacheTilesX * TILE + 16 || py + spriteHeight < -16 || py > _currentCacheTilesY * TILE + 16)
                    {
                        // Off-screen: skip expensive tinting and compositing operations
                        continue;
                    }

                    // Apply player tint to decoration sprites when enabled
                    try
                    {
                            // No special-case preview override for 0x3D: let the normal selection/fallbacks apply.

                        if (playerTintEnabled && decorationSpriteIds.Contains(s) && !nonPlayerTintSpriteIds.Contains(s) && chosenSprite != null)
                        {
                            chosenSprite = GetPlayerTintedSprite(chosenSprite, s_vis);
                        }
                    }
                    catch { }
                    if (spritePixelOffsets != null && spritePixelOffsets.TryGetValue(idx, out var offs))
                    {
                        px += offs.offsetX;
                        py += offs.offsetY; // match editor convention: positive offsetY moves sprite down
                    }
                    else if (spriteAnchors != null && spriteAnchors.TryGetValue(idx, out var anc))
                    {
                        int anchorKey = anc.anchorTileY * mapWidth + anc.anchorTileX;
                        if (spritePixelOffsets != null && spritePixelOffsets.TryGetValue(anchorKey, out var aoffs))
                        {
                            px += aoffs.offsetX;
                            py += aoffs.offsetY; // match editor convention
                        }
                    }

                    // Simulator tweak: medium poles (sprite id 0x2B and 0x2C) render 8px higher in preview
                    try
                    {
                        if (s == 0x2B || s == 0x2C) py -= 8;
                        // Shift long poles (sprite 0x2A/0x3A) upwards by 1.5 tiles in the simulator
                        if (s == 0x2A || s == 0x3A)
                        {
                            try
                            {
                                // Move up 1.5 tiles, then correct by moving down 8px per latest request
                                int shift = (int)Math.Round(TILE * 1.5) - 8; // net 1 tile (24-8=16)
                                py = Math.Max(0, py - shift);
                            }
                            catch { }
                        }
                        // Upright chains (0x2D) are nudged up to match preview; do not special-case 0x3D.
                        if (s == 0x2D) py -= 8;
                        // If this decoration sprite appears upside-down (content at top), nudge it up as well.
                        try
                        {
                            if (decorationSpriteIds.Contains(s) && s != 0x2D && chosenSprite is BitmapSource cbs && cbs.PixelHeight <= (TILE / 2.0))
                            {
                                if (IsImageTopHeavy(cbs)) py -= 8;
                            }
                        }
                        catch { }
                    }
                    catch { }

                    ImageSource? finalSprite = chosenSprite;

                    // Draw sprite into the sprite layer DrawingVisual
                    if (finalSprite is BitmapSource fbs)
                    {
                        spriteDc.DrawImage(finalSprite, new Rect(px, py, fbs.PixelWidth, fbs.PixelHeight));
                    }
                    spritesInUse++;

                    // Cache the world-space hitbox rect derived from the same values the renderer
                    // used so collisions can match the visible sprite even when overlays are off.
                    try
                    {
                        int id_for_overlay = s & 0xFF;
                        if (spriteAnchors != null && spriteAnchors.TryGetValue(idx, out var anch2))
                        {
                            int anchorKey2 = anch2.anchorTileY * mapWidth + anch2.anchorTileX;
                            if (anchorKey2 >= 0 && anchorKey2 < sprites.Length)
                            {
                                int anchoredId2 = sprites[anchorKey2];
                                if (anchoredId2 >= 0 && anchoredId2 < 256) id_for_overlay = anchoredId2 & 0xFF;
                            }
                        }

                        int hw_o = 0x10; int hh_o = 0x10; int hxoff_o = 0; int hyoff_o = 0;
                        if (id_for_overlay >= 0 && id_for_overlay < sprite_widths.Length) hw_o = sprite_widths[id_for_overlay];
                        if (id_for_overlay >= 0 && id_for_overlay < sprite_heights.Length) hh_o = sprite_heights[id_for_overlay];
                        // Skip hitbox overlay for DECO/COLR/OUTL/SPBH sentinel sprites
                        if (hh_o >= 0xFC) continue;
                        if (id_for_overlay >= 0 && id_for_overlay < sprite_x_offset.Length) hxoff_o = sprite_x_offset[id_for_overlay];
                        if (id_for_overlay >= 0 && id_for_overlay < sprite_y_offset.Length) hyoff_o = sprite_y_offset[id_for_overlay];

                        try
                        {
                            if (hw_o == TILE && hh_o == TILE && finalSprite is BitmapSource fbs3)
                            {
                                hw_o = Math.Max(1, fbs3.PixelWidth);
                                hh_o = Math.Max(1, fbs3.PixelHeight);
                            }
                        }
                        catch { }

                        // Convert RTB-local px/py to canvas-space for world coordinate computation
                        double hx_screen = (int)Math.Round(px - offsetX) + hxoff_o;
                        double hy_screen = (int)Math.Round(py - offsetY) + hyoff_o;

                        int pixelX_now2 = renderCameraX_fixed >> 8;
                        int pixelY_now2 = renderCameraY_fixed >> 8;
                        int worldLeft2 = (int)Math.Round(hx_screen) + pixelX_now2;
                        int worldTop2 = (int)Math.Round(hy_screen) + pixelY_now2 - gridRenderShiftYPx;
                        int worldRight2 = worldLeft2 + Math.Max(1, hw_o) - 1;
                        int worldBottom2 = worldTop2 + Math.Max(1, hh_o) - 1;
                        try { hitboxWorldCache[idx] = (worldLeft2, worldTop2, worldRight2, worldBottom2, renderFrameCounter); } catch { }
                    }
                    catch { }

                    // Draw hitbox overlay if requested and this is not a color-trigger sprite
                    try
                    {
                        // Only render overlays when the main editor option is enabled and
                        // this simulator's instance toggle is set.
                        if (MainWindow.Option_ShowSimulatorSpriteHitboxes && ShowSpriteHitboxes && !IsColorTriggerSprite(s) && !decorationSpriteIds.Contains(s))
                        {
                            System.Windows.Shapes.Rectangle hrect;
                            if (hitboxesInUse < hitboxPool.Count)
                            {
                                hrect = hitboxPool[hitboxesInUse];
                                hrect.Visibility = Visibility.Visible;
                            }
                            else
                            {
                                hrect = new System.Windows.Shapes.Rectangle();
                                hrect.Fill = new SolidColorBrush(Color.FromArgb(96, 255, 255, 0)); // translucent yellow
                                hrect.Stroke = new SolidColorBrush(Color.FromArgb(160, 255, 200, 0));
                                hrect.StrokeThickness = 1;
                                hrect.IsHitTestVisible = false;
                                hitboxPool.Add(hrect);
                                RenderCanvas.Children.Add(hrect);
                            }

                            // Determine hitbox from sprite tables; prefer anchor's sprite id for geometry when anchored
                            int id = s & 0xFF;
                            // Use the rendered `px`/`py` converted to canvas-space
                            // as the hitbox base so the overlay aligns with the visible sprite instance.
                            int hitbase_px_x = (int)Math.Round(px - offsetX);
                            int hitbase_px_y = (int)Math.Round(py - offsetY);
                            if (spriteAnchors != null && spriteAnchors.TryGetValue(idx, out var anch))
                            {
                                int anchorKey = anch.anchorTileY * mapWidth + anch.anchorTileX;
                                if (anchorKey >= 0 && anchorKey < sprites.Length)
                                {
                                    int anchoredId = sprites[anchorKey];
                                    if (anchoredId >= 0 && anchoredId < 256) id = anchoredId & 0xFF;
                                }

                                // Do not re-apply anchor pixel offsets here: `px`/`py` already include
                                // any per-position or anchor pixel offsets earlier in the renderer.
                            }

                            int hw = 0x10; int hh = 0x10; int hxoff = 0; int hyoff = 0;
                            if (id >= 0 && id < sprite_widths.Length) hw = sprite_widths[id];
                            if (id >= 0 && id < sprite_heights.Length) hh = sprite_heights[id];
                            // Skip hitbox overlay for DECO/COLR/OUTL/SPBH sentinel sprites
                            if (hh >= 0xFC) continue;
                            if (id >= 0 && id < sprite_x_offset.Length) hxoff = sprite_x_offset[id];
                            if (id >= 0 && id < sprite_y_offset.Length) hyoff = sprite_y_offset[id];

                            // If the hitbox table entry is the default TILE size but the final sprite
                            // image used for rendering is larger, prefer the image size for the overlay
                            // so the drawn rectangle reflects the actual graphic.
                            try
                            {
                                if (hw == TILE && hh == TILE && finalSprite is BitmapSource fbs2)
                                {
                                    hw = Math.Max(1, fbs2.PixelWidth);
                                    hh = Math.Max(1, fbs2.PixelHeight);
                                }
                            }
                            catch { }

                            double hx = hitbase_px_x + hxoff; // screen-space base + offsets
                            double hy = hitbase_px_y + hyoff;
                            // The generated record position already includes exporter offsets;
                            // sprite_y_offset must remain the exact NES runtime table.
                            hrect.Width = Math.Max(1, hw);
                            hrect.Height = Math.Max(1, hh);
                            System.Windows.Controls.Canvas.SetLeft(hrect, hx);
                            System.Windows.Controls.Canvas.SetTop(hrect, hy);
                            
                            hitboxesInUse++;
                        }
                    }
                    catch { }

                    
                }
            }

            // Close sprite DrawingContext and render to RTB
            spriteDc.Close();
            try
            {
                int sprRtbW = _currentCacheTilesX * TILE;
                int sprRtbH = _currentCacheTilesY * TILE;
                var spriteRtb = new RenderTargetBitmap(sprRtbW, sprRtbH, 96, 96, PixelFormats.Pbgra32);
                spriteRtb.Render(spriteDv);
                try { spriteRtb.Freeze(); } catch { }
                if (spriteLayerImage != null)
                {
                    spriteLayerImage.Source = spriteRtb;
                    spriteLayerImage.Width = sprRtbW;
                    spriteLayerImage.Height = sprRtbH;
                    System.Windows.Controls.Canvas.SetLeft(spriteLayerImage, -offsetX);
                    System.Windows.Controls.Canvas.SetTop(spriteLayerImage, -offsetY);
                }
            }
            catch { }

            // Hide all old pooled sprite images (no longer used — sprites rendered via RTB)
            for (int i = 0; i < spritePool.Count; i++) spritePool[i].Visibility = Visibility.Collapsed;

            // Hide remaining hitboxes
            for (int i = hitboxesInUse; i < hitboxPool.Count; i++) hitboxPool[i].Visibility = Visibility.Collapsed;
            // reset hitbox counter for next frame
            hitboxesInUse = 0;

            // Position the player visual based on the snapshotted state so
            // tiles and player are always rendered from the same physics frame.
            try
            {
                int playerPixelX, playerPixelY;
                int worldX, worldY;
                {
                    playerPixelX = (snapPlayerX >> 8) - (renderCameraX_fixed >> 8);
                    playerPixelY = (snapPlayerY >> 8) - (renderCameraY_fixed >> 8) + gridRenderShiftYPx;
                    
                    // Record center of player for path (use actual physics position)
                    worldX = (snapPlayerX >> 8) + (playerVisualWidth / 2);
                    
                    // For path, use center of collision hitbox
                    // Mini mode: 8x7 hitbox, positioned at Y+9 (normal) or Y+0 (inverted) in 16x16 space
                    // Normal mode: 15x15 hitbox at Y+0
                    if (snapMiniMode)
                    {
                        if (snapGravFlipped)
                        {
                            // Inverted: hitbox at Y+0, center at Y+3.5 → round to Y+4
                            worldY = (snapPlayerY >> 8) + 4;
                        }
                        else
                        {
                            // Normal: hitbox at Y+9, center at Y+9+3.5 → round to Y+13
                            worldY = (snapPlayerY >> 8) + 13;
                        }
                    }
                    else
                    {
                        // Center of 15x15 normal hitbox (at Y+0)
                        worldY = (snapPlayerY >> 8) + 8;
                    }
                }

                // For mini mode, adjust visual position based on gravity
                // Mini sprites are 8x8 pixels, normal sprites are 16x16 pixels
                // Normal gravity: shift down to align the 8x8 sprite with the collision hitbox.
                // Collision offset = (16 - 7) >> 1 = 4 for all mini modes; using the same
                // value here keeps the sprite bottom at floor + 1 (matching the normal cube).
                // The NES itself draws the sprite at +8 (bottom of the OAM tile), but the
                // NES BG layer hides overlapping pixels — the WPF renderer doesn't have BG
                // priority, so using the collision offset avoids the sprite sinking into ground.
                if (snapMiniMode)
                {
                    playerPixelY += 4; // collision offset (GetMiniCenterOffsetY) — gravity-independent
                }

                // Path recording moved to SimulateNumericStep for better performance (60Hz instead of 144Hz+)

                // Hide player completely in cam mode or when player is invisible
                if (camModeActive || playerInvis)
                {
                    if (playerImage != null) playerImage.Visibility = Visibility.Collapsed;
                    if (playerRect != null) playerRect.Visibility = Visibility.Collapsed;
                }
                else if (playerImage != null && playerImage.Source != null)
                {
                    System.Windows.Controls.Canvas.SetLeft(playerImage, playerPixelX);
                    System.Windows.Controls.Canvas.SetTop(playerImage, playerPixelY);
                    playerImage.Visibility = Visibility.Visible;
                    if (playerRect != null) playerRect.Visibility = Visibility.Collapsed;
                }
                else if (playerRect != null)
                {
                    System.Windows.Controls.Canvas.SetLeft(playerRect, playerPixelX);
                    System.Windows.Controls.Canvas.SetTop(playerRect, playerPixelY);
                    playerRect.Visibility = Visibility.Visible;
                }

                // Render trail ghosts when forcedTrails is active
                // NES trails=2: 3 ghost copies, each offset backwards by vel_x*2 pixels,
                // Y from playerOldPosY sliding window, only on even sim ticks (flicker effect)
                try
                {
                    bool showTrails = forcedTrails > 0 && !camModeActive && !playerInvis && (simTickCount & 1) == 0;
                    for (int tg = 0; tg < 3; tg++)
                    {
                        if (trailGhosts[tg] == null) continue;
                        if (!showTrails)
                        {
                            trailGhosts[tg]!.Visibility = Visibility.Collapsed;
                            continue;
                        }
                        // Copy current player sprite to ghost (including gravity flip)
                        if (playerImage != null && playerImage.Source != null)
                        {
                            trailGhosts[tg]!.Source = playerImage.Source;
                            trailGhosts[tg]!.Width = playerImage.Width;
                            trailGhosts[tg]!.Height = playerImage.Height;
                            trailGhosts[tg]!.RenderTransformOrigin = playerImage.RenderTransformOrigin;
                            trailGhosts[tg]!.RenderTransform = playerImage.RenderTransform;
                        }
                        // Position: offset backwards by vel_x*2 per ghost index
                        int velXPx = playerVelX_fixed >> 8;
                        int ghostX = playerPixelX - (velXPx * 2 * (tg + 1));
                        // Y from old position history (index 0=newest; use tg*2 for spacing)
                        int histIdx = Math.Min((tg + 1) * 2, playerOldPosY.Length - 1);
                        int ghostY = (playerOldPosY[histIdx] >> 8) - (renderCameraY_fixed >> 8) + gridRenderShiftYPx;
                        if (snapMiniMode) ghostY += 4;
                        System.Windows.Controls.Canvas.SetLeft(trailGhosts[tg]!, ghostX);
                        System.Windows.Controls.Canvas.SetTop(trailGhosts[tg]!, ghostY);
                        trailGhosts[tg]!.Opacity = 0.4 - (tg * 0.1); // fading opacity: 0.4, 0.3, 0.2
                        trailGhosts[tg]!.Visibility = Visibility.Visible;
                    }
                }
                catch { }
            }
            catch { }

            // Render player 2 if in dual mode
            try
            {
                if (!dual || camModeActive)
                {
                    // Hide player 2 if not in dual mode or in cam mode
                    if (player2Image != null) player2Image.Visibility = Visibility.Collapsed;
                    if (player2Rect != null) player2Rect.Visibility = Visibility.Collapsed;
                }
                else
                {
                    // Calculate player 2's screen position
                    int player2PixelX = (player_x_fixed[1] >> 8) - (renderCameraX_fixed >> 8);
                    int player2PixelY = (player_y_fixed[1] >> 8) - (renderCameraY_fixed >> 8);

                    // Apply mini mode visual adjustments for player 2
                    if (player_mini[1])  // Mini — gravity-independent offset
                    {
                        player2PixelY += 4;
                    }

                    // Render player 2 using image or rectangle
                    if (player2Image != null && player2Image.Source != null)
                    {
                        System.Windows.Controls.Canvas.SetLeft(player2Image, player2PixelX);
                        System.Windows.Controls.Canvas.SetTop(player2Image, player2PixelY);
                        player2Image.Visibility = Visibility.Visible;
                        if (player2Rect != null) player2Rect.Visibility = Visibility.Collapsed;
                    }
                    else if (player2Rect != null)
                    {
                        System.Windows.Controls.Canvas.SetLeft(player2Rect, player2PixelX);
                        System.Windows.Controls.Canvas.SetTop(player2Rect, player2PixelY);
                        player2Rect.Visibility = Visibility.Visible;
                    }
                }
            }
            catch { }

            // Apply sub-pixel smoothing with a translate transform for X and Y
            // Stabilize X fractional translation when the player is anchored at the interaction line
            int centerOffset_fixed_local = (TILE / 2) << 8;
            int playerCenter_fixed_now_local = snapPlayerX + centerOffset_fixed_local;
            bool isAnchoredNow = interactionScreenOffset_px >= 0 && playerCenter_fixed_now_local >= INTERACTION_LINE_FIXED;

            double fracX = (renderCameraX_fixed & 0xFF) / 256.0;
            double fracY = (renderCameraY_fixed & 0xFF) / 256.0;

            if (isAnchoredNow)
            {
                // Force fractional X to zero while anchored to avoid 1-px jitter when camera follows
                fracX = 0.0;
            }

            // Reuse transform to avoid per-frame allocation
            if (RenderCanvas.RenderTransform is TranslateTransform existingTT)
            {
                existingTT.X = -fracX;
                existingTT.Y = -fracY;
            }
            else
            {
                RenderCanvas.RenderTransform = new TranslateTransform(-fracX, -fracY);
            }

            // Update Y-velocity overlay if enabled
            try
            {
                if (showYVelocityOverlay)
                {
                    if (yVelTextBlock == null)
                    {
                        yVelTextBlock = new System.Windows.Controls.TextBlock();
                        yVelTextBlock.Foreground = new SolidColorBrush(Colors.Yellow);
                        yVelTextBlock.FontWeight = FontWeights.Bold;
                        yVelTextBlock.FontSize = 14;
                        yVelTextBlock.IsHitTestVisible = false;
                        RenderCanvas.Children.Add(yVelTextBlock);
                        try { System.Windows.Controls.Canvas.SetZIndex(yVelTextBlock, 2000); } catch { }
                    }
                    try
                    {
                        // Show fixed-point Y velocity as hex (and decimal px/frame for convenience)
                        int v_fixed = snapPlayerVelY; // fixed-point (8 frac bits)
                        double vel_px = v_fixed / 256.0;
                        string hex;
                        if (v_fixed < 0) hex = "-0x" + ((-v_fixed) & 0xFFFF).ToString("X4");
                        else hex = "0x" + (v_fixed & 0xFFFF).ToString("X4");
                        yVelTextBlock.Text = $"Y vel: {hex}  ({vel_px:F2} px/frame)\nInvertedGravity: {gravityReversed}";
                        yVelTextBlock.Visibility = Visibility.Visible;
                        System.Windows.Controls.Canvas.SetLeft(yVelTextBlock, 4);
                        System.Windows.Controls.Canvas.SetTop(yVelTextBlock, 4);
                    }
                    catch { }
                }
                else
                {
                    try { if (yVelTextBlock != null) yVelTextBlock.Visibility = Visibility.Collapsed; } catch { }
                }
            }
            catch { }

            // Update debug info window if visible
            try
            {
                if (debugWindow != null && debugWindow.IsLoaded)
                {
                    // Determine speed string
                    string speedStr = speed switch
                    {
                        0 => "0.5x",
                        1 => "1x",
                        2 => "2x",
                        3 => "3x",
                        4 => "4x",
                        5 => "0.1x",
                        _ => $"{speed}x"
                    };
                    
                    debugWindow.UpdateDebugInfo(
                        currentGameMode,
                        currplayer_mini != 0,
                        snapGravFlipped,
                        snapPlayerX >> 8,
                        snapPlayerY >> 8,
                        speedStr,
                        ninjajumps[currplayer],
                        dblocked,
                        hblocked,
                        fblocked,
                        jblocked,
                        orbed[currplayer],
                        blackOrbed,
                        dashing[currplayer],
                        robotJumpTime[currplayer],
                        snapPlayerVelY,
                        orbBufferActive[currplayer],
                        nocamlockforced
                    );
                }
            }
            catch { }
        }

        // Ensure initial render is performed on the UI thread so toned images and
        // tile-layer caches are built before the window is shown. Call from owner
        // before showing the simulator to guarantee the paused snapshot reflects
        // starting tints.
        public void EnsureInitialRender()
        {
            try
            {
                if (!Dispatcher.CheckAccess())
                {
                    Dispatcher.Invoke(new Action(() => {
                        try { RenderFrame(); } catch { }
                        try { if (pendingTintChange || pendingTintChangeIsStartup) { try { ApplyPendingTints(); } catch { } try { RenderFrame(); } catch { } } } catch { }
                    }));
                }
                else
                {
                    try { RenderFrame(); } catch { }
                    try { if (pendingTintChange || pendingTintChangeIsStartup) { try { ApplyPendingTints(); } catch { } try { RenderFrame(); } catch { } } } catch { }
                }
            }
            catch { }
        }

        // Use CompositionTarget.Rendering as the main loop to maintain consistent timing. We implement
        // a simple fixed-step simulation so animation and camera advance at 60Hz even if rendering
        // intermittently lags.
        private void CompositionTarget_Rendering(object? sender, EventArgs e)
        {
            if (windowClosed) return;
            
            try
            {
                // Advance a UI-driven animation counter when the numeric sim isn't running
                // or when the sim is paused so decorative/preview animations remain active.
                try
                {
                    double now = renderStopwatch.Elapsed.TotalMilliseconds;
                    double delta = Math.Max(0.0, now - uiAnimLastMs);
                    uiAnimLastMs = now;
                    // Apply time scaling to animation accumulation so animations respect slow-motion
                    uiAnimAccumulatedMs += delta * simTimeScale;
                    // Only advance UI-driven animation counter when NOT paused
                    // Animations should not advance while simulation is paused
                    if (!paused)
                    {
                        while (uiAnimAccumulatedMs >= SIM_STEP_MS)
                        {
                            animationFrame++;
                            uiAnimAccumulatedMs -= SIM_STEP_MS;
                        }
                    }
                }
                catch { }

                // If paused, still render the current frame and show the pause overlay.
                if (paused)
                {
                    // Skip rendering player during pathfinder precompute to avoid showing
                    // speculative positions from the background beam search.
                    if (!pfSimulating)
                        RenderFrame();
                    try
                    {
                        if (levelCompleteTriggered)
                        {
                            PauseOverlay.Visibility = System.Windows.Visibility.Collapsed;
                            LevelCompleteOverlay.Visibility = System.Windows.Visibility.Visible;
                        }
                        else if (!deathTriggered)
                        {
                            PauseOverlay.Visibility = System.Windows.Visibility.Visible;
                            LevelCompleteOverlay.Visibility = System.Windows.Visibility.Collapsed;
                        }
                        else
                        {
                            PauseOverlay.Visibility = System.Windows.Visibility.Collapsed;
                            LevelCompleteOverlay.Visibility = System.Windows.Visibility.Collapsed;
                        }
                    }
                    catch { }
                    return;
                }

                // If the simulation flagged a pending tint change, apply it here on the UI thread.
                if (pendingTintChange)
                {
                    try { ApplyPendingTints(); } catch { }
                }

                // Sync debug bar controls to match current sim state each frame
                try
                {
                    _suppressDebugBarEvents = true;
                    if (GameModeComboBox != null && GameModeComboBox.SelectedIndex != currentGameMode
                        && currentGameMode >= 0 && currentGameMode < GameModeComboBox.Items.Count)
                        GameModeComboBox.SelectedIndex = currentGameMode;
                    if (SpeedComboBox != null && SpeedComboBox.SelectedIndex != speed
                        && speed >= 0 && speed < SpeedComboBox.Items.Count)
                        SpeedComboBox.SelectedIndex = speed;
                    if (MiniCheckBox != null && MiniCheckBox.IsChecked != miniMode)
                        MiniCheckBox.IsChecked = miniMode;
                    if (InvertedCheckBox != null && InvertedCheckBox.IsChecked != gravityReversed)
                        InvertedCheckBox.IsChecked = gravityReversed;
                }
                catch { }
                finally { _suppressDebugBarEvents = false; }

                // Ensure overlays are not visible while running
                try { PauseOverlay.Visibility = System.Windows.Visibility.Collapsed; } catch { }
                try { LevelCompleteOverlay.Visibility = System.Windows.Visibility.Collapsed; } catch { }
                RenderFrame();
            }
            catch { }
        }

        // Perform numeric-only simulation step on a background thread at ~60Hz.
        private void SimulateNumericStep()
        {
            // Keep previous camera center for later anchor detection
            int prevCameraCenter_fixed;
            int prevPlayerCenter_fixed;
            int attemptedPlayerX_fixed;
            int attemptedPlayerCenter_fixed;
            int speedMultiplierLocal;
            int centerOffset_fixed = (TILE / 2) << 8;

            lock (simLock)
            {
                // CRITICAL: Check if restart is in progress or window closed
                // If so, exit immediately to allow restart to acquire the lock and avoid deadlock
                if (restartInProgress || windowClosed) return;
                
                // Respect pause: do not advance numeric simulation when paused.
                if (paused) return;
                
                // Increment simulation tick counter (used for timewarp and trail timing)
                simTickCount++;
                
                // Timewarp (slowMode): skip every other physics frame, matching NES behavior
                // On skipped frames, just return — no physics, no rendering update needed
                if (slowMode && (simTickCount & 1) != 0)
                    return;
                
                AppendSimDebug($"[STEP_START] step={simTickCount} pfFrame={pfFrameIndex} playerX_fixed=0x{playerX_fixed:X4} ({playerX_fixed >> 8}px), playerY_fixed=0x{playerY_fixed:X4} ({playerY_fixed >> 8}px), playerVelY_fixed=0x{playerVelY_fixed:X4}");

                // === PATHFINDER AI INPUT INJECTION ===
                if (pathfinderEnabled)
                {
                    bool pfInput = PF_GetInput();
                    pfInputThisFrame = pfInput; // Save for dual mode P2 re-injection
                    // CRITICAL: If PF_GetInput detected a phantom double-step (same tick
                    // generation), skip the ENTIRE physics frame. Running gravity/physics
                    // twice in one tick causes position divergence from the PF path.
                    if (pfWasPhantomStep)
                    {
                        AppendSimDebug($"[PF_PHANTOM] Skipping phantom double-step (tick={pfTickGeneration}, frame={pfFrameIndex})");
                        return;
                    }
                    PF_InjectInput(pfInput);
                    if (pfInput)
                        AppendSimDebug($"[PF] Frame {pfFrameIndex - 1}: JUMP INPUT injected (keyXHeld={keyXHeld}, pressCount={keyXPressedCount}, velY=0x{playerVelY_fixed:X4}, wasZeroed={wasZeroedByCollisionLastFrame})");
                }

                prevCameraCenter_fixed = cameraX_fixed + ((NES_W * TILE / 2) << 8);
                prevPlayerCenter_fixed = playerX_fixed + centerOffset_fixed;

                // CRITICAL: Determine if at 100% speed to use pure integer math (no floating-point)
                isFullSpeed = (simTimeScale == 1.0);
                speedMultiplierLocal = tabSpeedMultiplier; // atomic read of volatile-like field
                // A fresh NES UFO start has one already-completed reset tick whose
                // X movement used the reset/default speed. The first visible
                // sprite pass can hit a speed portal at X=0 (Chromatic Expedition),
                // but that new speed owns the following tick, not this X phase.
                bool useNesUfoResetSpeed = simTickCount == 1 && !dual && currentGameMode == 3;
                int movementSpeed_fixed = useNesUfoResetSpeed
                    ? CUBE_SPEED_X1
                    : currentSpeed_fixed;
                sbyte platformerHorizontalDirection = 0;
                if (forcePlatformer)
                {
                    if (pathfinderEnabled)
                        platformerHorizontalDirection = pfHorizontalDirectionThisFrame;
                    else if (IsPlatformerDirectionDownAsync(right: true))
                        platformerHorizontalDirection = 1;
                    else if (IsPlatformerDirectionDownAsync(right: false))
                        platformerHorizontalDirection = -1;
                }
                int platformerMovementStep_fixed;
                // Use exact integer math when at 100% speed to ensure determinism
                if (isFullSpeed)
                    platformerMovementStep_fixed = movementSpeed_fixed * speedMultiplierLocal;
                else
                    platformerMovementStep_fixed = (int)Math.Round(
                        (movementSpeed_fixed * speedMultiplierLocal) * simTimeScale);
                attemptedPlayerX_fixed = forcePlatformer
                    ? playerX_fixed
                    : playerX_fixed + platformerMovementStep_fixed;
                attemptedPlayerCenter_fixed = attemptedPlayerX_fixed + centerOffset_fixed;

                // -- Dash end check (before sprite_collide, matching NES state_game.h line 372-374) --
                if (dashing[currplayer] != 0)
                {
                    if (!(IsXDownAsync() || keyXHeld))
                    {
                        velocityY = 0;
                        playerVelY_fixed = 0;
                        dashing[currplayer] = 0;
                    }
                }

                // NES runs decrement_was_on_slope before sprite_collide, while the
                // old mode/gravity are still active. A same-frame portal must not
                // reinterpret a ship slope exit as a cube/ball slope exit.
                UpdateSlopeCounters(applyPosition: false);

                // -- Orbed clear (before sprite_collide, matching NES state_game.h line 557-559) --
                if (orbed[currplayer])
                {
                    if (!(IsXDownAsync() || keyXHeld))
                        orbed[currplayer] = false;
                }

                // === SPRITE INTERACTIONS (BEFORE MOVEMENT) ===
                // Check sprite interactions with the player's CURRENT position before moving
                // This ensures portals/pads/orbs are detected before the player moves past them
                // Capture entry mini state for SPEED_P1 — PF's ProcessSprites computes
                // playerRight once at entry using the ENTRY mini state. If a growth portal
                // changes mini mid-loop, PF still uses the old hitbox size for speed portal
                // overlap. SIM must match by snapshotting mini here.
                bool entryMiniMode_sp = miniMode;
                int entryPlayerY_fixed_sp = playerY_fixed; // Snapshot Y for speed portal overlap (PF caches Y at ProcessSprites entry)
                if (physicsEnabled)
                {
                    try
                    {
                        // Gameplay begins with a ring prepared by the preceding
                        // check_spr_objects. Prime it once at level start; normal
                        // frames refresh it after P1 movement/scroll below.
                        if (!simulatorNesSlotsPrimed)
                        {
                            UpdateSimulatorNesSlots();
                            simulatorNesSlotsPrimed = true;
                        }

                        // Reset orb/pad activation flag for this frame (will be set if collision occurs)
                        orbhitonthisframe[currplayer] = false;
                        BeginSimulatorNesSpritePass();
                        try
                        {
                            // NES sprite_collide dispatches each loaded hardware
                            // slot immediately. Every gameplay sprite category must
                            // therefore run inside this one slot 0 -> 15 walk.
                            for (int nesSlot = 0; nesSlot < simulatorNesSlots.Length; nesSlot++)
                            {
                                SelectSimulatorNesSpriteSlot(nesSlot);
                                if (!simulatorNesDispatchActive || !PrepareSimulatorNesSpriteSlot())
                                {
                                    if (levelCompleteTriggered)
                                        break;
                                    continue;
                                }

                                CheckDualPortal();
                                CheckSinglePortal();
                                CheckGameModePortals();
                                CheckBluePadCollision();
                                CheckGravityPortals();
                                CheckTeleportPortals();
                                CheckGravityModPortals();
                                CheckGravityModTriggers(prevPlayerCenter_fixed, attemptedPlayerCenter_fixed);
                                CheckMiniGrowthPortals();
                                CheckCamLockPortals(prevPlayerCenter_fixed, attemptedPlayerCenter_fixed);
                                CheckMiscTriggers();
                                CheckPadCollision();
                                CheckSpiderOrbPadCollision();
                                CheckSkullOrbCollisionNesOrder();
                                CheckRegularOrbCollisionNesOrder();
                                CheckDashOrbCollision();
                                CheckAlphabetBlocks();
                                CheckCoinCollision();

                                // === SPEED PORTAL CHECK (at OLD X, before X advance) ===
                                // NES detects speed portals during sprite_collide at OLD X.
                                {
                            int hitboxW_sp1 = entryMiniMode_sp ? 8 : 15;
                            int hitboxH_sp1 = entryMiniMode_sp ? 7 : 15;
                            // Use EXCLUSIVE player bounds (matching PF ProcessSprites exactly)
                            // PF: nesX = currentX_px + 1; playerRight = nesX + hbW (exclusive)
                            // PF overlap: !(playerRight < sp.HitLeft || sp.HitRight < nesX)
                            int scrollX_sp1 = SimulatorScrollX_px();
                            int nesX_sp1 = (playerX_fixed >> 8) + 1 - scrollX_sp1;
                            // Use entry mini state for Y offset (PF computes hbOffY at ProcessSprites entry)
                            int miniOffY_sp1 = entryMiniMode_sp ? ((0x10 - 7) >> 1) : 0;
                            int playerTop_sp1 = (entryPlayerY_fixed_sp >> 8) +
                                miniOffY_sp1 - (cameraY_fixed >> 8);
                            for (int slot_sp1 = 0; slot_sp1 < simulatorNesSlots.Length; slot_sp1++)
                            {
                                if (slot_sp1 != simulatorNesDispatchSlot) continue;
                                int idx = simulatorNesSlots[slot_sp1];
                                if (idx < 0) continue;
                                int sid = simulatorNesSpriteIds[idx];
                                if (sid < 0) continue;
                                if (!speedPortalMap.ContainsKey(sid)) continue;

                                // Compute sprite hitbox using NES table dimensions only
                                // (matching PF's SpriteEntry pre-computation, no image expansion)
                                int sid8_sp1 = sid & 0xFF;
                                int id_for_geom_sp1 = sid8_sp1;
                                int hw_sp1 = (id_for_geom_sp1 >= 0 && id_for_geom_sp1 < sprite_widths.Length) ? sprite_widths[id_for_geom_sp1] : TILE;
                                int hh_sp1 = (id_for_geom_sp1 >= 0 && id_for_geom_sp1 < sprite_heights.Length) ? sprite_heights[id_for_geom_sp1] : TILE;
                                if (hh_sp1 >= 0xFC) continue; // skip DECO/COLR/OUTL/SPBH sentinels
                                int hxoff_sp1 = (id_for_geom_sp1 >= 0 && id_for_geom_sp1 < sprite_x_offset.Length) ? sprite_x_offset[id_for_geom_sp1] : 0;
                                int hyoff_sp1 = (id_for_geom_sp1 >= 0 && id_for_geom_sp1 < SharedPhysics.sprite_y_offset.Length) ? SharedPhysics.sprite_y_offset[id_for_geom_sp1] : 0;
                                int sLeft_sp1 = SimulatorNesSaturatingOffset(
                                    simulatorNesSlotRealX[slot_sp1], hxoff_sp1);
                                int sTop_sp1 = SimulatorNesSaturatingOffset(
                                    simulatorNesSlotRealY[slot_sp1], hyoff_sp1);
                                bool xOverlap_sp1 = SimulatorNesAxisOverlaps(
                                    nesX_sp1, hitboxW_sp1, sLeft_sp1, hw_sp1);
                                bool yOverlap_sp1 = SimulatorNesAxisOverlaps(
                                    playerTop_sp1, hitboxH_sp1, sTop_sp1, hh_sp1);
                                if (xOverlap_sp1 && yOverlap_sp1)
                                {
                                    int spd = speedPortalMap[sid];
                                    currentSpeed_fixed = spd;
                                    if (spd == CUBE_SPEED_X05) speed = 0;
                                    else if (spd == CUBE_SPEED_X1) speed = 1;
                                    else if (spd == CUBE_SPEED_X2) speed = 2;
                                    else if (spd == CUBE_SPEED_X3) speed = 3;
                                    else if (spd == CUBE_SPEED_X4) speed = 4;
                                    // NES `spcl_spd_*` does NOT call
                                    // `idx8_inc(activesprites_activated, index)`,
                                    // so the speed portal re-fires every frame
                                    // the player overlaps it. Wave physics fix
                                    // depends on this re-fire.
                                    AppendSimDebug($"[SPEED_P1] slot={slot_sp1} sid=0x{sid:X2} VelX -> 0x{spd:X4}");
                                    // Recompute X advance with the new speed. A
                                    // fresh UFO start retains the reset-speed X
                                    // phase for this one tick; the portal speed is
                                    // already stored for the next tick.
                                    int portalMovementSpeed_fixed = useNesUfoResetSpeed
                                        ? CUBE_SPEED_X1
                                        : currentSpeed_fixed;
                                    if (isFullSpeed)
                                        platformerMovementStep_fixed = portalMovementSpeed_fixed * speedMultiplierLocal;
                                    else
                                        platformerMovementStep_fixed = (int)Math.Round(
                                            (portalMovementSpeed_fixed * speedMultiplierLocal) * simTimeScale);
                                    attemptedPlayerX_fixed = forcePlatformer
                                        ? playerX_fixed
                                        : playerX_fixed + platformerMovementStep_fixed;
                                    attemptedPlayerCenter_fixed = attemptedPlayerX_fixed + centerOffset_fixed;
                                }
                            }
                        }
                            }
                        }
                        finally
                        {
                            EndSimulatorNesSpritePass();
                        }
                        AppendSimDebug($"[GRAV_PRE_MOVEMENT] currplayer_gravity={currplayer_gravity:X2} gravityFlipped={gravityFlipped} gravityReversed={gravityReversed}");
                    }
                    catch { }
                }

                // Move the player forward in world coordinates AFTER sprite interactions
                int preAdvancePlayerX_fixed = playerX_fixed;  // Save OLD X for forward collision (NES checks at OLD X)
                playerX_fixed = attemptedPlayerX_fixed;
                
                // NOTE: Ground-support-at-new-X check has been removed to match PF
                // and NES behavior.  The NES has no such look-ahead — ground loss is
                // detected naturally by gravity pulling the player down and CubeEject
                // finding no floor.  The old check ran at NEW X before physics reverted
                // to OLD X, which could prematurely clear onGround/wasZeroed and cause
                // 1-frame divergences at ledge edges.
                if (onGround)
                {
                    try
                    {
                        bool stillSupported = false;
                        if (gravityReversed)
                        {
                            stillSupported = IsTouchingCeiling();
                        }
                        else
                        {
                            const int HITBOX_W_LOCAL = 15;
                            int playerCenter_px = (playerX_fixed >> 8) + (TILE / 2);
                            int playerLeft_px = playerCenter_px - (HITBOX_W_LOCAL / 2);
                            int playerRight_px = playerLeft_px + (HITBOX_W_LOCAL - 1);
                            int probeCenter_px = playerLeft_px + (HITBOX_W_LOCAL / 2);
                            int[] probeXs = new int[] { playerLeft_px, probeCenter_px, playerRight_px };
                            int hitboxH_gs = miniMode ? 7 : 15;
                            int hitboxOffY_gs = 0;
                            if (miniMode)
                                hitboxOffY_gs = (currentGameMode == 2) ? 4 : 9;
                            int footWorldY_px = (playerY_fixed >> 8) + hitboxOffY_gs + hitboxH_gs;
                            int tileBelowY_world = footWorldY_px / TILE;
                            int groundRowsToReserve_local = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                            int tileIndexY = tileBelowY_world + groundRowsToReserve_local;

                            if (tileIndexY >= mapHeight)
                            {
                                stillSupported = true;
                            }
                            else if (tileIndexY >= 0)
                            {
                                int localY = ((footWorldY_px % TILE) + TILE) % TILE;
                                foreach (int px in probeXs)
                                {
                                    int tx = px / TILE;
                                    if (tx < 0 || tx >= mapWidth) continue;
                                    int tid = tiles[tileIndexY * mapWidth + tx];
                                    int useTidForAnim = MapAnimatedTileIndex(tid);
                                    int collisionTid = useTidForAnim;
                                    if (useTidForAnim >= 1000)
                                    {
                                        if (useTidForAnim >= 1000 && useTidForAnim <= 1007)
                                            collisionTid = 0x08 + ((useTidForAnim - 1000) % 4);
                                        else if (useTidForAnim >= 1010 && useTidForAnim <= 1015)
                                        {
                                            int group = (useTidForAnim - 1010) % 3;
                                            collisionTid = (group == 0) ? 0x04 : (group == 1) ? 0x7D : 0x7F;
                                        }
                                        else if (useTidForAnim >= 1020 && useTidForAnim <= 1037)
                                            collisionTid = 0x74 + ((useTidForAnim - 1020) % 9);
                                        else
                                            collisionTid = tid;
                                    }
                                    var col = MetatileCollisionTable.GetCollision((byte)collisionTid);
                                    int tileStartX = tx * TILE;
                                    int localX = Math.Max(0, Math.Min(TILE - 1, px - tileStartX));
                                    // Half-slabs (COL_TOP) only occupy localY 0..7 — without
                                    // TileOccupiesPixel the player would falsely retain support
                                    // after walking past their solid region.
                                    if (ProvidesFloorAtColumnStatic(col, localX, out int _) &&
                                        SharedPhysics.TileOccupiesPixel(col, localX, localY))
                                    { stillSupported = true; break; }
                                }
                            }
                        }

                        if (!stillSupported)
                        {
                            onGround = false;
                            wasZeroedByCollisionLastFrame = false;
                        }
                    }
                    catch
                    {
                        onGround = false;
                        wasZeroedByCollisionLastFrame = false;
                    }
                }
                
                // === COLLISION/GROUNDING DISABLED ===
                /*
                // When player moves horizontally while considered grounded in the numeric
                // simulation, clear the stabilization counter and verify support so
                // walking off surfaces resumes gravity on the same frame.
                if (onGround)
                {
                    groundStabilizeCounter = 0;
                    try
                    {
                        bool stillSupported = false;
                        if (gravityReversed)
                        {
                            stillSupported = IsTouchingCeiling();
                        }
                        else
                        {
                            const int HITBOX_W_LOCAL = 15;
                            int playerCenter_px = (playerX_fixed >> 8) + (playerVisualWidth / 2);
                            int playerLeft_px = playerCenter_px - (HITBOX_W_LOCAL / 2);
                            int playerRight_px = playerLeft_px + (HITBOX_W_LOCAL - 1);
                            int footWorldY_px = (playerY_fixed >> 8) + playerVisualHeight - 1;
                            int tileBelowY_world = footWorldY_px / TILE;
                            int groundRowsToReserve_local = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                            int tileIndexY = tileBelowY_world + groundRowsToReserve_local;
                            if (tileIndexY >= 0 && tileIndexY < mapHeight)
                            {
                                for (int tx = playerLeft_px / TILE; tx <= playerRight_px / TILE; tx++)
                                {
                                    if (tx < 0 || tx >= mapWidth) continue;
                                    int tid = tiles[tileIndexY * mapWidth + tx];
                                    int useTidForAnim = MapAnimatedTileIndex(tid);
                                    int collisionTid = useTidForAnim;
                                    if (useTidForAnim >= 1000)
                                    {
                                        if (useTidForAnim >= 1000 && useTidForAnim <= 1007)
                                            collisionTid = 0x08 + ((useTidForAnim - 1000) % 4);
                                        else if (useTidForAnim >= 1010 && useTidForAnim <= 1015)
                                        {
                                            int group = (useTidForAnim - 1010) % 3;
                                            collisionTid = (group == 0) ? 0x04 : (group == 1) ? 0x7D : 0x7F;
                                        }
                                        else if (useTidForAnim >= 1020 && useTidForAnim <= 1037)
                                            collisionTid = 0x74 + ((useTidForAnim - 1020) % 9);
                                        else
                                            collisionTid = tid;
                                    }
                                    var col = MetatileCollisionTable.GetCollision((byte)collisionTid);
                                    int tileStartX = tx * TILE;
                                    int localX = Math.Max(0, Math.Min(TILE - 1, playerCenter_px - tileStartX));
                                    if (ProvidesFloorAtColumnStatic(col, localX, out int _)) { stillSupported = true; break; }
                                }
                            }
                        }

                        if (!stillSupported) onGround = false;
                    }
                    catch { onGround = false; }
                }
                */
                // === END COLLISION/GROUNDING DISABLED ===

                    // Atomically consume any jump-buffer frames at the start of the physics step
                    // so landing code can check a stable value. We clear the buffer here and
                    // test the captured value when resolving landing to avoid races with UI thread.
                            int jumpBuffered_local = Interlocked.Exchange(ref jumpBufferCounter, 0);
                            // Read keyX pressed count without clearing so we can respect held/edge presses
                            int pendingKeyX_local = Interlocked.CompareExchange(ref keyXPressedCount, 0, 0);

                // Interaction crossing detection
                bool crossedInteraction = prevPlayerCenter_fixed < INTERACTION_LINE_FIXED && attemptedPlayerCenter_fixed >= INTERACTION_LINE_FIXED;
                if (!forcePlatformer && crossedInteraction)
                {
                    interactionScreenOffset_px = (INTERACTION_LINE_FIXED >> 8) - (cameraX_fixed >> 8);
                }

                if (!forcePlatformer)
                {
                    int playerCenter_fixed_now = playerX_fixed + centerOffset_fixed;
                    if (playerCenter_fixed_now >= INTERACTION_LINE_FIXED)
                    {
                        if (interactionScreenOffset_px >= 0)
                        {
                            cameraX_fixed = playerX_fixed - (interactionScreenOffset_px << 8);
                        }
                        else
                        {
                            cameraX_fixed += attemptedPlayerCenter_fixed - INTERACTION_LINE_FIXED;
                        }

                        int maxCamera_fixed = Math.Max(0, (mapWidth - NES_W) * TILE) << 8;
                        if (cameraX_fixed < 0) cameraX_fixed = 0;
                        if (cameraX_fixed > maxCamera_fixed) cameraX_fixed = maxCamera_fixed;
                    }
                    else
                    {
                        interactionScreenOffset_px = -1;
                    }
                }
                else
                    interactionScreenOffset_px = -1;

                // Advance animation frame (handled by fixed-step simulation loop)

                // Vertical player movement & camera-follow behavior (numeric sim path)
                const int vStep_fixed_local = 512; // 2 px/frame
                int maxCameraY_fixed_local = Math.Max(0, (mapHeight - NES_H) * TILE) << 8;
                int maxPlayerY_fixed_local = Math.Max(0, (mapHeight * TILE - playerVisualHeight)) << 8;

                if (upHeld)
                {
                    // Use player's screen center Y for scrolling decisions to match camera panning behavior
                    int playerCenterScreenY_local = (playerY_fixed >> 8) + (playerVisualHeight / 2) - (cameraY_fixed >> 8);
                    int topThreshold_local = 5 * TILE; // 5 tiles from top

                    if (!jumpedOnce && camModeActive)
                    {
                        // Before first jump: Up acts as camera-only
                        if (cameraY_fixed > 0)
                        {
                            cameraY_fixed -= vStep_fixed_local;
                            if (cameraY_fixed < 0) cameraY_fixed = 0;
                        }
                    }
                    else
                    {
                        if (!physicsEnabled && camModeActive)
                        {
                            // Move player up
                            playerY_fixed -= vStep_fixed_local;
                            if (playerY_fixed < 0) playerY_fixed = 0;

                            // Recompute center after moving the player
                            playerCenterScreenY_local = (playerY_fixed >> 8) + (playerVisualHeight / 2) - (cameraY_fixed >> 8);
                            // If player's center is at or above the threshold, scroll camera up to follow
                            if (playerCenterScreenY_local <= topThreshold_local)
                            {
                                int need = topThreshold_local - playerCenterScreenY_local;
                                int camMove = Math.Min(need, (cameraY_fixed >> 8));
                                cameraY_fixed -= (camMove << 8);
                                if (cameraY_fixed < 0) cameraY_fixed = 0;
                            }
                        }
                        else
                        {
                            // Physics active: don't move player Y directly. Allow camera to scroll up if needed (only if cam mode active).
                            if (camModeActive)
                            {
                                if (playerCenterScreenY_local <= topThreshold_local)
                                {
                                    int need = topThreshold_local - playerCenterScreenY_local;
                                    int camMove = Math.Min(need, (cameraY_fixed >> 8));
                                    cameraY_fixed -= (camMove << 8);
                                    if (cameraY_fixed < 0) cameraY_fixed = 0;
                                }
                                else
                                {
                                    // Manual camera pan while physics is enabled: allow small step when holding Up
                                    if (cameraY_fixed > 0)
                                    {
                                        cameraY_fixed -= vStep_fixed_local;
                                        if (cameraY_fixed < 0) cameraY_fixed = 0;
                                    }
                                }
                            }
                        }
                    }
                }

                        

                if (downHeld)
                {
                    int bottomThresholdBottom_local = NES_H * TILE - 5 * TILE; // 5 tiles from bottom (measured from bottom edge)
                    int playerScreenY = (playerY_fixed >> 8) - (cameraY_fixed >> 8);
                    int playerScreenBottom_local = playerScreenY + playerVisualHeight;

                    if (!jumpedOnce && camModeActive)
                    {
                        // Before first jump: Down pans camera only
                        if (cameraY_fixed < maxCameraY_fixed_local)
                        {
                            cameraY_fixed += vStep_fixed_local;
                            if (cameraY_fixed > maxCameraY_fixed_local) cameraY_fixed = maxCameraY_fixed_local;
                        }
                    }
                    else
                    {
                        // If player's bottom is above the bottom threshold, allow moving player down.
                        // If player's bottom has reached or passed the threshold, scroll the camera instead.
                        if (playerScreenBottom_local < bottomThresholdBottom_local)
                        {
                            if (!physicsEnabled && camModeActive)
                            {
                                // Move player down
                                playerY_fixed += vStep_fixed_local;
                                if (playerY_fixed > maxPlayerY_fixed_local) playerY_fixed = maxPlayerY_fixed_local;
                            }
                            else
                            {
                                // Physics active: don't move player Y directly here; camera remains stationary unless thresholds
                            }
                        }
                        else
                        {
                            // Scroll camera down first until it reaches bottom (only if cam mode active)
                            if (camModeActive && cameraY_fixed < maxCameraY_fixed_local)
                            {
                                // Scroll camera down
                                cameraY_fixed += vStep_fixed_local;
                                if (cameraY_fixed > maxCameraY_fixed_local) cameraY_fixed = maxCameraY_fixed_local;
                                // Only advance player when physics is not enabled
                                if (!physicsEnabled)
                                {
                                    playerY_fixed += vStep_fixed_local;
                                    if (playerY_fixed > maxPlayerY_fixed_local) playerY_fixed = maxPlayerY_fixed_local;
                                }
                            }
                            else if (camModeActive)
                            {
                                // Manual camera pan while physics is enabled: allow small step when holding Down
                                if (cameraY_fixed < maxCameraY_fixed_local)
                                {
                                    cameraY_fixed += vStep_fixed_local;
                                    if (cameraY_fixed > maxCameraY_fixed_local) cameraY_fixed = maxCameraY_fixed_local;
                                }
                                else
                                {
                                    if (!physicsEnabled)
                                    {
                                        playerY_fixed += vStep_fixed_local;
                                        if (playerY_fixed > maxPlayerY_fixed_local) playerY_fixed = maxPlayerY_fixed_local;
                                    }
                                }
                            }
                        }
                    }
                }

                // === NES ORDER: physics/eject must run at OLD X ===
                // Save NEW X, temporarily revert to OLD X for physics dispatch + forward collision.
                // NES order: sprite_collide → movement (at OLD X) → x_movement_coll (at OLD X) → x_movement → bg_coll_death
                playerX_fixed = preAdvancePlayerX_fixed;
                bool platformerSideDeath = false;

                // Call game mode physics when enabled (disabled in cam mode for complete passthrough)
                if (physicsEnabled && !camModeActive)
                {
                    try
                    {
                        // Initialize state for all modes (sync from gravity system)
                        currplayer_mini = (byte)(miniMode ? 1 : 0);
                        currplayer_gravity = (byte)(gravityFlipped ? 0xFF : 0);
                        currplayer_table_idx = (currplayer_gravity != 0 ? 1 : 0) | (currplayer_mini != 0 ? 4 : 0);
                        if (!_levelNameLogged) { _levelNameLogged = true; AppendSimDebug($"[LEVEL] {_levelName}"); }
                        if (gravityFlipped) AppendSimDebug($"[PHYSICS] FLIPPED! Mode={currentGameMode}, gravity={currplayer_gravity:X2}, mini={currplayer_mini}, table_idx={currplayer_table_idx}, gravityFlipped={gravityFlipped}, gravityReversed={gravityReversed}");
                        else AppendSimDebug($"[PHYSICS] Mode={currentGameMode}, gravity={currplayer_gravity:X2}, mini={currplayer_mini}, table_idx={currplayer_table_idx}");
                        
                        // Sync gravity state BEFORE animation updates (animation methods use gravityFlipped)
                        gravityFlipped = (currplayer_gravity != 0);
                        
                        // All modes handle their own input internally now
                        switch (currentGameMode)
                        {
                            case 0: // Cube
                                ProcessCubePhysics_Fresh();
                                if (miniMode)
                                    UpdateCubeRotationMini();
                                else
                                    UpdateCubeRotation();
                                break;
                            case 1: // Ship
                                ShipPhysics_Fresh();
                                UpdateShipRotation();
                                break;
                            case 2: // Ball
                                BallPhysics_Fresh();
                                break;
                            case 3: // UFO
                                UfoPhysics_Fresh();
                                break;
                            case 4: // Robot (uses cube physics with hold-to-jump)
                                RobotPhysics_Fresh();
                                if (miniMode)
                                    UpdateCubeRotationMini();
                                else
                                    UpdateCubeRotation();
                                break;
                            case 5: // Spider
                                SpiderPhysics_Fresh();
                                break;
                            case 6: // Wave
                                WavePhysics_Fresh();
                                break;
                            case 7: // Swingcopter
                                BallPhysics_Fresh(); // Swingcopter uses ball physics
                                UpdateSwingcopterRotation();
                                break;
                            case 8: // Ninja (uses cube physics with triple jump)
                                NinjaPhysics_Fresh();
                                if (miniMode)
                                    UpdateCubeRotationMini();
                                else
                                    UpdateCubeRotation();
                                break;
                            case 9: // Pogo (uses ball physics with bounce mechanic)
                                BallPhysics_Fresh();
                                break;
                            case 10: // Snake (uses wave movement with gravity dash)
                                SnakePhysics_Fresh();
                                break;
                            case 11: // Football (uses cube physics with rotation animation)
                                FootballPhysics_Fresh();
                                UpdateFootballRotation();
                                break;
                        }

                        // NES x_movement reloads currplayer_vel_x only after the
                        // gamemode's Y movement has used the old saved velocity.
                        if (forcePlatformer)
                        {
                            attemptedPlayerX_fixed = ResolveSimulatorPlatformerHorizontal(
                                preAdvancePlayerX_fixed, platformerMovementStep_fixed,
                                platformerHorizontalDirection, out platformerSideDeath);
                            attemptedPlayerCenter_fixed = attemptedPlayerX_fixed + centerOffset_fixed;
                        }
                        else
                        {
                            playerVelX_fixed = currentSpeed_fixed;
                        }

                        if (platformerSideDeath && (attemptedPlayerX_fixed >> 8) > 0x20)
                        {
                            TriggerSimulatorDeferredDeath("Platformer side spike",
                                attemptedPlayerX_fixed >> 8, playerY_fixed >> 8);
                        }
                        
                        // Sync state back (gravity might have flipped)
                        gravityFlipped = (currplayer_gravity != 0);
                        AppendSimDebug($"[GRAV_POST_PHYSICS] currplayer_gravity={currplayer_gravity:X2} gravityFlipped={gravityFlipped} gravityReversed={gravityReversed} mini={miniMode}");
                        
                        // === 4-CORNER FLOOR SPIKE CHECK (NES bg_coll_floor_spikes) ===
                        // NES x_movement_coll runs bg_coll_floor_spikes() BEFORE bg_coll_R().
                        // Uses OLD X (pre-advance) and post-eject Y. Not gated by slope skip.
                        // NES: x_movement_coll is gated by invincible_counter
                        if (!MainWindow.Option_NoDeath && !deathTriggered && invincibleCounter == 0)
                        {
                            int floorSpikeX = preAdvancePlayerX_fixed >> 8;
                            int floorSpikeY = NesPlayerY_px(playerY_fixed);
                            // DIAG: log state near the problem spike area
                            if (floorSpikeX >= 6830 && floorSpikeX <= 6920 && currentGameMode == 4)
                            {
                                AppendSimDebug($"[SPIKE_DIAG] oldX={floorSpikeX} Y={floorSpikeY} velY=0x{playerVelY_fixed:X4} newX={attemptedPlayerX_fixed >> 8} mode={currentGameMode} deathTriggered={deathTriggered}");
                            }
                            if (CheckFloorSpikes(floorSpikeX, floorSpikeY, out int fsDeathX, out int fsDeathY))
                            {
                                AppendSimDebug($"[DEATH] Floor spike 4-corner death at ({fsDeathX},{fsDeathY})");
                                deathTriggered = true;
                                deathTileX = fsDeathX;
                                deathTileY = fsDeathY;
                                paused = true;
                                _ = StopMusicAsync();
                                
                                try
                                {
                                    Dispatcher.BeginInvoke(new Action(() =>
                                    {
                                        try { PauseOverlay.Visibility = System.Windows.Visibility.Collapsed; } catch { }
                                        if (this.Owner is MainWindow mw)
                                        {
                                            try { mw.PauseSimulatorPlayback(); } catch { }
                                            try { mw.AddDeathMarker(fsDeathX, fsDeathY); } catch { }
                                        }
                                    }));
                                }
                                catch { }
                            }
                        }
                        
                        // === FORWARD COLLISION CHECK (x_movement_coll in famidash) ===
                        // NES x_movement_coll() refreshes Generic.y = high_byte(currplayer_y) AFTER eject,
                        // so bg_coll_R sees post-eject Y. This lets cubes walk onto single blocks.
                        // NES bg_side_coll_common() skips the check entirely when
                        // currplayer_was_on_slope_counter or currplayer_slope_frames is non-zero
                        // (collision.h line 405-407). This prevents false wall-deaths when
                        // the player recently left a slope.
                        // NES: x_movement_coll is gated by invincible_counter
                        if (!forcePlatformer && !MainWindow.Option_NoDeath && !deathTriggered &&
                            !ShouldSkipSideCollisionForSlope() && invincibleCounter == 0)
                        {
                            bool needsForwardCheck = currentGameMode == 0 || // Cube
                                                    currentGameMode == 1 || // Ship
                                                    currentGameMode == 2 || // Ball (NES x_movement_coll runs unconditionally)
                                                    currentGameMode == 3 || // UFO (NES x_movement_coll runs unconditionally)
                                                    currentGameMode == 4 || // Robot
                                                    currentGameMode == 5 || // Spider
                                                    currentGameMode == 6 || // Wave (NES uses WAVE_WIDTH/WAVE_HEIGHT)
                                                    currentGameMode == 7 || // Swing
                                                    currentGameMode == 8 || // Ninja
                                                    currentGameMode == 9 || // Pogo
                                                    currentGameMode == 10;  // Football
                            
                            if (needsForwardCheck)
                            {
                                // OLD X, post-eject Y — matches NES x_movement_coll which refreshes
                                // Generic.y from currplayer_y (post-eject) before calling bg_coll_R
                                int playerX_px_fwd = preAdvancePlayerX_fixed >> 8;
                                int playerY_px_fwd = NesPlayerBgCollisionY_px(playerY_fixed);
                                int hitboxW_fwd, hitboxH_fwd, hitboxOffsetY_fwd;
                                if (currentGameMode == 6 || currentGameMode == 10) // Wave/snake background collision box
                                {
                                    hitboxW_fwd = 8;
                                    hitboxH_fwd = 8;
                                    hitboxOffsetY_fwd = 0;
                                }
                                else
                                {
                                    hitboxW_fwd = (currplayer_mini != 0) ? 8 : 15;
                                    hitboxH_fwd = (currplayer_mini != 0) ? 7 : 15;
                                    hitboxOffsetY_fwd = (currplayer_mini != 0 && currplayer_gravity == 0) ? 9 : 0;
                                }
                                
                                int collisionX_fwd = playerX_px_fwd;
                                int collisionY_fwd = playerY_px_fwd + hitboxOffsetY_fwd;
                                
                                // NES bg_coll_R checks at Generic.x + Generic.width (one pixel PAST the hitbox)
                                int playerRightEdge_fwd = collisionX_fwd + hitboxW_fwd;
                                // NES bg_side_coll_common: Generic.y + (mini ? (0x10-height)>>1 : 0) + (height>>1)
                                // then for mini cube/robot/ninja: += gravity ? 3 : -2
                                int playerCenterY_fwd;
                                if (currplayer_mini != 0)
                                {
                                    int miniTopOff = (0x10 - hitboxH_fwd) >> 1;  // (16-7)>>1 = 4
                                    playerCenterY_fwd = playerY_px_fwd + miniTopOff + (hitboxH_fwd >> 1);  // +4+3 = +7
                                    if (currentGameMode == 0 || currentGameMode == 4 || currentGameMode == 8)
                                        playerCenterY_fwd += (currplayer_gravity != 0) ? 3 : -2;
                                }
                                else
                                {
                                    playerCenterY_fwd = playerY_px_fwd + (hitboxH_fwd >> 1);  // +7 for normal cube
                                }
                                
                                int groundRowsToReserve_fwd = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                                
                                // Diagnostic: log forward collision probe details every frame
                                {
                                    int dbgTileX = playerRightEdge_fwd / TILE;
                                    int dbgTileY = playerCenterY_fwd / TILE;
                                    int dbgTileIdxY = dbgTileY + groundRowsToReserve_fwd;
                                    int dbgTid = -1;
                                    string dbgCol = "OOB";
                                    if (dbgTileX >= 0 && dbgTileX < mapWidth && dbgTileIdxY >= 0 && dbgTileIdxY < mapHeight)
                                    {
                                        int dbgIdx = dbgTileIdxY * mapWidth + dbgTileX;
                                        if (dbgIdx >= 0 && dbgIdx < tiles.Length)
                                        {
                                            dbgTid = tiles[dbgIdx];
                                            dbgCol = MetatileCollisionTable.GetCollision((byte)dbgTid).ToString();
                                        }
                                    }
                                    AppendSimDebug($"[FWD_CHECK] probe=({playerRightEdge_fwd},{playerCenterY_fwd}) tile=({dbgTileX},{dbgTileY}) tileIdxY={dbgTileIdxY} tid=0x{dbgTid:X2} col={dbgCol} oldX={playerX_px_fwd} newX={attemptedPlayerX_fixed >> 8}");
                                }
                                
                                bool middlePixelBlocked = CheckPixelCollision(playerRightEdge_fwd, playerCenterY_fwd, groundRowsToReserve_fwd);
                                // NES bg_coll_sides: COL_FLOOR_CEIL and COL_NO_SIDE never block side collision.
                                // CheckPixelCollision treats them as solid (correct for floor/ceiling),
                                // but they must be excluded for forward/side collision.
                                // NES bg_coll_sides/bg_coll_mini_blocks also never block on slope tiles.
                                if (middlePixelBlocked)
                                {
                                    int fwdTX = playerRightEdge_fwd / TILE;
                                    int fwdTY = playerCenterY_fwd / TILE;
                                    int fwdTIY = fwdTY + groundRowsToReserve_fwd;
                                    if (fwdTX >= 0 && fwdTX < mapWidth && fwdTIY >= 0 && fwdTIY < mapHeight)
                                    {
                                        int fwdTid = tiles[fwdTIY * mapWidth + fwdTX];
                                        var fwdCol = MetatileCollisionTable.GetCollision((byte)SharedPhysics.MapTileForCollision(fwdTid));
                                        if (fwdCol == MetatileCollision.COL_FLOOR_CEIL || fwdCol == MetatileCollision.COL_NO_SIDE ||
                                            SharedPhysics.IsSlopeTile(fwdCol))
                                            middlePixelBlocked = false;
                                    }
                                }
                                
                                // NES bg_side_coll_common calls bg_coll_spikes() at the forward
                                // probe, setting cube_data |= 1 for spike death.  The death is
                                // checked at the END of the same frame (cube_data & 1 → death,
                                // else cube_data = 0).  Eject clears cube_data BEFORE bg_coll_R,
                                // so forward-probe spike death is immediate.
                                bool middlePixelDeath = PointHitsSpikeFloor(playerRightEdge_fwd, playerCenterY_fwd, groundRowsToReserve_fwd);
                                
                                if (middlePixelBlocked || middlePixelDeath)
                                {
                                    string reason = middlePixelBlocked ? "Forward middle pixel collision" : "Forward spike death";
                                    AppendSimDebug($"[DEATH] {reason} at ({playerRightEdge_fwd},{playerCenterY_fwd}) - post-eject check at OLD X");
                                    deathTriggered = true;
                                    deathTileX = playerRightEdge_fwd;
                                    deathTileY = playerCenterY_fwd;
                                    paused = true;
                                    _ = StopMusicAsync();
                                    
                                    try
                                    {
                                        Dispatcher.BeginInvoke(new Action(() =>
                                        {
                                            try { PauseOverlay.Visibility = System.Windows.Visibility.Collapsed; } catch { }
                                            if (this.Owner is MainWindow mw)
                                            {
                                                try { mw.PauseSimulatorPlayback(); } catch { }
                                                try { mw.AddDeathMarker(playerRightEdge_fwd, playerCenterY_fwd); } catch { }
                                            }
                                        }));
                                    }
                                    catch { }
                                }
                            }
                        }
                        // === END FORWARD COLLISION CHECK ===
                        
                        // NES bg_side_coll_common slope Y nudge: when forward probe hits a
                        // slope (and not already on slope), adjust Y by ±2 pixels.
                        // Wave/snake handle slopes separately (as death), so skip them.
                        if (!forcePlatformer && invincibleCounter == 0 && !deathTriggered &&
                            !ShouldSkipSideCollisionForSlope() &&
                            currentGameMode != 6 && currentGameMode != 10)
                        {
                            int playerX_px_nudge = preAdvancePlayerX_fixed >> 8;
                            int playerY_px_nudge = NesPlayerBgCollisionY_px(playerY_fixed);
                            int hbW_nudge = (currplayer_mini != 0) ? 8 : 15;
                            int hbH_nudge = (currplayer_mini != 0) ? 7 : 15;
                            int centerY_nudge;
                            if (currplayer_mini != 0)
                            {
                                int miniOff = (0x10 - hbH_nudge) >> 1;
                                centerY_nudge = playerY_px_nudge + miniOff + (hbH_nudge >> 1);
                                if (currentGameMode == 0 || currentGameMode == 4 || currentGameMode == 8)
                                    centerY_nudge += (currplayer_gravity != 0) ? 3 : -2;
                            }
                            else
                            {
                                centerY_nudge = playerY_px_nudge + (hbH_nudge >> 1);
                            }
                            int rightEdge_nudge = playerX_px_nudge + hbW_nudge;
                            int nTX = rightEdge_nudge / TILE;
                            int nTY = centerY_nudge / TILE;
                            int groundRowsToReserve_n = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                            int nTIY = nTY + groundRowsToReserve_n;
                            if (nTX >= 0 && nTX < mapWidth && nTIY >= 0 && nTIY < mapHeight)
                            {
                                int nTid = tiles[nTIY * mapWidth + nTX];
                                var nCol = MetatileCollisionTable.GetCollision((byte)SharedPhysics.MapTileForCollision(nTid));
                                if (SharedPhysics.IsSlopeTile(nCol))
                                {
                                    // NES bg_side_coll_common dispatches to bg_coll_slope() —
                                    // wedge test (tmp4 >= tmp7).  Without this gate the nudge
                                    // false-fires whenever the probe lands in the empty half
                                    // of a ceiling slope tile (dreamer wave-portal entry).
                                    var (wedgeHit_n, _, _) = SharedPhysics.SlopeCalc(rightEdge_nudge, centerY_nudge, nCol);
                                    if (wedgeHit_n)
                                    {
                                        // NES: upside-down slopes (RU/LU) nudge +2, floor slopes (RD/LD) nudge -2
                                        bool isUpsideDown = (nCol == MetatileCollision.COL_SLOPE_RU45 ||
                                                            nCol == MetatileCollision.COL_SLOPE_LU45) ||
                                                           (nCol >= MetatileCollision.COL_SLOPE_RU22_RIGHT &&
                                                            nCol <= MetatileCollision.COL_SLOPE_LU22_LEFT) ||
                                                           (nCol >= MetatileCollision.COL_SLOPE_RU66_TOP &&
                                                            nCol <= MetatileCollision.COL_SLOPE_LU66_TOP);
                                        int fwdNudge = isUpsideDown ? 2 : -2;
                                        playerY_fixed += fwdNudge << 8;
                                    }
                                }
                            }
                        }
                        
                        // --- Wave-specific slope death checks (NES bg_coll_death + bg_coll_R) ---
                        // NES bg_coll_death calls bg_coll_slope at the center point.
                        // NES bg_coll_R → bg_side_coll_common calls bg_coll_slope at the right edge.
                        // Both kill the wave if the slope surface is reached (dblocked doesn't
                        // prevent slope death in bg_coll_death).
                        // NES x_movement: WAVE_WIDTH=8, WAVE_HEIGHT=8 for the player hitbox.
                        if (!MainWindow.Option_NoDeath && invincibleCounter == 0 && !deathTriggered && (currentGameMode == 6 || currentGameMode == 10))
                        {
                            int wPx = preAdvancePlayerX_fixed >> 8;
                            int wPy = NesPlayerY_px(playerY_fixed);
                            const int WAVE_W = 8, WAVE_H = 8;
                            int groundRowsToReserve_ws = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                            bool isMiniWave = (currplayer_mini != 0);
                            // NES bg_coll_death/bg_side_coll_common probe Y uses Generic.y + miniCenterAdj + height/2.
                            // Wave hitbox is 8x8 (Generic.height = 8); miniCenterAdj = (16-8)>>1 = 4 when mini.
                            int miniCenter = isMiniWave ? 4 : 0;
                            
                            // 1) Center-point slope (bg_coll_death → bg_coll_slope)
                            int cX = wPx + (WAVE_W >> 1) - 1;
                            int cY = wPy + miniCenter + (WAVE_H >> 1);
                            int cTileX = cX / TILE, cTileY = cY / TILE;
                            int cTileArrayY = cTileY + groundRowsToReserve_ws;
                            if (cTileX >= 0 && cTileX < mapWidth && cTileArrayY >= 0 && cTileArrayY < mapHeight)
                            {
                                byte cTileVal = (byte)SharedPhysics.MapTileForCollision(tiles[cTileArrayY * mapWidth + cTileX]);
                                var cCol = MetatileCollisionTable.GetCollision(cTileVal);
                                if (cCol >= MetatileCollision.COL_SLOPE_RD45 && cCol <= MetatileCollision.COL_SLOPE_LU66_TOP)
                                {
                                    bool skipSlope = (!isMiniWave && cCol == MetatileCollision.COL_SLOPE_LU45) ||
                                                     (isMiniWave && (cCol == MetatileCollision.COL_SLOPE_LU66_TOP || cCol == MetatileCollision.COL_SLOPE_LU66_BOT));
                                    if (!skipSlope && bg_coll_slope(cX, cY, cCol))
                                    {
                                        AppendSimDebug($"[WAVE_DEATH] center slope X={wPx} Y={wPy} tile=({cTileX},{cTileY}) col={cCol}");
                                        deathTriggered = true;
                                        paused = true;
                                        _ = StopMusicAsync();
                                        try { Dispatcher.BeginInvoke(new Action(() => { try { PauseOverlay.Visibility = System.Windows.Visibility.Collapsed; } catch { } if (this.Owner is MainWindow mw) { try { mw.PauseSimulatorPlayback(); } catch { } } })); } catch { }
                                    }
                                }
                            }
                            
                            // 2) Right-edge slope (bg_coll_R → bg_side_coll_common → bg_coll_slope)
                            //    NES bg_coll_slope() side effect on hit (regardless of gamemode/dblocked):
                            //      currplayer_slope_frames = 1; currplayer_was_on_slope_counter = 3;
                            //    Then wave/snake branch: if (!dblocked) cube_data |= 1 (death).
                            //    With dblocked, the slope counters still get set → next frame's
                            //    wave_movement zeros vY → wave gets stuck → bg_coll_death kills.
                            if (!forcePlatformer && !deathTriggered && !ShouldSkipSideCollisionForSlope())
                            {
                                int rX = wPx + WAVE_W;
                                int rY = wPy + miniCenter + (WAVE_H >> 1);
                                int rTileX = rX / TILE, rTileY = rY / TILE;
                                int rTileArrayY = rTileY + groundRowsToReserve_ws;
                                if (rTileX >= 0 && rTileX < mapWidth && rTileArrayY >= 0 && rTileArrayY < mapHeight)
                                {
                                    byte rTileVal = (byte)SharedPhysics.MapTileForCollision(tiles[rTileArrayY * mapWidth + rTileX]);
                                    var rCol = MetatileCollisionTable.GetCollision(rTileVal);
                                    if (rCol >= MetatileCollision.COL_SLOPE_RD45 && rCol <= MetatileCollision.COL_SLOPE_LU66_TOP)
                                    {
                                        bool skipSlope = (!isMiniWave && rCol == MetatileCollision.COL_SLOPE_LU45) ||
                                                         (isMiniWave && (rCol == MetatileCollision.COL_SLOPE_LU66_TOP || rCol == MetatileCollision.COL_SLOPE_LU66_BOT));
                                        if (!skipSlope && bg_coll_slope(rX, rY, rCol))
                                        {
                                            // bg_coll_slope() side effects (always on hit):
                                            currplayer_slope_frames = 1;
                                            currplayer_was_on_slope_counter = 3;
                                            if (!dblocked)
                                            {
                                                AppendSimDebug($"[WAVE_DEATH] R-edge slope X={wPx} Y={wPy} tile=({rTileX},{rTileY}) col={rCol}");
                                                deathTriggered = true;
                                                paused = true;
                                                _ = StopMusicAsync();
                                                try { Dispatcher.BeginInvoke(new Action(() => { try { PauseOverlay.Visibility = System.Windows.Visibility.Collapsed; } catch { } if (this.Owner is MainWindow mw) { try { mw.PauseSimulatorPlayback(); } catch { } } })); } catch { }
                                            }
                                            else
                                            {
                                                AppendSimDebug($"[WAVE_REDGE_SLOPE_DBLOCKED] X={wPx} Y={wPy} tile=({rTileX},{rTileY}) col={rCol} sf=1 swoc=3");
                                            }
                                        }
                                    }
                                }
                            }
                        }
                        
                        // NES order-of-operations note (collision.h + x_movement.h + state_game.h):
                        //   1) cube_movement → cube_eject (post-eject Y written to currplayer_y)
                        //   2) runthecolls → x_movement_coll: bg_coll_floor_spikes + bg_coll_R at OLD X
                        //   3) runthecolls → x_movement:
                        //        Generic.x = high_byte(currplayer_x)   ← OLD X
                        //        Generic.y = high_byte(currplayer_y)   ← post-eject Y
                        //        currplayer_x += currplayer_vel_x      ← advances X, but Generic.x is NEVER reloaded
                        //   4) runthecolls → bg_coll_death: uses Generic.x (still OLD X) for center pixel
                        //
                        // So bg_coll_death runs at OLD X (NOT NEW X). We previously advanced X
                        // first and used NEW X — that lets the player's center pixel sweep past
                        // a half-slab side that NES kills on (a 1–3 px discrepancy depending on
                        // current speed). Run the death check FIRST, then advance.
                        // SKULL_ORB sets cube_data bit $01 during sprite_collide;
                        // state_game observes it after x_movement and suppresses it
                        // only while P1's resulting X is still <= $20.
                        if ((simulatorSkullDeathPending[0] ||
                             simulatorSpiderBoundaryDeathPending[0]) &&
                            (attemptedPlayerX_fixed >> 8) > 0x20)
                        {
                            TriggerSimulatorDeferredDeath(
                                simulatorSkullDeathPending[0]
                                    ? "Skull orb activation"
                                    : "Spider scan boundary",
                                attemptedPlayerX_fixed >> 8, playerY_fixed >> 8);
                        }

                        if (!deathTriggered && !camModeActive && CheckDeathCollision(out int deathX_px, out int deathY_px, preAdvancePlayerX_fixed))
                        {
                            AppendSimDebug($"[DEATH] Death tile collision at ({deathX_px},{deathY_px}) (OLD X)");
                            deathTriggered = true;
                            deathTileX = deathX_px;
                            deathTileY = deathY_px;
                            paused = true;
                            _ = StopMusicAsync();
                            
                            try
                            {
                                Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    try { PauseOverlay.Visibility = System.Windows.Visibility.Collapsed; } catch { }
                                    if (this.Owner is MainWindow mw)
                                    {
                                        try { mw.PauseSimulatorPlayback(); } catch { }
                                        try { mw.AddDeathMarker(deathX_px, deathY_px); } catch { }
                                    }
                                }));
                            }
                            catch { }
                        }

                        // Empirical PF/ROM parity fallback: run center-point death probe at NEW X
                        // after movement has advanced X. Keep OLD-X check as primary NES-order path.
                        if (!forcePlatformer && !deathTriggered && !camModeActive &&
                            attemptedPlayerX_fixed != preAdvancePlayerX_fixed &&
                            CheckDeathCollision(out int deathX_new_px, out int deathY_new_px, attemptedPlayerX_fixed))
                        {
                            AppendSimDebug($"[DEATH] Death tile collision at ({deathX_new_px},{deathY_new_px}) (NEW X fallback)");
                            deathTriggered = true;
                            deathTileX = deathX_new_px;
                            deathTileY = deathY_new_px;
                            paused = true;
                            _ = StopMusicAsync();

                            try
                            {
                                Dispatcher.BeginInvoke(new Action(() =>
                                {
                                    try { PauseOverlay.Visibility = System.Windows.Visibility.Collapsed; } catch { }
                                    if (this.Owner is MainWindow mw)
                                    {
                                        try { mw.PauseSimulatorPlayback(); } catch { }
                                        try { mw.AddDeathMarker(deathX_new_px, deathY_new_px); } catch { }
                                    }
                                }));
                            }
                            catch { }
                        }
                        
                        // Now that bg_coll_death has run at OLD X, advance X to NEW X for
                        // the next frame's physics. NES x_movement already advanced
                        // currplayer_x before bg_coll_death ran; the SIM mirrors that final
                        // state by assigning here, after the death check.
                        playerX_fixed = attemptedPlayerX_fixed;
                        
                        // Reset gravity flip flag now that physics has processed it
                        gravityFlippedThisFrame = false;
                        
                        // Clear dblocked every frame (matches state_game.h line 636)
                        dblocked = false;

                        // NES decrements this global once after P1 runthecolls,
                        // before camera/slot refresh and before processing P2.
                        if (invincibleCounter > 0)
                            invincibleCounter--;

                        // Exact NES order:
                        // P1 sprite_collide -> movement/collisions -> scroll ->
                        // check_spr_objects -> save P1 -> P2 sprite_collide.
                        // P2 must consume the same refreshed 16-slot ring and must
                        // not run either camera scrolling or slot refresh again.
                        ApplySimulatorNesCameraScroll();
                        UpdateSimulatorNesSlots();
                        simulatorNesSlotsPrimed = true;
                        
                        // Dashing and orbed are now cleared before sprite interactions
                        // (matching NES state_game.h lines 372-374 and 557-559)
                        
                        // === SPEED_PRE_P2 DISABLED (Fix 17b) ===
                        // Speed portal detection has been moved to P1 sprite interactions
                        // (before X advance) to match NES/PF timing.  Detecting at NEW X
                        // here caused a 1-frame timing mismatch: SIM changed speed at
                        // end of frame N (after advancing), while PF detected at start
                        // of frame N+1 (before advancing).  The new [SPEED_P1] check
                        // in sprite interactions handles this correctly, and
                        // processedSpeedPortals prevents any re-detection.
                        
                        // === PLAYER 2 PROCESSING IN DUAL MODE ===
                        if (dual && !twoplayer)
                        {
                            AppendSimDebug($"[PLAYER2_START] Processing player 2: X={player_x_fixed[1]>>8} Y={player_y_fixed[1]>>8}");
                            
                            // Per-player orb tracking (playerProcessedOrbs) handles dual-mode
                            // re-activation prevention — no need to clear shared orbActivated here
                            
                            // Reset player 2's per-frame orb state to allow activation.
                            // NOTE: Do NOT clear orbBufferActive[1] or ballInputBufferCountdown[1]
                            // here — those are the ball-mode input buffer that must persist across
                            // frames so an airborne press can trigger a flip upon landing (matching
                            // the PF's P2_BallInputBuffer which persists across frames).
                            orbHoldSuppressing[1] = false;
                            orbHoldConsumedKeyStillDown[1] = false;
                            orbhitonthisframe[1] = false;
                            
                            // CRITICAL: Save player 1's current state before switching to player 2
                            player_x_fixed[0] = playerX_fixed;
                            player_y_fixed[0] = playerY_fixed;
                            player_vel_x_fixed[0] = playerVelX_fixed;
                            player_vel_y_fixed[0] = playerVelY_fixed;
                            player_mini[0] = miniMode;
                            player_gravity[0] = currplayer_gravity;
                            AppendSimDebug($"[P1_SAVE] player_gravity[0]={player_gravity[0]:X2} currplayer_gravity={currplayer_gravity:X2} gravityFlipped={gravityFlipped} gravityReversed={gravityReversed}");
                            
                            // Save player 1 per-player physics flags
                            player_wasZeroed[0] = wasZeroedByCollisionLastFrame;
                            player_onGround[0] = onGround;
                            player_groundStabilize[0] = groundStabilizeCounter;
                            
                            // Save player 1 ball flip state
                            player_ballFlipCooldown[0] = ballFlipCooldown;
                            player_ballWasGroundedBeforeFlip[0] = ballWasGroundedBeforeFlip;
                            
                            // Save player 1 rotation state
                            player_cubeRotate[0] = cubeRotate_fixed;
                            player_cubeRotateMini[0] = cubeRotateMini_fixed;
                            player_shipRotate[0] = shipRotate_fixed;
                            player_swingRotate[0] = swingcopterRotate_fixed;
                            player_footballRotate[0] = footballRotate_fixed;
                            
                            // Save player 1 slope state
                            SaveSlopeStateForPlayer(0);
                            
                            // --- Dual mode input re-injection for P2 ---
                            // Matches PF's dual input model: P1 runs first and may consume the
                            // press; P2 only sees what remains.  Without this, P1's ball flip /
                            // orb activation consumes the shared keyXPressedCount, leaving P2
                            // with pressJump=false even when the PF solution expects P2 to act.
                            // NOTE: Use player_ballFlipCooldown[0] (POST-physics value) to match
                            // the PF's p1_BallCooldownFrames semantics.  The PF snapshots
                            // BallCooldownFrames AFTER P1's StepFrame, so a flip on the current
                            // frame (cooldown goes 0→2→1) correctly shows > 0, consuming the
                            // press and preventing P2 from also flipping on the same frame.
                            if (pathfinderEnabled)
                            {
                                bool p1ConsumedPress = orbhitonthisframe[0];
                                if (!p1ConsumedPress && currentGameMode == 2 && player_ballFlipCooldown[0] > 0)
                                    p1ConsumedPress = true;

                                // Use the raw PF sequence value (before P1's hold-continuation
                                // stretches it) so P2 only sees actual True frames from the PF.
                                // P2 gets its own hold counter to bridge air-to-landing in ball mode.
                                bool p2RawInput = pfRawSequenceInput && !p1ConsumedPress;

                                Interlocked.Exchange(ref keyXPressedCount, 0);
                                Interlocked.Exchange(ref ballToggleRequested, 0);

                                if (currentGameMode == 2)
                                {
                                    // Ball mode: P2 needs its own hold counter (matching PF's per-player BallInputBuffer)
                                    if (p2RawInput)
                                    {
                                        p2BallHoldCounter = PF_BALL_HOLD_FRAMES;
                                        // Fresh press
                                        Interlocked.Exchange(ref keyXPressedCount, 1);
                                        keyXHeld = true;
                                        Interlocked.Exchange(ref ballToggleRequested, 1);
                                    }
                                    else if (p2BallHoldCounter > 0)
                                    {
                                        // Hold continuation: just hold (no press)
                                        keyXHeld = true;
                                    }
                                    else
                                    {
                                        keyXHeld = false;
                                    }
                                    if (p2BallHoldCounter > 0)
                                        p2BallHoldCounter--;
                                    // Cut hold early if P2 already flipped (matches PF_GetInput's ballFlipCooldown gate)
                                    if (player_ballFlipCooldown[1] > 0)
                                        p2BallHoldCounter = 0;
                                }
                                else
                                {
                                    // Non-ball modes: use the overall pfInputThisFrame
                                    // (hold continuation only affects ball mode)
                                    bool p2Input = pfInputThisFrame && !p1ConsumedPress;
                                    if (p2Input)
                                    {
                                        Interlocked.Exchange(ref keyXPressedCount, 1);
                                        keyXHeld = true;
                                    }
                                    else
                                    {
                                        keyXHeld = false;
                                    }
                                }
                                AppendSimDebug($"[DUAL_INPUT] pfInput={pfInputThisFrame} pfRaw={pfRawSequenceInput} p1Consumed={p1ConsumedPress} p2Hold={p2BallHoldCounter} pressCount={keyXPressedCount} held={keyXHeld}");
                            }
                            
                            // Switch to player 2
                            currplayer = 1;
                            applyPlayer2Colors = true;  // Flag that we should apply player 2 colors to icons
                            playerX_fixed = player_x_fixed[1];
                            playerY_fixed = player_y_fixed[1];
                            playerVelX_fixed = player_vel_x_fixed[1];
                            playerVelY_fixed = player_vel_y_fixed[1];
                            miniMode = player_mini[1];
                            currplayer_mini = (byte)(miniMode ? 1 : 0);
                            gravityFlipped = (player_gravity[1] != 0);
                            currplayer_gravity = player_gravity[1];
                            gravityReversed = (player_gravity[1] != 0);
                            effectiveInvertedByW = gravityReversed;
                            currplayer_table_idx = (currplayer_gravity != 0 ? 1 : 0) | (currplayer_mini != 0 ? 4 : 0);
                            
                            // Load player 2 slope state
                            LoadSlopeStateForPlayer(1);

                            // P2 also decrements its slope-exit counter before its
                            // sprite_collide pass in the NES dual-player loop.
                            UpdateSlopeCounters(applyPosition: false);
                            
                            // Load player 2 per-player physics flags
                            wasZeroedByCollisionLastFrame = player_wasZeroed[1];
                            onGround = player_onGround[1];
                            groundStabilizeCounter = player_groundStabilize[1];
                            
                            // Load player 2 ball flip state
                            ballFlipCooldown = player_ballFlipCooldown[1];
                            ballWasGroundedBeforeFlip = player_ballWasGroundedBeforeFlip[1];
                            
                            // Load player 2 rotation state
                            cubeRotate_fixed = player_cubeRotate[1];
                            cubeRotateMini_fixed = player_cubeRotateMini[1];
                            shipRotate_fixed = player_shipRotate[1];
                            swingcopterRotate_fixed = player_swingRotate[1];
                            footballRotate_fixed = player_footballRotate[1];
                            
                            // === SPRITE INTERACTIONS FOR PLAYER 2 ===
                            try
                            {
                                orbhitonthisframe[currplayer] = false;
                                bool entryMiniMode_sp2 = miniMode;
                                int entryPlayerY_fixed_sp2 = playerY_fixed;
                                BeginSimulatorNesSpritePass();
                                try
                                {
                                    for (int nesSlot = 0; nesSlot < simulatorNesSlots.Length; nesSlot++)
                                    {
                                        SelectSimulatorNesSpriteSlot(nesSlot);
                                        if (!simulatorNesDispatchActive || !PrepareSimulatorNesSpriteSlot())
                                        {
                                            if (levelCompleteTriggered)
                                                break;
                                            continue;
                                        }

                                        CheckDualPortal();
                                        CheckSinglePortal();
                                        CheckGameModePortals();
                                        CheckBluePadCollision();
                                        CheckGravityPortals();
                                        CheckTeleportPortals();
                                        CheckGravityModPortals();
                                        CheckGravityModTriggers(prevPlayerCenter_fixed, attemptedPlayerCenter_fixed);
                                        CheckMiniGrowthPortals();
                                        CheckCamLockPortals(prevPlayerCenter_fixed, attemptedPlayerCenter_fixed);
                                        CheckMiscTriggers();
                                        CheckPadCollision();
                                        CheckSpiderOrbPadCollision();
                                        CheckSkullOrbCollisionNesOrder();
                                        CheckRegularOrbCollisionNesOrder();
                                        CheckDashOrbCollision();
                                        CheckAlphabetBlocks();
                                        CheckCoinCollision();

                                        // Speed portal check for this exact P2 NES slot.
                                        {
                                    int hitboxW_sp2 = entryMiniMode_sp2 ? 8 : 15;
                                    int hitboxH_sp2 = entryMiniMode_sp2 ? 7 : 15;
                                    int scrollX_sp2 = SimulatorScrollX_px();
                                    int nesX_sp2 = (playerX_fixed >> 8) + 1 - scrollX_sp2;
                                    int miniOffY_sp2 = entryMiniMode_sp2 ? ((0x10 - 7) >> 1) : 0;
                                    int playerTop_sp2 = (entryPlayerY_fixed_sp2 >> 8) +
                                        miniOffY_sp2 - (cameraY_fixed >> 8);
                                    for (int slot_sp2 = 0; slot_sp2 < simulatorNesSlots.Length; slot_sp2++)
                                    {
                                        if (slot_sp2 != simulatorNesDispatchSlot) continue;
                                        int idx = simulatorNesSlots[slot_sp2];
                                        if (idx < 0) continue;
                                        int sid = simulatorNesSpriteIds[idx];
                                        if (sid < 0) continue;
                                        if (!speedPortalMap.ContainsKey(sid)) continue;

                                        int sid8_sp2 = sid & 0xFF;
                                        int id_for_geom_sp2 = sid8_sp2;
                                        int hw_sp2 = (id_for_geom_sp2 >= 0 && id_for_geom_sp2 < sprite_widths.Length) ? sprite_widths[id_for_geom_sp2] : TILE;
                                        int hh_sp2 = (id_for_geom_sp2 >= 0 && id_for_geom_sp2 < sprite_heights.Length) ? sprite_heights[id_for_geom_sp2] : TILE;
                                        if (hh_sp2 >= 0xFC) continue;
                                        int hxoff_sp2 = (id_for_geom_sp2 >= 0 && id_for_geom_sp2 < sprite_x_offset.Length) ? sprite_x_offset[id_for_geom_sp2] : 0;
                                        int hyoff_sp2 = (id_for_geom_sp2 >= 0 && id_for_geom_sp2 < SharedPhysics.sprite_y_offset.Length) ? SharedPhysics.sprite_y_offset[id_for_geom_sp2] : 0;
                                        int sLeft_sp2 = SimulatorNesSaturatingOffset(
                                            simulatorNesSlotRealX[slot_sp2], hxoff_sp2);
                                        int sTop_sp2 = SimulatorNesSaturatingOffset(
                                            simulatorNesSlotRealY[slot_sp2], hyoff_sp2);
                                        bool xOverlap_sp2 = SimulatorNesAxisOverlaps(
                                            nesX_sp2, hitboxW_sp2, sLeft_sp2, hw_sp2);
                                        bool yOverlap_sp2 = SimulatorNesAxisOverlaps(
                                            playerTop_sp2, hitboxH_sp2, sTop_sp2, hh_sp2);
                                        if (xOverlap_sp2 && yOverlap_sp2)
                                        {
                                            int spd = speedPortalMap[sid];
                                            currentSpeed_fixed = spd;
                                            if (spd == CUBE_SPEED_X05) speed = 0;
                                            else if (spd == CUBE_SPEED_X1) speed = 1;
                                            else if (spd == CUBE_SPEED_X2) speed = 2;
                                            else if (spd == CUBE_SPEED_X3) speed = 3;
                                            else if (spd == CUBE_SPEED_X4) speed = 4;
                                            // NES `spcl_spd_*` does NOT one-shot via
                                            // idx8_inc; speed portals re-fire every
                                            // frame the player overlaps them.
                                            AppendSimDebug($"[SPEED_P2] slot={slot_sp2} sid=0x{sid:X2} VelX -> 0x{spd:X4}");
                                        }
                                    }
                                }
                                    }
                                }
                                finally
                                {
                                    EndSimulatorNesSpritePass();
                                }
                            }
                            catch { }
                            
                            // === PHYSICS FOR PLAYER 2 ===
                            try
                            {
                                // Sync gravity state BEFORE animation updates
                                gravityFlipped = (currplayer_gravity != 0);
                                
                                // Run physics for player 2 (same as player 1)
                                switch (currentGameMode)
                                {
                                    case 0: // Cube
                                        ProcessCubePhysics_Fresh();
                                        if (miniMode)
                                            UpdateCubeRotationMini();
                                        else
                                            UpdateCubeRotation();
                                        break;
                                    case 1: // Ship
                                        ShipPhysics_Fresh();
                                        UpdateShipRotation();
                                        break;
                                    case 2: // Ball
                                        BallPhysics_Fresh();
                                        break;
                                    case 3: // UFO
                                        UfoPhysics_Fresh();
                                        break;
                                    case 4: // Robot
                                        RobotPhysics_Fresh();
                                        if (miniMode)
                                            UpdateCubeRotationMini();
                                        else
                                            UpdateCubeRotation();
                                        break;
                                    case 5: // Spider
                                        SpiderPhysics_Fresh();
                                        break;
                                    case 6: // Wave
                                        WavePhysics_Fresh();
                                        break;
                                    case 7: // Swingcopter
                                        BallPhysics_Fresh();
                                        UpdateSwingcopterRotation();
                                        break;
                                    case 8: // Ninja
                                        NinjaPhysics_Fresh();
                                        if (miniMode)
                                            UpdateCubeRotationMini();
                                        else
                                            UpdateCubeRotation();
                                        break;
                                    case 9: // Pogo
                                        BallPhysics_Fresh();
                                        break;
                                    case 10: // Snake
                                        SnakePhysics_Fresh();
                                        break;
                                    case 11: // Football
                                        FootballPhysics_Fresh();
                                        UpdateFootballRotation();
                                        break;
                                }
                                
                                gravityFlipped = (currplayer_gravity != 0);
                                
                                // Clear per-frame flags for player 2
                                dblocked = false;
                                if (dashing[currplayer] != 0)
                                {
                                    if (!(IsXDownAsync() || keyXHeld))
                                    {
                                        velocityY = 0;
                                        playerVelY_fixed = 0;
                                        dashing[currplayer] = 0;
                                    }
                                }
                                if (orbed[currplayer])
                                {
                                    if (!(IsXDownAsync() || keyXHeld))
                                        orbed[currplayer] = false;
                                }
                            }
                            catch (Exception ex)
                            {
                                AppendSimDebug($"[PLAYER2] Physics error: {ex.Message}");
                            }
                            
                            // Sync player 2's X to player 1 AFTER movement/input routine (physics) completes
                            // This ensures path recording captures the synchronized X position
                            playerX_fixed = player_x_fixed[0];
                            player_x_fixed[1] = player_x_fixed[0];
                            playerVelX_fixed = player_vel_x_fixed[0];
                            player_vel_x_fixed[1] = player_vel_x_fixed[0];
                            AppendSimDebug($"[PLAYER2_X_SYNC] Synced X to player 1: {playerX_fixed>>8}");
                            
                            // === P2 DEATH CHECKS (NES: x_movement_coll + bg_coll_death run for P2) ===
                            // NES processes x_movement_coll (floor spikes + forward collision) and
                            // bg_coll_death for P2 at P1's X, P2's post-physics Y.
                            // P2's X doesn't advance independently (synced to P1), so no preAdvance distinction.
                            if ((simulatorSkullDeathPending[1] ||
                                 simulatorSpiderBoundaryDeathPending[1]) &&
                                (playerX_fixed >> 8) > 0x20)
                            {
                                TriggerSimulatorDeferredDeath(
                                    simulatorSkullDeathPending[1]
                                        ? "Skull orb activation (P2)"
                                        : "Spider scan boundary (P2)",
                                    playerX_fixed >> 8, playerY_fixed >> 8);
                            }

                            if (!MainWindow.Option_NoDeath && invincibleCounter == 0 && !deathTriggered)
                            {
                                int p2X_px = playerX_fixed >> 8;
                                int p2Y_px = NesPlayerY_px(playerY_fixed);
                                int groundRowsToReserve_p2 = (hasGroundLayer && groundTileRows > 0) ? Math.Min(3, groundTileRows) : 0;
                                
                                // 1) Floor spike check (NES bg_coll_floor_spikes)
                                if (CheckFloorSpikes(p2X_px, p2Y_px, out int p2FsDeathX, out int p2FsDeathY))
                                {
                                    AppendSimDebug($"[P2_DEATH] Floor spike at ({p2FsDeathX},{p2FsDeathY})");
                                    deathTriggered = true;
                                    deathTileX = p2FsDeathX;
                                    deathTileY = p2FsDeathY;
                                    paused = true;
                                    _ = StopMusicAsync();
                                    try { Dispatcher.BeginInvoke(new Action(() => { try { PauseOverlay.Visibility = System.Windows.Visibility.Collapsed; } catch { } if (this.Owner is MainWindow mw) { try { mw.PauseSimulatorPlayback(); } catch { } try { mw.AddDeathMarker(p2FsDeathX, p2FsDeathY); } catch { } } })); } catch { }
                                }
                                
                                // 2) Forward collision check (NES bg_coll_R → bg_side_coll_common)
                                if (!deathTriggered && !ShouldSkipSideCollisionForSlope())
                                {
                                    int hitboxW_p2 = (currentGameMode == 6 || currentGameMode == 10) ? 8 : ((currplayer_mini != 0) ? 8 : 15);
                                    int hitboxH_p2 = (currentGameMode == 6 || currentGameMode == 10) ? 8 : ((currplayer_mini != 0) ? 7 : 15);
                                    int rightEdge_p2 = p2X_px + hitboxW_p2;
                                    int centerY_p2;
                                    if (currentGameMode == 6 || currentGameMode == 10)
                                    {
                                        centerY_p2 = p2Y_px + ((currplayer_mini != 0) ? ((0x10 - hitboxH_p2) >> 1) : 0) + (hitboxH_p2 >> 1);
                                    }
                                    else if (currplayer_mini != 0)
                                    {
                                        int miniTopOff_p2 = (0x10 - hitboxH_p2) >> 1;
                                        centerY_p2 = p2Y_px + miniTopOff_p2 + (hitboxH_p2 >> 1);
                                        if (currentGameMode == 0 || currentGameMode == 4 || currentGameMode == 8)
                                            centerY_p2 += (currplayer_gravity != 0) ? 3 : -2;
                                    }
                                    else
                                    {
                                        centerY_p2 = p2Y_px + (hitboxH_p2 >> 1);
                                    }
                                    
                                    bool p2MiddleBlocked = CheckPixelCollision(rightEdge_p2, centerY_p2, groundRowsToReserve_p2);
                                    if (p2MiddleBlocked)
                                    {
                                        int fwdTX_p2 = rightEdge_p2 / TILE;
                                        int fwdTY_p2 = centerY_p2 / TILE;
                                        int fwdTIY_p2 = fwdTY_p2 + groundRowsToReserve_p2;
                                        if (fwdTX_p2 >= 0 && fwdTX_p2 < mapWidth && fwdTIY_p2 >= 0 && fwdTIY_p2 < mapHeight)
                                        {
                                            int fwdTid_p2 = tiles[fwdTIY_p2 * mapWidth + fwdTX_p2];
                                            var fwdCol_p2 = MetatileCollisionTable.GetCollision((byte)SharedPhysics.MapTileForCollision(fwdTid_p2));
                                            if (fwdCol_p2 == MetatileCollision.COL_FLOOR_CEIL || fwdCol_p2 == MetatileCollision.COL_NO_SIDE ||
                                                SharedPhysics.IsSlopeTile(fwdCol_p2))
                                                p2MiddleBlocked = false;
                                        }
                                    }
                                    bool p2MiddleDeath = PointHitsSpikeFloor(rightEdge_p2, centerY_p2, groundRowsToReserve_p2);
                                    
                                    if (p2MiddleBlocked || p2MiddleDeath)
                                    {
                                        string reason_p2 = p2MiddleBlocked ? "Forward collision" : "Forward spike death";
                                        AppendSimDebug($"[P2_DEATH] {reason_p2} at ({rightEdge_p2},{centerY_p2})");
                                        deathTriggered = true;
                                        deathTileX = rightEdge_p2;
                                        deathTileY = centerY_p2;
                                        paused = true;
                                        _ = StopMusicAsync();
                                        try { Dispatcher.BeginInvoke(new Action(() => { try { PauseOverlay.Visibility = System.Windows.Visibility.Collapsed; } catch { } if (this.Owner is MainWindow mw) { try { mw.PauseSimulatorPlayback(); } catch { } try { mw.AddDeathMarker(rightEdge_p2, centerY_p2); } catch { } } })); } catch { }
                                    }
                                    
                                    // Slope Y nudge for P2 (non-wave/snake)
                                    if (!deathTriggered && currentGameMode != 6 && currentGameMode != 10)
                                    {
                                        int nTX_p2 = rightEdge_p2 / TILE;
                                        int nTY_p2 = centerY_p2 / TILE;
                                        int nTIY_p2 = nTY_p2 + groundRowsToReserve_p2;
                                        if (nTX_p2 >= 0 && nTX_p2 < mapWidth && nTIY_p2 >= 0 && nTIY_p2 < mapHeight)
                                        {
                                            int nTid_p2 = tiles[nTIY_p2 * mapWidth + nTX_p2];
                                            var nCol_p2 = MetatileCollisionTable.GetCollision((byte)SharedPhysics.MapTileForCollision(nTid_p2));
                                            if (SharedPhysics.IsSlopeTile(nCol_p2))
                                            {
                                                // Wedge gate (NES bg_coll_slope) — see P1 fix above.
                                                var (wedgeHit_p2, _, _) = SharedPhysics.SlopeCalc(rightEdge_p2, centerY_p2, nCol_p2);
                                                if (wedgeHit_p2)
                                                {
                                                    bool isUp_p2 = (nCol_p2 == MetatileCollision.COL_SLOPE_RU45 ||
                                                                    nCol_p2 == MetatileCollision.COL_SLOPE_LU45) ||
                                                                   (nCol_p2 >= MetatileCollision.COL_SLOPE_RU22_RIGHT &&
                                                                    nCol_p2 <= MetatileCollision.COL_SLOPE_LU22_LEFT) ||
                                                                   (nCol_p2 >= MetatileCollision.COL_SLOPE_RU66_TOP &&
                                                                    nCol_p2 <= MetatileCollision.COL_SLOPE_LU66_TOP);
                                                    playerY_fixed += (isUp_p2 ? 2 : -2) << 8;
                                                }
                                            }
                                        }
                                    }
                                }
                                
                                // 3) Wave/snake slope death checks
                                if (!deathTriggered && (currentGameMode == 6 || currentGameMode == 10))
                                {
                                    int wW_p2 = 8, wH_p2 = 8;
                                    bool isMiniWave_p2 = (currplayer_mini != 0);
                                    int miniCenter_p2 = isMiniWave_p2 ? ((0x10 - wH_p2) >> 1) : 0;
                                    // Center-point slope
                                    int cX_p2 = p2X_px + (wW_p2 >> 1) - 1;
                                    int cY_p2 = p2Y_px + miniCenter_p2 + (wH_p2 >> 1);
                                    int cTX_p2 = cX_p2 / TILE, cTY_p2 = cY_p2 / TILE;
                                    int cTIY_p2 = cTY_p2 + groundRowsToReserve_p2;
                                    if (cTX_p2 >= 0 && cTX_p2 < mapWidth && cTIY_p2 >= 0 && cTIY_p2 < mapHeight)
                                    {
                                        byte cTVal_p2 = (byte)SharedPhysics.MapTileForCollision(tiles[cTIY_p2 * mapWidth + cTX_p2]);
                                        var cCol_p2 = MetatileCollisionTable.GetCollision(cTVal_p2);
                                        if (cCol_p2 >= MetatileCollision.COL_SLOPE_RD45 && cCol_p2 <= MetatileCollision.COL_SLOPE_LU66_TOP)
                                        {
                                            bool skipS_p2 = (!isMiniWave_p2 && cCol_p2 == MetatileCollision.COL_SLOPE_LU45) ||
                                                            (isMiniWave_p2 && (cCol_p2 == MetatileCollision.COL_SLOPE_LU66_TOP || cCol_p2 == MetatileCollision.COL_SLOPE_LU66_BOT));
                                            if (!skipS_p2 && bg_coll_slope(cX_p2, cY_p2, cCol_p2))
                                            {
                                                AppendSimDebug($"[P2_DEATH] Wave center slope at ({cTX_p2},{cTY_p2})");
                                                deathTriggered = true;
                                                paused = true;
                                                _ = StopMusicAsync();
                                                try { Dispatcher.BeginInvoke(new Action(() => { try { PauseOverlay.Visibility = System.Windows.Visibility.Collapsed; } catch { } if (this.Owner is MainWindow mw) { try { mw.PauseSimulatorPlayback(); } catch { } } })); } catch { }
                                            }
                                        }
                                    }
                                    // Right-edge slope
                                    if (!deathTriggered && !ShouldSkipSideCollisionForSlope())
                                    {
                                        int rX_p2 = p2X_px + wW_p2;
                                        int rY_p2 = p2Y_px + miniCenter_p2 + (wH_p2 >> 1);
                                        int rTX_p2 = rX_p2 / TILE, rTY_p2 = rY_p2 / TILE;
                                        int rTIY_p2 = rTY_p2 + groundRowsToReserve_p2;
                                        if (rTX_p2 >= 0 && rTX_p2 < mapWidth && rTIY_p2 >= 0 && rTIY_p2 < mapHeight)
                                        {
                                            byte rTVal_p2 = (byte)SharedPhysics.MapTileForCollision(tiles[rTIY_p2 * mapWidth + rTX_p2]);
                                            var rCol_p2 = MetatileCollisionTable.GetCollision(rTVal_p2);
                                            if (rCol_p2 >= MetatileCollision.COL_SLOPE_RD45 && rCol_p2 <= MetatileCollision.COL_SLOPE_LU66_TOP)
                                            {
                                                bool skipS2_p2 = (!isMiniWave_p2 && rCol_p2 == MetatileCollision.COL_SLOPE_LU45) ||
                                                                  (isMiniWave_p2 && (rCol_p2 == MetatileCollision.COL_SLOPE_LU66_TOP || rCol_p2 == MetatileCollision.COL_SLOPE_LU66_BOT));
                                                if (!skipS2_p2 && bg_coll_slope(rX_p2, rY_p2, rCol_p2))
                                                {
                                                    AppendSimDebug($"[P2_DEATH] Wave R-edge slope at ({rTX_p2},{rTY_p2})");
                                                    deathTriggered = true;
                                                    paused = true;
                                                    _ = StopMusicAsync();
                                                    try { Dispatcher.BeginInvoke(new Action(() => { try { PauseOverlay.Visibility = System.Windows.Visibility.Collapsed; } catch { } if (this.Owner is MainWindow mw) { try { mw.PauseSimulatorPlayback(); } catch { } } })); } catch { }
                                                }
                                            }
                                        }
                                    }
                                }
                                
                                // 4) Center-point death tile check (NES bg_coll_death)
                                if (!deathTriggered)
                                {
                                    if (CheckDeathCollision(out int p2DeathX, out int p2DeathY))
                                    {
                                        AppendSimDebug($"[P2_DEATH] Death tile at ({p2DeathX},{p2DeathY})");
                                        deathTriggered = true;
                                        deathTileX = p2DeathX;
                                        deathTileY = p2DeathY;
                                        paused = true;
                                        _ = StopMusicAsync();
                                        try { Dispatcher.BeginInvoke(new Action(() => { try { PauseOverlay.Visibility = System.Windows.Visibility.Collapsed; } catch { } if (this.Owner is MainWindow mw) { try { mw.PauseSimulatorPlayback(); } catch { } try { mw.AddDeathMarker(p2DeathX, p2DeathY); } catch { } } })); } catch { }
                                    }
                                }
                            }
                            
                            // Save player 2 state back to arrays
                            player_x_fixed[1] = playerX_fixed;
                            player_y_fixed[1] = playerY_fixed;
                            player_vel_x_fixed[1] = playerVelX_fixed;
                            player_vel_y_fixed[1] = playerVelY_fixed;
                            player_mini[1] = miniMode;
                            player_gravity[1] = currplayer_gravity;
                            
                            // Save player 2 per-player physics flags
                            player_wasZeroed[1] = wasZeroedByCollisionLastFrame;
                            player_onGround[1] = onGround;
                            player_groundStabilize[1] = groundStabilizeCounter;
                            
                            // Save player 2 ball flip state
                            player_ballFlipCooldown[1] = ballFlipCooldown;
                            player_ballWasGroundedBeforeFlip[1] = ballWasGroundedBeforeFlip;
                            
                            // Save player 2 rotation state
                            player_cubeRotate[1] = cubeRotate_fixed;
                            player_cubeRotateMini[1] = cubeRotateMini_fixed;
                            player_shipRotate[1] = shipRotate_fixed;
                            player_swingRotate[1] = swingcopterRotate_fixed;
                            player_footballRotate[1] = footballRotate_fixed;
                            
                            // Save player 2 slope state
                            SaveSlopeStateForPlayer(1);
                            
                            AppendSimDebug($"[PLAYER2_END] Player 2 final state: X={player_x_fixed[1]>>8} Y={player_y_fixed[1]>>8}");
                            
                            // --- Deferred single portal exit (NES match) ---
                            // Only fires when P2 hit the portal (P1 case exits immediately).
                            // P2's CheckSinglePortal already overwrote player_*[0] with
                            // P2's pre-physics Y/gravity/vel (matching NES spcl_sngl_pt).
                            if (singlePortalExitPending)
                            {
                                singlePortalExitPending = false;
                                dual = false;
                                _prevDualActiveForP2Path = false;
                                AppendSimDebug($"[SINGLE_PORTAL_EXIT] dual=false, P1 gets P2's state: Y={player_y_fixed[0]>>8}, velY={player_vel_y_fixed[0]:X4}, grav={player_gravity[0]:X2}");
                            }
                            
                            // Capture P2 state needed for sprite update before switching back to P1.
                            // We'll dispatch an async UI update using these captured values so we
                            // don't deadlock (Dispatcher.Invoke blocks and can freeze).
                            int p2_gameMode = currentGameMode;
                            bool p2_mini = miniMode;
                            int p2_velY = playerVelY_fixed;
                            bool p2_gravReversed = gravityReversed;
                            
                            // Switch back to player 1 for rendering
                            currplayer = 0;
                            applyPlayer2Colors = false;  // Reset color flag for player 1
                            playerX_fixed = player_x_fixed[0];
                            playerY_fixed = player_y_fixed[0];
                            playerVelX_fixed = player_vel_x_fixed[0];
                            playerVelY_fixed = player_vel_y_fixed[0];
                            miniMode = player_mini[0];
                            currplayer_mini = (byte)(miniMode ? 1 : 0);
                            gravityFlipped = (player_gravity[0] != 0);
                            currplayer_gravity = player_gravity[0];
                            gravityReversed = (player_gravity[0] != 0);
                            effectiveInvertedByW = gravityReversed;
                            currplayer_table_idx = (currplayer_gravity != 0 ? 1 : 0) | (currplayer_mini != 0 ? 4 : 0);
                            AppendSimDebug($"[P1_RESTORE] player_gravity[0]={player_gravity[0]:X2} currplayer_gravity={currplayer_gravity:X2} gravityFlipped={gravityFlipped} gravityReversed={gravityReversed}");
                            
                            // Load player 1 per-player physics flags
                            wasZeroedByCollisionLastFrame = player_wasZeroed[0];
                            onGround = player_onGround[0];
                            groundStabilizeCounter = player_groundStabilize[0];
                            
                            // Load player 1 ball flip state
                            ballFlipCooldown = player_ballFlipCooldown[0];
                            ballWasGroundedBeforeFlip = player_ballWasGroundedBeforeFlip[0];
                            
                            // Load player 1 rotation state
                            cubeRotate_fixed = player_cubeRotate[0];
                            cubeRotateMini_fixed = player_cubeRotateMini[0];
                            shipRotate_fixed = player_shipRotate[0];
                            swingcopterRotate_fixed = player_swingRotate[0];
                            footballRotate_fixed = player_footballRotate[0];
                            
                            // Load player 1 slope state
                            LoadSlopeStateForPlayer(0);
                            
                            // Update player2Image with P2's correct mode sprite (async, non-blocking).
                            // Fix 21: Acquire simLock so the temporary field swap doesn't race
                            // with the background sim thread reading shared physics state.
                            try
                            {
                                Dispatcher?.BeginInvoke(new Action(() =>
                                {
                                    lock (simLock)
                                    {
                                        try
                                        {
                                            // Save P1 fields that UpdatePlayerImageForMode reads
                                            int save_gameMode = currentGameMode;
                                            bool save_mini = miniMode;
                                            int save_velY = playerVelY_fixed;
                                            bool save_gravRev = gravityReversed;
                                            bool save_gravFlip = gravityFlipped;
                                            int save_cubeRot = cubeRotate_fixed;
                                            int save_cubeRotMini = cubeRotateMini_fixed;
                                            int save_shipRot = shipRotate_fixed;
                                            int save_swingRot = swingcopterRotate_fixed;
                                            int save_footballRot = footballRotate_fixed;
                                            
                                            // Temporarily set P2's captured state
                                            currentGameMode = p2_gameMode;
                                            miniMode = p2_mini;
                                            playerVelY_fixed = p2_velY;
                                            gravityReversed = p2_gravReversed;
                                            gravityFlipped = p2_gravReversed;
                                            cubeRotate_fixed = player_cubeRotate[1];
                                            cubeRotateMini_fixed = player_cubeRotateMini[1];
                                            shipRotate_fixed = player_shipRotate[1];
                                            swingcopterRotate_fixed = player_swingRotate[1];
                                            footballRotate_fixed = player_footballRotate[1];
                                            applyPlayer2Colors = true;
                                            
                                            UpdatePlayerImageForMode();
                                            
                                            // For cube/robot/ninja modes, also select the correct
                                            // rotation frame (UpdatePlayerImageForMode only loads
                                            // the base image; rotation frames are normally applied
                                            // in the render loop for P1 only).
                                            if (p2_gameMode == 0 || p2_gameMode == 4 || p2_gameMode == 8)
                                            {
                                                try
                                                {
                                                    if (!p2_mini)
                                                    {
                                                        int p2SpriteEntry = GetCubeSpriteFrame();
                                                        int p2TileIdx = p2SpriteEntry & 0x07;
                                                        bool p2HFlip = (p2SpriteEntry & 0x40) != 0;
                                                        bool p2VFlip = (p2SpriteEntry & 0x80) != 0;
                                                        string[] p2FrameNames = (p2_gameMode == 8) ? s_ninjaFrameNames : s_cubeFrameNames;
                                                        if (p2TileIdx >= p2FrameNames.Length) p2TileIdx = 0;
                                                        string p2ChosenFrame = p2FrameNames[p2TileIdx];
                                                        var p2RotImg = LoadCachedResourceImage(p2ChosenFrame);
                                                        if (p2RotImg != null && playerImage != null)
                                                        {
                                                            playerImage.Source = App.EnsureUnfrozenForRender(p2RotImg) ?? p2RotImg;
                                                            playerImage.Tag = p2ChosenFrame;
                                                            // Apply cube rotation flip
                                                            if (p2HFlip || p2VFlip)
                                                            {
                                                                playerImage.RenderTransformOrigin = new Point(0.5, 0.5);
                                                                playerImage.RenderTransform = new ScaleTransform(p2HFlip ? -1 : 1, p2VFlip ? -1 : 1);
                                                            }
                                                            else
                                                            {
                                                                playerImage.RenderTransform = Transform.Identity;
                                                            }
                                                        }
                                                    }
                                                    else
                                                    {
                                                        int p2MiniFrame = GetCubeSpriteMiniFrame();
                                                        // Get flip flags for mini P2
                                                        int p2MiniRawFrame = (cubeRotateMini_fixed >> 8) & 0xFF;
                                                        if (p2MiniRawFrame < 0 || p2MiniRawFrame >= 24) p2MiniRawFrame = 0;
                                                        int p2MiniEntry = drawcube_sprite_table[p2MiniRawFrame];
                                                        bool p2MiniHFlip = (p2MiniEntry & 0x40) != 0;
                                                        bool p2MiniVFlip = (p2MiniEntry & 0x80) != 0;
                                                        string[] p2MiniFrameNames = (p2_gameMode == 8) ? new string[]
                                                        {
                                                            "ninja_mini_00_frame_0.png", "ninja_mini_01_frame_1.png",
                                                            "ninja_mini_02_frame_2.png", "ninja_mini_03_frame_3.png",
                                                            "ninja_mini_04_frame_4.png"
                                                        } : new string[]
                                                        {
                                                            "cube_mini_00_frame_0.png", "cube_mini_01_frame_1.png",
                                                            "cube_mini_02_frame_2.png", "cube_mini_03_frame_3.png",
                                                            "cube_mini_04_frame_4.png"
                                                        };
                                                        string p2ChosenMini = p2MiniFrameNames[p2MiniFrame];
                                                        var p2MiniImg = LoadCachedResourceImage(p2ChosenMini);
                                                        if (p2MiniImg != null && playerImage != null)
                                                        {
                                                            playerImage.Source = App.EnsureUnfrozenForRender(p2MiniImg) ?? p2MiniImg;
                                                            playerImage.Tag = p2ChosenMini;
                                                            // Apply cube rotation flip for mini P2
                                                            if (p2MiniHFlip || p2MiniVFlip)
                                                            {
                                                                playerImage.RenderTransformOrigin = new Point(0.5, 0.5);
                                                                playerImage.RenderTransform = new ScaleTransform(p2MiniHFlip ? -1 : 1, p2MiniVFlip ? -1 : 1);
                                                            }
                                                            else
                                                            {
                                                                playerImage.RenderTransform = Transform.Identity;
                                                            }
                                                        }
                                                    }
                                                }
                                                catch { }
                                            }
                                            
                                            if (player2Image != null && playerImage != null && playerImage.Source != null)
                                            {
                                                player2Image.Source = playerImage.Source;
                                                player2Image.Width = playerImage.Width;
                                                player2Image.Height = playerImage.Height;
                                                player2Image.RenderTransformOrigin = playerImage.RenderTransformOrigin;
                                                player2Image.RenderTransform = playerImage.RenderTransform;
                                            }
                                            
                                            // Restore P1 fields
                                            currentGameMode = save_gameMode;
                                            miniMode = save_mini;
                                            playerVelY_fixed = save_velY;
                                            gravityReversed = save_gravRev;
                                            gravityFlipped = save_gravFlip;
                                            cubeRotate_fixed = save_cubeRot;
                                            cubeRotateMini_fixed = save_cubeRotMini;
                                            shipRotate_fixed = save_shipRot;
                                            swingcopterRotate_fixed = save_swingRot;
                                            footballRotate_fixed = save_footballRot;
                                            applyPlayer2Colors = false;
                                            
                                            // Now update playerImage with P1's correct sprite
                                            UpdatePlayerImageForMode();
                                        }
                                        catch { }
                                    }
                                }));
                            }
                            catch { }
                        }
                    }
                    catch (Exception ex)
                    {
                        AppendSimDebug($"Game mode physics error (mode {currentGameMode}): {ex.Message}");
                    }
                }

                if (false && physicsEnabled)
                {
                    try
                        {
                            // Decrement grounded-stabilization counter each numeric step
                            if (groundStabilizeCounter > 0) groundStabilizeCounter--;
                            // Per-frame ceiling collision stabilization: if the cube's head
                            // is overlapping a blocking ceiling while numeric gravity is
                            // inverted (either canonical or numeric-only), treat the ceiling
                            // as a solid support for this frame. Snap vertical velocity to
                            // zero and mark `onGround` so gravity is suppressed this frame.
                            // This is checked every numeric frame (deterministic) rather
                            // than relying on a time window to avoid jitter.
                            // try { Cube_HandleCeilingStabilization(); } catch { } // REMOVED - fresh port
                            // Read input flags atomically so numeric sim doesn't race with UI poll.
                            bool keyXHeld_local;
                            int keyXHeldStartedOnGround_local_int = 0;
                            lock (simLock)
                            {
                                keyXHeld_local = keyXHeld;
                                try { keyXHeldStartedOnGround_local_int = Interlocked.CompareExchange(ref keyXHeldStartedOnGroundInt, 0, 0); } catch { keyXHeldStartedOnGround_local_int = 0; }
                            }

                            // Detailed trace for diagnosis: record world Y, maxY, vel, and flags (use local copies)
                            // Read OS-level held state for logic where needed; avoid expensive logging here

                            // Track if a jump was applied this numeric step so we skip immediate gravity application
                            bool jumpAppliedThisStep_local = false;

                            bool effectiveOnGround_local = onGround || groundStabilizeCounter > 0 || invertedCeilingHoldCounter > 0;

                            // NOTE: Fresh physics handlers read and consume keyXPressedCount themselves
                            // (via Interlocked.CompareExchange/Exchange inside ProcessCubePhysics_Fresh, etc.)
                            // Do NOT consume keyXPressedCount here - it would eat the press before Fresh physics sees it.
                            int pendingPresses_num = Interlocked.CompareExchange(ref keyXPressedCount, 0, 0); // peek only
                            int pendingPressStartedOnGround = Interlocked.CompareExchange(ref keyXPressStartedOnGroundInt, 0, 0); // peek only
                            int pendingPresses_forLater = 0;

                            // Update orb buffer: set/clear according to strict rules
                            try
                            {
                                // Clear buffer and hold-consumption when landing
                                if (effectiveOnGround_local)
                                {
                                    orbBufferActive[currplayer] = false;
                                    ballInputBufferCountdown[currplayer] = 0;
                                    orbHoldConsumed[currplayer] = false;
                                    orbHoldConsumedKeyStillDown[currplayer] = false;
                                    orbActivationConsumedThisPress[currplayer] = false;
                                }
                                else if (jumpAppliedThisStep_local)
                                {
                                    // A jump used this frame should not also prime the orb buffer
                                    orbBufferActive[currplayer] = false;
                                    ballInputBufferCountdown[currplayer] = 0;
                                    orbHoldConsumed[currplayer] = false;
                                    orbHoldConsumedKeyStillDown[currplayer] = false;
                                    orbActivationConsumedThisPress[currplayer] = false;
                                }
                                else
                                {
                                    // Do not consider deferred pending presses as a fresh press for buffering
                                    bool freshPressEdge = (pendingPresses_num > 0) && (pendingPressStartedOnGround == 0) && !(pendingPresses_forLater > 0);

                                    // Set buffer when a fresh press occurs while airborne
                                    // Do not allow fresh presses to prime if a previous
                                    // hold-activation consumed the held X and suppression
                                    // is active; a release is required to reset.
                                    if (freshPressEdge && !effectiveOnGround_local && !orbHoldSuppressing[currplayer])
                                    {
                                        orbBufferActive[currplayer] = true;
                                        orbHoldConsumed[currplayer] = false;
                                    }

                                    // Also allow holding X in-air to prime the buffer (if not already active).
                                    // This covers the case where X was pressed earlier and the player became
                                    // airborne before we could set the buffer on the press frame.
                                    if (!orbBufferActive[currplayer] && (keyXHeld_local || IsXDownAsync()) && !effectiveOnGround_local && keyXHeldStartedOnGround_local_int == 0 && !orbHoldConsumedKeyStillDown[currplayer] && !orbHoldSuppressing[currplayer])
                                    {
                                        orbBufferActive[currplayer] = true;
                                        // Do not mark orbHoldConsumed here; consumption happens when a hold-based
                                        // activation actually fires.
                                    }

                                    // NOTE: clearing the orb buffer when X is released is handled
                                    // after orb activation checks below to avoid races where the
                                    // UI thread primes the buffer and the numeric thread clears
                                    // it before activations can consume it.

                                    // A fresh press edge should reset hold-consumption so a new hold can be used
                                    if (freshPressEdge) { orbHoldConsumed[currplayer] = false; orbHoldConsumedKeyStillDown[currplayer] = false; }
                                }
                            }
                            catch { }

                            // === REFACTORED PHYSICS INTEGRATION ===
                            // Use the new refactored physics system for all game modes
                            if (useRefactoredPhysics)
                            {
                                try
                                {
                                    // ProcessAllGameModesRefactored(); // REMOVED - fresh port
                                }
                                catch (Exception ex)
                                {
                                    AppendSimDebug($"Refactored physics error: {ex.Message}");
                                    // Fall back to old physics on error
                                    useRefactoredPhysics = false;
                                }
                            }
                            // === END REFACTORED PHYSICS ===
                            else if (!jumpAppliedThisStep_local && !effectiveOnGround_local && (playerVelY_fixed != 0 || playerY_fixed < maxPlayerY_fixed_local - LAND_EPS_FIXED))
                            // Apply gravity only if we did not just apply a jump, are not grounded,
                            // and if moving vertically or not at the bottom clamp. Prevents gravity
                            // from kicking in while standing on a surface which caused jitter.
                            {
                                if (currentGameMode == 1)
                                {
                                    // Ship-mode numeric sim gravity selection (use local key state)
                                    try
                                    {
                                        bool normalGravity = !gravityReversed;
                                        bool movingUpRelative = normalGravity ? (playerVelY_fixed < 0) : (playerVelY_fixed > 0);
                                        bool xheld_local = IsXDownAsync() || keyXHeld_local;

                                                int gravitySign_local = gravityReversed ? -1 : 1;
                                                // canonical gravity flag already applied via gravityReversed

                                                bool movingUpRelative_local = gravitySign_local > 0 ? (playerVelY_fixed < 0) : (playerVelY_fixed > 0);
                                                int tmpMag_local = movingUpRelative_local ? (xheld_local ? SHIP_GRAVITY_HOLD_FALL : SHIP_GRAVITY_BASE)
                                                                                      : (xheld_local ? SHIP_GRAVITY_AFTER_HOLD : SHIP_GRAVITY);

                                                int tmpgravity_local = (int)(tmpMag_local * gravitySign_local * gravityMultiplier);
                                                if (xheld_local) tmpgravity_local = -tmpgravity_local; // X = thrust opposite to gravity

                                                try { playerVelY_fixed += (int)Math.Round(tmpgravity_local * simTimeScale * simTimeScale); } catch { playerVelY_fixed += tmpgravity_local; }

                                                try
                                                {
                                                    if (gravitySign_local < 0)
                                                    {
                                                        if (playerVelY_fixed < -SHIP_MAX_FALLSPEED) playerVelY_fixed = -SHIP_MAX_FALLSPEED;
                                                        if (playerVelY_fixed > SHIP_MAX_FALLSPEED_HOLD) playerVelY_fixed = SHIP_MAX_FALLSPEED_HOLD;
                                                    }
                                                    else
                                                    {
                                                        if (playerVelY_fixed < -SHIP_MAX_FALLSPEED_HOLD) playerVelY_fixed = -SHIP_MAX_FALLSPEED_HOLD;
                                                        if (playerVelY_fixed > SHIP_MAX_FALLSPEED) playerVelY_fixed = SHIP_MAX_FALLSPEED;
                                                    }
                                                }
                                                catch { }
                                            }
                                            catch { try { playerVelY_fixed += (int)Math.Round(effectiveGravity_fixed * gravityMultiplier * simTimeScale * simTimeScale); } catch { playerVelY_fixed += (int)(effectiveGravity_fixed * gravityMultiplier); } }
                                }
                                        else if (currentGameMode == 2)
                                        {
                                            try
                                            {
                                                int gravitySign_local = gravityReversed ? -1 : 1;
                                                // canonical gravity flag already applied via gravityReversed
                                                bool xheld_local = IsXDownAsync() || keyXHeld_local;

                                                int tmpMag_local = BALL_GRAVITY;
                                                // Use ballGoingDown as the authoritative gravity direction for ball mode
                                                int gravityDir_local = ballGoingDown ? 1 : -1;
                                                if (gravityReversed) gravityDir_local = -gravityDir_local;
                                                // canonical gravity flag already applied via gravityReversed

                                                int tmpgravity_local = tmpMag_local * gravityDir_local;

                                                try { playerVelY_fixed += (int)Math.Round(tmpgravity_local * simTimeScale * simTimeScale); } catch { playerVelY_fixed += tmpgravity_local; }

                                                try
                                                {
                                                    int effectiveBallMaxFall_local = gravityDir_local >= 0 ? BALL_MAX_FALLSPEED : -BALL_MAX_FALLSPEED;
                                                    if (effectiveBallMaxFall_local >= 0)
                                                    {
                                                        if (playerVelY_fixed > effectiveBallMaxFall_local) playerVelY_fixed = effectiveBallMaxFall_local;
                                                    }
                                                    else
                                                    {
                                                        if (playerVelY_fixed < effectiveBallMaxFall_local) playerVelY_fixed = effectiveBallMaxFall_local;
                                                    }
                                                }
                                                catch { }
                                            }
                                            catch { try { playerVelY_fixed += (int)Math.Round(effectiveGravity_fixed * gravityMultiplier * simTimeScale * simTimeScale); } catch { playerVelY_fixed += (int)(effectiveGravity_fixed * gravityMultiplier); } }
                                        }
                                        else
                                        {
                                            try { playerVelY_fixed += (int)Math.Round(effectiveGravity_fixed * gravityMultiplier * simTimeScale * simTimeScale); } catch { playerVelY_fixed += (int)(effectiveGravity_fixed * gravityMultiplier); }
                                            try
                                            {
                                                if (effectiveMaxFall_fixed >= 0)
                                                {
                                                    if (playerVelY_fixed > effectiveMaxFall_fixed) playerVelY_fixed = effectiveMaxFall_fixed;
                                                }
                                                else
                                                {
                                                    if (playerVelY_fixed < effectiveMaxFall_fixed) playerVelY_fixed = effectiveMaxFall_fixed;
                                                }
                                            }
                                            catch { }
                                        }
                            }

                            // integrate
                            playerY_fixed += playerVelY_fixed;

                            // Inline gravity portal check REMOVED (Fix 20) — this was a second
                            // duplicate that ran after P1_RESTORE in dual mode, corrupting P1's
                            // gravity by activating normal portals. Gravity portals are now
                            // handled exclusively by CheckGravityPortals() in the Fresh physics path.

                            // try { OrbPad_HandleNumericActivations(pendingPresses_num, pendingPressStartedOnGround, keyXHeld_local, ref jumpAppliedThisStep_local, pendingPresses_forLater); } catch { } // REMOVED - fresh port
                            try
                            {
                                // Cube_HandleDeferredJump(pendingPresses_forLater, ref jumpAppliedThisStep_local); // REMOVED - fresh port
                            }
                            catch { }

                            try
                            {
                                // Cube_HandleHeldJump(jumpBuffered_local, pendingPresses_num, keyXHeld_local, ref jumpAppliedThisStep_local); // REMOVED - fresh port
                            }
                            catch { }

                            // Ceiling collision handled by cube-mode specific handler
                            try
                            {
                                // Cube_HandleCeilingCollision(jumpBuffered_local, pendingKeyX_local, keyXHeld_local, pendingPresses_num, pendingPressStartedOnGround, pendingPresses_forLater, ref jumpAppliedThisStep_local); // REMOVED - fresh port
                            }
                            catch { }

                        // Landing detection replaced by cube-mode handler
                        try
                        {
                            // Cube_HandleLandingAndReversed(jumpBuffered_local, pendingKeyX_local, keyXHeld_local, pendingPresses_num, pendingPressStartedOnGround, pendingPresses_forLater, ref jumpAppliedThisStep_local); // REMOVED - fresh port
                        }
                        catch { }
                    }
                    catch { }
                }

                // Clamp cameraY
                if (cameraY_fixed < 0) cameraY_fixed = 0;
                if (cameraY_fixed > maxCameraY_fixed_local) cameraY_fixed = maxCameraY_fixed_local;

                // Final safety clamp: ensure player remains above ground after camera moves
                if (playerY_fixed > maxPlayerY_fixed_local) { playerY_fixed = maxPlayerY_fixed_local; playerVelY_fixed = 0; }

                // === RIGHT SIDE DEATH CHECK - REMOVED ===
                // This check has been moved to CubeEject_Fresh() where it runs as part of collision detection
                // Keeping it here caused inconsistent timing between runs
                // === END RIGHT SIDE DEATH CHECK ===
                
                // === SCREEN-SPACE ENFORCEMENT — DISABLED ===
                // This was pushing playerY to keep the sprite above the visual ground boundary,
                // but it fights physics: the implicit-ground eject places the cube at Y=369,
                // while this clamp pushed it to Y=368, which clips into tile row 23 and causes
                // a floor eject to Y=353. The forward collision probe at center Y=360 (tile 22)
                // then misses the block at tile 23 — "snap over" bug.
                // NES has no such enforcement — ground position is determined purely by physics.
                // === END SCREEN-SPACE ENFORCEMENT ===

                // Detect speed portals between prevCameraCenter_fixed and current center
                int center_fixed = cameraX_fixed + ((NES_W * TILE / 2) << 8);
                int? newSpeed_fixed = null;
                // When physics is active, SPEED_P1/SPEED_P2 in sprite interactions
                // handle speed portals at correct NES timing (OLD X, before advance).
                // Skip legacy detection to avoid wrong-timing speed changes.
                if (!physicsEnabled)
                {
                for (int _si = 0; !physicsEnabled && _si < nonEmptySpriteIndices.Length; _si++)
                {
                    int idx = nonEmptySpriteIndices[_si]; int sid = sprites[idx];
                    if (sid < 0) continue;

                    // Gravity portals are now handled exclusively by CheckGravityPortals()
                    // in the Fresh physics path. The duplicate UI-path check was removed
                    // because it ran after P2 processing with P1's restored state, causing
                    // spurious portal activations that desynchronized gravity during dual mode.

                    if (!speedPortalMap.ContainsKey(sid)) continue;
                    int anchorTileX = (spriteAnchors != null && spriteAnchors.TryGetValue(idx, out var a)) ? a.anchorTileX : idx % mapWidth;
                    int anchorX_center_fixed = ((anchorTileX * TILE) + (TILE / 2)) << 8;

                    // When cam mode is OFF, use hitbox-based collision like gamemode/gravity portals
                    if (!camModeActive)
                    {
                        int hitboxW_speed = miniMode ? 8 : 15;
                        int hitboxH_speed = miniMode ? 7 : 15;
                        int playerLeft_px_speed = (playerX_fixed >> 8) + 1;
                        int playerRight_px_speed = playerLeft_px_speed + hitboxW_speed - 1;
                        int playerTop_px_speed = (playerY_fixed >> 8);
                        playerTop_px_speed += GetMiniSpriteOffsetY();
                        int playerBottom_px_speed = playerTop_px_speed + hitboxH_speed - 1;

                        if (SpriteIntersectsPlayer(idx, sid, playerLeft_px_speed, playerRight_px_speed, playerTop_px_speed, playerBottom_px_speed))
                        {
                            if (!processedSpeedPortals.Contains(idx))
                            {
                                newSpeed_fixed = speedPortalMap[sid];
                                processedSpeedPortals.Add(idx);
                                break;
                            }
                        }
                        // No else: once processed, speed portals stay in the set.
                    }
                    // When cam mode is ON, use interaction-line crossing logic
                    else if (crossedInteraction)
                    {
                        if (anchorX_center_fixed > prevPlayerCenter_fixed && anchorX_center_fixed <= INTERACTION_LINE_FIXED)
                        {
                            if (!processedSpeedPortals.Contains(idx))
                            {
                                newSpeed_fixed = speedPortalMap[sid];
                                processedSpeedPortals.Add(idx);
                                break;
                            }
                        }
                        // No else: once processed, speed portals stay in the set.
                    }
                    else
                    {
                        if (center_fixed >= prevCameraCenter_fixed && anchorX_center_fixed > prevCameraCenter_fixed && anchorX_center_fixed <= center_fixed)
                        {
                            if (!processedSpeedPortals.Contains(idx))
                            {
                                newSpeed_fixed = speedPortalMap[sid];
                                processedSpeedPortals.Add(idx);
                                break;
                            }
                        }
                        // No else: once processed, speed portals stay in the set.
                    }
                }
                if (newSpeed_fixed.HasValue)
                {
                    currentSpeed_fixed = newSpeed_fixed.Value;
                    playerVelX_fixed = newSpeed_fixed.Value; // Update Wave horizontal velocity for current speed
                    
                    // Update speed variable for debug display
                    if (newSpeed_fixed.Value == CUBE_SPEED_X05) speed = 0;
                    else if (newSpeed_fixed.Value == CUBE_SPEED_X1) speed = 1;
                    else if (newSpeed_fixed.Value == CUBE_SPEED_X2) speed = 2;
                    else if (newSpeed_fixed.Value == CUBE_SPEED_X3) speed = 3;
                    else if (newSpeed_fixed.Value == CUBE_SPEED_X4) speed = 4;
                    else if (newSpeed_fixed.Value == CUBE_SPEED_SLOW) speed = 5;
                    
                    // Update combo box selection to match
                    try
                    {
                        Dispatcher?.BeginInvoke(new Action(() =>
                        {
                            if (SpeedComboBox != null && speed >= 0 && speed < SpeedComboBox.Items.Count)
                            {
                                SpeedComboBox.SelectedIndex = speed;
                            }
                        }));
                    }
                    catch { }
                }
                } // end if (!physicsEnabled) — legacy speed portal detection

                // Detect color triggers; instead of sampling/pixel work here, record pending triggers
                int bestBg_fixed = int.MaxValue; int? bgIdxLocal = null; int? bgSidLocal = null;
                int bestTile_fixed = int.MaxValue; int? tileIdxLocal = null; int? tileSidLocal = null;
                int bestGround_fixed = int.MaxValue; int? groundIdxLocal = null; int? groundSidLocal = null;

                for (int _si = 0; !physicsEnabled && _si < nonEmptySpriteIndices.Length; _si++)
                {
                    int idx = nonEmptySpriteIndices[_si]; int sid = sprites[idx];
                    if (sid < 0) continue;
                    if (!IsColorTriggerSprite(sid)) continue;
                    int anchorTileX = (spriteAnchors != null && spriteAnchors.TryGetValue(idx, out var a2)) ? a2.anchorTileX : idx % mapWidth;
                    int anchorX_center_fixed = ((anchorTileX * TILE) + (TILE / 2)) << 8;
                    if (crossedInteraction)
                    {
                        if (anchorX_center_fixed > prevPlayerCenter_fixed && anchorX_center_fixed <= INTERACTION_LINE_FIXED)
                        {
                            if (IsBackgroundTrigger(sid)) { if (anchorX_center_fixed < bestBg_fixed) { bestBg_fixed = anchorX_center_fixed; bgIdxLocal = idx; bgSidLocal = sid; } }
                            else if (IsTileTrigger(sid)) { if (anchorX_center_fixed < bestTile_fixed) { bestTile_fixed = anchorX_center_fixed; tileIdxLocal = idx; tileSidLocal = sid; } }
                            else if (IsGroundTrigger(sid)) { if (anchorX_center_fixed < bestGround_fixed) { bestGround_fixed = anchorX_center_fixed; groundIdxLocal = idx; groundSidLocal = sid; } }
                        }
                        else
                        {
                            if (processedColorTriggers.Contains(idx) && anchorX_center_fixed > INTERACTION_LINE_FIXED) processedColorTriggers.Remove(idx);
                            if (processedGravityPortals.Contains(idx) && anchorX_center_fixed > INTERACTION_LINE_FIXED) processedGravityPortals.Remove(idx);
                            if (processedGravityModPortals.Contains(idx) && anchorX_center_fixed > INTERACTION_LINE_FIXED) processedGravityModPortals.Remove(idx);
                            if (processedTeleportPortals.Contains(idx) && anchorX_center_fixed > INTERACTION_LINE_FIXED) processedTeleportPortals.Remove(idx);
                        }
                    }
                    else
                    {
                        if (center_fixed >= prevCameraCenter_fixed && anchorX_center_fixed > prevCameraCenter_fixed && anchorX_center_fixed <= center_fixed)
                        {
                            if (!processedColorTriggers.Contains(idx))
                            {
                                if (IsBackgroundTrigger(sid)) { if (anchorX_center_fixed < bestBg_fixed) { bestBg_fixed = anchorX_center_fixed; bgIdxLocal = idx; bgSidLocal = sid; } }
                                else if (IsTileTrigger(sid)) { if (anchorX_center_fixed < bestTile_fixed) { bestTile_fixed = anchorX_center_fixed; tileIdxLocal = idx; tileSidLocal = sid; } }
                                else if (IsGroundTrigger(sid)) { if (anchorX_center_fixed < bestGround_fixed) { bestGround_fixed = anchorX_center_fixed; groundIdxLocal = idx; groundSidLocal = sid; } }
                            }
                        }
                        else { if (processedColorTriggers.Contains(idx)) processedColorTriggers.Remove(idx); if (processedGravityPortals.Contains(idx)) processedGravityPortals.Remove(idx); if (processedGravityModPortals.Contains(idx)) processedGravityModPortals.Remove(idx); if (processedGameModePortals.Contains(idx)) processedGameModePortals.Remove(idx); if (processedOrbs.Contains(idx)) processedOrbs.Remove(idx); if (processedTeleportPortals.Contains(idx)) processedTeleportPortals.Remove(idx); }
                    }
                }

                // If any triggers detected, set pending fields so UI thread will sample and apply tints
                if (bgIdxLocal.HasValue) { pendingBgIdx = bgIdxLocal ?? -1; pendingBgSid = bgSidLocal ?? -1; pendingTintChange = true; }
                if (tileIdxLocal.HasValue) { pendingTileIdx = tileIdxLocal ?? -1; pendingTileSid = tileSidLocal ?? -1; pendingTintChange = true; }
                if (groundIdxLocal.HasValue) { pendingGroundIdx = groundIdxLocal ?? -1; pendingGroundSid = groundSidLocal ?? -1; pendingTintChange = true; }

                // Detect end-level trigger (sprite 0x0F) using the same X position logic as color triggers.
                // NES only processes sprites that are loaded into activesprites slots, which requires
                // them to be on-screen. Add a Y visibility check so off-screen triggers don't fire.
                if (!physicsEnabled && !levelCompleteTriggered && !deathTriggered)
                {
                    int screenTopY = cameraY_fixed >> 8;
                    int screenBottomY = screenTopY + NES_H * TILE;

                    for (int _si = 0; _si < nonEmptySpriteIndices.Length; _si++)
                    {
                        int idx = nonEmptySpriteIndices[_si]; int sid = sprites[idx];
                        if (sid != 0x0F) continue;
                        if (processedEndLevelTriggers.Contains(idx)) continue;
                        int anchorTileX, anchorTileY;
                        if (spriteAnchors != null && spriteAnchors.TryGetValue(idx, out var aEnd))
                        {
                            anchorTileX = aEnd.anchorTileX;
                            anchorTileY = aEnd.anchorTileY;
                        }
                        else
                        {
                            anchorTileX = idx % mapWidth;
                            anchorTileY = idx / mapWidth;
                        }

                        // Skip if sprite is not vertically on-screen
                        int spriteWorldY = anchorTileY * TILE;
                        if (spriteWorldY + TILE < screenTopY || spriteWorldY >= screenBottomY)
                            continue;

                        int anchorX_center_fixed = ((anchorTileX * TILE) + (TILE / 2)) << 8;

                        bool activated = false;
                        if (crossedInteraction)
                        {
                            if (anchorX_center_fixed > prevPlayerCenter_fixed && anchorX_center_fixed <= INTERACTION_LINE_FIXED)
                                activated = true;
                        }
                        else
                        {
                            if (center_fixed >= prevCameraCenter_fixed && anchorX_center_fixed > prevCameraCenter_fixed && anchorX_center_fixed <= center_fixed)
                                activated = true;
                        }

                        if (activated)
                        {
                            processedEndLevelTriggers.Add(idx);
                            levelCompleteTriggered = true;
                            paused = true;
                            // Music keeps running — do NOT call StopMusicAsync()
                            AppendSimDebug($"[LEVEL_COMPLETE] End-level trigger 0x0F at anchor tile X={anchorTileX}");

                            try
                            {
                                // Capture coin info for UI thread
                                var coinInfoSnapshot = new System.Collections.Generic.List<(int spriteIndex, int spriteId)>(collectedCoinInfo);
                                Dispatcher?.BeginInvoke(new Action(() =>
                                {
                                    try { PauseOverlay.Visibility = System.Windows.Visibility.Collapsed; } catch { }
                                    try { LevelCompleteOverlay.Visibility = System.Windows.Visibility.Visible; } catch { }
                                    try { PopulateCoinDisplay(coinInfoSnapshot); } catch { }
                                }));
                            }
                            catch { }
                            break;
                        }
                        else
                        {
                            // If trigger moved past the player (scrolled off), allow re-detection
                            if (anchorX_center_fixed > center_fixed && processedEndLevelTriggers.Contains(idx))
                                processedEndLevelTriggers.Remove(idx);
                        }
                    }
                }

                // Detect hide/show player triggers (0x6F=hide, 0x7F=show) using
                // the same X-crossing logic as end-level/color triggers.
                // NES activates these on X threshold crossing, not hitbox overlap.
                for (int _si = 0; !physicsEnabled && _si < nonEmptySpriteIndices.Length; _si++)
                {
                    int idx = nonEmptySpriteIndices[_si]; int sid = sprites[idx];
                    if (sid != 0x6F && sid != 0x7F) continue;
                    if (processedPlayerInvisTriggers.Contains(idx)) continue;
                    int anchorTileX = (spriteAnchors != null && spriteAnchors.TryGetValue(idx, out var aVis)) ? aVis.anchorTileX : idx % mapWidth;
                    int anchorX_center_fixed = ((anchorTileX * TILE) + (TILE / 2)) << 8;

                    bool activated = false;
                    if (crossedInteraction)
                    {
                        if (anchorX_center_fixed > prevPlayerCenter_fixed && anchorX_center_fixed <= INTERACTION_LINE_FIXED)
                            activated = true;
                    }
                    else
                    {
                        if (center_fixed >= prevCameraCenter_fixed && anchorX_center_fixed > prevCameraCenter_fixed && anchorX_center_fixed <= center_fixed)
                            activated = true;
                    }

                    if (activated)
                    {
                        playerInvis = (sid == 0x6F);
                        processedPlayerInvisTriggers.Add(idx);
                    }
                    else
                    {
                        if (anchorX_center_fixed > center_fixed && processedPlayerInvisTriggers.Contains(idx))
                            processedPlayerInvisTriggers.Remove(idx);
                    }
                }
            }

            // Update trail Y position history (sliding window)
            // Shift old positions down and store current Y at index 0
            // Use playerY_fixed (the active player's Y) rather than player_y_fixed[0],
            // which is only synced during dual mode transitions and stays 0 in single-player.
            if (forcedTrails > 0)
            {
                for (int ti = playerOldPosY.Length - 1; ti > 0; ti--)
                    playerOldPosY[ti] = playerOldPosY[ti - 1];
                playerOldPosY[0] = playerY_fixed;
            }

            // If we have pending tints, schedule application on UI thread for heavier image work
            if (pendingTintChange)
            {
                try { Dispatcher?.BeginInvoke((Action)(() => { try { ApplyPendingTints(); } catch { } })); } catch { }
            }
        }

        /// <summary>
        /// Populate the coin display panel on the level complete overlay with sprite anchors
        /// of collected coins.
        /// </summary>
        private void PopulateCoinDisplay(System.Collections.Generic.List<(int spriteIndex, int spriteId)> coinInfo)
        {
            try
            {
                if (CoinDisplayPanel == null) return;
                CoinDisplayPanel.Children.Clear();

                if (coinInfo == null || coinInfo.Count == 0) return;

                foreach (var (spriteIndex, spriteId) in coinInfo)
                {
                    try
                    {
                        // Skip mini coins (0x6E) — only show regular coins on level complete screen
                        if (SharedPhysics.IsMiniCoinSprite(spriteId)) continue;

                        // Get the sprite image for this coin
                        ImageSource? coinImage = null;
                        if (spriteImages != null && spriteId >= 0 && spriteId < spriteImages.Length)
                            coinImage = spriteImages[spriteId];

                        if (coinImage != null)
                        {
                            var img = new System.Windows.Controls.Image
                            {
                                Source = coinImage,
                                Width = 16,
                                Height = 16,
                                Margin = new Thickness(4, 0, 4, 0),
                                SnapsToDevicePixels = true
                            };
                            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
                            CoinDisplayPanel.Children.Add(img);
                        }
                        else
                        {
                            // Fallback: show a text placeholder
                            var txt = new System.Windows.Controls.TextBlock
                            {
                                Text = $"0x{spriteId:X2}",
                                Foreground = new SolidColorBrush(Colors.Gold),
                                FontSize = 10,
                                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                                Margin = new Thickness(4, 0, 4, 0),
                                VerticalAlignment = VerticalAlignment.Center
                            };
                            CoinDisplayPanel.Children.Add(txt);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        // Apply pending trigger tints on the UI thread and regenerate toned images as necessary
        private void ApplyPendingTints()
        {
            try
            {
                bool wasStartup = pendingTintChangeIsStartup;
                int bgIdxLocal = pendingBgIdx; int bgSidLocal = pendingBgSid;
                int tileIdxLocal = pendingTileIdx; int tileSidLocal = pendingTileSid;
                int groundIdxLocal = pendingGroundIdx; int groundSidLocal = pendingGroundSid;

                pendingBgIdx = -1; pendingBgSid = -1; pendingTileIdx = -1; pendingTileSid = -1; pendingGroundIdx = -1; pendingGroundSid = -1; pendingTintChange = false; pendingTintChangeIsStartup = false;
                // Remember that we applied a startup-origin tint so future regenerations avoid object tinting.
                if (wasStartup) startupTintApplied = true;

                var prevBackgroundTint = backgroundTint; var prevTileTint = tileTint; var prevGroundTint = groundTint;
                var prevBackgroundForceSolidBlack = backgroundForceSolidBlack;
                // Determine the candidate tile/object tint for this pending change. If a tile/object
                // trigger is pending (tileIdxLocal present) prefer its new tint so outline recoloring
                // uses the up-to-date color even before we assign `tileTint` below.
                Color candidateTileTint = tileTint;
                try { if (tileIdxLocal >= 0 && tileSidLocal >= 0) candidateTileTint = ColorFromTrigger(tileSidLocal); } catch { }

                if (bgIdxLocal >= 0 && bgSidLocal >= 0)
                {
                    var c = ColorFromTrigger(bgSidLocal);
                    backgroundTint = c; processedColorTriggers.Add(bgIdxLocal);
                    // mark special-case solid-black background trigger (0x8F)
                    backgroundForceSolidBlack = (bgSidLocal == 0x8F);
                    if (enableSimulatorDebugLogging && !triggerLogged.Contains(bgIdxLocal)) { WriteTempLog($"Simulator: Applied background trigger at idx={bgIdxLocal} sid=0x{bgSidLocal:X} color={c}"); triggerLogged.Add(bgIdxLocal); }
                }
                if (tileIdxLocal >= 0 && tileSidLocal >= 0)
                {
                    var c = ColorFromTrigger(tileSidLocal);
                    tileTint = c; processedColorTriggers.Add(tileIdxLocal);
                    if (enableSimulatorDebugLogging && !triggerLogged.Contains(tileIdxLocal)) { WriteTempLog($"Simulator: Applied tile trigger at idx={tileIdxLocal} sid=0x{tileSidLocal:X} color={c}"); triggerLogged.Add(tileIdxLocal); }
                }
                if (groundIdxLocal >= 0 && groundSidLocal >= 0)
                {
                    var c = ColorFromTrigger(groundSidLocal);
                    // If this pending ground trigger is the special black-ground id (0xCF),
                    // force the ground tint to pure black so the ground images become
                    // black-masked (preserving the thin white seam). This keeps the
                    // behavior consistent regardless of whether tints are applied
                    // immediately or via the pending-tint path.
                    if (groundSidLocal == 0xCF)
                    {
                        // Special-case 0xCF in the pending-path as well: force pure black
                        // so the ground images are generated as two-tone black and the
                        // seams remain available for object tinting.
                        groundTint = Color.FromArgb(255, 0, 0, 0);
                    }
                    else
                    {
                        groundTint = c;
                    }
                    processedColorTriggers.Add(groundIdxLocal);
                    if (enableSimulatorDebugLogging && !triggerLogged.Contains(groundIdxLocal)) { WriteTempLog($"Simulator: Applied ground trigger at idx={groundIdxLocal} sid=0x{groundSidLocal:X} color={c}"); triggerLogged.Add(groundIdxLocal); }
                }

                bool bgChanged = !AreColorsEqual(prevBackgroundTint, backgroundTint);
                bool tileChanged = !AreColorsEqual(prevTileTint, tileTint);
                bool grdChanged = !AreColorsEqual(prevGroundTint, groundTint);
                // If this pending tint application originated from startup, force a regeneration
                // so two-tone/outline images exist before the first render even when the
                // starting tint equals the current tint values.
                bool forceRegenerateOnStartup = wasStartup;

                // Compute outline tint to use for this regeneration. Preserve object outline
                // recoloring on ground-only changes so object tints are not cleared by a ground trigger.
                var outlineTintParam = Color.FromArgb(0, 0, 0, 0);
                if (!wasStartup)
                {
                    if (tileIdxLocal >= 0 || !AreColorsEqual(prevTileTint, candidateTileTint))
                    {
                        // Explicit tile/object trigger or tile tint changed: use the candidate/new tint
                        outlineTintParam = candidateTileTint;
                    }
                    else if (grdChanged)
                    {
                        // Ground-only change: preserve existing tile tint as outline so object
                        // recolors survive ground updates.
                        outlineTintParam = tileTint;
                    }
                }

                // Check if we can use cached toned images instead of regenerating
                bool canUseCache = !forceRegenerateOnStartup && 
                    cachedTileTonedImages != null && 
                    cachedParallaxTonedImages != null && 
                    cachedGroundTonedImages != null &&
                    AreColorsEqual(cachedBackgroundTint, backgroundTint) &&
                    AreColorsEqual(cachedTileTint, tileTint) &&
                    AreColorsEqual(cachedGroundTint, groundTint) &&
                    cachedBackgroundForceSolidBlack == backgroundForceSolidBlack;

                if (canUseCache)
                {
                    // Use cached images - no regeneration needed
                    tileTonedImages = cachedTileTonedImages;
                    parallaxTonedImages = cachedParallaxTonedImages;
                    groundTonedImages = cachedGroundTonedImages;
                }
                else if (tileChanged || bgChanged || grdChanged || forceRegenerateOnStartup)
                {
                    // Recompute tile-toned images.
                    // Compute separate palette-driven primary/secondary colors for background and ground
                    // so background triggers do not override ground visuals.
                    Color? bgPrimary = null; Color? bgSecondary = null;
                    Color? grdPrimary = null; Color? grdSecondary = null;
                    try
                    {
                        var palette = PaletteProvider.GetPalette();

                        // Background palette mapping (only from explicit background trigger)
                        if (bgSidLocal >= 0)
                        {
                            // Lighter shade must be the exact trigger color; darker shade comes from the
                            // trigger one row up (sid - 0x10). If the trigger is in the first row
                            // (0x80..0x8C) then the darker shade must be pure black.
                            try
                            {
                                var prim = ColorFromTrigger(bgSidLocal);
                                bgPrimary = prim;
                                if (bgSidLocal >= 0x80 && bgSidLocal <= 0x8C)
                                {
                                    bgSecondary = Color.FromArgb(255, 0, 0, 0);
                                }
                                else
                                {
                                    int darkerSid = bgSidLocal - 0x10;
                                    var darker = ColorFromTrigger(darkerSid);
                                    bgSecondary = darker;
                                }
                            }
                            catch { bgPrimary = null; bgSecondary = null; }
                        }

                        // Ground palette mapping (only from explicit ground trigger)
                        if (groundSidLocal >= 0)
                        {
                            int? pidx = GetPaletteIndexForBackgroundTrigger(groundSidLocal);
                            if (pidx.HasValue && pidx.Value >= 0 && pidx.Value < palette.Length)
                            {
                                grdPrimary = palette[pidx.Value];
                                int col = pidx.Value % 14; if (col > 12) col = 12;
                                int row = pidx.Value / 14;
                                // Special-case: triggers in 0xC0..0xCF must have darker secondary = pure black
                                if (groundSidLocal >= 0xC0 && groundSidLocal <= 0xCF)
                                {
                                    grdSecondary = Color.FromArgb(255, 0, 0, 0);
                                }
                                else
                                {
                                    // Ground two-tone: derive a darker secondary from primary (avoid row-shifting)
                                    try
                                    {
                                        RgbToHsl(grdPrimary.Value.R, grdPrimary.Value.G, grdPrimary.Value.B, out double gh, out double gs, out double gl);
                                        double darkerL = Math.Max(0.0, gl - 0.12);
                                        RgbFromHsl(gh, gs, darkerL, out byte sgr, out byte sgg, out byte sgb);
                                        grdSecondary = Color.FromArgb(255, sgr, sgg, sgb);
                                    }
                                    catch { grdSecondary = Color.FromArgb(255, 0, 0, 0); }
                                }
                            }
                        }
                    }
                    catch { bgPrimary = null; bgSecondary = null; grdPrimary = null; grdSecondary = null; }

                    try
                    {
                        // Regenerate tile toned images using background two-tone mapping when available,
                        // and use tileTint to recolor white/outline pixels only (object triggers affect outlines).
                        if (tileImages != null)
                        {
                            // Use two-tone mapping for all tiles by default (so slopes and other tiles
                            // get the same background/non-white recoloring behavior). White/near-white
                            // outlines are handled by `outlineTint` (tileTint) so object color triggers
                            // still recolor outlines as intended.
                            // Only pass the outline tint when this regeneration is a result of an
                            // explicit object/tile trigger (tileIdxLocal >= 0). Background or
                            // ground-triggered regenerations should not recolor white seams.
                            // NOTE: `outlineTintParam` is computed earlier to preserve object
                            // outline recolors on ground-only updates; do not overwrite it here.
                            if (bgPrimary.HasValue)
                                tileTonedImages = CreateTwoToneTileImages(tileImages, bgPrimary.Value, bgSecondary ?? Color.FromArgb(255, 0, 0, 0), outlineTintParam);
                            else
                            {
                                if (backgroundForceSolidBlack)
                                {
                                    // For 0x8F, make non-white, non-green pixels black for tiles
                                    try { tileTonedImages = CreateBlackMaskedExceptColorArray(tileImages, playerPlaceholderGreen, Color.FromArgb(0,0,0,0), false); }
                                    catch { tileTonedImages = CreateTwoToneTileImages(tileImages, backgroundTint, Color.FromArgb(255, 0, 0, 0), outlineTintParam); }
                                }
                                else
                                {
                                    tileTonedImages = CreateTwoToneTileImages(tileImages, backgroundTint, Color.FromArgb(255, 0, 0, 0), outlineTintParam);
                                }
                            }

                            // Per-tile overrides: ensure specific tiles use background or ground tinting
                            try
                            {
                                if (tileTonedImages != null && tileImages != null)
                                {
                                    // Tiles that should get the background tint like other tiles: 0x90-0xA3, 0xD7, 0xD8
                                    // Process 0x90-0xA3 range
                                    for (int i = 0x90; i <= 0xA3; i++)
                                    {
                                        if (i >= 0 && i < tileImages.Length)
                                        {
                                            ImageSource? rep = null;
                                            // For slope tiles, ensure white/outline parts are recolored by object triggers (tileTint)
                                            // while non-white areas get background two-tone mapping. Prefer palette-driven two-tone
                                            // mapping when available; otherwise fall back to using backgroundTint as primary.
                                            if (bgPrimary.HasValue)
                                            {
                                                var arr = CreateTwoToneTileImages(new ImageSource?[] { tileImages[i]! }, bgPrimary.Value, bgSecondary ?? Color.FromArgb(255, 0, 0, 0), outlineTintParam);
                                                if (arr != null && arr.Length > 0) rep = arr[0];
                                            }
                                            else
                                            {
                                                // Use backgroundTint for non-white areas and outlineTintParam for outlines.
                                                // If solid-black background is forced, prefer the black-masked variant
                                                // so non-white/non-green pixels become black and are not overwritten
                                                // by two-tone fallbacks.
                                                if (backgroundForceSolidBlack)
                                                {
                                                    try { rep = CreateBlackMaskedExceptColor(tileImages[i], playerPlaceholderGreen, outlineTintParam, false); }
                                                    catch { rep = tileImages[i]; }
                                                }
                                                else
                                                {
                                                    var arr = CreateTwoToneTileImages(new ImageSource?[] { tileImages[i]! }, backgroundTint, Color.FromArgb(255, 0, 0, 0), outlineTintParam);
                                                    if (arr != null && arr.Length > 0) rep = arr[0];
                                                }
                                            }
                                            if (rep != null) tileTonedImages[i] = rep;
                                        }
                                    }

                                    // Process 0xD7 and 0xD8 separately
                                    foreach (int i in new[] { 0xD7, 0xD8 })
                                    {
                                        if (i >= 0 && i < tileImages.Length)
                                        {
                                            ImageSource? rep = null;
                                            if (bgPrimary.HasValue)
                                            {
                                                var arr = CreateTwoToneTileImages(new ImageSource?[] { tileImages[i]! }, bgPrimary.Value, bgSecondary ?? Color.FromArgb(255, 0, 0, 0), outlineTintParam);
                                                if (arr != null && arr.Length > 0) rep = arr[0];
                                            }
                                            else
                                            {
                                                if (backgroundForceSolidBlack)
                                                {
                                                    try { rep = CreateBlackMaskedExceptColor(tileImages[i], playerPlaceholderGreen, outlineTintParam, false); }
                                                    catch { rep = tileImages[i]; }
                                                }
                                                else
                                                {
                                                    var arr = CreateTwoToneTileImages(new ImageSource?[] { tileImages[i]! }, backgroundTint, Color.FromArgb(255, 0, 0, 0), outlineTintParam);
                                                    if (arr != null && arr.Length > 0) rep = arr[0];
                                                }
                                            }
                                            if (rep != null) tileTonedImages[i] = rep;
                                        }
                                    }

                                    // Tiles that should get ground tint (exactly like ground): 0x01/02, 0x05/06, 0x88/89
                                    int[] groundTiles = new[] { 0x01, 0x02, 0x05, 0x06, 0x88, 0x89 };
                                    foreach (var i in groundTiles)
                                    {
                                        if (i >= 0 && i < tileImages.Length)
                                        {
                                            // Ground tiles should only respond to ground color triggers and should not
                                            // have their near-white outline pixels recolored here — those outlines
                                            // must remain untouched so object triggers can recolor them later.
                                            // Use ground palette mapping (grdPrimary/grdSecondary) when present; otherwise
                                            // fall back to a two-tone mapping using `groundTint`. Pass a transparent
                                            // outline tint so white/seam pixels are preserved now.
                                            // Determine outline behavior: if an outline tint (object/tile) is active
                                            // for this regeneration, allow seam pixels to be recolored to that color.
                                            // Otherwise preserve seams by passing a transparent outline tint.
                                            var outlineForThisTile = (outlineTintParam.A > 0) ? outlineTintParam : Color.FromArgb(0, 0, 0, 0);
                                            if (grdPrimary.HasValue)
                                            {
                                                var arr = CreateTwoToneTileImages(new ImageSource?[] { tileImages[i]! }, grdSecondary ?? Color.FromArgb(255, 0, 0, 0), grdPrimary.Value, outlineForThisTile);
                                                if (arr != null && arr.Length > 0 && arr[0] != null) tileTonedImages[i] = arr[0];
                                            }
                                            else
                                            {
                                                if (backgroundForceSolidBlack || (groundTint.A == 255 && groundTint.R == 0 && groundTint.G == 0 && groundTint.B == 0))
                                                {
                                                    try { tileTonedImages[i] = CreateBlackMaskedExceptColor(tileImages[i], playerPlaceholderGreen, outlineForThisTile, outlineForThisTile.A > 0); }
                                                    catch { tileTonedImages[i] = tileImages[i]; }
                                                }
                                                else
                                                {
                                                    // Use two-tone mapping based on groundTint and a palette-derived darker color.
                                                    var darker = PaletteHelper.RowUpColor(Color.FromArgb(groundTint.A, groundTint.R, groundTint.G, groundTint.B));
                                                    var arr = CreateTwoToneTileImages(new ImageSource?[] { tileImages[i]! }, groundTint, darker, outlineForThisTile);
                                                    if (arr != null && arr.Length > 0 && arr[0] != null) tileTonedImages[i] = arr[0];
                                                }
                                            }
                                        }
                                    }

                                    // Remap visuals: 0x8F should look like 0x2F; 0xFC should look like 0x00
                                    if (0x2F >= 0 && 0x2F < tileTonedImages.Length && 0x8F >= 0 && 0x8F < tileTonedImages.Length)
                                    {
                                        tileTonedImages[0x8F] = tileTonedImages[0x2F];
                                    }
                                    if (0x00 >= 0 && 0x00 < tileTonedImages.Length && 0xFC >= 0 && 0xFC < tileTonedImages.Length)
                                    {
                                        tileTonedImages[0xFC] = tileTonedImages[0x00];
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                    catch { tileTonedImages = CreateHslShiftedImages(tileImages, tileTint, tileTint); }

                    try {
                        // Only regenerate parallax when background actually changed or when a palette mapping
                        // is explicitly provided via bgPrimary, or when forced-black flag changed.
                        // Also regenerate when this was a startup-origin application so initial
                        // render sees fresh toned images.
                        if (!AreColorsEqual(prevBackgroundTint, backgroundTint) || prevBackgroundForceSolidBlack != backgroundForceSolidBlack || bgPrimary.HasValue || forceRegenerateOnStartup)
                        {
                            if (parallaxImages != null)
                            {
                                // Prefer palette-driven two-tone for the background/parallax images.
                                if (bgPrimary.HasValue)
                                {
                                    // Ensure we have a sensible secondary color: prefer palette-derived, otherwise
                                    // synthesize a darker secondary from the primary to avoid falling back to pure black.
                                    Color synthesizedBgSecondary;
                                    if (bgSecondary.HasValue) synthesizedBgSecondary = bgSecondary.Value;
                                    else { RgbToHsl(bgPrimary.Value.R, bgPrimary.Value.G, bgPrimary.Value.B, out double _bh, out double _bs, out double _bl); double _darker = Math.Max(0.0, _bl - 0.12); RgbFromHsl(_bh, _bs, _darker, out byte _dbr, out byte _dbg, out byte _dbb); synthesizedBgSecondary = Color.FromArgb(255, _dbr, _dbg, _dbb); }
                                    // Use primary as the lighter mapping and secondary as darker so the
                                    // background two-tone matches how ground visuals are derived.
                                    parallaxTonedImages = CreateTwoToneTileImages(parallaxImages, bgPrimary.Value, synthesizedBgSecondary, outlineTintParam);
                                }
                                else if (backgroundForceSolidBlack)
                                {
                                    // forced solid black: don't generate from images
                                    parallaxTonedImages = null;
                                }
                                else if (backgroundTint.A == 255 && backgroundTint.R == 0 && backgroundTint.G == 0 && backgroundTint.B == 0)
                                {
                                    parallaxTonedImages = CreateTwoToneTileImages(parallaxImages, Color.FromArgb(255,0,0,0), Color.FromArgb(255,0,0,0), outlineTintParam);
                                }
                                else
                                {
                                    parallaxTonedImages = CreateHueShiftedImages(parallaxImages, backgroundTint, outlineTintParam);
                                }
                            }
                            else parallaxTonedImages = parallaxImages;
                        }
                    } catch { parallaxTonedImages = parallaxImages; }
                    try
                    {
                        // Only regenerate ground visuals when the ground tint actually changed
                        // or when an explicit ground palette mapping is available.
                        // Also regenerate on startup-origin so ground visuals are ready
                        // for the first render.
                        if (!AreColorsEqual(prevGroundTint, groundTint) || grdPrimary.HasValue || forceRegenerateOnStartup)
                        {
                            if (grdPrimary.HasValue)
                            {
                                // For ground visuals, flip primary/secondary so ground uses swapped two-tone
                                Color synthesizedGrdSecondary;
                                if (grdSecondary.HasValue)
                                    synthesizedGrdSecondary = grdSecondary.Value;
                                else
                                {
                                    RgbToHsl(grdPrimary.Value.R, grdPrimary.Value.G, grdPrimary.Value.B, out double _gh, out double _gs, out double _gl);
                                    double _gdarker = Math.Max(0.0, _gl - 0.12);
                                    RgbFromHsl(_gh, _gs, _gdarker, out byte _sr, out byte _sg, out byte _sb);
                                    synthesizedGrdSecondary = Color.FromArgb(255, _sr, _sg, _sb);
                                }
                                groundTonedImages = CreateTwoToneTileImages(groundImages, synthesizedGrdSecondary, grdPrimary.Value, outlineTintParam);
                            }
                            else
                            {
                                // Preserve previous special-case behavior for pure-black ground tint
                                if (groundTint.A == 255 && groundTint.R == 0 && groundTint.G == 0 && groundTint.B == 0)
                                {
                                    groundTonedImages = CreateTwoToneTileImages(groundImages, Color.FromArgb(255, 0, 0, 0), Color.FromArgb(255, 0, 0, 0), outlineTintParam);
                                }
                                else
                                {
                                    // If ground tint is fully-opaque but no palette mapping is available,
                                    // generate a two-tone pair from the ground tint by making a darker
                                    // secondary color instead of performing a full replacement which
                                    // would make the ground a solid color.
                                    if (groundTint.A == 255)
                                    {
                                        // compute a darker variant for secondary
                                        RgbToHsl(groundTint.R, groundTint.G, groundTint.B, out double gh, out double gs, out double gl);
                                        double secL = Math.Max(0.0, gl - 0.12);
                                        RgbFromHsl(gh, gs, secL, out byte sr, out byte sg, out byte sb);
                                        var secondaryFromGround = Color.FromArgb(255, sr, sg, sb);
                                        // flip primary/secondary for ground visuals
                                        groundTonedImages = CreateTwoToneTileImages(groundImages, secondaryFromGround, groundTint, outlineTintParam);
                                    }
                                    else
                                    {
                                        groundTonedImages = CreateHueShiftedImages(groundImages, groundTint, outlineTintParam);
                                    }
                                }
                            }
                        }
                    }
                    catch { groundTonedImages = groundImages; }

                    // Cache the generated images for future use
                    cachedTileTonedImages = tileTonedImages;
                    cachedParallaxTonedImages = parallaxTonedImages;
                    cachedGroundTonedImages = groundTonedImages;
                    cachedBackgroundTint = backgroundTint;
                    cachedTileTint = tileTint;
                    cachedGroundTint = groundTint;
                    cachedBackgroundForceSolidBlack = backgroundForceSolidBlack;

                        // If an outline tint is active for this regeneration, force-apply an outline-only
                        // recolor to the ground images so thin/anti-aliased white seams are recolored
                        // the same way other tiles are when object triggers fire.
                        try
                        {
                            if (outlineTintParam.A > 0 && groundTonedImages != null)
                            {
                                var recol = CreateOutlineTintedTileImages(groundTonedImages, outlineTintParam);
                                if (recol != null) groundTonedImages = recol;
                            }
                        }
                        catch { }

                    // Also create/update a tinted full-parallax bitmap if a full parallax bitmap was provided
                    // Only update the full parallax bitmap when the background actually changed or when
                    // there is an explicit palette mapping for background, or when forced-black toggled.
                    try
                    {
                        if (parallaxBitmap != null && (bgChanged || prevBackgroundForceSolidBlack != backgroundForceSolidBlack || bgPrimary.HasValue))
                        {
                            ImageSource?[]? arr = null;
                            // Prefer palette-driven two-tone for full parallax bitmap when available
                            if (bgPrimary.HasValue)
                            {
                                arr = CreateTwoToneTileImages(new ImageSource?[] { parallaxBitmap! }, bgPrimary.Value, bgSecondary ?? Color.FromArgb(255,0,0,0), outlineTintParam);
                            }
                            else if (backgroundTint.A == 255 && backgroundTint.R == 0 && backgroundTint.G == 0 && backgroundTint.B == 0)
                                arr = CreateBlackMaskedImages(new ImageSource?[] { parallaxBitmap! });
                            else
                                arr = CreateHueShiftedImages(new ImageSource?[] { parallaxBitmap! }, backgroundTint);

                            if (arr != null && arr.Length > 0 && arr[0] != null) parallaxBitmapToned = arr[0];
                            else parallaxBitmapToned = parallaxBitmap;
                        }
                        else
                        {
                            // If we are not updating the parallax bitmap due to an unrelated ground change,
                            // leave the previous toned bitmap as-is so background visuals remain stable.
                        }
                    }
                    catch { parallaxBitmapToned = parallaxBitmap; }
                    // If the background tint changed, regenerate saw-frame tinted images
                    // Also regenerate saw-frame images when this is startup-origin so they
                    // appear correctly on the very first render.
                    if (!AreColorsEqual(prevBackgroundTint, backgroundTint) || forceRegenerateOnStartup)
                    {
                        try
                        {
                            // When background is pure opaque black, make saw frames fully black
                            // (no white lines or outlines). Otherwise preserve existing HSL shifted
                            // behavior and pass `tileTint` as outline tint so object recolors work.
                            if (backgroundTint.A == 255 && backgroundTint.R == 0 && backgroundTint.G == 0 && backgroundTint.B == 0)
                            {
                                this.sawFrame1TilesTinted = CreateSolidBlackImages(this.sawFrame1TilesOrig);
                                this.sawFrame2TilesTinted = CreateSolidBlackImages(this.sawFrame2TilesOrig);
                                this.smallSawFrame1TilesTinted = CreateSolidBlackImages(this.smallSawFrame1TilesOrig);
                                this.smallSawFrame2TilesTinted = CreateSolidBlackImages(this.smallSawFrame2TilesOrig);
                                this.largeSawFrame1TilesTinted = CreateSolidBlackImages(this.largeSawFrame1TilesOrig);
                                this.largeSawFrame2TilesTinted = CreateSolidBlackImages(this.largeSawFrame2TilesOrig);
                            }
                            else
                            {
                                // Use outlineTintParam so startup-origin changes don't recolor outlines/objects.
                                this.sawFrame1TilesTinted = CreateHslShiftedImages(this.sawFrame1TilesOrig, backgroundTint, outlineTintParam);
                                this.sawFrame2TilesTinted = CreateHslShiftedImages(this.sawFrame2TilesOrig, backgroundTint, outlineTintParam);
                                this.smallSawFrame1TilesTinted = CreateHslShiftedImages(this.smallSawFrame1TilesOrig, backgroundTint, outlineTintParam);
                                this.smallSawFrame2TilesTinted = CreateHslShiftedImages(this.smallSawFrame2TilesOrig, backgroundTint, outlineTintParam);
                                this.largeSawFrame1TilesTinted = CreateHslShiftedImages(this.largeSawFrame1TilesOrig, backgroundTint, outlineTintParam);
                                this.largeSawFrame2TilesTinted = CreateHslShiftedImages(this.largeSawFrame2TilesOrig, backgroundTint, outlineTintParam);
                            }
                        }
                        catch { }
                    }

                    tileLayerCache = null;
                    try { groundTintedTileCache.Clear(); } catch { }
                    try { spriteBackgroundCompositeCache.Clear(); } catch { }
                }
            }
            catch { }
        }

        // Determine if a sprite id is a color trigger we should consider
        private bool IsColorTriggerSprite(int spriteIdx)
        {
            if (spriteIdx < 0) return false;
            // Accept ranges like the editor, but exclude certain low-nibble values per user request
            // Background triggers: 0x80-0xAC
            // Tile triggers: 0xB0-0xBF
            // Ground triggers: 0xC0-0xEC
            bool inRanges = (spriteIdx >= 0x80 && spriteIdx <= 0x8C) || spriteIdx == 0x8F ||
                            (spriteIdx >= 0x90 && spriteIdx <= 0x9C) || spriteIdx == 0x9F ||
                            (spriteIdx >= 0xA0 && spriteIdx <= 0xAC) || (spriteIdx >= 0xAE && spriteIdx <= 0xAF) ||
                            (spriteIdx >= 0xB0 && spriteIdx <= 0xBF) ||
                            (spriteIdx >= 0xC0 && spriteIdx <= 0xCC) || spriteIdx == 0xCF ||
                            (spriteIdx >= 0xD0 && spriteIdx <= 0xDC) ||
                            (spriteIdx >= 0xE0 && spriteIdx <= 0xEC);
            if (!inRanges) return false;

            // Disregard specific combinations where low nibble is D/E/F for certain high nibbles
            int low = spriteIdx & 0x0F;
            int high = spriteIdx & 0xF0;
            if (low >= 0xD)
            {
                // Most high-nibble groups with low >= 0xD are excluded, but allow explicit
                // single-value exceptions such as 0x8F and 0xCF which should act as triggers.
                if (high == 0x80 || high == 0x90 || high == 0xA0 || high == 0xC0 || high == 0xD0 || high == 0xE0)
                {
                    if (spriteIdx == 0x8F || spriteIdx == 0xCF)
                    {
                        // explicit exceptions: keep as trigger
                    }
                    else
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private bool IsBackgroundTrigger(int spriteIdx)
        {
            // Include 0x8F as valid background trigger per request
            return (spriteIdx >= 0x80 && spriteIdx <= 0xAC || spriteIdx == 0x8F) && IsColorTriggerSprite(spriteIdx);
        }

        private bool IsTileTrigger(int spriteIdx)
        {
            return spriteIdx >= 0xB0 && spriteIdx <= 0xBF && IsColorTriggerSprite(spriteIdx);
        }

        private bool IsGravityModTrigger(int spriteIdx)
        {
            return spriteIdx >= 0x70 && spriteIdx <= 0x74;
        }

        private bool IsGroundTrigger(int spriteIdx)
        {
            // Include 0xCF as a valid ground trigger per request
            return ((spriteIdx >= 0xC0 && spriteIdx <= 0xEC) || spriteIdx == 0xCF) && IsColorTriggerSprite(spriteIdx);
        }

        private Color ColorFromTrigger(int spriteIdx)
        {
            // Special-case: certain trigger sprites explicitly mean "black" regardless of sampling.
            // Keep sprite-only black triggers here (0x8F and 0xBF). Do NOT special-case 0xCF
            // so ground triggers can be sampled or palette-mapped like other ground tints.
            if (spriteIdx == 0x8F || spriteIdx == 0xBF)
            {
                return Color.FromArgb(255, 0, 0, 0);
            }
            // If this sprite corresponds to a palette cell in the Color Picker, prefer that exact color.
            try
            {
                var palette = PaletteProvider.GetPalette(); // expects 14*4 (or similar)
                try
                {
                    // For background triggers, use the shifted palette index (one row darker) so
                    // the background tint and tile two-tone mapping agree.
                    int? pidx = GetPaletteIndexForBackgroundTrigger(spriteIdx);
                    if (pidx.HasValue && pidx.Value >= 0 && pidx.Value < palette.Length)
                    {
                        var p = palette[pidx.Value];
                        return Color.FromArgb(255, p.R, p.G, p.B);
                    }
                }
                catch { }
            }
            catch { }

            // Prefer sampling the actual sprite/preview image color so the tint matches icon color
            try
            {
                var c = SampleRepresentativeColorFromSprite(spriteIdx);
                if (c.HasValue) return c.Value;
            }
            catch { }

            // Fallback: map low nibble to HSL hue
            int colorIndex = spriteIdx & 0x0F; // 0-15
            double hue = (colorIndex / 16.0) * 360.0;
            return HslToColor(hue, 0.7, 0.45);
        }

        private Color? SampleRepresentativeColorFromSprite(int spriteIdx)
        {
            ImageSource? src = null;
            if (previewSpriteMap != null && previewSpriteMap.TryGetValue(spriteIdx, out var p) && p != null)
                src = p;
            else if (spriteImages != null && spriteIdx >= 0 && spriteIdx < spriteImages.Length)
                src = spriteImages[spriteIdx];

            if (src is BitmapSource bs)
            {
                try
                {
                    var conv = new FormatConvertedBitmap(bs, PixelFormats.Pbgra32, null, 0);
                    int w = Math.Max(1, conv.PixelWidth);
                    int h = Math.Max(1, conv.PixelHeight);
                    int stride = w * 4;
                    var pixels = new byte[h * stride];
                    conv.CopyPixels(pixels, stride, 0);

                    // Search for a non-transparent, non-black pixel. Prefer most frequent color isn't necessary; choose first non-black.
                    for (int i = 0; i < pixels.Length; i += 4)
                    {
                        byte b = pixels[i + 0];
                        byte g = pixels[i + 1];
                        byte r = pixels[i + 2];
                        byte a = pixels[i + 3];
                        if (a > 32)
                        {
                            // ignore fully black pixels (icons often have black outlines)
                            if (!(r == 0 && g == 0 && b == 0))
                            {
                                return Color.FromArgb(255, r, g, b);
                            }
                        }
                    }
                }
                catch { }
            }
            return null;
        }

        // Determine whether a bitmap's visible pixels are concentrated near the top of the image.
        // This heuristic allows detecting upside-down halves (content at top) vs normal bottom-aligned halves.
        private bool IsImageTopHeavy(BitmapSource bs)
        {
            try
            {
                if (bs == null) return false;
                int key = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(bs);
                if (imageTopHeavyCache.TryGetValue(key, out var val)) return val;

                var conv = new FormatConvertedBitmap(bs, PixelFormats.Pbgra32, null, 0);
                int w = Math.Max(1, conv.PixelWidth);
                int h = Math.Max(1, conv.PixelHeight);
                int stride = w * 4;
                var pixels = new byte[h * stride];
                conv.CopyPixels(pixels, stride, 0);

                long sumY = 0;
                int count = 0;
                // Sample at most a limited number of pixels for performance.
                int maxSamples = 4000;
                for (int y = 0; y < h && count < maxSamples; y++)
                {
                    int rowStart = y * stride;
                    for (int x = 0; x < w && count < maxSamples; x++)
                    {
                        int i = rowStart + x * 4;
                        byte a = pixels[i + 3];
                        if (a > 32)
                        {
                            // count this pixel
                            sumY += y;
                            count++;
                        }
                    }
                }

                bool topHeavy = false;
                if (count > 0)
                {
                    double avgY = (double)sumY / (double)count;
                    // If the average non-transparent pixel Y is in the top ~45% of the image,
                    // consider the image top-heavy (likely upside-down bottom half).
                    topHeavy = avgY < (h * 0.45);
                }

                imageTopHeavyCache[key] = topHeavy;
                return topHeavy;
            }
            catch { return false; }
        }

        private Color HslToColor(double h, double s, double l)
        {
            // h in [0,360), s,l in [0,1]
            double c = (1 - Math.Abs(2 * l - 1)) * s;
            double hh = h / 60.0;
            double x = c * (1 - Math.Abs((hh % 2) - 1));
            double r1 = 0, g1 = 0, b1 = 0;
            if (0 <= hh && hh < 1) { r1 = c; g1 = x; b1 = 0; }
            else if (1 <= hh && hh < 2) { r1 = x; g1 = c; b1 = 0; }
            else if (2 <= hh && hh < 3) { r1 = 0; g1 = c; b1 = x; }
            else if (3 <= hh && hh < 4) { r1 = 0; g1 = x; b1 = c; }
            else if (4 <= hh && hh < 5) { r1 = x; g1 = 0; b1 = c; }
            else { r1 = c; g1 = 0; b1 = x; }
            double m = l - c / 2.0;
            byte r = (byte)Math.Round((r1 + m) * 255.0);
            byte g = (byte)Math.Round((g1 + m) * 255.0);
            byte b = (byte)Math.Round((b1 + m) * 255.0);
            return Color.FromArgb(255, r, g, b);
        }

        // Map a starting color code (0x00-0x2C or 0x0F) to the corresponding trigger sprite id.
        // Background mapping: 0x00-0x0C -> 0x80-0x8C, 0x10-0x1C -> 0x90-0x9C, 0x20-0x2C -> 0xA0-0xAC
        // Ground mapping: same ranges but mapped to 0xC0/0xD0/0xE0 groups respectively
        // Special-case: 0x0F -> 0x8F for background, 0xCF for ground.
        private int MapStartingCodeToTrigger(int code, bool isGround)
        {
            try
            {
                if (code == 0x0F)
                {
                    return isGround ? 0xCF : 0x8F;
                }
                int low = code & 0x0F;
                int high = code & 0xF0;
                if (high == 0x00)
                {
                    return (isGround ? 0xC0 : 0x80) + low;
                }
                if (high == 0x10)
                {
                    return (isGround ? 0xD0 : 0x90) + low;
                }
                if (high == 0x20)
                {
                    return (isGround ? 0xE0 : 0xA0) + low;
                }
            }
            catch { }
            return isGround ? 0xC0 : 0x80; // fallback
        }

        // Given a background trigger sprite id, return the corresponding palette index (or null).
        // Mirrors the mapping used by ColorFromTrigger but returns the numeric palette index instead of a color.
        // Given a background trigger sprite id, return the corresponding palette index (or null).
        // This mapping returns the exact palette index that corresponds to the trigger sprite
        // (no automatic up-row shifting). Callers may choose to compute a darker/lighter
        // variant explicitly if desired.
        private int? GetPaletteIndexForBackgroundTrigger(int spriteIdx)
        {
            try
            {
                int idx = -1;
                if (spriteIdx >= 0x80 && spriteIdx <= 0x8C) idx = (spriteIdx - 0x80) + (0 * 14);
                else if (spriteIdx >= 0x90 && spriteIdx <= 0x9C) idx = (spriteIdx - 0x90) + (1 * 14);
                else if (spriteIdx >= 0xA0 && spriteIdx <= 0xAC) idx = (spriteIdx - 0xA0) + (2 * 14);
                else if (spriteIdx >= 0xC0 && spriteIdx <= 0xCC) idx = (spriteIdx - 0xC0) + (0 * 14);
                else if (spriteIdx >= 0xD0 && spriteIdx <= 0xDC) idx = (spriteIdx - 0xD0) + (1 * 14);
                else if (spriteIdx >= 0xE0 && spriteIdx <= 0xEC) idx = (spriteIdx - 0xE0) + (2 * 14);

                if (idx >= 0) return idx;
            }
            catch { }
            return null;
        }

        // (Deprecated: previous implementation shifted selection to the row above.)

        // Create two-tone tile images: non-white pixels are classified into lighter/darker groups and
        // mapped to bgPrimary/bgSecondary respectively; white/near-white outlines are mapped to outlineTint.
        private ImageSource?[]? CreateTwoToneTileImages(ImageSource?[]? originals, Color bgPrimary, Color bgSecondary, Color outlineTint)
        {
            if (originals == null) return null;
            var outList = new System.Collections.Generic.List<ImageSource>(originals.Length);
            foreach (var src in originals)
            {
                if (src == null)
                {
                    var pf = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Pbgra32, null);
                    var pxf = new byte[4];
                    try { pf.WritePixels(new Int32Rect(0, 0, 1, 1), pxf, 4, 0); } catch { }
                    try { pf.Freeze(); } catch { }
                    outList.Add(pf);
                    continue;
                }
                if (src is BitmapSource bs)
                {
                    try
                    {
                        var conv = new FormatConvertedBitmap(bs, PixelFormats.Pbgra32, null, 0);
                        int w = conv.PixelWidth; int h = conv.PixelHeight; int stride = w * 4;
                        var pixels = new byte[h * stride];
                        conv.CopyPixels(pixels, stride, 0);

                        // Compute min/max luminance of non-white, non-transparent pixels to determine threshold
                        double minL = 1.0, maxL = 0.0; int count = 0;
                        for (int i = 0; i < pixels.Length; i += 4)
                        {
                            byte b = pixels[i + 0]; byte g = pixels[i + 1]; byte r = pixels[i + 2]; byte a = pixels[i + 3];
                            if (a == 0) continue;
                            double lum = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;
                            // Skip near-white when computing min/max (use 0.82 to catch anti-aliased outlines)
                            if (lum >= 0.82) continue;
                            if (lum < minL) minL = lum;
                            if (lum > maxL) maxL = lum;
                            count++;
                        }
                        double threshold = (count > 0) ? ((minL + maxL) / 2.0) : 0.5;

                        // Determine a white-detection threshold. If the outline tint is pure opaque black
                        // (object trigger like 0xBF), be more aggressive in treating light pixels as "white"
                        // so object-black triggers recolor thin/anti-aliased whites properly.
                        double whiteThreshold = 0.82;
                        if (outlineTint.A == 255 && outlineTint.R == 0 && outlineTint.G == 0 && outlineTint.B == 0)
                        {
                            whiteThreshold = 0.70; // treat more pixels as white when applying black outline tint
                        }

                        for (int i = 0; i < pixels.Length; i += 4)
                        {
                            byte ob = pixels[i + 0]; byte og = pixels[i + 1]; byte orr = pixels[i + 2]; byte a = pixels[i + 3];
                            if (a == 0) continue;
                            double lum = (0.2126 * orr + 0.7152 * og + 0.0722 * ob) / 255.0;
                            bool isWhite = lum >= whiteThreshold;
                            bool isBlack = (orr <= 12 && og <= 12 && ob <= 12);

                            if (isBlack)
                            {
                                // Preserve true black pixels unchanged (do not tint blacks with background)
                                continue;
                            }

                            if (isWhite)
                            {
                                // Only recolor white outlines if an explicit outline tint is provided (alpha>0).
                                if (outlineTint.A > 0)
                                {
                                    pixels[i + 3] = 255; // ensure opaque when recoloring
                                    pixels[i + 2] = outlineTint.R;
                                    pixels[i + 1] = outlineTint.G;
                                    pixels[i + 0] = outlineTint.B;
                                }
                                else
                                {
                                    // leave white as-is
                                    continue;
                                }
                            }
                            else
                            {
                                // Map into primary/secondary based on luminance threshold
                                Color target = (lum >= threshold) ? bgPrimary : bgSecondary;
                                pixels[i + 3] = 255;
                                pixels[i + 2] = target.R;
                                pixels[i + 1] = target.G;
                                pixels[i + 0] = target.B;
                            }
                        }

                        var wb = new WriteableBitmap(w, h, conv.DpiX, conv.DpiY, PixelFormats.Pbgra32, null);
                        wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
                        // Do not freeze WriteableBitmap; it must remain writable for Lock/WritePixels.
                        outList.Add(wb);
                    }
                    catch
                    {
                            if (src != null) outList.Add(src);
                            else outList.Add(new WriteableBitmap(1, 1, 96, 96, PixelFormats.Pbgra32, null));
                    }
                }
                else
                {
                        if (src != null) outList.Add(src);
                        else outList.Add(new WriteableBitmap(1, 1, 96, 96, PixelFormats.Pbgra32, null));
                }
            }
            return outList.ToArray();
        }

        // Create tile images where only near-white outline pixels are replaced by outlineTint while
        // keeping other pixels unchanged.
        private ImageSource?[]? CreateOutlineTintedTileImages(ImageSource?[]? originals, Color outlineTint)
        {
            if (originals == null) return null;
            var outList = new System.Collections.Generic.List<ImageSource>(originals.Length);
            foreach (var src in originals)
            {
                if (src == null)
                {
                    var pf = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Pbgra32, null);
                    var pxf = new byte[4];
                    try { pf.WritePixels(new Int32Rect(0, 0, 1, 1), pxf, 4, 0); } catch { }
                    try { pf.Freeze(); } catch { }
                    outList.Add(pf);
                    continue;
                }
                if (src is BitmapSource bs)
                {
                    try
                    {
                        var conv = new FormatConvertedBitmap(bs, PixelFormats.Pbgra32, null, 0);
                        int w = conv.PixelWidth; int h = conv.PixelHeight; int stride = w * 4;
                        var pixels = new byte[h * stride];
                        conv.CopyPixels(pixels, stride, 0);

                        // Choose a white-detection threshold. Be more aggressive when outline tint is
                        // pure opaque black so object-black triggers recolor a wider set of light pixels.
                        double outlineWhiteThreshold = 0.82;
                        if (outlineTint.A == 255 && outlineTint.R == 0 && outlineTint.G == 0 && outlineTint.B == 0) outlineWhiteThreshold = 0.70;
                        for (int i = 0; i < pixels.Length; i += 4)
                        {
                            byte b = pixels[i + 0]; byte g = pixels[i + 1]; byte r = pixels[i + 2]; byte a = pixels[i + 3];
                            if (a == 0) continue;
                            double lum = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;
                            bool isWhite = lum >= outlineWhiteThreshold;
                            if (isWhite)
                            {
                                pixels[i + 3] = 255;
                                pixels[i + 2] = outlineTint.R;
                                pixels[i + 1] = outlineTint.G;
                                pixels[i + 0] = outlineTint.B;
                            }
                        }

                        var wb = new WriteableBitmap(w, h, conv.DpiX, conv.DpiY, PixelFormats.Pbgra32, null);
                        wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
                        // Do not freeze WriteableBitmap; it must remain writable for Lock/WritePixels.
                        outList.Add(wb);
                    }
                    catch { outList.Add(src); }
                }
                else outList.Add(src);
            }
            return outList.ToArray();
        }

        // Helper: compare colors (treat nullability not applicable here)
        private static bool AreColorsEqual(Color a, Color b)
        {
            return a.A == b.A && a.R == b.R && a.G == b.G && a.B == b.B;
        }

        // Return a player-tinted copy of the given sprite image (cached).
        private ImageSource? GetPlayerTintedSprite(ImageSource src, int spriteId)
        {
            if (!playerTintEnabled) return src;
            try
            {
                // Include the source image identity in the cache key so different frames
                // of the same sprite id don't collapse to the same cached tinted image.
                int srcHash = src != null ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(src) : 0;
                long key = (((long)spriteId) << 48) | (((long)srcHash & 0xFFFF) << 32) | ((long)playerTint.A << 24) | ((long)playerTint.R << 16) | ((long)playerTint.G << 8) | playerTint.B;
                if (tintedSpriteCache.TryGetValue(key, out var cached)) return cached;

                if (src is BitmapSource bs)
                {
                    var conv = new FormatConvertedBitmap(bs, PixelFormats.Pbgra32, null, 0);
                    int w = Math.Max(1, conv.PixelWidth);
                    int h = Math.Max(1, conv.PixelHeight);
                    int stride = w * 4;
                    var pixels = new byte[h * stride];
                    conv.CopyPixels(pixels, stride, 0);

                    // compute tint hue
                    RgbToHsl(playerTint.R, playerTint.G, playerTint.B, out double tintH, out double tintS, out double tintL);

                    for (int i = 0; i < pixels.Length; i += 4)
                    {
                        byte b = pixels[i + 0];
                        byte g = pixels[i + 1];
                        byte r = pixels[i + 2];
                        byte a = pixels[i + 3];
                        if (a == 0) continue; // preserve fully transparent pixels

                        // Replace hue with tint hue while preserving original saturation/lightness
                        RgbToHsl(r, g, b, out double ph, out double ps, out double pl);
                        double nh = tintH; double ns = ps; double nl = pl;
                        RgbFromHsl(nh, ns, nl, out byte nr, out byte ng, out byte nb);
                        pixels[i + 0] = nb;
                        pixels[i + 1] = ng;
                        pixels[i + 2] = nr;
                        // alpha unchanged
                    }

                    var wb = new WriteableBitmap(w, h, conv.DpiX, conv.DpiY, PixelFormats.Pbgra32, null);
                    wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
                    // Do not freeze WriteableBitmap; it must remain writable for Lock/WritePixels.
                    tintedSpriteCache[key] = wb;
                    return wb;
                }
                else
                {
                    tintedSpriteCache[key] = src;
                    return src;
                }
            }
            catch
            {
                return src;
            }
        }

        // Composite a sprite image over the rendered tile layer (if available) at the given
        // destination coordinates so transparent areas show the exact underlying pixels.
        // Falls back to compositing over a flat `bg` color when the tile layer is unavailable.
        private ImageSource? CompositeSpriteOverBackgroundAt(ImageSource src, Color bg, int destX, int destY)
        {
            if (src == null) return null;
            if (!(src is BitmapSource bs)) return src;
            try
            {
                int srcHash = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(bs);
                // Include animationFrame and tile-layer canvas offsets so cached composites
                // update when animated tiles/frame or fractional camera offsets change.
                int layerLeft = 0, layerTop = 0;
                try { if (tileLayerImage != null) { var v = System.Windows.Controls.Canvas.GetLeft(tileLayerImage); if (!double.IsNaN(v)) layerLeft = (int)Math.Round(v); var t = System.Windows.Controls.Canvas.GetTop(tileLayerImage); if (!double.IsNaN(t)) layerTop = (int)Math.Round(t); } } catch { }
                string key = string.Format("{0}:{1:X2}{2:X2}{3:X2}{4:X2}:{5}:{6}:{7}:{8}:{9}", srcHash, bg.A, bg.R, bg.G, bg.B, destX, destY, animationFrame, layerLeft, layerTop);
                if (spriteBackgroundCompositeCache.TryGetValue(key, out var cached)) return cached;

                // Use WPF rendering to compose the sprite over the background region. This
                // ensures correct alpha blending and avoids subtle premultiplication issues
                // that can cause semi-transparent pixels to render the wrong color.
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    // Draw background: either the cropped tile-layer region or a flat color
                    if (tileLayerCache is BitmapSource layer)
                    {
                        try
                        {
                            int layerW = layer.PixelWidth;
                            int layerH = layer.PixelHeight;
                            int srcLeft = destX - layerLeft;
                            int srcTop = destY - layerTop;
                            int ovLeft = Math.Max(0, srcLeft);
                            int ovTop = Math.Max(0, srcTop);
                            int ovRight = Math.Min(layerW, srcLeft + Math.Max(1, (int)bs.PixelWidth));
                            int ovBottom = Math.Min(layerH, srcTop + Math.Max(1, (int)bs.PixelHeight));
                            int ovW = Math.Max(0, ovRight - ovLeft);
                            int ovH = Math.Max(0, ovBottom - ovTop);
                            if (ovW > 0 && ovH > 0)
                            {
                                try
                                {
                                    var cropped = new CroppedBitmap(layer, new Int32Rect(ovLeft, ovTop, ovW, ovH));
                                    var brushImg = App.EnsureUnfrozenForRender(cropped) ?? cropped;
                                    var brush = new ImageBrush(brushImg) { Stretch = Stretch.None, TileMode = TileMode.None };
                                    // Draw the full visible tile layer translated into sprite-local coordinates
                                    // so transparent sprite pixels reveal the exact rendered pixels beneath them.
                                    // If the full visible layer isn't available, fall back to the persistent
                                    // background brush (parallax ImageBrush) or a flat color as before.
                                    bool drewFullLayer = false;
                                    try
                                    {
                                        if (tileLayerImage != null && tileLayerImage.Source is BitmapSource fullLayer)
                                        {
                                            double tx = layerLeft - destX;
                                            double ty = layerTop - destY;
                                            dc.PushTransform(new TranslateTransform(tx, ty));
                                            var drawFull = App.EnsureUnfrozenForRender(fullLayer) ?? fullLayer;
                                            dc.DrawImage(drawFull, new Rect(0, 0, fullLayer.PixelWidth, fullLayer.PixelHeight));
                                            dc.Pop();
                                            drewFullLayer = true;
                                        }
                                    }
                                    catch { drewFullLayer = false; }

                                    if (!drewFullLayer)
                                    {
                                        // If we couldn't draw the full tile layer, fall back to the persistent background
                                        // brush or a flat color. Prefer the persistent background so parallax/ground
                                        // visuals still show through transparent sprite pixels.
                                        Brush? bgBrush = null;
                                        try { if (bgRectPersistent != null && bgRectPersistent.Fill != null) bgBrush = bgRectPersistent.Fill; } catch { bgBrush = null; }
                                        if (bgBrush == null)
                                        {
                                            bgBrush = new SolidColorBrush(Color.FromArgb(bg.A, bg.R, bg.G, bg.B));
                                            dc.DrawRectangle(bgBrush ?? new SolidColorBrush(Color.FromArgb(bg.A, bg.R, bg.G, bg.B)), null, new Rect(0, 0, bs.PixelWidth, bs.PixelHeight));
                                        }
                                        else
                                        {
                                            try
                                            {
                                                if (bgBrush is ImageBrush ib)
                                                {
                                                    var srcImg = ib.ImageSource;
                                                    // compute palette-accurate darker version for consistency with tile tinting
                                                    Color darkerBg = bg;
                                                    try
                                                    {
                                                        var palette = PaletteProvider.GetPalette();
                                                        int? nearest = GetNearestPaletteIndexForColor(bg, palette);
                                                        if (nearest.HasValue)
                                                        {
                                                            int col = nearest.Value % 14;
                                                            int row = nearest.Value / 14;
                                                            int finalIdx = row * 14 + (col > 12 ? 12 : col);
                                                            if (finalIdx >= 0 && finalIdx < palette.Length) darkerBg = Color.FromArgb(bg.A, palette[finalIdx].R, palette[finalIdx].G, palette[finalIdx].B);
                                                        }
                                                        else
                                                        {
                                                            RgbToHsl(bg.R, bg.G, bg.B, out double hh, out double ss, out double ll);
                                                            ll = Math.Max(0.0, ll - 0.12);
                                                            RgbFromHsl(hh, ss, ll, out byte dr, out byte dg, out byte db);
                                                            darkerBg = Color.FromArgb(bg.A, dr, dg, db);
                                                        }
                                                    }
                                                    catch { }
                                                    ImageSource useImg = srcImg ?? new WriteableBitmap(1, 1, 96, 96, PixelFormats.Pbgra32, null);
                                                    try { if (srcImg != null) { var arr = CreateHslShiftedImages(new ImageSource?[] { srcImg! }, darkerBg); if (arr != null && arr.Length > 0 && arr[0] != null) useImg = arr[0]!; } } catch { }
                                                    var newIbImg = App.EnsureUnfrozenForRender(useImg) ?? useImg;
                                                    var newIb = new ImageBrush(newIbImg)
                                                    {
                                                        Stretch = ib.Stretch,
                                                        TileMode = ib.TileMode,
                                                        Viewport = ib.Viewport,
                                                        ViewportUnits = ib.ViewportUnits,
                                                    };
                                                    double ox = 0, oy = 0;
                                                    try { if (ib.Transform is TranslateTransform tt) { ox = tt.X; oy = tt.Y; } else if (ib.Transform is MatrixTransform mt) { ox = mt.Matrix.OffsetX; oy = mt.Matrix.OffsetY; } } catch { }
                                                    newIb.Transform = new TranslateTransform(ox - destX, oy - destY);
                                                    dc.DrawRectangle(newIb, null, new Rect(0, 0, bs.PixelWidth, bs.PixelHeight));
                                                }
                                                else
                                                {
                                                    dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(bg.A, bg.R, bg.G, bg.B)), null, new Rect(0, 0, bs.PixelWidth, bs.PixelHeight));
                                                }
                                            }
                                            catch
                                            {
                                                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(bg.A, bg.R, bg.G, bg.B)), null, new Rect(0, 0, bs.PixelWidth, bs.PixelHeight));
                                            }
                                        }
                                    }

                                    // Draw the cropped content on top at the appropriate offset (if present)
                                    int dx = ovLeft - srcLeft;
                                    int dy = ovTop - srcTop;
                                    dc.PushTransform(new TranslateTransform(-dx, -dy));
                                    dc.DrawRectangle(brush, null, new Rect(dx, dy, ovW, ovH));
                                    dc.Pop();
                                }
                                catch
                                {
                                    Brush? bgBrush2 = null;
                                    try { if (bgRectPersistent != null && bgRectPersistent.Fill != null) bgBrush2 = bgRectPersistent.Fill; } catch { bgBrush2 = null; }
                                    if (bgBrush2 == null)
                                    {
                                        bgBrush2 = new SolidColorBrush(Color.FromArgb(bg.A, bg.R, bg.G, bg.B));
                                    }
                                    else
                                    {
                                        try
                                        {
                                            if (bgBrush2 is ImageBrush ib2)
                                            {
                                                var srcImg2 = ib2.ImageSource;
                                                // compute darker version for consistency with tile tinting
                                                    Color darkerBg2 = bg;
                                                    try
                                                    {
                                                        var palette2 = PaletteProvider.GetPalette();
                                                        int? nearest2 = GetNearestPaletteIndexForColor(bg, palette2);
                                                        if (nearest2.HasValue)
                                                        {
                                                            int col2 = nearest2.Value % 14;
                                                            int row2 = nearest2.Value / 14;
                                                            int finalIdx2 = row2 * 14 + (col2 > 12 ? 12 : col2);
                                                            if (finalIdx2 >= 0 && finalIdx2 < palette2.Length) darkerBg2 = Color.FromArgb(bg.A, palette2[finalIdx2].R, palette2[finalIdx2].G, palette2[finalIdx2].B);
                                                        }
                                                        else
                                                        {
                                                            RgbToHsl(bg.R, bg.G, bg.B, out double hh2, out double ss2, out double ll2);
                                                            ll2 = Math.Max(0.0, ll2 - 0.12);
                                                            RgbFromHsl(hh2, ss2, ll2, out byte dr2, out byte dg2, out byte db2);
                                                            darkerBg2 = Color.FromArgb(bg.A, dr2, dg2, db2);
                                                        }
                                                    }
                                                    catch { }
                                                    ImageSource useImg2 = srcImg2 ?? new WriteableBitmap(1, 1, 96, 96, PixelFormats.Pbgra32, null);
                                                    try { if (srcImg2 != null) { var arr2 = CreateHslShiftedImages(new ImageSource?[] { srcImg2! }, darkerBg2); if (arr2 != null && arr2.Length > 0 && arr2[0] != null) useImg2 = arr2[0]!; } } catch { }
                                                var newIb2Img = App.EnsureUnfrozenForRender(useImg2) ?? useImg2;
                                                var newIb2 = new ImageBrush(newIb2Img)
                                                {
                                                    Stretch = ib2.Stretch,
                                                    TileMode = ib2.TileMode,
                                                    Viewport = ib2.Viewport,
                                                    ViewportUnits = ib2.ViewportUnits,
                                                };
                                                double ox2 = 0, oy2 = 0;
                                                try { if (ib2.Transform is TranslateTransform tt2) { ox2 = tt2.X; oy2 = tt2.Y; } else if (ib2.Transform is MatrixTransform mt2) { ox2 = mt2.Matrix.OffsetX; oy2 = mt2.Matrix.OffsetY; } } catch { }
                                                newIb2.Transform = new TranslateTransform(ox2 - destX, oy2 - destY);
                                                bgBrush2 = newIb2;
                                            }
                                        }
                                        catch { }
                                    }
                                        dc.DrawRectangle(bgBrush2 ?? new SolidColorBrush(Color.FromArgb(bg.A, bg.R, bg.G, bg.B)), null, new Rect(0, 0, bs.PixelWidth, bs.PixelHeight));
                                }
                            }
                            else
                            {
                                // Cropping the local tile layer cache returned nothing. Attempt to draw from the
                                // visible tileLayerImage.Source (full layer) so decorations still composite correctly
                                // even when the cached region doesn't cover the sprite.
                                try
                                {
                                    if (tileLayerImage != null && tileLayerImage.Source is BitmapSource fullLayer)
                                    {
                                        // Translate so that the sprite-local (destX,destY) region of the full layer
                                        // maps to (0,0) in the drawing visual.
                                        double tx = layerLeft - destX;
                                        double ty = layerTop - destY;
                                        dc.PushTransform(new TranslateTransform(tx, ty));
                                        var drawFull2 = App.EnsureUnfrozenForRender(fullLayer) ?? fullLayer;
                                        dc.DrawImage(drawFull2, new Rect(0, 0, fullLayer.PixelWidth, fullLayer.PixelHeight));
                                        dc.Pop();
                                    }
                                    else
                                    {
                                        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(bg.A, bg.R, bg.G, bg.B)), null, new Rect(0, 0, bs.PixelWidth, bs.PixelHeight));
                                    }
                                }
                                catch
                                {
                                    Brush? bgBrush3 = null;
                                    try { if (bgRectPersistent != null && bgRectPersistent.Fill != null) bgBrush3 = bgRectPersistent.Fill; } catch { bgBrush3 = null; }
                                    if (bgBrush3 == null)
                                    {
                                        bgBrush3 = new SolidColorBrush(Color.FromArgb(bg.A, bg.R, bg.G, bg.B));
                                    }
                                    else
                                    {
                                        try
                                        {
                                            if (bgBrush3 is ImageBrush ib3)
                                            {
                                                var srcImg3 = ib3.ImageSource;
                                                Color darkerBg3 = bg;
                                                try { RgbToHsl(bg.R, bg.G, bg.B, out double hh3, out double ss3, out double ll3); ll3 = Math.Max(0.0, ll3 - 0.12); RgbFromHsl(hh3, ss3, ll3, out byte dr3, out byte dg3, out byte db3); darkerBg3 = Color.FromArgb(bg.A, dr3, dg3, db3); } catch { }
                                                ImageSource useImg3 = srcImg3 ?? new WriteableBitmap(1, 1, 96, 96, PixelFormats.Pbgra32, null);
                                                try { if (srcImg3 != null) { var arr3 = CreateHslShiftedImages(new ImageSource?[] { srcImg3! }, darkerBg3); if (arr3 != null && arr3.Length > 0 && arr3[0] != null) useImg3 = arr3[0]!; } } catch { }
                                                var newIb3Img = App.EnsureUnfrozenForRender(useImg3) ?? useImg3;
                                                var newIb3 = new ImageBrush(newIb3Img)
                                                {
                                                    Stretch = ib3.Stretch,
                                                    TileMode = ib3.TileMode,
                                                    Viewport = ib3.Viewport,
                                                    ViewportUnits = ib3.ViewportUnits,
                                                };
                                                double ox3 = 0, oy3 = 0;
                                                try { if (ib3.Transform is TranslateTransform tt3) { ox3 = tt3.X; oy3 = tt3.Y; } else if (ib3.Transform is MatrixTransform mt3) { ox3 = mt3.Matrix.OffsetX; oy3 = mt3.Matrix.OffsetY; } } catch { }
                                                newIb3.Transform = new TranslateTransform(ox3 - destX, oy3 - destY);
                                                bgBrush3 = newIb3;
                                            }
                                        }
                                        catch { }
                                    }
                                    dc.DrawRectangle(bgBrush3 ?? new SolidColorBrush(Color.FromArgb(bg.A, bg.R, bg.G, bg.B)), null, new Rect(0, 0, bs.PixelWidth, bs.PixelHeight));
                                }
                            }
                        }
                        catch
                        {
                            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(bg.A, bg.R, bg.G, bg.B)), null, new Rect(0, 0, bs.PixelWidth, bs.PixelHeight));
                        }
                    }
                    else
                    {
                        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(bg.A, bg.R, bg.G, bg.B)), null, new Rect(0, 0, bs.PixelWidth, bs.PixelHeight));
                    }

                    // Draw the sprite on top; WPF will handle alpha blending correctly
                    var drawBs = App.EnsureUnfrozenForRender(bs) ?? bs;
                    dc.DrawImage(drawBs, new Rect(0, 0, bs.PixelWidth, bs.PixelHeight));
                }

                var rtb = new RenderTargetBitmap((int)bs.PixelWidth, (int)bs.PixelHeight, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);
                rtb.Freeze();
                spriteBackgroundCompositeCache[key] = rtb;
                return rtb;
            }
            catch { return src; }
        }

        // Regenerate toned images for tiles and saw frames using HSL hue shifting.
        // This mirrors MainWindow.UpdateTileTint's behavior so simulator can apply tints locally.
        private void UpdateTonedImagesForTileTint(Color newTileTint, Color? outlineOverride = null)
        {
            try
            {
                // Create HSL-shifted copies for tiles and saw frames
                // Allow caller to override the outline tint (used to keep outlines uncolored at startup).
                // If outlineOverride is null, fall back to the current `tileTint` member.
                Color outline = outlineOverride.HasValue ? outlineOverride.Value : tileTint;
                if (tileImages != null)
                {
                    if (backgroundForceSolidBlack)
                    {
                        try { tileTonedImages = CreateBlackMaskedExceptColorArray(tileImages, playerPlaceholderGreen, outline, false); }
                        catch { tileTonedImages = CreateHslShiftedImages(tileImages, newTileTint, outline); }
                    }
                    else
                    {
                        tileTonedImages = CreateHslShiftedImages(tileImages, newTileTint, outline);
                    }
                }

                // For a small set of tiles that contain decorative green pixels (#5ACE52),
                // ensure those pixels are not affected by tile/bg/obj tints and instead
                // receive only the player tint. Post-process the generated `tileTonedImages`
                // so the green pixels from the original tile images are replaced by `playerTint`.
                try
                {
                    if (tileTonedImages != null && tileImages != null)
                    {
                        int[] special = new int[] { 0x0C, 0x0D, 0x0E, 0x0F, 0x13, 0x14, 0x80, 0x81, 0x84, 0x85, 0x86, 0x87 };
                        foreach (var si in special)
                        {
                            if (si >= 0 && si < tileTonedImages.Length && si >= 0 && si < tileImages.Length && tileTonedImages[si] != null && tileImages[si] != null)
                            {
                                try { tileTonedImages[si] = ApplyPlayerTintToGreenPixels(tileTonedImages[si], tileImages[si], playerTint); } catch { }
                            }
                        }
                    }
                }
                catch { }

                if (sawFrame1TilesTinted != null && sawFrame1TilesTinted.Length > 0)
                {
                    // If we already had tinted saw frames passed in, re-tint the original saw frames
                    // Fallback: if original arrays are null, do nothing
                }

                // NOTE: saw frames are intentionally NOT retinted here from tile tint.
                // Saws should respond only to background color triggers (handled elsewhere).
            }
            catch { }
        }

        // Create a black-masked copy of a single image: non-white pixels become black.
        // If `outlineTint` is provided (alpha>0) and `recolorOutline` is true, near-white
        // pixels will be recolored to that tint so object-outline recoloring continues to
        // work when appropriate. When `recolorOutline` is false the top seam/near-white
        // pixels are left untouched so another system (object tinting) can control them.
        private ImageSource? CreateBlackMaskedImage(ImageSource? src, Color outlineTint = default, bool recolorOutline = true)
        {
            if (src == null) return null;
            if (!(src is BitmapSource bs)) return src;
            try
            {
                var conv = new FormatConvertedBitmap(bs, PixelFormats.Pbgra32, null, 0);
                int w = Math.Max(1, conv.PixelWidth);
                int h = Math.Max(1, conv.PixelHeight);
                int stride = w * 4;
                var pixels = new byte[h * stride];
                conv.CopyPixels(pixels, stride, 0);
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    byte b = pixels[i + 0];
                    byte g = pixels[i + 1];
                    byte r = pixels[i + 2];
                    byte a = pixels[i + 3];
                    if (a == 0) continue;
                    // Use perceptual luminance to detect near-white (preserve thin white lines
                    // and anti-aliased edge pixels).
                    double lum = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;
                    bool isWhite = lum >= 0.82;
                    if (isWhite)
                    {
                        if (recolorOutline && outlineTint.A > 0)
                        {
                            // Recolor near-white pixels to the outline tint so object tints still apply.
                            pixels[i + 3] = 255;
                            pixels[i + 2] = outlineTint.R;
                            pixels[i + 1] = outlineTint.G;
                            pixels[i + 0] = outlineTint.B;
                        }
                        else
                        {
                            // Preserve original near-white pixel color to avoid overwriting downstream tinting.
                            continue;
                        }
                    }
                    else
                    {
                        // Make pixel opaque black
                        pixels[i + 0] = 0;
                        pixels[i + 1] = 0;
                        pixels[i + 2] = 0;
                        pixels[i + 3] = 255;
                    }
                }
                var wb = new WriteableBitmap(w, h, conv.DpiX, conv.DpiY, PixelFormats.Pbgra32, null);
                wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
                // Do not freeze WriteableBitmap; it must remain writable for Lock/WritePixels.
                return wb;
            }
            catch { return src; }
        }

        // Create a black-masked copy but preserve pixels matching `excludeColor` (exact RGB match)
        // and preserve near-white outline pixels unless recolorOutline==true.
        private ImageSource? CreateBlackMaskedExceptColor(ImageSource? src, Color excludeColor, Color outlineTint = default, bool recolorOutline = true)
        {
            if (src == null) return null;
            if (!(src is BitmapSource bs)) return src;
            try
            {
                var conv = new FormatConvertedBitmap(bs, PixelFormats.Pbgra32, null, 0);
                int w = Math.Max(1, conv.PixelWidth);
                int h = Math.Max(1, conv.PixelHeight);
                int stride = w * 4;
                var pixels = new byte[h * stride];
                conv.CopyPixels(pixels, stride, 0);
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    byte b = pixels[i + 0];
                    byte g = pixels[i + 1];
                    byte r = pixels[i + 2];
                    byte a = pixels[i + 3];
                    if (a == 0) continue;
                    // Preserve exact exclude color (e.g., player placeholder green #FF322B)
                    if (r == excludeColor.R && g == excludeColor.G && b == excludeColor.B) continue;
                    double lum = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;
                    bool isWhite = (lum >= 0.82);
                    if (isWhite)
                    {
                        if (recolorOutline && outlineTint.A > 0)
                        {
                            pixels[i + 3] = 255;
                            pixels[i + 2] = outlineTint.R;
                            pixels[i + 1] = outlineTint.G;
                            pixels[i + 0] = outlineTint.B;
                        }
                        else
                        {
                            continue;
                        }
                    }
                    else
                    {
                        // Make pixel opaque black
                        pixels[i + 0] = 0; pixels[i + 1] = 0; pixels[i + 2] = 0; pixels[i + 3] = 255;
                    }
                }
                var wb = new WriteableBitmap(w, h, conv.DpiX, conv.DpiY, PixelFormats.Pbgra32, null);
                wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
                // Do not freeze WriteableBitmap; it must remain writable for Lock/WritePixels.
                return wb;
            }
            catch { return src; }
        }

        // Recolor exact-match green pixels (#5ACE52) from the original image to the given playerTint
        // in the provided source image. This preserves the pixel layout from `src` while ensuring
        // only the special green areas receive the player tint color.
        private ImageSource? ApplyPlayerTintToGreenPixels(ImageSource? src, ImageSource? original, Color playerTint)
        {
            if (src == null || original == null) return src;
            if (!(src is BitmapSource sb) || !(original is BitmapSource ob)) return src;

            try
            {
                var convSrc = new FormatConvertedBitmap(sb, PixelFormats.Pbgra32, null, 0);
                var convOrig = new FormatConvertedBitmap(ob, PixelFormats.Pbgra32, null, 0);
                int w = Math.Max(1, convSrc.PixelWidth);
                int h = Math.Max(1, convSrc.PixelHeight);
                if (convOrig.PixelWidth != w || convOrig.PixelHeight != h) return src;
                int stride = w * 4;
                var pixelsSrc = new byte[h * stride];
                var pixelsOrig = new byte[h * stride];
                convSrc.CopyPixels(pixelsSrc, stride, 0);
                convOrig.CopyPixels(pixelsOrig, stride, 0);

                // special green RGB
                byte gR = 0x5A; byte gG = 0xCE; byte gB = 0x52; // #5ACE52

                for (int i = 0; i < pixelsSrc.Length; i += 4)
                {
                    byte ob_b = pixelsOrig[i + 0];
                    byte ob_g = pixelsOrig[i + 1];
                    byte ob_r = pixelsOrig[i + 2];
                    byte ob_a = pixelsOrig[i + 3];
                    if (ob_a == 0) continue;
                    if (ob_r == gR && ob_g == gG && ob_b == gB)
                    {
                        // apply playerTint (opaque)
                        pixelsSrc[i + 3] = playerTint.A;
                        pixelsSrc[i + 2] = playerTint.R;
                        pixelsSrc[i + 1] = playerTint.G;
                        pixelsSrc[i + 0] = playerTint.B;
                    }
                }

                var wb = new WriteableBitmap(w, h, convSrc.DpiX, convSrc.DpiY, PixelFormats.Pbgra32, null);
                wb.WritePixels(new Int32Rect(0, 0, w, h), pixelsSrc, stride, 0);
                // Do not freeze WriteableBitmap; it must remain writable for Lock/WritePixels.
                return wb;
            }
            catch { return src; }
        }

        // Create HSL-hue shifted copies of images. For each visible, non-black/non-white pixel
        // we replace the hue with the tint's hue while preserving the original saturation and lightness.
        // Returns originals if tint.A == 0.
        private ImageSource?[]? CreateHslShiftedImages(ImageSource?[]? originals, Color tint, Color outlineTint = default)
        {
            if (originals == null) return null;
            if (tint.A == 0) return originals; // no change requested
            // If the tint is fully opaque (A==255) and not the special-cased black/white handled above,
            // perform a full replacement: set non-transparent, non-black pixels to the exact tint RGB.
            // Preserve near-white outline pixels according to `outlineTint` so seam recoloring still works.
            var outList = new System.Collections.Generic.List<ImageSource>(originals.Length);
            if (tint.A == 255)
            {
                foreach (var src in originals)
                {
                    if (src == null)
                    {
                        var pf = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Pbgra32, null);
                        var pxf = new byte[4];
                        try { pf.WritePixels(new Int32Rect(0, 0, 1, 1), pxf, 4, 0); } catch { }
                        try { pf.Freeze(); } catch { }
                        outList.Add(pf);
                        continue;
                    }
                    if (src is BitmapSource bs)
                    {
                        try
                        {
                            var conv = new FormatConvertedBitmap(bs, PixelFormats.Pbgra32, null, 0);
                            int w = conv.PixelWidth; int h = conv.PixelHeight; int stride = w * 4;
                            var pixels = new byte[h * stride];
                            conv.CopyPixels(pixels, stride, 0);

                            double nearWhiteThreshold = 0.82;
                            if (outlineTint.A == 255 && outlineTint.R == 0 && outlineTint.G == 0 && outlineTint.B == 0) nearWhiteThreshold = 0.70;

                            for (int i = 0; i < pixels.Length; i += 4)
                            {
                                byte b = pixels[i + 0];
                                byte g = pixels[i + 1];
                                byte r = pixels[i + 2];
                                byte a = pixels[i + 3];
                                if (a == 0) continue;
                                double lum = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;
                                bool isBlack = (r <= 12 && g <= 12 && b <= 12);
                                bool isNearWhite = (lum >= nearWhiteThreshold);
                                if (isBlack) continue;
                                if (isNearWhite)
                                {
                                    if (outlineTint.A > 0)
                                    {
                                        pixels[i + 3] = outlineTint.A;
                                        pixels[i + 2] = outlineTint.R;
                                        pixels[i + 1] = outlineTint.G;
                                        pixels[i + 0] = outlineTint.B;
                                    }
                                    continue;
                                }
                                // Full replacement with tint color
                                pixels[i + 3] = 255;
                                pixels[i + 2] = tint.R;
                                pixels[i + 1] = tint.G;
                                pixels[i + 0] = tint.B;
                            }

                            var wb = new WriteableBitmap(w, h, conv.DpiX, conv.DpiY, PixelFormats.Pbgra32, null);
                            wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
                            // Do not freeze WriteableBitmap; it must remain writable for Lock/WritePixels.
                            outList.Add(wb);
                        }
                        catch
                        {
                            if (src == null)
                            {
                                var pf = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Pbgra32, null);
                                try { pf.WritePixels(new Int32Rect(0, 0, 1, 1), new byte[4], 4, 0); } catch { }
                                try { pf.Freeze(); } catch { }
                                outList.Add(pf);
                            }
                            else outList.Add(src);
                        }
                    }
                    else
                    {
                        if (src == null)
                        {
                            var pf = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Bgra32, null);
                            try { pf.WritePixels(new Int32Rect(0, 0, 1, 1), new byte[4], 4, 0); } catch { }
                            try { pf.Freeze(); } catch { }
                            outList.Add(pf);
                        }
                        else outList.Add(src);
                    }
                }
                return outList.ToArray();
            }

            // Precompute tint hue
            RgbToHsl(tint.R, tint.G, tint.B, out double tintH, out double tintS, out double tintL);

            
            foreach (var src in originals)
            {
                if (src is BitmapSource bs)
                {
                    try
                    {
                        var conv = new FormatConvertedBitmap(bs, PixelFormats.Bgra32, null, 0);
                        int w = conv.PixelWidth; int h = conv.PixelHeight; int stride = w * 4;
                        var pixels = new byte[h * stride];
                        conv.CopyPixels(pixels, stride, 0);

                        for (int i = 0; i < pixels.Length; i += 4)
                        {
                            byte b = pixels[i + 0];
                            byte g = pixels[i + 1];
                            byte r = pixels[i + 2];
                            byte a = pixels[i + 3];
                            // Skip fully transparent and near-black pixels. Handle near-white outlines
                            // via perceptual luminance so anti-aliased white lines are detected reliably.
                            if (a == 0) continue;
                            double lum = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;
                            bool isBlack = (r <= 12 && g <= 12 && b <= 12);
                            double nearWhiteThreshold = 0.82;
                            if (outlineTint.A == 255 && outlineTint.R == 0 && outlineTint.G == 0 && outlineTint.B == 0) nearWhiteThreshold = 0.70;
                            bool isNearWhite = (lum >= nearWhiteThreshold);
                            if (isBlack) continue;
                            if (isNearWhite)
                            {
                                if (outlineTint.A > 0)
                                {
                                    pixels[i + 3] = outlineTint.A;
                                    pixels[i + 2] = outlineTint.R;
                                    pixels[i + 1] = outlineTint.G;
                                    pixels[i + 0] = outlineTint.B;
                                }
                                continue;
                            }

                            // Convert pixel to HSL, replace hue with tint hue, keep S/L
                            RgbToHsl(r, g, b, out double ph, out double ps, out double pl);
                            double nh = tintH; // replace hue
                            double ns = ps;
                            double nl = pl;
                            RgbFromHsl(nh, ns, nl, out byte nr, out byte ng, out byte nb);

                            pixels[i + 0] = nb;
                            pixels[i + 1] = ng;
                            pixels[i + 2] = nr;
                            // alpha unchanged
                        }

                        var wb = new WriteableBitmap(w, h, conv.DpiX, conv.DpiY, PixelFormats.Bgra32, null);
                        wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
                        // Do not freeze WriteableBitmap; it must remain writable for Lock/WritePixels.
                        outList.Add(wb);
                    }
                    catch
                    {
                        if (src == null)
                        {
                            var pf = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Bgra32, null);
                            try { pf.WritePixels(new Int32Rect(0, 0, 1, 1), new byte[4], 4, 0); } catch { }
                            try { pf.Freeze(); } catch { }
                            outList.Add(pf);
                        }
                        else outList.Add(src);
                    }
                }
                else
                {
                    if (src == null)
                    {
                        var pf = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Bgra32, null);
                        try { pf.WritePixels(new Int32Rect(0, 0, 1, 1), new byte[4], 4, 0); } catch { }
                        try { pf.Freeze(); } catch { }
                        outList.Add(pf);
                    }
                    else outList.Add(src);
                }
            }
            return outList.ToArray();
        }

        // Create a simple 2-frame pulsing animation from a single sprite image by producing
        // a slightly brightened second frame. Returns null on failure.
        private ImageSource?[]? CreateTwoFramePulse(ImageSource src)
        {
            try
            {
                if (src is BitmapSource bs)
                {
                    // Convert to BGRA32
                    var conv = new FormatConvertedBitmap(bs, PixelFormats.Bgra32, null, 0);
                    int w = Math.Max(1, conv.PixelWidth);
                    int h = Math.Max(1, conv.PixelHeight);
                    int stride = w * 4;
                    var pixels = new byte[h * stride];
                    conv.CopyPixels(pixels, stride, 0);

                    // Create brightened copy by blending each color toward white (alpha preserved)
                    var bright = new byte[h * stride];
                    const double factor = 0.35; // how strongly to brighten
                    for (int i = 0; i < pixels.Length; i += 4)
                    {
                        byte b = pixels[i + 0];
                        byte g = pixels[i + 1];
                        byte r = pixels[i + 2];
                        byte a = pixels[i + 3];
                        if (a == 0) { bright[i + 0] = b; bright[i + 1] = g; bright[i + 2] = r; bright[i + 3] = a; continue; }
                        bright[i + 0] = (byte)Math.Min(255, (int)Math.Round(b + (255 - b) * factor));
                        bright[i + 1] = (byte)Math.Min(255, (int)Math.Round(g + (255 - g) * factor));
                        bright[i + 2] = (byte)Math.Min(255, (int)Math.Round(r + (255 - r) * factor));
                        bright[i + 3] = a;
                    }

                    var wb1 = new WriteableBitmap(conv.PixelWidth, conv.PixelHeight, conv.DpiX, conv.DpiY, PixelFormats.Bgra32, null);
                    wb1.WritePixels(new Int32Rect(0, 0, conv.PixelWidth, conv.PixelHeight), pixels, stride, 0);
                    wb1.Freeze();
                    var wb2 = new WriteableBitmap(conv.PixelWidth, conv.PixelHeight, conv.DpiX, conv.DpiY, PixelFormats.Bgra32, null);
                    wb2.WritePixels(new Int32Rect(0, 0, conv.PixelWidth, conv.PixelHeight), bright, stride, 0);
                    wb2.Freeze();
                    return new ImageSource?[] { wb1, wb2 };
                }
            }
            catch { }
            return null;
        }

        // Find the nearest palette index for a given color. Returns null on failure.
        private int? GetNearestPaletteIndexForColor(Color c, Color[]? palette)
        {
            try
            {
                if (palette == null || palette.Length == 0) return null;
                int best = -1; double bestDist = double.MaxValue;
                for (int i = 0; i < palette.Length; i++)
                {
                    var p = palette[i];
                    double dr = p.R - c.R; double dg = p.G - c.G; double db = p.B - c.B;
                    double d = dr * dr + dg * dg + db * db;
                    if (d < bestDist) { bestDist = d; best = i; }
                }
                if (best >= 0) return best;
            }
            catch { }
            return null;
        }

        // Create hue-shifted images with interpolation towards tint hue (used for ground in MainWindow)
        private ImageSource?[]? CreateHueShiftedImages(ImageSource?[]? originals, Color tint, Color outlineTint = default)
        {
            if (originals == null) return null;
            if (tint.A == 0) return originals; // strength 0 => no change
            // If the tint is pure opaque black, produce two-tone images that map
            // non-white pixels to pure black while preserving near-white outlines
            // so object-outline tints can recolor the seam. Use CreateTwoToneTileImages
            // for consistent behavior with other ground/tiles code paths.
            if (tint.A == 255 && tint.R == 0 && tint.G == 0 && tint.B == 0)
            {
                return CreateTwoToneTileImages(originals, Color.FromArgb(255, 0, 0, 0), Color.FromArgb(255, 0, 0, 0), outlineTint);
            }

            // If the tint is pure opaque white, produce white-masked images where
            // non-white pixels become opaque white while preserving near-white
            // seam pixels (so object-outline tinting can control them). Use the
            // outlineTint only for cases where callers want seam recoloring; by
            // default we avoid recoloring the seam here.
            if (tint.A == 255 && tint.R == 255 && tint.G == 255 && tint.B == 255)
            {
                return CreateWhiteMaskedImages(originals, outlineTint, false);
            }

            // If the tint is fully opaque (but not pure-black/white handled above),
            // perform a full replacement of pixel colors with the tint while preserving
            // near-white outlines according to `outlineTint`.
            if (tint.A == 255)
            {
                var outListFull = new System.Collections.Generic.List<ImageSource>(originals.Length);
                foreach (var src in originals)
                {
                    if (src == null)
                    {
                        var pf = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Bgra32, null);
                        var pxf = new byte[4];
                        try { pf.WritePixels(new Int32Rect(0, 0, 1, 1), pxf, 4, 0); } catch { }
                        try { pf.Freeze(); } catch { }
                        outListFull.Add(pf);
                        continue;
                    }
                    if (src is BitmapSource bs)
                    {
                        try
                        {
                            var conv = new FormatConvertedBitmap(bs, PixelFormats.Bgra32, null, 0);
                            int w = conv.PixelWidth; int h = conv.PixelHeight; int stride = w * 4;
                            var pixels = new byte[h * stride];
                            conv.CopyPixels(pixels, stride, 0);

                            double nearWhiteThreshold = 0.82;
                            if (outlineTint.A == 255 && outlineTint.R == 0 && outlineTint.G == 0 && outlineTint.B == 0) nearWhiteThreshold = 0.70;

                            for (int i = 0; i < pixels.Length; i += 4)
                            {
                                byte b = pixels[i + 0];
                                byte g = pixels[i + 1];
                                byte r = pixels[i + 2];
                                byte a = pixels[i + 3];
                                if (a == 0) continue;
                                double lum = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;
                                bool isBlack = (r <= 12 && g <= 12 && b <= 12);
                                bool isNearWhite = (lum >= nearWhiteThreshold);
                                if (isBlack) continue;
                                if (isNearWhite)
                                {
                                    if (outlineTint.A > 0)
                                    {
                                        pixels[i + 3] = outlineTint.A;
                                        pixels[i + 2] = outlineTint.R;
                                        pixels[i + 1] = outlineTint.G;
                                        pixels[i + 0] = outlineTint.B;
                                    }
                                    continue;
                                }
                                // Full replacement with tint color
                                pixels[i + 3] = 255;
                                pixels[i + 2] = tint.R;
                                pixels[i + 1] = tint.G;
                                pixels[i + 0] = tint.B;
                            }

                            var wb = new WriteableBitmap(w, h, conv.DpiX, conv.DpiY, PixelFormats.Bgra32, null);
                            wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
                            // Do not freeze WriteableBitmap; it must remain writable for Lock/WritePixels.
                            outListFull.Add(wb);
                        }
                        catch
                        {
                            if (src == null)
                            {
                                var pf = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Bgra32, null);
                                try { pf.WritePixels(new Int32Rect(0, 0, 1, 1), new byte[4], 4, 0); } catch { }
                                try { pf.Freeze(); } catch { }
                                outListFull.Add(pf);
                            }
                            else outListFull.Add(src);
                        }
                    }
                    else outListFull.Add(src);
                }
                return outListFull.ToArray();
            }

            double strength = tint.A / 255.0;
            // convert tint color to HSL once
            RgbToHsl(tint.R, tint.G, tint.B, out double tintH, out double tintS, out double tintL);
            var outList = new System.Collections.Generic.List<ImageSource>(originals.Length);

            foreach (var src in originals)
            {
                if (src == null)
                {
                    var pf = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Bgra32, null);
                    var pxf = new byte[4];
                    try { pf.WritePixels(new Int32Rect(0, 0, 1, 1), pxf, 4, 0); } catch { }
                    try { pf.Freeze(); } catch { }
                    outList.Add(pf);
                    continue;
                }
                if (src is BitmapSource bs)
                {
                    try
                    {
                        var conv = new FormatConvertedBitmap(bs, PixelFormats.Bgra32, null, 0);
                        int w = conv.PixelWidth; int h = conv.PixelHeight; int stride = w * 4;
                        var pixels = new byte[h * stride];
                        conv.CopyPixels(pixels, stride, 0);

                        for (int i = 0; i < pixels.Length; i += 4)
                        {
                            int b = pixels[i + 0];
                            int g = pixels[i + 1];
                            int r = pixels[i + 2];
                            int a = pixels[i + 3];

                            // Preserve fully transparent pixels
                            if (a == 0) continue;

                            // Use perceptual luminance to identify near-white outline pixels.
                            double lum = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;
                            bool isNearWhite = lum >= 0.82;

                            if (isNearWhite && outlineTint.A > 0)
                            {
                                // Apply outline tint exactly for near-white pixels when requested
                                pixels[i + 3] = outlineTint.A;
                                pixels[i + 2] = outlineTint.R;
                                pixels[i + 1] = outlineTint.G;
                                pixels[i + 0] = outlineTint.B;
                                continue;
                            }

                            RgbToHsl((byte)r, (byte)g, (byte)b, out double h0, out double s0, out double l0);

                            // interpolate hue towards tint hue, and optionally scale/lerp saturation
                            double newH = LerpAngle(h0, tintH, strength);
                            double newS = s0 * (1.0 - strength) + tintS * strength;
                            double newL = l0; // preserve original lightness to keep details

                            RgbFromHsl(newH, newS, newL, out byte r2, out byte g2, out byte b2);

                            pixels[i + 0] = b2;
                            pixels[i + 1] = g2;
                            pixels[i + 2] = r2;
                            pixels[i + 3] = (byte)a; // keep original alpha
                        }

                        var wb = new WriteableBitmap(w, h, conv.DpiX, conv.DpiY, PixelFormats.Bgra32, null);
                        wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
                        // Do not freeze WriteableBitmap; it must remain writable for Lock/WritePixels.
                        outList.Add(wb);
                    }
                    catch
                    {
                        outList.Add(src ?? new WriteableBitmap(1, 1, 96, 96, PixelFormats.Bgra32, null));
                    }
                }
                else
                {
                    outList.Add(src ?? new WriteableBitmap(1, 1, 96, 96, PixelFormats.Bgra32, null));
                }
            }
            return outList.ToArray();
        }

        // Create images where non-white pixels become solid black (alpha preserved for transparent pixels),
        // and near-white pixels are preserved as white. This produces a 'black with white line' effect
        // useful for pure-black triggers.
        private ImageSource?[]? CreateBlackMaskedImages(ImageSource?[]? originals, Color outlineTint = default, bool recolorOutline = true)
        {
            if (originals == null) return null;
            var outList = new System.Collections.Generic.List<ImageSource>(originals.Length);
            foreach (var src in originals)
            {
                if (src == null)
                {
                    var pf = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Bgra32, null);
                    var pxf = new byte[4];
                    try { pf.WritePixels(new Int32Rect(0, 0, 1, 1), pxf, 4, 0); } catch { }
                    try { pf.Freeze(); } catch { }
                    outList.Add(pf);
                    continue;
                }
                if (src is BitmapSource bs)
                {
                    try
                    {
                        var conv = new FormatConvertedBitmap(bs, PixelFormats.Bgra32, null, 0);
                        int w = conv.PixelWidth; int h = conv.PixelHeight; int stride = w * 4;
                        var pixels = new byte[h * stride];
                        conv.CopyPixels(pixels, stride, 0);

                        for (int i = 0; i < pixels.Length; i += 4)
                        {
                            byte b = pixels[i + 0];
                            byte g = pixels[i + 1];
                            byte r = pixels[i + 2];
                            byte a = pixels[i + 3];
                            if (a == 0) continue; // preserve transparency
                            double lum = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;
                            bool isWhite = (lum >= 0.82);
                            if (isWhite)
                            {
                                if (recolorOutline && outlineTint.A > 0)
                                {
                                    pixels[i + 3] = 255;
                                    pixels[i + 2] = outlineTint.R;
                                    pixels[i + 1] = outlineTint.G;
                                    pixels[i + 0] = outlineTint.B;
                                }
                                else
                                {
                                    // preserve original near-white color
                                    continue;
                                }
                            }
                            else
                            {
                                pixels[i + 0] = 0; pixels[i + 1] = 0; pixels[i + 2] = 0;
                                pixels[i + 3] = 255;
                            }
                        }

                        var wb = new WriteableBitmap(w, h, conv.DpiX, conv.DpiY, PixelFormats.Bgra32, null);
                        wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
                        // Do not freeze WriteableBitmap; it must remain writable for Lock/WritePixels.
                        outList.Add(wb);
                    }
                    catch
                    {
                        outList.Add(src);
                    }
                }
                else
                {
                    outList.Add(src);
                }
            }
            return outList.ToArray();
        }

        private ImageSource?[]? CreateBlackMaskedExceptColorArray(ImageSource?[]? originals, Color excludeColor, Color outlineTint = default, bool recolorOutline = true)
        {
            if (originals == null) return null;
            var outList = new System.Collections.Generic.List<ImageSource>(originals.Length);
            foreach (var src in originals)
            {
                try
                {
                    var v = CreateBlackMaskedExceptColor(src, excludeColor, outlineTint, recolorOutline);
                    outList.Add(v ?? src ?? new WriteableBitmap(1,1,96,96,PixelFormats.Bgra32,null));
                }
                catch { outList.Add(src ?? new WriteableBitmap(1,1,96,96,PixelFormats.Bgra32,null)); }
            }
            return outList.ToArray();
        }

        // Create white-masked images: non-white pixels become opaque white, and
        // near-white pixels are left untouched unless `recolorOutline` is true.
        // This mirrors CreateBlackMaskedImages but produces white instead, used for
        // representing a pure-white ground tint so the seam can remain editable by
        // object-outline tints.
        private ImageSource?[]? CreateWhiteMaskedImages(ImageSource?[]? originals, Color outlineTint = default, bool recolorOutline = false)
        {
            if (originals == null) return null;
            var outList = new System.Collections.Generic.List<ImageSource>(originals.Length);
            foreach (var src in originals)
            {
                if (src == null)
                {
                    var pf = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Bgra32, null);
                    var pxf = new byte[4];
                    try { pf.WritePixels(new Int32Rect(0, 0, 1, 1), pxf, 4, 0); } catch { }
                    try { pf.Freeze(); } catch { }
                    outList.Add(pf);
                    continue;
                }
                if (src is BitmapSource bs)
                {
                    try
                    {
                        var conv = new FormatConvertedBitmap(bs, PixelFormats.Bgra32, null, 0);
                        int w = conv.PixelWidth; int h = conv.PixelHeight; int stride = w * 4;
                        var pixels = new byte[h * stride];
                        conv.CopyPixels(pixels, stride, 0);

                        for (int i = 0; i < pixels.Length; i += 4)
                        {
                            byte b = pixels[i + 0];
                            byte g = pixels[i + 1];
                            byte r = pixels[i + 2];
                            byte a = pixels[i + 3];
                            if (a == 0) continue; // preserve transparency
                            double lum = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;
                            bool isNearWhite = (lum >= 0.82);
                            if (isNearWhite)
                            {
                                if (recolorOutline && outlineTint.A > 0)
                                {
                                    pixels[i + 3] = 255;
                                    pixels[i + 2] = outlineTint.R;
                                    pixels[i + 1] = outlineTint.G;
                                    pixels[i + 0] = outlineTint.B;
                                }
                                else
                                {
                                    // leave near-white seam pixels untouched
                                    continue;
                                }
                            }
                            else
                            {
                                // make pixel opaque white
                                pixels[i + 2] = 255;
                                pixels[i + 1] = 255;
                                pixels[i + 0] = 255;
                                pixels[i + 3] = 255;
                            }
                        }

                        var wb = new WriteableBitmap(w, h, conv.DpiX, conv.DpiY, PixelFormats.Bgra32, null);
                        wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
                        // Do not freeze WriteableBitmap; it must remain writable for Lock/WritePixels.
                        outList.Add(wb);
                    }
                    catch { outList.Add(src); }
                }
                else outList.Add(src);
            }
            return outList.ToArray();
        }

        // Create fully black images: non-transparent pixels become opaque black.
        // Use this for saws when background is pure black so no white lines remain.
        private ImageSource?[]? CreateSolidBlackImages(ImageSource?[]? originals)
        {
            if (originals == null) return null;
            var outList = new System.Collections.Generic.List<ImageSource>(originals.Length);
            foreach (var src in originals)
            {
                if (src == null)
                {
                    var pf = new WriteableBitmap(1, 1, 96, 96, PixelFormats.Bgra32, null);
                    var pxf = new byte[4];
                    try { pf.WritePixels(new Int32Rect(0, 0, 1, 1), pxf, 4, 0); } catch { }
                    try { pf.Freeze(); } catch { }
                    outList.Add(pf);
                    continue;
                }
                if (src is BitmapSource bs)
                {
                    try
                    {
                        var conv = new FormatConvertedBitmap(bs, PixelFormats.Bgra32, null, 0);
                        int w = Math.Max(1, conv.PixelWidth);
                        int h = Math.Max(1, conv.PixelHeight);
                        int stride = w * 4;
                        var pixels = new byte[h * stride];
                        conv.CopyPixels(pixels, stride, 0);
                        for (int i = 0; i < pixels.Length; i += 4)
                        {
                            byte a = pixels[i + 3];
                            if (a == 0) continue;
                            pixels[i + 0] = 0; // b
                            pixels[i + 1] = 0; // g
                            pixels[i + 2] = 0; // r
                            pixels[i + 3] = 255; // make opaque
                        }
                        var wb = new WriteableBitmap(w, h, conv.DpiX, conv.DpiY, PixelFormats.Bgra32, null);
                        wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
                        // Do not freeze WriteableBitmap; it must remain writable for Lock/WritePixels.
                        outList.Add(wb);
                    }
                    catch { outList.Add(src); }
                }
                else outList.Add(src);
            }
            return outList.ToArray();
        }

        // Helper: convert RGB byte values to HSL (H in degrees 0..360, S/L 0..1)
        private static void RgbToHsl(byte r8, byte g8, byte b8, out double h, out double s, out double l)
        {
            double r = r8 / 255.0, g = g8 / 255.0, b = b8 / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            l = (max + min) / 2.0;
            if (max == min)
            {
                h = 0.0; s = 0.0; return;
            }
            double d = max - min;
            s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
            if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
            else if (max == g) h = (b - r) / d + 2;
            else h = (r - g) / d + 4;
            h *= 60.0;
        }

        // Helper: convert HSL to RGB bytes. H in degrees 0..360, S/L 0..1
        private static void RgbFromHsl(double h, double s, double l, out byte r8, out byte g8, out byte b8)
        {
            double r, g, b;
            if (s == 0)
            {
                r = g = b = l; // achromatic
            }
            else
            {
                double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
                double p = 2 * l - q;
                double hk = (h % 360.0) / 360.0;
                double[] t = new double[3] { hk + 1.0 / 3.0, hk, hk - 1.0 / 3.0 };
                double[] rgb = new double[3];
                for (int i = 0; i < 3; i++)
                {
                    double tc = t[i];
                    if (tc < 0) tc += 1.0; if (tc > 1) tc -= 1.0;
                    if (tc < 1.0 / 6.0) rgb[i] = p + (q - p) * 6.0 * tc;
                    else if (tc < 1.0 / 2.0) rgb[i] = q;
                    else if (tc < 2.0 / 3.0) rgb[i] = p + (q - p) * (2.0 / 3.0 - tc) * 6.0;
                    else rgb[i] = p;
                }
                r = rgb[0]; g = rgb[1]; b = rgb[2];
            }
            r8 = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(r * 255.0)));
            g8 = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(g * 255.0)));
            b8 = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(b * 255.0)));
        }

        // Linear interpolation for circular hue (degrees). t in 0..1
        private static double LerpAngle(double a, double b, double t)
        {
            // convert to radians for shortest path
            double diff = (b - a + 540.0) % 360.0 - 180.0;
            return (a + diff * t + 360.0) % 360.0;
        }

        // Replace player 1 colors with player 2 colors in a bitmap
        private System.Windows.Media.Imaging.BitmapSource ReplaceColorsForPlayer2(System.Windows.Media.Imaging.BitmapSource original)
        {
            try
            {
                AppendSimDebug($"[COLOR_REPLACE] Starting color replacement, original format: {original.Format}");
                
                // Color mappings for player 2 (BGRA format since that's how WPF stores pixels)
                // #50C72A (green) → #F36AFF (magenta/pink)
                // #4DADFF (blue) → #BCBE00 (yellow-green)
                var colorMap = new System.Collections.Generic.Dictionary<uint, uint>
                {
                    { 0xFF2AC750, 0xFFFF6AF3 },  // BGRA: #50C72A → #F36AFF
                    { 0xFFFFAD4D, 0xFF00BEBC }   // BGRA: #4DADFF → #BCBE00
                };

                // Create a FormatConvertedBitmap to ensure we have BGRA32 format
                var fcb = new System.Windows.Media.Imaging.FormatConvertedBitmap(original, System.Windows.Media.PixelFormats.Bgra32, null, 0.0);

                // Create a WriteableBitmap from the converted bitmap
                var wb = new System.Windows.Media.Imaging.WriteableBitmap(fcb);
                wb.Lock();

                int replacedCount = 0;
                unsafe
                {
                    uint* pixels = (uint*)wb.BackBuffer;
                    int pixelCount = wb.PixelWidth * wb.PixelHeight;

                    for (int i = 0; i < pixelCount; i++)
                    {
                        uint originalColor = pixels[i];
                        if (colorMap.TryGetValue(originalColor, out uint newColor))
                        {
                            pixels[i] = newColor;
                            replacedCount++;
                        }
                    }
                }

                AppendSimDebug($"[COLOR_REPLACE] Replaced {replacedCount} pixels out of {wb.PixelWidth * wb.PixelHeight}, size: {wb.PixelWidth}x{wb.PixelHeight}");

                wb.AddDirtyRect(new System.Windows.Int32Rect(0, 0, wb.PixelWidth, wb.PixelHeight));
                wb.Unlock();
                wb.Freeze();

                // Return the modified WriteableBitmap (which is also a BitmapImage/BitmapSource)
                return wb;
            }
            catch (Exception ex)
            {
                AppendSimDebug($"[COLOR_REPLACE] Error: {ex.Message}");
                return original;
            }
        }
    }
}



