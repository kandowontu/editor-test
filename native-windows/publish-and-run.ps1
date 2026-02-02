# PublishAndRun.ps1
# Publishes a Release build and runs it

param(
    [string]$OutputDir = "ReleaseBuild",
    [switch]$NoRun = $false
)

$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$publishDir = Join-Path $projectDir $OutputDir

Write-Host "Publishing Release build to: $publishDir" -ForegroundColor Cyan

# Clean previous build if it exists
if (Test-Path $publishDir) {
    Write-Host "Removing previous build..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force $publishDir
}

# Publish the release build
Push-Location $projectDir
try {
    Write-Host "Running: dotnet publish -c Release -o $OutputDir" -ForegroundColor Cyan
    Write-Host "This may take a minute..." -ForegroundColor Yellow
    
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    dotnet publish -c Release -o $OutputDir
    $sw.Stop()
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Publish failed with exit code $LASTEXITCODE" -ForegroundColor Red
        exit 1
    }
    
    Write-Host "Publish succeeded in $($sw.Elapsed.TotalSeconds) seconds!" -ForegroundColor Green
    
    # Find and run the executable
    $exePath = Join-Path $publishDir "FamidashEditor.exe"
    
    if (Test-Path $exePath) {
        Write-Host "Executable created at: $exePath" -ForegroundColor Green
        Write-Host "Size: $([math]::Round((Get-Item $exePath).Length / 1MB, 2)) MB" -ForegroundColor Cyan
        
        if (-not $NoRun) {
            Write-Host "Launching application..." -ForegroundColor Cyan
            Start-Process -FilePath $exePath -WindowStyle Normal
            Write-Host "Application launched in background" -ForegroundColor Green
        }
    }
    else {
        Write-Host "ERROR: Executable not found at $exePath" -ForegroundColor Red
        exit 1
    }
}
finally {
    Pop-Location
}
