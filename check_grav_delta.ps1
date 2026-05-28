param([int]$From=2210,[int]$To=2230)
$mt="$env:TEMP\famidash_mesen_physics_debug_dorabaebasic6_20260522_025551_880.log"
$lines=Get-Content $mt
$cursim=0; $ufo=$null
foreach($l in $lines){
  if($l -match '^F=\d+ sim=(\d+)'){ $cursim=[int]$matches[1]; $ufo=$null; continue }
  if($cursim -lt $From -or $cursim -gt $To){ continue }
  if($l -match 'ufo_movement\.in.*cpvy=(-?\d+)'){ $ufo=[int]$matches[1]; continue }
  if($l -match 'bg_coll_U\.in.*cpvy=(-?\d+)' -and $ufo -ne $null){
    $bg=[int]$matches[1]
    Write-Host ("sim={0} ufo={1} bgU={2} delta={3}" -f $cursim,$ufo,$bg,($bg-$ufo))
    $ufo=$null
  }
}
