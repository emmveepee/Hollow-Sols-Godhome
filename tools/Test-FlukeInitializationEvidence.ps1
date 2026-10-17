param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
& "$PSScriptRoot/Test-RosterEvidence.ps1" -Session $Session -Boss Flukemarm
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
$flies=@([regex]::Matches($hk,'HK_ROSTER_TARGET entity=(-?\d+) activation=1 name=Fluke Fly Spawner(?: \(\d+\))? max=(\d+) shape=BoxCollider2D'))
if($flies.Count -lt 12){throw 'Two native sets of six authored fly bodies required.'}
foreach($fly in $flies){
    $entity=$fly.Groups[1].Value; $maximum=$fly.Groups[2].Value
    if($maximum -ne '13'){throw 'Installed fly initialization changed; audit rather than invent HP.'}
    if($ns -notmatch "NS_SUMMON_BOUND entity=$entity generation=1 owner=-?\d+ encounter=[\w-]+ frame=\d+ damage=0 tier=0 retained=\d+ health=$maximum maxHealth=$maximum(?:\s|$)"){
        throw "Fresh native fly did not arrive with a full health bar: $entity"
    }
}
Write-Output "PASS: Flukemarm session=$Session authoredFlyBindings=$($flies.Count), native activation HP and received health/maxHealth are 13/13. HUD size/readability remains human acceptance."
