param([int]$MinSc = 0, [int]$MaxSc = 5000, [int]$Limit = 50, [switch]$OnlyDiff)
$pf = "$env:TEMP\famidash_pf_trace_dorabaebasic6_20260526_125956_790.csv"
$nes = "$env:TEMP\famidash_mesen_trace_dorabaebasic6_20260526_130706_834.csv"
$pfRows = @{}
foreach ($l in Get-Content $pf) {
    $p = $l -split ','
    if ($p.Length -lt 11) { continue }
    if ($p[0] -notmatch '^\d+$') { continue }
    $f = [int]$p[0]
    $vyHex = $p[3]
    $vy = [Convert]::ToInt64($vyHex.Substring(2), 16)
    if ($vy -gt 2147483647) { $vy -= 4294967296 }
    $pfRows[$f] = [pscustomobject]@{ X=[int]$p[6]; Y=[int]$p[7]; Vy=$vy; CamY=[int]$p[9]; Ylow=$p[20]; CamLow=$p[21]; Sub=[int]$p[22] }
}
$out = New-Object System.Collections.Generic.List[string]
$out.Add("rom,sim,nes_px,nes_py,pf_x,pf_y,dx,dy,nes_vy,pf_vy,nes_sy,nes_subpx,nes_tgt")
$count = 0
foreach ($l in Get-Content $nes) {
    $p = $l -split ','
    if ($p.Length -lt 20) { continue }
    if ($p[0] -notmatch '^\d+$') { continue }
    $rf = [int]$p[0]
    $sc = [int]$p[1]
    if ($sc -lt $MinSc -or $sc -gt $MaxSc) { continue }
    $pfIdx = $sc - 1
    if (-not $pfRows.ContainsKey($pfIdx)) { continue }
    $pr = $pfRows[$pfIdx]
    $nes_px = [int]$p[2]; $nes_py = [int]$p[3]
    $nes_vy = [int]$p[10]; $nes_sy = [int]$p[9]; $nes_sub = [int]$p[15]; $nes_tgt = [int]$p[17]
    $dx = $nes_px - $pr.X; $dy = $nes_py - $pr.Y
    if ($OnlyDiff -and $dx -eq 0 -and $dy -eq 0) { continue }
    $out.Add("$rf,$sc,$nes_px,$nes_py,$($pr.X),$($pr.Y),$dx,$dy,$nes_vy,$($pr.Vy),$nes_sy,$nes_sub,$nes_tgt")
    $count++
    if ($count -ge $Limit) { break }
}
$out | ForEach-Object { $_ }
