# Fix remaining array indexing issues in SimulatorWindow.xaml.cs
# This script carefully replaces unindexed array variables with [currplayer] indexing

$filePath = "c:\Editor Test\native-windows\SimulatorWindow.xaml.cs"
$content = [System.IO.File]::ReadAllText($filePath)

# Pattern-based replacements - very specific to avoid false matches
# Each pattern includes context to ensure uniqueness

$replacements = @(
    # Simple variable reads with operators (avoid if already has [currplayer])
    @{ old = 'playerX_fixed\s+>>'; new = 'playerX_fixed[currplayer] >>' },
    @{ old = 'playerY_fixed\s+>>'; new = 'playerY_fixed[currplayer] >>' },
    @{ old = 'playerVelY_fixed\s+==\s+0'; new = 'playerVelY_fixed[currplayer] == 0' },
    @{ old = 'playerVelY_fixed\s+>'; new = 'playerVelY_fixed[currplayer] >' },
    @{ old = 'playerVelY_fixed\s+<'; new = 'playerVelY_fixed[currplayer] <' },
    @{ old = 'playerVelY_fixed\s+/'; new = 'playerVelY_fixed[currplayer] /' },
    @{ old = 'playerVelY_fixed\s+\+='; new = 'playerVelY_fixed[currplayer] +=' },
    @{ old = 'playerVelY_fixed\s+-='; new = 'playerVelY_fixed[currplayer] -=' },
    @{ old = 'playerVelY_fixed\s+&'; new = 'playerVelY_fixed[currplayer] &' },
    @{ old = 'gravityFlipped\s+!='; new = 'gravityFlipped[currplayer] !=' },
    @{ old = '!gravityFlipped'; new = '!gravityFlipped[currplayer]' },
    @{ old = 'gravityFlipped\s*\)?$'; new = 'gravityFlipped[currplayer])' },
    @{ old = 'gravityFlipped\s+&&'; new = 'gravityFlipped[currplayer] &&' },
    @{ old = 'gravityFlipped\s+\|\|'; new = 'gravityFlipped[currplayer] ||' },
)

foreach ($replacement in $replacements) {
    # Use negative lookbehind/lookahead to avoid double-indexing
    $pattern = "(?<!\[currplayer\])($($replacement.old))(?!\[currplayer\])"
    $content = [regex]::Replace($content, $pattern, $replacement.new)
}

[System.IO.File]::WriteAllText($filePath, $content)
Write-Host "Fixed remaining array indexing issues"
