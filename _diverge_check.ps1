param([int]$startSim=5400, [int]$endSim=5560)
$nes="$env:TEMP\famidash_mesen_trace.csv"
$pf="$env:TEMP\famidash_pf_trace.csv"
$nesMap = @{}
Get-Content $nes | ForEach-Object {
    $c = $_ -split ','
    if ($c[1] -match '^\d+$') {
        $sim = [int]$c[1]
        if (-not $nesMap.ContainsKey($sim)) {
            $vy = [int]$c[10]
            $nesMap[$sim] = @{ px=[int]$c[2]; py=[int]$c[3]; vy=$vy; mode=$c[14]; mini=$c[24] }
        }
    }
}
$pfMap = @{}
Get-Content $pf | ForEach-Object {
    if ($_ -match '^(\d+),0x([0-9A-Fa-f]+),0x([0-9A-Fa-f]+),0x([0-9A-Fa-f]+),(\d),(\d),(\d+),(\d+),(\d),(\d+),(\d+),(\d+),') {
        $f=[int]$Matches[1]
        $vy=[Convert]::ToInt32($Matches[4],16)
        if ($vy -ge 0x80000000) { $vy = $vy - 0x100000000 } elseif ($vy -ge 0x8000) { }
        $pfMap[$f] = @{ px=[int]$Matches[7]; py=[int]$Matches[8]; vy=$vy; mode=[int]$Matches[12]; mini=[int]$Matches[6] }
    }
}
$results = @()
for ($s = $startSim; $s -le $endSim; $s++) {
    $f = $s - 1
    if ($nesMap.ContainsKey($s) -and $pfMap.ContainsKey($f)) {
        $n = $nesMap[$s]; $p = $pfMap[$f]
        $dx = $p.px - $n.px; $dy = $p.py - $n.py; $dvy = $p.vy - $n.vy
        $diff = ($dx -ne 0) -or ($dy -ne 0) -or ($dvy -ne 0)
        if ($diff) {
            $results += [pscustomobject]@{ sim=$s; pff=$f; PFx=$p.px; PFy=$p.py; PFvy=$p.vy; PFmode=$p.mode; PFmini=$p.mini; NESx=$n.px; NESy=$n.py; NESvy=$n.vy; dx=$dx; dy=$dy; dvy=$dvy }
        }
    }
}
$results | Format-Table -AutoSize
