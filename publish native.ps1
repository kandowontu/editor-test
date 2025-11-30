$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition

# Regenerate parsed album JSON and index map so published build has up-to-date song->index mapping
$parseScript = Join-Path $scriptDir 'scripts\parse-famistudio-album.ps1'
$mapScript = Join-Path $scriptDir 'native-windows\generate_song_index_map.ps1'
$album = Join-Path $scriptDir 'the album.txt'
if ((Test-Path $parseScript -PathType Leaf) -and (Test-Path $album -PathType Leaf)) {
	Write-Host "Parsing album.txt into JSON..."
	& $parseScript -InputPath $album -OutputPath (Join-Path $scriptDir 'native-windows\fami-album-parsed.json')
}
if (Test-Path $mapScript -PathType Leaf) {
	Write-Host "Generating song index map..."
	& $mapScript
}

# Publish
dotnet publish "c:\Editor Test\native-windows\FamidashEditor.csproj" -c Release -r win-x64 -o "c:\Editor Test\native-windows\published" --self-contained true -p:PublishSingleFile=false
# Ensure mapping and album files are embedded in the published output so indexes align at runtime
$mapFile = Join-Path $scriptDir 'native-windows\fami-song-index-map.json'
$parsedFile = Join-Path $scriptDir 'native-windows\fami-album-parsed.json'
$albumFms = Join-Path $scriptDir 'the album.fms'
$albumTxt = Join-Path $scriptDir 'the album.txt'
$dest = Join-Path $scriptDir 'native-windows\published'

if (Test-Path $mapFile) { Copy-Item -Path $mapFile -Destination (Join-Path $dest (Split-Path $mapFile -Leaf)) -Force }
if (Test-Path $parsedFile) { Copy-Item -Path $parsedFile -Destination (Join-Path $dest (Split-Path $parsedFile -Leaf)) -Force }

# Prefer embedding .fms if available; otherwise include the album.txt
if (Test-Path $albumFms) {
	Copy-Item -Path $albumFms -Destination (Join-Path $dest (Split-Path $albumFms -Leaf)) -Force
	Write-Host "Included .fms in published output: $albumFms"
}
elseif (Test-Path $albumTxt) {
	Copy-Item -Path $albumTxt -Destination (Join-Path $dest (Split-Path $albumTxt -Leaf)) -Force
	Write-Host "Included album.txt in published output: $albumTxt"
}

Write-Host "Publish complete. Published files are in: $dest"