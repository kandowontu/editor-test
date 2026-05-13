$pf  = Import-Csv ($env:TEMP+'\famidash_pf_trace.csv')
$nes = Get-Content ($env:TEMP+'\famidash_mesen_trace.csv') | Select-Object -Skip 1 | ConvertFrom-Csv
# Index NES by sim_cursor
$nesBySim = @{}
foreach ($r in $nes) { if ($r.sim_cursor) { $nesBySim[[int]$r.sim_cursor] = $r } }
Write-Host ("PF rows: {0}  NES sim rows: {1}" -f $pf.Count, $nesBySim.Count)
$diffs = 0
$firstDiff = $null
foreach ($p in $pf) {
    $f = [int]$p.frame
    $sim = $f + 1
    if (-not $nesBySim.ContainsKey($sim)) { continue }
    $n = $nesBySim[$sim]
    $pfMini  = [int]$p.mini
    $nesMini = ([int]$n.table_idx -band 4) -shr 2
    if ($pfMini -ne $nesMini) {
        $diffs++
        if (-not $firstDiff) {
            $firstDiff = [pscustomobject]@{
                f=$f; sim=$sim
                PFx=$p.X_px; NESx=$n.px; PFy=$p.Y_px; NESy=$n.py
                PFmini=$pfMini; NESmini=$nesMini
                NES_table_idx=$n.table_idx; NES_gamemode=$n.gamemode
            }
        }
    }
}
Write-Host ("mini diffs: {0}" -f $diffs)
if ($firstDiff) { Write-Host "FIRST DIFF:"; $firstDiff | Format-List }
# Also dump last 8 PF frames with NES side by side
Write-Host "`nLAST 10 PF frames vs NES:"
$pf | Select-Object -Last 10 | ForEach-Object {
    $f = [int]$_.frame
    $sim = $f + 1
    if ($nesBySim.ContainsKey($sim)) {
        $n = $nesBySim[$sim]
        $nm = ([int]$n.table_idx -band 4) -shr 2
        "f={0,5} sim={1,5} PF=({2},{3}) m={4} | NES=({5},{6}) m={7} ti={8} gm={9}" -f `
            $f,$sim,$_.X_px,$_.Y_px,$_.mini,$n.px,$n.py,$nm,$n.table_idx,$n.gamemode
    } else {
        "f={0,5} sim={1,5} (no NES sim row)" -f $f,$sim
    }
}
