# Revert array references
$filePath = "c:\Editor Test\native-windows"

$filesToFix = @(
    "CubePhysics_Fresh.partial.cs",
    "FreshPhysicsHelpers.partial.cs",
    "NinjaPhysics_Fresh.partial.cs",
    "RobotPhysics_Fresh.partial.cs",
    "ShipPhysics_Fresh.partial.cs"
)

$patterns = @(
    @{ Find = '([^[])(miniMode)([^[]|\s|\]|;|,|\)|\])'; Replace = '${1}miniMode[currplayer]${3}' },
    @{ Find = '([^[])(gravityFlipped)([^[]|\s|\]|;|,|\)|\])'; Replace = '${1}gravityFlipped[currplayer]${3}' }
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
            }
        }
        
        if ($content -ne $originalContent) {
            Set-Content -Path $fullPath -Value $content
            Write-Host "Reverted: $file"
        }
    }
}

Write-Host "Total reverts applied: $totalFixes"
