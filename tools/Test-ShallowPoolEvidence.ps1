param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$log = Get-Content -LiteralPath "$workspace/.local/runs/HollowKnight/$Session/plugin.log" -Raw
if ($log -match 'HK_HUB_POOL_FAILED|HK_HUB_FAILED') { throw 'Hub pool diagnostic failed.' }
$floor = [regex]::Match($log, 'HK_HUB_SHALLOW_WATER left=([\d.]+) right=([\d.]+) surface=([\d.]+) floor=([\d.]+) depth=([\d.]+)')
$entry = [regex]::Match($log, 'HK_HUB_POOL_ENTER x=([\d.]+) y=([\d.]+) nativeInput=True')
if (!$floor.Success -or !$entry.Success -or $log -notmatch 'HK_HUB_POOL_EXIT .*nativeInput=True') { throw 'Shallow floor or native traversal evidence missing.' }
$culture = [Globalization.CultureInfo]::InvariantCulture
function Number($group) { [double]::Parse($group.Value, $culture) }
if ([Math]::Abs((Number $floor.Groups[3]) - (Number $floor.Groups[4]) - 0.2) -gt 0.001) { throw 'Incorrect water depth.' }
if ([Math]::Abs((Number $entry.Groups[2]) - (Number $floor.Groups[4])) -gt 0.03) { throw 'Yi did not stand on the shallow floor.' }
if ((Number $entry.Groups[1]) -le (Number $floor.Groups[1]) -or (Number $entry.Groups[1]) -ge (Number $floor.Groups[2])) { throw 'Probe outside water extent.' }
Write-Output 'PASS: native Yi grounded within 0.03 units of the 0.2-deep pool floor and exited using native input. Final appearance remains a player check.'
