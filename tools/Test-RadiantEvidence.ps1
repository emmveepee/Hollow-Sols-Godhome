param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$hk = Get-Content -LiteralPath "$workspace/.local/runs/HollowKnight/$Session/plugin.log" -Raw
$ns = Get-Content -LiteralPath "$workspace/.local/runs/NineSols/$Session/plugin.log" -Raw
if (($hk+$ns) -match 'LIFECYCLE_FAILED|HIT_FAILED|CONTACT_FAILED|NS_MEMORY_FAILED|NS_INPUT_FAILED|HK_PROXY_FAILED|HK_HUB_FAILED|HK_GODHOME_PROBE_FAILED') { throw 'Radiant adapter failed.' }
if ($hk -notmatch 'HK_HUB_COMBAT_STATUE_SELECT tier=2') { throw 'Native Radiant selection missing.' }
if ($hk -notmatch 'HK_COMBAT_DIFFICULTY profile=PureVessel tier=2 health=1850 baseDamage=2') { throw 'Native Radiant HP/difficulty missing.' }
if ($ns -notmatch 'NS_RADIANT_PROBE_BEGIN nativeJade=True charge=True' -or $ns -notmatch 'NS_RADIANT_REVIVED health=1 charge=False nativeCollision=True' -or $ns -notmatch 'NS_RADIANT_SECOND_HIT_DEAD charge=False') { throw 'Native Radiant revival/second-hit death missing.' }
$start = $ns.IndexOf('NS_RADIANT_PROBE_BEGIN')
$end = $ns.IndexOf('NS_RADIANT_SECOND_HIT_DEAD', $start)
if ($ns.Substring($start,$end-$start) -match 'NS_CHEAT_GRANT') { throw 'Resource refills contaminated the revival test.' }
if ($ns -notmatch 'NS_RADIANT_NO_JADE_DEAD before=240 after=0 activated=False state=Dead nativeCollision=True') { throw 'Full-health Radiant death without jade missing.' }
if ($hk -notmatch 'HK_DEFEAT_REQUEST nativeDie=True' -or $hk -notmatch 'HK_HUB_COMBAT_RETURN_READY[^\r\n]*presented=True nativeWakeComplete=True') { throw 'Native defeat and visible Hall return missing.' }
if ([regex]::Matches($hk,'HK_HUB_COMBAT_RETURN_READY[^\r\n]*presented=True nativeWakeComplete=True').Count -lt 2) { throw 'Both Radiant visible returns are required.' }
Write-Output 'PASS: native Radiant selection, first collision revival consuming jade charge, second collision death without refills, full-health death without jade, native defeats and visible returns.'
