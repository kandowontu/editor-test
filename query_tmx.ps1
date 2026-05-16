$content = Get-Content "c:\Editor Test\famidash\LEVELS\LEVEL DATA\lvlset_HUGE\eighto.tmx" -Raw
$xml = [xml]$content
$targetGids = @(277, 278, 279, 289, 290, 366)
$results = @()
foreach ($layer in $xml.map.layer) {
    $width = [int]$layer.width
    $dataString = $layer.data."#text"
    if (-not $dataString) { continue }
    $data = $dataString.Split(",`r`n".ToCharArray(), [System.StringSplitOptions]::RemoveEmptyEntries)
    for ($i = 0; $i -lt $data.Length; $i++) {
        $gid = [int]$data[$i].Trim()
        if ($targetGids -contains $gid) {
            $row = [Math]::Floor($i / $width)
            $col = $i % $width
            if ($col -le 700) {
                $results += [PSCustomObject]@{
                    Layer = $layer.name
                    GID = $gid
                    TileX = $col
                    TileY = $row
                    PixelX = $col * 8
                    PixelY = $row * 8
                    SpriteID = "0x{0:X}" -f ($gid - 257)
                }
            }
        }
    }
}
$results | Format-Table -AutoSize
