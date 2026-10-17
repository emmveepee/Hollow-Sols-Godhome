param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
$boss=(Get-Content "$root/.local/runs/HollowKnight/$Session/encounter-profile.request" -Raw).Trim()
if($boss -notin @('DungDefender','WhiteDefender')){throw 'Wrong defender encounter.'}
$swipes=@([regex]::Matches($hk,'HK_POOLED_ATTACK id=(-?\d+) activation=\d+ active=True category=Melee name=Throw Swipe action=(\d+) frame=(\d+)'))
if($swipes.Count -lt 2){throw 'Repeated native throwing swipes unexercised.'}
if(@($swipes | Where-Object {$_.Groups[2].Value -eq '0'}).Count){throw 'Native throw swipe lacks logical strike identity.'}
if(@($swipes | Group-Object { $_.Groups[1].Value+':'+$_.Groups[2].Value } | Where-Object Count -gt 1).Count){throw 'Separate native throw swipe activations reused one strike identity.'}
$actions=@($swipes | ForEach-Object {$_.Groups[2].Value} | Sort-Object -Unique)
$injuries=0
foreach($action in $actions){
 if($hk -notmatch "HK_SHOCKWAVE_STRIKE_GROUP action=$action root=Dung_Ball_Large(?:_W)?\(Clone\)[^\r\n]*emittedShapesShareVerdict=True"){throw 'Throw ball lacks the swipe native spawn action.'}
 if($hk -notmatch "HK_POOLED_ATTACK id=-?\d+ activation=\d+ active=True category=Melee name=Dung Ball Large(?: W)?\(Clone\) action=$action frame="){throw 'Native thrown ball damage geometry lacks its swipe action.'}
 $hits=@([regex]::Matches($ns,"NS_VOLUME_VERDICT encounter=([\w-]+) collider=-?\d+ activation=\d+ action=$action kind=damage hostFrame=\d+ nativeFrame=(\d+)"))
 foreach($encounter in @($hits | Group-Object {$_.Groups[1].Value})){
  if($encounter.Count -gt 1){throw 'A throw swipe/ball strike injured Yi more than once.'}
  $frame=$encounter.Group[0].Groups[2].Value
  if($ns -notmatch "NS_VOLUME_ACTION_RETIRED action=$action sameNativeFrame=$frame damaging=False"){throw 'Throw siblings were not retired in the damage frame.'}
  $injuries++
 }
}
if($injuries -lt 2){throw 'Need repeated actual throw injuries, not only missed geometry.'}
Write-Output "PASS (scoped): $boss native throw actions=$($actions.Count), real single-injury throws=$injuries; swipe/ball share synchronous retirement. Independent older balls remain separate."
