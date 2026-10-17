param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$run="$root/.local/runs/HollowKnight/$Session"
if((Get-Content "$run/encounter-profile.request" -Raw).Trim() -ne 'NailsageSly'){throw 'Sly evidence profile required.'}
if(Test-Path "$run/invulnerable-probe.request"){throw 'Invulnerability cannot prove counter behavior.'}
& "$PSScriptRoot/Test-BotDamageEvidence.ps1" -Session $Session
& "$PSScriptRoot/Test-AuthoredMoveEvidence.ps1" -Session $Session -Boss NailsageSly
$hk=Get-BoundedRuntimeLog $run
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
foreach($exit in @('Cyclone_End','Fall','Stun_Reset')){
 if($hk -notmatch "HK_AUTHORED_MOVE_COUNTER entity=-?\d+ action=\d+ exit=$exit hold=0 nativeFSM=True"){throw "Missing real native Sly counter exit: $exit"}
}
$streaks=@([regex]::Matches($hk,'(?m)^(?<time>\S+) HK_AUTHORED_COUNTER_STREAK entity=(?<entity>-?\d+) action=(?<action>\d+) count=(?<count>[123]) required=3 frame=\d+'))
$sequence=@()
$completed=0
foreach($counter in $streaks){
 $count=[int]$counter.Groups['count'].Value
 if($count -eq 1){$sequence=@($counter);continue}
 if($sequence.Count -ne $count-1 -or $sequence[0].Groups['entity'].Value -ne $counter.Groups['entity'].Value){throw 'Invalid native consecutive-counter progression.'}
 $sequence+= $counter
 if($count -ne 3){continue}
 foreach($accepted in $sequence){
  $action=$accepted.Groups['action'].Value
  $stamp=[DateTimeOffset]::Parse($accepted.Groups['time'].Value)
  $verdict=@([regex]::Matches($ns,"(?m)^(\S+) NS_VOLUME_CRIMSON_COUNTER action=$action frame=\d+ encounter=[\w-]+ collider=-?\d+ activation=\d+") | Where-Object { [Math]::Abs(([DateTimeOffset]::Parse($_.Groups[1].Value)-$stamp).TotalMilliseconds) -le 100 })
  if($verdict.Count -ne 1){throw 'Counter streak lacks an exact native Yi unbound verdict.'}
 }
 $entity=$counter.Groups['entity'].Value;$action=$counter.Groups['action'].Value
 $thirdAt=[DateTimeOffset]::Parse($counter.Groups['time'].Value)
 $resume=@([regex]::Matches($hk,"(?m)^(\S+) HK_AUTHORED_FINAL_STAGGER_RESUME entity=$entity state=Flipping phasePreserved=True hp=\d+ frame=\d+") | Where-Object { $delay=([DateTimeOffset]::Parse($_.Groups[1].Value)-$thirdAt).TotalSeconds; $delay -gt 0 -and $delay -le 12 })
 if($resume.Count -ne 1 -or $hk -notmatch "HK_AUTHORED_MOVE_COUNTER entity=$entity action=$action exit=Stun_Reset"){throw 'Third unbound did not complete native final-phase stagger and recovery.'}
 $completed++;$sequence=@()
}
if($completed -lt 1){throw 'No completed native three-unbound final stagger/recovery; retain the run as partial evidence.'}
Write-Output "PASS (scoped): Sly native cyclone/dash/heavy exits and $completed completed final three-unbound stagger/recoveries; no simultaneous injuries."
