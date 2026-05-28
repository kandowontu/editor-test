$pfFile = "$env:TEMP\famidash_pf_trace_dorabaebasic6_20260521_183221_941.csv"
$mtFile = "$env:TEMP\famidash_mesen_trace_dorabaebasic6_20260521_184100_990.csv"

$pf = Import-Csv $pfFile
$mtRaw = Get-Content $mtFile
$mt = $mtRaw[1..($mtRaw.Count-1)] | ConvertFrom-Csv

$mtBySim = @{}
foreach($r in $mt){ $mtBySim[[int]$r.sim_cursor] = $r }

# Print all frames where dy changes from prev
$prevDy = $null
$count=0
foreach($p in $pf){
  $f = [int]$p.frame
  $simKey = $f + 1
  if(-not $mtBySim.ContainsKey($simKey)){ continue }
  $m = $mtBySim[$simKey]
  $dy = [int]$p.Y_px - [int]$m.py
  if($prevDy -ne $dy){
    Write-Host ("f={0} pfY={1} mtY={2} dy={3} pfVY={4} mtVY={5} pfMode={6} mtMode={7} onG={8} cpG={9}" -f $f, $p.Y_px, $m.py, $dy, $p.VelY_fixed, $m.vel_y, $p.mode, $m.gamemode, $p.onGround, $m.cp_gravity)
    $prevDy = $dy
    $count++
    if($count -gt 60){ break }
  }
}
