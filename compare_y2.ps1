$nes = "$env:TEMP\famidash_mesen_trace.csv"
$pf  = "$env:TEMP\famidash_pf_trace.csv"
$nesRows = @{}
Get-Content $nes | Where-Object { $_ -match '^\d' } | ForEach-Object {
    $c = $_.Split(',')
    if ($c.Length -lt 15) { return }
    $sim = [int]$c[1]
    if (-not $nesRows.ContainsKey($sim)) { $nesRows[$sim] = $c }
}
$prevDX = 0; $prevDY = 0
$out = New-Object System.Collections.Generic.List[string]
$pfLines = Get-Content $pf | Select-Object -Skip 1
foreach ($line in $pfLines) {
    $c = $line.Split(',')
    $f = [int]$c[0]
    if (-not $nesRows.ContainsKey($f)) { continue }
    $n = $nesRows[$f]
    $pfX = [int]$c[6]; $nesX = [int]$n[2]
    $pfY = [int]$c[7]; $nesY = [int]$n[3]
    $dX = $pfX - $nesX; $dY = $pfY - $nesY
    if ($dX -ne $prevDX -or $dY -ne $prevDY) {
        $out.Add(("f={0,5} PFx={1,5} NESx={2,5} dX={3,3} PFy={4,4} NESy={5,4} dY={6,4}" -f $f,$pfX,$nesX,$dX,$pfY,$nesY,$dY))
        $prevDX = $dX; $prevDY = $dY
    }
}
"Total transitions: $($out.Count)"
$out | Select-Object -First 20
Write-Host "..."
$out | Select-Object -Last 60
