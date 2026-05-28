$dbg = "c:\Editor Test\famidash\BUILD\huge\famidash.dbg"
$out = "$env:TEMP\sym_find.txt"
Remove-Item $out -ErrorAction SilentlyContinue
# Sym entries look like: sym id=X,name="...",addrsize=absolute,size=N,scope=Y,def=Z,val=0xBD2A,seg=N,type=lab
$hits = @()
Get-Content $dbg -ReadCount 1000 | ForEach-Object {
    foreach ($line in $_) {
        if ($line -like '*val=0xBD2*' -or $line -like '*val=0xBD3[0-3]*') {
            $hits += $line
        }
        if ($line -match 'val=0xBD([23])([0-9A-Fa-f])') {
            $hits += $line
        }
    }
}
$hits | Select-Object -First 80 | Out-File $out -Encoding utf8
"hits=$($hits.Count) saved to $out"
