$nes="$env:TEMP\famidash_mesen_trace.csv"
$pf="$env:TEMP\famidash_pf_trace.csv"
$pfRows=@{}
Get-Content $pf | Select-Object -Skip 1 | ForEach-Object {
    $c=$_ -split ','
    if($c[0] -match '^\d+$'){ $pfRows[[int]$c[0]] = $c }
}
$lo=[int]$args[0]; $hi=[int]$args[1]
# NES rom_frame starts before sim begins; sim_cursor=1 stays constant during intro.
# Find offset: rom_frame where sim first becomes 2 (= PF f=1).
$introOffset = $null
Get-Content $nes | Select-Object -Skip 2 | ForEach-Object {
    if ($null -eq $introOffset) {
        $c=$_ -split ','
        if ($c.Count -ge 14 -and $c[1] -match '^\d+$' -and [int]$c[1] -ge 2) {
            $introOffset = [int]$c[0] - 1
        }
    }
}
"intro offset rom_frame=$introOffset"
Get-Content $nes | Select-Object -Skip 2 | ForEach-Object {
    $c=$_ -split ','
    if ($c.Count -ge 14 -and $c[0] -match '^\d+$') {
        $rom=[int]$c[0]
        $f = $rom - $introOffset
        if($pfRows.ContainsKey($f) -and $f -ge $lo -and $f -le $hi){
            $p=$pfRows[$f]
            $px=[long]$c[2]; $py=[long]$c[3]
            if ($px -gt 2000000000) { $px = $px - 4294967296 }
            if ($py -gt 2000000000) { $py = $py - 4294967296 }
            $dx=[int]$p[6]-$px
            $dy=[int]$p[7]-$py
            $pg= if($p[12] -eq '1'){"FF"}else{"00"}
            "f=$f rom=$rom NES=($px,$py,vy=$($c[10]),sim=$($c[1])) PF=($($p[6]),$($p[7]),vy=$($p[3]),g=$pg) dx=$dx dy=$dy"
        }
    }
}
