param([Parameter(Mandatory)][Guid]$Session,[Parameter(Mandatory)][ValidateSet('MassiveMossCharger','HiveKnight','BrokenVessel','LostKin','Nosk','WingedNosk','SoulWarrior','TraitorLord','GodTamer','DungDefender','WhiteDefender','NailsageSly','OroMato','CrystalGuardian','EnragedGuardian','GreyPrinceZote')][string]$Boss)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
if((Get-Content -LiteralPath "$root/.local/runs/HollowKnight/$Session/encounter-profile.request" -Raw).Trim() -ne $Boss){throw 'Wrong requested native move profile.'}
if(($hk+$ns) -match '(?:CONTACT|HIT|LIFECYCLE|NS_TARGET|NS_INPUT|HK_HUB|HK_PROXY|NS_VISUAL|HK_VISUAL)_FAILED'){throw 'Native move integration failure.'}
$counters=@([regex]::Matches($hk,'HK_AUTHORED_MOVE_COUNTER entity=(-?\d+) action=(\d+) exit=([^ ]+) hold=([\d.]+) nativeFSM=True frame=(\d+)'))
if($counters.Count -lt 2){throw 'Need at least two native counter transitions.'}
foreach($counter in $counters){
 $entity=$counter.Groups[1].Value;$action=$counter.Groups[2].Value
 $targetDefense=$ns -match "NS_HIT_OUTCOME sequence=\d+ frame=\d+ entity=$entity kind=(TaiChiKick|UnboundedCounter)[^\r\n]* attack=$action(?:\s|$)"
 if(-not $targetDefense -and $Boss -in @('DungDefender','WhiteDefender','NailsageSly','CrystalGuardian','EnragedGuardian','GreyPrinceZote') -and $counter.Groups[3].Value -in @('Stun_Set','Stun_Reset','Beam_End','Stun_Start')) {
  # Volume crimson defenses submit through the encounter anchor, rather than
  # the target-hit queue. Pair actual verdict/submission/native acknowledgement
  # by sequence and time; action numbers alone repeat across encounter resets.
  $lineStart=$hk.LastIndexOf("`n",$counter.Index)+1
  $stamp=[datetime]::Parse(($hk.Substring($lineStart,$counter.Index-$lineStart)).Trim())
  foreach($ack in [regex]::Matches($hk,"(\S+Z) HK_COUNTER_RESULT sequence=(\d+) attack=$action applied=True")) {
   if([Math]::Abs(([datetime]::Parse($ack.Groups[1].Value)-$stamp).TotalMilliseconds) -gt 100){continue}
   $sequence=$ack.Groups[2].Value
   $submitted=@([regex]::Matches($ns,"(\S+Z) NS_COUNTER_SUBMITTED sequence=$sequence attack=$action") | Where-Object {[Math]::Abs(([datetime]::Parse($_.Groups[1].Value)-$stamp).TotalMilliseconds) -le 100})
   $verdict=@([regex]::Matches($ns,"(\S+Z) NS_VOLUME_CRIMSON_COUNTER action=$action frame=\d+ encounter=[\w-]+ collider=-?\d+ activation=\d+") | Where-Object {[Math]::Abs(([datetime]::Parse($_.Groups[1].Value)-$stamp).TotalMilliseconds) -le 100})
   if($submitted.Count -eq 1 -and $verdict.Count -eq 1){$targetDefense=$true;break}
  }
 }
 if(-not $targetDefense){throw 'Native counter has no matching real Yi defense outcome.'}
 if($ns -notmatch "NS_VOLUME_ACTION_RETIRED action=$action "){throw 'Counter did not immediately retire its shared collision.'}
 if([double]$counter.Groups[4].Value -gt 0){
  $release=[regex]::Match($hk,"HK_AUTHORED_MOVE_HOLD_END entity=$entity action=$action gameplaySeconds=([\d.E+-]+) maxDrift=([\d.E+-]+) nativeStateRestored=True")
  if(-not $release.Success -or [double]$release.Groups[1].Value -lt [double]$counter.Groups[4].Value -or [double]$release.Groups[2].Value -gt .001){throw 'Requested native stationary recovery duration/position is unproven.'}
 }
}
if($Boss -eq 'GodTamer' -and [regex]::Matches($hk,'HK_GOD_TAMER_ROLL_COUNTER entity=-?\d+ action=\d+ effectActive=False audioPlaying=False state=RC_Land nativeRecovery=True').Count -lt 2){throw 'Native roll effect/audio termination is unproven.'}
if($Boss -eq 'SoulWarrior') {
 & "$PSScriptRoot/Test-SessionFailureEvidence.ps1" -Session $Session
 if($hk -notmatch 'HK_NATIVE_RANGE_PIVOT[^\r\n]*queries=3 nativeTerrainSensors=True aimedTargetUnchanged=True' -or
    $hk -notmatch 'HK_WARRIOR_NATIVE_STATE[^\r\n]*Dash_Range_[LR]_Blocked:False' -or
    $hk -notmatch 'HK_WARRIOR_NATIVE_STATE[^\r\n]*Stomp_Range_Blocked:False' -or
    $ns -notmatch 'NS_INPUT_NATIVE[^\r\n]*grounded=True'){throw 'Native grounded terrain-query correction is unproven.'}
 foreach($counter in $counters) {
  $entity=$counter.Groups[1].Value;$action=$counter.Groups[2].Value
  if($counter.Groups[3].Value -ne 'Idle' -or
    $hk -notmatch "HK_WARRIOR_COUNTER_RECOVERY entity=$entity action=$action slashActive=False exit=Idle skippedAnimationWait=True nativeNextChoice=True") {
   throw 'Warrior counter still depends on a skipped slash animation.'
  }
 }
 if(@($counters | Group-Object {$_.Groups[1].Value} | Where-Object Count -ge 2).Count -lt 1){throw 'No actor resumed native attacks for a subsequent real counter.'}
}
Write-Output "PASS (scoped): $Boss $Session nativeCounters=$($counters.Count), exact Yi defense/native exit, immediate strike retirement and completed stationary leases. All-tier timing and ordinary play remain separate gates."

