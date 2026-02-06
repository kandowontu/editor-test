# Fix undefined currplayer_mini and currplayer_gravity references
# Replace with actual class member variables

param(
    [string]$Folder = "c:\Editor Test\native-windows"
)

$files = Get-ChildItem -Path $Folder -Filter "*.partial.cs" -Recurse
$totalFixed = 0

foreach ($file in $files) {
    $content = [System.IO.File]::ReadAllText($file.FullName)
    $original = $content
    $fileFixed = 0
    
    # Replace currplayer_mini != 0 with miniMode[currplayer]
    if ($content -match 'currplayer_mini\s*!=\s*0') {
        $content = $content -replace '\(currplayer_mini\s*!=\s*0\)', '(miniMode[currplayer])'
        $content = $content -replace 'currplayer_mini\s*!=\s*0', 'miniMode[currplayer]'
        $fileFixed++
    }
    
    # Replace currplayer_gravity != 0 with gravityFlipped[currplayer]
    if ($content -match 'currplayer_gravity\s*!=\s*0') {
        $content = $content -replace '\(currplayer_gravity\s*!=\s*0\)', '(!gravityFlipped[currplayer])'
        $content = $content -replace 'currplayer_gravity\s*!=\s*0', '!gravityFlipped[currplayer]'
        $fileFixed++
    }
    
    # Replace currplayer_gravity == 0 with !gravityFlipped[currplayer]
    if ($content -match 'currplayer_gravity\s*==\s*0') {
        $content = $content -replace 'currplayer_gravity\s*==\s*0', '!gravityFlipped[currplayer]'
        $fileFixed++
    }
    
    # Replace currplayer_mini assignments
    if ($content -match 'currplayer_mini\s*=') {
        $content = $content -replace 'currplayer_mini\s*=\s*\(byte\)', 'miniMode[currplayer] ='
        $content = $content -replace 'currplayer_mini\s*=', 'miniMode[currplayer] ='
        $fileFixed++
    }
    
    # Replace currplayer_gravity assignments  
    if ($content -match 'currplayer_gravity\s*=') {
        $content = $content -replace 'currplayer_gravity\s*=\s*\(byte\)', 'gravityFlipped[currplayer] ='
        $content = $content -replace 'currplayer_gravity\s*=', 'gravityFlipped[currplayer] ='
        $fileFixed++
    }
    
    if ($content -ne $original) {
        [System.IO.File]::WriteAllText($file.FullName, $content)
        Write-Host "$($file.Name): Fixed $fileFixed occurrences"
        $totalFixed += $fileFixed
    }
}

Write-Host "Total fixes applied: $totalFixed"
