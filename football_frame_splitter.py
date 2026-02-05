#!/usr/bin/env python3
"""
Extract 7 football frames from football-expanded.png
Each frame is 16x16 pixels arranged horizontally
Creates: football.png, football1.png through football6.png (7 frames total)
"""

from PIL import Image
import os

# Load the expanded football image
expanded_path = r"c:\Editor Test\football-expanded.png"
output_dir = r"c:\Editor Test"

# Each frame is 16x16
frame_width = 16
frame_height = 16

# Load image
img = Image.open(expanded_path)
img_width, img_height = img.size

print(f"Loaded football-expanded.png: {img_width}x{img_height}")

# Extract 7 frames horizontally (0-6)
frames = []
for i in range(7):
    x = i * frame_width
    y = 0
    
    # Crop the frame
    frame = img.crop((x, y, x + frame_width, y + frame_height))
    frames.append(frame)
    print(f"Extracted frame {i} from position ({x}, {y})")

# Save frames
filenames = [
    "football.png",
    "football1.png",
    "football2.png",
    "football3.png",
    "football4.png",
    "football5.png",
    "football6.png"
]

for i, (frame, filename) in enumerate(zip(frames, filenames)):
    output_path = os.path.join(output_dir, filename)
    frame.save(output_path)
    print(f"Saved {filename}")

print("\nAll 7 frames extracted successfully!")
