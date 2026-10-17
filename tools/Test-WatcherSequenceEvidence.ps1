param([Parameter(Mandatory)][Guid]$Session,[switch]$RequireSwordDamage)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
if((Get-Content "$root/.local/runs/HollowKnight/$Session/encounter-profile.request" -Raw).Trim() -ne 'WatcherKnights'){throw 'Wrong native encounter profile.'}
$begins=@([regex]::Matches($hk,'HK_WATCHER_BALL_BEGIN entity=(-?\d+) action=(\d+) mode=(ground|air|back) gameplayTime=([\d.]+) frame=\d+'))
$counters=@([regex]::Matches($hk,'HK_WATCHER_COUNTER entity=(-?\d+) action=(\d+) mode=(ground|air|back) nativeExit=(Charge_End|Land|Roll_End) sparks=(True|False) rollAudio=(True|False) jumpAudio=(True|False) gameplayTime=([\d.]+) readyAt=([\d.]+) nativeCleanup=True frame=\d+'))
$warnings=@([regex]::Matches($ns,'NS_WATCHER_WARNING encounter=([\w-]+) entity=(-?\d+) activation=(\d+) action=(\d+) owner=(-?\d+) sounded=(True|False) frame=(\d+)'))
$starts=@([regex]::Matches($hk,'ENCOUNTER_STATE role=HollowKnight encounter=([\w-]+)[^\r\n]* phase=Preparing'))
function Get-EncounterForRecord($record){
 $start=@($starts|Where-Object {$_.Index -lt $record.Index}|Select-Object -Last 1)
 if($start.Count -ne 1){throw 'Native Watcher evidence lacks encounter ownership.'}
 return $start[0].Groups[1].Value
}
if($begins.Count -lt 4 -or $counters.Count -lt 2 -or $warnings.Count -lt 4){throw 'Repeated native Watcher sequence/counter/cue coverage is unexercised.'}
foreach($group in $warnings|Group-Object { $_.Groups[1].Value+':'+$_.Groups[2].Value+':'+$_.Groups[4].Value }){
 if($group.Count -ne 1){throw 'A Watcher ball sequence replayed its native warning/sound.'}
}
$modes=@{}
foreach($counter in $counters){
 $entity=$counter.Groups[1].Value;$action=$counter.Groups[2].Value;$mode=$counter.Groups[3].Value
 $enc=Get-EncounterForRecord $counter
 $modes[$mode]=$true
 if($mode -eq 'ground' -and ($counter.Groups[4].Value -ne 'Charge_End' -or $counter.Groups[5].Value -ne 'False' -or $counter.Groups[6].Value -ne 'False')){throw 'Ground counter did not execute native spark/audio cleanup.'}
 if($mode -eq 'air' -and ($counter.Groups[4].Value -ne 'Land' -or $counter.Groups[7].Value -ne 'False')){throw 'Air counter did not execute native spin-audio cleanup.'}
 if([double]$counter.Groups[9].Value-[double]$counter.Groups[8].Value -lt 1.999){throw 'Counter lacks the requested two-second gameplay deadline.'}
 foreach($begin in $begins|Where-Object {$_.Index -gt $counter.Index -and $_.Groups[1].Value -eq $entity -and (Get-EncounterForRecord $_) -eq $enc}){
  if([double]$begin.Groups[4].Value -lt [double]$counter.Groups[9].Value){throw 'Same Watcher started another ball attack inside its counter cooldown.'}
 }
 if($ns -notmatch "NS_VOLUME_ACTION_RETIRED action=$action sameNativeFrame=\d+ damaging=False"){throw 'Counter lacks actual same-frame collision retirement.'}
 if($ns -notmatch "NS_HIT_OUTCOME sequence=\d+ frame=\d+ entity=$entity kind=TaiChiKick[^\r\n]* encounter=$enc [^\r\n]* attack=$action(?:\s|$)" -and
   $ns -notmatch "NS_VOLUME_CRIMSON_COUNTER action=$action frame=\d+ encounter=$enc\b"){throw 'Native exit lacks actual encounter-owned Yi defense.'}
}
if(-not $modes.ContainsKey('ground') -or -not $modes.ContainsKey('air')){throw 'Both grounded and aerial native counter cleanup are required.'}
$swordHits=0
if($RequireSwordDamage){
 foreach($sword in [regex]::Matches($hk,'HK_WATCHER_SWORD_BEGIN entity=(-?\d+) action=(\d+) swing=([12]) push=(-?1) frame=\d+')){
  $action=$sword.Groups[2].Value;$direction=$sword.Groups[4].Value
  $enc=Get-EncounterForRecord $sword
  $hits=@([regex]::Matches($ns,"NS_VOLUME_VERDICT encounter=$enc collider=-?\d+ activation=\d+ action=$action kind=damage [^\r\n]*incomingDirection=(-?\d+)"))
  foreach($hit in $hits){if($hit.Groups[1].Value -ne $direction){throw 'Native sword injury used the wrong swing direction.'};$swordHits++}
 }
 if($swordHits -lt 2){throw 'Actual directional sword injuries remain unexercised.'}
}
Write-Output "PASS (scoped): Watcher sequences=$($begins.Count) counters=$($counters.Count) unique native warning/sound cues=$($warnings.Count); native ground/air cleanup and two-second cooldown; directional sword injuries=$swordHits. Complete fights, other tiers and backward-roll counter remain separate."
