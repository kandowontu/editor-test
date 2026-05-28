$pf = "$env:TEMP\famidash_pf_trace_cataclysm_20260518_151738_650.csv"
$mt = "$env:TEMP\famidash_mesen_trace_cataclysm_20260518_151724_006.csv"

$pfRows = @{}
Get-Content $pf | Select-Object -Skip 1 | ForEach-Object {
    $p = $_ -split ','
    if ($p[0] -match '^\d+$') {
        $pfRows[[int]$p[0]] = [PSCustomObject]@{
            x = [int]$p[6]; y = [int]$p[7]
            vy = [int]("0x" + $p[3].TrimStart('0x'))
            mode = [int]$p[11]; mini = [int]$p[23]
        }
    }
}

$mtRows = @{}
Get-Content $mt | Select-Object -Skip 2 | ForEach-Object {
    $p = $_ -split ','
    if ($p[1] -match '^\d+$') {
        $s = [int]$p[1]
        if (-not $mtRows.ContainsKey($s)) {
            $mtRows[$s] = [PSCustomObject]@{
                x = [int]$p[2]; y = [int]$p[3]
                vy = [int]$p[10]; mode = [int]$p[14]; mini = [int]$p[24]
                rawx = [int64]$p[6]; rawy = [int64]$p[7]
            }
        }
    }
}

"sim`tPFx`tMTx`tdx`tPFy`tMTy`tdy`tPFvy`tMTvy`tPFmode`tMTmode"
$firstDx = -1; $firstDy = -1; $firstMode = -1
for ($i = 0; $i -le 412; $i++) {
    if ($pfRows.ContainsKey($i) -and $mtRows.ContainsKey($i)) {
        $p = $pfRows[$i]; $m = $mtRows[$i]
        $dx = $m.x - $p.x; $dy = $m.y - $p.y
        if ($firstDx -lt 0 -and $dx -ne 0) { $firstDx = $i }
        if ($firstDy -lt 0 -and $dy -ne 0) { $firstDy = $i }
        if ($firstMode -lt 0 -and $p.mode -ne $m.mode) { $firstMode = $i }
    }
}
"FIRST dx mismatch: $firstDx"
"FIRST dy mismatch: $firstDy"
"FIRST mode mismatch: $firstMode"
""
"--- around dy first divergence ---"
for ($i = [Math]::Max(0, $firstDy - 5); $i -le ($firstDy + 10); $i++) {
    if ($pfRows.ContainsKey($i) -and $mtRows.ContainsKey($i)) {
        $p = $pfRows[$i]; $m = $mtRows[$i]
        "$i`t$($p.x)`t$($m.x)`t$($m.x-$p.x)`t$($p.y)`t$($m.y)`t$($m.y-$p.y)`t$($p.vy)`t$($m.vy)`t$($p.mode)`t$($m.mode)"
    }
}
""
"--- around death ---"
for ($i = 388; $i -le 412; $i++) {
    if ($pfRows.ContainsKey($i) -and $mtRows.ContainsKey($i)) {
        $p = $pfRows[$i]; $m = $mtRows[$i]
        "$i`t$($p.x)`t$($m.x)`t$($m.x-$p.x)`t$($p.y)`t$($m.y)`t$($m.y-$p.y)`t$($p.vy)`t$($m.vy)`t$($p.mode)`t$($m.mode)"
    }
}
