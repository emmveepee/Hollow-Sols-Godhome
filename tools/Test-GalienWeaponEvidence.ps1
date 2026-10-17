param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$run="$root/.local/runs/HollowKnight/$Session"
if((Get-Content "$run/encounter-profile.request" -Raw).Trim() -ne 'Galien'){throw 'Galien evidence required.'}
if(Test-Path "$run/invulnerable-probe.request"){throw 'Invulnerability cannot certify weapon damage.'}
$hk=Get-BoundedRuntimeLog $run
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
if(($hk+$ns) -match '(?:HIT|CONTACT|NS_INPUT|HK_HUB|NS_VISUAL)_FAILED'){throw 'Weapon integration failure.'}
& "$PSScriptRoot/Test-BotDamageEvidence.ps1" -Session $Session -RequireUnshieldedHealing
$counters=@([regex]::Matches($hk,'(?m)^(?<time>\S+) HK_NATIVE_WEAPON_COUNTER action=(?<action>\d+) state=Decel nativeIdle=True hold=3 frame=\d+'))
$complete=0;$cancelled=0
foreach($counter in $counters){
 $action=$counter.Groups['action'].Value;$at=[DateTimeOffset]::Parse($counter.Groups['time'].Value)
 $ends=@([regex]::Matches($hk,"(?m)^(?<time>\S+) HK_NATIVE_WEAPON_HOLD_END action=$action gameplaySeconds=(?<seconds>[\d.]+) maxDrift=(?<drift>[\d.]+) nativeResume=(?<resume>True|False) frame=\d+") | Where-Object {[DateTimeOffset]::Parse($_.Groups['time'].Value) -gt $at})
 if(!$ends.Count){throw 'Native weapon hold has no release evidence.'}
 $end=$ends[0];$until=[DateTimeOffset]::Parse($end.Groups['time'].Value)
 $verdicts=@([regex]::Matches($ns,"(?m)^(?<time>\S+) NS_VOLUME_CRIMSON_COUNTER action=$action frame=\d+ encounter=(?<encounter>[\w-]+) collider=-?\d+ activation=\d+") | Where-Object {[Math]::Abs(([DateTimeOffset]::Parse($_.Groups['time'].Value)-$at).TotalSeconds) -lt .25})
 if(!$verdicts.Count){throw 'Native unbound verdict is missing near the weapon transition.'}
 $encounter=$verdicts[0].Groups['encounter'].Value
 $receipts=@([regex]::Matches($hk,'(?m)^(?<time>\S+) HIT_RECEIVED sequence=(?<sequence>\d+) entity=-?\d+ damage=0 kind=UnboundedCounter') | Where-Object {[Math]::Abs(([DateTimeOffset]::Parse($_.Groups['time'].Value)-$at).TotalSeconds) -lt .25})
 if(!$receipts.Count){throw 'Native counter submission missing.'}
 $sequence=$receipts[0].Groups['sequence'].Value
 $acks=@([regex]::Matches($ns,"(?m)^(?<time>\S+) HIT_ACK sequence=$sequence result=Applied health=\d+") | Where-Object {[Math]::Abs(([DateTimeOffset]::Parse($_.Groups['time'].Value)-$at).TotalSeconds) -lt .5})
 if(!$acks.Count){throw 'Native counter acknowledgement missing.'}
 $injuries=@([regex]::Matches($ns,"(?m)^(?<time>\S+) NS_VOLUME_VERDICT encounter=$encounter collider=-?\d+ activation=\d+ action=$action kind=damage[^\r\n]*") | Where-Object {$t=[DateTimeOffset]::Parse($_.Groups['time'].Value);$t -gt $at -and $t -lt $until})
 if($injuries.Count){throw 'Countered native weapon dealt damage during its hold.'}
 if([float]$end.Groups['drift'].Value -gt .001){throw 'Native weapon drifted during its hold.'}
 if($end.Groups['resume'].Value -eq 'True'){
  if([float]$end.Groups['seconds'].Value -lt 3 -or [float]$end.Groups['seconds'].Value -gt 3.1){throw 'Native recovery duration changed.'}
  $restart=@([regex]::Matches($hk,'(?m)^(?<time>\S+) HK_NATIVE_WEAPON_SPIN action=\d+ owner=Galien_Hammer frame=\d+') | Where-Object {$t=[DateTimeOffset]::Parse($_.Groups['time'].Value);$t -ge $until -and ($t-$until).TotalSeconds -lt .25})
  if(!$restart.Count){throw 'Native RECEL did not restart the weapon.'}
  $complete++
 }else{
  $terminal=@([regex]::Matches($hk,"(?m)^(?<time>\S+) ENCOUNTER_STATE role=HollowKnight encounter=$encounter geometryFrame=\d+ phase=(Victory|Returned)") | Where-Object {$t=[DateTimeOffset]::Parse($_.Groups['time'].Value);$t -ge $at -and $t -le $until})
  if(!$terminal.Count){throw 'Incomplete hold lacks native encounter completion evidence.'}
  $cancelled++
 }
}
if($complete -lt 2){throw 'Need repeated complete native unbound/recovery cycles.'}
# A persistent collider must actually rearm without an activation change. Group
# by encounter too: action/collider numbers alone cannot identify a later fight.
$damage=@([regex]::Matches($ns,'(?m)^(?<time>\S+) NS_VOLUME_VERDICT encounter=(?<encounter>[\w-]+) collider=(?<collider>-?\d+) activation=(?<activation>\d+) action=(?<action>\d+) kind=damage[^\r\n]*'))
$groups=@($damage | Group-Object {"$($_.Groups['encounter'].Value)/$($_.Groups['collider'].Value)/$($_.Groups['activation'].Value)/$($_.Groups['action'].Value)"} | Where-Object Count -gt 1)
$big=0;$mini=0
foreach($group in $groups){
 $collider=$group.Group[0].Groups['collider'].Value
 $isBig=[long]$group.Group[0].Groups['action'].Value -gt 0
 $name=if($isBig){'Hit (Spin|Idle)'}else{'Galien Mini Hammer\(Clone\)'}
 if($hk -notmatch "HK_POOLED_ATTACK id=$collider activation=\d+ active=True category=Melee name=$name action=\d+ frame=\d+"){throw 'Repeated injury lacks the exact native weapon owner evidence.'}
 $previous=$null
 foreach($hit in $group.Group){
  $at=[DateTimeOffset]::Parse($hit.Groups['time'].Value)
  if($null -ne $previous -and ($at-$previous).TotalSeconds -lt .98){throw 'Persistent weapon rearmed before its one-second retirement.'}
  $previous=$at
 }
 if($isBig){$big++}else{$mini++}
}
if(!$big -or !$mini){throw 'Need real repeated injuries from persistent big and independent mini weapon activations.'}
Write-Output "PASS (scoped): Galien complete native three-second idle/restart cycles=$complete, native terminal cancellations=$cancelled, persistent big rearm groups=$big, independent mini rearm groups=$mini; no held-action injury or simultaneous injury. Attuned scripted evidence only; all-tier human feel and visual bounds remain separate."
