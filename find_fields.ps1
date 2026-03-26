$content = Get-Content "c:\Editor Test\native-windows\PathfinderEngine.cs"
$ranges = @(
    @(7979,8097),
    @(8144,8218),
    @(8219,8351),
    @(9478,9657),
    @(9658,9902),
    @(9903,9984),
    @(9985,10156),
    @(10157,10578),
    @(10579,10829),
    @(10830,10905),
    @(10906,10946),
    @(10947,11118),
    @(11119,11144),
    @(11145,11242),
    @(11243,11283),
    @(11284,11400)
)

foreach ($r in $ranges) {
    $s = $r[0] - 1
    $e = $r[1] - 1
    for ($i = $s; $i -le $e; $i++) {
        $line = $content[$i]
        # Match _fieldName followed by assignment operator
        if ($line -match '_\w+\s*(=|\+=|-=|\+\+|--)' `
            -and $line -notmatch '^\s*//' `
            -and $line -notmatch '\bs\.' `
            -and $line -notmatch '\bstate\.' `
            -and $line -notmatch '^\s*(private|public|internal|protected)\s' `
            -and $line -notmatch '^\s*bool\s' `
            -and $line -notmatch '^\s*int\s' `
            -and $line -notmatch '^\s*var\s' `
            -and $line -notmatch '^\s*string\s' `
            -and $line -notmatch '^\s*byte\s') {
            $ln = $i + 1
            Write-Output "${ln}: $($line.TrimStart())"
        }
    }
}
