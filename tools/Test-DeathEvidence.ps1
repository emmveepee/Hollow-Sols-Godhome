param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$ns = Get-Content -LiteralPath "$workspace/.local/runs/NineSols/$Session/plugin.log" -Raw
if ($ns -match 'NS_MEMORY_FAILED|NS_INPUT_FAILED') { throw 'Native death/revival audit failed.' }
if ($ns -notmatch 'NS_DEATH_OBSERVED player=(-?\d+) health=0 state=Dead') { throw 'Native Yi death missing.' }
$playerId=$Matches[1]
$ns = $ns.Substring($ns.IndexOf('NS_DEATH_OBSERVED'))
if ($ns -notmatch "NS_REVIVE_VERIFIED player=$playerId health=240 ammo=5 potions=8 chi=0 scene=A2_S5_BossHorseman_Final") { throw 'Native full revival in the same memory encounter missing.' }
foreach($method in @('PlayerDeadState.Revive','Player.RestoreEverything')) {
    if (-not $ns.Contains("phase=before method=$method")) { throw "Native lifecycle callback missing: $method" }
}
if (-not $ns.Contains('phase=before method=GameCore.ResetLevel') -and -not $ns.Contains('phase=before method=Player.RespawnAtSavePoint')) { throw 'Neither native revival route was observed after death.' }
Write-Output 'PASS: native incoming lethal damage, Yi death, native revival and restoration to full health/ammo/potions with cleared Qi. Cross-game death/retry remains unverified.'
