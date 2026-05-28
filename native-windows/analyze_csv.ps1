$path = "C:\Users\kando\AppData\Local\Temp\famidash_mesen_trace_cataclysm_20260520_171856_581.csv"
$mt = Import-Csv $path
"Total rows:"
$mt.Count
"Columns:"
$mt[0].psobject.Properties.Name -join ", "
"Distinct sim_cursor count:"
($mt | Group-Object sim_cursor).Count
"Last 30 rows:"
$mt | Select-Object -Last 30 | Format-Table sim_cursor, px, py, gamemode, vy, death_pc, death_ctx -AutoSize | Out-String -Width 250
"Rows where death_pc -ne empty:"
$deaths = $mt | Where-Object { $_.death_pc -ne "" -and $_.death_pc -ne $null -and $_.death_pc -ne "0" }
"Total death-marked rows:"
$deaths.Count
"First 5 deaths:"
$deaths | Select-Object -First 5 | Format-Table sim_cursor, px, py, gamemode, death_pc, death_ctx -AutoSize | Out-String -Width 250
"Last 5 deaths:"
$deaths | Select-Object -Last 5 | Format-Table sim_cursor, px, py, gamemode, death_pc, death_ctx -AutoSize | Out-String -Width 250
"Rows per sim_cursor at sim=887:"
$mt | Where-Object { $_.sim_cursor -eq "887" } | Format-Table sim_cursor, px, py, gamemode, vy, death_pc, death_ctx -AutoSize | Out-String -Width 250
"Rows per sim_cursor at sim=886:"
$mt | Where-Object { $_.sim_cursor -eq "886" } | Format-Table sim_cursor, px, py, gamemode, vy, death_pc, death_ctx -AutoSize | Out-String -Width 250
