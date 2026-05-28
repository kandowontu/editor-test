param(
  [string]$Pf  = "$env:TEMP\famidash_pf_trace_dorabaebasic6_20260526_185253_741.csv",
  [string]$Nes = "$env:TEMP\famidash_mesen_trace_dorabaebasic6_20260526_190145_204.csv",
  [int]$Start = 0,
  [int]$Count = 50
)
$pfLines  = Get-Content $Pf
$nesLines = Get-Content $Nes
$pfHdr  = $pfLines[0]  -split ","
$nesHdr = $nesLines[1] -split ","
$pfYi  = [Array]::IndexOf($pfHdr,"Y_fixed")
$pfVy  = [Array]::IndexOf($pfHdr,"VelY_fixed")
$pfYpx = [Array]::IndexOf($pfHdr,"Y_px")
$pfLow = [Array]::IndexOf($pfHdr,"Y_lowB")
$nesCp = [Array]::IndexOf($nesHdr,"cp_y")
$nesVy = [Array]::IndexOf($nesHdr,"vel_y")
$nesPy = [Array]::IndexOf($nesHdr,"py")
$nesSc = [Array]::IndexOf($nesHdr,"sim_cursor")
$nesByS = @{}
foreach ($l in ($nesLines | Select-Object -Skip 2)) {
  $c = $l -split ","
  if ($c.Length -le $nesSc) { continue }
  $s = $c[$nesSc]
  if ($s -match '^\d+$') { $nesByS[[int]$s] = $c }
}
"PF rows: $($pfLines.Count-1)  NES sims: $($nesByS.Count)"
"frame  PF_Y(int.lo) Y_px  NES_Y_lo py  PF_vy NES_vy  dY dLow"
for ($f=$Start; $f -lt ($Start+$Count); $f++) {
  if (($f+1) -ge $pfLines.Count) { break }
  $pfRow = $pfLines[$f+1] -split ","
  $sc = $f + 1
  if (-not $nesByS.ContainsKey($sc)) { continue }
  $nesRow = $nesByS[$sc]
  $pfYval  = [int64]$pfRow[$pfYi]
  $pfYint  = $pfYval -shr 8
  $pfYlow  = $pfYval -band 0xFF
  $nesYlow = [int64]$nesRow[$nesCp] -band 0xFF
  $pfYpxV  = [int]$pfRow[$pfYpx]
  $nesPyV  = [int]$nesRow[$nesPy]
  $dY    = $pfYpxV - $nesPyV
  $dLow  = $pfYlow - $nesYlow
  "{0,4}  ({1},0x{2:X2}) {3}  0x{4:X2} {5}  {6} {7}  {8} {9}" -f $f,$pfYint,$pfYlow,$pfYpxV,$nesYlow,$nesPyV,$pfRow[$pfVy],$nesRow[$nesVy],$dY,$dLow
}
