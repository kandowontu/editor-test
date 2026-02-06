# Fix orb buffer array indexing
# Add [currplayer] to orbBufferActive, orbActivationConsumedThisPress, orbHoldConsumed, orbHoldConsumedKeyStillDown, orbHoldSuppressing

param(
    [string]$FilePath = "c:\Editor Test\native-windows"
)

$files = @(
    "SimulatorWindow.xaml.cs",
    "BallPhysics_Fresh.partial.cs",
    "FootballPhysics_Fresh.partial.cs",
    "OrbSystem.partial.cs"
)

$variables = @(
    "orbBufferActive",
    "orbActivationConsumedThisPress", 
    "orbHoldConsumed",
    "orbHoldConsumedKeyStillDown",
    "orbHoldSuppressing"
)

foreach ($file in $files) {
    $path = Join-Path $FilePath $file
    if (!(Test-Path $path)) {
        Write-Host "File not found: $path"
        continue
    }
    
    $content = Get-Content $path -Raw
    $originalLen = $content.Length
    
    foreach ($var in $variables) {
        # Match variable that is NOT followed by [ or assignment to an array
        # Pattern: variable name followed by = false/true or used in expressions
        
        # Skip declaration lines (lines with "new bool[2]")
        # Pattern 1: variable followed by = true/false (not in declaration)
        $pattern = "(?<![a-zA-Z0-9_\[])\b$var\s*="
        $replacement = "$var`[currplayer] ="
        
        # Only replace if not already indexed
        $testPattern = "$var\[currplayer\]"
        if ($content -notmatch [regex]::Escape($testPattern)) {
            $content = [regex]::Replace($content, $pattern, $replacement)
            Write-Host "Updated assignments in $file for $var"
        }
        
        # Pattern 2: variable used in conditionals (!variable, !variable &&, etc)
        $pattern2 = "!$var(\s|[&\|()])"
        $replacement2 = "!$var`[currplayer]`$1"
        
        if ($content -match $pattern2) {
            $content = [regex]::Replace($content, $pattern2, $replacement2)
            Write-Host "Updated conditionals in $file for $var"
        }
        
        # Pattern 3: variable in boolean expressions (var &&, var ||, etc)
        $pattern3 = "(?<![a-zA-Z0-9_\[])\b$var\s+&&"
        $replacement3 = "$var`[currplayer] &&"
        
        if ($content -match $pattern3) {
            $content = [regex]::Replace($content, $pattern3, $replacement3)
            Write-Host "Updated && expressions in $file for $var"
        }
    }
    
    if ($content.Length -ne $originalLen) {
        Set-Content $path $content
        Write-Host "Updated: $file"
    }
}

Write-Host "Done"
