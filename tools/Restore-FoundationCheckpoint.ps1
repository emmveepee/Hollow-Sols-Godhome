[CmdletBinding()]
param([switch]$CheckOnly, [ValidateSet('fe5c5d8','7958c4d','fd58ffc','88b925a','4ea53ef','724fff3','851467e','35a474d','a1b861f','704cde3','2768bb2','ec522cf','bc19c4c','7693e97','28f4d93','2f61016','c2ac766','54c90e5','44237c6','ed76d7c','2cd7613','25ce401','2e4c0d0','c9c1e20','b779fa0','dbdc759','f82a2fa','nkg1009','nkg1010b')][string]$Checkpoint='fe5c5d8')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$backup=Join-Path $root $(if($Checkpoint -eq 'nkg1010b'){'.local/nkg-followup/rollback'}elseif($Checkpoint -eq 'nkg1009'){'.local/nkg-mechanics/rollback-nkg1009'}elseif($Checkpoint -eq 'f82a2fa'){'.local/roster-oct09/rollback-f82a2fa'}elseif($Checkpoint -eq 'dbdc759'){'.local/talisman-traversal/rollback-dbdc759'}elseif($Checkpoint -eq 'b779fa0'){'.local/lethal-kick/rollback-b779fa0'}elseif($Checkpoint -eq 'c9c1e20'){'.local/draw-order/rollback-c9c1e20'}elseif($Checkpoint -eq '2e4c0d0'){'.local/grounded-dive/rollback-2e4c0d0'}elseif($Checkpoint -eq '25ce401'){'.local/local-aura/rollback-25ce401'}elseif($Checkpoint -eq '2cd7613'){'.local/sisters-fix/rollback-2cd7613'}elseif($Checkpoint -eq 'ed76d7c'){'.local/detailed-logging/rollback-ed76d7c'}elseif($Checkpoint -eq '44237c6'){'.local/hornet/rollback-44237c6'}elseif($Checkpoint -eq '54c90e5'){'.local/attack-refinement/rollback-54c90e5'}elseif($Checkpoint -eq 'c2ac766'){'.local/playtest-oct08/rollback-c2ac766'}elseif($Checkpoint -eq '2f61016'){'.local/pantheons/rollback-2f61016'}elseif($Checkpoint -eq '28f4d93'){'.local/effect-library/rollback-28f4d93'}elseif($Checkpoint -eq '7693e97'){'.local/canvas-retirement/rollback-7693e97'}elseif($Checkpoint -eq 'bc19c4c'){'.local/rage-vfx/rollback-bc19c4c'}elseif($Checkpoint -eq 'ec522cf'){'.local/canvas-primary/rollback-ec522cf'}elseif($Checkpoint -eq '2768bb2'){'.local/content-foundation/rollback-2768bb2'}elseif($Checkpoint -eq '704cde3'){'.local/armor-phases/rollback-704cde3'}elseif($Checkpoint -eq 'a1b861f'){'.local/player-regressions/rollback-a1b861f'}elseif($Checkpoint -eq '35a474d'){'.local/broad-roster/rollback-35a474d'}elseif($Checkpoint -eq '851467e'){'.local/roster/rollback-851467e'}elseif($Checkpoint -ne 'fe5c5d8'){".local/pv-tuning/rollback-$Checkpoint"}else{'.local/foundations/rollback-fe5c5d8'})
if(Get-Process hollow_knight,NineSols -ErrorAction SilentlyContinue){throw 'Close both games before checking or restoring deployment.'}
$plans=@()
foreach($game in @('HollowKnight','NineSols')) {
    $manifestPath=Join-Path $root ".local/deployments/$game.json"
    $current=Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $oldPath=Join-Path $backup $(if($Checkpoint -in @('851467e','35a474d','a1b861f','704cde3','2768bb2','ec522cf','bc19c4c','7693e97','28f4d93','2f61016','c2ac766','54c90e5','44237c6','ed76d7c','2cd7613','25ce401','2e4c0d0','c9c1e20','b779fa0','dbdc759','f82a2fa','nkg1009','nkg1010b')){"$game/deployment.json"}else{"$game.json"})
    $old=Get-Content -LiteralPath $oldPath -Raw | ConvertFrom-Json
    if($old.root -ne $current.root -or $old.assemblyHash -ne $current.assemblyHash -or $old.mode -ne 'no-save-io'){throw 'Rollback targets a different game build or installation.'}
    if((Get-FileHash -LiteralPath $current.assemblyPath).Hash -ne $current.assemblyHash){throw 'Installed native assembly changed.'}
    $owned=@('BepInEx\core\YiGodhome.Bootstrap.dll','BepInEx\patchers\YiGodhome.SaveGuard.dll',"BepInEx\plugins\YiGodhome.$game.dll")
    foreach($relative in $owned) {
        $live=Join-Path $current.root $relative
        $saved=Join-Path (Join-Path $backup $game) $relative
        $now=@($current.files | Where-Object relative -eq $relative); $then=@($old.files | Where-Object relative -eq $relative)
        if($now.Count -ne 1 -or $then.Count -ne 1 -or (Get-FileHash -LiteralPath $live).Hash -ne $now[0].sha256 -or (Get-FileHash -LiteralPath $saved).Hash -ne $then[0].sha256){throw "Changed or unverified owned file: $game/$relative"}
        $plans+=@{source=$saved;destination=$live;hash=$then[0].sha256}
    }
}
if($CheckOnly){Write-Output "PASS: exact $Checkpoint three-component rollback verified for both closed games. No files changed."; return}
foreach($plan in $plans){Copy-Item -LiteralPath $plan.source -Destination $plan.destination; if((Get-FileHash -LiteralPath $plan.destination).Hash -ne $plan.hash){throw 'Rollback copy failed hash verification.'}}
foreach($game in @('HollowKnight','NineSols')){Copy-Item -LiteralPath (Join-Path $backup $(if($Checkpoint -in @('851467e','35a474d','a1b861f','704cde3','2768bb2','ec522cf','bc19c4c','7693e97','28f4d93','2f61016','c2ac766','54c90e5','44237c6','ed76d7c','2cd7613','25ce401','2e4c0d0','c9c1e20','b779fa0','dbdc759','f82a2fa','nkg1009','nkg1010b')){"$game/deployment.json"}else{"$game.json"})) -Destination (Join-Path $root ".local/deployments/$game.json")}
Write-Output 'Restored only the six owned components and exact deployment manifests. Source checkout and all saves remain untouched. Do not redeploy newer source unless intentionally leaving rollback.'
