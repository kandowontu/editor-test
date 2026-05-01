$content = Get-Content "c:\Editor Test\native-windows\famidash_trace_compare.txt"
# find rows with non-zero dy near rom_frame 1900-2020
$count=0
foreach ($row in $content) {
  if ($row -match '^(\d+),(\d+),\((-?\d+),(-?\d+)\),\((-?\d+),(-?\d+)\),') {
    $rf=[int]$matches[1]; $dy=[int]$matches[6]-[int]$matches[4]; $dx=[int]$matches[5]-[int]$matches[3]
    if ($rf -ge 1990 -and $rf -le 2050) {
      "rom=$rf NES=($($matches[3]),$($matches[4])) SIM=($($matches[5]),$($matches[6])) dx=$dx dy=$dy"
      $count++
      if ($count -gt 50) { break }
    }
  }
}
