$file = "native-windows\MainWindow.xaml.cs"
$content = Get-Content $file -Raw

# Replace ZoomSlider references
$content = $content -replace 'if \(ZoomSlider != null\)', 'if (ZoomComboBox != null)'
$content = $content -replace 'ZoomSlider\.ValueChanged', 'ZoomComboBox.SelectionChanged'
$content = $content -replace 'ZoomSlider\.Value', 'GetCurrentZoom()'
$content = $content -replace '\(ZoomSlider != null\) \? GetCurrentZoom\(\)', 'GetCurrentZoom()'
$content = $content -replace 'ZoomSlider!=null\?GetCurrentZoom\(\)', 'GetCurrentZoom()'
$content = $content -replace 'ZoomSlider\.Minimum', '0.5'
$content = $content -replace 'ZoomSlider\.Maximum', '4.0'
$content = $content -replace 'ZoomSlider = newScale;', 'SetZoom(newScale);'

Set-Content $file $content -NoNewline

Write-Host "Replacements complete"
