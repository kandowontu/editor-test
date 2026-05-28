$pf = Get-Content "$env:TEMP\famidash_pf_trace_dorabaebasic7_20260526_235737_535.csv"
$nes = Get-Content "$env:TEMP\famidash_mesen_trace_dorabaebasic7_20260527_000935_405.csv"
$nesMap=@{}
for($i=2;$i -lt $nes.Count;$i++){
  $c=$nes[$i]-split','; if($c.Length -le 1){continue}; if($c[1] -notmatch '^\d+$'){continue}
  $si=[int]$c[1]; if(-not $nesMap.ContainsKey($si)){$nesMap[$si]=$c}
}
$pfMap=@{}
for($i=1;$i -lt $pf.Count;$i++){ $c=$pf[$i]-split','; $pfMap[[int]$c[0]]=$c }
$lo = [int]$args[0]; $hi = [int]$args[1]
for($sc=$lo;$sc -le $hi;$sc++){
  if(-not $nesMap.ContainsKey($sc)){continue}
  $pfF=$sc-1
  if(-not $pfMap.ContainsKey($pfF)){continue}
  $nc=$nesMap[$sc]; $pc=$pfMap[$pfF]
  "sc={0,4} pf={1,4} NES(x={2,5},y={3,4},vy={4,6},sy={5,4}) PF(x={6,5},y={7,4},vy={8,11},slt={9,3},lst={10,3},swo={11},camY={12,3})" -f $sc,$pfF,$nc[2],$nc[3],$nc[10],$nc[9],$pc[6],$pc[7],$pc[3],$pc[25],$pc[26],$pc[28],$pc[9]
}
