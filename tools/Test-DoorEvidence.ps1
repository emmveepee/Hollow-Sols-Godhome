param([Parameter(Mandatory)][Guid]$Session, [switch]$RequireFront)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$ns = Get-Content -LiteralPath "$workspace/.local/runs/NineSols/$Session/plugin.log" -Raw
$hk = Get-Content -LiteralPath "$workspace/.local/runs/HollowKnight/$Session/plugin.log" -Raw
if ($ns -match 'NS_MEMORY_FAILED|NS_INPUT_FAILED' -or $hk -match 'HK_HUB_FAILED|HK_VISUAL_FAILED') { throw 'Runtime failed during entrance test.' }
if ($hk -notmatch 'HK_ROOM_FADE_HELD scene=GG_Workshop gate=left1' -or $hk -notmatch 'HK_ROOM_FADE_RELEASED geometry=\d+ nativeImage=True') { throw 'Missing image-gated native fade.' }
if ($ns -notmatch 'NS_ROOM_ENTRY_STARTED style=2 state=MoveTo' -or $ns -notmatch 'NS_ROOM_ENTRY_COMPLETE style=2') { throw 'Native side entrance did not complete.' }
$walk = @([regex]::Matches($ns,'NS_ROOM_ENTRY_SAMPLE state=MoveTo position=\(([-\d.]+), ([-\d.]+),'))
if ($walk.Count -lt 5) { throw 'Too few native walking samples.' }
if ([double]$walk[-1].Groups[1].Value - [double]$walk[0].Groups[1].Value -lt 20) { throw 'Native walk did not move into the room.' }
foreach ($sample in $walk) { if ([Math]::Abs([double]$sample.Groups[2].Value - [double]$walk[0].Groups[2].Value) -gt 0.2) { throw 'Native entry left the floor.' } }
if ($RequireFront) {
    if ($ns -notmatch 'NS_ROOM_ENTRY_STARTED style=1 state=ZExit' -or $ns -notmatch 'NS_ROOM_ENTRY_COMPLETE style=1') { throw 'Native front entrance did not complete.' }
    if ($hk -notmatch 'HK_KNIGHT_PARTICLES_HIDDEN count=[1-9]\d* cameraMasksUnchanged=True') { throw 'Knight particle isolation missing.' }
}
Write-Output "PASS: native floor-level side walking, matching-image fade release$(if ($RequireFront) { ', native front exit and isolated Knight particles' }). Visual doorway acceptance remains separate."
