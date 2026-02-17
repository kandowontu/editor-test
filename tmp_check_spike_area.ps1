$tmxPath = "C:\Editor Test\cycles.tmx"
[xml]$tmx = Get-Content $tmxPath -Raw
$tileLayer = $tmx.map.layer | Where-Object { $_.name -eq "layer" }
$tileData = $tileLayer.data.InnerText.Trim().Split(",") | ForEach-Object { [int]$_.Trim() }
$w = [int]$tmx.map.width

# Dump tiles around (228,24) - cols 225-232, rows 20-27
Write-Output "=== Tile layer around (228,24) ==="
Write-Output "groundRowsToReserve=3, tileY = tileArrayY - 3"
Write-Output ""
foreach ($row in 20..26) {
    $worldTileY = $row - 3
    $worldY = $worldTileY * 16
    $line = "r$row (tY=$worldTileY wY=$worldY): "
    foreach ($col in 225..232) {
        $idx = $row * $w + $col
        $gid = $tileData[$idx]
        $tid = if ($gid -gt 0) { $gid - 1 } else { -1 }
        if ($gid -gt 0) {
            $line += "($col)=0x$('{0:X2}' -f $tid) "
        } else {
            $line += "($col)=---- "
        }
    }
    Write-Output $line
}

# Also check what collision type each non-empty tile maps to
Write-Output ""
Write-Output "=== Collision types for unique tiles found ==="
$uniqueTiles = @{}
foreach ($row in 20..26) {
    foreach ($col in 225..232) {
        $idx = $row * $w + $col
        $gid = $tileData[$idx]
        if ($gid -gt 0) {
            $tid = $gid - 1
            $key = "0x$('{0:X2}' -f $tid)"
            if (-not $uniqueTiles.ContainsKey($key)) {
                $uniqueTiles[$key] = "r$row,c$col"
            }
        }
    }
}
foreach ($key in $uniqueTiles.Keys | Sort-Object) {
    Write-Output "  $key first at $($uniqueTiles[$key])"
}

# Check the cube death scenario:
# Cube on platform at r25 (floor top = worldTileY 22, Y=352)
# Cube Y = 352 - 15 = 337, center Y = 337 + 7 = 344
# Center tileY = 344/16 = 21, tileArrayY = 24
# Check what's at each column at tileArrayY 24:
Write-Output ""
Write-Output "=== Death check tiles at tileArrayY=24 (where cube center is when on floor) ==="
foreach ($col in 225..232) {
    $idx = 24 * $w + $col
    $gid = $tileData[$idx]
    $tid = if ($gid -gt 0) { $gid - 1 } else { -1 }
    $tidHex = if ($tid -ge 0) { "0x$('{0:X2}' -f $tid)" } else { "----" }
    Write-Output "  ($col,24) = $tidHex"
}

# And forward collision check: probe at rightEdge = X + 15, centerY = Y + 7
# When cube is at pixel col 228 area (X=228*16=3648), rightEdge=3648+15=3663
# tileX = 3663/16 = 228 (same col)
Write-Output ""
Write-Output "=== Forward collision tiles at tileArrayY=24 (probe center) ==="
Write-Output "  Cube at X=3648 (col 228), rightEdge=3663, probe tileX=228"
Write-Output "  Cube at X=3632 (col 227), rightEdge=3647, probe tileX=227"

# Check eject floor check: playerBottom at tileArrayY=25
Write-Output ""
Write-Output "=== Floor tiles at tileArrayY=25 (floor landing) ==="
foreach ($col in 225..232) {
    $idx = 25 * $w + $col
    $gid = $tileData[$idx]
    $tid = if ($gid -gt 0) { $gid - 1 } else { -1 }
    $tidHex = if ($tid -ge 0) { "0x$('{0:X2}' -f $tid)" } else { "----" }
    Write-Output "  ($col,25) = $tidHex"
}

# Check eject floor spike check - spikes at feet
# When cube is landing on r25 floor, playerBottom = 352
# Spike check Y = 352, tileY = 352/16 = 22, tileArrayY = 22+3 = 25
Write-Output ""
Write-Output "=== Staircase spikes above platform ==="
foreach ($col in 224..232) {
    foreach ($row in 21..24) {
        $idx = $row * $w + $col
        $gid = $tileData[$idx]
        if ($gid -gt 0) {
            $tid = $gid - 1
            $worldTileY = $row - 3
            $worldY = $worldTileY * 16
            Write-Output "  ($col,$row) tid=0x$('{0:X2}' -f $tid) worldTileY=$worldTileY topY=$worldY botY=$($worldY+16)"
        }
    }
}
