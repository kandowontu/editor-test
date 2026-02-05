# Dual Mode Array Mapping Reference

This document maps all `[2]`-sized arrays from `famidash/SAUCE/famidash.h` to their C# simulator equivalents in `native-windows/SimulatorWindow.xaml.cs`. These arrays represent per-player state variables that will be needed for dual mode implementation.

## Summary
- **Total famidash.h [2] arrays:** 29
- **C# equivalents found:** 13 direct matches + 12 single-player conversions
- **Arrays not yet in C#:** 4 (will need implementation for dual mode)

---

## 1. Cube Data Arrays

### cube_data[2]
- **famidash.h:** Line 130 - `uint8_t cube_data[2];`
- **C# equivalent:** `byte currplayer_cube_data` (single-player)
- **Type:** `uint8_t` → `byte`
- **Purpose:** Cube state data (frame counter, state flags)
- **Note:** Currently single-player, needs array conversion for dual mode

### cube_rotate[2]
- **famidash.h:** Line 131 - `uint16_t cube_rotate[2];`
- **C# equivalents (all modes share this variable in ROM):**
  - `int cubeRotate_fixed` - Cube mode rotation
  - `int shipRotate_fixed` - Ship mode rotation (velocity-based animation)
  - `int swingcopterRotate_fixed` - Swingcopter mode rotation (velocity-based)
  - `int footballRotate_fixed` - Football mode rotation (cube-style with flip table)
- **Type:** `uint16_t` → `int` (fixed-point, 16-bit with 8-bit fractional)
- **Purpose:** All modes use the same ROM variable for rotation animation state
- **ROM Note:** In nesdash.s assembly, all game modes use `cube_rotate` for their rotation handling
- **C# Note:** Separated into mode-specific variables for clarity; each needs to be converted to `[2]` array for dual mode

---

## 2. Slope/Terrain Arrays

### slope_frames[2]
- **famidash.h:** Line 155 - `int8_t slope_frames[2];`
- **C# equivalent:** NOT YET IMPLEMENTED
- **Type:** `int8_t` → `sbyte`
- **Purpose:** Counter for frames spent on slope surface
- **Dual Mode Note:** Will need per-player tracking

### slope_type[2]
- **famidash.h:** Line 157 - `uint8_t slope_type[2];`
- **C# equivalent:** NOT YET IMPLEMENTED
- **Type:** `uint8_t` → `byte`
- **Purpose:** Type of slope currently on (0=none, other values = slope type)
- **Dual Mode Note:** Will need per-player tracking

### was_on_slope_counter[2]
- **famidash.h:** Line 158 - `uint8_t was_on_slope_counter[2];`
- **C# equivalent:** NOT YET IMPLEMENTED
- **Type:** `uint8_t` → `byte`
- **Purpose:** Counter for frames since last on slope
- **Dual Mode Note:** Will need per-player tracking

### last_slope_type[2]
- **famidash.h:** Line 403 - `uint8_t last_slope_type[2];`
- **C# equivalent:** NOT YET IMPLEMENTED
- **Type:** `uint8_t` → `byte`
- **Purpose:** Type of previous slope encountered
- **Dual Mode Note:** Will need per-player tracking

---

## 3. Player Position & Velocity Arrays

### player_x[2]
- **famidash.h:** Line 317 - `uint16_t player_x[2];`
- **C# equivalent:** `int playerX_fixed` (single-player)
- **Type:** `uint16_t` → `int` (fixed-point, 8 fractional bits)
- **Purpose:** Player's X position in world space
- **Current Implementation:** `private int playerX_fixed = 0;` (Line 1124)

### player_y[2]
- **famidash.h:** Line 318 - `uint16_t player_y[2];`
- **C# equivalent:** `int playerY_fixed` (single-player)
- **Type:** `uint16_t` → `int` (fixed-point, 8 fractional bits)
- **Purpose:** Player's Y position in world space
- **Current Implementation:** `private int playerY_fixed = 0;` (Line 1125)

### player_vel_x[2]
- **famidash.h:** Line 319 - `int16_t player_vel_x[2];`
- **C# equivalent:** `int playerVelX_fixed` (NOT YET FOUND in search)
- **Type:** `int16_t` → `int` (fixed-point)
- **Purpose:** Player's horizontal velocity
- **Dual Mode Note:** Inferred from codebase but not yet explicitly found

