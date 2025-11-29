<#
PowerShell script to parse a FamiStudio "text export" (the album.txt)
and output a JSON file containing parsed song names to be consumed
by the editor at startup so it doesn't need to parse the big text file.

Usage:
  .\parse-famistudio-album.ps1 -Input "C:\Editor Test\the album.txt" -Output "native-windows\fami-album-parsed.json"
#>
param(
    [string]$Input = "the album.txt",
    [string]$Output = "native-windows\fami-album-parsed.json"
)

if (!(Test-Path $Input)) {
    Write-Error "Input file not found: $Input"
    exit 2
}

$lines = Get-Content $Input -ErrorAction Stop

$songLineRegex = '^\s*Song\b.*?Name\s*=\s*"([^"]+)"'
$genericNameRegex = 'Name\s*=\s*"([^"]+)"'

$result = New-Object System.Collections.Generic.List[string]

# First pass: direct Song lines
foreach ($line in $lines) {
    $m = [regex]::Match($line, $songLineRegex, 'IgnoreCase')
    if ($m.Success) {
        $name = $m.Groups[1].Value.Trim()
        if ($name -and -not ($result.Contains($name))) { $result.Add($name) }
    }
}

if ($result.Count -eq 0) {
    # Second pass: find 'Song' blocks and look ahead
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $l = $lines[$i]
        if ($l.TrimStart().StartsWith('Song ', 'InvariantCultureIgnoreCase') -or $l.TrimStart().StartsWith('Song`\t', 'InvariantCultureIgnoreCase') -or $l.TrimStart().Equals('Song')) {
            for ($j = $i; $j -lt [Math]::Min($i + 6, $lines.Count); $j++) {
                $gm = [regex]::Match($lines[$j], $genericNameRegex, 'IgnoreCase')
                if ($gm.Success) {
                    $name = $gm.Groups[1].Value.Trim()
                    if ($name -and -not ($result.Contains($name))) { $result.Add($name); break }
                }
            }
        }
    }
}

if ($result.Count -eq 0) {
    # Third pass: gather Name="..." excluding DPCMSample / Instrument / Meta
    foreach ($line in $lines) {
        $trim = $line.TrimStart()
        if ($trim.StartsWith('DPCMSample', 'InvariantCultureIgnoreCase') -or $trim.StartsWith('Instrument', 'InvariantCultureIgnoreCase') -or $trim.StartsWith('Meta', 'InvariantCultureIgnoreCase')) { continue }
        $gm = [regex]::Match($line, $genericNameRegex, 'IgnoreCase')
        if ($gm.Success) {
            $name = $gm.Groups[1].Value.Trim()
            if ($name -and -not ($result.Contains($name))) { $result.Add($name) }
        }
    }
}

# Fallback: if none found, try to extract some tokens that look like songs
if ($result.Count -eq 0) {
    $matches = [regex]::Matches((Get-Content $Input -Raw), 'Song\s*[:=]\s*"?([A-Za-z0-9 _-]{1,60})"?', 'IgnoreCase')
    foreach ($m in $matches) {
        $v = $m.Groups[1].Value.Trim()
        if ($v -and -not ($result.Contains($v))) { $result.Add($v) }
    }
}

# Write JSON (use ConvertTo-Json for compatibility)
$outObj = @{ parsedNames = $result }
$dir = Split-Path $Output -Parent
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
$txt = $outObj | ConvertTo-Json -Depth 5
Set-Content -Path $Output -Value $txt -Encoding UTF8

Write-Host "Parsed $($result.Count) names and wrote to: $Output"