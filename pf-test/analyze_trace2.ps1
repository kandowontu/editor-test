## Analyze PF trace - write results to file
$outFile = "c:\Editor Test\pf-test\analysis_output.txt"
$trace = [System.IO.File]::ReadAllLines("$env:TEMP\famidash_pf_trace.csv")

$out = @()
$out += "Trace lines: $($trace.Count)"
$out += "Trace file date: $((Get-Item "$env:TEMP\famidash_pf_trace.csv").LastWriteTime)"

# Find death frames (alive=0) 
$deathLines = @()
for ($i = 1; $i -lt $trace.Count; $i++) {
    $parts = $trace[$i] -split ","
    if ($parts.Count -ge 6 -and $parts[5] -eq "0") {
        $deathLines += "$($trace[$i])"
    }
}

$out += ""
$out += "Total death entries: $($deathLines.Count)"
$out += ""
$out += "First 20 deaths:"
$deathLines | Select-Object -First 20 | ForEach-Object { $out += "  $_" }

# Show unique death X positions
$deathXs = @{}
foreach ($dl in $deathLines) {
    $p = $dl -split ","
    $x = $p[6]
    if (-not $deathXs.ContainsKey($x)) { $deathXs[$x] = 0 }
    $deathXs[$x]++
}
$out += ""
$out += "Death X positions (unique):"
$deathXs.GetEnumerator() | Sort-Object { [int]$_.Key } | ForEach-Object { $out += "  X=$($_.Key)px  count=$($_.Value)" }

# Latest debug log
$logs = Get-ChildItem "$env:TEMP\famidash_pf_debug_*.txt" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 3
$out += ""
$out += "Latest debug logs:"
foreach ($l in $logs) { $out += "  $($l.Name)  $($l.Length)b  $($l.LastWriteTime)" }

# Read the newest debug log if it exists, search for death context
if ($logs.Count -gt 0) {
    $newest = $logs[0].FullName
    $out += ""
    $out += "Searching newest log: $($logs[0].Name)"
    
    # Find first DEATH line and surrounding context
    $logLines = [System.IO.File]::ReadAllLines($newest)
    $out += "Log total lines: $($logLines.Count)"
    
    for ($i = 0; $i -lt $logLines.Count; $i++) {
        if ($logLines[$i] -match "\[DEATH\]") {
            $start = [Math]::Max(0, $i - 5)
            $end = [Math]::Min($logLines.Count - 1, $i + 2)
            $out += ""
            $out += "=== First death context (line $i) ==="
            for ($j = $start; $j -le $end; $j++) {
                $out += "  L$j : $($logLines[$j])"
            }
            break
        }
    }
}

[System.IO.File]::WriteAllLines($outFile, $out)
Write-Host "Analysis written to $outFile"
