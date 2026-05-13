param([int]$Lo=2393, [int]$Hi=2415)
$pf="$env:TEMP\famidash_pf_trace.csv"
$nes="$env:TEMP\famidash_mesen_trace.csv"
Write-Host "PF rows pf=$Lo-$Hi :"
foreach($l in Get-Content $pf){
  $c=$l -split ','
  if($c[0] -match '^\d+$' -and [int]$c[0] -ge $Lo -and [int]$c[0] -le $Hi){
    $yfHex=($c[2] -replace '0x','')
    $yf=[Convert]::ToInt64($yfHex,16)
    $vyHex=($c[3] -replace '0x','')
    $vy=[Convert]::ToInt64($vyHex,16)
    if($vy -ge 2147483648L){ $vy -= 4294967296L }
    Write-Host ("f={0} Y={1} (sub={2}) vy={3} inp={4} onG={5} mode={6} gF={7}" -f $c[0],$c[2],$yf,$vy,$c[4],$c[8],$c[11],$c[12])
  }
}
Write-Host ""
Write-Host "NES rows sc=$($Lo+1)-$($Hi+1) :"
foreach($l in Get-Content $nes){
  $c=$l -split ','
  if($c.Count -ge 11 -and $c[0] -match '^\d+$'){
    $sc=[int]$c[1]
    if($sc -ge ($Lo+1) -and $sc -le ($Hi+1)){
      $sy=[int64]$c[9]; if($sy -ge 2147483648L){$sy-=4294967296L}
      $ry=[int64]$c[7]
      $sub=$sy*256+$ry-12288
      $vy=[int64]$c[10]
      Write-Host ("rom_f={0} sc={1} py={2} raw_y={3} sy={4} sub={5} vy={6} cube={7} a_n={8}" -f $c[0],$c[1],$c[3],$c[7],$sy,$sub,$vy,$c[20],$c[4])
    }
  }
}
