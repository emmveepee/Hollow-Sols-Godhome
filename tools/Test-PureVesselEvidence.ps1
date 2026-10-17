param([Parameter(Mandatory)][Guid]$Session, [ValidateRange(0,1)][int]$Tier=0)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk = Get-BoundedRuntimeLog -Run "$workspace/.local/runs/HollowKnight/$Session"
$ns = Get-BoundedRuntimeLog -Run "$workspace/.local/runs/NineSols/$Session"
& "$PSScriptRoot/Test-HubCombatEvidence.ps1" -Session $Session -RequireReentry
$hp = if ($Tier -eq 0) {1600} else {1850}
$after = if ($Tier -eq 0) {186} else {132}
if ($hk -notmatch "HK_COMBAT_DIFFICULTY profile=PureVessel tier=$Tier health=$hp baseDamage=2") { throw 'Incorrect native Pure Vessel difficulty or HP.' }
if ($ns -notmatch "NS_DIFFICULTY_DAMAGE tier=$Tier before=240 after=$after expected=$after nativeCollision=True") { throw 'Native incoming tier damage not verified.' }
if ($hk -notmatch 'phase=Victory') { throw 'No Pure Vessel victory.' }
foreach ($kind in @('Melee','Bow','Talisman')) {
    if ($hk -notmatch "HK_NATIVE_HIT[^\r\n]*kind=$kind[^\r\n]*applied=True") { throw "Missing native $kind damage." }
}
if ($ns -notmatch 'NS_CONTACT_RESOLVED[^\r\n]*kind=parry') { throw 'Native Pure Vessel parry missing.' }
foreach ($r in [regex]::Matches($ns,'NS_TARGET_RESERVE capacity=([\d.]+) injuryBefore=([\d.]+) injuryAfter=([\d.]+)')) {
    if ($r.Groups[2].Value -ne $r.Groups[3].Value) { throw 'Reserve rebase changed internal injury.' }
}
Write-Output "PASS: Pure Vessel tier $Tier native HP, native incoming damage, melee/bow/talisman, parry, victory, repeated selection and visible returns."
