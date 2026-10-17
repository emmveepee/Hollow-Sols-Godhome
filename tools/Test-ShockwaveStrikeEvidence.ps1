param([Parameter(Mandatory)][Guid]$Session,[switch]$CounterOnly,[switch]$BeforeEvidenceCap)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
$cap=[regex]::Match($hk,'HK_SHOCKWAVE_EVIDENCE_CAPPED frame=(\d+) controllers=64 laterControllersUnobserved=True')
if($BeforeEvidenceCap){
 if(!$cap.Success){throw 'Explicit native evidence-cap frame missing; cannot infer a partial audit window.'}
 $capFrame=[long]$cap.Groups[1].Value
 # Only HK geometry preceding the explicit cap is fully observable. Preserve
 # every injury to those observed actions, including later settling outcomes.
 $hk=($hk -split "`n"|Where-Object {
  if($_ -match 'HK_(?:SHOCKWAVE_STRIKE_GROUP|POOLED_ATTACK).*\bframe=(\d+)'){return [long]$Matches[1] -lt $capFrame}
  return $true
 }) -join "`n"
}
$boss=(Get-Content "$root/.local/runs/HollowKnight/$Session/encounter-profile.request" -Raw).Trim()
$waveName=if($boss -eq 'GreyPrinceZote'){'Shockwave_Wave_Z'}else{'Shockwave_Wave'}
$waves=@([regex]::Matches($hk,"HK_SHOCKWAVE_STRIKE_GROUP action=(\d+) root=$waveName\(Clone\)[^\r\n]*emittedShapesShareVerdict=True"))
$pieces=@([regex]::Matches($hk,'HK_POOLED_ATTACK id=-?\d+ activation=\d+ active=True category=Melee name=Shockwave Spurt[^\r\n]* action=(\d+) frame='))
if(!$waves.Count -or !$pieces.Count){throw 'Native controller/emitted shockwave damage geometry unexercised.'}
$actions=@($waves | ForEach-Object {$_.Groups[1].Value} | Sort-Object -Unique)
$generations=@([regex]::Matches($hk,'ENCOUNTER_STATE role=HollowKnight encounter=([\w-]+) geometryFrame=\d+ phase=Preparing'))
function Get-NativeGeneration([int]$index){
 $generation=$null
 foreach($candidate in $generations){if($candidate.Index -gt $index){break};$generation=$candidate.Groups[1].Value}
 if(!$generation){throw 'Native wave evidence lacks encounter-generation boundary.'}
 return $generation
}
$observed=@{}
foreach($wave in $waves){$observed["$(Get-NativeGeneration $wave.Index)/$($wave.Groups[1].Value)"]=$true}
foreach($piece in $pieces){
 if($piece.Groups[1].Value -eq '0' -or !$observed.ContainsKey("$(Get-NativeGeneration $piece.Index)/$($piece.Groups[1].Value)")){throw 'An active shockwave piece did not inherit an observed controller strike in its native encounter.'}
}
$injuries=0
$zoteShapes=@{}
if($boss -eq 'GreyPrinceZote'){
 foreach($shape in [regex]::Matches($hk,'HK_POOLED_ATTACK id=(-?\d+) activation=\d+ active=(?:True|False) category=Melee name=(?:Shockwave Spurt[^\r\n]*|Grey Prince) action=')){
  $zoteShapes[$shape.Groups[1].Value]=$true
 }
}
foreach($identity in $observed.Keys){
 $nativeEncounter,$action=$identity -split '/'
 $hits=@([regex]::Matches($ns,"NS_VOLUME_VERDICT encounter=($nativeEncounter) collider=-?\d+ activation=\d+ action=$action kind=damage hostFrame=\d+ nativeFrame=(\d+)"))
 if($boss -eq 'GreyPrinceZote'){
  # Passive minion contact has a separate action/cooldown namespace.
  # Include the actual boss body and native wave pieces, excluding independent minions.
  $hits=@($hits|Where-Object {$zoteShapes.ContainsKey([regex]::Match($_.Value,'collider=(-?\d+)').Groups[1].Value)})
 }
 foreach($encounter in @($hits | Group-Object {$_.Groups[1].Value})){
  if($encounter.Count -gt 1){throw "One native wave strike applied multiple injuries: encounter=$nativeEncounter action=$action."}
  $frame=$encounter.Group[0].Groups[2].Value
  if($ns -notmatch "NS_VOLUME_ACTION_RETIRED action=$action sameNativeFrame=$frame damaging=False"){throw 'Shared shockwave damage retirement was not synchronous.'}
  $injuries++
 }
}
if($CounterOnly){
 $counters=@([regex]::Matches($hk,'HK_AUTHORED_SEQUENCE_COUNTER entity=-?\d+ action=(\d+) state=(?:S_Attack|Slam|S_Attack_Recover) nativeSequenceContinues=True frame=(\d+)'))
 $verified=0
 foreach($counter in $counters){
  $action=$counter.Groups[1].Value;$nativeFrame=[long]$counter.Groups[2].Value
  $nativeEncounter=Get-NativeGeneration $counter.Index
  if(!@($waves | Where-Object { (Get-NativeGeneration $_.Index) -eq $nativeEncounter -and $_.Groups[1].Value -eq $action -and $_.Value -match 'frame=(\d+)' -and [long]$Matches[1] -ge $nativeFrame -and [long]$Matches[1] -le $nativeFrame+120 }).Count){continue}
  $defense=[regex]::Match($ns,"NS_VOLUME_CRIMSON_COUNTER action=$action frame=(\d+) encounter=($nativeEncounter)")
  if(!$defense.Success){continue}
  $encounter=$defense.Groups[2].Value
  if($ns -match "NS_VOLUME_VERDICT encounter=$encounter collider=-?\d+ activation=\d+ action=$action kind=damage"){throw 'A countered native smash/wave accepted injury.'}
  if($ns -notmatch "NS_VOLUME_ACTION_RETIRED action=$action sameNativeFrame=$($defense.Groups[1].Value) damaging=False"){throw 'Native unbound retirement lacks same-frame evidence.'}
  $verified++
 }
 if($verified -lt 2){throw 'Need repeated native smash unbounds followed by same-action waves without injury.'}
 Write-Output "PASS (scoped): native smash counters=$verified; sequence continues, emitted wave shares retired action, no same-action injury. Damage-only coverage remains separate."
}else{
 if($injuries -lt 2){throw 'Need repeated actual injury to observed wave actions, not only missed geometry.'}
}
Write-Output "PASS (scoped): native shockwave controllers=$($waves.Count), active emitted pieces=$($pieces.Count), single-injury actions=$injuries. Paired-wave and other-family coverage remain separate."
if($BeforeEvidenceCap){Write-Output "Partial window ends before HK frame=$capFrame; later controller geometry is unverified. Whole-run injury checks are unchanged."}
