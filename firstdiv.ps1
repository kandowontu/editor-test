param(
  [string]$Pf  = "$env:TEMP\famidash_pf_trace_dorabaebasic6_20260526_213546_860.csv",
  [string]$Nes = "$env:TEMP\famidash_mesen_trace_dorabaebasic6_20260526_214317_717.csv",
  [int]$FrameOffset = 0   # PF_frame = NES_sim_cursor + FrameOffset
)

$pf = Get-Content $Pf
$pfHdr = $pf[0] -split ","
$pf_f = [Array]::IndexOf($pfHdr, "frame")
$pf_xpx = [Array]::IndexOf($pfHdr, "X_px")
$pf_ypx = [Array]::IndexOf($pfHdr, "Y_px")
$pf_vy = [Array]::IndexOf($pfHdr, "VelY_fixed")
$pf_yfx = [Array]::IndexOf($pfHdr, "Y_fixed")
$pf_mode = [Array]::IndexOf($pfHdr, "mode")
$pf_slt = [Array]::IndexOf($pfHdr, "SlopeType")
$pf_lst = [Array]::IndexOf($pfHdr, "LastSlopeType")
$pf_slf = [Array]::IndexOf($pfHdr, "SlopeFrames")

$nes = Get-Content $Nes
$nesHdr = $nes[1] -split ","
$n_sc = [Array]::IndexOf($nesHdr, "sim_cursor")
$n_px = [Array]::IndexOf($nesHdr, "px")
$n_py = [Array]::IndexOf($nesHdr, "py")
$n_vy = [Array]::IndexOf($nesHdr, "vel_y")
$n_gm = [Array]::IndexOf($nesHdr, "gamemode")
$n_rawy = [Array]::IndexOf($nesHdr, "raw_y")
$n_sy = [Array]::IndexOf($nesHdr, "scrolly")

$pfMap = @{}
for ($i = 1; $i -lt $pf.Count; $i++) {
  $c = $pf[$i] -split ","
  if ($c.Length -gt $pf_f) { $pfMap[[int]$c[$pf_f]] = $c }
}

$nesMap = @{}
for ($i = 2; $i -lt $nes.Count; $i++) {
  $c = $nes[$i] -split ","
  if ($c.Length -le $n_sc) { continue }
  $sc = $c[$n_sc]
  if ($sc -notmatch '^\d+$') { continue }
  $sci = [int]$sc
  if (-not $nesMap.ContainsKey($sci)) { $nesMap[$sci] = $c }
}

$firstDiv = -1
$divInfo = ""
$keys = $nesMap.Keys | Sort-Object
foreach ($sc in $keys) {
  $pfF = $sc + $FrameOffset
  if (-not $pfMap.ContainsKey($pfF)) { continue }
  $nc = $nesMap[$sc]
  $pc = $pfMap[$pfF]
  $nPx = [int64]$nc[$n_px]; $nPy = [int64]$nc[$n_py]
  $pPx = [int64]$pc[$pf_xpx]; $pPy = [int64]$pc[$pf_ypx]
  if ($nPx -lt 0 -or $nPx -gt 100000) { continue }
  if ($nPx -ne $pPx -or $nPy -ne $pPy) {
    $firstDiv = $sc
    $divInfo = "NES sc=$sc px=$nPx py=$nPy gm=$($nc[$n_gm]) vy=$($nc[$n_vy])  vs  PF f=$pfF Xpx=$pPx Ypx=$pPy mode=$($pc[$pf_mode]) VY=$($pc[$pf_vy])"
    break
  }
}

Write-Host "First divergence: $firstDiv"
Write-Host $divInfo

if ($firstDiv -ge 0) {
  $lo = [Math]::Max(1, $firstDiv - 5)
  $hi = $firstDiv + 8
  Write-Host ""
  Write-Host ("{0,-6} | NES px,py,vy,gm,rawy | PF Xpx,Ypx,VY,mode,Yfx | slt lst slf" -f "sc")
  for ($sc = $lo; $sc -le $hi; $sc++) {
    if (-not $nesMap.ContainsKey($sc)) { continue }
    $pfF = $sc + $FrameOffset
    if (-not $pfMap.ContainsKey($pfF)) { continue }
    $nc = $nesMap[$sc]; $pc = $pfMap[$pfF]
    $nesStr = "{0,5},{1,3},{2,4},{3},{4,6}" -f $nc[$n_px],$nc[$n_py],$nc[$n_vy],$nc[$n_gm],$nc[$n_rawy]
    $pfStr = "{0,5},{1,3},{2,5},{3},{4,6}" -f $pc[$pf_xpx],$pc[$pf_ypx],$pc[$pf_vy],$pc[$pf_mode],$pc[$pf_yfx]
    Write-Host ("{0,-6} | {1} | {2} | {3} {4} {5}" -f $sc, $nesStr, $pfStr, $pc[$pf_slt], $pc[$pf_lst], $pc[$pf_slf])
  }
}
