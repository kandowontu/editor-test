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

# Prefer parsing names directly from an available .fms file so indices align with the project's internal ordering.
$albumNames = @()
$fmsCandidates = @(
    (Join-Path $repoRoot 'the album.fms'),
    (Join-Path $repoRoot 'native-windows\the album.fms')
)
$fmsPath = $null
foreach ($c in $fmsCandidates) { if (Test-Path $c) { $fmsPath = $c; break } }
if ($fmsPath) {
    Write-Host "Parsing song names from .fms: $fmsPath"
    try {
        $bytes = [System.IO.File]::ReadAllBytes($fmsPath)
        $text = [System.Text.Encoding]::UTF8.GetString($bytes)
        $matches = [regex]::Matches($text, '"name"\s*:\s*"([^"\\]+)"', 'IgnoreCase')
        foreach ($m in $matches) { $v = $m.Groups[1].Value.Trim(); if ($v -and -not ($albumNames -contains $v)) { $albumNames += $v } }
    } catch { Write-Warning "Failed to parse .fms as text; falling back to album.txt" }
}

# If we didn't get names from .fms, fall back to parsing album.txt
if ($albumNames.Count -eq 0) {
    $lines = Get-Content $albumTxt -ErrorAction Stop
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
}

if (Test-Path $probeFile) { $probe = (Get-Content $probeFile -Raw | ConvertFrom-Json) } else { $probe = @() }

# If we don't have probe data, attempt to probe using an available FamiStudio CLI so we map to playable indices.
if (($probe -eq $null -or $probe.Count -eq 0) -and $albumNames.Count -gt 0) {
    $candidates = @(
        (Join-Path $repoRoot 'native-windows\libs\famistudio\FamiStudio.exe'),
        'C:\Program Files\FamiStudio\FamiStudio.exe',
        'C:\Program Files (x86)\FamiStudio\FamiStudio.exe',
        (Join-Path $repoRoot 'native-windows\FamiStudio.exe')
    )
    $exe = $null
    foreach ($c in $candidates) { if (Test-Path $c) { $exe = $c; break } }
    if ($exe) {
        Write-Host "Probing playable song indices with: $exe"
        $playable = @()
        $max = [Math]::Min(1024, [Math]::Max(256, $albumNames.Count * 2))
        $consecutive = 0
        for ($i = 0; $i -lt $max; $i++) {
            $tmp = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), "fms_probe_$i.wav")
            $args = "`"$exe`" wav-export `"$tmp`" -export-songs:$i -wav-export-rate:48000"
            try {
                $psi = New-Object System.Diagnostics.ProcessStartInfo
                $psi.FileName = $exe
                $psi.Arguments = "wav-export `"$tmp`" -export-songs:$i -wav-export-rate:48000"
                $psi.CreateNoWindow = $true
                $psi.UseShellExecute = $false
                $p = [System.Diagnostics.Process]::Start($psi)
                if ($p -ne $null) { $p.WaitForExit(4000) }
                if (Test-Path $tmp) {
                    $fi = Get-Item $tmp -ErrorAction SilentlyContinue
                    if ($fi -and $fi.Length -gt 1024) {
                        $playable += $i
                        $consecutive = 0
                    } else { $consecutive++ }
                    try { Remove-Item $tmp -Force } catch { }
                } else { $consecutive++ }
            } catch { $consecutive++ }
            if ($consecutive -ge 12) { break }
        }
        if ($playable.Count -gt 0) { $probe = $playable }
    }
}

$map = @()
# If we have probe data and it's useful, prefer it; otherwise generate a full mapping from albumNames.
# Create a full mapping from albumNames in order so published builds have a complete name->index table.
if ($albumNames.Count -gt 0) {
    for ($i=0; $i -lt $albumNames.Count; $i++) { $map += @{ index = $i; name = $albumNames[$i] } }
}
else {
    # Fallback to parsed names if nothing else produced albumNames
    for ($i=0; $i -lt $parsed.Count; $i++) { $map += @{ index = $i; name = $parsed[$i] } }
}

$map | ConvertTo-Json -Depth 5 | Set-Content $outFile -Encoding UTF8
if (!(Test-Path (Split-Path $outFile2 -Parent))) { New-Item -ItemType Directory -Path (Split-Path $outFile2 -Parent) -Force | Out-Null }
Copy-Item -Path $outFile -Destination $outFile2 -Force
Write-Output "Wrote mapping: $outFile and copied to build output." 
$map | Format-Table -AutoSize
