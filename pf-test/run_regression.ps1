cd "c:\Editor Test\pf-test"
$results = @()
$levels = @("clutterfunk","firetemple","fingerdash","electroman","blastprocessing")
foreach ($l in $levels) {
    $tmx = "c:\Editor Test\famidash\LEVELS\BIG backup\lvlset_BIG\$l.tmx"
    $out = dotnet run -c Release --no-build -- "$tmx" 2>$null
    $res = ($out | Select-String "Result:").Line
    $msg = ($out | Select-String "Message:").Line
    $results += "$l : $res | $msg"
    $results[-1] | Write-Host
}
$results | Out-File "c:\Editor Test\pf-test\regression_results.txt" -Encoding utf8
Write-Host "`nDone! Results in regression_results.txt"
