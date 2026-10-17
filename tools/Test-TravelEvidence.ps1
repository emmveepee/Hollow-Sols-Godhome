[CmdletBinding()]
param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog -Run "$root/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog -Run "$root/.local/runs/NineSols/$Session"
if(($hk+$ns) -match '_FAILED(?:\s|$)' -or $hk -match 'SESSION_TERMINAL'){throw 'Travel session failed.'}
foreach($marker in @('HK_TRAVEL_ROOT_VERIFIED','HK_TRAVEL_FIXTURE_CANCEL','HK_DEBUG_TRAVEL_REQUEST destination=GG_Workshop cancelEncounter=True',
 'HK_DEBUG_TRAVEL_COMPLETE destination=GG_Workshop nativeReturnAlreadyArrived=True','HK_TRAVEL_HALL_RETRY_RELEASE drained=True','HK_TRAVEL_HALL_RETRY_ENTERED nativeStatue=True','HK_DEBUG_TRAVEL_REQUEST destination=GG_Atrium cancelEncounter=True',
 'HK_ENCOUNTER_ADMISSION_CANCELLED scene=GG_Gruz_Mother state=GG_Extra_Pause nativeReturn=True',
 'HK_HUB_COMBAT_ENDED victory=False','HK_HUB_COMBAT_RETURN_READY','HK_DEBUG_TRAVEL_BEGIN destination=GG_Atrium nativeTransition=True pendingOutcomes=0',
 'HK_DEBUG_TRAVEL_BEGIN destination=GG_Workshop nativeTransition=True pendingOutcomes=0','HK_TRAVEL_FIXTURE_COMPLETE nativeCancellation=True aliveYi=True hubHallRoundTrip=True')) {
 if(-not $hk.Contains($marker)){throw "Missing travel evidence: $marker"}
}
if($hk -match 'HK_HUB_COMBAT_ENDED victory=True' -or $ns -match 'NS_HEALTH_DIED'){throw 'Cancellation masqueraded as victory or death.'}
if($ns -notmatch 'NS_RETURN_PREPARE_EARLY peerPhase=Defeated pendingOutcomes=0' -or $ns -notmatch 'NS_HUB_COMBAT_RESET victory=False'){throw 'Alive cancellation did not use drained native reset.'}
$finished=$hk.IndexOf('HK_TRAVEL_FIXTURE_COMPLETE')
foreach($m in [regex]::Matches($ns,'SESSION_TERMINAL reason=([^ ]+)')) {
 if($m.Groups[1].Value -notin @('VISUAL_CHANNEL_STOPPED','RAGE_MASK_CHANNEL_STOPPED','PEER_DISCONNECTED') -or -not $hk.Contains('SMOKE_EXIT_REQUEST game=HollowKnight')){throw 'Unexpected peer terminal.'}
 $line=$ns.Substring(0,$m.Index).Split("`n")[-1]
 $doneLine=$hk.Substring(0,$finished).Split("`n")[-1]
 if([DateTime]::Parse($line.Split(' ')[0]) -le [DateTime]::Parse($doneLine.Split(' ')[0])){throw 'Peer failed before travel completion.'}
}
Write-Output "PASS ${Session}: alive native encounter cancellation, drained reset/visible return, ordinary hub/Hall transition callbacks. Physical F9/pointer acceptance remains separate."
