$pf="$env:TEMP\famidash_pf_trace.csv"
$nes="$env:TEMP\famidash_mesen_trace.csv"
$pfMap=@{}
foreach ($l in Get-Content $pf) {
  if ($l -match "^\d+,") {
    $f=$l -split ','
    $pfMap[[int]$f[0]]=@{X=[int]$f[6];Y=[int]$f[7];M=$f[11];Mn=$f[23]}
  }
}
$nesMap=@{}
foreach ($l in Get-Content $nes) {
  if ($l -match "^\d+,") {
    $f=$l -split ','
    $key=[int]$f[1]
    if (-not $nesMap.ContainsKey($key)) {
      $nesMap[$key]=@{X=[int]$f[2];Y=[int]$f[3];M=$f[14];Mn=$f[24]}
    }
  }
}
"sim,nesX,nesY,nesMode,nesMn,pfX,pfY,pfM,pfMn,dx,dy"
foreach ($s in 3460..3475) {
  if ($nesMap.ContainsKey($s) -and $pfMap.ContainsKey($s-1)) {
    $n=$nesMap[$s]; $p=$pfMap[$s-1]
    "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10}" -f $s,$n.X,$n.Y,$n.M,$n.Mn,$p.X,$p.Y,$p.M,$p.Mn,($p.X-$n.X),($p.Y-$n.Y)
  }
}
"--- mini wave area ---"
foreach ($s in 5195..5260) {
  if ($nesMap.ContainsKey($s) -and $pfMap.ContainsKey($s-1)) {
    $n=$nesMap[$s]; $p=$pfMap[$s-1]
    "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10}" -f $s,$n.X,$n.Y,$n.M,$n.Mn,$p.X,$p.Y,$p.M,$p.Mn,($p.X-$n.X),($p.Y-$n.Y)
  }
}
