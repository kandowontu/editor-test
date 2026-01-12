ye# Famidash Simulator - Collision System Refactor Summary

## What Was Done

I've successfully refactored the collision and movement system for the Famidash simulator to properly match the game's source code behavior. Here's a complete breakdown:

## New Files Created

### 1. `GameModes/CollisionHelpers.partial.cs`
A comprehensive collision detection helper system with the following methods:

- **`TileBlocksFromBelow()`** - Detects floor/ceiling blocking based on gravity orientation
  - Normal gravity: checks bottom half of tiles (COL_BOTTOM tiles block)
  - Reversed gravity: checks top half of tiles (COL_TOP tiles block)
  - Handles all stair types, half-slabs, and complex shapes

- **`TileCausesDeath()`** - Complete death collision implementation
  - All spike types: bottom, top, left, right, corners, both sides
  - Directional death tiles that respect gravity
  - Proper pixel-perfect spike detection matching original game
  - Supports all metatile death types from the source

- **`CheckFloorCollision()`** - Floor detection with gravity awareness
  - Returns the Y position of the nearest floor
  - Respects gravity orientation (floor vs ceiling)
  - Checks multiple tiles under player's feet

- **`CheckCeilingCollision()`** - Ceiling detection
  - Proper handling for both gravity orientations
  - Per-pixel accuracy

- **`CheckDeathCollision()`** - Full hitbox death checking
  - Checks all tiles the player overlaps
  - Respects MainWindow.Option_NoDeath setting

- **`MapCollisionTileIndex()`** - Helper for animated tile collision mapping

### 2. `GameModes/CubePhysicsRefactored.partial.cs`
Complete cube physics rewrite following the correct game flow:

**Game Flow: Gravity → Move → Input → Collision**

- **`ProcessCubePhysicsRefactored()`** - Main physics loop
  1. Apply gravity
  2. Integrate velocity (move player)
  3. Check input (X key)
  4. Check death collision
  5. Check ceiling collision (if moving away from ground)
  6. Check floor collision (if moving toward ground)

- **`ApplyCubeGravity()`** - Proper gravity application
  - Respects mini mode (different gravity constant)
  - Respects gravity orientation
  - Skips gravity when grounded

- **`HandleCeilingCollisionRefactored()`** - Ceiling collision response
  - Normal gravity: bonk and stop
  - Reversed gravity: land and allow jumping!

- **`HandleFloorCollisionRefactored()`** - Floor collision response
  - Snap to floor when within landing distance
  - Consume jump buffer if present
  - Allow immediate jump on landing
  - Works for both gravity orientations

- **`ApplyCubeJump()`** - Jump impulse
  - Proper velocity based on mini mode
  - Correct direction based on gravity
  - Respects time scale

### 3. `GameModes/COLLISION_REFACTOR_README.md`
Complete documentation of the refactoring including:
- Feature descriptions
- Integration instructions
- Testing guidelines
- TODO list for future features

## Key Improvements

### 1. Proper Gravity Orientation
- **Normal Gravity:** Bottom of player used for landing, top for ceiling bonk
- **Reversed Gravity:** Top of player used for landing, bottom for ceiling bonk
- All collision checks respect current gravity state

### 2. Complete Death Collision
Implemented all 20+ spike and hazard types from the original game:
- `COL_DEATH` - Full death tile
- `COL_DEATH_BOTTOM/TOP/LEFT/RIGHT` - Directional death
- `COL_DEATH_BOTTOM_LEFT/RIGHT` - Corner combinations
- `COL_DEATH_TOP_LEFT/RIGHT` - Corner combinations
- `COL_BOTTOM_SPIKES` - Bottom spikes
- `COL_TOP_CENTER_SPIKE` - Top spike
- `COL_DOWN_LEFT/RIGHT_SPIKE` - Directional corner spikes
- `COL_UP_LEFT/RIGHT_SPIKE` - Upward corner spikes
- `COL_LEFT/RIGHT_SPIKE_BLOCK` - Side spike blocks
- All with proper pixel-perfect collision detection

