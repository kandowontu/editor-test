param(
    [int]$ThrottleLimit = 8,
    [switch]$Coins
)

# Build once up front
Write-Host "Building pf-test (Release)..."
Push-Location "$PSScriptRoot"
dotnet build -c Release 2>&1 | Out-Null
Pop-Location

$exe = "$PSScriptRoot\bin\Release\net8.0-windows\win-x64\PfTest.exe"
if (!(Test-Path $exe)) { Write-Error "Build output not found: $exe"; exit 1 }

$levels = @(
	"akrile","gameover","icdx","jawbreaker","shardscapes","silentcircles","tetrix","madness"
)

$tmxDir = "C:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE"
$tmpDir = Join-Path $env:TEMP "pf-test-parallel"
if (Test-Path $tmpDir) { Remove-Item $tmpDir -Recurse -Force }
New-Item $tmpDir -ItemType Directory -Force | Out-Null

Write-Host "Running $($levels.Count) levels with $ThrottleLimit parallel processes..."
$sw = [System.Diagnostics.Stopwatch]::StartNew()

# Launch processes in batches using Start-Process + output files
$running = @{}  # level -> Process object
$queue = [System.Collections.Queue]::new($levels)
$passed = 0; $failed = 0; $errors = @()
$done = 0

while ($queue.Count -gt 0 -or $running.Count -gt 0) {
    # Fill up to ThrottleLimit
    while ($running.Count -lt $ThrottleLimit -and $queue.Count -gt 0) {
        $lvl = $queue.Dequeue()
        $tmx = Join-Path $tmxDir "$lvl.tmx"
        $outFile = Join-Path $tmpDir "$lvl.txt"
        $argStr = "`"$tmx`""
        if ($Coins) { $argStr += " --coins" }
        $proc = Start-Process -FilePath $exe -ArgumentList $argStr `
            -NoNewWindow -PassThru `
            -RedirectStandardOutput $outFile `
            -RedirectStandardError (Join-Path $tmpDir "$lvl.err.txt")
        $running[$lvl] = $proc
    }

    # Check for completed processes
    $finished = @()
    foreach ($kv in $running.GetEnumerator()) {
        if ($kv.Value.HasExited) { $finished += $kv.Key }
    }
    foreach ($lvl in $finished) {
        $done++
        $outFile = Join-Path $tmpDir "$lvl.txt"
        $errFile = Join-Path $tmpDir "$lvl.err.txt"
        $out = ""
        if (Test-Path $outFile) { $out += Get-Content $outFile -Raw -ErrorAction SilentlyContinue }
        if (Test-Path $errFile) { $out += Get-Content $errFile -Raw -ErrorAction SilentlyContinue }
        $lines = ($out -split "`n") | Where-Object { $_ -match 'Result:|Message:' }
        Write-Host "--- $lvl --- [$done/$($levels.Count)]" -ForegroundColor Cyan
        if ($lines) { $lines | ForEach-Object { Write-Host $_ } }
        if ($out -match 'Result: SUCCESS') { $passed++ }
        else { $failed++; $errors += $lvl }
        $running.Remove($lvl)
    }

    if ($running.Count -ge $ThrottleLimit -or ($queue.Count -eq 0 -and $running.Count -gt 0)) {
        Start-Sleep -Milliseconds 500
    }
}

$sw.Stop()
Write-Host ""
Write-Host "===== SUMMARY =====" -ForegroundColor Yellow
Write-Host "Total: $($levels.Count)  Passed: $passed  Failed: $failed  Time: $([math]::Round($sw.Elapsed.TotalSeconds, 1))s"
if ($errors.Count -gt 0) {
    Write-Host "Failed levels: $($errors -join ', ')" -ForegroundColor Red
}
