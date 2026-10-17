[CmdletBinding()]
param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$hk = Get-Content -LiteralPath "$workspace/.local/runs/HollowKnight/$Session/plugin.log" -Raw
$ns = Get-Content -LiteralPath "$workspace/.local/runs/NineSols/$Session/plugin.log" -Raw
if (($hk + $ns) -match 'NS_VISUAL_FAILED|HK_VISUAL_FAILED|Invalid visual|Visual .*mismatch') { throw 'Visual failure in runtime evidence.' }
$sent = @{}
foreach ($m in [regex]::Matches($ns, 'VISUAL_SENT sequence=(\d+) frame=(\d+) hash=(\d+) bytes=(\d+) geometryFrame=(\d+)')) {
    $sent[$m.Groups[1].Value] = $m.Value.Replace('VISUAL_SENT','VISUAL_RECEIVED')
}
$received = @{}
foreach ($m in [regex]::Matches($hk, 'VISUAL_RECEIVED sequence=(\d+) frame=(\d+) hash=(\d+) bytes=(\d+) geometryFrame=(\d+)')) {
    if ($sent[$m.Groups[1].Value] -ne $m.Value) { throw "Visual mismatch at $($m.Groups[1].Value)" }
    $received[$m.Groups[1].Value] = "$($m.Groups[2].Value):$($m.Groups[3].Value)"
}
$presented = 0
$hashes = [Collections.Generic.HashSet[string]]::new()
foreach ($m in [regex]::Matches($hk, 'HK_VISUAL_PRESENTED sequence=(\d+) frame=(\d+) hash=(\d+)')) {
    if ($received[$m.Groups[1].Value] -ne "$($m.Groups[2].Value):$($m.Groups[3].Value)") { throw 'Presented image does not match received image.' }
    $presented++; $null = $hashes.Add($m.Groups[3].Value)
}
if ($presented -lt 5 -or $hashes.Count -lt 3) { throw 'Insufficient changing native frames presented.' }
$captures = [regex]::Matches($ns, 'NS_VISUAL_CAPTURE .*opaquePixels=(\d+) clearPixels=(\d+)')
if ($captures.Count -lt 3) { throw 'Missing alpha capture evidence.' }
foreach ($m in $captures) {
    if ([int]$m.Groups[1].Value -le 0 -or [int]$m.Groups[2].Value -le 0) { throw 'Capture lacks visible pixels or transparency.' }
}
if ($hk -notmatch 'HK_VISUAL_HIDDEN reason=stale-or-mismatched-frame') { throw 'Missing stale visual cleanup.' }
Write-Output "PASS: $presented exact captured/received/presented records, $($hashes.Count) distinct frame hashes, visible/transparent pixels and stale cleanup. Visual appearance requires separate screenshot review."
