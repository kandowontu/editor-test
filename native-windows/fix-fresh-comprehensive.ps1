#!/usr/bin/env pwsh
# Fix unindexed array variables with complete coverage for all contexts

$freshPhysicsFiles = @(
    "WavePhysics_Fresh.partial.cs",
    "RobotPhysics_Fresh.partial.cs",
    "BallPhysics_Fresh.partial.cs",
    "UfoPhysics_Fresh.partial.cs",
    "ShipPhysics_Fresh.partial.cs",
    "CubePhysics_Fresh.partial.cs",
    "NinjaPhysics_Fresh.partial.cs",
    "SpiderPhysics_Fresh.partial.cs",
    "FootballPhysics_Fresh.partial.cs",
    "SnakePhysics_Fresh.partial.cs"
)

$variablesToIndex = @(
    'playerX_fixed',
    'playerY_fixed',
    'playerVelY_fixed',
    'dashing',
    'orbed',
    'hblocked',
    'jblocked',
    'fblocked',
    'dblocked',
    'ufoOrbed',
    'blackOrbed',
    'orbhitonthisframe'
)

$totalReplacements = 0

foreach ($file in $freshPhysicsFiles) {
    if (!(Test-Path $file)) {
        Write-Host "File not found: $file"
        continue
    }

    $content = Get-Content $file -Raw
    $beforeLength = $content.Length
    
    foreach ($var in $variablesToIndex) {
        # First escape the variable name for regex if it contains special chars
        $escapedVar = [regex]::Escape($var)
        
        # Strategy: Find all word boundaries where the variable appears NOT followed by [
        # Then add [currplayer] after it
        
        # This pattern matches the variable when:
        # - Not preceded by [ or alphanumeric
        # - Not followed by [ (already indexed)
        # Matches the bare variable itself for capturing
        
        $pattern = "(?<![a-zA-Z0-9_\[])($escapedVar)(?!\[)"
        
        # In the replacement, add [currplayer] right after the variable
        $replacement = "`$1[currplayer]"
        
        $content = [regex]::Replace($content, $pattern, $replacement)
    }
    
    if ($content.Length -ne $beforeLength) {
        $totalReplacements += ($content.Length - $beforeLength) / 15  # Rough estimate
        Write-Host "Fixed: $file"
    } else {
        Write-Host "No changes: $file"
    }
    
    Set-Content $file $content
}

Write-Host ""
Write-Host "Applied comprehensive indexing fixes to all Fresh physics files."
Write-Host "Done!"
