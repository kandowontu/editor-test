from PIL import Image
import os

# Load the expanded green pad image
img = Image.open('green-pad-expanded.png')
width, height = img.size

print(f"Image size: {width}x{height}")

# Each frame is 16 wide x 32 tall (4 frames across)
frame_width = 16
frame_height = 32

# Calculate number of frames
num_frames = width // frame_width
print(f"Number of frames: {num_frames}")

# Extract each frame
for i in range(num_frames):
    left = i * frame_width
    right = left + frame_width
    top = 0
    bottom = frame_height
    
    frame = img.crop((left, top, right, bottom))
    frame.save(f'green-pad-expanded-frame{i+1}.png')
    print(f"Extracted frame {i+1}: {left},{top} to {right},{bottom}")

print("Done!")
