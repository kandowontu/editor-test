$pf = Get-Content "$env:TEMP\famidash_pf_trace_dorabaebasic6_20260526_213546_860.csv"
$nes = Get-Content "$env:TEMP\famidash_mesen_trace_dorabaebasic6_20260526_214317_717.csv"
$pfHdr = $pf[0] -split ","
$nesHdr = $nes[1] -split ","
$pf_xpx = [Array]::IndexOf($pfHdr,"X_px")
$pf_ypx = [Array]::IndexOf($pfHdr,"Y_px")
$pf_mode = [Array]::IndexOf($pfHdr,"mode")
$pf_vy = [Array]::IndexOf($pfHdr,"VelY_fixed")
$pf_yfx = [Array]::IndexOf($pfHdr,"Y_fixed")
$n_sc = [Array]::IndexOf($nesHdr,"sim_cursor")
$n_px = [Array]::IndexOf($nesHdr,"px")
$n_py = [Array]::IndexOf($nesHdr,"py")
$n_gm = [Array]::IndexOf($nesHdr,"gamemode")
$n_vy = [Array]::IndexOf($nesHdr,"vel_y")
$nesMap = @{}
for ($i=2; $i -lt $nes.Count; $i++) {
  $c = $nes[$i] -split ","
  if ($c.Length -le $n_sc) { continue }
  $s = $c[$n_sc]
  if ($s -notmatch '^\d+$') { continue }
  $si = [int]$s
  if (-not $nesMap.ContainsKey($si)) { $nesMap[$si] = $c }
}
$pfMap = @{}
for ($i=1; $i -lt $pf.Count; $i++) {
  $c = $pf[$i] -split ","
  $pfMap[[int]$c[0]] = $c
}
$frames = @(0,10,50,100,500,1000,1500,2000,2500,3000,3500,3900,3990,3993,4000,4100,4200,4300,4400,4447)
foreach ($f in $frames) {
  $sc = $f + 1
  if ($nesMap.ContainsKey($sc) -and $pfMap.ContainsKey($f)) {
    $pc = $pfMap[$f]; $nc = $nesMap[$sc]
    $match = if ([int]$nc[$n_px] -eq [int]$pc[$pf_xpx] -and [int]$nc[$n_py] -eq [int]$pc[$pf_ypx]) { "OK" } else { "DIV" }
    Write-Host ("f={0,4} sc={1,4} | NES px={2,6} py={3,4} gm={4} vy={5,4} | PF Xpx={6,6} Ypx={7,4} mode={8} VY={9,5} | {10}" -f `
      $f, $sc, $nc[$n_px], $nc[$n_py], $nc[$n_gm], $nc[$n_vy], $pc[$pf_xpx], $pc[$pf_ypx], $pc[$pf_mode], $pc[$pf_vy], $match)
  }
}
