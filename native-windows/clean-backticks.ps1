
# Fix the backtick issues introduced by the previous script

$files = @(
    "WavePhysics_Fresh.partial.cs",
    "RobotPhysics_Fresh.partial.cs",
    "BallPhysics_Fresh.partial.cs",
    "UfoPhysics_Fresh.partial.cs",
    "ShipPhysics_Fresh.partial.cs",
    "CubePhysics_Fresh.partial.cs",
    "NinjaPhysics_Fresh.partial.cs",
    "SpiderPhysics_Fresh.partial.cs",
    "SnakePhysics_Fresh.partial.cs"
)

foreach ($file in $files) {
    $path = "c:\Editor Test\native-windows\$file"
    if (-not (Test-Path $path)) { continue }
    
    $content = Get-Content $path -Raw
    
    # Remove backticks that were added by regex replacement
    $content = $content -replace '`\$', '$'
    
    Set-Content -Path $path -Value $content -Encoding UTF8
    Write-Host "Cleaned: $file"
}

Write-Host "Done!"
