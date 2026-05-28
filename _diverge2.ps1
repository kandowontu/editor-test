$f = Get-ChildItem $env:TEMP\famidash_pf_debug_cataclysm_*.txt | Sort LastWriteTime | Select -Last 1
$mt = Get-ChildItem $env:TEMP\famidash_mesen_trace_cataclysm_*.csv | Sort LastWriteTime | Select -Last 1
Write-Host "PF: $($f.Name)"
Write-Host "MT: $($mt.Name)"
$mtRows = Get-Content $mt.FullName | Select -Skip 1 | ConvertFrom-Csv
$pf = @{}
Get-Content $f.FullName | Select-String -Pattern "REPLAY" | ForEach-Object {
  if ($_ -match 'REPLAY f=(\d+)\] X=0x[0-9A-F]+ \((\d+)px\) Y=0x[0-9A-F]+ \((\d+)px\)') {
    $pf[[int]$matches[1]] = @([int]$matches[2], [int]$matches[3])
  }
}
# Use signed-aware MT px parse (it stores some as unsigned wrap of negatives)
function ToS([string]$v) { $n = [int64]$v; if ($n -gt 2147483647) { return ($n - 4294967296) }; return $n }
$prevDx = $null; $prevDy = $null
$changes = @()
foreach ($r in $mtRows) {
  $sc = [int]$r.sim_cursor
  if ($pf.ContainsKey($sc)) {
    $mx = ToS $r.px; $my = ToS $r.py
    $dx = $pf[$sc][0] - $mx
    $dy = $pf[$sc][1] - $my
    if ($prevDx -ne $null -and ($dx -ne $prevDx -or $dy -ne $prevDy)) {
      $changes += "sim=$sc d=($dx,$dy) was=($prevDx,$prevDy) MT=($mx,$my) PF=($($pf[$sc][0]),$($pf[$sc][1])) mode=$($r.gamemode) mini=$($r.mini) grav=$($r.cp_gravity) vy=$($r.vel_y)"
    }
    $prevDx = $dx; $prevDy = $dy
  }
}
Write-Host "Total delta changes: $($changes.Count)"
$changes | Select -First 30
