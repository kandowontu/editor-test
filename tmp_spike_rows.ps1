$tmxPath = "C:\Editor Test\cycles.tmx"
[xml]$tmx = Get-Content $tmxPath -Raw
$tl = $tmx.map.layer | Where-Object { $_.name -eq "layer" }
$d = $tl.data.InnerText.Trim().Split(",") | ForEach-Object { [int]$_.Trim() }
$w = [int]$tmx.map.width

Write-Output "=== Row 25 (tileY=22, worldY=352) cols 218-232 ==="
$line = ""
foreach ($c in 218..232) {
    $gid = $d[25 * $w + $c]
    $tid = if ($gid -gt 0) { $gid - 1 } else { -1 }
    if ($gid -gt 0) {
        $line += "($c)=0x$('{0:X2}' -f $tid) "
    } else {
        $line += "($c)=---- "
    }
}
Write-Output $line

Write-Output ""
Write-Output "=== Row 26 (tileY=23, worldY=368) cols 218-232 ==="
$line = ""
foreach ($c in 218..232) {
    $gid = $d[26 * $w + $c]
    $tid = if ($gid -gt 0) { $gid - 1 } else { -1 }
    if ($gid -gt 0) {
        $line += "($c)=0x$('{0:X2}' -f $tid) "
    } else {
        $line += "($c)=---- "
    }
}
Write-Output $line

Write-Output ""
Write-Output "=== Row 24 (tileY=21, worldY=336) cols 218-232 ==="
$line = ""
foreach ($c in 218..232) {
    $gid = $d[24 * $w + $c]
    $tid = if ($gid -gt 0) { $gid - 1 } else { -1 }
    if ($gid -gt 0) {
        $line += "($c)=0x$('{0:X2}' -f $tid) "
    } else {
        $line += "($c)=---- "
    }
}
Write-Output $line

# Key question: what are the spikes at r25 225-227 on the NES?
# 0x11 = standard upward spike = COL_DEATH
# COL_DEATH kill zone: Y[4,11] X[4,8]
# Cube on r26 floor: Y=353, center=360, localY in r25 tile = 360-352 = 8 (IN range [4,11])
# So the cube walking on r26 gets killed by r25 spikes. CORRECT NES behavior.
Write-Output ""
Write-Output "=== Cube on r26 floor vs r25 spikes ==="
Write-Output "Cube Y=353, centerY=360, in r25 tile: localY=360-352=8"
Write-Output "COL_DEATH kills at Y[4,11]: localY=8 IS in range -> KILL (correct)"

# Now check: what tiles are at r23 around col 228?
Write-Output ""
Write-Output "=== Row 23 (tileY=20, worldY=320) cols 224-232 ==="
$line = ""
foreach ($c in 224..232) {
    $gid = $d[23 * $w + $c]
    $tid = if ($gid -gt 0) { $gid - 1 } else { -1 }
    if ($gid -gt 0) {
        $line += "($c)=0x$('{0:X2}' -f $tid) "
    } else {
        $line += "($c)=---- "
    }
}
Write-Output $line

# Row 22
Write-Output "=== Row 22 (tileY=19, worldY=304) cols 224-232 ==="
$line = ""
foreach ($c in 224..232) {
    $gid = $d[22 * $w + $c]
    $tid = if ($gid -gt 0) { $gid - 1 } else { -1 }
    if ($gid -gt 0) {
        $line += "($c)=0x$('{0:X2}' -f $tid) "
    } else {
        $line += "($c)=---- "
    }
}
Write-Output $line
