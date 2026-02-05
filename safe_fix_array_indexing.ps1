# Safe line-by-line replacement for array indexing
# This processes the file carefully to avoid false matches

param(
    [string]$FilePath = "c:\Editor Test\native-windows\SimulatorWindow.xaml.cs"
)

$lines = [System.IO.File]::ReadAllLines($FilePath)
$modified = $false

for ($i = 0; $i -lt $lines.Length; $i++) {
    $line = $lines[$i]
    $original = $line
    
    # Only process lines with class member array variables
    if ($line -match '(playerX_fixed|playerY_fixed|playerVelY_fixed|gravityFlipped|miniMode|cubeRotate|shipRotate|swingcopter|footballRotate|orbed|dashing|hblocked|jblocked|fblocked|dblocked)') {
        
        # Skip lines that are variable declarations (lines with "new")
        if ($line -match '=\s*new') {
            continue
        }
        
        # Skip lines that already have [currplayer]
        if ($line -match '\[currplayer\]') {
            continue
        }
        
        # Skip lines that are comments
        if ($line -match '^\s*//') {
            continue
        }
        
        # Now apply safe replacements with word boundaries
        
        # Pattern 1: playerX_fixed >> (must be word boundary after)
        if ($line -match 'playerX_fixed\s+>>') {
            $line = $line -replace 'playerX_fixed(\s+>>)', 'playerX_fixed[currplayer]$1'
        }
        
        # Pattern 2: playerY_fixed >>
        if ($line -match 'playerY_fixed\s+>>') {
            $line = $line -replace 'playerY_fixed(\s+>>)', 'playerY_fixed[currplayer]$1'
        }
        
        # Pattern 3: playerVelY_fixed operators
        if ($line -match 'playerVelY_fixed\s+(==|!=|<|>|<<|>>|&|\+|-|\|)') {
            $line = $line -replace 'playerVelY_fixed(\s+(==|!=|<|>|<<|>>|&|\+|-|\|))', 'playerVelY_fixed[currplayer]$1'
        }
        
        # Pattern 4: miniMode
        if ($line -match 'miniMode\b') {
            $line = $line -replace 'miniMode(\W)', 'miniMode[currplayer]$1'
        }
        
        # Pattern 5: gravityFlipped
        if ($line -match 'gravityFlipped\b' -and $line -notmatch 'gravityFlipped\[') {
            $line = $line -replace 'gravityFlipped(\W)', 'gravityFlipped[currplayer]$1'
            $line = $line -replace '^(!gravityFlipped)', '!gravityFlipped[currplayer]'
        }
        
        if ($line -ne $original) {
            $lines[$i] = $line
            $modified = $true
            Write-Host "Line $($i+1): Fixed array indexing"
        }
    }
}

if ($modified) {
    [System.IO.File]::WriteAllLines($FilePath, $lines)
    Write-Host "File updated successfully"
} else {
    Write-Host "No changes needed"
}
