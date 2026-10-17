param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
$boss=(Get-Content "$root/.local/runs/HollowKnight/$Session/encounter-profile.request" -Raw).Trim()
if($boss -notin @('DungDefender','WhiteDefender')){throw 'Wrong defender encounter.'}
$groups=@([regex]::Matches($hk,'HK_SHOCKWAVE_STRIKE_GROUP action=(\d+) root=Dung_Ball_Small\(Clone\) frame=\d+ emittedShapesShareVerdict=True'))
if($groups.Count -lt 4){throw 'Missing actual grouped native eruption emission.'}
$actions=@($groups | ForEach-Object {$_.Groups[1].Value} | Sort-Object -Unique)
$injuries=0
foreach($action in $actions){
 if($hk -notmatch "HK_POOLED_ATTACK id=-?\d+ activation=\d+ active=True category=Melee name=(?:Dung Defender|White Defender) action=$action frame="){throw 'Eruption debris lacks the same active body strike.'}
 $hits=@([regex]::Matches($ns,"NS_VOLUME_VERDICT encounter=([\w-]+) collider=-?\d+ activation=\d+ action=$action kind=damage hostFrame=\d+ nativeFrame=(\d+) health=([\d.]+)"))
 foreach($encounter in @($hits | Group-Object {$_.Groups[1].Value})){
  if($encounter.Count -gt 1){throw 'One eruption accepted multiple native injuries.'}
  $frame=$encounter.Group[0].Groups[2].Value
  if($ns -notmatch "NS_VOLUME_ACTION_RETIRED action=$action sameNativeFrame=$frame damaging=False"){throw 'Eruption siblings were not retired in the damage frame.'}
  $injuries++
 }
}
if($injuries -lt 2){throw 'Need repeated actual eruption injuries, not only missed shapes.'}
& "$PSScriptRoot/Test-BotDamageEvidence.ps1" -Session $Session
Write-Output "PASS (scoped): $boss eruption groups=$($actions.Count) real single-injury eruptions=$injuries; body/debris share synchronous retirement."
