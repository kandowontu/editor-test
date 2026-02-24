$tmx = "c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\everyend.tmx"
$content = [System.IO.File]::ReadAllText($tmx)
# Find CSV data between <data> tags
$m = [regex]::Match($content, '<data encoding="csv">\s*([\s\S]*?)\s*</data>')
$csv = $m.Groups[1].Value.Trim()
$allTiles = $csv -split "[,\r\n]+" | Where-Object { $_ -match "^\d+$" } | ForEach-Object { [int]$_ }
Write-Output "Total tiles: $($allTiles.Count)"
$mapW = 4573

# Collision lookup (simplified)
function Get-Col($tid) {
    switch ($tid) {
        0 { return "____" }
        0x01 { return "FLCL" }
        0x02 { return "FL__" }
        0x10 { return "SALL" }
        0x11 { return "DETH" }
        0x12 { return "D_BT" }
        0x13 { return "D_TP" }
        0x1B { return "DETH" }
        0x1C { return "DETH" }
        0x2F { return "____" }
        0x30 { return "SALL" }
        0xDA { return "SPUL" }
        default { 
            if ($tid -eq 0) { return "____" }
            return "{0:X2}" -f $tid
        }
    }
}

# Dump rows 35-56 for columns 375-406 (X=6000-6496)
Write-Output ""
Write-Output "=== Tile Grid: X=6000-6496, Rows 35-56 (TMX rows 38-59) ==="
$hdr = "     "
for ($c = 375; $c -le 406; $c++) { $hdr += "c$c " }
Write-Output $hdr
for ($row = 38; $row -le 59 -and $row -lt 57; $row++) {
    $worldRow = $row - 3
    $yPx = $worldRow * 16
    $line = "wR{0:D2} " -f $worldRow
    for ($col = 375; $col -le 406; $col++) {
        $idx = $row * $mapW + $col
        if ($idx -lt $allTiles.Count) {
            $tid = $allTiles[$idx]
            $line += (Get-Col $tid) + " "
        } else { $line += "???? " }
    }
    Write-Output "$line  (Y=${yPx}px)"
}

Write-Output ""
Write-Output "=== Platform area detail (rows 37-43, cols 375-415) ==="
for ($row = 40; $row -le 46; $row++) {
    $worldRow = $row - 3
    $yPx = $worldRow * 16
    $line = "wR{0:D2} " -f $worldRow
    for ($col = 375; $col -le 415; $col++) {
        $idx = $row * $mapW + $col
        $tid = $allTiles[$idx]
        $c = Get-Col $tid
        $line += $c + " "
    }
    Write-Output "$line  (Y=${yPx}px)"
}
