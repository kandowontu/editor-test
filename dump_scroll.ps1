param([int]$Lo=440, [int]$Hi=450)
$nes="$env:TEMP\famidash_mesen_trace.csv"
$pf="$env:TEMP\famidash_pf_trace.csv"
Write-Host "NES sc=${Lo}-${Hi}:"
foreach($l in Get-Content $nes){
  $c=$l -split ','
  if($c.Count -ge 16 -and $c[0] -match '^\d+$'){
    $sc=[int]$c[1]
    if($sc -ge $Lo -and $sc -le $Hi){
      Write-Host ("sc={0} rom_f={1} sy={2} sy_subpx={3} tgt_sy={4} py={5} raw_y={6} cp_y={7} vy={8}" -f $sc,$c[0],$c[9],$c[15],$c[17],$c[3],$c[7],$c[18],$c[10])
    }
  }
}
Write-Host ""
$pflo = $Lo - 1; $pfhi = $Hi - 1
Write-Host "PF f=${pflo}-${pfhi}:"
foreach($l in Get-Content $pf){
  $c=$l -split ','
  if($c[0] -match '^\d+$'){
    $f=[int]$c[0]
    if($f -ge ($Lo-1) -and $f -le ($Hi-1)){
      Write-Host ("f={0} camY={1} tgtCamY={2} camYfx={3} sySub={4} Ypx={5} Yfx={6}" -f $f,$c[9],$c[10],$c[19],$c[22],$c[7],$c[2])
    }
  }
}
