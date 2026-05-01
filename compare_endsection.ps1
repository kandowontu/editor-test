$mesen = Get-Content "$env:TEMP\famidash_mesen_trace.csv"
$sim = @{}
foreach ($l in Get-Content "$env:TEMP\famidash_replay.csv") {
    if ($l -match '^\d') {
        $p = $l -split ','
        $sim[[int]$p[0]] = @{X=[int]$p[1]; Y=[int]$p[2]}
    }
}

"=== Around the high ship portal (lines 4400..4749) ==="
"=== Looking for |dy|>=2 ==="
for ($i=4400; $i -le 4749; $i++) {
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
    if ([Math]::Abs($dx) -ge 2 -or [Math]::Abs($dy) -ge 2) {
        "line=$i rom=$rom cur=$cur NES=($px,$py) SIM=($($s.X),$($s.Y)) dx=$dx dy=$dy scrolly=$sy"
    }
}
