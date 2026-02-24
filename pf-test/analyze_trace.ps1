## Quick analysis of PF trace to find death location
$trace = [System.IO.File]::ReadAllLines("$env:TEMP\famidash_pf_trace.csv")
Write-Host "Trace lines: $($trace.Count)"

# Find death frames (alive=0)
$deaths = @()
for ($i = 1; $i -lt $trace.Count; $i++) {
    $parts = $trace[$i] -split ","
    if ($parts.Count -ge 6 -and $parts[5] -eq "0") {
        $deaths += [PSCustomObject]@{
            Line = $i
            Frame = $parts[0]
            X_px = $parts[6]
            Y_px = $parts[7]
            VelY = $parts[3]
            OnGround = $parts[8]
        }
    }
}

Write-Host "`nTotal death frames: $($deaths.Count)"
Write-Host "`nFirst 20 deaths:"
$deaths | Select-Object -First 20 | Format-Table -AutoSize

# Find the latest debug log
$logs = Get-ChildItem "$env:TEMP\famidash_pf_debug_*.txt" | Sort-Object LastWriteTime -Descending
Write-Host "`nLatest debug logs:"
$logs | Select-Object -First 3 Name, @{N="Size";E={"{0:N0}" -f $_.Length}}, @{N="When";E={$_.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")}} | Format-Table -AutoSize