### player_vel_y[2]
- **famidash.h:** Line 320 - `int16_t player_vel_y[2];`
- **C# equivalent:** `int playerVelY_fixed` (single-player)
- **Type:** `int16_t` → `int` (fixed-point)
- **Purpose:** Player's vertical velocity
- **Current Implementation:** `private int playerVelY_fixed = 0;` (Line 2523)

### player_gravity[2]
- **famidash.h:** Line 321 - `uint8_t player_gravity[2];`
- **C# equivalent:** `bool gravityFlipped` (single-player, boolean)
- **Type:** `uint8_t` → `bool`
- **Purpose:** 0 = down gravity, 0xFF = up gravity
- **Current Implementation:** `private bool gravityFlipped = false;` (Line 2465)
- **Note:** C# uses boolean instead of byte

### player_mini[2]
- **famidash.h:** Line 322 - `uint8_t player_mini[2];`
- **C# equivalent:** `bool miniMode` (single-player)
- **Type:** `uint8_t` → `bool`
- **Purpose:** 0 = normal, 1 = mini
- **Current Implementation:** `private bool miniMode = false;` (Line 2466)
- **Note:** C# uses boolean instead of byte

### orbhitonthisframe[2]
- **famidash.h:** Line 323 - `uint8_t orbhitonthisframe[2];`
- **C# equivalent:** `bool orbhitonthisframe` (single-player)
- **Type:** `uint8_t` → `bool`
- **Purpose:** Orb/pad activated on current frame
- **Current Implementation:** `private bool orbhitonthisframe = false;` (Line 2521)
- **Note:** C# uses boolean instead of byte

### chargepower[2]
- **famidash.h:** Line 324 - `uint8_t chargepower[2];`
- **C# equivalent:** `int[] chargepower` (array)
- **Type:** `uint8_t` → `int`
- **Purpose:** Football charge accumulation
- **Current Implementation:** `private int[] chargepower = new int[2];` (Line 2589)

---

## 4. Orb/Pad State Arrays

### orbed[2]
- **famidash.h:** Line 347 - `uint8_t orbed[2];`
- **C# equivalent:** `bool orbed` (single-player)
- **Type:** `uint8_t` → `bool`
- **Purpose:** Prevents jumps/teleports until X released
- **Current Implementation:** `private bool orbed = false;` (Line 2578)
- **Dependencies:** Spider orbs/pads, teleport portals, S blocks, J blocks
- **Note:** C# uses boolean instead of byte

### ufo_orbed[2]
- **famidash.h:** Line 456 - `uint8_t ufo_orbed[2];`
- **C# equivalent:** `bool ufoOrbed` (single-player)
- **Type:** `uint8_t` → `bool`
- **Purpose:** UFO-specific orb state
- **Current Implementation:** `private bool ufoOrbed = false;` (Line 2577)
- **Note:** C# uses boolean instead of byte

### black_orbed[2]
- **famidash.h:** Line 457 - `uint8_t black_orbed[2];`
- **C# equivalent:** `bool blackOrbed` (single-player)
- **Type:** `uint8_t` → `bool`
- **Purpose:** Spider black orb hold mechanic
- **Current Implementation:** `private bool blackOrbed = false;` (Line 2579)
- **Note:** C# uses boolean instead of byte

### ball_switched[2]
- **famidash.h:** Line 354 - `uint8_t ball_switched[2];`
- **C# equivalent:** `bool[] ballSwitched` (array)
- **Type:** `uint8_t` → `bool`
- **Purpose:** Ball mode gravity switch state
- **Current Implementation:** `private bool[] ballSwitched = new bool[2];` (Line 2574)

---

## 5. Dashing State Array

### dashing[2]
- **famidash.h:** Line 459 - `uint8_t dashing[2];`
- **C# equivalent:** `int dashing` (single-player)
- **Type:** `uint8_t` → `int`
- **Purpose:** Dash state: 0=not, 1=horiz, 2=45° up, 3=45° down, 4=up, 5=down
- **Current Implementation:** `private int dashing = 0;` (Line 2580)

---

## 6. Block Collision Arrays

### hblocked[2]
- **famidash.h:** Line 366 - `uint8_t hblocked[2];`
- **C# equivalent:** `bool hblocked` (single-player)
- **Type:** `uint8_t` → `bool`
- **Purpose:** H block - instant ceiling ejection
- **Current Implementation:** `private bool hblocked = false;` (Line 2582)
- **Note:** C# uses boolean instead of byte

