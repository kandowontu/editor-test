$pf = Get-Content "$env:TEMP\famidash_pf_trace_dorabaebasic6_20260526_213546_860.csv"
$nes = Get-Content "$env:TEMP\famidash_mesen_trace_dorabaebasic6_20260526_214317_717.csv"
$pfHdr = $pf[0] -split ","
$nesHdr = $nes[1] -split ","
$pf_xpx = [Array]::IndexOf($pfHdr,"X_px")
$pf_ypx = [Array]::IndexOf($pfHdr,"Y_px")
$pf_mode = [Array]::IndexOf($pfHdr,"mode")
$pf_vy = [Array]::IndexOf($pfHdr,"VelY_fixed")
$pf_yfx = [Array]::IndexOf($pfHdr,"Y_fixed")
$pf_slt = [Array]::IndexOf($pfHdr,"SlopeType")
$pf_lst = [Array]::IndexOf($pfHdr,"LastSlopeType")
$pf_slf = [Array]::IndexOf($pfHdr,"SlopeFrames")
$pf_camy = [Array]::IndexOf($pfHdr,"CamY_px")
$pf_grav = [Array]::IndexOf($pfHdr,"gravFlipped")
$n_sc = [Array]::IndexOf($nesHdr,"sim_cursor")
$n_px = [Array]::IndexOf($nesHdr,"px")
$n_py = [Array]::IndexOf($nesHdr,"py")
$n_gm = [Array]::IndexOf($nesHdr,"gamemode")
$n_vy = [Array]::IndexOf($nesHdr,"vel_y")
$n_sy = [Array]::IndexOf($nesHdr,"scrolly")
$n_rawy = [Array]::IndexOf($nesHdr,"raw_y")

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

$divs = New-Object System.Collections.ArrayList
$sortedKeys = $nesMap.Keys | Sort-Object {[int]$_}
foreach ($sc in $sortedKeys) {
  $pfF = $sc - 1
  if ($pfF -lt 0) { continue }
  if (-not $pfMap.ContainsKey($pfF)) { continue }
  $nc = $nesMap[$sc]
  $pc = $pfMap[$pfF]
  $nPx = [int64]$nc[$n_px]
  if ($nPx -lt 0 -or $nPx -gt 100000) { continue }
  $nPy = [int64]$nc[$n_py]
  $pPx = [int64]$pc[$pf_xpx]
  $pPy = [int64]$pc[$pf_ypx]
  $nVy = [int64]$nc[$n_vy]
  $pVyHex = $pc[$pf_vy] -replace '^0x',''
  $pVy = [Convert]::ToInt64($pVyHex, 16)
  if ($pVy -ge ([int64]2147483648)) { $pVy -= ([int64]4294967296) }
  $vyDiff = ($nVy -ne $pVy)
  if ($nPx -ne $pPx -or $nPy -ne $pPy -or $vyDiff) {
    [void]$divs.Add(@{sc=$sc; pfF=$pfF; nc=$nc; pc=$pc; dx=($pPx-$nPx); dy=($pPy-$nPy); dvy=($pVy-$nVy)})
  }
}

Write-Host "Total divergent frames: $($divs.Count)"
if ($divs.Count -gt 0) {
  Write-Host "First 5 divs:"
  for ($i = 0; $i -lt [Math]::Min(10, $divs.Count); $i++) {
    $d = $divs[$i]
    Write-Host (("  sc={0,4} pfF={1,4} dx={14,3} dy={15,3} dvy={16,5} | NES(py={3,3},vy={5,5},gm={4}) PF(Ypx={7,3},VY={9},mode={8},Yfx={10}) slt={11} lst={12} slf={13}") -f `
      $d.sc, $d.pfF, $d.nc[$n_px], $d.nc[$n_py], $d.nc[$n_gm], $d.nc[$n_vy], `
      $d.pc[$pf_xpx], $d.pc[$pf_ypx], $d.pc[$pf_mode], $d.pc[$pf_vy], $d.pc[$pf_yfx], `
      $d.pc[$pf_slt], $d.pc[$pf_lst], $d.pc[$pf_slf], $d.dx, $d.dy, $d.dvy)
  }
  Write-Host ""
  Write-Host "Context around first divergence (sc-5 .. sc+10):"
  $firstSc = $divs[0].sc
  $lo = [Math]::Max(1, $firstSc - 5)
  $hi = $firstSc + 10
  Write-Host ("{0,-5} {1,-6} | NES px,py,vy,gm,sy,rawy        | PF Xpx,Ypx,VY,mode,Yfx,camY,gf | slt lst slf" -f "sc","pfF")
  for ($sc = $lo; $sc -le $hi; $sc++) {
    if (-not $nesMap.ContainsKey($sc)) { continue }
    $pfF = $sc - 1
    if (-not $pfMap.ContainsKey($pfF)) { continue }
    $nc = $nesMap[$sc]; $pc = $pfMap[$pfF]
    $marker = ""
    if ([int64]$nc[$n_px] -ne [int64]$pc[$pf_xpx] -or [int64]$nc[$n_py] -ne [int64]$pc[$pf_ypx]) { $marker = "  <-- DIV" }
    $nesStr = "{0,5},{1,3},{2,5},{3},{4,5},{5,6}" -f $nc[$n_px],$nc[$n_py],$nc[$n_vy],$nc[$n_gm],$nc[$n_sy],$nc[$n_rawy]
    $pfStr = "{0,5},{1,3},{2,9},{3},{4,7},{5,3},{6}" -f $pc[$pf_xpx],$pc[$pf_ypx],$pc[$pf_vy],$pc[$pf_mode],$pc[$pf_yfx],$pc[$pf_camy],$pc[$pf_grav]
    Write-Host ("{0,-5} {1,-6} | {2} | {3} | {4} {5} {6}{7}" -f $sc, $pfF, $nesStr, $pfStr, $pc[$pf_slt], $pc[$pf_lst], $pc[$pf_slf], $marker)
  }
}
