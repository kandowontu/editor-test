#!/usr/bin/env pwsh
# Fix unindexed array variables in Fresh physics files using careful patterns
# Only apply well-tested, non-overlapping patterns

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
    $originalLines = ($content -split "`n").Count
    
    foreach ($var in $variablesToIndex) {
        # PATTERN 1: Variable followed by shift operator (>> or <<)
        # Example: playerX_fixed >> 8  →  playerX_fixed[currplayer] >> 8
        # Uses negative lookbehind to ensure not already indexed
        $before = $content.Length
        $content = [regex]::Replace($content, "(?<!\[)($var)(\s*(?:>>|<<))", "`$1[currplayer]`$2")
        $after = $content.Length
        if ($before -ne $after) {
            $totalReplacements += 1
        }
        
        # PATTERN 2: Variable assignment (equals without other operators)
        # But be careful to not match == or !=
        # Example: playerVelY_fixed = value  →  playerVelY_fixed[currplayer] = value
        $before = $content.Length
        $content = [regex]::Replace($content, "(?<!\[)($var)(\s*=\s*)(?!=)", "`$1[currplayer]`$2")
        $after = $content.Length
        if ($before -ne $after) {
            $totalReplacements += 1
        }
        
        # PATTERN 3: Variable followed by += -= *= /= %= |= &= ^=
        # Example: playerVelY_fixed += 5  →  playerVelY_fixed[currplayer] += 5
        $before = $content.Length
        $content = [regex]::Replace($content, "(?<!\[)($var)(\s*(?:\+=|-=|\*=|/=|%=|\|=|&=|\^=))", "`$1[currplayer]`$2")
        $after = $content.Length
        if ($before -ne $after) {
            $totalReplacements += 1
        }
        
        # PATTERN 4: Variable in comparisons (>, <, >=, <=, ==, !=)
        # Example: playerVelY_fixed > 0  →  playerVelY_fixed[currplayer] > 0
        $before = $content.Length
        $content = [regex]::Replace($content, "(?<!\[)($var)(\s*(?:>|<|>=|<=|==|!=))", "`$1[currplayer]`$2")
        $after = $content.Length
        if ($before -ne $after) {
            $totalReplacements += 1
        }
        
        # PATTERN 5: Variable used in function calls or casts
        # Example: Convert.ToInt32(playerVelY_fixed)  →  Convert.ToInt32(playerVelY_fixed[currplayer])
        # Pattern: variable followed by ) or ,
        $before = $content.Length
        $content = [regex]::Replace($content, "(?<!\[)($var)(?=\s*[\),])", "`$1[currplayer]")
        $after = $content.Length
        if ($before -ne $after) {
            $totalReplacements += 1
        }
    }
    
    Write-Host "Fixed: $file"
    Set-Content $file $content
}

Write-Host ""
Write-Host "Done! Pattern application completed on all Fresh physics files."
