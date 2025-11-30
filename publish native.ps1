$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition

# Regenerate parsed album JSON and index map so published build has up-to-date song->index mapping
$parseScript = Join-Path $scriptDir 'scripts\parse-famistudio-album.ps1'
$mapScript = Join-Path $scriptDir 'native-windows\generate_song_index_map.ps1'
$album = Join-Path $scriptDir 'the album.txt'
if (Test-Path $parseScript -PathType Leaf -and Test-Path $album -PathType Leaf) {
	Write-Host "Parsing album.txt into JSON..."
	& $parseScript -InputPath $album -OutputPath (Join-Path $scriptDir 'native-windows\fami-album-parsed.json')
}
if (Test-Path $mapScript -PathType Leaf) {
	Write-Host "Generating song index map..."
	& $mapScript
}

# Export pre-rendered WAVs (if FamiStudio available and album present)
$exportScript = Join-Path $scriptDir 'native-windows\scripts\export-famistudio-wavs.ps1'
if (Test-Path $exportScript -PathType Leaf -and Test-Path $album -PathType Leaf) {
	Write-Host "Exporting WAVs..."
	& $exportScript -FmsFile $album
}

# Publish
dotnet publish "c:\Editor Test\native-windows\FamidashEditor.csproj" -c Release -r win-x64 -o "c:\Editor Test\native-windows\published" --self-contained true -p:PublishSingleFile=false

# Copy mapping and WAVs into published output
$assets = Join-Path $scriptDir 'native-windows\assets\famistudio-wavs'
$mapFile = Join-Path $scriptDir 'native-windows\fami-song-index-map.json'
$dest = Join-Path $scriptDir 'native-windows\published'

if (Test-Path $mapFile) { Copy-Item -Path $mapFile -Destination (Join-Path $dest (Split-Path $mapFile -Leaf)) -Force }
if (Test-Path $assets) {
	$destWavs = Join-Path $dest 'famistudio-wavs'
	if (Test-Path $destWavs) { Remove-Item -Path $destWavs -Recurse -Force -ErrorAction SilentlyContinue }
	Copy-Item -Path $assets -Destination $destWavs -Recurse -Force
	Write-Host "Included pre-rendered WAVs in published output: $destWavs"
}

Write-Host "Publish complete. Published files are in: $dest"