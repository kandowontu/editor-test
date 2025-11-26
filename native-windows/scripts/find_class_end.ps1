$path = "c:\Editor Test\native-windows\MainWindow.xaml.cs"
$lines = Get-Content -LiteralPath $path
$match = ($lines | Select-String -Pattern 'public partial class MainWindow' | Select-Object -First 1)
if ($match -eq $null) { Write-Output "Class not found"; exit 1 }
$start = $match.LineNumber
Write-Output "StartLine=$start"

# Find the first opening brace after the class declaration and start counting from there
$openLine = $null
for ($i = $start - 1; $i -lt $lines.Length; $i++) {
    if ($lines[$i] -match '{') { $openLine = $i; break }
}
if ($openLine -eq $null) { Write-Output "Opening brace not found"; exit 1 }
$count = 1
for ($i = $openLine + 1; $i -lt $lines.Length; $i++) {
    $line = $lines[$i]
    $chars = $line.ToCharArray()
    foreach ($c in $chars) {
        if ($c -eq '{') { $count++ } elseif ($c -eq '}') { $count-- }
    }
    if ($count -eq 0) { Write-Output "Class closes at line: $($i+1)"; break }
}
if ($count -ne 0) { Write-Output "Class did not close, final count=$count" }
if ($count -ne 0) { Write-Output "Class did not close, final count=$count" }
