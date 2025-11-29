<#
PowerShell script to parse a FamiStudio "text export" (the album.txt)
and output a JSON file containing parsed song names to be consumed
by the editor at startup so it doesn't need to parse the big text file.

Usage:
  .\parse-famistudio-album.ps1 -Input "C:\Editor Test\the album.txt" -Output "native-windows\fami-album-parsed.json"
#>
param(
    [string]$InputPath = "the album.txt",
    [string]$OutputPath = "native-windows\fami-album-parsed.json"
)

if (!(Test-Path $InputPath)) {
    Write-Error "Input file not found: $InputPath"
    exit 2
}

$lines = Get-Content $InputPath -ErrorAction Stop

$nameRegex = 'Name\s*=\s*"([^"]+)"'

# We will parse by locating top-level 'Song' tokens and extracting the nearest Name attribute
# within that Song block. This preserves song order and avoids collecting instrument/sample names.
$result = New-Object System.Collections.Generic.List[string]

for ($i = 0; $i -lt $lines.Count; $i++) {
    $l = $lines[$i]
    $trim = $l.TrimStart()
    if ($trim.StartsWith('Song', 'InvariantCultureIgnoreCase') -and ($trim.Length -eq 4 -or [char]::IsWhiteSpace($trim[4]) -or $trim[4] -eq "`t")) {
        # scan this line and the following lines (bounded) for Name="..." within the Song block
        for ($j = $i; $j -lt [Math]::Min($i + 24, $lines.Count); $j++) {
            $lj = $lines[$j]
            # If we hit a new top-level token (no indentation) and it's not the same Song block, stop scanning
            if ($j -gt $i -and $lj.Length -gt 0 -and -not [char]::IsWhiteSpace($lj[0])) { break }
            $gm = [regex]::Match($lj, $nameRegex, 'IgnoreCase')
            if ($gm.Success) {
                $name = $gm.Groups[1].Value.Trim()
                if ($name -and -not ($result.Contains($name))) { $result.Add($name) }
                break
            }
        }
    }
}

# If we didn't find any Song blocks (older exports), fall back to looking for Song token markers like 'Song:' or 'Song='
if ($result.Count -eq 0) {
    $matches = [regex]::Matches((Get-Content $Input -Raw), 'Song\s*[:=]\s*"?([A-Za-z0-9 _-]{1,120})"?', 'IgnoreCase')
    foreach ($m in $matches) {
        $v = $m.Groups[1].Value.Trim()
        if ($v -and -not ($result.Contains($v))) { $result.Add($v) }
    }
}

# Fallback: if none found, try to extract some tokens that look like songs
if ($result.Count -eq 0) {
    $matches = [regex]::Matches((Get-Content $InputPath -Raw), 'Song\s*[:=]\s*"?([A-Za-z0-9 _-]{1,120})"?', 'IgnoreCase')
    foreach ($m in $matches) {
        $v = $m.Groups[1].Value.Trim()
        if ($v -and -not ($result.Contains($v))) { $result.Add($v) }
    }
}

# Write JSON (use ConvertTo-Json for compatibility)
$outObj = @{ parsedNames = $result }
$dir = Split-Path $OutputPath -Parent
if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
$txt = $outObj | ConvertTo-Json -Depth 5
Set-Content -Path $OutputPath -Value $txt -Encoding UTF8

Write-Host "Parsed $($result.Count) names and wrote to: $OutputPath"