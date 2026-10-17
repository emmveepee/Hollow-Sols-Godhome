param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
if(($hk+$ns) -match 'NS_CONTACT_FAILED|LOCAL_AURA_FAILED|HIT_FAILED|gameplay-frame-timeout'){throw 'Gameplay failure in grounded dive evidence.'}
$hits=[regex]::Matches($ns,'NS_VOLUME_VERDICT encounter=(\S+) collider=(-?\d+) activation=(\d+) action=(\d+) kind=damage hostFrame=\d+ nativeFrame=(\d+) health=(\S+) grounded=True')
$verified=0
foreach($hit in $hits){
 $enc=$hit.Groups[1].Value;$id=$hit.Groups[2].Value;$activation=$hit.Groups[3].Value;$action=$hit.Groups[4].Value;$frame=$hit.Groups[5].Value
 if($hk -notmatch "HK_ROSTER_ATTACK_WINDOW entity=-?\d+ state=Dstab_Attack action=$action "){continue}
 if($ns -notmatch "NS_SISTERS_DESCENT_FIXTURE encounter=$enc collider=$id activation=$activation action=$action nativeCollision=True frame=\d+ floorPlacement=True hostFloor="){continue}
 $resolved=[regex]::Match($ns,"NS_CONTACT_DRAIN_RESOLVED dealer=(-?\d+) generation=(\d+) kind=damage frame=$frame\b")
 if(-not $resolved.Success){continue}
 $dealer=$resolved.Groups[1].Value;$generation=$resolved.Groups[2].Value
 if($ns -notmatch "NS_ATTACK_BOUND id=$id dealer=$dealer category=Melee"){throw 'Wrong native dealer for grounded dive.'}
 if($ns -notmatch "NS_CONTACT_QUEUED dealer=$dealer generation=$generation frame=\d+ grounded=True"){throw 'Missing grounded native contact queue.'}
 if($ns -notmatch "NS_CONTACT_DRAIN dealer=$dealer generation=$generation seconds=\S+ frame=\d+ newContacts=False"){throw 'Missing closed-collider drain lease.'}
 $verified++
}
if($verified -lt 1){throw 'No matched grounded native dive hit after collider closure.'}
Write-Output "PASS (focused): session=$Session groundedDiveHits=$verified; authored dive, floor placement, native queue, closed collider and real native damage matched. This does not certify repeated wins, crossing kicks, Watcher Knights or human stationary feel."
