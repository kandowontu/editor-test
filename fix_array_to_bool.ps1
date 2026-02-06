# Fix array references to use bool variables instead
$filePath = "c:\Editor Test\native-windows"

$filesToFix = @(
    "CubePhysics_Fresh.partial.cs",
    "FreshPhysicsHelpers.partial.cs",
    "NinjaPhysics_Fresh.partial.cs",
    "RobotPhysics_Fresh.partial.cs",
    "ShipPhysics_Fresh.partial.cs"
)

$patterns = @(
    @{ Find = 'miniMode\[currplayer\]'; Replace = 'miniMode' },
    @{ Find = 'gravityFlipped\[currplayer\]'; Replace = 'gravityFlipped' }
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
                Write-Host "$file - Replaced $matchCount occurrences of '$($pattern.Find)'"
            }
        }
        
        if ($content -ne $originalContent) {
            Set-Content -Path $fullPath -Value $content
            Write-Host "Updated: $file"
        }
    }
}

Write-Host "Total fixes applied: $totalFixes"
