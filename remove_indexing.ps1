# Remove [currplayer] indexing from miniMode and gravityFlipped
$filePath = "c:\Editor Test\native-windows"

# Get all partial.cs files that reference miniMode or gravityFlipped with indexing
$allPartialFiles = Get-ChildItem -Path $filePath -Filter "*.partial.cs" | Select-Object -ExpandProperty FullName

$patterns = @(
    @{ Find = 'miniMode\[currplayer\]'; Replace = 'miniMode' },
    @{ Find = 'gravityFlipped\[currplayer\]'; Replace = 'gravityFlipped' }
)

$totalFixes = 0

foreach ($file in $allPartialFiles) {
    $content = Get-Content $file -Raw
    $originalContent = $content
    $fileMod = 0
    
    foreach ($pattern in $patterns) {
        $matches = [regex]::Matches($content, [regex]::Escape($pattern.Find))
        $matchCount = $matches.Count
        if ($matchCount -gt 0) {
            $content = $content -replace [regex]::Escape($pattern.Find), $pattern.Replace
            $totalFixes += $matchCount
            $fileMod += $matchCount
        }
    }
    
    if ($content -ne $originalContent) {
        Set-Content -Path $file -Value $content
        $shortName = Split-Path -Leaf $file
        Write-Host "$shortName - Fixed $fileMod occurrences"
    }
}

Write-Host "Total fixes applied: $totalFixes"
