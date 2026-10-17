param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk = Get-BoundedRuntimeLog -Run "$workspace/.local/runs/HollowKnight/$Session" -Lines
$ns = Get-BoundedRuntimeLog -Run "$workspace/.local/runs/NineSols/$Session" -Lines
function Timestamp([string]$line) { [DateTimeOffset]::Parse(($line -split ' ')[0]) }
$begins = @($hk | Where-Object { $_ -match 'HK_RETURN_PRESENTATION_BEGIN nativeFadeUnchanged=True' })
$images = @($hk | Where-Object { $_ -match 'HK_RETURN_IMAGE_READY geometry=(\d+) nativeFrame=(\d+) sequence=(\d+) fade=0 renderer=True opaquePixels=True' })
$entries = @($ns | Where-Object { $_ -match 'NS_ROOM_ENTRY_COMPLETE style=4' })
$ups = @($ns | Where-Object { $_ -match 'NS_RETURN_GET_UP' })
$ready = @($hk | Where-Object { $_ -match 'HK_HUB_COMBAT_RETURN_READY' })
$nativeReturns = @($hk | Where-Object { $_ -match 'HK_ENCOUNTER_RETURN native=True' })
if ($begins.Count -lt 2 -or $images.Count -ne $begins.Count -or $entries.Count -ne $begins.Count -or $ups.Count -ne $begins.Count -or $ready.Count -ne $begins.Count -or $nativeReturns.Count -ne $begins.Count) { throw 'Incomplete identified native return sequences.' }
if (($hk+$ns) -match 'HK_RETURN_FADE_HELD|HK_RETURN_MASK_HELD|SESSION_TERMINAL.*phase=(Active|Restoring|Victory|Defeated)') { throw 'Return used fade-delay compensation or a terminal session.' }
$latencies = @()
for ($i=0; $i -lt $images.Count; $i++) {
    $image = $images[$i]
    $null = $image -match 'geometry=(\d+) nativeFrame=(\d+)'; $geometry=$Matches[1]; $frame=[long]$Matches[2]
    $captures = @($ns | Where-Object { $_ -match "NS_RETURN_POSE_CAPTURED geometry=$geometry nativeFrame=(\d+) state=LieDown .*renderer=True fade=0 opaquePixels=True" })
    $early = @($hk | Where-Object { $_ -match "HK_RETURN_GEOMETRY_EARLY geometry=$geometry state=" })
    $present = @($hk | Where-Object { $_ -match "HK_ROOM_FADE_RELEASED geometry=$geometry nativeImage=True cameraHeld=False nativeReturnTiming=True" })
    if ($captures.Count -ne 1 -or $early.Count -ne 1 -or $present.Count -ne 1) { throw 'Missing early geometry, actual lying capture or unchanged native return timing.' }
    $null = $captures[0] -match 'nativeFrame=(\d+)'
    if ($frame -lt [long]$Matches[1] -or (Timestamp $present[0]) -lt (Timestamp $image)) { throw 'Presentation precedes identified opaque lying capture.' }
    $ms = ((Timestamp $image)-(Timestamp $begins[$i])).TotalMilliseconds
    if ($ms -lt 0 -or $ms -gt 250 -or (Timestamp $image) -ge (Timestamp $nativeReturns[$i])) { throw "Lying image still waited for HK wake or missed its 250 ms preparation budget: $ms ms." }
    $until = Timestamp $ready[$i]
    $lateResets = @($ns | Where-Object { $_ -match 'NS_REVIVAL_CLEANUP|NS_WARM_RESET_ARMED' -and (Timestamp $_) -ge (Timestamp $captures[0]) -and (Timestamp $_) -lt $until })
    if ($lateResets.Count -ne 0) { throw 'Native restoration invalidated an already captured lying return image.' }
    if ((Timestamp $ready[$i]) -lt (Timestamp $entries[$i]) -or (Timestamp $ready[$i]) -lt (Timestamp $nativeReturns[$i]) -or ((Timestamp $entries[$i])-(Timestamp $ups[$i])).TotalMilliseconds -lt 200) { throw 'Control preceded completed native wake/get-up.' }
    $latencies += [Math]::Round($ms,3)
}
@{session=$Session;returns=$images.Count;lyingImageMs=$latencies;budgetMs=250;nativeFadeUnchanged=$true;scope='Early opaque LieDown image, native-return/control ordering. Full-screen artwork and natural death/focus remain player acceptance.'} | ConvertTo-Json | Set-Content -LiteralPath "$workspace/.local/input-tests/$Session/return-presentation.json"
Write-Output "PASS: $($images.Count) early opaque lying images in $($latencies -join ', ') ms; native return fades unchanged, controls gated by both native wakes."
