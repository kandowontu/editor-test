$mt = Import-Csv "$env:TEMP\famidash_mesen_trace.csv" -Header 'rom_frame','sim_cursor','px','py','a_next','a_cur','raw_x','raw_y','scrollx','scrolly','vel_y','table_idx','gravity_mod','dashing','gamemode','scroll_y_subpx','framerate','tgt_scroll_y','cp_y','scroll_y_raw','cube_data','death_pc','death_ctx','collmap_r8','mini','cp_gravity','nocamlock','nocamlockforced','min_scroll_y','dual' | Select-Object -Skip 2
$pf = Import-Csv "$env:TEMP\famidash_pf_trace.csv"
$nesByCur = @{}
foreach($r in $mt){ if($r.sim_cursor -match '^\d+$'){ $nesByCur[[int]$r.sim_cursor] = $r } }
$pfByF = @{}
foreach($r in $pf){ if($r.frame -match '^\d+$'){ $pfByF[[int]$r.frame] = $r } }
$rows = @()
for($sc=1; $sc -le 2200; $sc++){
  $n = $nesByCur[$sc]; $p = $pfByF[$sc-1]
  if($n -and $p){
    $nx_px = [int]$n.px - 8
    $nx_raw = [int]$n.raw_x
    $px_fx = [Convert]::ToInt32($p.X_fixed.Substring(2),16)
    $px_pxi = [int]([Math]::Floor($px_fx/256.0))
    $dx_px = $nx_px - $px_pxi
    $rows += [PSCustomObject]@{sc=$sc; nx=$nx_px; nraw=$nx_raw; nsc=[int]$n.scrollx; px=$px_pxi; pfx=$px_fx; dx=$dx_px; ng=[int]$n.gamemode; pg=[int]$p.mode; n_ax=$n.a_cur}
  }
}
$prev = 0; $events = 0
$rows | ForEach-Object {
  if($_.dx -ne $prev){
    $events++
    if($events -le 20){
      Write-Host ("CHG sc={0} dx={1}->{2} nx={3} px={4} sub=0x{5:X2} ng={6} pg={7}" -f $_.sc, $prev, $_.dx, $_.nx, $_.px, $_.sub, $_.ng, $_.pg)
    }
  }
  $prev = $_.dx
}
Write-Host "Total dx-change events: $events"
Write-Host "`n-- around sc=1832 --"
$rows | ?{ $_.sc -ge 1825 -and $_.sc -le 1850 } | ft -AutoSize
Write-Host "`n-- around sc=1969 --"
$rows | ?{ $_.sc -ge 1960 -and $_.sc -le 1985 } | ft -AutoSize
Write-Host "`n-- around sc=2100 --"
$rows | ?{ $_.sc -ge 2100 -and $_.sc -le 2115 } | ft -AutoSize
