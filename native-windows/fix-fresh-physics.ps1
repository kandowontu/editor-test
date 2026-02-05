#!/usr/bin/env pwsh
# Fix all unindexed array variable uses in Fresh physics files
# Add [currplayer] indexing to: playerX_fixed, playerY_fixed, playerVelY_fixed, dashing, orbed,
# hblocked, jblocked, fblocked, dblocked, ufoOrbed, blackOrbed, orbhitonthisframe

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

# Patterns to replace: Variable names that should be indexed with [currplayer]
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

# For each file, go through and fix unindexed variable uses
$totalReplacements = 0

foreach ($file in $freshPhysicsFiles) {
    $filePath = $file
    if (!(Test-Path $filePath)) {
        Write-Host "File not found: $filePath"
        continue
    }

    $content = Get-Content $filePath -Raw
    $originalContent = $content
    
    foreach ($var in $variablesToIndex) {
        # Pattern 1: Variable followed by shift operator (>>)
        # Match: playerX_fixed >> 8  (not already indexed)
        # Don't match: playerX_fixed[currplayer] >> 8  (already indexed)
        $pattern1 = "(?<!\[)($var)\s*>>"
        $replacement1 = "`$1[currplayer] >>"
        $content = [regex]::Replace($content, $pattern1, $replacement1)
        
        # Pattern 2: Variable followed by shift operator (<<)
        $pattern2 = "(?<!\[)($var)\s*<<"
        $replacement2 = "`$1[currplayer] <<"
        $content = [regex]::Replace($content, $pattern2, $replacement2)
        
        # Pattern 3: Variable followed by comparison or arithmetic operators
        # Examples: playerVelY_fixed > 0, playerVelY_fixed += 5, playerX_fixed = value
        # Match unindexed occurrences
        $pattern3 = "(?<!\[)($var)(\s*(?:[+\-*/%&|^]=|[<>=!]=|[<>=+\-*/%&|^()])"
        $replacement3 = "`$1[currplayer]`$2"
        $content = [regex]::Replace($content, $pattern3, $replacement3)
        
        # Pattern 4: Variable assignment (=) with value
        $pattern4 = "(?<!\[)($var)\s*="
        $replacement4 = "`$1[currplayer] ="
        $content = [regex]::Replace($content, $pattern4, $replacement4)
        
        # Pattern 5: Variable as function parameter or in expressions
        # This is trickier - we need to be careful about already indexed vars
        $pattern5 = "(?<!\[)($var)(?!\[)"  # Variable NOT followed by [
        $replacement5 = "`$1[currplayer]"
        
        # Only apply if not already followed by [
        # Split by lines to be more careful
        $lines = $content -split "`n"
        $newLines = @()
        foreach ($line in $lines) {
            # Skip if line contains already-indexed version
            if ($line -match "\[$var\[currplayer\]" -or $line -match "$var\[currplayer\]") {
                # Already done
                $newLines += $line
            } else {
                # Apply replacements carefully
                # Replace bare variable uses with indexed versions, but avoid double-indexing
                $newLine = $line
                
                # Simple approach: if we see the bare variable name not followed by [, add [currplayer]
                # But be careful about already indexed things
                $matches = [regex]::Matches($newLine, "(?<![a-zA-Z0-9_])($var)(?!\[)")
                if ($matches.Count -gt 0) {
                    # Need to replace from right to left to preserve positions
                    for ($i = $matches.Count - 1; $i -ge 0; $i--) {
                        $match = $matches[$i]
                        $start = $match.Index
                        $length = $match.Length
                        $newLine = $newLine.Substring(0, $start) + "$($var)[currplayer]" + $newLine.Substring($start + $length)
                    }
                }
                $newLines += $newLine
            }
        }
        $content = $newLines -join "`n"
    }
    
    # Count replacements
    $diff = Compare-Object ($originalContent -split "`n") ($content -split "`n")
    if ($diff) {
        $replacementCount = ($diff | Measure-Object).Count
        $totalReplacements += $replacementCount
        Write-Host "Fixed: $file ($replacementCount changes)"
        Set-Content $filePath $content
    } else {
        Write-Host "No changes needed: $file"
    }
}

Write-Host ""
Write-Host "Total changes: $totalReplacements"
Write-Host "Done!"
