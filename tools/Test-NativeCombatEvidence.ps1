[CmdletBinding()]
param([Parameter(Mandatory)][Guid]$Session, [switch]$PausedTarget, [switch]$ContactProbe)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$root = "$workspace/.local/runs/NineSols/$Session"
$log = Get-Content -LiteralPath "$root/plugin.log" -Raw
$boot = Get-Content -LiteralPath "$root/bootstrap.log" -Raw
if ($boot -notmatch "BOOTSTRAP_GUARD_VERIFIED session=$Session" -or $boot -match 'FATAL' -or $log -match 'NS_MEMORY_FAILED|NS_INPUT_FAILED') { throw 'Protected native combat session failed.' }
foreach ($marker in @('NS_MEMORY_READY index=3 memory=True player=True','NS_ENEMY_REQUEST point=VR_Challenge_A2_S5 rawScene=A2_S5_BossHorseman_Final',
    'NS_ENEMY_ENTRY_TARGET','NS_ENEMY_READY name=StealthGameMonster_SpearHorseMan','SMOKE_EXIT_REQUEST')) {
    if (-not $log.Contains($marker)) { throw "Native combat evidence missing: $marker" }
}
$parries = @([regex]::Matches($log,'phase=after method=PlayerParryState.Parried[^\r\n]*result=True[^\r\n]*accurate=True'))
if ($parries.Count -lt 2) { throw 'Repeated native accurate parries were not observed.' }
$deposit = [regex]::Match($log,'phase=after method=FooAttachable.OnDeposit instance=(-?\d+)')
if (-not $deposit.Success -or $log -notmatch "phase=after method=FooAttachable.OnExplode instance=$($deposit.Groups[1].Value) ") { throw 'Native deposit and explosion on the same target were not observed.' }
$damagePairs = @([regex]::Matches($log,'phase=before method=PostureSystem.TakeDamage instance=(-?\d+)[^\r\n]*posture=([\d.]+)[^\r\n]*\r?\n[^\r\n]*phase=after method=PostureSystem.TakeDamage instance=\1[^\r\n]*posture=([\d.]+)'))
$accepted = @($damagePairs | Where-Object { [double]::Parse($_.Groups[3].Value,[Globalization.CultureInfo]::InvariantCulture) -lt [double]::Parse($_.Groups[2].Value,[Globalization.CultureInfo]::InvariantCulture) })
if ($accepted.Count -lt 2) { throw 'Native target damage deltas missing.' }
if ($log -notmatch 'phase=after method=PlayerHealth.ReceiveDamage[^\r\n]*result=True health=(?!240)[\d.]+') { throw 'Native incoming health damage missing.' }
$heal = [regex]::Match($log,'NS_ENEMY_HEAL_VERIFIED before=([\d.]+) after=([\d.]+) potions=7')
if (-not $heal.Success -or [double]$heal.Groups[2].Value -le [double]$heal.Groups[1].Value -or $log -notmatch 'NS_INPUT_NATIVE[^\r\n]*state=DrinkPotion') { throw 'Native healing and potion use missing.' }
if ($PausedTarget) {
    $pausedAt = $log.IndexOf('NS_ENEMY_HEAL_FIXTURE')
    $hits = @($accepted | Where-Object { $_.Index -gt $pausedAt -and $_.Value -match 'state=Attack ' })
    if ($pausedAt -lt 0 -or $hits.Count -lt 3) { throw 'Repeated accepted melee damage on the paused target is unverified.' }
}
if ($ContactProbe) {
    if ($log -notmatch 'NS_ENEMY_DUPLICATE_REJECTED') { throw 'Same-frame native duplicate rejection missing.' }
    $windows = @([regex]::Matches($log,'NS_CONTACT_PLACEMENT active=True[^\r\n]*health=(\d+)[^\r\n]*chi=(\d+)([\s\S]*?)NS_CONTACT_PLACEMENT active=False[^\r\n]*health=(\d+)[^\r\n]*chi=(\d+)'))
    if ($windows.Count -ne 3) { throw 'Three translated contact windows required.' }
    if ([int]$windows[0].Groups[4].Value -ge [int]$windows[0].Groups[1].Value -or $windows[0].Groups[3].Value -notmatch 'method=PlayerHealth.ReceiveDamage[^\r\n]*result=True') { throw 'Translated undefended hit missing.' }
    if ($windows[1].Groups[4].Value -ne $windows[1].Groups[1].Value -or [int]$windows[1].Groups[5].Value -ne [int]$windows[1].Groups[2].Value + 1 -or $windows[1].Groups[3].Value -notmatch 'method=PlayerParryState.Parried[^\r\n]*result=True[^\r\n]*accurate=True') { throw 'Translated native accurate parry missing.' }
    if ([int]$windows[2].Groups[4].Value -ge [int]$windows[2].Groups[1].Value -or $windows[2].Groups[3].Value -notmatch 'method=PlayerHealth.ReceiveDamage[^\r\n]*result=True') { throw 'Early-parry damage outcome missing.' }
    Write-Output 'PASS: translated native attack collider produced damage, accurate parry/Qi, and early-parry damage with same-frame duplicate rejection.'
}
Write-Output "PASS: native enemy entry; $($parries.Count) accurate parries; same-target talisman deposit/explosion; $($accepted.Count) native damage deltas; incoming damage and healing 8->7. No cross-game combat or imprecise-parry claim."
