$tmxPath = "C:\Editor Test\cycles.tmx"
[xml]$tmx = Get-Content $tmxPath -Raw
$spLayer = $tmx.map.layer | Where-Object { $_.name -eq "SP" }
$data = $spLayer.data.InnerText.Trim().Split(",") | ForEach-Object { [int]$_.Trim() }
$w = [int]$tmx.map.width

Write-Output "Map width: $w"
Write-Output ""
Write-Output "=== SP layer around (240-250, 21-25) ==="
foreach ($row in 21..25) {
    $line = "r${row}: "
    foreach ($col in 240..250) {
        $gid = $data[$row * $w + $col]
        if ($gid -gt 0) {
            $sid = $gid - 257
            $line += "($col)=0x$('{0:X2}' -f $sid) "
        }
    }
    if ($line -ne "r${row}: ") { Write-Output $line }
}

Write-Output ""
Write-Output "=== Checking specific positions ==="
foreach ($pos in @(@(244,23), @(243,23), @(243,22), @(244,22), @(245,23))) {
    $col = $pos[0]; $row = $pos[1]
    $idx = $row * $w + $col
    $gid = $data[$idx]
    $sid = if ($gid -gt 0) { $gid - 257 } else { -1 }
    Write-Output "  TMX($col,$row) idx=$idx gid=$gid sid=0x$('{0:X2}' -f $sid)"
}