### jblocked[2]
- **famidash.h:** Line 367 - `uint8_t jblocked[2];`
- **C# equivalent:** `bool jblocked` (single-player)
- **Type:** `uint8_t` → `bool`
- **Purpose:** J block - requires press instead of hold
- **Current Implementation:** `private bool jblocked = false;` (Line 2583)
- **Note:** C# uses boolean instead of byte

### fblocked[2]
- **famidash.h:** Line 368 - `uint8_t fblocked[2];`
- **C# equivalent:** `bool fblocked` (single-player)
- **Type:** `uint8_t` → `bool`
- **Purpose:** F block - force press-to-jump, gravity flip on hit
- **Current Implementation:** `private bool fblocked = false;` (Line 2584)
- **Note:** C# uses boolean instead of byte

### dblocked[2]
- **famidash.h:** Line 487 - `uint8_t dblocked[2];`
- **C# equivalent:** `bool dblocked` (single-player)
- **Type:** `uint8_t` → `bool`
- **Purpose:** D block - wave movement state
- **Current Implementation:** `private bool dblocked = false;` (Line 2585)
- **Note:** C# uses boolean instead of byte

---

## 7. Mode-Specific Animation Arrays

### spiderframe[2]
- **famidash.h:** Line 362 - `uint8_t spiderframe[2];`
- **C# equivalent:** `int spiderAnimationFrameCounter` (single-player counter)
- **Type:** `uint8_t` → `int`
- **Purpose:** Spider animation frame state
- **Current Implementation:** `private int spiderAnimationFrameCounter = 0;` (Line 2548)
- **Note:** C# uses counter + accumulator; spider-specific

### robotframe[2]
- **famidash.h:** Line 363 - `uint8_t robotframe[2];`
- **C# equivalent:** `int robotAnimationFrameCounter` (single-player counter)
- **Type:** `uint8_t` → `int`
- **Purpose:** Robot animation frame state
- **Current Implementation:** `private int robotAnimationFrameCounter = 0;` (Line 2550)
- **Note:** C# uses counter + accumulator

### robotjumpframe[2]
- **famidash.h:** Line 364 - `uint8_t robotjumpframe[2];`
- **C# equivalent:** `int[] robotJumpFrame` (array)
- **Type:** `uint8_t` → `int`
- **Purpose:** Robot jump animation frame
- **Current Implementation:** `private int[] robotJumpFrame = new int[2];` (Line 2588)
- **Note:** Already dual-capable array

### robotjumptime[2]
- **famidash.h:** Line 365 - `uint8_t robotjumptime[2];`
- **C# equivalent:** `int[] robotJumpTime` (array)
- **Type:** `uint8_t` → `int`
- **Purpose:** Robot jump timer/counter
- **Current Implementation:** `private int[] robotJumpTime = new int[2];` (Line 2586)
- **Note:** Already dual-capable array

### ninjajumps[2]
- **famidash.h:** Line 369 - `uint8_t ninjajumps[2];`
- **C# equivalent:** `int[] ninjajumps` (array)
- **Type:** `uint8_t` → `int`
- **Purpose:** Ninja available jump count
- **Current Implementation:** `private int[] ninjajumps = new int[2] { 3, 3 };` (Line 2585)
- **Note:** Already dual-capable array, initialized to 3

---

## Implementation Plan for Dual Mode

### Already Array-Based (Ready for Dual Support)
These variables are already declared as `[2]` arrays and can support dual mode with minimal refactoring:
- `chargepower[2]` - Football charge
- `ballSwitched[2]` - Ball gravity state
- `robotJumpTime[2]` - Robot jump timer
- `robotJumpFrame[2]` - Robot jump animation
- `ninjajumps[2]` - Ninja jump count

