#!/usr/bin/env python3
"""
Cube Animation Frame Splitter for Famidash

The cube animation in Famidash uses a 24-frame rotation system (0-23), but it's
generated from only 7 unique frames using a flip table.

The full 24-frame sequence:
- Index 0-6:   Frames 0-6 (no flip)
- Index 7-11:  Frames 5,4,3,2,1 (vertical flip)
- Index 12-18: Frames 0-6 (both flips)
- Index 19-23: Frames 5,4,3,2,1 (horizontal flip)

For our purposes (360° seamless rotation), we only need the 7 base frames.
The rounding table snaps to the nearest 90° when velocity is 0:
- Snap to frame 0 (0°, 360°) - index 0
- Snap to frame 2 (90°) - index 2
- Snap to frame 4 (180°) - index 4
- Snap to frame 6 (270°) - index 6

Frame meanings:
- Frame 0: Upright (0°)
- Frame 1: 45° clockwise
- Frame 2: 90° clockwise (side)
- Frame 3: 135° clockwise
- Frame 4: 180° (upside down)
- Frame 5: 225° clockwise (or 135° counter-clockwise)
- Frame 6: 270° clockwise (or 90° counter-clockwise - opposite side)
"""

from PIL import Image
import sys

def split_cube_expanded():
    # Load the expanded PNG
    img = Image.open(r"c:\Editor Test\cube-expanded.png")
    width, height = img.size
    
    print(f"Loading cube-expanded.png: {width}x{height}")
    
    # Each frame is 16x16 pixels
    frame_size = 16
    
    # Determine how many frames are in the image
    frames_per_row = width // frame_size
    num_rows = height // frame_size
    total_frames = frames_per_row * num_rows
    
    print(f"Image dimensions: {width}x{height}")
    print(f"Frames per row: {frames_per_row}")
    print(f"Number of rows: {num_rows}")
    print(f"Total frames detected: {total_frames}")
    
    # Extract the 7 frames
    frames = []
    frame_names = [
        "frame_0_upright",
        "frame_1_45cw",
        "frame_2_90cw_side",
        "frame_3_135cw",
        "frame_4_180_upside",
        "frame_5_225cw",
        "frame_6_270cw_opposite"
    ]
    
    for i in range(7):
        # Calculate position in the image
        row = i // frames_per_row
        col = i % frames_per_row
        
        left = col * frame_size
        top = row * frame_size
        right = left + frame_size
        bottom = top + frame_size
        
        # Extract the frame
        frame = img.crop((left, top, right, bottom))
        frames.append(frame)
        
        # Save individual frame
        output_path = f"c:\\Editor Test\\cube_{i:02d}_{frame_names[i]}.png"
        frame.save(output_path)
        print(f"✓ Extracted frame {i}: {frame_names[i]} -> cube_{i:02d}_{frame_names[i]}.png")
    
    print("\n✓ All 7 frames extracted successfully!")
    print("\nFrame rotation guide:")
    print("  Frame 0: Upright (0°, 360°)    - snap point when velocity = 0")
    print("  Frame 1: 45° clockwise")
    print("  Frame 2: 90° clockwise         - snap point")
    print("  Frame 3: 135° clockwise")
    print("  Frame 4: 180° upside down     - snap point")
    print("  Frame 5: 225° clockwise (135° CCW)")
    print("  Frame 6: 270° clockwise        - snap point (opposite side)")
    print("\nCube rotation value (0-23) maps to frame index:")
    print("  Values 0-6:   Frames 0-6 (no flip)")
    print("  Values 7-11:  Frames 5-1 (vertical flip)")
    print("  Values 12-18: Frames 0-6 (both flip)")
    print("  Values 19-23: Frames 5-1 (horizontal flip)")

if __name__ == "__main__":
    try:
        split_cube_expanded()
    except FileNotFoundError:
        print("ERROR: cube-expanded.png not found at c:\\Editor Test\\")
        sys.exit(1)
    except Exception as e:
        print(f"ERROR: {e}")
        sys.exit(1)
