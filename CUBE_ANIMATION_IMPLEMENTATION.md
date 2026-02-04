# Cube Animation Implementation for Simulator

## Overview
Integrated the NES Famidash cube animation logic from `nesdash.s` into the C# simulator. The cube performs a smooth 360° rotation animation based on velocity using 7 unique sprite frames that generate 24 total frames through horizontal and vertical mirroring.

## Implementation Details

### Tables (from nesdash.s)

#### `DrawcubeRoundingTable`
Maps cube rotation indices to rounding adjustments for snapping to nearest 90° when velocity = 0.

```csharp
private static readonly int[] DrawcubeRoundingTable = new int[]
{
    0, -1, -2, 3, 2, 1,  // First half
    0, -1, -2, 3, 2, 1,  // Doubled
    -24                    // Edge case
};
```

#### `DrawcubeSpriteTable`
Maps `cubeRotate` index (0-23) to sprite frame (0-6) with flip flags (bits 6-7).

```
Values 0-6:   Frames 0-6 (NOFLIP = 0x00)
Values 7-11:  Frames 5-1 (V_FLIP = 0x80)
Values 12-18: Frames 0-6 (HVFLIP = 0xC0)
Values 19-23: Frames 5-1 (H_FLIP = 0x40)
```

### State Variables

- **`cubeRotate`** (int): Current rotation index (0-23), maps to animation frame
- **`CUBE_GRAVITY_LO`** (const): Rotation increment per frame (0x6B)

### Methods

#### `UpdateCubeRotation()`
Called every physics frame for Cube (mode 0), Robot (mode 4), and Ninja (mode 8).

**Logic:**
- **Velocity = 0**: Snap to nearest 90° snap point (0, 6, 12, 18) using rounding table
- **Velocity ≠ 0**: Increment/decrement based on gravity direction
  - Normal gravity (down): `cubeRotate += 0x6B`
  - Inverted gravity (up): `cubeRotate -= 0x6B`
- **Capped at 0-23** range (wraps around)

#### `GetCubeSpriteFrame()`
Returns the frame index (bits 0-2) and flip flags (bits 6-7) for current rotation.

```csharp
private int GetCubeSpriteFrame()
{
    if (cubeRotate >= 0 && cubeRotate < DrawcubeSpriteTable.Length)
        return DrawcubeSpriteTable[cubeRotate];
    return 0;  // Default
}
```

### Integration Points

1. **Physics Loop** (`SimulateNumericStep`)
   - Called after physics functions for modes 0, 4, 8
   - Updates rotation state before rendering

2. **Player Reset** (`Restart`/`InitializeLevel`)
   - Resets `cubeRotate = 0` when player respawns
   - Ensures upright frame at level start

3. **Sprite Rendering** (future)
   - `GetCubeSpriteFrame()` returns frame + flip info
   - Can be used to crop/display correct animation frame from sprite sheets

## Frame Meaning

The 7 base frames represent rotation around the Z-axis:

- **Frame 0**: 0° (Upright) - snap point
- **Frame 1**: ~51° clockwise
- **Frame 2**: ~102° (Side) - snap point  
- **Frame 3**: ~153° clockwise
- **Frame 4**: ~204° (Upside down) - snap point
- **Frame 5**: ~255° clockwise
- **Frame 6**: ~306° (Opposite side) - snap point

The 24-frame sequence creates smooth 360° rotation through mirroring:
- Frames 0-6 play forward
- Frames 7-11 play reversed vertically
- Frames 12-18 play reversed both ways
- Frames 19-23 play reversed horizontally

## Files Modified

- **SimulatorWindow.xaml.cs**
  - Added `cubeRotate` state variable
  - Added `DrawcubeRoundingTable` and `DrawcubeSpriteTable` constants
  - Added `UpdateCubeRotation()` method
  - Added `GetCubeSpriteFrame()` method
  - Integrated calls in `SimulateNumericStep()` for modes 0, 4, 8
  - Reset in `RestartLevel()`/initialization

## Extracted Frame Assets

Created individual 16x16 frame PNGs from `cube-expanded.png`:
- `cube_00_frame_0_upright.png`
- `cube_01_frame_1_45cw.png`
- `cube_02_frame_2_90cw_side.png`
- `cube_03_frame_3_135cw.png`
- `cube_04_frame_4_180_upside.png`
- `cube_05_frame_5_225cw.png`
- `cube_06_frame_6_270cw_opposite.png`

## Testing Notes

The rotation logic should now:
1. ✅ Snap cube to nearest 90° when velocity is 0
2. ✅ Rotate smoothly when moving (velocity ≠ 0)
3. ✅ Respect gravity direction (normal vs inverted)
4. ✅ Wrap correctly at rotation boundaries
5. ✅ Reset to frame 0 on respawn

Next step: Integrate sprite frame rendering to actually display the animated frames during gameplay.
