#!/usr/bin/env python3
from PIL import Image
import os

# Open the expanded ship-mini image
img_path = "ship-mini-expanded.png"
if not os.path.exists(img_path):
    print(f"Error: {img_path} not found")
    exit(1)

img = Image.open(img_path)
print(f"Loaded {img_path}: {img.size}")

# Extract 5 frames of 8x8 each
frame_size = 8
frames = []

for i in range(5):
    x = i * frame_size
    y = 0
    frame = img.crop((x, y, x + frame_size, y + frame_size))
    frames.append(frame)
    
    # Save frame as ship_mini<N>.png (or ship-mini.png for frame 0)
    if i == 0:
        output_name = "ship-mini.png"
    else:
        output_name = f"ship-mini{i}.png"
    
    frame.save(output_name)
    print(f"Saved {output_name} ({frame_size}x{frame_size})")

print(f"\nExtracted 5 frames successfully!")
print("Files created:")
print("  - ship-mini.png (frame 0)")
print("  - ship-mini1.png (frame 1)")
print("  - ship-mini2.png (frame 2)")
print("  - ship-mini3.png (frame 3)")
print("  - ship-mini4.png (frame 4)")
