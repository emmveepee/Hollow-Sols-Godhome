param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
foreach($game in @('HollowKnight','NineSols')){
 if(Test-Path -LiteralPath "$root/.local/runs/$game/$Session/native-integration-failure.txt") {throw "Preserved native integration failure in $game for session $Session."}
 $log=Get-BoundedRuntimeLog "$root/.local/runs/$game/$Session"
 $failure=[regex]::Match($log,'(?m)^\S+ (?:SESSION_TERMINAL reason=(?:HIT_FAILED|CONTACT_FAILED|LIFECYCLE_FAILED|NS_INPUT_FAILED|NS_MEMORY_FAILED|NS_VISUAL_FAILED|HK_PROXY_FAILED|HK_HUB_FAILED)|HK_TARGET_FIXTURE_FAILED|NS_WALL_FIXTURE_FAILED|(?:CONTACT|VISUAL|HUD|BOSS_HUD|SUMMON_UI|WALL_ART|ATTACK_PALETTE|RAGE_MASK|RAGE_OUTLINE)_STOP reason=(?:InvalidDataException|InvalidOperationException))[^\r\n]*')
 if($failure.Success){throw "Unexpected native failure in $game`: $($failure.Value). Paired capture closure alone is not a successful combat test."}
 if($log -match 'HK_GODHOME_PROBE_FAILED|SESSION_TERMINAL reason=DIAGNOSTIC_COMPLETION_FAILED') {throw 'Native Godhome lifecycle/completion probe failed; encounter completion is unproven.'}
 if($log -match 'HK_NAILMASTER_PAIRED_DISPATCH_REJECTED') {throw 'Native paired dispatch contract was rejected; synchronized behavior is unproven.'}
}
Write-Output "PASS (failure gate): session=$Session no logged native integration/fixture/contract failure. Deadline/disconnect/combat coverage remain separate."
