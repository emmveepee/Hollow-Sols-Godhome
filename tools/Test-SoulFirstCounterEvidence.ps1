param([Parameter(Mandatory)][Guid]$Session,[switch]$RequireDelayedAck)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
$boss=(Get-Content "$root/.local/runs/HollowKnight/$Session/encounter-profile.request" -Raw).Trim()
if($boss -notin @('SoulMaster','SoulTyrant')){throw 'Wrong native Soul family.'}
& "$PSScriptRoot/Test-SessionFailureEvidence.ps1" -Session $Session
$counters=@([regex]::Matches($hk,'(?m)^(\S+Z) HK_SOUL_PHASE_ONE_COUNTER entity=(-?\d+) action=(\d+) nativeExit=Stun_Init finalRules=False frame=(\d+)'))
if($counters.Count -lt 2){throw 'Repeated actual first-phase counters are unexercised.'}
$delayed=0
foreach($counter in $counters){
 $at=[datetime]::Parse($counter.Groups[1].Value);$entity=$counter.Groups[2].Value;$action=$counter.Groups[3].Value;$frame=[long]$counter.Groups[4].Value
 $acks=@([regex]::Matches($hk,"(?m)^(\S+Z) HK_COUNTER_RESULT sequence=(\d+) attack=$action applied=True")|Where-Object {[Math]::Abs(([datetime]::Parse($_.Groups[1].Value)-$at).TotalMilliseconds) -le 100})
 if($acks.Count -ne 1){throw 'First dive counter lacks exact native acknowledgement.'}
 $seq=$acks[0].Groups[2].Value
 $sent=@([regex]::Matches($ns,"(?m)^(\S+Z) NS_COUNTER_SUBMITTED sequence=$seq attack=$action\b")|Where-Object {[Math]::Abs(([datetime]::Parse($_.Groups[1].Value)-$at).TotalMilliseconds) -le 100})
 $verdict=@([regex]::Matches($ns,"(?m)^(\S+Z) NS_VOLUME_CRIMSON_COUNTER action=$action frame=\d+ encounter=([\w-]+) collider=-?\d+ activation=\d+")|Where-Object {[Math]::Abs(([datetime]::Parse($_.Groups[1].Value)-$at).TotalMilliseconds) -le 100})
 if($sent.Count -ne 1 -or $verdict.Count -ne 1){throw 'First dive counter lacks actual Yi Unbound success.'}
 $states=@([regex]::Matches($hk,"(?m)^(\S+Z) HK_SOUL_FIRST_STAGGER_STATE entity=$entity state=(\w+)[^\r\n]*invincible=False renderer=True[^\r\n]*frame=(\d+)")|Where-Object {[long]$_.Groups[3].Value -ge $frame -and ([datetime]::Parse($_.Groups[1].Value)-$at).TotalSeconds -ge 0 -and ([datetime]::Parse($_.Groups[1].Value)-$at).TotalSeconds -lt 3})
 if(!$states.Count -or $states[0].Groups[2].Value -ne 'Stun_Start_1' -or !@($states|Where-Object {$_.Groups[2].Value -eq 'Stunned'}).Count){throw 'Actual native phase-one deflated stagger sequence is unproven.'}
 $lease=@([regex]::Matches($hk,"(?m)^(\S+Z) HK_SOUL_DIVE_DELAYED_ACK entity=$entity action=$action nativeState=(Quake_Land|Quake_Waves|Re_Pos) gameplayTime=([\d.]+) deadline=([\d.]+) exactStrike=True frame=\d+")|Where-Object {[Math]::Abs(([datetime]::Parse($_.Groups[1].Value)-$at).TotalMilliseconds) -le 100})
 foreach($ack in $lease){if([double]$ack.Groups[3].Value -gt [double]$ack.Groups[4].Value){throw 'Delayed acknowledgement exceeded its native-time lease.'};$delayed++}
}
if($RequireDelayedAck -and !$delayed){throw 'Actual post-landing delayed counter acknowledgement is unexercised.'}
Write-Output "PASS (scoped): $boss first-phase Unbounds=$($counters.Count) real native Stun Init/visible vulnerable Stun Start1/Stunned; delayed exact-strike acknowledgements=$delayed. Delayed path required=$RequireDelayedAck. Other tiers/pixels remain separate."
