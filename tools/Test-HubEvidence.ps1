param([Parameter(Mandatory)][Guid]$Session, [switch]$RequireHall, [switch]$RequirePool, [switch]$RequireStatue, [switch]$RequireVoid)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$hk = Get-Content -LiteralPath "$workspace/.local/runs/HollowKnight/$Session/plugin.log" -Raw
$ns = Get-Content -LiteralPath "$workspace/.local/runs/NineSols/$Session/plugin.log" -Raw
if (($hk+$ns) -match '(HK_HUB|HK_PROXY|HK_VISUAL|NS_MEMORY|NS_INPUT|NS_VISUAL)_FAILED') { throw 'Hub adapter failed.' }
$rooms = @([regex]::Matches($hk, 'HK_HUB_GEOMETRY scene=([^ ]+) frame=(\d+)'))
if (-not $rooms.Count) { throw 'No host room generation.' }
foreach ($room in $rooms) {
    $scene = [regex]::Escape($room.Groups[1].Value)
    $frame = $room.Groups[2].Value
    if ($ns -notmatch "NS_GEOMETRY_IMPORTED source=$Session scene=$scene frame=$frame ") { throw "Room not imported: $scene/$frame" }
    if ($hk -notmatch "HK_VISUAL_BINDING scene=$scene geometry=$frame sprite=True material=True active=True enabled=True cameraVisible=True") { throw "No bound, camera-visible Yi renderer in room: $scene/$frame" }
}
if ($RequireHall -and ($hk -notmatch 'HK_HUB_GEOMETRY scene=GG_Workshop' -or $ns -notmatch 'NS_HUB_HANDOFF scene=GG_Workshop')) { throw 'No verified Hall handoff.' }
if ($RequireStatue) {
    $requested = [regex]::Match($hk, '(?m)^(\S+) HK_HUB_MENU_FIXTURE request=True')
    $opened = [regex]::Match($hk, '(?m)^(\S+) HK_HUB_MENU_FIXTURE opened=True nativeCancel=True')
    if (-not $requested.Success -or -not $opened.Success -or $hk -notmatch 'HK_HUB_MENU_FIXTURE controlReturned=True') { throw 'Native statue open/cancel cycle missing.' }
    $elapsed = ([DateTime]::Parse($opened.Groups[1].Value) - [DateTime]::Parse($requested.Groups[1].Value)).TotalSeconds
    if ($elapsed -lt 0 -or $elapsed -ge 2.5) { throw "Statue relied on walk timeout: $elapsed seconds." }
    $cycle = [regex]::Match($hk, '(?s)HK_HUB_MENU_FIXTURE request=True.*?HK_HUB_MENU_FIXTURE controlReturned=True')
    if (-not $cycle.Success -or $cycle.Value -match 'HK_HUB_GEOMETRY|HK_VISUAL_HIDDEN' -or $cycle.Value -notmatch 'HK_HUB_INTERACTION_RESUMED geometry=\d+ mappingPreserved=True') { throw 'Statue cancellation lost its room mapping or visual.' }
    Write-Output "PASS: native statue open/cancel in $elapsed seconds without the three-second walk timeout."
}
if ($RequirePool) {
    if ($hk -match 'HK_HUB_POOL_FAILED') { throw 'Pool fixture failed.' }
    $pool = [regex]::Match($hk, '(?s)HK_HUB_POOL_ENTER.*?HK_HUB_POOL_EXIT[^\r\n]*')
    if (-not $pool.Success -or $pool.Value -match 'HK_HUB_INTERACTION|HK_HUB_GEOMETRY|HK_VISUAL_HIDDEN') { throw 'Pool exit missing or interrupted by a control/geometry/visibility handoff.' }
    if ($hk -notmatch 'HK_HUB_WATER_OWNERSHIP knightSwimming=False nativeYiMovement=True waterEffectsUnchanged=True') { throw 'Knight swimming ownership was not isolated.' }
    Write-Output 'PASS: native-input pool entry/exit without control, geometry or visibility interruption.'
}
if ($RequireVoid) {
    $cycle = [regex]::Match($hk, '(?s)HK_HUB_VOID_FIXTURE_BEGIN geometry=(\d+).*?HK_HUB_VOID_MOVEMENT_VERIFIED[^\r\n]*')
    if (-not $cycle.Success -or $cycle.Value -match 'HK_HUB_INTERACTION|HK_HUB_GEOMETRY|HK_VISUAL_HIDDEN') { throw 'Void recovery missing or interrupted by native Knight handoff.' }
    $recoveries = @([regex]::Matches($cycle.Value, 'HK_HUB_VOID_RECOVERED count=(\d+) x=([^ ]+) y=([^ ]+) geometry=(\d+)'))
    if ($recoveries.Count -ne 2 -or @($recoveries | Where-Object { $_.Groups[4].Value -ne $cycle.Groups[1].Value }).Count) { throw 'Two recoveries with retained room mapping required.' }
    if ($ns -notmatch 'NS_DREAM_VOID_BOUND[^\r\n]+damage=0 nativeRecovery=True' -or $hk -notmatch 'HK_DREAM_VOID_OWNERSHIP nativeYiRecovery=True') { throw 'Harmless native recovery ownership missing.' }
    $nativeVoids = @([regex]::Matches($ns, 'NS_DREAM_VOID_RECOVERY count=\d+ id=-?\d+ health=([^ ]+) damage=0'))
    if ($nativeVoids.Count -ne 2 -or $nativeVoids[0].Groups[1].Value -ne $nativeVoids[1].Groups[1].Value -or $hk -match 'HK_HUB_VOID_FAILED') { throw 'Two harmless recoveries with unchanged health required.' }
    if ($ns -notmatch 'NS_RECOVERY_FRAME[^\r\n]*recovering=True fade=1 state=Respawn' -or $ns -notmatch 'NS_RECOVERY_FRAME[^\r\n]*recovering=False fade=0') { throw 'Native covered recovery and fade release missing.' }
    if ($hk -notmatch 'HK_RECOVERY_DRAWN[^\r\n]*fade=1') { throw 'Host did not draw native recovery cover.' }
    $after = $hk.Substring($cycle.Index + $cycle.Length)
    if ($after -notmatch 'HK_VISUAL_BINDING[^\r\n]+active=True enabled=True cameraVisible=True') { throw 'No visible Yi after void recovery.' }
    Write-Output 'PASS: two native void recoveries, covered relocation, post-recovery movement and visible Yi without Knight room-return or mapping restart.'
}
$states = @([regex]::Matches($hk, 'HK_HUB_STATE scene=([^ ]+) geometry=(\d+) ready=True canInteract=(True|False) knightVisible=(True|False) nativeMovement=(True|False)'))
if ($states.Count -lt 5 -or @($states | Where-Object { $_.Groups[4].Value -ne 'False' -or $_.Groups[5].Value -ne 'False' }).Count) { throw 'Yi-only native movement ownership not established.' }
if (-not @($states | Where-Object { $_.Groups[3].Value -eq 'True' }).Count) { throw 'Native interaction predicate never available.' }
if ($ns -notmatch 'NS_GEOMETRY_ENTRY_VERIFIED[^\r\n]*nativeGround=True diagnosticRelocation=False' -or $ns -notmatch 'NS_INPUT_NATIVE[^\r\n]*state=(Jump|Roll|Normal)[^\r\n]*vx=-?[1-9]') { throw 'No native ground/movement evidence.' }
if ($ns -match 'NS_GEOMETRY_WALL_MOVE|NS_GEOMETRY_FALL') { throw 'Hub entry executed a diagnostic relocation.' }
$sent = @{}
foreach ($m in [regex]::Matches($ns, 'VISUAL_SENT sequence=(\d+) frame=(\d+) hash=(\d+)')) { $sent[$m.Groups[1].Value] = "$($m.Groups[2].Value):$($m.Groups[3].Value)" }
$images = @([regex]::Matches($hk, 'HK_VISUAL_PRESENTED sequence=(\d+) frame=(\d+) hash=(\d+)'))
if ($images.Count -lt 5) { throw 'Insufficient Yi presentation records.' }
foreach ($m in $images) { if ($sent[$m.Groups[1].Value] -ne "$($m.Groups[2].Value):$($m.Groups[3].Value)") { throw 'Presented pixels do not match native capture.' } }
Write-Output "PASS: $($rooms.Count) matched room generations, native collision/movement, $($states.Count) Yi-only proxy samples with native interaction availability, $($images.Count) matching native images. Physical door/menu behavior requires the player report."