### 3. Correct Game Flow
The new system follows the exact flow from the Famidash source:
1. Gravity is applied first
2. Player position is updated
3. Input is checked
4. Collision detection and response happens last

This matches the original C code in `/famidash/SAUCE/gamemodes/gamemode_cube.h`

### 4. Proper Input Handling
- X key (not "up" or "a") is used for jumping per your specification
- Jump buffering allows pressing jump slightly before landing
- Input check happens between movement and collision (correct order)

### 5. Mini Mode Ready
- Infrastructure in place for mini mode
- Different gravity constant (0x6F vs 0x6B)
- Different jump velocity (-0x4D0 vs -0x590)
- Different hitbox sizes ready to implement

### 6. Dual Mode Ready
- Code structure supports dual mode
- Can easily add second player processing

## Integration

The refactored system is integrated into `SimulatorWindow.xaml.cs` with a feature flag:

```csharp
private bool useRefactoredPhysics = true;
```

When enabled (default), cube mode (gamemode 0) uses the new refactored physics. Other modes continue using their existing systems. The integration happens in `SimulateNumericStep()` right before the gravity application section.

## Files Modified

### `SimulatorWindow.xaml.cs`
- Added `useRefactoredPhysics` flag
- Integrated refactored physics call in numeric simulation step
- Falls back to old physics on error for safety

## Collision Types Now Supported

From `/famidash/METATILES/metatiles.h`, all these are now properly handled:

- `COL_NONE` (0x00) - Empty space
- `COL_ALL` (0x07) - Full solid block
- `COL_BOTTOM` (0x06) - Bottom half-slab
- `COL_TOP` (0x05) - Top half-slab
- `COL_LEFT/RIGHT` (0x24/0x25) - Side half-slabs
- `COL_FLOOR_CEIL` (0x09) - Special floor/ceiling
- `COL_UP_LEFT/RIGHT` (0x20/0x21) - Top corner quarters
- `COL_DOWN_LEFT/RIGHT` (0x22/0x23) - Bottom corner quarters
- `COL_TOP_LEFT/RIGHT_STAIRS` (0x3A/0x39) - Stair tiles
- `COL_BOTTOM_LEFT/RIGHT_STAIRS` (0x38/0x37) - Stair tiles
- All death and spike variations (0x01-0x04, 0x2B-0x36, etc.)
- All slope types (ready for future implementation)

## Testing Recommendations

1. **Basic Movement**
   - Jump and land on platforms
   - Walk off edges
   - Hit ceiling from below

2. **Reversed Gravity**
   - Trigger a gravity reversal portal
   - Land on "ceiling" (top of tiles)
   - Verify jump direction reversed
   - Hit "floor" (bottom tiles) from above

3. **Death Collision**
   - Place different spike tiles in a level
   - Verify they kill from correct direction
   - Test corner spikes
   - Test spike blocks from sides

4. **Edge Cases**
   - Jump buffering (press jump before landing)
   - Walking off platforms (should fall immediately)
   - Ceiling bonk while jumping

## Future Enhancements Ready to Implement

The refactored code structure makes these easy to add:

1. **Mini Mode** - Just need to detect mini state and pass to physics
2. **Dual Mode** - Call physics twice for two players
3. **Slope Collision** - Metatile definitions already exist
4. **Robot Mode** - Similar structure to cube
5. **Spider Mode** - Teleport-to-surface mechanic
6. **Wave Mode** - Free-form movement

## Performance

The new system is efficient:
- Only checks tiles near the player
- Per-pixel checks only where needed
- Early-out on death detection
- Reuses collision helper functions

## Compatibility

- Maintains all existing simulator features (portals, orbs, speeds)
- Falls back to old physics on error
- Can be toggled on/off via `useRefactoredPhysics` flag
- No breaking changes to other game modes

## Summary

This refactor provides a solid, accurate collision and movement system that properly matches the original Famidash game behavior. The code is well-organized, documented, and ready for future enhancements like mini mode and dual mode.
