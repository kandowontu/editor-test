#!/usr/bin/env python3
# Remove TITLEDEMO section from GPATHDAT.ASM

import os

filepath = r"c:\Editor Test\ultrastarfox2\SF2\PATH\GPATHDAT.ASM"

# Read the file
with open(filepath, 'r', encoding='utf-8') as f:
    lines = f.readlines()

print(f"Total lines in file: {len(lines)}")
print(f"Line 2866 (0-indexed): {lines[2865][:50] if len(lines) > 2865 else 'N/A'}")
print(f"Line 2867 (0-indexed): {lines[2866][:50] if len(lines) > 2866 else 'N/A'}")
print(f"Line 3829 (0-indexed): {lines[3829][:50] if len(lines) > 3829 else 'N/A'}")
print(f"Line 3830 (0-indexed): {lines[3830][:50] if len(lines) > 3830 else 'N/A'}")

# Lines to remove: 2867 (1-indexed) to 3830 (1-indexed) = array indices 2866 to 3829
# Keep lines 0-2865 and from 3830 onward
kept_lines = lines[:2866]

# Add the comment about moved section  
kept_lines.append('\n')
kept_lines.append(';	TITLE DEMO - MOVED TO PATH\\PATH3DAT.ASM (PATHS3 BANK 37)\n')
kept_lines.append('\n')

# Add everything from line 3830 onward (array index 3829)
kept_lines.extend(lines[3829:])

print(f"After edit: {len(kept_lines)} lines")

# Write back
with open(filepath, 'w', encoding='utf-8') as f:
    f.writelines(kept_lines)

print("TITLEDEMO section removed successfully")
