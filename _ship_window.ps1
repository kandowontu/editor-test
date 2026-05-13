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
Write-Output "sim,NES_px,NES_py,NES_vy,PF_X,PF_Y,PF_vy,a_cur"
for ($k = 4434; $k -le 4445; $k++) {
    if (-not $nesByCursor.ContainsKey($k) -or -not $pfByFrame.ContainsKey($k)) { continue }
    $n = $nesByCursor[$k]; $p = $pfByFrame[$k]
    Write-Output "$k,$($n[2]),$($n[3]),$($n[10]),$($p.X_px),$($p.Y_px),$($p.VelY_fixed),$($n[5])"
}
