# Script to fix array variable indexing in SimulatorWindow.xaml.cs
# This script replaces unindexed array variable usages with [currplayer] indexing

$filePath = "c:\Editor Test\native-windows\SimulatorWindow.xaml.cs"
$content = [System.IO.File]::ReadAllText($filePath)

# Array variables that need fixing
$variables = @(
    'playerX_fixed',
    'playerY_fixed',
    'playerVelY_fixed',
    'cubeRotate_fixed',
    'shipRotate_fixed',
    'swingcopterRotate_fixed',
    'footballRotate_fixed',
    'gravityFlipped',
    'miniMode',
    'orbed',
    'ufoOrbed',
    'blackOrbed',
    'dashing',
    'hblocked',
    'jblocked',
    'fblocked',
    'dblocked',
    'orbhitonthisframe',
    'pogoBounceAnimationCounter',
    'ballAnimationFrameCounter',
    'robotAnimationFrameCounter',
    'spiderAnimationFrameCounter',
    'pogoBounceAnimationFrameAccum',
    'ballAnimationFrameAccum',
    'robotAnimationFrameAccum',
    'spiderAnimationFrameAccum'
)

# Common operators to fix
$patterns = @(
    # Pattern 1: variable >> 8 (bit shift right)
    @{ pattern = '(\s|^)($var)\s*>>\s*8'; replacement = '${1}${2}[currplayer] >> 8' },
    # Pattern 2: variable & 0xFF (bitwise AND)
    @{ pattern = '(\s|^)($var)\s*&\s*0xFF'; replacement = '${1}${2}[currplayer] & 0xFF' },
    # Pattern 3: variable == 0
    @{ pattern = '(\s|^)($var)\s*==\s*0'; replacement = '${1}${2}[currplayer] == 0' },
    # Pattern 4: variable = value (assignment)
    @{ pattern = '(\s)($var)\s*=\s*'; replacement = '${1}${2}[currplayer] = ' },
    # Pattern 5: variable /= value
    @{ pattern = '(\s|^)($var)\s*/='; replacement = '${1}${2}[currplayer] /=' },
    # Pattern 6: variable += value
    @{ pattern = '(\s|^)($var)\s*\+='; replacement = '${1}${2}[currplayer] +=' },
    # Pattern 7: variable -= value
    @{ pattern = '(\s|^)($var)\s*-='; replacement = '${1}${2}[currplayer] -=' }
)

foreach ($var in $variables) {
    Write-Host "Processing: $var"
    foreach ($p in $patterns) {
        $regex = $p.pattern -replace '\$var', $var
        $replacement = $p.replacement -replace '\$var', $var
        
        # Skip if variable is already indexed
        $regex = $regex + "(?!\[currplayer\])"
        
        $content = [regex]::Replace($content, $regex, $replacement)
    }
}

# Write the fixed content back
[System.IO.File]::WriteAllText($filePath, $content)
Write-Host "Fixed array indexing in $filePath"
