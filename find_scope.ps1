$dbg = "c:\Editor Test\famidash\BUILD\huge\famidash.dbg"
$out = "$env:TEMP\scope_find.txt"
Remove-Item $out -ErrorAction SilentlyContinue
$hits = @()
Get-Content $dbg -ReadCount 1000 | ForEach-Object {
    foreach ($line in $_) {
        if ($line -like 'scope*id=199,*' -or $line -like 'scope*id=129,*' -or $line -like 'scope*id=188,*' -or $line -like 'scope*id=66,*' -or $line -like 'scope*id=231,*') {
            $hits += $line
        }
    }
}
$hits | Out-File $out -Encoding utf8
"hits=$($hits.Count)"
