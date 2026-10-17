param([Parameter(Mandatory)][Guid]$Session,[switch]$RequireLaterContact)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
$boss=(Get-Content "$root/.local/runs/HollowKnight/$Session/encounter-profile.request" -Raw).Trim()
if($boss -eq 'WatcherKnights'){throw 'Watcher Knights are excluded from summon/minion talisman immunity.'}
& "$PSScriptRoot/Test-SessionFailureEvidence.ps1" -Session $Session
$refused=@([regex]::Matches($ns,'NS_TALISMAN_CONTACT_REFUSED encounter=([\w-]+) collider=(-?\d+) activation=(\d+) action=(\d+) state=(FooAttack|FooExplode) stage=(queue|damage) health=([\d.]+) contactOnly=True nativeFrame=(\d+) gameplayTime=([\d.]+)'))
if($refused.Count -lt 2){throw 'Repeated actual native contact attempts during talisman are unexercised.'}
$starts=@([regex]::Matches($hk,'ENCOUNTER_STATE role=HollowKnight encounter=([\w-]+)[^\r\n]* phase=Preparing'))
$groups=@([regex]::Matches($hk,'HK_MINION_CONTACT_GROUP entity=(-?\d+) activation=(\d+) action=(\d+) seconds=1 nativeHealthOwner=True frame=\d+'))
$injuries=@([regex]::Matches($ns,'NS_VOLUME_VERDICT encounter=([\w-]+) collider=(-?\d+) activation=(\d+) action=(\d+) kind=damage hostFrame=\d+ nativeFrame=(\d+)'))
$minions=0;$later=0
foreach($block in $refused){
 $enc=$block.Groups[1].Value;$id=$block.Groups[2].Value;$activation=$block.Groups[3].Value;$action=$block.Groups[4].Value;$frame=[long]$block.Groups[8].Value
 $matches=@($groups|Where-Object {
  $group=$_;$owner=@($starts|Where-Object Index -LT $group.Index|Select-Object -Last 1)
  $group.Groups[3].Value -eq $action -and $owner.Count -eq 1 -and $owner[0].Groups[1].Value -eq $enc
 })
 if(!$matches.Count){continue} # Mawlek's explicitly permitted body is a separate policy.
 if(@($injuries|Where-Object {$_.Groups[1].Value -eq $enc -and $_.Groups[2].Value -eq $id -and $_.Groups[3].Value -eq $activation -and [long]$_.Groups[5].Value -eq $frame}).Count){throw 'The contact refused during talisman also injured Yi in that native frame.'}
 $minions++
 $later+=@($injuries|Where-Object {$_.Groups[1].Value -eq $enc -and $_.Groups[2].Value -eq $id -and $_.Groups[3].Value -eq $activation -and $_.Groups[4].Value -eq $action -and [long]$_.Groups[5].Value -gt $frame}).Count
}
if($minions -lt 2 -or ($RequireLaterContact -and !$later)){throw 'Native minion talisman protection or required later live contact is unexercised.'}
Write-Output "PASS (scoped): $boss actual minion contact refusals=$minions during native FooAttack/FooExplode, simultaneous injuries=0; later same-contact injuries=$later. Later live contact required=$RequireLaterContact. Mawlek, Watcher exclusion and other tiers remain separate."