### Single-Player → Array Conversion Needed
These variables need conversion from single-player boolean/int to `[2]` arrays:
- `playerX_fixed` → `int[] playerX_fixed = new int[2]`
- `playerY_fixed` → `int[] playerY_fixed = new int[2]`
- `playerVelY_fixed` → `int[] playerVelY_fixed = new int[2]`
- `gravityFlipped` → `bool[] gravityFlipped = new bool[2]`
- `miniMode` → `bool[] miniMode = new bool[2]`
- `orbhitonthisframe` → `bool[] orbhitonthisframe = new bool[2]`
- `orbed` → `bool[] orbed = new bool[2]`
- `ufoOrbed` → `bool[] ufoOrbed = new bool[2]`
- `blackOrbed` → `bool[] blackOrbed = new bool[2]`
- `dashing` → `int[] dashing = new int[2]`
- `hblocked` → `bool[] hblocked = new bool[2]`
- `jblocked` → `bool[] jblocked = new bool[2]`
- `fblocked` → `bool[] fblocked = new bool[2]`
- `dblocked` → `bool[] dblocked = new bool[2]`
- **Rotation variables (all from `cube_rotate[2]` in ROM):**
  - `cubeRotate_fixed` → `int[] cubeRotate_fixed = new int[2]`
  - `shipRotate_fixed` → `int[] shipRotate_fixed = new int[2]`
  - `swingcopterRotate_fixed` → `int[] swingcopterRotate_fixed = new int[2]`
  - `footballRotate_fixed` → `int[] footballRotate_fixed = new int[2]`
- `spiderAnimationFrameCounter` → `int[] spiderAnimationFrameCounter = new int[2]`
- `robotAnimationFrameCounter` → `int[] robotAnimationFrameCounter = new int[2]`

### Missing Implementation (New Variables)
These famidash.h [2] arrays need to be added to C# simulator:
- `slope_frames[2]` - Slope frame counter
- `slope_type[2]` - Current slope type
- `was_on_slope_counter[2]` - Frames since on slope
- `last_slope_type[2]` - Previous slope type
- `playerVelX_fixed[2]` - Horizontal velocity (probably exists but not found in search)

---

## Notes

### Naming Convention Differences
- **NES ROM:** snake_case with numeric prefixes (e.g., `player_x`, `player_gravity`)
- **C# Simulator:** camelCase with descriptive suffixes (e.g., `playerX_fixed`, `gravityFlipped`)

### Fixed-Point Representation
- Both NES and C# use fixed-point math for position/velocity
- NES: 16-bit format varies by variable
- C# Simulator: Uses 8 fractional bits for position (`<< 8` and `>> 8` shifts)

### Boolean vs Byte
- **NES:** Uses `uint8_t` for all flags (0 = false, non-zero = true)
- **C#:** Uses native `bool` type (more idiomatic)
- During dual mode conversion, maintain type consistency

### Animation Counters
- **NES ROM:** Single frame counter variable
- **C# Simulator:** Uses counter + accumulator pattern for smooth animation
  - `robotAnimationFrameCounter` + `robotAnimationFrameAccum`
  - `spiderAnimationFrameCounter` + `spiderAnimationFrameAccum`
  - `pogoBounceAnimationCounter` + `pogoBounceAnimationFrameAccum`
  - `ballAnimationFrameCounter` + `ballAnimationFrameAccum`

### Per-Mode State
Some variables are game-mode-specific and may need per-mode arrays rather than generic [2] arrays:
- Cube rotation (`cubeRotate_fixed`) - different for each mode
- Animation frames - each mode has unique animation state

---

## Files Affected

### Primary Files to Modify
1. **SimulatorWindow.xaml.cs** - Main simulator state variables
   - Physics variables (position, velocity, gravity, mini mode)
   - Block collision flags
   - Animation counters
   - Orb/pad states

2. **Physics Partial Files** (per game mode)
   - `CubePhysics_Fresh.partial.cs` - Cube mode physics
   - `ShipPhysics_Fresh.partial.cs` - Ship mode physics
   - `BallPhysics.cs` - Ball mode physics
   - `RobotPhysics_Fresh.partial.cs` - Robot mode physics
   - `UFOPhysics_Fresh.partial.cs` - UFO mode physics
   - (Other mode files as needed)

### Supporting Files
- `GameModePhysics.cs` - Mode-specific gravity/physics constants
- `SetOptionsWindow.xaml.cs` - Difficulty selection already dual-capable
- Configuration/JSON loading for level state (if applicable)

---

## Testing Strategy

1. **Single-Player Baseline:** Verify all conversions maintain single-player behavior
2. **Array Indexing:** Test with `[0]` (current player) to ensure equivalence
3. **Dual Mode Activation:** Toggle dual mode and verify both players tracked independently
4. **Cross-Player Physics:** Verify gravity flip, mini toggle affect correct player
5. **Collision State:** Verify block states don't interfere between players
6. **Animation Sync:** Verify both players animate independently
