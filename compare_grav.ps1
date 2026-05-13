$nes = "$env:TEMP\famidash_mesen_trace.csv"
$pf  = "$env:TEMP\famidash_pf_trace.csv"
$nesRows = @{}
Get-Content $nes | Where-Object { $_ -match '^\d' } | ForEach-Object {
    $c = $_.Split(',')
    if ($c.Length -lt 15) { return }
    $sim = [int]$c[1]
    if (-not $nesRows.ContainsKey($sim)) { $nesRows[$sim] = $c }
}
$prevG = 99
$out = New-Object System.Collections.Generic.List[string]
$pfLines = Get-Content $pf | Select-Object -Skip 1
foreach ($line in $pfLines) {
    $c = $line.Split(',')
    $f = [int]$c[0]
    $simKey = $f + 1
    if (-not $nesRows.ContainsKey($simKey)) { continue }
    $n = $nesRows[$simKey]
    $pfG = [int]$c[12]
    $nesG = [int]$n[12]   # gravity_mod
    $dG = $pfG - $nesG
    if ($dG -ne $prevG) {
        $pfX=[int]$c[6]; $pfY=[int]$c[7]
        $out.Add(("f={0,5} X={1,5} Y={2,4}  PFgrav={3} NESgrav={4}  diff={5}" -f $f,$pfX,$pfY,$pfG,$nesG,$dG))
        $prevG = $dG
    }
}
"Total grav-diff transitions: $($out.Count)"
"--- first 30 ---"
$out | Select-Object -First 30
"--- last 5 ---"
$out | Select-Object -Last 5
