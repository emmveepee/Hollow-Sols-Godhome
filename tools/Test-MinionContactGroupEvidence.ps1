param([Parameter(Mandatory)][Guid]$Session,[Guid]$Encounter=[Guid]::Empty)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
if($Encounter -ne [Guid]::Empty){
 $start=[regex]::Match($hk,"ENCOUNTER_STATE role=HollowKnight encounter=$Encounter geometryFrame=\d+ phase=Preparing")
 if(!$start.Success){throw 'Requested native minion generation was not recorded.'}
 $hk=$hk.Substring($start.Index)
 $next=[regex]::Match($hk,"ENCOUNTER_STATE role=HollowKnight encounter=(?!$Encounter)[\w-]+ geometryFrame=\d+ phase=Preparing")
 if($next.Success){$hk=$hk.Substring(0,$next.Index)}
}
$groups=@([regex]::Matches($hk,'HK_MINION_CONTACT_GROUP entity=(-?\d+) activation=(\d+) action=(\d+) seconds=1 nativeHealthOwner=True frame=\d+'))
if($groups.Count -lt 2){throw 'No independent native minion contact groups exercised.'}
$actions=@($groups | ForEach-Object {$_.Groups[3].Value} | Sort-Object -Unique)
$injuries=0; $rearms=0
foreach($action in $actions){
 $hits=@([regex]::Matches($ns,"(?m)^(?<time>\S+) NS_VOLUME_VERDICT encounter=(?<encounter>[\w-]+) collider=-?\d+ activation=\d+ action=$action kind=damage hostFrame=\d+ nativeFrame=(?<frame>\d+)[^\r\n]*"))
 foreach($nativeGeneration in @($hits | Group-Object {$_.Groups['encounter'].Value})){
  if($Encounter -ne [Guid]::Empty -and $nativeGeneration.Name -ne "$Encounter"){continue}
  $prior=$null
  foreach($hit in $nativeGeneration.Group){
   $frame=$hit.Groups['frame'].Value
   if($ns -notmatch "NS_ACTION_COOLDOWN action=$action seconds=1 sameNativeFrame=$frame damaging=False"){throw 'Minion injury did not synchronously suspend sibling contact shapes.'}
   $at=[DateTimeOffset]::Parse($hit.Groups['time'].Value)
   # Pauses can only increase wall-clock duration of the native gameplay lease.
   if($null -ne $prior){if(($at-$prior).TotalMilliseconds -lt 975){throw 'Same native minion hit again inside its one-second contact retirement.'}; $rearms++}
   $prior=$at; $injuries++
  }
 }
}
if($injuries -lt 2){throw 'Need multiple actual minion contact injuries.'}
& "$PSScriptRoot/Test-BotDamageEvidence.ps1" -Session $Session -RequireUnshieldedHealing -Encounter $Encounter
& "$PSScriptRoot/Test-SessionFailureEvidence.ps1" -Session $Session
Write-Output "PASS (scoped): encounter=$Encounter independent minion contact groups=$($groups.Count), actual injuries=$injuries, later native rearms=$rearms; sibling shapes share one-second retirement. Talisman/true-global modes remain separate."
