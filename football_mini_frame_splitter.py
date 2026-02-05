#!/usr/bin/env python3
"""Extract 7 frames from football-mini-expanded.png (8x8 each)"""

from PIL import Image
import os

def split_football_mini_frames():
    input_path = "football-mini-expanded.png"
    
    if not os.path.exists(input_path):
        print(f"Error: {input_path} not found")
        return
    
    img = Image.open(input_path)
    print(f"Image size: {img.size}")
    
    # Extract 7 frames of 8x8 each
    frame_width = 8
    frame_height = 8
    
    for i in range(7):
        left = i * frame_width
        top = 0
        right = left + frame_width
        bottom = top + frame_height
        
        frame = img.crop((left, top, right, bottom))
        
        # Save with proper naming
        if i == 0:
            output_name = "football-mini.png"
        else:
            output_name = f"football-mini{i}.png"
        
        frame.save(output_name)
        print(f"Extracted frame {i}: {output_name}")
    
    print("Done! Created football-mini.png through football-mini6.png")

if __name__ == "__main__":
    split_football_mini_frames()
