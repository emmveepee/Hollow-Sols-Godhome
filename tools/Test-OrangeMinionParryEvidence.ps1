param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
$parries=@([regex]::Matches($hk,'HK_ORANGE_MINION_PARRY entity=(-?\d+) action=(\d+) sequence=(\d+) before=1 nativeMinimum=(-?\d+) killed=(True|False) applied=True nativeHit=True frame=(\d+)'))
$owners=@([regex]::Matches($hk,'HK_MINION_CONTACT_GROUP entity=(-?\d+) activation=(\d+) action=(\d+) seconds=[\d.]+ nativeHealthOwner=True'))
if($parries.Count -lt 2){throw 'Repeated actual native orange-minion parry dispels are unexercised.'}
$starts=@([regex]::Matches($hk,'ENCOUNTER_STATE role=HollowKnight encounter=([\w-]+)[^\r\n]* phase=Preparing'))
$appliedIdentities=@{};$tested=0
foreach($parry in $parries) {
 $entity=$parry.Groups[1].Value;$action=$parry.Groups[2].Value
 $owner=@($owners|Where-Object {$_.Index -lt $parry.Index -and $_.Groups[1].Value -eq $entity -and $_.Groups[3].Value -eq $action}|Select-Object -Last 1)
 if($owner.Count -ne 1){throw "Parried minion entity=$entity lacks actual generation/contact ownership."}
 $generation=$owner[0].Groups[2].Value
 $encounter=@($starts|Where-Object {$_.Index -lt $parry.Index}|Select-Object -Last 1)
 if($encounter.Count -ne 1){throw 'Minion parry has no native encounter identity.'}
 $enc=$encounter[0].Groups[1].Value
 $identity=$enc+':'+$parry.Groups[3].Value
 if($appliedIdentities.ContainsKey($identity)){throw 'One encounter outcome was applied to multiple minion injuries.'}
 $appliedIdentities[$identity]=$true
 if($ns -notmatch "NS_NATIVE_MINION_PARRY_NOTIFY action=$action\b[^\r\n]*damage=0 verdict=(?:parry|counter)") {throw 'No actual native defense notification supports the minion injury.'}
 if($ns -notmatch "NS_SUMMON_RETIRED encounter=$enc entity=$entity generation=$generation\b") {throw 'Native minion did not retire from the translated target/health presentation.'}
 $defenses=@([regex]::Matches($ns,"NS_VOLUME_VERDICT encounter=$enc collider=-?\d+ activation=\d+ action=$action kind=(?:parry|counter) hostFrame=\d+ nativeFrame=(\d+)"))
 if(!$defenses.Count){throw 'Actual native verdict lacks the exact minion contact action/encounter.'}
 $defendedFrame=($defenses|ForEach-Object {[long]$_.Groups[1].Value}|Measure-Object -Minimum).Minimum
 foreach($hit in [regex]::Matches($ns,"NS_VOLUME_VERDICT encounter=$enc collider=-?\d+ activation=\d+ action=$action kind=damage hostFrame=\d+ nativeFrame=(\d+)")) {
  if([long]$hit.Groups[1].Value -ge $defendedFrame){throw "Retired parried minion action=$action still caused native contact injury."}
 }
 $tested++
}
Write-Output "PASS (scoped): native orange-minion parry dispels=$tested; actual defense notifications, exact contact target/generation, one native Nail hit, target retirement and no later injury from the retired contact action. Other minions, ordinary attack coverage and all tiers remain separate."
