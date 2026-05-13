$lines = Get-Content "$env:TEMP\famidash_mesen_trace.csv"
$respawnLines = @()
for($i=0; $i -lt $lines.Count; $i++){ if($lines[$i] -match 'respawn'){ $respawnLines += $i } }
$startLine = if ($respawnLines.Count -gt 0) { $respawnLines[-1] + 1 } else { 2 }
$dataLines = $lines[$startLine..($lines.Count-1)] | Where-Object { $_ -notmatch '^#' -and $_.Trim() -ne '' }
$nesByCursor = @{}
foreach ($l in $dataLines) { $f = $l -split ','; $cur = [int]$f[1]; $nesByCursor[$cur] = $f }
$pf = Import-Csv "$env:TEMP\famidash_pf_trace.csv"
$pfByFrame = @{}
foreach ($r in $pf) { $pfByFrame[[int]$r.frame] = $r }
$keys = $nesByCursor.Keys | Sort-Object
# Mode transitions in NES
$prevGm = -1
Write-Output "=== NES gamemode transitions ==="
foreach ($k in $keys) {
    $n = $nesByCursor[$k]; $gm = [int]$n[14]
    if ($gm -ne $prevGm) {
        $pfX = if ($pfByFrame.ContainsKey($k)) { $pfByFrame[$k].X_px } else { '?' }
        $pfY = if ($pfByFrame.ContainsKey($k)) { $pfByFrame[$k].Y_px } else { '?' }
        $pfGm = if ($pfByFrame.ContainsKey($k)) { $pfByFrame[$k].mode } else { '?' }
        Write-Output ("sim={0,5} NES gm={1} px={2} py={3} | PF gm={4} X={5} Y={6}" -f $k,$gm,$n[2],$n[3],$pfGm,$pfX,$pfY)
        $prevGm = $gm
    }
}
Write-Output "=== PF mode transitions ==="
$prevGm = -1
foreach ($r in $pf) {
    $gm = [int]$r.mode
    if ($gm -ne $prevGm) {
        Write-Output "frame=$($r.frame) PF gm=$gm X=$($r.X_px) Y=$($r.Y_px)"
        $prevGm = $gm
    }
}
