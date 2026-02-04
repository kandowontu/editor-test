#!/usr/bin/env python3
"""
Extract 5 8x8 mini ninja frames from ninja-mini-expanded.png
The image is laid out horizontally: 5 frames * 8 pixels = 40 pixels wide, 8 pixels tall
"""

from PIL import Image
import os

# Load the expanded image
input_path = r"c:\Editor Test\ninja-mini-expanded.png"
output_dir = r"c:\Editor Test"

if not os.path.exists(input_path):
    print(f"Error: {input_path} not found")
    exit(1)

img = Image.open(input_path)
print(f"Loaded image: {img.size} ({img.width}x{img.height})")

# Extract 5 frames, each 8x8
frame_width = 8
frame_height = 8

for i in range(5):
    left = i * frame_width
    top = 0
    right = left + frame_width
    bottom = top + frame_height
    
    frame = img.crop((left, top, right, bottom))
    
    # Save with descriptive name
    output_path = os.path.join(output_dir, f"ninja_mini_{i:02d}_frame_{i}.png")
    frame.save(output_path)
    print(f"Extracted frame {i}: {output_path}")

print("Done!")
