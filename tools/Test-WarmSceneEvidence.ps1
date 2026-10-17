param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$ns = Get-Content -LiteralPath "$workspace/.local/runs/NineSols/$Session/plugin.log" -Raw
if ($ns -match 'NS_MEMORY_FAILED|LIFECYCLE_FAILED|CONTACT_FAILED|HIT_FAILED|NS_INPUT_FAILED|NS_VISUAL_FAILED') { throw 'Native warm-scene run reported a failure.' }
$arms = @([regex]::Matches($ns, 'NS_WARM_RESET_ARMED victory=(True|False) sceneHandle=(-?\d+) player=(-?\d+)'))
$resets = @([regex]::Matches($ns, 'NS_WARM_RESET_COMPLETED reloaded=False sceneHandle=(-?\d+) player=(-?\d+)'))
if ($arms.Count -eq 0 -or $arms.Count -ne $resets.Count) { throw 'Warm resets are missing or unbalanced.' }
$identity = $arms[0].Groups[2].Value + ':' + $arms[0].Groups[3].Value
for ($i=0; $i -lt $arms.Count; $i++) {
    if ($arms[$i].Index -ge $resets[$i].Index -or ($arms[$i].Groups[2].Value + ':' + $arms[$i].Groups[3].Value) -ne $identity -or ($resets[$i].Groups[1].Value + ':' + $resets[$i].Groups[2].Value) -ne $identity) { throw 'Scene/player identity or reset ordering changed.' }
}
if ([regex]::Matches($ns,'SCENE_READY game=NineSols scene=A2_S5_BossHorseman_Final ').Count -ne 1 -or $ns.Contains('NS_REVIVAL_SCENE')) { throw 'Native scene was loaded more than once.' }
if ([regex]::Matches($ns,'NS_ENEMY_REUSE scene=').Count -ne $resets.Count -or [regex]::Matches($ns,'NS_REVIVAL_READY health=240 ammo=5 potions=8 chi=').Count -ne $resets.Count) { throw 'Reset did not restore native resources and reacquire the combat owner.' }
if ($ns.Contains('NS_ENEMY_ENTRY_TARGET')) { throw 'Native boss introduction was used.' }
$owners = @([regex]::Matches($ns,'NS_CONTACT_OWNER_READY owner=(-?\d+) '))
if ($owners.Count -ne $resets.Count + 1 -or @($owners | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique).Count -ne 1) { throw 'Warm reset replaced or failed to reacquire the scene-authored combat owner.' }
Write-Output "PASS: $($resets.Count) native resets reused the same scene and player, reset native pools, restored resources and reacquired the initialized combat graph without a scene reload or introduction."
