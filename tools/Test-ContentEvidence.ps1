param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$run="$root/.local/runs/NineSols/$Session"
$log=Get-BoundedRuntimeLog -Run $run
if($log -match 'SESSION_TERMINAL|NS_CONTENT_FAILED'){throw 'Content session failed; do not promote partial acquisition to complete proof.'}
foreach($pattern in @(
    'NS_CONTENT_MANIFEST types=561 methods=\d+ stripped=0 discovery=False',
    'NS_REPOSITORY_DONOR scene=A5_S5_JieChuanHall .*levelsBlocked=1',
    'NS_REPOSITORY_DONOR scene=AG_Tutorial_Lear_S2_.*levelsBlocked=2',
    'NS_REPOSITORY_TEMPLATE kind=JiequanWarning .*inactive=True',
    'NS_REPOSITORY_TEMPLATE kind=ClimbArtwork .*inactive=True',
    'NS_REPOSITORY_READY templates=2 donorsLoaded=False',
    'NS_REPOSITORY_CAPACITY independent=8 overflowRefused=True',
    'NS_CONTENT_GUARD_RELEASED methods=\d+ donorsLoaded=False',
    'NS_REPOSITORY_DISPOSED templates=2 leases=0',
    'NS_CONTENT_CANVAS_VERIFIED .*scene=YiGodhomeCanvasAudit moved=[\d.]+ jumpRise=[\d.]+ nativeGround=True warningRuns=3 nativeLevelUnchanged=True donorsUnloaded=True',
    'NS_CONTENT_COMPLETE assets=2 nativeGameplay=True sceneCreated=True normalMigration=False',
    'NS_CONTENT_DISPOSED .*restorePlayer=True',
    'BOOTSTRAP_GUARD_VERIFIED')) {
    $text=if($pattern -eq 'BOOTSTRAP_GUARD_VERIFIED'){Get-Content "$run/bootstrap.log" -Raw}else{$log}
    if($text -notmatch $pattern){throw "Missing native content evidence: $pattern"}
}
$plays=[regex]::Matches($log,'NS_CONTENT_WARNING_PLAY run=(\d+) instance=(-?\d+) nativePool=True donorLoaded=False')
if($plays.Count -ne 3 -or @($plays | ForEach-Object {$_.Groups[2].Value} | Sort-Object -Unique).Count -ne 1){throw 'Native warning reset/reuse did not use the same retained owner three times.'}
foreach($file in @('canvas.png','yi.png','wall.png','jiequan-warning.png')) {
    if(-not (Test-Path "$run/$file") -or $log -notmatch "NS_CONTENT_CAPTURE file=$([regex]::Escape($file)) .*opaquePixels=(\d+)" -or [int]$Matches[1] -lt 32){throw "Missing rendered native content: $file"}
}
Write-Output "PASS: session=$Session native donor quarantine/unload, retained Jiequan cue and climb art pixels, native pool reuse, Yi movement/jump/owned-ground landing and cleanup. Inspect captures; this is not ordinary HK integration or arbitrary content support."
