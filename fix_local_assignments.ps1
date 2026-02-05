# Fix local variable assignments from array variables
# When a local variable is assigned from an array variable, add [currplayer]

param(
    [string]$FilePath = "c:\Editor Test\native-windows\SimulatorWindow.xaml.cs"
)

$lines = [System.IO.File]::ReadAllLines($FilePath)
$modified = $false
$fixCount = 0

for ($i = 0; $i -lt $lines.Length; $i++) {
    $line = $lines[$i]
    $original = $line
    
    # Skip comments
    if ($line -match '^\s*//') {
        continue
    }
    
    # Skip lines that are array declarations
    if ($line -match '=\s*new\s+(int|bool|double)\s*\[') {
        continue
    }
    
    # Pattern: int xxx = playerVelY_fixed; → int xxx = playerVelY_fixed[currplayer];
    if ($line -match 'int\s+\w+\s*=\s*playerVelY_fixed\s*[;]') {
        $line = $line -replace '= playerVelY_fixed\s*;', '= playerVelY_fixed[currplayer];'
    }
    
    # Pattern: int xxx = cubeRotate (used as scalar in local assignment)
    if ($line -match '(int|var)\s+\w+\s*=\s*(cubeRotate_fixed|shipRotate_fixed|swingcopterRotate_fixed|footballRotate_fixed|cubeRotateMini_fixed)\s*[;]' -and $line -notmatch '\[currplayer\]') {
        # These should have [currplayer] added
        $line = $line -replace '= (cubeRotate_fixed|shipRotate_fixed|swingcopterRotate_fixed|footballRotate_fixed|cubeRotateMini_fixed)\s*;', '= $1[currplayer];'
    }
    
    # Pattern: int xxx -= playerVelY_fixed; (and similar operators)
    if ($line -match '([+\-*/%|&]|<<|>>=|playerVelY_fixed)\s*[;]' -and $line -notmatch '\[currplayer\]') {
        if ($line -match 'playerVelY_fixed\s*[;]') {
            $line = $line -replace 'playerVelY_fixed\s*;', 'playerVelY_fixed[currplayer];'
        }
    }
    
    if ($line -ne $original) {
        $lines[$i] = $line
        $modified = $true
        $fixCount++
        Write-Host "Line $($i+1): Fixed local variable assignment"
    }
}

if ($modified) {
    [System.IO.File]::WriteAllLines($FilePath, $lines)
    Write-Host "File updated successfully - Fixed $fixCount lines"
} else {
    Write-Host "No changes needed"
}
