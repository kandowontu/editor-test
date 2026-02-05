
# Fix malformed patterns with backticks before numbers and operators

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
    
    # Remove backticks before digits
    $content = $content -replace '`(\d+)', '$1'
    
    # Remove backticks before closing parens/brackets
    $content = $content -replace '`(\))', '$1'
    
    Set-Content -Path $path -Value $content -Encoding UTF8
    Write-Host "Fixed: $file"
}

Write-Host "Done!"
