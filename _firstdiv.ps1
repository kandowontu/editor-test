$lines = Get-Content "$env:TEMP\famidash_mesen_trace.csv"
$respawnLines = @()
for($i=0; $i -lt $lines.Count; $i++){ if($lines[$i] -match 'respawn'){ $respawnLines += $i } }
$startLine = $respawnLines[-1] + 1
$dataLines = $lines[$startLine..($lines.Count-1)] | Where-Object { $_ -notmatch '^#' -and $_.Trim() -ne '' }
$nesByCursor = @{}
foreach ($l in $dataLines) { $f = $l -split ','; $cur = [int]$f[1]; $nesByCursor[$cur] = $f }
$pf = Import-Csv "$env:TEMP\famidash_pf_trace.csv"
$pfByFrame = @{}
foreach ($r in $pf) { $pfByFrame[[int]$r.frame] = $r }
$keys = $nesByCursor.Keys | Sort-Object
# PF and NES seem off by 1 frame at startup. Align by best-fit at sim 200.
$bestOff = 0; $bestErr = 9999
for ($off = -3; $off -le 3; $off++) {
    $e = 0; $cnt = 0
    for ($k = 100; $k -le 300; $k++) {
        if (-not $nesByCursor.ContainsKey($k) -or -not $pfByFrame.ContainsKey($k+$off)) { continue }
        $e += [Math]::Abs([int]$nesByCursor[$k][2] - [int]$pfByFrame[$k+$off].X_px); $cnt++
    }
    if ($cnt -gt 0) {
        $avg = $e/$cnt
        Write-Output "  off=$off avg_dx=$avg"
        if ($avg -lt $bestErr) { $bestErr = $avg; $bestOff = $off }
    }
}
Write-Output "Best PF offset = $bestOff (avg_err=$bestErr)"
$pfOff = $bestOff
foreach ($k in $keys) {
    if ($k -lt 100) { continue }
    if (-not $pfByFrame.ContainsKey($k+$pfOff)) { continue }
    $n = $nesByCursor[$k]; $p = $pfByFrame[$k+$pfOff]
    $ngm = [int]$n[14]; $pgm = [int]$p.mode
    if ($ngm -ne $pgm) { Write-Output "MODE DIVERGE sim=$k NES gm=$ngm | PF gm=$pgm at PFframe=$($k+$pfOff)"; return }
    $npx = [int]$n[2]; $npy = [int]$n[3]
    $ppx = [int]$p.X_px; $ppy = [int]$p.Y_px
    $dx = $npx - $ppx; $dy = $npy - $ppy
    if ([Math]::Abs($dx) -gt 2 -or [Math]::Abs($dy) -gt 2) {
        Write-Output "FIRST DIV sim=$k gm=$ngm NES=($npx,$npy) PF=($ppx,$ppy) dx=$dx dy=$dy"
        for ($i = $k-5; $i -le $k+5; $i++) {
            if (-not $nesByCursor.ContainsKey($i) -or -not $pfByFrame.ContainsKey($i+$pfOff)) { continue }
            $n2 = $nesByCursor[$i]; $p2 = $pfByFrame[$i+$pfOff]
            Write-Output "  sim=$i gm=$($n2[14]) NES=($($n2[2]),$($n2[3])) vy=$($n2[10]) acur=$($n2[5]) | PF=($($p2.X_px),$($p2.Y_px)) vy=$($p2.VelY_fixed) inp=$($p2.input)"
        }
        return
    }
}
Write-Output "no div > 2px"
