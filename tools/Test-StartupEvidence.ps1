param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$hk = Get-Content -LiteralPath "$workspace/.local/runs/HollowKnight/$Session/plugin.log" -Raw
$ns = Get-Content -LiteralPath "$workspace/.local/runs/NineSols/$Session/plugin.log" -Raw
if (($hk+$ns) -match 'HK_ENCOUNTER_GATE_FAILED|NS_MEMORY_FAILED|LIFECYCLE_FAILED') { throw 'Encounter startup failed.' }
$holds = @([regex]::Matches($hk,'([^\r\n ]+) HK_ENCOUNTER_HELD '))
$releases = @([regex]::Matches($hk,'([^\r\n ]+) HK_ENCOUNTER_RELEASED waitedSeconds=([\d.]+)'))
$nativeActive = @([regex]::Matches($ns,'([^\r\n ]+) ENCOUNTER_STATE role=NineSols encounter=[\w-]+ geometryFrame=\d+ phase=Active'))
if ($holds.Count -eq 0 -or $holds.Count -ne $releases.Count -or $nativeActive.Count -ne $releases.Count) { throw 'Every encounter must be held and released after native combat readiness.' }
for($i=0; $i -lt $holds.Count; $i++) {
    if ($holds[$i].Index -ge $releases[$i].Index -or [DateTimeOffset]::Parse($nativeActive[$i].Groups[1].Value) -gt [DateTimeOffset]::Parse($releases[$i].Groups[1].Value)) { throw 'Host released before native readiness.' }
}
if ($ns.Contains('NS_ENEMY_ENTRY_TARGET') -or -not $ns.Contains('NS_ENEMY_DIRECT_READY')) { throw 'Native introduction was used or direct initialization missing.' }
Write-Output "PASS: $($holds.Count) encounters waited for native combat readiness; initialized native owners were acquired without the walk/intro."
