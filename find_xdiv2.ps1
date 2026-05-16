$mt = Import-Csv "$env:TEMP\famidash_mesen_trace.csv" -Header 'rom_frame','sim_cursor','px','py','a_next','a_cur','raw_x','raw_y','scrollx','scrolly','vel_y','table_idx','gravity_mod','dashing','gamemode','scroll_y_subpx','framerate','tgt_scroll_y','cp_y','scroll_y_raw','cube_data','death_pc','death_ctx','collmap_r8','mini','cp_gravity','nocamlock','nocamlockforced','min_scroll_y','dual' | Select-Object -Skip 2
$pf = Import-Csv "$env:TEMP\famidash_pf_trace.csv"
# Take FIRST entry per sim_cursor (when cursor freshly advanced)
$nesByCur = @{}
foreach($r in $mt){
  if($r.sim_cursor -match '^\d+$'){
    $sc = [int]$r.sim_cursor
    if(-not $nesByCur.ContainsKey($sc)){ $nesByCur[$sc] = $r }
  }
}
$pfByF = @{}
foreach($r in $pf){ if($r.frame -match '^\d+$'){ $pfByF[[int]$r.frame] = $r } }
$rows = @()
$maxSc = ($nesByCur.Keys | Measure-Object -Maximum).Maximum
$alignments = @(0, -1, -2)
foreach($shift in $alignments){
  $matches = 0; $total = 0
  for($sc=1; $sc -le 2200; $sc++){
    $n = $nesByCur[$sc]; $p = $pfByF[$sc + $shift]
    if($n -and $p){
      $total++
      $nx = [int]$n.px - 8
      $px = [Convert]::ToInt32($p.X_fixed.Substring(2),16)
      $pxInt = [int]([Math]::Floor($px/256.0))
      if($nx -eq $pxInt){ $matches++ }
    }
  }
  $pct = if($total -gt 0){[int](100*$matches/$total)}else{0}
  Write-Host ("Alignment shift={0}: {1}/{2} match ({3}%)" -f $shift, $matches, $total, $pct)
}
# Best alignment - use shift=0 (PF[sc] vs NES[sc])
Write-Host "`n--- Using shift=0 (PF[sc] vs NES[sc] FIRST-row) ---"
for($sc=1; $sc -le 2200; $sc++){
  $n = $nesByCur[$sc]; $p = $pfByF[$sc]
  if($n -and $p){
    $nx_px = [int]$n.px - 8
    $nx_raw = [int]$n.raw_x
    $nsc = [int]$n.scrollx
    $px_fx = [Convert]::ToInt32($p.X_fixed.Substring(2),16)
    $px_pxi = [int]([Math]::Floor($px_fx/256.0))
    $dx_px = $nx_px - $px_pxi
    $rows += [PSCustomObject]@{sc=$sc; nx=$nx_px; nraw=$nx_raw; nsc=$nsc; px=$px_pxi; pfx=$px_fx; dx=$dx_px; rom=$n.rom_frame; ng=[int]$n.gamemode; pg=[int]$p.mode}
  }
}
$prev = 0; $events = 0
$rows | ForEach-Object {
  if($_.dx -ne $prev){
    $events++
    if($events -le 25){
      Write-Host ("CHG sc={0} rom={1} dx={2}->{3} nx={4} px={5} pfx_hex=0x{6:X}" -f $_.sc, $_.rom, $prev, $_.dx, $_.nx, $_.px, $_.pfx)
    }
  }
  $prev = $_.dx
}
Write-Host "Total events: $events"
Write-Host "`n-- around sc=1832 --"
$rows | ?{ $_.sc -ge 1825 -and $_.sc -le 1850 } | ft -AutoSize
Write-Host "`n-- around sc=2110 --"
$rows | ?{ $_.sc -ge 2100 -and $_.sc -le 2115 } | ft -AutoSize
