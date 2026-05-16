$mt = Import-Csv "$env:TEMP\famidash_mesen_trace.csv" -Header 'rom_frame','sim_cursor','px','py','a_next','a_cur','raw_x','raw_y','scrollx','scrolly','vel_y','table_idx','gravity_mod','dashing','gamemode','scroll_y_subpx','framerate','tgt_scroll_y','cp_y','scroll_y_raw','cube_data','death_pc','death_ctx','collmap_r8','mini','cp_gravity','nocamlock','nocamlockforced','min_scroll_y','dual' | Select-Object -Skip 2
$pf = Import-Csv "$env:TEMP\famidash_pf_trace.csv"
# FIRST entry per sim_cursor
$nesByCur = @{}
foreach($r in $mt){ if($r.sim_cursor -match '^\d+$'){ $sc=[int]$r.sim_cursor; if(-not $nesByCur.ContainsKey($sc)){$nesByCur[$sc]=$r} } }
$pfByF = @{}
foreach($r in $pf){ if($r.frame -match '^\d+$'){ $pfByF[[int]$r.frame] = $r } }
# Use shift=-1: NES[sc] aligns with PF[sc-1]
$rows = @()
for($sc=2; $sc -le 2200; $sc++){
  $n = $nesByCur[$sc]; $p = $pfByF[$sc-1]
  if($n -and $p){
    $nx = [int]$n.px - 8
    $px = [Convert]::ToInt32($p.X_fixed.Substring(2),16)
    $pxi = [int]([Math]::Floor($px/256.0))
    $rows += [PSCustomObject]@{sc=$sc; nx=$nx; px=$pxi; dx=($nx - $pxi); ng=[int]$n.gamemode; pg=[int]$p.mode; ms=[int]$n.mini; rom=$n.rom_frame}
  }
}
# Transitions
$prev = 0; $events = 0
$rows | ForEach-Object {
  if($_.dx -ne $prev){
    $events++
    if($events -le 30){
      Write-Host ("CHG sc={0} rom={1} dx={2}->{3} nx={4} px={5} ng={6} ms={7}" -f $_.sc, $_.rom, $prev, $_.dx, $_.nx, $_.px, $_.ng, $_.ms)
    }
  }
  $prev = $_.dx
}
Write-Host "Total events: $events"
# Distribution of dx values
$dist = $rows | Group-Object dx | Sort-Object Name | Select-Object Name, Count
Write-Host "`nDistribution of dx:"
$dist | Format-Table -AutoSize
Write-Host "`n-- around sc=2110 --"
$rows | ?{ $_.sc -ge 2105 -and $_.sc -le 2120 } | Format-Table -AutoSize
