#!/usr/bin/env pwsh
# Identify unindexed array variable uses  in Fresh physics files

$freshPhysicsFiles = @(
    "BallPhysics_Fresh.partial.cs"
)

$arrayVariables = @(
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

foreach ($file in $freshPhysicsFiles) {
    if (!(Test-Path $file)) {
        Write-Host "File not found: $file"
        continue
    }

    $content = Get-Content $file -Raw
    $lines = $content -split "`n"
    
    Write-Host "=== Checking $file ==="
    
    foreach ($var in $arrayVariables) {
        # Find lines with the variable NOT followed by [
        # Use a simple regex: variable followed by space/operator/paren, but not [
        
        $pattern = "\b$var\s*(?![\[])"
        $matches = $content | Select-String $pattern  
        
        if ($matches.Count -gt 0) {
            Write-Host "Found $($matches.Count) potential unindexed uses of $var"
        }
    }
}
