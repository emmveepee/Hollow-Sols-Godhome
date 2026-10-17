param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
& "$PSScriptRoot/Test-HubCombatEvidence.ps1" -Session $Session -RequireReentry
$hk=Get-Content -LiteralPath "$root/.local/runs/HollowKnight/$Session/plugin.log" -Raw
$ns=Get-Content -LiteralPath "$root/.local/runs/NineSols/$Session/plugin.log" -Raw
$scenes=@([regex]::Matches($hk,'HK_HUB_COMBAT_BEGIN scene=([^ ]+) geometry=') | ForEach-Object {$_.Groups[1].Value} | Select-Object -Unique)
if($scenes.Count -ne 2 -or $hk -notmatch 'HK_BOSS_SWITCH_FIXTURE from=VengeflyKing to=GruzMother nativeStatue=True priorReturned=True'){throw 'Two different native encounters required.'}
if([regex]::Matches($ns,'NS_SUMMON_DISPOSE retained=0 active=0 pending=0').Count -lt 2){throw 'Native receiver teardown did not return to zero after each boss.'}
Write-Output "PASS: Vengefly to Gruz through native statues, fresh encounter/target identities, acknowledged damage and visible returns; both receiver pools disposed. Session=$Session."
