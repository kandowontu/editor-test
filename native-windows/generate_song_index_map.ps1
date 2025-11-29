# Generates fami-song-index-map.json by parsing 'the album.txt' and probe_playable_indices.json
# Determine repository root as the parent directory of this script's folder
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path "$PSScriptRoot\.." -ErrorAction SilentlyContinue
$repoRoot = $repoRoot.Path
$parsedFile = Join-Path $repoRoot 'native-windows\fami-album-parsed.json'
$albumTxt = Join-Path $repoRoot 'the album.txt'
$probeFile = Join-Path $repoRoot 'native-windows\bin\Debug\net8.0-windows\win-x64\probe_playable_indices.json'
$outFile = Join-Path $repoRoot 'native-windows\fami-song-index-map.json'
$outFile2 = Join-Path $repoRoot 'native-windows\bin\Debug\net8.0-windows\win-x64\fami-song-index-map.json'

if (-not (Test-Path $parsedFile)) { Write-Error "Parsed JSON not found: $parsedFile"; exit 2 }
if (-not (Test-Path $albumTxt)) { Write-Error "Album TXT not found: $albumTxt"; exit 2 }

$parsed = (Get-Content $parsedFile -Raw | ConvertFrom-Json).parsedNames
$lines = Get-Content $albumTxt -ErrorAction Stop
$albumNames = @()
for ($i=0; $i -lt $lines.Count; $i++) {
    $l = $lines[$i]
    $t = $l.TrimStart()
    if ($t -match '^(?i:Song)($|\s|\t)') {
        $found = $null
        for ($j=$i; $j -lt [Math]::Min($lines.Count, $i+24); $j++) {
            $m = [regex]::Match($lines[$j], 'Name\s*=\s*"([^"]+)"', 'IgnoreCase')
            if ($m.Success) { $found = $m.Groups[1].Value.Trim(); break }
        }
        if ($found -ne $null) { $albumNames += $found }
    }
}

if (Test-Path $probeFile) { $probe = (Get-Content $probeFile -Raw | ConvertFrom-Json) } else { $probe = @() }

$map = @()
foreach ($idx in $probe) {
    $entry = @{}
    $entry.index = $idx
    if ($idx -lt $albumNames.Count) { $entry.name = $albumNames[$idx] }
    elseif ($idx -lt $parsed.Count) { $entry.name = $parsed[$idx] }
    else { $entry.name = "Song $idx" }
    $map += $entry
}

# If probe is empty but we have albumNames, create mapping for the first N indices
if ($map.Count -eq 0 -and $albumNames.Count -gt 0) {
    for ($i=0; $i -lt [Math]::Min(64, $albumNames.Count); $i++) {
        $map += @{ index = $i; name = $albumNames[$i] }
    }
}

$map | ConvertTo-Json -Depth 5 | Set-Content $outFile -Encoding UTF8
if (!(Test-Path (Split-Path $outFile2 -Parent))) { New-Item -ItemType Directory -Path (Split-Path $outFile2 -Parent) -Force | Out-Null }
Copy-Item -Path $outFile -Destination $outFile2 -Force
Write-Output "Wrote mapping: $outFile and copied to build output." 
$map | Format-Table -AutoSize
