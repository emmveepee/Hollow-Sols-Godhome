param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$run="$root/.local/runs/HollowKnight/$Session"
if((Get-Content "$run/encounter-profile.request" -Raw).Trim() -ne 'HornetSentinel'){throw 'Hornet Sentinel evidence required.'}
$hk=Get-BoundedRuntimeLog $run;$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
if(($hk+$ns) -match '(?:CONTACT|HIT|NS_VISUAL|HK_VISUAL)_FAILED|CONTACT_STOP reason=(?:InvalidDataException|InvalidOperationException)'){throw 'Hornet integration failed.'}
$parries=@([regex]::Matches($ns,'(?m)^(?<time>\S+) NS_VOLUME_VERDICT encounter=(?<encounter>[\w-]+) collider=(?<id>-?\d+) activation=(?<activation>\d+) action=(?<action>\d+) kind=parry hostFrame=\d+ nativeFrame=(?<frame>\d+)'))
$completed=0;$reused=0
foreach($parry in $parries){
 $id=$parry.Groups['id'].Value;$activation=$parry.Groups['activation'].Value;$action=$parry.Groups['action'].Value
 $at=[DateTimeOffset]::Parse($parry.Groups['time'].Value)
 $source=@([regex]::Matches($hk,"(?m)^(?<time>\S+) HK_POOLED_ATTACK id=$id activation=$activation active=True category=Melee name=Hit Counter 1 action=$action frame=\d+") | Where-Object {$t=[DateTimeOffset]::Parse($_.Groups['time'].Value);$t -le $at.AddMilliseconds(15) -and ($at-$t).TotalSeconds -lt .5})
 if(!$source.Count){continue} # Returning needle parries are separate strikes.
 if($source.Count -ne 1){throw 'Ambiguous native first-counter activation.'}
 $tail=@([regex]::Matches($hk,"(?m)^(?<time>\S+) HK_POOLED_ATTACK id=-?\d+ activation=\d+ active=True category=Melee name=Hit Counter 2 action=$action frame=\d+") | Where-Object {$t=[DateTimeOffset]::Parse($_.Groups['time'].Value);$t -ge $at -and ($t-$at).TotalSeconds -lt 1})
 if($tail.Count -ne 1){throw 'Parried native counter did not exercise its later second hitbox.'}
 if($ns -notmatch "NS_VOLUME_ACTION_RETIRED action=$action sameNativeFrame=$($parry.Groups['frame'].Value) damaging=False"){throw 'Counter parry did not retire the shared strike immediately.'}
 $enc=$parry.Groups['encounter'].Value
 if($ns -match "NS_VOLUME_VERDICT encounter=$enc collider=-?\d+ activation=\d+ action=$action kind=damage "){throw 'Parried counter chain also injured Yi.'}
 $completed++
 $next=[long]$activation+1
 if($hk -match "HK_POOLED_ATTACK id=$id activation=$next active=True category=Melee name=Hit Counter 1 action=[1-9]\d* frame="){$reused++}
}
if($completed -lt 2 -or $reused -lt 1){throw "Need repeated real counter parries through later native hitboxes and fresh reuse: counters=$completed reused=$reused."}
& "$PSScriptRoot/Test-BotDamageEvidence.ps1" -Session $Session -RequireUnshieldedHealing
Write-Output "PASS (scoped): $completed actual first-counter parries through native second-hitbox activation, no same-strike injury, $reused later fresh counter activations. Needle, difficulty, victory and human timing coverage remain separate."
