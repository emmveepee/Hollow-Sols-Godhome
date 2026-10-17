[CmdletBinding()]
param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$hk = Get-Content -LiteralPath "$workspace/.local/runs/HollowKnight/$Session/plugin.log" -Raw
if ($hk -match 'HK_PROXY_FAILED') { throw 'Proxy runtime failed.' }
$dismount = $hk.IndexOf('HK_PROXY_DISMOUNT_VERIFIED')
$ready = $hk.IndexOf('HK_PROXY_READY')
if ($dismount -lt 0 -or $ready -lt $dismount) { throw 'Proxy started before verified native bench exit.' }
$activeProxy = $hk.Substring($ready).Split('HK_PROXY_RELEASED')[0]
if ($activeProxy -match 'HK_OVERLAY name=black_solid[^\r\n]*enabled=False' -or
    $activeProxy -notmatch 'HK_OVERLAY name=black_solid[^\r\n]*enabled=True') { throw 'Native camera-mask renderers were disabled or not observed during proxy control.' }
if ($hk -notmatch 'HK_PROXY_BENCH_STATE state=Get Off atBench=False relinquished=True' -or
    $hk -notmatch 'HK_PROXY_BENCH_STATE state=Regain Control atBench=False relinquished=False') { throw 'Native bench cleanup not observed.' }
$samples = @([regex]::Matches($hk, 'HK_PROXY_SAMPLE x=([-\d.E+]+) y=([-\d.E+]+) error=([-\d.E+]+) cameraX=([-\d.E+]+) cameraY=([-\d.E+]+) nativeDisabled=(True|False)'))
if ($samples.Count -lt 5) { throw 'Insufficient proxy samples.' }
$camera = @(); $ys = @()
foreach ($m in $samples) {
    $errorValue = [float]::Parse($m.Groups[3].Value,[Globalization.CultureInfo]::InvariantCulture)
    if ($errorValue -gt 0.01 -or $m.Groups[6].Value -ne 'True') { throw 'Proxy drift or native movement enabled.' }
    $camera += [float]::Parse($m.Groups[4].Value,[Globalization.CultureInfo]::InvariantCulture)
    $ys += [float]::Parse($m.Groups[2].Value,[Globalization.CultureInfo]::InvariantCulture)
}
$range = $camera | Measure-Object -Minimum -Maximum
$height = $ys | Measure-Object -Minimum -Maximum
if ($range.Maximum - $range.Minimum -lt 1 -or $height.Maximum - $height.Minimum -lt 1) { throw 'No meaningful native camera movement or vertical proxy movement.' }
if ($hk -notmatch 'HK_PROXY_FROZEN reason=stale-pose' -or $hk -notmatch 'HK_PROXY_RELEASED') { throw 'Missing stale freeze or teardown.' }
Write-Output "PASS: native bench exit, $($samples.Count) accurate proxy samples, camera X travel $($range.Maximum - $range.Minimum), vertical movement, stale freeze and teardown."
