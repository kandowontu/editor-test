$pf = Get-Content "$env:TEMP\famidash_pf_trace.csv"
$mesen = Get-Content "$env:TEMP\famidash_mesen_trace.csv"
$mLookup = @{}
foreach ($l in $mesen) {
    if ($l -match '^(\d+),(\d+),\d+,\d+,\d+,\d+,\d+,\d+,\d+,(\d+)$') {
        $mLookup[[int]$Matches[2]] = [int]$Matches[3]
    }
}
$h = $pf[0] -split ','
$idxCam = [Array]::IndexOf($h, 'CamY_px')
$idxTgt = [Array]::IndexOf($h, 'TgtCamY_px')
Write-Host "CamY_px col=$idxCam  TgtCamY_px=$idxTgt"
$prev = -9999
for ($i = 1; $i -lt $pf.Count; $i++) {
    $line = $pf[$i]
    $cols = $line -split ','
    if ($cols.Count -lt 12) { continue }
    $f = [int]$cols[0]
    $cy = [int]$cols[$idxCam]
    $tgt = [int]$cols[$idxTgt]
    if ($mLookup.ContainsKey($f)) {
        $sy = $mLookup[$f] - 528
        $d = $cy - $sy
        if ($d -ne $prev) {
            Write-Host "f=$f PF_CamY=$cy NES_CamY_eq=$sy diff=$d (PF tgt=$tgt)"
            $prev = $d
        }
    }
}
