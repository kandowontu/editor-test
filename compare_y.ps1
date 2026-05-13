$nes = "$env:TEMP\famidash_mesen_trace.csv"
$pf  = "$env:TEMP\famidash_pf_trace.csv"
$nesRows = @{}
Get-Content $nes | Where-Object { $_ -match '^\d' } | ForEach-Object {
    $c = $_.Split(',')
    if ($c.Length -lt 15) { return }
    $sim = [int]$c[1]
    if (-not $nesRows.ContainsKey($sim)) { $nesRows[$sim] = $c }
}
$out = New-Object System.Collections.Generic.List[string]
$out.Add("frame  PFx  NESx  dX  PFy  NESy  dY")
$pfLines = Get-Content $pf | Select-Object -Skip 1
foreach ($line in $pfLines) {
    $c = $line.Split(',')
    $f = [int]$c[0]
    if (-not $nesRows.ContainsKey($f)) { continue }
    $n = $nesRows[$f]
    $pfX = [int]$c[6]; $nesX = [int]$n[2]
    $pfY = [int]$c[7]; $nesY = [int]$n[3]
    $dY = $pfY - $nesY
    if ([math]::Abs($dY) -gt 4) {
        $out.Add(("{0,5} {1,5} {2,5} {3,4} {4,4} {5,4} {6,4}" -f $f,$pfX,$nesX,($pfX-$nesX),$pfY,$nesY,$dY))
    }
}
$out | Select-Object -First 30
Write-Host "---"
$out | Select-Object -Last 5
