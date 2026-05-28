$pfFile = "$env:TEMP\famidash_pf_trace_dorabaebasic6_20260521_183221_941.csv"
$mtFile = "$env:TEMP\famidash_mesen_trace_dorabaebasic6_20260521_184100_990.csv"

$pf = Import-Csv $pfFile
# MT has a leading nes_y_offset,528 line -> skip it
$mtRaw = Get-Content $mtFile
$mt = $mtRaw[1..($mtRaw.Count-1)] | ConvertFrom-Csv

# Build map: sim_cursor -> last row
$mtBySim = @{}
foreach($r in $mt){
  $s = [int]$r.sim_cursor
  $mtBySim[$s] = $r
}

# Frame alignment: PF f=N vs MT sim=N+1 (post-grav cpvy)
# But also try direct comparison: PF.X_px,Y_px vs MT.px,py
$divFrames = @()
foreach($p in $pf){
  $f = [int]$p.frame
  $simKey = $f + 1
  if(-not $mtBySim.ContainsKey($simKey)){ continue }
  $m = $mtBySim[$simKey]
  $pfPx = [int]$p.X_px
  $pfPy = [int]$p.Y_px
  $mtPx = [int]$m.px
  $mtPy = [int]$m.py
  $dx = $pfPx - $mtPx
  $dy = $pfPy - $mtPy
  if([math]::Abs($dx) -gt 1 -or [math]::Abs($dy) -gt 1){
    $divFrames += [PSCustomObject]@{ f=$f; sim=$simKey; pfX=$pfPx; mtX=$mtPx; pfY=$pfPy; mtY=$mtPy; dx=$dx; dy=$dy; pfVY=$p.VelY_fixed; mtVY=$m.vel_y; pfMode=$p.mode; mtMode=$m.gamemode }
    if($divFrames.Count -ge 20){ break }
  }
}
$divFrames | Format-Table
