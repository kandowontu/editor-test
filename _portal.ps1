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
# Show frames around portal entry: 4030..4050
Write-Output "sim,NES_gm,NES_px,NES_py,NES_vy,PF_gm,PF_X,PF_Y,PF_vy"
for ($k = 4030; $k -le 4060; $k++) {
    if (-not $nesByCursor.ContainsKey($k) -or -not $pfByFrame.ContainsKey($k)) { continue }
    $n = $nesByCursor[$k]; $p = $pfByFrame[$k]
    Write-Output "$k,$($n[14]),$($n[2]),$($n[3]),$($n[10]),$($p.mode),$($p.X_px),$($p.Y_px),$($p.VelY_fixed)"
}
