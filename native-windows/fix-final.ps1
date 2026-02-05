#!/usr/bin/env pwsh
# Final conservative fix: only apply simple, bulletproof patterns

$files = @(
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

# These are the variables that MUST be indexed with [currplayer]
$arrayVars = @(
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
    'orbhitonthisframe',
    'pogoBounceAnimationCounter',
    'pogoBounceAnimationFrameAccum',
    'ballAnimationFrameCounter',
    'ballAnimationFrameAccum',
    'robotAnimationFrameCounter',
    'robotAnimationFrameAccum',
    'gravityFlipped',
    'ballSwitched'
)

foreach ($file in $files) {
    if (!(Test-Path $file)) {
        continue
    }

    $content = Get-Content $file -Raw
    $originalLen = $content.Length
    
    # Strategy: Treat each variable independently
    # For each variable, find `varname` NOT followed by `[` and add `[currplayer]`
    
    foreach ($var in $arrayVars) {
        # Build a pattern that matches the variable as a complete token
        # Ensure it's preceded by non-word char and followed by non-[
        
        # This pattern says: match var only if not immediately preceded by [ or word char
        # and not immediately followed by [
        $pattern = "(?<![a-zA-Z0-9_\[])($var)(?!\[)"
        
        # Replace with indexed version
        $content = [regex]::Replace($content, $pattern, "`$1[currplayer]")
    }
    
    if ($content.Length -ne $originalLen) {
        Write-Host "Fixed: $file"
        Set-Content $file $content
    }
}

Write-Host "Done!"
