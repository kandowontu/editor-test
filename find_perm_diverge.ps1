$lines = Get-Content "$env:TEMP\famidash_trace_compare.txt"
# Find last line where dx=0 dy=0
$last = -1
for ($i = $lines.Count - 1; $i -ge 17; $i--) {
    if ($lines[$i] -match '^(\d+),\d+,\([-\d]+,[-\d]+\),\([-\d]+,[-\d]+\),(-?\d+),(-?\d+),') {
        $dx = [int]$Matches[2]
        $dy = [int]$Matches[3]
        if ($dx -eq 0 -and $dy -eq 0) { $last = $i; break }
    }
}
Write-Host "last fully-aligned line=$last"
$end = [Math]::Min($last + 60, $lines.Count - 1)
$lines[$last..$end]
