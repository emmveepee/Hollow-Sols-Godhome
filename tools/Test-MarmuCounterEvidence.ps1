param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$run="$root/.local/runs/HollowKnight/$Session"
if((Get-Content "$run/encounter-profile.request" -Raw).Trim() -ne 'Marmu'){throw 'Marmu evidence required.'}
$hk=Get-BoundedRuntimeLog $run;$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
if(($hk+$ns) -match '(?:HIT|CONTACT|NS_INPUT|HK_HUB|NS_VISUAL)_FAILED'){throw 'Marmu integration failure.'}
& "$PSScriptRoot/Test-BotDamageEvidence.ps1" -Session $Session -RequireUnshieldedHealing
$counters=@([regex]::Matches($hk,'(?m)^(?<time>\S+) HK_AUTHORED_MOVE_COUNTER entity=(?<entity>-?\d+) action=(?<action>\d+) exit=Unroll hold=3 nativeFSM=True frame=\d+'))
$complete=0;$terminal=0
foreach($counter in $counters){
 $entity=$counter.Groups['entity'].Value;$action=$counter.Groups['action'].Value;$at=[DateTimeOffset]::Parse($counter.Groups['time'].Value)
 $verdicts=@([regex]::Matches($ns,"(?m)^(?<time>\S+) NS_VOLUME_CRIMSON_COUNTER action=$action frame=(?<native>\d+) encounter=(?<encounter>[\w-]+) collider=-?\d+ activation=\d+") | Where-Object {[Math]::Abs(([DateTimeOffset]::Parse($_.Groups['time'].Value)-$at).TotalSeconds) -lt .1})
 if($verdicts.Count -ne 1){throw 'Actual native crimson counter verdict missing or ambiguous.'}
 $encounter=$verdicts[0].Groups['encounter'].Value;$native=$verdicts[0].Groups['native'].Value
 if($ns -notmatch "NS_VOLUME_ACTION_RETIRED action=$action sameNativeFrame=$native damaging=False"){throw 'Counter did not synchronously retire Marmu strike.'}
 $acks=@([regex]::Matches($hk,"(?m)^(?<time>\S+) HK_COUNTER_RESULT sequence=(?<sequence>\d+) attack=$action applied=True") | Where-Object {[Math]::Abs(([DateTimeOffset]::Parse($_.Groups['time'].Value)-$at).TotalSeconds) -lt .1})
 if($acks.Count -ne 1){throw 'Native counter acknowledgement missing.'}
 $sequence=$acks[0].Groups['sequence'].Value
 $submitted=@([regex]::Matches($ns,"(?m)^(?<time>\S+) NS_COUNTER_SUBMITTED sequence=$sequence attack=$action") | Where-Object {[Math]::Abs(([DateTimeOffset]::Parse($_.Groups['time'].Value)-$at).TotalSeconds) -lt .1})
 if($submitted.Count -ne 1){throw 'Actual counter submission missing.'}
 $release=[regex]::Match($hk,"(?m)^(?<time>\S+) HK_AUTHORED_MOVE_HOLD_END entity=$entity action=$action gameplaySeconds=(?<seconds>[\d.]+) maxDrift=(?<drift>[\d.]+) nativeStateRestored=True frame=\d+ stationaryClip=paused spriteChanges=0")
 if(!$release.Success -or [float]$release.Groups['drift'].Value -gt .001){throw 'Native position/sprite-stable recovery missing.'}
 $until=[DateTimeOffset]::Parse($release.Groups['time'].Value)
 $injuries=@([regex]::Matches($ns,"(?m)^(?<time>\S+) NS_VOLUME_VERDICT encounter=$encounter collider=-?\d+ activation=\d+ action=$action kind=damage[^\r\n]*") | Where-Object {$t=[DateTimeOffset]::Parse($_.Groups['time'].Value);$t -ge $at -and $t -le $until})
 if($injuries.Count){throw 'Countered Marmu dealt damage during recovery.'}
 $seconds=[float]$release.Groups['seconds'].Value
 if($seconds -ge 3 -and $seconds -lt 3.1){$complete++;continue}
 $victory=@([regex]::Matches($hk,"(?m)^(?<time>\S+) ENCOUNTER_STATE role=HollowKnight encounter=$encounter geometryFrame=\d+ phase=Victory") | Where-Object {$t=[DateTimeOffset]::Parse($_.Groups['time'].Value);$t -gt $at -and $t -le $until})
 if(!$victory.Count){throw 'Shortened native recovery has no native victory evidence.'}
 $terminal++
}
if($complete -lt 2){throw 'Need repeated complete three-second native stationary recoveries.'}
& "$PSScriptRoot/Test-RosterEvidence.ps1" -Session $Session -Boss Marmu
Write-Output "PASS (scoped): Marmu complete native Unroll/three-second recoveries=$complete, holds ending at native victory=$terminal; zero drift/sprite changes and no held-strike injury. Actual injury oracle passes; foreground and other tiers remain unverified."
