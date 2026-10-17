param([Parameter(Mandatory)][Guid]$Session, [switch]$HighResolution)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$hk = Get-Content -LiteralPath "$workspace/.local/runs/HollowKnight/$Session/plugin.log" -Raw
$ns = Get-Content -LiteralPath "$workspace/.local/runs/NineSols/$Session/plugin.log" -Raw
if (($hk+$ns) -match 'NS_VISUAL_FAILED|HUD_FAILED') { throw 'Native HUD capture failed.' }
foreach ($marker in @('NS_HUD_READY native=True','NS_HUD_CAPTURE')) { if (-not $ns.Contains($marker)) { throw "Missing native HUD evidence: $marker" } }
if ($HighResolution) {
    if ($ns -notmatch 'NS_HUD_READY[^\r\n]*width=(\d+) height=(\d+) atlas=True' -or [int]$Matches[1] -le 512 -or [int]$Matches[2] -le 512) { throw 'High-resolution native HUD atlas missing.' }
}
foreach ($marker in @('HK_YI_HUD_READY nativeHudHidden=True','HK_YI_HUD_RELEASED nativeHudRestored=True')) { if (-not $hk.Contains($marker)) { throw "Missing host HUD evidence: $marker" } }
$sent = @{}; $received = @{}
foreach ($m in [regex]::Matches($ns, '(?<![A-Z_])HUD_SENT sequence=(\d+) frame=(\d+) hash=(\d+)[^\r\n]*geometryFrame=(\d+)')) { $sent[$m.Groups[1].Value] = "$($m.Groups[2].Value):$($m.Groups[3].Value):$($m.Groups[4].Value)" }
foreach ($m in [regex]::Matches($hk, '(?<![A-Z_])HUD_RECEIVED sequence=(\d+) frame=(\d+) hash=(\d+)[^\r\n]*geometryFrame=(\d+)')) { $received[$m.Groups[1].Value] = "$($m.Groups[2].Value):$($m.Groups[3].Value):$($m.Groups[4].Value)" }
$count = 0
foreach ($m in [regex]::Matches($hk, 'HK_HUD_PRESENTED sequence=(\d+) frame=(\d+) hash=(\d+) geometry=(\d+)')) {
    $key = $m.Groups[1].Value; $value = "$($m.Groups[2].Value):$($m.Groups[3].Value):$($m.Groups[4].Value)"
    if ($sent[$key] -ne $value -or $received[$key] -ne $value) { throw 'HUD pixels or geometry changed between native capture and host presentation.' }
    $count++
}
if ($count -lt 10) { throw 'Insufficient native HUD transfer evidence.' }
foreach ($m in [regex]::Matches($ns, 'NS_HUD_CAPTURE[^\r\n]*visiblePixels=(\d+)')) { if ([int]$m.Groups[1].Value -le 0) { throw 'Empty native HUD image.' } }
Write-Output "PASS: $count exact native/received/presented HUD records and native HUD visibility restoration. Appearance and resource changes require separate capture review."
