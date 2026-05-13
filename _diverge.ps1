$lines = Get-Content "$env:TEMP\famidash_mesen_trace.csv"
Write-Output "total lines: $($lines.Count)"
$respawnLines = @()
for($i=0; $i -lt $lines.Count; $i++){ if($lines[$i] -match 'respawn'){ $respawnLines += $i } }
Write-Output "respawn lines: $($respawnLines -join ',')"
$startLine = if ($respawnLines.Count -gt 0) { $respawnLines[-1] + 1 } else { 2 }
$dataLines = $lines[$startLine..($lines.Count-1)] | Where-Object { $_ -notmatch '^#' -and $_.Trim() -ne '' }
Write-Output "data rows in last segment: $($dataLines.Count)"
$nesByCursor = @{}
foreach ($l in $dataLines) {
    $f = $l -split ','
    $cur = [int]$f[1]
    $nesByCursor[$cur] = $f
}
Write-Output "unique sim_cursors: $($nesByCursor.Count)"
$pf = Import-Csv "$env:TEMP\famidash_pf_trace.csv"
Write-Output "PF rows: $($pf.Count)"
$pfByFrame = @{}
foreach ($r in $pf) { $pfByFrame[[int]$r.frame] = $r }
$keys = $nesByCursor.Keys | Sort-Object
$gmTransitions = @{}
$prevGm = -1
foreach ($k in $keys) {
    $gm = [int]$nesByCursor[$k][14]
    if ($gm -ne $prevGm) { $gmTransitions[$k] = $gm; $prevGm = $gm }
}
Write-Output "NES gamemode transitions:"
foreach ($k in ($gmTransitions.Keys | Sort-Object)) { Write-Output "  sim=$k gm=$($gmTransitions[$k])" }

# Find first ship-mode Y divergence > 4px
foreach ($k in $keys) {
    if ($k -lt 100) { continue }
    if (-not $pfByFrame.ContainsKey($k)) { continue }
    $n = $nesByCursor[$k]; $p = $pfByFrame[$k]
    $ngm = [int]$n[14]
    if ($ngm -ne 1) { continue }   # only ship mode
    $npx = [int]$n[2]; $npy = [int]$n[3]; $nvy = [int]$n[10]
    $ppx = [int]$p.X_px; $ppy = [int]$p.Y_px; $pgm = [int]$p.mode
    if ($ngm -ne $pgm) { continue }
    if ([Math]::Abs($npy - $ppy) -gt 4) { Write-Output "SHIP Y DIVERGE sim=$k NES px=$npx py=$npy vy=$nvy | PF X=$ppx Y=$ppy vy=$($p.VelY_fixed)"; return }
    if ([Math]::Abs($npx - $ppx) -gt 6) { Write-Output "SHIP X DIVERGE sim=$k NES px=$npx py=$npy | PF X=$ppx Y=$ppy"; return }
}
Write-Output "no ship divergence > 4px"
Write-Output "no divergence"
