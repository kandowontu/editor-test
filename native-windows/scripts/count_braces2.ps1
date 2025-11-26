$path = "c:\Editor Test\native-windows\MainWindow.xaml.cs"
$lines = Get-Content -LiteralPath $path
$open = 0
$close = 0
for ($i=0; $i -lt $lines.Length; $i++) {
    $line = $lines[$i]
    $open += ($line.ToCharArray() | Where-Object { $_ -eq '{' }).Count
    $close += ($line.ToCharArray() | Where-Object { $_ -eq '}' }).Count
}
Write-Output "Open braces: $open"
Write-Output "Close braces: $close"
Write-Output "Difference (open - close): $($open - $close)"
