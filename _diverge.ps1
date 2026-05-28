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
$div = @()
foreach ($r in $mtRows) {
  $sc = [int]$r.sim_cursor
  if ($pf.ContainsKey($sc)) {
    $dx = $pf[$sc][0] - [int]$r.px
    $dy = $pf[$sc][1] - [int]$r.py
    if ($dx -ne 0 -or $dy -ne 0) { $div += "sim=$sc MT=($($r.px),$($r.py)) PF=($($pf[$sc][0]),$($pf[$sc][1])) d=($dx,$dy) mode=$($r.gamemode) vy=$($r.vel_y)" }
  }
}
Write-Host "Total divergent frames: $($div.Count)"
Write-Host "--- First 40 ---"
$div | Select -First 40
