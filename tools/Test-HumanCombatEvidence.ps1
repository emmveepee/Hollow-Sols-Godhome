param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$hk = Get-Content -LiteralPath "$workspace/.local/runs/HollowKnight/$Session/plugin.log" -Raw
$ns = Get-Content -LiteralPath "$workspace/.local/runs/NineSols/$Session/plugin.log" -Raw
if (($hk+$ns) -match 'LIFECYCLE_FAILED|HIT_FAILED|CONTACT_FAILED|NS_MEMORY_FAILED|NS_INPUT_FAILED|HK_PROXY_FAILED|HK_GODHOME_PROBE_FAILED|NS_VISUAL_FAILED|HK_VISUAL_FAILED') { throw 'Physical combat adapter failed; inspect session logs.' }
if (-not $hk.Contains('HK_INPUT_SOURCE mode=physical') -or $hk.Contains('HK_INPUT_SCRIPT_BEGIN') -or $ns.Contains('NS_TARGET_BODY_ISOLATED')) { throw 'Physical combat was replaced or altered by a scripted fixture.' }
if (-not $hk.Contains('HK_PROXY_ARENA_ENTRY_VERIFIED') -or -not $ns.Contains('NS_GEOMETRY_VERIFIED')) { throw 'Physical combat arena did not initialize.' }
$hits = [regex]::Matches($hk,'HK_NATIVE_HIT sequence=(\d+) kind=(\w+)[^\r\n]*applied=True')
$kinds = @($hits | ForEach-Object { $_.Groups[2].Value } | Sort-Object -Unique)
$victories = [regex]::Matches($hk,'HK_HIT_ENCOUNTER_COMPLETE nativeBossDead=True').Count
$defeats = [regex]::Matches($hk,'HK_DEFEAT_VERIFIED').Count
$retries = [regex]::Matches($ns,'NS_RETRY_BEGIN').Count
$inputEdges = [regex]::Matches($ns,'NS_INPUT_EDGE').Count
Write-Output "Observed physical session $Session`: native input edges=$inputEdges; applied hits=$($hits.Count); kinds=$($kinds -join ','); victories=$victories; verified defeats=$defeats; retries=$retries."
Write-Output 'No adapter failure found. Controller feel, visible effects, talisman timing and retry-button behavior require the player report; telemetry alone is not a playability pass.'
