$testDir = "C:\Editor Test\native-windows\bin\Debug\net7.0-windows\win-x64"
for ($i = 0; $i -lt 6; $i++) {
    $candidate = Join-Path $testDir "famidash.bmp"
    $exists = Test-Path $candidate
    Write-Host "[$i] Dir: $testDir"
    Write-Host "    Checking: $candidate - Exists: $exists"
    $parent = Split-Path $testDir -Parent
    if (-not $parent) { 
        Write-Host "    No parent directory!"
        break 
    }
    $testDir = $parent
}
