param([Parameter(Mandatory)][Guid]$Session, [switch]$ClimbOnly)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$ns = Get-Content -LiteralPath "$workspace/.local/runs/NineSols/$Session/plugin.log" -Raw
$hk = Get-Content -LiteralPath "$workspace/.local/runs/HollowKnight/$Session/plugin.log" -Raw
if ($ns -match 'NS_WALL_FIXTURE_FAILED|NS_MEMORY_FAILED|NS_INPUT_FAILED' -or $hk -match 'HK_HUB_FAILED|HK_PROXY_FAILED') { throw 'Wall runtime failed.' }
foreach ($index in 0..3) {
    $match = [regex]::Match($ns, "NS_WALL_FIXTURE_PASS index=$index nativeWallRun=True gain=([\d.]+) finalState=Normal requiredGain=([\d.]+) bottom=(\d+) top=(\d+)")
    $bottom=@(19,15,16,19)[$index]
    if (-not $match.Success -or [double]$match.Groups[1].Value -lt [double]$match.Groups[2].Value -or [int]$match.Groups[3].Value -ne $bottom -or [int]$match.Groups[4].Value -ne 35 -or [double]$match.Groups[2].Value -lt (35-$bottom-2.01)) { throw "Surface $index did not complete its actual-height native climbing and ledge exit." }
}
if ($ns -notmatch 'NS_WALL_FIXTURE_COMPLETE surfaces=4 originalPositionRestored=True') { throw 'Wall fixture did not restore its entry position.' }
if($ClimbOnly){Write-Output 'PASS (climbing only): four actual-height native climbs/Normal ledge exits and restored entry position. Combat return and physical appearance are not assessed.';return}
if ($hk -notmatch 'HK_HUB_COMBAT_RETURN_READY[^\r\n]*presented=True nativeWakeComplete=True') { throw 'No visible/control-ready return after the wall diagnostic.' }
Write-Output 'PASS: four full-height native climbs and normal-state ledge exits, followed by a presented boss return. Physical route/appearance remain player checks.'
