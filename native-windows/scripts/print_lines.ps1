param($start=1,$end=10)
$start = [int]$start; $end = [int]$end
$lines = Get-Content 'MainWindow.xaml.cs'
for ($i = $start; $i -le $end; $i++) {
    $ln = $lines[$i-1]
    $num = $i.ToString().PadLeft(6)
    Write-Host "$num $ln"
}