param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$hk = Get-Content -LiteralPath "$workspace/.local/runs/HollowKnight/$Session/plugin.log" -Raw
$ns = Get-Content -LiteralPath "$workspace/.local/runs/NineSols/$Session/plugin.log" -Raw
if (($hk+$ns) -match 'LIFECYCLE_FAILED|HIT_FAILED|CONTACT_FAILED|NS_MEMORY_FAILED|HK_PROXY_FAILED|HK_GODHOME_PROBE_FAILED') { throw 'Paired lifecycle failed.' }
foreach ($marker in @('NS_YI_DEFEAT_PUBLISHED health=0 oldAdaptersReleased=True','NS_REVIVAL_CLEANUP nativeDead=True geometryReleased=True ownerReleased=True','NS_REVIVAL_READY health=240 ammo=5 potions=8 chi=0')) {
    if (-not $ns.Contains($marker)) { throw "Missing native revival evidence: $marker" }
}
if ($ns.Contains('NS_WARM_RESET_ARMED')) {
    if (-not $ns.Contains('NS_WARM_QUICK_REVIVE nativeBranch=True')) { throw 'Native quick-revival branch missing.' }
    & "$PSScriptRoot/Test-WarmSceneEvidence.ps1" -Session $Session
}
elseif (-not $ns.Contains('NS_REVIVAL_SCENE scene=A2_S5_BossHorseman_Final')) { throw 'Native restoration route missing.' }
foreach ($marker in @('HK_YI_DEFEAT_ACCEPTED nativeHealthAuthority=True proxyReleased=True','HK_DEFEAT_VERIFIED nativeReturn=True bossCompleted=False completionUnchanged=True controlRestored=True knightHP=9')) {
    if (-not $hk.Contains($marker)) { throw "Missing host defeat evidence: $marker" }
}
if ($hk -notmatch 'ENCOUNTER_STATE role=HollowKnight encounter=([\w-]+) geometryFrame=(\d+) phase=Returned') { throw 'Host did not acknowledge native Hall return.' }
$encounter = $Matches[1]; $geometry = $Matches[2]
if ($ns -notmatch "ENCOUNTER_STATE role=NineSols encounter=$encounter geometryFrame=$geometry phase=Ready") { throw 'Restored Yi and host encounter identities differ.' }
Write-Output 'PASS: real translated contact killed native Yi, both adapters cleaned up, native revival restored resources, HK native defeat returned to Hall with control and no completion reward, and matching lifecycle readiness was acknowledged.'
