$lines=Get-Content 'C:\Editor Test\the album.txt'
$count=0
for($i=0;$i -lt $lines.Count;$i++){
    $t=$lines[$i].TrimStart()
    if($t -match '^(?i:Song)($|\s|\t)') {$count++}
}
Write-Host "SongBlocks= $count"