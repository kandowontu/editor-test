# Fix remaining currplayer_gravity and currplayer_mini references
$filePath = "c:\Editor Test\native-windows"

# Files that still have undefined references
$filesToFix = @(
    "CubePhysics_Fresh.partial.cs",
    "FreshPhysicsHelpers.partial.cs",
    "NinjaPhysics_Fresh.partial.cs",
    "RobotPhysics_Fresh.partial.cs",
    "ShipPhysics_Fresh.partial.cs"
)

$patterns = @(
    @{ Find = '\(currplayer_mini\s*!=\s*0\)'; Replace = '(miniMode[currplayer])' },
    @{ Find = 'currplayer_mini\s*!=\s*0'; Replace = 'miniMode[currplayer]' },
    @{ Find = '\(currplayer_gravity\s*!=\s*0\)'; Replace = '(!gravityFlipped[currplayer])' },
    @{ Find = 'currplayer_gravity\s*!=\s*0'; Replace = '!gravityFlipped[currplayer]' },
    @{ Find = 'currplayer_gravity\s*==\s*0'; Replace = '!gravityFlipped[currplayer]' },
    @{ Find = 'currplayer_mini\s*='; Replace = 'miniMode[currplayer] =' },
    @{ Find = 'currplayer_gravity\s*='; Replace = 'gravityFlipped[currplayer] =' }
)

$totalFixes = 0

foreach ($file in $filesToFix) {
    $fullPath = Join-Path $filePath $file
    if (Test-Path $fullPath) {
        $content = Get-Content $fullPath -Raw
        $originalContent = $content
        
        foreach ($pattern in $patterns) {
            $matches = [regex]::Matches($content, $pattern.Find)
            $matchCount = $matches.Count
            if ($matchCount -gt 0) {
                $content = [regex]::Replace($content, $pattern.Find, $pattern.Replace)
                $totalFixes += $matchCount
                Write-Host "$file - Fixed $matchCount occurrences"
            }
        }
        
        if ($content -ne $originalContent) {
            Set-Content -Path $fullPath -Value $content
            Write-Host "Updated: $file"
        }
    }
}

Write-Host "Total fixes applied: $totalFixes"
