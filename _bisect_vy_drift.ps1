$pf = (Get-ChildItem 'C:\Users\kando\AppData\Local\Temp\famidash_pf_debug_dorabaebasic6_*.txt' | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
$nes = 'C:\Users\kando\AppData\Local\Temp\famidash_mesen_physics_debug_dorabaebasic6_20260525_150905_249.log'

$pfData = @{}
foreach($l in (Get-Content $pf)){
    if($l -match '\[REPLAY f=(\d+)\] X=0x[0-9A-F]+ \(\d+px\) Y=0x([0-9A-F]+) \(\d+px\) velY=0x([0-9A-F-]+)'){
        $f = [int]$matches[1]; $y = [convert]::ToInt32($matches[2],16); $v=$matches[3]
        $pfData[$f] = @{Y=$y; V=$v}
    }
}

$nesData = @{}
$curSim = 0
foreach($l in (Get-Content $nes)){
    if($l -match '^F=\d+ sim=(\d+)'){ $curSim = [int]$matches[1] }
    elseif($l -match 'ufo_movement.in.*cpy=([0-9A-Fa-f]+) cpvx=-?\d+ cpvy=(-?\d+) G=\(([0-9A-Fa-f]+),([0-9A-Fa-f]+)'){
        $nesData[$curSim] = @{cpy=$matches[1]; cpvy=[int]$matches[2]; Gx=$matches[3]; Gy=$matches[4]}
    }
}

Write-Host ("Loaded {0} PF frames, {1} NES sims" -f $pfData.Count, $nesData.Count)

# Bisect: find first frame with non-zero drift
$samples = @(1,10,50,100,200,500,800,1000,1200,1400,1424,1500,1600,1700,1800,1900,2000,2050,2100,2150,2170,2175,2178,2179,2180,2181,2182)
Write-Host "`nFrame   PFY  NESlinY  expPF  drift |  PFvelY  NESvY"
foreach($f in $samples){
    $s = $f + 1
    if(-not $pfData.ContainsKey($f) -or -not $nesData.ContainsKey($s)){ continue }
    $pfY = $pfData[$f].Y -shr 8
    $cpyHex = $nesData[$s].cpy.PadLeft(4,'0').Substring(0,2)
    $nesGY = [convert]::ToInt32($cpyHex,16)
    $nesWorldY = 718 + $nesGY
    $expPF = $nesWorldY - 528
    $drift = $pfY - $expPF
    Write-Host ("{0,5}  {1,4}  {2,5}    {3,5}  {4,5} | {5,7}  {6,7}" -f $f, $pfY, $nesWorldY, $expPF, $drift, $pfData[$f].V, $nesData[$s].cpvy)
}
