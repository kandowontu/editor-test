$raw = Get-Content ($env:TEMP+'\famidash_mesen_trace.csv')
$header = $raw[1]  # row 0 is "nes_y_offset,48", row 1 is real header
# Split into segments by respawn separator
$segments = @()
$cur = New-Object System.Collections.Generic.List[string]
for ($i = 2; $i -lt $raw.Count; $i++) {
    $line = $raw[$i]
    if ($line -match '^# --- respawn ---') {
        if ($cur.Count -gt 0) { $segments += ,@($cur.ToArray()); $cur.Clear() }
    } elseif ($line -match '^\d') {
        $cur.Add($line)
    }
}
if ($cur.Count -gt 0) { $segments += ,@($cur.ToArray()) }
Write-Host "Segments: $($segments.Count)"
$idx = 0
$best = -1
$bestIdx = -1
foreach ($s in $segments) {
    $maxSim = ($s | ForEach-Object { ($_ -split ',')[1] -as [int] } | Measure-Object -Maximum).Maximum
    Write-Host ("  seg[{0}] rows={1} maxSim={2}" -f $idx,$s.Count,$maxSim)
    if ($maxSim -gt $best) { $best = $maxSim; $bestIdx = $idx }
    $idx++
}
Write-Host "Longest segment idx=$bestIdx maxSim=$best"
$nesRaw = $segments[$bestIdx]
$nes = $nesRaw | ForEach-Object { ($header + "`n" + $_) | ConvertFrom-Csv } | ForEach-Object { $_ }
# Simpler:
$nes = ($header,$nesRaw | ForEach-Object { $_ }) -join "`n" | ConvertFrom-Csv
Write-Host "Parsed rows: $($nes.Count)"

# Index by sim
$nesBySim = @{}
foreach ($r in $nes) { $nesBySim[[int]$r.sim_cursor] = $r }

Write-Host "`nNES gravity_mod transitions in longest segment:"
$prev = -2
foreach ($r in $nes | Sort-Object { [int]$_.sim_cursor }) {
    $g = [int]$r.gravity_mod
    if ($g -ne $prev) { $sc=$r.sim_cursor; $rpx=$r.px; $rpy=$r.py; $rti=$r.table_idx; $rgm=$r.gamemode; Write-Host "  sim=$sc px=$rpx py=$rpy grav=$g table_idx=$rti gm=$rgm"; $prev = $g }
}

Write-Host "`nNES last 10 rows of longest segment:"
$nes | Sort-Object { [int]$_.sim_cursor } | Select-Object -Last 10 | Format-Table sim_cursor,px,py,vel_y,gravity_mod,table_idx,gamemode,death_ctx -AutoSize

# Compare PF gravFlipped vs NES gravity_mod
$pf = Import-Csv ($env:TEMP+'\famidash_pf_trace.csv')
Write-Host "`nGrav divergences (PF gravFlipped != NES gravity_mod) -- first 10 transitions of diff:"
$diffs = 0; $prevDiff = -1
foreach ($p in $pf) {
    $f = [int]$p.frame
    $sim = $f + 1
    if (-not $nesBySim.ContainsKey($sim)) { continue }
    $n = $nesBySim[$sim]
    $pfG = [int]$p.gravFlipped
    $nG  = [int]$n.gravity_mod
    $cur = if ($pfG -ne $nG) { 1 } else { 0 }
    if ($cur -ne $prevDiff) {
        $px=$p.X_px; $py=$p.Y_px; $pm=$p.mode; $ngm=$n.gamemode; $nti=$n.table_idx
        Write-Host "  f=$f sim=$sim PF=($px,$py) PFgrav=$pfG NESgrav=$nG PFmode=$pm NESmode=$ngm NESti=$nti"
        $prevDiff = $cur
        $diffs++
        if ($diffs -ge 20) { break }
    }
}
