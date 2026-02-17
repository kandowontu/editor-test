$tmxPath = "C:\Editor Test\cycles.tmx"
[xml]$tmx = Get-Content $tmxPath -Raw
$tileLayer = $tmx.map.layer | Where-Object { $_.name -eq "layer" }
$spLayer = $tmx.map.layer | Where-Object { $_.name -eq "SP" }
$tileData = $tileLayer.data.InnerText.Trim().Split(",") | ForEach-Object { [int]$_.Trim() }
$spData = $spLayer.data.InnerText.Trim().Split(",") | ForEach-Object { [int]$_.Trim() }
$w = [int]$tmx.map.width

# Check specific tile array positions of interest
$positions = @(
    @(244, 24, "Death at X=3902 center tile"),
    @(240, 21, "Death at X=3838 center tile"),
    @(233, 21, "Death at X=3728 center tile"),
    @(237, 23, "Death at X=3791 center tile"),
    @(250, 16, "FWD_COLL tile at X=3985"),
    @(228, 24, "Pole position"),
    @(231, 23, "Pole position"),
    @(240, 23, "Pole position"),
    @(243, 23, "Pole position"),
    @(243, 24, "Pole position"),
    @(247, 23, "Pole 0x2B position")
)

Write-Output "Map width: $w"
Write-Output ""

foreach ($pos in $positions) {
    $col = $pos[0]; $row = $pos[1]; $desc = $pos[2]
    $idx = $row * $w + $col
    $tgid = $tileData[$idx]
    $sgid = $spData[$idx]
    $tid = if ($tgid -gt 0) { $tgid - 1 } else { 0 }
    $sid = if ($sgid -gt 0) { $sgid - 257 } else { -1 }
    Write-Output "$desc : TMX($col,$row) tileGID=$tgid tid=0x$('{0:X2}' -f $tid) sprGID=$sgid sid=0x$('{0:X2}' -f $sid)"
}

# Also dump the tile row 24, cols 240-250
Write-Output ""
Write-Output "=== Tile layer row 24, cols 240-250 ==="
$line = "r24: "
foreach ($col in 240..250) {
    $idx = 24 * $w + $col
    $gid = $tileData[$idx]
    $tid = if ($gid -gt 0) { $gid - 1 } else { 0 }
    if ($gid -gt 0) {
        $line += "($col)=0x$('{0:X2}' -f $tid) "
    }
}
Write-Output $line

# And tile row 21, cols 238-244
Write-Output "=== Tile layer row 21, cols 238-244 ==="
$line = "r21: "
foreach ($col in 238..244) {
    $idx = 21 * $w + $col
    $gid = $tileData[$idx]
    $tid = if ($gid -gt 0) { $gid - 1 } else { 0 }
    if ($gid -gt 0) {
        $line += "($col)=0x$('{0:X2}' -f $tid) "
    } else {
        $line += "($col)=EMPTY "
    }
}
Write-Output $line
