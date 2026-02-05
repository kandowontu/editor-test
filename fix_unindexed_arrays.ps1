# Comprehensive fix for all unindexed array variable accesses in SimulatorWindow
# Fixes assignments, comparisons, and operators that use array variables

param(
    [string]$FilePath = "c:\Editor Test\native-windows\SimulatorWindow.xaml.cs"
)

$lines = [System.IO.File]::ReadAllLines($FilePath)
$modified = $false
$fixCount = 0

# Variables that are now arrays and need [currplayer] indexing
$arrayVars = @(
    'playerVelY_fixed', 'dashing', 'orbed', 'ufoOrbed', 'blackOrbed',
    'hblocked', 'jblocked', 'fblocked', 'dblocked', 'miniMode', 'gravityFlipped',
    'orbhitonthisframe', 'pogoBounceAnimationCounter', 'ballAnimationFrameCounter',
    'robotAnimationFrameCounter', 'spiderAnimationFrameCounter',
    'pogoBounceAnimationFrameAccum', 'ballAnimationFrameAccum',
    'robotAnimationFrameAccum', 'spiderAnimationFrameAccum'
)

for ($i = 0; $i -lt $lines.Length; $i++) {
    $line = $lines[$i]
    $original = $line
    
    # Skip comments and declarations
    if ($line -match '^\s*//') {
        continue
    }
    if ($line -match 'private.*=' -or $line -match 'new\s+(int|bool|double)\s*\[') {
        continue
    }
    
    # For each array variable, check if it's used without [currplayer]
    foreach ($var in $arrayVars) {
        # Skip if already indexed
        if ($line -match "$var\s*\[") {
            continue
        }
        
        # Pattern 1: xxx = variable; (assignment)
        if ($line -match "= $var\s*[;,)]") {
            $line = $line -replace "= $var([;,)])", "= $var[currplayer]`$1"
        }
        
        # Pattern 2: variable = value; (assignment TO variable)
        if ($line -match "$var\s*= " -and $line -notmatch "$var\s*=\s*\[\s*") {
            $line = $line -replace "$var(\s*=)", "$var[currplayer]`$1"
        }
        
        # Pattern 3: xxx += variable; (compound assignment)
        if ($line -match "([+\-*/&|]|<<|>>)= $var\s*[;)]") {
            $line = $line -replace "([+\-*/&|]|<<|>>=) $var([;)])", "`$1= $var[currplayer]`$2"
        }
        
        # Pattern 4: variable += value; (compound assignment TO variable)
        if ($line -match "$var\s*([+\-*/&|]|<<|>>)=" -and $line -notmatch "$var\s*\[") {
            $line = $line -replace "$var(\s*[+\-*/&|]|<<|>>=)", "$var[currplayer]`$1"
        }
        
        # Pattern 5: if (variable) comparisons
        if ($line -match "if\s*\(\s*$var" -and $line -notmatch "$var\s*\[") {
            $line = $line -replace "\($var", "($var[currplayer]"
        }
        
        # Pattern 6: variable != value; or variable == value;
        if ($line -match "$var\s*(==|!=)" -and $line -notmatch "$var\s*\[") {
            $line = $line -replace "$var(\s*(==|!=))", "$var[currplayer]`$1"
        }
        
        # Pattern 7: variable ? xxx : yyy;
        if ($line -match "$var\s*\?" -and $line -notmatch "$var\s*\[") {
            $line = $line -replace "$var(\s*\?)", "$var[currplayer]`$1"
        }
        
        # Pattern 8: !variable
        if ($line -match "!\s*$var" -and $line -notmatch "$var\s*\[") {
            $line = $line -replace "!\s*$var\b", "!$var[currplayer]"
        }
    }
    
    if ($line -ne $original) {
        $lines[$i] = $line
        $modified = $true
        $fixCount++
        Write-Host "Line $($i+1): Fixed unindexed array access"
    }
}

if ($modified) {
    [System.IO.File]::WriteAllLines($FilePath, $lines)
    Write-Host "File updated successfully - Fixed $fixCount lines"
} else {
    Write-Host "No changes needed"
}
