param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session" -Lines
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session" -Lines
if(($hk+$ns) -match '(?:CONTACT|HIT|NS_INPUT|NS_VISUAL|HK_VISUAL|NS_HUD_ASYNC)_FAILED'){throw 'Native integration/readback failure.'}
foreach($channel in 'player_hud','boss_hud'){
 if(!($ns -match "NS_HUD_ASYNC_VERIFIED channel=$channel exact=True bytes=[1-9]\d+ ")){throw "Missing same-render exact pixels: $channel"}
}
$upper=$hk | Where-Object {$_ -match 'HK_BALLOON_EXPORT_CULL .*above=[1-9]\d* ceiling=[\d.]+ wholeBounds=True margin=True upperCutoff=True nativeLifetime=True'}
if($upper.Count -lt 5){throw 'Insufficient actual upper-balloon cutoff windows.'}
# Join each native verdict to its exact bound dealer and then the native host
# Firebat impact; unrelated balloons/body strikes cannot satisfy facing evidence.
$bindings=@{};$resolved=@{};$directions=@{}
foreach($line in $ns){
 if($line -match 'NS_ATTACK_BOUND id=(-?\d+) dealer=(-?\d+) category=Projectile'){$bindings[$Matches[1]]=$Matches[2]}
 if($line -match 'NS_CONTACT_RESOLVED .*kind=damage dealer=(-?\d+) frame=(\d+).*incomingDirection=(-?\d+) facing=(Left|Right)'){
  $resolved["$($Matches[1])/$($Matches[2])"]=@($Matches[3],$Matches[4])
 }
 if($line -match '^(\S+) NS_PROJECTILE_RESOLVED id=(-?\d+) activation=(\d+) sameNativeFrame=(\d+) verdict=damage reflected=False'){
  $at=[datetime]$Matches[1];$id=$Matches[2];$activation=$Matches[3];$frame=$Matches[4]
  $r=$resolved["$($bindings[$id])/$frame"]
  if(!$r -or $r[0] -notin '-1','1'){continue}
  $impact=$hk | Where-Object {$_ -match "^(\S+) HK_PROJECTILE_RESOLVED id=$id activation=$activation nativeImpact=True root=Nightmare Firebat\(Clone\) state=Impact" -and [math]::Abs((([datetime]$Matches[1])-$at).TotalSeconds) -lt 1}
  if(!$impact){continue}
  $expected=if($r[0] -eq '1'){'Left'}else{'Right'}
  if($r[1] -ne $expected){throw "Native bat faces wrong way: id=$id incoming=$($r[0]) facing=$($r[1])"}
  $directions[$r[0]]=$true
 }
}
if(!$directions['1'] -or !$directions['-1']){throw 'Missing exact native bat damage/Impact evidence in both directions.'}
Write-Output "PASS (scoped): same-render player/boss HUD bytes, $($upper.Count) upper-balloon cutoff windows with off-edge margin/native lifetime, and both real Firebat hit directions. Does not certify 144Hz or human acceptance."
