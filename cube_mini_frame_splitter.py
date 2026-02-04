#!/usr/bin/env python3
"""
Extract 5 8x8 frames from cube-mini-expanded.png
"""
from PIL import Image
import os

# Load the source image
source_path = "cube-mini-expanded.png"
if not os.path.exists(source_path):
    print(f"Error: {source_path} not found")
    exit(1)

img = Image.open(source_path)
width, height = img.size

print(f"Source image size: {width}x{height}")

# Expected: 5 frames of 8x8 horizontally = 40x8
# Each frame is 8x8 pixels
frame_width = 8
frame_height = 8
num_frames = 5

frames_created = []

for i in range(num_frames):
    left = i * frame_width
    top = 0
    right = left + frame_width
    bottom = top + frame_height
    
    frame = img.crop((left, top, right, bottom))
    output_path = f"cube_mini_{i:02d}_frame_{i}.png"
    frame.save(output_path)
    frames_created.append(output_path)
    print(f"Created {output_path}")

print(f"\nAll {len(frames_created)} frames extracted successfully!")
