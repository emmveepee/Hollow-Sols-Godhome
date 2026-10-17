[CmdletBinding()]
param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$ns = Get-Content -LiteralPath "$workspace/.local/runs/NineSols/$Session/plugin.log" -Raw
$hk = Get-Content -LiteralPath "$workspace/.local/runs/HollowKnight/$Session/plugin.log" -Raw
if ($hk -notmatch 'HK_INPUT_SCRIPT_COMPLETE .*extendedActions=True') { throw 'Extended native action sequence did not complete.' }
foreach ($action in @('Heal','WeaponAttack','FooAttack')) {
    if ($ns -notmatch "NS_INPUT_EDGE .*action=$action pressed=True" -or $ns -notmatch "NS_INPUT_EDGE .*action=$action pressed=False released=True") { throw "Missing native $action edge pair." }
}
foreach ($state in @('DrinkPotion','ShootArrow')) {
    if ($ns -notmatch "NS_INPUT_NATIVE .*state=$state ") { throw "Native $state not reached." }
}
$resources = @([regex]::Matches($ns,'NS_INPUT_RESOURCES id=(\d+) frame=(\d+) ammo=([\d.]+) heals=(\d+) chi=([\d.]+) health=([\d.]+)'))
if ($resources.Count -lt 3) { throw 'Insufficient resource changes.' }
$first = $resources[0]; $last = $resources[-1]
$ammoBefore = [float]::Parse($first.Groups[3].Value,[Globalization.CultureInfo]::InvariantCulture)
$ammoAfter = [float]::Parse($last.Groups[3].Value,[Globalization.CultureInfo]::InvariantCulture)
$healsBefore = [int]$first.Groups[4].Value; $healsAfter = [int]$last.Groups[4].Value
if ($ammoAfter -ge $ammoBefore -or $ammoAfter -lt 0 -or $healsAfter -ne $healsBefore - 1) { throw 'Native bow/potion consumption did not match requests.' }
foreach ($sample in $resources) { if ([float]::Parse($sample.Groups[5].Value,[Globalization.CultureInfo]::InvariantCulture) -ne 0) { throw 'Zero-chi fixture changed unexpectedly.' } }
if ($ns -match 'NS_INPUT_NATIVE .*state=FooAttack ') { throw 'Talisman activated without native chi.' }
Write-Output "PASS: native heal/bow/talisman edge pairs; DrinkPotion ($healsBefore->$healsAfter) and ShootArrow ($ammoBefore->$ammoAfter) consume native resources; zero-chi talisman request stays refused. Attachment/explosion and actual health restoration remain untested."
