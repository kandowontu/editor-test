#!/usr/bin/env python3
"""
Extract 5 ship frames from ship-expanded.png
Each frame is 16x16 pixels arranged horizontally
Creates: ship.png, ship2.png, ship5.png, ship6.png

Frame mapping from nesdash.s:
- Frame 0 (ship.png): used for game frames 0/1
- Frame 1 (ship2.png): used for game frame 2
- Frame 2: used for game frames 3/4 (but not extracted - might be internal)
- Frame 3 (ship5.png): used for game frame 5
- Frame 4 (ship6.png): used for game frames 6/7

Actually, based on the SHIP[] array: {Ship_0, Ship_0, Ship_1, Ship_2, Ship_2, Ship_5, Ship_6, Ship_6}
We need 5 distinct frames: 0, 1, 2, 5, 6
These will be extracted and saved as ship.png, ship2.png, ship5.png, ship6.png
(Or we might need to save them differently based on the actual animation system)
"""

from PIL import Image
import os

# Load the expanded ship image
expanded_path = r"c:\Editor Test\ship-expanded.png"
output_dir = r"c:\Editor Test"

# Each frame is 16x16
frame_width = 16
frame_height = 16

# Load image
img = Image.open(expanded_path)
img_width, img_height = img.size

print(f"Loaded ship-expanded.png: {img_width}x{img_height}")

# Extract 5 frames horizontally (0, 1, 2, 3, 4)
# These correspond to the array indices in the NES code
frames = []
for i in range(5):
    x = i * frame_width
    y = 0
    
    # Crop the frame
    frame = img.crop((x, y, x + frame_width, y + frame_height))
    frames.append(frame)
    print(f"Extracted frame {i} from position ({x}, {y})")

# Save frames as ship.png, ship2.png, ship5.png, ship6.png
# Based on SHIP[] = {Ship_0, Ship_0, Ship_1, Ship_2, Ship_2, Ship_5, Ship_6, Ship_6}
# We map: 0→Ship_0, 1→Ship_1, 2→Ship_2, 3→Ship_5, 4→Ship_6

output_files = [
    ("ship.png", 0),      # Ship_0
    ("ship2.png", 1),     # Ship_1
    ("ship5.png", 3),     # Ship_5 (frame index 3 in expanded)
    ("ship6.png", 4),     # Ship_6 (frame index 4 in expanded)
]

for output_name, frame_idx in output_files:
    output_path = os.path.join(output_dir, output_name)
    frames[frame_idx].save(output_path)
    print(f"Saved {output_name}")

print("\nExtraction complete!")
print("Created: ship.png, ship2.png, ship5.png, ship6.png")
