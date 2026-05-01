$mesen = Get-Content "$env:TEMP\famidash_mesen_trace.csv"
$sim = @{}
foreach ($l in Get-Content "$env:TEMP\famidash_replay.csv") {
    if ($l -match '^\d') {
        $p = $l -split ','
        $sim[[int]$p[0]] = @{X=[int]$p[1]; Y=[int]$p[2]}
    }
}

$attempt2Start = 17
$attempt2End = 4749
$prevDx = 0
$prevDy = 0
$transitions = 0
"=== Searching attempt 2 (lines $attempt2Start..$attempt2End) for px/py divergence transitions ==="
for ($i=$attempt2Start; $i -le $attempt2End; $i++) {
    $l = $mesen[$i]
    if ($l -like "#*") { continue }
    $p = $l -split ','
    if ($p.Count -lt 9) { continue }
    $rom = [int]$p[0]
    $cur = [int]$p[1]
    $px = [int]$p[2]
    $py = [int]$p[3]
    $sx = [int]$p[7]
    $sy = [int]$p[8]
    $simIdx = $cur - 1
    if (-not $sim.ContainsKey($simIdx)) { continue }
    $s = $sim[$simIdx]
    $dx = $px - $s.X
    $dy = $py - $s.Y
    if ($dx -ne $prevDx -or $dy -ne $prevDy) {
        "rom=$rom cur=$cur NES=($px,$py) SIM=($($s.X),$($s.Y)) dx=$dx dy=$dy scrolly=$sy (prev dx=$prevDx dy=$prevDy)"
        $prevDx = $dx
        $prevDy = $dy
        $transitions++
        if ($transitions -gt 80) { break }
    }
}
