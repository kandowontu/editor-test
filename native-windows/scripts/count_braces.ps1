$path = "MainWindow.xaml.cs"
$open = 0
$close = 0
$lineNo = 0
Get-Content $path | ForEach-Object {
    $lineNo++
    $open += ([regex]::Matches($_,'\{')).Count
    $close += ([regex]::Matches($_,'\}')).Count
    if ($close -gt $open) {
        Write-Host "Closing exceeds opening at line $lineNo"
        exit 0
    }
}
Write-Host "Total opens: $open, closes: $close"
Write-Host "Done"
