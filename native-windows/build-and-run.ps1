# Build and Run Script for FamidashEditor
# Closes any running instance, builds the project, and launches the new build

Write-Host "Closing any running FamidashEditor instances..." -ForegroundColor Yellow
Get-Process -Name "FamidashEditor" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

Write-Host "Building FamidashEditor..." -ForegroundColor Cyan
dotnet build

if ($LASTEXITCODE -eq 0) {
    Write-Host "Build succeeded! Launching FamidashEditor..." -ForegroundColor Green
    Start-Process ".\bin\Debug\net7.0-windows\FamidashEditor.exe"
} else {
    Write-Host "Build failed. Not launching application." -ForegroundColor Red
}
