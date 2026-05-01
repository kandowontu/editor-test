$content = Get-Content "$env:TEMP\famidash_trace_compare.txt"
$count = 0
$prev_dy = 0
foreach ($row in $content) {
  if ($row -match '^(\d+),(\d+),\((-?\d+),(-?\d+)\),\((-?\d+),(-?\d+)\),') {
    $dy = [int]$matches[6] - [int]$matches[4]
    $dx = [int]$matches[5] - [int]$matches[3]
    if ($dy -ne $prev_dy) {
      "rom=$($matches[1]) NES=($($matches[3]),$($matches[4])) SIM=($($matches[5]),$($matches[6])) dx=$dx dy=$dy (was $prev_dy)"
      $prev_dy = $dy
      $count++
      if ($count -ge 30) { break }
    }
  }
}
