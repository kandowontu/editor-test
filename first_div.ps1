param(
  [string]$Pf  = "$env:TEMP\famidash_pf_trace_dorabaebasic6_20260526_185253_741.csv",
  [string]$Nes = "$env:TEMP\famidash_mesen_trace_dorabaebasic6_20260526_190145_204.csv"
)
$pfLines  = Get-Content $Pf
$nesLines = Get-Content $Nes
$pfHdr  = $pfLines[0]  -split ","
$nesHdr = $nesLines[1] -split ","
$pfYi  = [Array]::IndexOf($pfHdr,"Y_fixed")
$pfYpx = [Array]::IndexOf($pfHdr,"Y_px")
$nesCp = [Array]::IndexOf($nesHdr,"cp_y")
$nesPy = [Array]::IndexOf($nesHdr,"py")
$nesSc = [Array]::IndexOf($nesHdr,"sim_cursor")
$nesByS = @{}
foreach ($l in ($nesLines | Select-Object -Skip 2)) {
  $c = $l -split ","
  if ($c.Length -le $nesSc) { continue }
  $s = $c[$nesSc]
  if ($s -match '^\d+$') { $nesByS[[int]$s] = $c }
}
$firstY = $null; $firstLow = $null
$nesMaxSc = ($nesByS.Keys | Sort-Object -Descending | Select-Object -First 1)
$max = [Math]::Min($pfLines.Count-1, [int]$nesMaxSc)
for ($f=0; $f -lt $max; $f++) {
  if (($f+1) -ge $pfLines.Count) { break }
  $pfRow = $pfLines[$f+1] -split ","
  $sc = $f + 1
  if (-not $nesByS.ContainsKey($sc)) { continue }
  $nesRow = $nesByS[$sc]
  $pfYval = [int64]$pfRow[$pfYi]
  $pfYlow = $pfYval -band 0xFF
  $nesYlow = [int64]$nesRow[$nesCp] -band 0xFF
  $pfYpxV = [int]$pfRow[$pfYpx]
  $nesPyV = [int]$nesRow[$nesPy]
  if (($firstLow -eq $null) -and ($pfYlow -ne $nesYlow)) {
    $firstLow = $f
    "First Y_low divergence at frame=$f  PF=0x{0:X2} NES=0x{1:X2}  PF_Y_px={2} NES_py={3}" -f $pfYlow,$nesYlow,$pfYpxV,$nesPyV
  }
  if (($firstY -eq $null) -and ($pfYpxV -ne $nesPyV)) {
    $firstY = $f
    "First Y_px divergence at frame=$f  PF_Y_px={0} NES_py={1}  PF_low=0x{2:X2} NES_low=0x{3:X2}" -f $pfYpxV,$nesPyV,$pfYlow,$nesYlow
  }
  if (($firstY -ne $null) -and ($firstLow -ne $null)) { break }
}
if ($firstLow -eq $null) { "No Y_low divergence detected" }
if ($firstY -eq $null) { "No Y_px divergence detected" }
