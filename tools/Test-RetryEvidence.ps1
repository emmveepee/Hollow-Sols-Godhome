param([Parameter(Mandatory)][Guid]$Session, [ValidateSet('GruzMother','Xero')][string]$Boss = 'GruzMother')
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$hk = Get-Content -LiteralPath "$workspace/.local/runs/HollowKnight/$Session/plugin.log" -Raw
$ns = Get-Content -LiteralPath "$workspace/.local/runs/NineSols/$Session/plugin.log" -Raw
if (($hk+$ns) -match 'LIFECYCLE_FAILED|HIT_FAILED|CONTACT_FAILED|NS_MEMORY_FAILED|HK_PROXY_FAILED|HK_GODHOME_PROBE_FAILED') { throw 'Retry failed.' }
$scene = if ($Boss -eq 'Xero') { 'GG_Ghost_Xero' } else { 'GG_Gruz_Mother' }
if (-not $hk.Contains("HK_NATIVE_STATUE_REENTRY scene=$scene")) { throw 'Second encounter did not use the native scene-entry observer.' }
$active = @([regex]::Matches($hk, 'ENCOUNTER_STATE role=HollowKnight encounter=([\w-]+) geometryFrame=(\d+) phase=Active'))
if ($active.Count -ne 2 -or $active[0].Groups[1].Value -eq $active[1].Groups[1].Value -or [long]$active[0].Groups[2].Value -ge [long]$active[1].Groups[2].Value) { throw 'Retry did not create a fresh encounter and geometry.' }
$id = $active[1].Groups[1].Value; $frame = $active[1].Groups[2].Value
if (-not $hk.Contains("HK_RETRY_HIT_VERIFIED encounter=$id") -or $ns -notmatch "ENCOUNTER_STATE role=NineSols encounter=$id geometryFrame=$frame phase=Active") { throw 'Second encounter not active on both peers.' }
if ($ns -notmatch "NS_RETRY_BEGIN geometryFrame=$frame nativeHealth=240") { throw 'Retry began without native full-health restoration.' }
if ($hk -notmatch 'HK_NATIVE_HIT sequence=1 kind=Melee nativeDamage=40 translated=21 before=650 after=629 applied=True (?:targetDead|dead)=False' -or $ns -notmatch 'NS_HOST_HIT_RESULT sequence=1 result=Applied health=629') { throw 'New native boss hit or its acknowledgement missing.' }
if ($hk -notmatch 'HK_NATIVE_HIT sequence=(\d+) kind=Bow[^\r\n]*after=(\d+) applied=True') { throw 'Retry bow outcome missing.' }
$bowSequence=$Matches[1]; $bowHealth=$Matches[2]
if ($ns -notmatch "NS_HOST_HIT_RESULT sequence=$bowSequence result=Applied health=$bowHealth") { throw 'Retry bow acknowledgement missing.' }
if (-not $hk.Contains("HK_PROXY_READY nativeHeroRetained=True nativeMovement=False geometryFrame=$frame")) { throw 'New native target proxy not initialized.' }
if (($ns | Select-String -Pattern 'NS_GEOMETRY_VERIFIED airborneToGrounded=True wallBlocked=True' -AllMatches).Matches.Count -ne 2) { throw 'Both native collision imports must pass.' }
foreach ($log in @($hk,$ns)) { if (-not $log.Contains('reason=retry-fixture-complete')) { throw 'Retry test did not finish cleanly on both peers.' } }
Write-Output 'PASS: native defeat/revival followed by a second native statue entry, fresh encounter/geometry/pipes, full-health Yi, verified terrain, new proxy and acknowledged melee/bow against a fresh 650-HP boss.'
