param([Parameter(Mandatory)][Guid]$Session,[switch]$RequireAirborneCleanup,[switch]$RequireIndependentStrikes)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
if((Get-Content "$root/.local/runs/HollowKnight/$Session/encounter-profile.request" -Raw).Trim() -ne 'OroMato'){throw 'Wrong native fight.'}
& "$PSScriptRoot/Test-SessionFailureEvidence.ps1" -Session $Session
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
$dispatches=@([regex]::Matches($hk,'(?m)^(\S+) HK_NAILMASTER_PAIRED_STRIKE group=(\d+) first=(-?\d+) second=(-?\d+) state=(2_Slash|2_Stomp|Stomp_Slash) frame=(\d+)'))
if($dispatches.Count -lt 2){throw 'Need repeated actual dual-attack dispatch.'}
$confirmed=0
$airborne=0
$multiSource=0
foreach($dispatch in $dispatches) {
 $group=$dispatch.Groups[2].Value
 $first=$dispatch.Groups[3].Value;$second=$dispatch.Groups[4].Value
 $start=[datetime]::Parse($dispatch.Groups[1].Value)
 $retire=@([regex]::Matches($ns,"(?m)^(\S+) NS_COUNTER_GROUP_RETIRED group=$group sources=(\d+) sameNativeFrame=(\d+) damaging=False") | Where-Object {$delta=([datetime]::Parse($_.Groups[1].Value)-$start).TotalSeconds; $delta -ge 0 -and $delta -le 10})
 # Group IDs restart with a native encounter; bind by timestamp as well.
 if($retire.Count -eq 0){continue}
 # The mixed attack can be countered while the other brother is still in
 # kinematic anticipation, before that brother has any active damage source.
 # Both native recoveries still must occur. Require multi-source retirement
 # elsewhere in this same run, rather than inventing an active second source.
 if($retire.Count -ne 1 -or [int]$retire[0].Groups[2].Value -lt 1){throw 'Paired counter did not retire its current native sources exactly once.'}
 if([int]$retire[0].Groups[2].Value -ge 2){$multiSource++}
 $at=[datetime]::Parse($retire[0].Groups[1].Value)
 $members=@([regex]::Matches($hk,"(?m)^(\S+) HK_NAILMASTER_PAIRED_COUNTER group=$group entity=(-?\d+) action=(\d+) state=(Combo_Recover|Land) frame=(\d+)(?: ownStrike=\d+)?") | Where-Object {[Math]::Abs(([datetime]::Parse($_.Groups[1].Value)-$at).TotalMilliseconds) -lt 250})
 if($members.Count -ne 2 -or @($members | ForEach-Object {$_.Groups[2].Value} | Sort-Object -Unique).Count -ne 2){throw 'One unbound did not recover two distinct native brothers.'}
 if(@($members | Where-Object {$_.Groups[2].Value -in @($first,$second)}).Count -ne 2){throw 'Counter recovered the wrong native actor pair.'}
 if($members[0].Groups[5].Value -ne $members[1].Groups[5].Value -or $members[0].Groups[3].Value -ne $members[1].Groups[3].Value){throw 'Pair recovery was not one shared native-frame counter.'}
 if($RequireIndependentStrikes) {
  $owned=@($members | ForEach-Object {[regex]::Match($_.Value,'ownStrike=(\d+)')})
  if(@($owned | Where-Object {$_.Success -and [long]$_.Groups[1].Value -gt 0}).Count -ne 2 -or
      $owned[0].Groups[1].Value -eq $owned[1].Groups[1].Value){throw 'Counter group replaced independent actor strike identities.'}
 }
 foreach($member in $members) {
  $entity=$member.Groups[2].Value;$frame=$member.Groups[5].Value
  $physics=[regex]::Match($hk,"HK_NAILMASTER_COUNTER_PHYSICS group=$group entity=$entity before=(Dynamic|Kinematic) after=Dynamic nativeDiveCleanup=True frame=$frame(?:\s|$)")
  if(-not $physics.Success){throw 'Paired native body cleanup was not restored to authored dynamic physics.'}
  if($physics.Groups[1].Value -eq 'Kinematic'){$airborne++}
 }
 $action=$members[0].Groups[3].Value
 $verdict=@([regex]::Matches($ns,"(?m)^(\S+) NS_VOLUME_CRIMSON_COUNTER action=$action frame=\d+ encounter=[\w-]+ collider=-?\d+ activation=\d+") | Where-Object {[Math]::Abs(([datetime]::Parse($_.Groups[1].Value)-$at).TotalMilliseconds) -lt 100})
 if($verdict.Count -ne 1){throw 'Paired recovery has no unique real Yi crimson counter verdict.'}
 $ack=@([regex]::Matches($hk,"(?m)^(\S+) HK_COUNTER_RESULT sequence=(\d+) attack=$action applied=True") | Where-Object {[Math]::Abs(([datetime]::Parse($_.Groups[1].Value)-$at).TotalMilliseconds) -lt 250})
 if($ack.Count -ne 1){throw 'Native paired recovery has no unique acknowledged counter receipt.'}
 $sequence=$ack[0].Groups[2].Value
 $submitted=@([regex]::Matches($ns,"(?m)^(\S+) NS_COUNTER_SUBMITTED sequence=$sequence attack=$action") | Where-Object {[Math]::Abs(([datetime]::Parse($_.Groups[1].Value)-$at).TotalMilliseconds) -lt 250})
 if($submitted.Count -ne 1){throw 'Native paired counter acknowledgement has no matching Yi submission.'}
 $confirmed++
}
if($confirmed -lt 2){throw 'Need at least two real paired unbound counters.'}
if($multiSource -eq 0){throw 'No actual multi-source shared retirement was exercised.'}
if($RequireAirborneCleanup -and $airborne -eq 0){throw 'Native airborne kinematic cleanup was not exercised.'}
Write-Output "PASS (scoped): $Session pairedUnbounds=$confirmed, actual dual dispatch, one real Yi verdict, same-frame multi-source retirement and both authored native recovery states. Other tiers, every synchronized family and ordinary visual timing remain separate."
