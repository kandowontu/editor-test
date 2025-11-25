# Create a minimal, self-contained publish of FamidashEditor
# Usage: run in PowerShell (Windows PowerShell 5.1 or PowerShell Core)
# This will produce a single-file, trimmed, self-contained Windows x64 publish

param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Project = "FamidashEditor.csproj",
    [string]$OutputDir = "Published-Minimal",
    [switch]$SelfContained = $true,
    [switch]$SingleFile = $true,
    [switch]$Trim = $true
)

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptDir

Write-Host "Creating minimal publish for project: $Project"

# Build publish arguments
$args = @("-c", $Configuration, "-r", $Runtime, "--nologo", "-o", $OutputDir)
if ($SelfContained) { $args += "--self-contained"; $args += "true" }
if ($SingleFile) { $args += "/p:PublishSingleFile=true" }
if ($Trim) { $args += "/p:PublishTrimmed=true" }

# For WPF apps, bundling as single file and trimming can cause issues; user can disable Trim or SingleFile if needed
Write-Host "dotnet publish arguments: $($args -join ' ')"

# Execute publish
$publishCmd = @("dotnet", "publish", $Project) + $args
Write-Host "Running: $($publishCmd -join ' ')
"

$processInfo = New-Object System.Diagnostics.ProcessStartInfo
$processInfo.FileName = "dotnet"
$processInfo.Arguments = "publish `"$Project`" $($args | ForEach-Object { if ($_ -match ' ') { "`"$_`"" } else { $_ } } -join ' ')"
$processInfo.RedirectStandardOutput = $true
$processInfo.RedirectStandardError = $true
$processInfo.UseShellExecute = $false
$processInfo.CreateNoWindow = $true

$proc = New-Object System.Diagnostics.Process
$proc.StartInfo = $processInfo
$proc.Start() | Out-Null

while (-not $proc.HasExited) {
    $out = $proc.StandardOutput.ReadLine()
    if ($out -ne $null) { Write-Host $out }
}

# drain remaining output
while (-not $proc.StandardOutput.EndOfStream) { $out = $proc.StandardOutput.ReadLine(); if ($out) { Write-Host $out } }
while (-not $proc.StandardError.EndOfStream) { $err = $proc.StandardError.ReadLine(); if ($err) { Write-Error $err } }

$exitCode = $proc.ExitCode
if ($exitCode -eq 0) {
    Write-Host "Publish completed successfully. Output in: $(Join-Path $scriptDir $OutputDir)" -ForegroundColor Green
} else {
    Write-Error "Publish failed with exit code $exitCode"
}
