$fms='C:\Editor Test\the album.fms'
if (-not (Test-Path $fms)) { Write-Host "No fms"; exit }
$bytes=[System.IO.File]::ReadAllBytes($fms)
$text=[System.Text.Encoding]::UTF8.GetString($bytes)
$matches=[regex]::Matches($text,'"name"\s*:\s*"([^"\\]+)"','IgnoreCase')
Write-Host "Matches: $($matches.Count)"
for ($i=0;$i -lt [Math]::Min(10,$matches.Count); $i++) { Write-Host ($i.ToString() + ': ' + $matches[$i].Groups[1].Value) }
