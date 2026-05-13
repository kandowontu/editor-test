$nes="$env:TEMP\famidash_mesen_trace.csv"
$pf="$env:TEMP\famidash_pf_trace.csv"
$pfRows=@{}
Get-Content $pf | Select-Object -Skip 1 | ForEach-Object {
    $c=$_ -split ','
    if($c[0] -match '^\d+$'){ $pfRows[[int]$c[0]] = $c }
}
$lo=[int]$args[0]; $hi=[int]$args[1]
Get-Content $nes | Select-Object -Skip 2 | ForEach-Object {
    $c=$_ -split ','
    if ($c.Count -ge 14 -and $c[1] -match '^\d+$') {
        $sim=[int]$c[1]
        $f=$sim-1
        if($pfRows.ContainsKey($f) -and $sim -ge $lo -and $sim -le $hi){
            $p=$pfRows[$f]
            $dx=[int]$p[6]-[int]$c[2]
            $dy=[int]$p[7]-[int]$c[3]
            $pg= if($p[12] -eq '1'){"FF"}else{"00"}
            "sim=$sim NES=($($c[2]),$($c[3]),vy=$($c[10])) PF=($($p[6]),$($p[7]),vy=$($p[3]),g=$pg) dx=$dx dy=$dy"
        }
    }
} | Select-Object -Unique
