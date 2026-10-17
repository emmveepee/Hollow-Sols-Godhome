param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$root/.local/runs/NineSols/$Session"
$holds=@([regex]::Matches($ns,'NS_VOLLEY_DAMAGE_RETIRED group=(\d+) sameNativeFrame=(\d+) defenseIndependent=True initialHold=1 gameplayTime=([\d.]+) independentAfterHold=True repeat=False'))
$hits=@([regex]::Matches($ns,'NS_VOLUME_VERDICT encounter=([\w-]+) collider=(-?\d+) activation=(\d+) action=(\d+) kind=damage hostFrame=\d+ nativeFrame=(\d+)[^\r\n]* damageGroup=(\d+) gameplayTime=([\d.]+)'))
$owners=@([regex]::Matches($hk,'HK_NATIVE_VOLLEY_GROUP group=(\d+) root=([^ ]+) rootId=(-?\d+) generation=(\d+) frame=(\d+) defenseIndependent=True'))
$ownerEncounters=@{};$activeEncounter=''
foreach($marker in [regex]::Matches($hk,'ENCOUNTER_STATE role=HollowKnight encounter=([\w-]+)[^\r\n]* phase=Preparing|HK_NATIVE_VOLLEY_GROUP[^\r\n]*')) {
 if($marker.Groups[1].Success){$activeEncounter=$marker.Groups[1].Value}else{$ownerEncounters[$marker.Index]=$activeEncounter}
}
$tested=0;$nativePieces=0
foreach($hold in $holds) {
 $group=$hold.Groups[1].Value;$frame=$hold.Groups[2].Value

 $first=@($hits|Where-Object {$_.Groups[5].Value -eq $frame -and $_.Groups[6].Value -eq $group})
 if($first.Count -ne 1){throw "Emission group=$group has duplicated or missing initial native injury."}
 $encounter=$first[0].Groups[1].Value
 $members=@($owners|Where-Object {$_.Groups[1].Value -eq $group -and $ownerEncounters[$_.Index] -eq $encounter})
 if($members.Count -lt 2){continue} # Single shots and bounded owner-trace gaps are outside this proof.
 $start=[double]::Parse($hold.Groups[3].Value,[Globalization.CultureInfo]::InvariantCulture)
 $initial=[double]::Parse($first[0].Groups[7].Value,[Globalization.CultureInfo]::InvariantCulture)
 if([Math]::Abs($start-$initial) -gt .002){throw 'Hold is not synchronous with the native initial injury.'}
 foreach($hit in $hits|Where-Object {$_.Groups[1].Value -eq $encounter -and $_.Groups[6].Value -eq $group -and [long]$_.Groups[5].Value -gt [long]$frame}) {
  $now=[double]::Parse($hit.Groups[7].Value,[Globalization.CultureInfo]::InvariantCulture)
  if($now -lt $start+1-.0001){throw "Emission group=$group injured Yi during its paused-gameplay safety window."}
 }
 if(@($members|Group-Object {$_.Groups[3].Value+':'+$_.Groups[4].Value}|Where-Object {$_.Count -gt 1}).Count){throw 'Native emitted pieces repeat a root/generation identity.'}
 $tested++;$nativePieces+=$members.Count
}
if(!$tested){throw 'No injured multi-piece native emission was owner-proven.'}
Write-Output "PASS (scoped): multi-piece native emission windows=$tested owner-proven pieces=$nativePieces; one actual initial injury and no sibling injury within one paused gameplay second. Survivor rearming, independent contacts and unobserved owners are separate evidence."
