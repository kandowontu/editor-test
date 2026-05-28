$pf="$env:TEMP\famidash_pf_trace_dorabaebasic6_20260522_024425_084.csv"
$mt="$env:TEMP\famidash_mesen_trace_dorabaebasic6_20260522_025551_880.csv"
$pfArr = @{}
foreach($l in (Get-Content $pf | Select-Object -Skip 1)){
  $c = $l.Split(',')
  if($c.Length -gt 12){
    $pfArr[[int]$c[0]] = @{X=[int]$c[6]; Y=[int]$c[7]; mode=$c[11]; vy=$c[3]; onG=$c[8]; gf=$c[12]}
  }
}
$mtArr = @{}
foreach($l in (Get-Content $mt | Select-Object -Skip 2)){
  $c = $l.Split(',')
  if($c.Length -gt 14){
    $mtArr[[int]$c[1]] = @{X=[int]$c[2]; Y=[int]$c[3]; mode=$c[14]; gm=$c[12]; vy=$c[10]; sx=$c[8]; cp_g=$c[25]}
  }
}
Write-Host "frame,PFx,MTx,dx,PFy,MTy,dy,PFmode,MTmode,MTgm,PFvy,MTvy,PFonG,PFgf,MTcp_g"
# PF.f=N END maps to MT.sim=N+1
$firstDiv = -1
for($pfF=0; $pfF -lt 2700; $pfF++){
  $mtSim = $pfF + 1
  if(-not $pfArr.ContainsKey($pfF)){ continue }
  if(-not $mtArr.ContainsKey($mtSim)){ continue }
  $p = $pfArr[$pfF]
  $m = $mtArr[$mtSim]
  $dx = $p.X - $m.X
  $dy = $p.Y - $m.Y
  if($pfF -lt 2250){ continue }
  if($firstDiv -lt 0 -and ([Math]::Abs($dx) -gt 3 -or [Math]::Abs($dy) -gt 5)){
    $firstDiv = $pfF
    Write-Host "FIRST_DIVERGENCE: pfF=$pfF mtSim=$mtSim dx=$dx dy=$dy"
  }
}
if($firstDiv -ge 0){
  for($pfF=[Math]::Max(0,$firstDiv-5); $pfF -le $firstDiv+8; $pfF++){
    $mtSim = $pfF + 1
    if(-not $pfArr.ContainsKey($pfF)){ continue }
    if(-not $mtArr.ContainsKey($mtSim)){ continue }
    $p = $pfArr[$pfF]; $m = $mtArr[$mtSim]
    $dx = $p.X - $m.X; $dy = $p.Y - $m.Y
    Write-Host "pf=$pfF mt=$mtSim PFx=$($p.X) MTx=$($m.X) dx=$dx PFy=$($p.Y) MTy=$($m.Y) dy=$dy PFm=$($p.mode) MTm=$($m.mode) MTgm=$($m.gm) PFvy=$($p.vy) MTvy=$($m.vy) PFonG=$($p.onG) PFgf=$($p.gf) MTcpg=$($m.cp_g)"
  }
}
