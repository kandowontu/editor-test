$nesLines = Get-Content "C:\Editor Test\metatile_collision_table.txt"

$csLines = Get-Content "C:\Editor Test\native-windows\MetatileCollision.cs"
$pfAll = @()
$inBlock = $false
foreach ($line in $csLines) {
    if ($line -match 'mappingText\s*=\s*@"') { $inBlock = $true; continue }
    if ($inBlock) {
        if ($line.Trim() -match '^\s*";') { break }
        $trimmed = $line.Trim()
        if ($trimmed -ne '') {
            if ($trimmed -match '(COL_[A-Z0-9_]+)') {
                $pfAll += $Matches[1]
            } else {
                # No COL_ match - enum default is COL_NONE (value 0)
                $pfAll += 'COL_NONE'
            }
        }
    }
}

Write-Host "NES entries: $($nesLines.Count)"
Write-Host "PF entries: $($pfAll.Count)"
Write-Host ""

$maxIdx = [Math]::Min($nesLines.Count, $pfAll.Count)
$diffCount = 0

for ($i = 0; $i -lt $maxIdx; $i++) {
    $nes = $nesLines[$i].Trim()
    $pf = $pfAll[$i]

    if ($nes -ne $pf) {
        # Check equivalences
        $equiv = $false
        # COL_TOP_SPIKES <-> COL_TOP_CENTER_SPIKE
        if (($nes -eq 'COL_TOP_SPIKES' -and $pf -eq 'COL_TOP_CENTER_SPIKE') -or
            ($nes -eq 'COL_TOP_CENTER_SPIKE' -and $pf -eq 'COL_TOP_SPIKES')) {
            $equiv = $true
        }

        if (-not $equiv) {
            $hex = '{0:X2}' -f $i
            Write-Host "Tile $i (0x$hex): NES=$nes  PF=$pf"
            $diffCount++
        }
    }
}

if ($nesLines.Count -gt $pfAll.Count) {
    for ($i = $pfAll.Count; $i -lt $nesLines.Count; $i++) {
        $hex = '{0:X2}' -f $i
        Write-Host "Tile $i (0x$hex): NES=$($nesLines[$i].Trim())  PF=(MISSING)"
        $diffCount++
    }
}
if ($pfAll.Count -gt $nesLines.Count) {
    for ($i = $nesLines.Count; $i -lt $pfAll.Count; $i++) {
        $hex = '{0:X2}' -f $i
        Write-Host "Tile $i (0x$hex): NES=(MISSING)  PF=$($pfAll[$i])"
        $diffCount++
    }
}

Write-Host ""
Write-Host "Total differences: $diffCount"
