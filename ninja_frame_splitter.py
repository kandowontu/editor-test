#!/usr/bin/env python3
"""
Extract 7 16x16 ninja frames from ninja-expanded.png
The image is laid out horizontally: 7 frames * 16 pixels = 112 pixels wide, 16 pixels tall
"""

from PIL import Image
import os

# Load the expanded image
input_path = r"c:\Editor Test\ninja-expanded.png"
output_dir = r"c:\Editor Test"

if not os.path.exists(input_path):
    print(f"Error: {input_path} not found")
    exit(1)

img = Image.open(input_path)
print(f"Loaded image: {img.size} ({img.width}x{img.height})")

# Extract 7 frames, each 16x16
frame_width = 16
frame_height = 16

for i in range(7):
    left = i * frame_width
    top = 0
    right = left + frame_width
    bottom = top + frame_height
    
    frame = img.crop((left, top, right, bottom))
    
    # Save with descriptive name
    output_path = os.path.join(output_dir, f"ninja_{i:02d}_frame_{i}.png")
    frame.save(output_path)
    print(f"Extracted frame {i}: {output_path}")

print("Done!")
