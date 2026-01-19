# START POS Marker Tool

The START POS marker allows you to test your level from a specific position with synchronized music timing.

## How to Use

### Activating the START POS Tool
1. **Press P** to activate the START POS tool (or click the green diamond button in the toolbar)
2. **Menu**: Tools → START POS Marker
3. The tool button shows a green diamond (◆) labeled "START POS"

### Placing the START POS Marker
1. With the START POS tool active, **click anywhere on the map** to place the marker
2. A green diamond marker appears at the clicked position
3. A message displays the exact world pixel coordinates: "START POS: (X, Y)"

### Clearing the START POS Marker
- **Press Ctrl+P** to remove the marker and return to normal start (X=0)
- The marker is also cleared when loading a new level

### Using the START POS in Simulator
When a START POS marker is set, clicking the Restart button (or restarting the simulator) will:
- Spawn the player at the marker position (both X and Y coordinates)
- Calculate the music timing based on all speed portals from X=0 to the marker position
- **Seek the music playback** to the calculated time offset
- The music will be synchronized as if you had played from the beginning and reached that position

### Visual Indicator
- The START POS marker appears as a **green rectangle** showing the exact 16x16 pixel area where the player will spawn
- Semi-transparent green fill (30% opacity)
- Lime green outline
- Rendered above all other elements (Z-index 2002)
- Scales with zoom level for consistent visibility
- Not grid-locked - can be placed at any pixel position

### Technical Details

**Music Timing Calculation:**
- Scans all speed portals (0x14-0x21) from X=0 to marker X position
- Calculates cumulative travel time based on speed changes
- Default speed is 1x (CUBE_SPEED_X1 = 256 pixels/frame)
- Speed portal map:
  - 0x14: 0.5x speed (128 px/frame)
  - 0x15: 1x speed (256 px/frame)
  - 0x16: 2x speed (512 px/frame)
  - 0x20: 3x speed (768 px/frame)
  - 0x21: 4x speed (1024 px/frame)
- Formula: `time = distance / (speed_fixed / 256 * 60 fps)`

**Music Playback:**
- Uses cached MP3 files from Documents folder (extracted from FamiStudio .fms files)
- Supports time-warped playback for simulator speed adjustments
- Seeks to calculated offset using NAudio's `AudioFileReader.CurrentTime`
- 100ms initialization delay before seeking to ensure audio is ready

### Notes
- The marker is cleared when loading a new level
- Position is stored in world pixel coordinates (not tiles)
- Y position is also used for spawning (useful for testing ceiling sections with inverted gravity)
- Music offset works with the cached MP3 playback system
- Seeking is performed asynchronously after playback starts
