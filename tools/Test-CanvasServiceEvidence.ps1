param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$run="$root/.local/runs/NineSols/$Session"
$log=Get-BoundedRuntimeLog -Run $run
$configuration=Get-Content -LiteralPath "$run/run.json" -Raw | ConvertFrom-Json
if($configuration.seconds -ne 120 -or $log -notmatch 'SESSION_CONFIGURATION capabilities=canvas-service-audit,enemy-probe' -or $log -match '_FAILED|SESSION_TERMINAL|NS_CANVAS_READY'){throw 'Service inventory was not an isolated successful bootstrap audit.'}
$events=@([regex]::Matches($log,'NS_CANVAS_SERVICE_AUDIT level=(-?\d+) scene=A2_S5_BossHorseman_Final bytes=(\d+) readOnly=True once=True'))
if($events.Count -ne 1){throw 'Exactly one completed native service inventory required.'}
$path="$run/canvas-service-audit.txt"
if((Get-Item -LiteralPath $path).Length -gt 256KB){throw 'Service inventory exceeds its bounded report budget.'}
$report=Get-Content -LiteralPath $path -Raw
if($report -notmatch ('nativeMvid=dcdc095d-05f0-4390-897b-045fd51ebc28 scene=A2_S5_BossHorseman_Final level='+$events[0].Groups[1].Value+' player=-?\d+')){throw 'Report build/owner identity mismatch.'}
foreach($owner in @('GameLevel','GameCore','ResetManager','Player')){if($report -notmatch "OWNER ${owner}:"){throw "Missing scoped native owner $owner"}}
foreach($cache in @('restters','levelStarts','levelDestroys','clearReferences')){if($report -notmatch "CACHE GameLevel\.$cache count=\d+"){throw "Missing native level cache $cache"}}
Write-Output 'PASS: bounded standalone native service inventory with pinned build and owner/cache evidence. Nested dependency closure and safe asset pruning are not established.'
