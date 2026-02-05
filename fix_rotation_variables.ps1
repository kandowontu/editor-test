# Fix unindexed array accesses for rotation variables in SimulatorWindow.xaml.cs

param(
    [string]$FilePath = "c:\Editor Test\native-windows\SimulatorWindow.xaml.cs"
)

$lines = [System.IO.File]::ReadAllLines($FilePath)
$modified = $false
$fixCount = 0

for ($i = 0; $i -lt $lines.Length; $i++) {
    $line = $lines[$i]
    $original = $line
    
    # Skip lines that already have the variable indexed with [currplayer]
    if ($line -match '\[(currplayer|0|1)\]') {
        continue
    }
    
    # Skip lines that are comments only
    if ($line -match '^\s*//') {
        continue
    }
    
    # Skip variable declarations
    if ($line -match 'private.*=' -or $line -match '^\s*new\s') {
        continue
    }
    
    # Pattern 1: cubeRotate_fixed used in operations (not in declaration or already indexed)
    if ($line -match 'cubeRotate_fixed\s*[>>&|]|cubeRotate_fixed\s*=\s*\(') {
        $line = $line -replace 'cubeRotate_fixed([^[])', 'cubeRotate_fixed[currplayer]$1'
        $line = $line -replace 'cubeRotate_fixed$', 'cubeRotate_fixed[currplayer]'
    }
    
    # Pattern 2: shipRotate_fixed
    if ($line -match 'shipRotate_fixed\s*[>>&|]|shipRotate_fixed\s*=\s*\(') {
        $line = $line -replace 'shipRotate_fixed([^[])', 'shipRotate_fixed[currplayer]$1'
        $line = $line -replace 'shipRotate_fixed$', 'shipRotate_fixed[currplayer]'
    }
    
    # Pattern 3: swingcopterRotate_fixed
    if ($line -match 'swingcopterRotate_fixed\s*[>>&|]|swingcopterRotate_fixed\s*=\s*\(') {
        $line = $line -replace 'swingcopterRotate_fixed([^[])', 'swingcopterRotate_fixed[currplayer]$1'
        $line = $line -replace 'swingcopterRotate_fixed$', 'swingcopterRotate_fixed[currplayer]'
    }
    
    # Pattern 4: footballRotate_fixed
    if ($line -match 'footballRotate_fixed\s*[>>&|]|footballRotate_fixed\s*=\s*\(') {
        $line = $line -replace 'footballRotate_fixed([^[])', 'footballRotate_fixed[currplayer]$1'
        $line = $line -replace 'footballRotate_fixed$', 'footballRotate_fixed[currplayer]'
    }
    
    # Pattern 5: cubeRotateMini_fixed
    if ($line -match 'cubeRotateMini_fixed\s*[>>&|]|cubeRotateMini_fixed\s*=\s*\(') {
        $line = $line -replace 'cubeRotateMini_fixed([^[])', 'cubeRotateMini_fixed[currplayer]$1'
        $line = $line -replace 'cubeRotateMini_fixed$', 'cubeRotateMini_fixed[currplayer]'
    }
    
    if ($line -ne $original) {
        $lines[$i] = $line
        $modified = $true
        $fixCount++
        Write-Host "Line $($i+1): Fixed rotation variable"
    }
}

if ($modified) {
    [System.IO.File]::WriteAllLines($FilePath, $lines)
    Write-Host "File updated successfully - Fixed $fixCount lines"
} else {
    Write-Host "No changes needed"
}
