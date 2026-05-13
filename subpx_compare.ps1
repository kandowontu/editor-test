param([int]$ShowFirst=20, [int]$Threshold=1, [int]$AlignOffset=1, [int]$MinPf=200)
$nes="$env:TEMP\famidash_mesen_trace.csv"
$pf="$env:TEMP\famidash_pf_trace.csv"
$nesData=@{}
foreach($l in Get-Content $nes){
  $c=$l -split ','
  if($c[0] -match '^\d+$'){
    $sy=[int64]$c[9]; if($sy -ge 2147483648L){$sy -= 4294967296L}
    $ry=[int64]$c[7]
    $vy=[int64]$c[10]; if($vy -ge 32768L){$vy -= 65536L}
    $sub=$sy*256 + $ry - 12288
    $nesData[[int]$c[1]] = @{ f=[int]$c[0]; sub=$sub; vy=$vy; a=[int]$c[4]; px=[int64]$c[2]; py=[int64]$c[3] }
  }
}
$pfData=@{}
foreach($l in Get-Content $pf){
  $c=$l -split ','
  if($c[0] -match '^\d+$'){
    $yfHex=($c[2] -replace '0x','')
    $yf=[Convert]::ToInt64($yfHex,16)
    if($yf -ge 8388608L){ $yf -= 16777216L }
    $vyHex=($c[3] -replace '0x','')
    $vy=[Convert]::ToInt64($vyHex,16)
    if($vy -ge 8388608L){ $vy -= 16777216L }
    $pfData[[int]$c[0]] = @{ yf=$yf; vy=$vy; input=[int]$c[4]; ypx=[int]$c[7] }
  }
}
Write-Host "NES sim_cursor rows: $($nesData.Count)  PF frame rows: $($pfData.Count)  AlignOffset(sc=pf+offset): $AlignOffset"
$divs = New-Object System.Collections.Generic.List[object]
foreach($k in ($pfData.Keys | Sort-Object)){
  $sc = $k + $AlignOffset
  if($nesData.ContainsKey($sc)){
    $n = $nesData[$sc]
    $p = $pfData[$k]
    $d = [int64]$n.sub - [int64]$p.yf
    $dvy = [int64]$n.vy - [int64]$p.vy
    $divs.Add([PSCustomObject]@{ pf=$k; sc=$sc; rom_f=$n.f; nes_sub=$n.sub; pf_sub=$p.yf; dy_sub=$d; dy_px=[math]::Round($d/256.0,3); nes_vy=$n.vy; pf_vy=$p.vy; dvy=$dvy; pf_inp=$p.input; nes_a=$n.a; px=$n.px; py=$n.py; pf_py=$p.ypx })
  }
}
Write-Host "Compared: $($divs.Count)"
Write-Host "`n=== FIRST $ShowFirst rows where |dy_sub| >= $Threshold (after pf>=$MinPf) ==="
$divs | Where-Object { [math]::Abs($_.dy_sub) -ge $Threshold -and $_.pf -ge $MinPf } | Select-Object -First $ShowFirst | Format-Table -AutoSize
Write-Host "`n=== FIRST 5 rows where |dvy| > 0 (after pf>=$MinPf) ==="
$divs | Where-Object { $_.dvy -ne 0 -and $_.pf -ge $MinPf } | Select-Object -First 5 | Format-Table -AutoSize
