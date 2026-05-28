$pfFile = "$env:TEMP\famidash_pf_trace_dorabaebasic6_20260521_183221_941.csv"
$mtFile = "$env:TEMP\famidash_mesen_trace_dorabaebasic6_20260521_184100_990.csv"

$pf = Import-Csv $pfFile
$mtRaw = Get-Content $mtFile
$mt = $mtRaw[1..($mtRaw.Count-1)] | ConvertFrom-Csv
$mtBySim = @{}
foreach($r in $mt){ $mtBySim[[int]$r.sim_cursor] = $r }

# Frames 2265..2312 detail
"f  | PF Y vY gF mode onG | MT Y vY cpG tableIdx gravMod"
foreach($p in $pf){
  $f=[int]$p.frame
  if($f -lt 2265 -or $f -gt 2312){ continue }
  $sim=$f+1
  if(-not $mtBySim.ContainsKey($sim)){ continue }
  $m=$mtBySim[$sim]
  $vyHex=$p.VelY_fixed
  $vyInt = [int64]($vyHex.Replace('0x',''),16) -bxor 0
  # parse hex with signed conversion
  $h = $vyHex.Replace('0x','')
  $u = [Convert]::ToInt64($h,16)
  if($u -ge 0x80000000){ $u = $u - 0x100000000 }
  Write-Host ("{0,4} | pf y={1,3} vY={2,6} gF={3} m={4} onG={5} | mt y={6,3} vY={7,6} cpG={8} ti={9} gM={10}" -f $f,$p.Y_px,$u,$p.gravFlipped,$p.mode,$p.onGround,$m.py,$m.vel_y,$m.cp_gravity,$m.table_idx,$m.gravity_mod)
}
