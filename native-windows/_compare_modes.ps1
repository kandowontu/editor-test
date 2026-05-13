$nes = Get-Content C:\Users\kando\AppData\Local\Temp\famidash_mesen_trace.csv |
    Select-Object -Skip 2 |
    ConvertFrom-Csv -Header @('rom_frame','sim_cursor','px','py','a_next','a_cur','raw_x','raw_y','scrollx','scrolly','vel_y','table_idx','gravity_mod','dashing','gamemode','scroll_y_subpx','framerate','tgt_scroll_y','cp_y','scroll_y_raw','cube_data','death_pc','death_ctx','collmap_r8')
$pf = Import-Csv C:\Users\kando\AppData\Local\Temp\famidash_pf_trace.csv
$nesH = @{}
foreach ($r in $nes) { if ($r.sim_cursor -match '^\d+$') { $nesH[[int]$r.sim_cursor] = $r } }
$prevDiff = 0
foreach ($p in $pf) {
    $f = [int]$p.frame
    $r = $nesH[$f + 1]
    if (-not $r) { continue }
    $pfCam = [int]$p.CamY_px
    $nesScroll = [int]$r.scrolly
    $diff = ($nesScroll - 48) - $pfCam
    if ($diff -ne $prevDiff) {
        "f=$f pfCam=$pfCam nesScroll=$nesScroll subpx=$($r.scroll_y_subpx) diff=$diff (prev=$prevDiff) PFmode=$($p.mode) NESmode=$($r.gamemode) PFvelY=0x$($p.VelY_fixed) NESvelY=$($r.vel_y) PFy=$($p.Y_px) NESpy=$($r.py)"
        $prevDiff = $diff
    }
}
