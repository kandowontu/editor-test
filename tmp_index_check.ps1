$p=(Get-Content 'C:\Editor Test\native-windows\fami-album-parsed.json' -Raw | ConvertFrom-Json).parsedNames
Write-Host "ParsedIndex Shiawase=" ($p.IndexOf('Shiawase VIP'))
Write-Host "ParsedIndex Sonic=" ($p.IndexOf('Sonic blaster'))
$p2=(Get-Content 'C:\Editor Test\native-windows\fami-song-index-map.json' -Raw | ConvertFrom-Json)
$mapIndex = @{ }
foreach ($e in $p2) { $mapIndex[$e.name] = $e.index }
Write-Host "MapIndex Shiawase=" ($mapIndex['Shiawase VIP'])
Write-Host "MapIndex Sonic=" ($mapIndex['Sonic blaster'])
