param([int]$Start=3990,[int]$End=4000)
$nes = "$env:TEMP\famidash_mesen_trace_dorabaebasic6_20260526_190145_204.csv"
$lines = Get-Content $nes
foreach ($l in ($lines | Select-Object -Skip 2)) {
  $c = $l -split ","
  if ($c.Length -le 14) { continue }
  if ($c[1] -match '^\d+$' -and [int]$c[1] -ge $Start -and [int]$c[1] -le $End) {
    "rom={0} sc={1} px={2} py={3} a_next={4} a_cur={5} vy={6} gm={7} gmod={8} dash={9}" -f $c[0],$c[1],$c[2],$c[3],$c[4],$c[5],$c[10],$c[14],$c[12],$c[13]
  }
}
