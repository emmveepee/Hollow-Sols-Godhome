param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$log = Get-Content -LiteralPath "$workspace/.local/runs/NineSols/$Session/plugin.log" -Raw
if ($log -match 'NS_MEMORY_FAILED|CONTACT_FAILED') { throw 'Independent native attack fixture failed.' }
if ($log -notmatch 'NS_DEALER_GRAPH dealer=[^\r\n]*YiGodhome Native Attack/SlashDamage\(Clone\) parry=[^\r\n]*YiGodhome Native Attack/Parriable\(Clone\)') { throw 'Independent attack/parry graph missing.' }
foreach ($method in @('PlayerHealth.ReceiveDamage','PlayerParryState.Parried')) {
    if ($log -notmatch "phase=after method=$([regex]::Escape($method))[^\r\n]*result=True[^\r\n]*dealer=SlashDamage\(Clone\)") { throw "Missing native cloned-attack result: $method" }
}
if ($log -notmatch 'phase=after method=PlayerParryState.Parried[^\r\n]*accurate=True[^\r\n]*dealer=SlashDamage\(Clone\)') { throw 'Accurate native parry missing.' }
Write-Output 'PASS: independently initialized native attack graph delivered native damage and accurate parry. This isolated test does not establish host projectile placement.'
