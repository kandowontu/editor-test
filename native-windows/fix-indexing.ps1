
# Fix indexing in all Fresh physics files
# This script adds [currplayer] indexing to player state array variables

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

$replacements = @(
    # Player position/velocity that need [currplayer] indexing
    @{ Pattern = 'playerX_fixed\s*>>\s*(\d+)'; Replacement = 'playerX_fixed[currplayer] >> `$1' },
    @{ Pattern = 'playerY_fixed\s*>>\s*(\d+)'; Replacement = 'playerY_fixed[currplayer] >> `$1' },
    @{ Pattern = 'playerVelY_fixed\s*>>\s*(\d+)'; Replacement = 'playerVelY_fixed[currplayer] >> `$1' },
    
    @{ Pattern = 'playerX_fixed\s*<<\s*(\d+)'; Replacement = 'playerX_fixed[currplayer] << `$1' },
    @{ Pattern = 'playerY_fixed\s*<<\s*(\d+)'; Replacement = 'playerY_fixed[currplayer] << `$1' },
    @{ Pattern = 'playerVelY_fixed\s*<<\s*(\d+)'; Replacement = 'playerVelY_fixed[currplayer] << `$1' },
    
    @{ Pattern = 'playerVelY_fixed\s*>\s*(\S+)'; Replacement = 'playerVelY_fixed[currplayer] > `$1' },
    @{ Pattern = 'playerVelY_fixed\s*<\s*(\S+)'; Replacement = 'playerVelY_fixed[currplayer] < `$1' },
    @{ Pattern = 'playerVelY_fixed\s*>=\s*(\S+)'; Replacement = 'playerVelY_fixed[currplayer] >= `$1' },
    @{ Pattern = 'playerVelY_fixed\s*<=\s*(\S+)'; Replacement = 'playerVelY_fixed[currplayer] <= `$1' },
    @{ Pattern = 'playerVelY_fixed\s*==\s*(\S+)'; Replacement = 'playerVelY_fixed[currplayer] == `$1' },
    @{ Pattern = 'playerVelY_fixed\s*!=\s*(\S+)'; Replacement = 'playerVelY_fixed[currplayer] != `$1' }
)

foreach ($file in $files) {
    $path = "c:\Editor Test\native-windows\$file"
    if (-not (Test-Path $path)) {
        Write-Host "File not found: $path"
        continue
    }
    
    $content = Get-Content $path -Raw
    $original = $content
    
    foreach ($repl in $replacements) {
        $content = [regex]::Replace($content, $repl.Pattern, $repl.Replacement)
    }
    
    if ($content -ne $original) {
        Set-Content -Path $path -Value $content -Encoding UTF8
        Write-Host "Fixed: $file"
    } else {
        Write-Host "No changes: $file"
    }
}

Write-Host "Done!"
