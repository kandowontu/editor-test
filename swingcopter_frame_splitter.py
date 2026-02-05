#!/usr/bin/env python3
"""
Extract 5 swingcopter frames from swingcopter-expanded.png
Each frame is 16x16 pixels arranged horizontally
Creates 5 unique PNG files that map to 8 game frames:
- swingcopter.png (frame 0) → used for game frames 0/1
- swingcopter1.png (frame 1) → used for game frame 2
- swingcopter2.png (frame 2) → used for game frames 3/4
- swingcopter3.png (frame 3) → used for game frame 5
- swingcopter4.png (frame 4) → used for game frames 6/7
"""

from PIL import Image
import os

# Load the expanded swingcopter image
expanded_path = r"c:\Editor Test\swingcopter-expanded.png"
output_dir = r"c:\Editor Test"

# Each frame is 16x16
frame_width = 16
frame_height = 16

# Load image
img = Image.open(expanded_path)
img_width, img_height = img.size

print(f"Loaded swingcopter-expanded.png: {img_width}x{img_height}")

# Extract 5 frames horizontally (0-4)
frames = []
for i in range(5):
    x = i * frame_width
    y = 0
    
    # Crop the frame
    frame = img.crop((x, y, x + frame_width, y + frame_height))
    frames.append(frame)
    print(f"Extracted frame {i} from position ({x}, {y})")

# Save as unique PNG files
filenames = [
    "swingcopter.png",
    "swingcopter1.png",
    "swingcopter2.png",
    "swingcopter3.png",
    "swingcopter4.png"
]

for i, (frame, filename) in enumerate(zip(frames, filenames)):
    output_path = os.path.join(output_dir, filename)
    frame.save(output_path)
    print(f"Saved {filename}")

print("\nAll 5 unique frames extracted successfully!")
print("Frame mapping: { 0, 0, 1, 2, 2, 3, 4, 4 }")


