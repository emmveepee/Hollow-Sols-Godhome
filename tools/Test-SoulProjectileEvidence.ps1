param([Parameter(Mandatory)][Guid]$Session,[switch]$RequireOrbitRepeat,[switch]$RequireWallImpact,[switch]$RequireNativeFlight)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
$boss=(Get-Content "$root/.local/runs/HollowKnight/$Session/encounter-profile.request" -Raw).Trim()
if($boss -notin @('SoulMaster','SoulTyrant')){throw 'Wrong native Soul projectile profile.'}
$resolved=@([regex]::Matches($ns,'NS_PROJECTILE_RESOLVED id=(-?\d+) activation=(\d+) sameNativeFrame=(\d+) verdict=(parry|counter|damage) reflected=(True|False)'))
$verdicts=@([regex]::Matches($ns,'NS_VOLUME_VERDICT encounter=([\w-]+) collider=(-?\d+) activation=(\d+) action=\d+ kind=(parry|counter|damage) hostFrame=\d+ nativeFrame=(\d+)[^\r\n]*'))
if($resolved.Count -lt 4){throw 'Repeated actual projectile verdicts are unexercised.'}
$tested=@{};$parries=0;$injuries=0
foreach($receipt in $resolved){
 $id=$receipt.Groups[1].Value;$activation=$receipt.Groups[2].Value;$frame=$receipt.Groups[3].Value
 $matching=@($verdicts|Where-Object {$_.Groups[2].Value -eq $id -and $_.Groups[3].Value -eq $activation -and $_.Groups[5].Value -eq $frame})
 if($matching.Count -ne 1 -or $matching[0].Groups[4].Value -ne $receipt.Groups[4].Value){throw 'Projectile resolution lacks exactly one matching native verdict.'}
 $enc=$matching[0].Groups[1].Value;$key=$enc+':'+$id+':'+$activation
 if($tested.ContainsKey($key)){throw 'Same native projectile activation resolved twice.'};$tested[$key]=$true
 $all=@($verdicts|Where-Object {$_.Groups[1].Value -eq $enc -and $_.Groups[2].Value -eq $id -and $_.Groups[3].Value -eq $activation})
 if($all.Count -ne 1){throw 'Same native projectile activation both defended/injured or resolved again.'}
 if($receipt.Groups[4].Value -eq 'damage'){
  if($receipt.Groups[5].Value -ne 'False'){throw 'An injuring projectile was also reflected.'};$injuries++
 }else{
  if($receipt.Groups[5].Value -eq 'True'){$parries++}
  elseif($receipt.Groups[4].Value -eq 'counter'){throw 'Successful unbound projectile counter did not request reflection.'}
  else{
   $rules=@([regex]::Matches($ns,"NS_PROJECTILE_DEFENSE_RULE precise=(True|False) ricochet=(True|False) reflectionEligible=(True|False) frame=$frame\b"))
   if(!$rules.Count -or @($rules|Where-Object {$_.Groups[3].Value -ne 'False'}).Count){throw 'Non-reflected parry lacks unambiguous native imprecise/no-Ricochet evidence.'}
  }
 }
}
if($parries -lt 2 -or $injuries -lt 1){throw 'Need actual reflected defenses and an independently injuring projectile.'}
$orbitImpacts=@([regex]::Matches($hk,'HK_SOUL_ORBIT_IMPACT id=(-?\d+) activation=(\d+) state=Impact parent=none frame=\d+'))
if($RequireOrbitRepeat){
 $nsStarts=@([regex]::Matches($ns,'ENCOUNTER_STATE role=NineSols encounter=([\w-]+)[^\r\n]* phase=(?:Restoring|Active)'))
 $hkStarts=@([regex]::Matches($hk,'ENCOUNTER_STATE role=HollowKnight encounter=([\w-]+)[^\r\n]* phase=Preparing'))
 function Get-RecordEncounter($record,$starts){
  $start=@($starts|Where-Object {$_.Index -lt $record.Index}|Select-Object -Last 1)
  if($start.Count -ne 1){throw 'Orbit evidence lacks native encounter ownership.'}
  return $start[0].Groups[1].Value
 }
 $groups=@([regex]::Matches($ns,'NS_SOUL_ORBIT_PARRY_FIXTURE group=(\d+) collider=(-?\d+) activation=(\d+) nativeInput=True frame=\d+')|Group-Object { (Get-RecordEncounter $_ $nsStarts)+':'+$_.Groups[1].Value })
 $provedByEncounter=@{}
 foreach($group in $groups){
  $proved=$false
  foreach($trial in $group.Group){
   $id=$trial.Groups[2].Value;$activation=$trial.Groups[3].Value
   $enc=Get-RecordEncounter $trial $nsStarts
   $detached=@([regex]::Matches($hk,"HK_SOUL_ORBIT_DETACHED id=$id activation=$activation\b[^\r\n]*")|Where-Object {(Get-RecordEncounter $_ $hkStarts) -eq $enc})
   $flights=@([regex]::Matches($hk,"HK_SOUL_ORBIT_FLIGHT id=$id activation=$activation [^\r\n]*parent=none [^\r\n]*")|Where-Object {(Get-RecordEncounter $_ $hkStarts) -eq $enc})
   $impacts=@($orbitImpacts|Where-Object {$_.Groups[1].Value -eq $id -and $_.Groups[2].Value -eq $activation -and (Get-RecordEncounter $_ $hkStarts) -eq $enc})
   if($detached.Count -and $flights.Count -and $impacts.Count){$proved=$true}
  }
  if($proved){$provedByEncounter[$enc]=1+$provedByEncounter[$enc]}
 }
 if(-not @($provedByEncounter.Values|Where-Object {$_ -ge 2}).Count){throw 'Two native orbit casts in one encounter with detached world flight and native Impact are unexercised.'}
}
$walls=@([regex]::Matches($hk,'HK_REFLECTED_TERRAIN_POP projectile=(-?\d+) activation=(\d+) terrain=(-?\d+) damage=0 frame=(\d+)'))
if($RequireWallImpact -and $walls.Count -lt 2){throw 'Repeated native reflected terrain collisions are unexercised.'}
if($RequireNativeFlight){
 $states=@([regex]::Matches($hk,'HK_POOLED_ATTACK id=-?\d+ activation=\d+ active=(True|False) category=Projectile[^\r\n]* nativeOrbState=(\S+)'))
 $live=@($states|Where-Object {$_.Groups[1].Value -eq 'True'})
 if(@($live|Where-Object {$_.Groups[2].Value -notin @('Orbiting','Chase_Hero')}).Count){throw 'Soul projectile was armed outside native flight.'}
 if($live.Count -lt 4 -or !@($states|Where-Object {$_.Groups[1].Value -eq 'False' -and $_.Groups[2].Value -in @('Pause','Init')}).Count){throw 'Native flight activation and dormant spawn window are unexercised.'}
}
Write-Output "PASS (scoped): $boss session=$Session exclusive projectile verdicts=$($tested.Count) actual reflected defenses=$parries actual injuries=$injuries; detached orbit impacts=$($orbitImpacts.Count) native terrain pops=$($walls.Count). Complete fights/other tiers and unrequested paths remain separate."
