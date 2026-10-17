param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$workspace=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog "$workspace/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog "$workspace/.local/runs/NineSols/$Session"
foreach($game in @('HollowKnight','NineSols')) {
 if(-not(Test-Path -LiteralPath "$workspace/.local/runs/$game/$Session/invulnerable-probe.request")){throw 'Missing explicit paired victory-only capability.'}
}
if(($hk+$ns) -match 'LIFECYCLE_FAILED|HIT_FAILED|CONTACT_FAILED|NS_MEMORY_FAILED|NS_INPUT_FAILED|HK_PROXY_FAILED|HK_HUB_FAILED|HK_GODHOME_PROBE_FAILED|HK_DEFEAT_REQUEST|NS_CONTACT_RESOLVED id=\d+ kind=damage|NS_RADIANT_PROBE_BEGIN'){throw 'Victory-only fixture failed or mixed with damage/defeat checks.'}
if($hk -notmatch 'HK_HUB_COMBAT_STATUE_SELECT tier=2' -or $ns -notmatch 'NS_INVULNERABLE_VICTORY_BEGIN nativeVote=True tier=2'){throw 'Native Radiant selection or immunity lease missing.'}
$wins=[regex]::Matches($hk,'HK_HUB_COMBAT_ENDED victory=True').Count
$returns=[regex]::Matches($hk,'HK_HUB_COMBAT_RETURN_READY[^\r\n]*presented=True nativeWakeComplete=True').Count
if($wins -eq 0 -or $returns -eq 0 -or $ns -notmatch 'NS_INVULNERABLE_VICTORY_END nativeVoteRemoved=True'){throw 'Native victory, visible return or released immunity lease missing.'}
Write-Output "PASS (victory-only): session=$Session nativeVictories=$wins visibleReturns=$returns nativeImmunityLease=True. This run does not certify damage, defenses, defeat or performance."
