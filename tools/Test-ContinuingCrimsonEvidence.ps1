param([Parameter(Mandatory)][Guid]$Session,[switch]$RequireNativeContinuation)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
$boss=(Get-Content "$root/.local/runs/HollowKnight/$Session/encounter-profile.request" -Raw).Trim()
if($boss -notin @('BrokenVessel','LostKin','TraitorLord')){throw 'Wrong non-interrupting Crimson profile.'}
& "$PSScriptRoot/Test-SessionFailureEvidence.ps1" -Session $Session
$vessel=$boss -ne 'TraitorLord'
$pattern=if($vessel){'(?m)^(\S+Z) HK_VESSEL_SHAKE_COUNTER action=(\d+) nativeRecycle=True bossInterrupted=False frame=(\d+)'}else{'(?m)^(\S+Z) HK_AUTHORED_SEQUENCE_COUNTER entity=(-?\d+) action=(\d+) state=Waves nativeSequenceContinues=True frame=(\d+)'}
$counters=@([regex]::Matches($hk,$pattern))
if($counters.Count -lt 2){throw 'Repeated actual non-interrupting Crimson counters are unexercised.'}
$proved=0
foreach($counter in $counters){
 $at=[datetime]::Parse($counter.Groups[1].Value)
 $action=if($vessel){$counter.Groups[2].Value}else{$counter.Groups[3].Value}
 $frame=if($vessel){[long]$counter.Groups[3].Value}else{[long]$counter.Groups[4].Value}
 $acks=@([regex]::Matches($hk,"(?m)^(\S+Z) HK_COUNTER_RESULT sequence=(\d+) attack=$action applied=True")|Where-Object {[Math]::Abs(([datetime]::Parse($_.Groups[1].Value)-$at).TotalMilliseconds) -le 100})
 if($acks.Count -ne 1){throw 'Counter lacks one exact native acknowledgement.'}
 $seq=$acks[0].Groups[2].Value
 $sent=@([regex]::Matches($ns,"(?m)^(\S+Z) NS_COUNTER_SUBMITTED sequence=$seq attack=$action\b")|Where-Object {[Math]::Abs(([datetime]::Parse($_.Groups[1].Value)-$at).TotalMilliseconds) -le 100})
 $verdict=@([regex]::Matches($ns,"(?m)^(\S+Z) NS_VOLUME_CRIMSON_COUNTER action=$action frame=(\d+) encounter=([\w-]+) collider=(-?\d+) activation=(\d+)")|Where-Object {[Math]::Abs(([datetime]::Parse($_.Groups[1].Value)-$at).TotalMilliseconds) -le 100})
 if($sent.Count -ne 1 -or $verdict.Count -ne 1){throw 'Counter lacks exact real Yi Unbound verdict and submission.'}
 $enc=$verdict[0].Groups[3].Value
 $retired=@([regex]::Matches($ns,"(?m)^(\S+Z) NS_VOLUME_ACTION_RETIRED action=$action\b[^\r\n]*")|Where-Object {[Math]::Abs(([datetime]::Parse($_.Groups[1].Value)-$at).TotalMilliseconds) -le 100})
 if($retired.Count -ne 1){throw 'Counter did not immediately retire its strike.'}
 if($ns -match "NS_VOLUME_VERDICT encounter=$enc collider=-?\d+ activation=\d+ action=$action kind=damage\b"){throw 'The countered strike also injured Yi.'}
 if($vessel){
  $spawn=@([regex]::Matches($hk,"(?m)^(\S+Z) HK_VESSEL_SHAKE_PROJECTILE entity=(-?\d+) action=$action group=\d+[^\r\n]*crimson=True nativeSequenceContinues=True frame=\d+")|Where-Object {[datetime]::Parse($_.Groups[1].Value) -le $at}|Select-Object -Last 1)
  if($spawn.Count -ne 1){throw 'Counter lacks its native shake projectile owner.'}
  $entity=$spawn[0].Groups[2].Value
 }else{
  $entity=$counter.Groups[2].Value
  if($hk -notmatch "HK_TRAITOR_WAVE_MANIFEST entity=$entity wave=-?\d+ nativeActivation=Waves singleDamageOwner=True counterInterrupts=False"){throw 'Wave lacks the exact native single-owner contract.'}
 }
 if($RequireNativeContinuation){
  $states=@([regex]::Matches($hk,"(?m)^(\S+Z) HK_CONTINUING_MOVE_STATE entity=$entity action=\d+ state=(\w+) [^\r\n]*nativeFSM=True frame=(\d+)")|Where-Object {[datetime]::Parse($_.Groups[1].Value) -gt $at -and ([datetime]::Parse($_.Groups[1].Value)-$at).TotalSeconds -lt 3 -and [long]$_.Groups[3].Value -gt $frame})
  $expected=if($vessel){'Shake_End'}else{'Slam_End'}
  if(!$states.Count -or $states[0].Groups[2].Value -ne $expected){throw "Native counter did not continue naturally to $expected."}
 }
 $proved++
}
Write-Output "PASS (scoped): $boss native non-interrupting Crimson counters=$proved; exact projectile/wave owner, real Unbound verdict, acknowledgement and strike retirement. Native continuation required=$RequireNativeContinuation. Ordinary parry rejection/other tiers remain separate."
