param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$run="$root/.local/runs/HollowKnight/$Session"
if((Get-Content "$run/encounter-profile.request" -Raw).Trim() -ne 'ElderHu'){throw 'Elder Hu evidence required.'}
$hk=Get-BoundedRuntimeLog $run
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
if(($hk+$ns) -match '(?:HIT|CONTACT|NS_INPUT|HK_HUB|NS_VISUAL)_FAILED'){throw 'Ring integration failure.'}
& "$PSScriptRoot/Test-BotDamageEvidence.ps1" -Session $Session -RequireUnshieldedHealing
$parries=@([regex]::Matches($ns,'NS_VOLUME_VERDICT encounter=(?<encounter>[\w-]+) collider=(?<collider>-?\d+) activation=\d+ action=(?<action>\d+) kind=parry hostFrame=(?<host>\d+) nativeFrame=(?<native>\d+)[^\r\n]* grounded=(?<grounded>True|False)[^\r\n]*'))
$ground=0;$air=0;$tails=0
foreach($parry in $parries){
 $encounter=$parry.Groups['encounter'].Value;$action=$parry.Groups['action'].Value;$id=$parry.Groups['collider'].Value
 $start=[regex]::Match($hk,"ENCOUNTER_STATE role=HollowKnight encounter=$encounter geometryFrame=\d+ phase=Preparing")
 if(!$start.Success){throw 'Native ring encounter generation missing.'}
 $scope=$hk.Substring($start.Index);$next=[regex]::Match($scope,"ENCOUNTER_STATE role=HollowKnight encounter=(?!$encounter)[\w-]+ geometryFrame=\d+ phase=Preparing")
 if($next.Success){$scope=$scope.Substring(0,$next.Index)}
 $bindings=@([regex]::Matches($scope,'HK_RING_STRIKE_BOUND index=\d+ owner=(?<owner>-?\d+) colliders=(?<ids>[-\d,]+) shapes=4 nativeReset=True') | Where-Object {$_.Groups['ids'].Value.Split(',') -contains $id})
 if($bindings.Count -ne 1){throw 'Parried collider lacks one exact native ring owner.'}
 if($action -eq '0'){throw 'Native ring parry still has no shared strike identity.'}
 $owner=$bindings[0].Groups['owner'].Value
 if($scope -notmatch "HK_FINITE_WEAPON_STRIKE owner=\d+ entity=$owner generation=\d+ action=$action nativeEnable=True frame=\d+"){throw 'Native root activation did not author the parried strike.'}
 $native=$parry.Groups['native'].Value
 if($ns -notmatch "NS_VOLUME_ACTION_RETIRED action=$action sameNativeFrame=$native damaging=False"){throw 'Native parry did not synchronously retire sibling shapes.'}
 if($ns -match "NS_VOLUME_VERDICT encounter=$encounter collider=-?\d+ activation=\d+ action=$action kind=damage"){throw 'A parried native ring also injured Yi.'}
 $ids=$bindings[0].Groups['ids'].Value.Split(',')
 $later=@([regex]::Matches($scope,"HK_POOLED_ATTACK id=(?<id>-?\d+) activation=\d+ active=True category=Melee name=Box Impact action=$action frame=(?<frame>\d+)") | Where-Object {$ids -contains $_.Groups['id'].Value -and [long]$_.Groups['frame'].Value -gt [long]$parry.Groups['host'].Value})
 if(!$later.Count){continue}
 $tails++
 if($parry.Groups['grounded'].Value -eq 'True'){$ground++}else{$air++}
}
if($ground -lt 2 -or $air -lt 2){throw "Need repeated grounded and airborne native parries followed by actual same-strike landing shapes; got ground=$ground air=$air."}
if($hk -notmatch 'HK_FINITE_WEAPON_STRIKE owner=\d+ entity=-?\d+ generation=(?:[2-9]|[1-9]\d+) action=\d+ nativeEnable=True'){throw 'Native ring root reuse is unexercised.'}
Write-Output "PASS (scoped): Elder Hu ground=$ground airborne=$air actual post-parry native landing tails=$tails, all shared strike retirements synchronous with no same-strike injury; native ring reuse proven. Tier and human feel remain separate."
