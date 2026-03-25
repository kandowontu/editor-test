$pftest = "c:\Editor Test\pf-test"
$lvlDir = "c:\Editor Test\famidash\LEVELS\BIG backup\lvlset_BIG"
$results = "c:\Editor Test\test_results.txt"

Set-Location $pftest

$tests = @(
    @{ Name = "clutterfunk"; File = "clutterfunk.tmx" },
    @{ Name = "firetemple"; File = "firetemple.tmx" },
    @{ Name = "fingerdash"; File = "fingerdash.tmx" },
    @{ Name = "electromanadventures"; File = "electromanadventures.tmx" }
)

"=== PF-TEST REGRESSION RESULTS ===" | Out-File $results
"Started: $(Get-Date)" | Out-File $results -Append

foreach ($t in $tests) {
    $tmx = Join-Path $lvlDir $t.File
    "---" | Out-File $results -Append
    "TEST: $($t.Name)" | Out-File $results -Append
    
    $outFile = "c:\Editor Test\out_$($t.Name).txt"
    $errFile = "c:\Editor Test\err_$($t.Name).txt"
    
    $p = Start-Process -FilePath "dotnet" -ArgumentList "run -c Release --no-build -- `"$tmx`"" -WorkingDirectory $pftest -RedirectStandardOutput $outFile -RedirectStandardError $errFile -PassThru -NoNewWindow
    $p.WaitForExit()
    
    "ExitCode: $($p.ExitCode)" | Out-File $results -Append
    
    $resultLine = Get-Content $outFile | Select-String "^Result:" | Select-Object -Last 1
    $messageLine = Get-Content $outFile | Select-String "^Message:" | Select-Object -Last 1
    
    if ($resultLine) { "$resultLine" | Out-File $results -Append } else { "Result: (not found in output)" | Out-File $results -Append }
    if ($messageLine) { "$messageLine" | Out-File $results -Append } else { "Message: (not found in output)" | Out-File $results -Append }
}

"---" | Out-File $results -Append
"Finished: $(Get-Date)" | Out-File $results -Append
"=== DONE ===" | Out-File $results -Append
