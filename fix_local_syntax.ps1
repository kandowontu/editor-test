# Careful fix for local variable declarations with invalid array syntax
# These are specific, targeted replacements for the 3 known problem lines

param(
    [string]$FilePath = "c:\Editor Test\native-windows\SimulatorWindow.xaml.cs"
)

$content = [System.IO.File]::ReadAllText($FilePath)

# Fix 1: Line ~1813 - bool newminiMode[currplayer] = isMiniPortal;
# This should be: bool newMiniMode = isMiniPortal;
$content = $content -replace 'bool newminiMode\[currplayer\]\s*=\s*isMiniPortal;', 'bool newMiniMode = isMiniPortal;'

# Fix 2 & 3: Lines ~1938-1941 with miniMode[currplayer] and gravityFlipped[currplayer] local declarations
# These should NOT have [currplayer] since they're local variables
# bool miniMode[currplayer] = (currplayer_mini != 0); → bool miniMode_temp = (currplayer_mini != 0);
# bool gravityFlipped[currplayer] = ... → bool gravityFlipped_temp = ...

# But we need to be careful - these variables are referenced later, so we need to update those references too
# Actually, looking at the code context, these are references to the class member miniMode/gravityFlipped
# So the code should just use miniMode[currplayer] and gravityFlipped[currplayer] directly without local declaration

# Find the block and replace - look for the pattern more carefully
$pattern = "bool miniMode\[currplayer\]\s*=\s*\(currplayer_mini != 0\);\s*bool gravityFlipped\[currplayer\]\s*=\s*\(currplayer_gravity != 0\);"
$replacement = "bool isMini = (currplayer_mini != 0);" + [Environment]::NewLine + "                bool isGravityFlipped = (currplayer_gravity != 0);"

$content = $content -replace $pattern, $replacement

# Now update the references to these vars in the same block
# miniMode[currplayer] ? → isMini ?
# gravityFlipped[currplayer] ? → isGravityFlipped ?
# But only in the CheckDeathCollision function block

# Find "Use actual collision hitbox size" comment and the block following it
# Pattern: After "Use actual collision hitbox size (15x15 for normal, 8x7 for mini)"
# Replace miniMode[currplayer] with isMini and gravityFlipped[currplayer] with isGravityFlipped in the next ~30 lines

# Actually, let's search more specifically for the exact problem
# The safest approach: just remove the [currplayer] from the declarations

$content = $content -replace 'bool miniMode\[currplayer\]\s*=\s*\(\s*currplayer_mini', 'bool isMini = (currplayer_mini'
$content = $content -replace 'bool gravityFlipped\[currplayer\]\s*=\s*\(\s*currplayer_gravity', 'bool isGravityFlipped = (currplayer_gravity'

# Update references
$content = $content -replace 'miniMode\[currplayer\]\s*\?', 'isMini ?'
$content = $content -replace 'miniMode\[currplayer\]\s*&&', 'isMini &&'
$content = $content -replace '!gravityFlipped\[currplayer\]', '!isGravityFlipped'
$content = $content -replace 'gravityFlipped\[currplayer\]\s*\?', 'isGravityFlipped ?'
$content = $content -replace 'gravityFlipped\[currplayer\]\s*&&', 'isGravityFlipped &&'

[System.IO.File]::WriteAllText($FilePath, $content)
Write-Host "File updated successfully"
