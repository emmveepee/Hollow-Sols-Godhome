param([Parameter(Mandatory)][Guid]$Session, [ValidateSet('Melee','Bow','Talisman')][string[]]$RequiredKinds = @('Melee','Bow','Talisman'))
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$hk = Get-Content -LiteralPath "$workspace/.local/runs/HollowKnight/$Session/plugin.log" -Raw
$ns = Get-Content -LiteralPath "$workspace/.local/runs/NineSols/$Session/plugin.log" -Raw
if (($hk + $ns) -match 'HIT_FAILED|CONTACT_FAILED|NS_MEMORY_FAILED|NS_INPUT_FAILED|HK_PROXY_FAILED|LIFECYCLE_FAILED|HK_GODHOME_PROBE_FAILED') { throw 'Outgoing native hit diagnostic failed.' }
foreach ($marker in @('HK_HIT_ENCOUNTER_COMPLETE nativeBossDead=True proxyReleased=True','HK_ENCOUNTER_RETURN native=True bossDead=True')) { if (-not $hk.Contains($marker)) { throw "Missing native victory evidence: $marker" } }
if ($hk -notmatch 'HK_NATIVE_BOSSES_DEAD' -or $ns -notmatch 'ENCOUNTER_STATE role=NineSols[^\r\n]*phase=Restoring') { throw 'Native encounter verdict/restoration missing.' }
$hits = @([regex]::Matches($hk,'HK_NATIVE_HIT sequence=(\d+) kind=(\w+) nativeDamage=([\d.E+-]+) translated=(\d+) before=(\d+) after=(\d+) applied=(True|False) (?:targetDead|dead)=(True|False)'))
if ($hits.Count -lt 5) { throw 'Insufficient native host hits.' }
$health = 650
$ids = [Collections.Generic.HashSet[string]]::new()
$kinds = [Collections.Generic.HashSet[string]]::new()
foreach ($h in $hits) {
    $id=$h.Groups[1].Value; $kind=$h.Groups[2].Value; $damage=$h.Groups[3].Value; $before=[int]$h.Groups[5].Value; $after=[int]$h.Groups[6].Value
    if (-not $ids.Add($id) -or $before -ne $health -or $after -gt $before) { throw 'Host native hit repeated or health continuity changed.' }
    if ($ns -notmatch "NS_HIT_OUTCOME sequence=$id frame=\d+ entity=-?\d+ kind=$kind damage=$([regex]::Escape($damage))(?: encounter=[\w-]+ activation=\d+ attack=\d+)?\r?\n") { throw 'Host hit missing matching native outcome.' }
    if ($ns -notmatch "NS_HOST_HIT_RESULT sequence=$id result=(Applied|Refused) health=$after\r?\n") { throw 'Host hit missing matching acknowledgement.' }
    if ($h.Groups[7].Value -eq 'True') { $null=$kinds.Add($kind); if ($after -ge $before) { throw 'Accepted hit did not damage host.' } }
    $health=$after
}
if ($health -ne 0) { throw 'Native host boss survived diagnostic.' }
foreach($kind in $RequiredKinds) { if (-not $kinds.Contains($kind)) { throw "Native $kind outcome not applied to host." } }
foreach ($marker in @('NS_VICTORY_RESET','NS_REVIVAL_READY health=240 ammo=5 potions=8 chi=')) {
    if (-not $ns.Contains($marker)) { throw "Missing native victory restoration: $marker" }
}
if ($ns.Contains('NS_WARM_RESET_ARMED')) { & "$PSScriptRoot/Test-WarmSceneEvidence.ps1" -Session $Session }
elseif (-not $ns.Contains('NS_REVIVAL_SCENE scene=A2_S5_BossHorseman_Final')) { throw 'Native restoration route missing.' }
# Native RestoreEverything fills health, ammo and medicine, but preserves Qi.
if ($ns -match 'NS_VICTORY_RESET[^\r\n]*chi=([\d.]+)') {
    $earnedQi = $Matches[1]
    if ($ns -notmatch "NS_REVIVAL_READY health=240 ammo=5 potions=8 chi=$([regex]::Escape($earnedQi))\r?\n") { throw 'Victory restoration changed earned native Qi.' }
}
if ($ns.IndexOf('NS_REVIVAL_READY') -lt $ns.IndexOf('NS_VICTORY_RESET')) { throw 'Restoration preceded the victory reset.' }
if ($hk -notmatch 'ENCOUNTER_STATE role=HollowKnight encounter=([\w-]+) geometryFrame=(\d+) phase=Returned') { throw 'Native victory did not restore Hall control.' }
$encounter=$Matches[1]; $geometry=$Matches[2]
if ($ns -notmatch "ENCOUNTER_STATE role=NineSols encounter=$encounter geometryFrame=$geometry phase=Ready") { throw 'Victory restoration did not acknowledge the matching encounter.' }
Write-Output "PASS: $($hits.Count) ordered native outcomes/host hits/acknowledgements; observed kinds=$($kinds -join ','); native boss death, Hall control and full native Yi restoration."
