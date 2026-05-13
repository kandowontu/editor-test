param([int]$ShowFirst=40, [int]$MinPf=200, [int]$AlignOffset=1)
$nes="$env:TEMP\famidash_mesen_trace.csv"
$pf="$env:TEMP\famidash_pf_trace.csv"
$nesData=@{}
foreach($l in Get-Content $nes){
  $c=$l -split ','
  if($c[0] -match '^\d+$' -and $c.Count -ge 11){
    $vy=[int64]$c[10]
    $nesData[[int]$c[1]] = @{ rom_f=[int]$c[0]; py=[int]$c[3]; px=[int64]$c[2]; vy=$vy; a=[int]$c[4]; raw_y=[int]$c[7]; sy=[int64]$c[9]; cube=[int]$c[20] }
  }
}
$pfData=@{}
foreach($l in Get-Content $pf){
  $c=$l -split ','
  if($c[0] -match '^\d+$'){
    $yf=[Convert]::ToInt64(($c[2] -replace '0x',''),16)
    $vyU=[Convert]::ToInt64(($c[3] -replace '0x',''),16)
    $vy=$vyU; if($vy -ge 2147483648L){ $vy -= 4294967296L }
    $pfData[[int]$c[0]] = @{ ypx=[int]$c[7]; yfx=$yf; vy=$vy; input=[int]$c[4]; onG=[int]$c[8] }
  }
}
$divs = New-Object System.Collections.Generic.List[object]
foreach($k in ($pfData.Keys | Sort-Object)){
  $sc = $k + $AlignOffset
  if($nesData.ContainsKey($sc)){
    $n = $nesData[$sc]
    $p = $pfData[$k]
    $dpy = $n.py - $p.ypx
    $dvy = $n.vy - $p.vy
    $divs.Add([PSCustomObject]@{ pf=$k; sc=$sc; rom_f=$n.rom_f; nes_py=$n.py; pf_py=$p.ypx; dpy=$dpy; nes_vy=$n.vy; pf_vy=$p.vy; dvy=$dvy; nes_a=$n.a; pf_inp=$p.input; nes_cube=$n.cube; raw_y=$n.raw_y; pf_lo=$p.yfx -band 0xFF })
  }
}
Write-Host "`n=== FIRST $ShowFirst rows where dpy != 0 OR dvy != 0 (after pf>=$MinPf) ==="
$divs | Where-Object { ($_.dpy -ne 0 -or $_.dvy -ne 0) -and $_.pf -ge $MinPf } | Select-Object -First $ShowFirst | Format-Table -AutoSize
Write-Host "`n=== FIRST 15 rows where dpy != 0 (after pf>=$MinPf) ==="
$divs | Where-Object { $_.dpy -ne 0 -and $_.pf -ge $MinPf } | Select-Object -First 15 | Format-Table -AutoSize
