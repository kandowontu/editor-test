$nes = "$env:TEMP\famidash_mesen_trace.csv"
$pf  = "$env:TEMP\famidash_pf_trace.csv"
$nesRows = @{}
Get-Content $nes | Where-Object { $_ -match '^\d' } | ForEach-Object {
    $c = $_.Split(',')
    if ($c.Length -lt 15) { return }
    $sim = [int]$c[1]
    if (-not $nesRows.ContainsKey($sim)) { $nesRows[$sim] = $c }
}
$prevDX = 99; $prevDY = 99
$out = New-Object System.Collections.Generic.List[string]
$pfLines = Get-Content $pf | Select-Object -Skip 1
foreach ($line in $pfLines) {
    $c = $line.Split(',')
    $f = [int]$c[0]
    $simKey = $f + 1   # PF f=N corresponds to NES sim_cursor=N+1
    if (-not $nesRows.ContainsKey($simKey)) { continue }
    $n = $nesRows[$simKey]
    $pfX = [int]$c[6]; $nesX = [int]$n[2]
    $pfY = [int]$c[7]; $nesY = [int]$n[3]
    $dX = $pfX - $nesX; $dY = $pfY - $nesY
    if ($dX -ne $prevDX -or $dY -ne $prevDY) {
        $out.Add(("f={0,5}->sim={1,5} PFx={2,5} NESx={3,5} dX={4,3} PFy={5,4} NESy={6,4} dY={7,4}" -f $f,$simKey,$pfX,$nesX,$dX,$pfY,$nesY,$dY))
        $prevDX = $dX; $prevDY = $dY
    }
}
"Total transitions: $($out.Count)"
"--- first 15 ---"
$out | Select-Object -First 15
"--- last 30 ---"
$out | Select-Object -Last 30
