param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$hk = Get-Content -LiteralPath "$workspace/.local/runs/HollowKnight/$Session/plugin.log" -Raw
if ($hk -match 'HK_GODHOME_PROBE_FAILED|HK_NATIVE_BOSSES_DEAD|HK_ENCOUNTER_FIXTURE_HIT') { throw 'Native defeat audit failed or produced a victory.' }
if ($hk -notmatch 'HK_DEFEAT_REQUEST nativeDie=True bossHP=650 knightHP=9') { throw 'Protected native defeat request missing.' }
if ($hk -notmatch 'HK_DEFEAT_VERIFIED nativeReturn=True bossCompleted=False completionUnchanged=True controlRestored=True knightHP=9') { throw 'Native defeat return/control/progression verification missing.' }
Write-Output 'PASS: native dream defeat returns to the Hall, restores control and leaves boss completion unchanged without changing Knight HP.'
