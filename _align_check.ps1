param([int]$startSim=600, [int]$endSim=5560)
$nes="$env:TEMP\famidash_mesen_trace.csv"
$pf="$env:TEMP\famidash_pf_trace.csv"
$nesMap = @{}
Get-Content $nes | ForEach-Object {
    $c = $_ -split ','
    if ($c[1] -match '^\d+$' -and $c[10] -match '^-?\d+$') {
        $sim = [int]$c[1]
        if (-not $nesMap.ContainsKey($sim)) {
            $nesMap[$sim] = @{ px=[int]$c[2]; py=[int]$c[3]; vy=[int]$c[10]; mode=[int]$c[14]; mini=[int]$c[24] }
        }
    }
}
$pfRows = @{}
Get-Content $pf | ForEach-Object {
    if ($_ -match '^(\d+),') {
        $cols = $_ -split ','
        if ($cols[0] -match '^\d+$') {
            $f = [int]$cols[0]
            $vy = [Convert]::ToInt64($cols[3].Substring(2),16)
            if ($vy -ge 0x80000000) { $vy = $vy - 0x100000000 }
            $pfRows[$f] = @{ px=[int]$cols[6]; py=[int]$cols[7]; vy=$vy; mode=[int]$cols[11]; mini=[int]$cols[23] }
        }
    }
}
"=== offset PF_f = NES_sim - 1 ==="
$reported = 0
for ($s = $startSim; $s -le $endSim; $s++) {
    $f = $s - 1
    if (-not $nesMap.ContainsKey($s) -or -not $pfRows.ContainsKey($f)) { continue }
    $n = $nesMap[$s]; $p = $pfRows[$f]
    $dx = $p.px - $n.px; $dy = $p.py - $n.py
    if ($dx -ne 0 -or $dy -ne 0) {
        "sim=$s pff=$f dx=$dx dy=$dy PF=($($p.px),$($p.py))m=$($p.mode)mi=$($p.mini)vy=$($p.vy) NES=($($n.px),$($n.py))m=$($n.mode)mi=$($n.mini)vy=$($n.vy)"
        $reported++
        if ($reported -ge 50) { break }
    }
}
