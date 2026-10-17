[CmdletBinding()]
param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hostRows=(Get-BoundedRuntimeLog -Run "$root/.local/runs/HollowKnight/$Session") -split '\r?\n'
$nativeRows=(Get-BoundedRuntimeLog -Run "$root/.local/runs/NineSols/$Session") -split '\r?\n'
if($hostRows -match 'SESSION_TERMINAL|_FAILED(?:\s|$)' -or $nativeRows -match '_FAILED(?:\s|$)'){throw 'Lever fixture failed.'}
if(-not ($nativeRows -match 'NS_MELEE_WINDOWS_BOUND sources=')){throw 'Native melee sources absent.'}
$start=@($hostRows | Where-Object {$_ -match 'HK_LEVER_FIXTURE lever='})
$finish=@($hostRows | Where-Object {$_ -match 'HK_LEVER_FIXTURE_COMPLETE lever='})
if($start.Count -ne 1 -or $finish.Count -ne 1){throw 'Native lever swap did not finish exactly once.'}
foreach($terminal in @($nativeRows | Where-Object {$_ -match 'SESSION_TERMINAL'})) {
    if($terminal -notmatch 'reason=(?:VISUAL_CHANNEL_STOPPED|RAGE_MASK_CHANNEL_STOPPED|PEER_DISCONNECTED) ' -or -not ($hostRows -match 'SMOKE_EXIT_REQUEST game=HollowKnight') -or
        [DateTime]::Parse($terminal.Split(' ')[0]) -le [DateTime]::Parse($finish[0].Split(' ')[0])) { throw 'Unexpected native terminal during lever fixture.' }
}
if($start[0] -notmatch 'lever=(-?\d+)'){throw 'Missing lever identity.'}; $lever=$Matches[1]
$hits=@($hostRows | Where-Object {$_ -match "HK_STATUE_LEVER_MELEE lever=$lever "})
if(@($hits | Where-Object {$_ -match 'changed=True'}).Count -lt 2 -or $finish[0] -notmatch "lever=$lever nativeSwapFinished=True.*meleeToggles=\d+ statueVisible=True"){throw 'Two actual melee toggles and visible statue completion required.'}
if($start[0] -match 'replacement=True' -and (-not($hostRows -match 'HK_DREAM_LEVER_TOGGLED.*dream=True meshSwap=False nativeFade=True') -or -not($hostRows -match 'HK_DREAM_LEVER_TOGGLED.*dream=False meshSwap=False nativeFade=True'))){throw 'Both dream lighting directions required.'}
$activations=@($hits | ForEach-Object {if($_ -notmatch 'collider=(-?\d+) activation=(\d+) swing=(\d+) geometry=(\d+)' -or [long]$Matches[3] -le 0){throw 'Missing native melee swing identity.'}; $Matches[3]})
if(@($activations | Select-Object -Unique).Count -ne $hits.Count){throw 'Native melee swing toggled a lever more than once.'}
Write-Output "PASS ${Session}: distinct actual melee swings, two native toggles, busy completion and visible statue artwork. $($start[0]). Physical reach/visual fit of every lever remains separate."
