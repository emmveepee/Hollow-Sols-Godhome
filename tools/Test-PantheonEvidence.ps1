[CmdletBinding()]
param([Parameter(Mandatory)][guid]$Session,[ValidateRange(0,20)][int]$RequireHandoffs=1,[switch]$RequireReturn,[switch]$RequireInterlude,[switch]$CompletedBeforeHostExit)
$ErrorActionPreference='Stop'
$workspace=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk=Get-BoundedRuntimeLog -Run "$workspace/.local/runs/HollowKnight/$Session"
$ns=Get-BoundedRuntimeLog -Run "$workspace/.local/runs/NineSols/$Session"
$cutoff=$null
if($CompletedBeforeHostExit) {
    $exit=[regex]::Match($hk,'(?m)^(\S+) SMOKE_EXIT_REQUEST game=HollowKnight frame=\d+\s*$')
    if(-not $exit.Success){throw 'Scoped completion needs the explicit bounded host timer exit.'}
    $cutoff=[datetime]::Parse($exit.Groups[1].Value)
}
foreach($text in @($hk,$ns)) {
    foreach($line in ($text -split "`n" | Where-Object {$_ -match 'SESSION_TERMINAL|LIFECYCLE_FAILED|PROBE_FAILED|HK_TARGET_FIXTURE_FAILED'})) {
        if($cutoff -and $line -match '^\S+ SESSION_TERMINAL reason=VISUAL_CHANNEL_STOPPED .*phase=Ready ' -and [datetime]::Parse(($line -split ' ')[0]) -ge $cutoff){continue}
        throw "Native failure remains outside completed-boundary scope: $line"
    }
}
if($hk -notmatch 'HK_PANTHEON_FIXTURE_BEGIN' -or $ns -notmatch 'NS_CANVAS_READY scene=YiGodhomeCanvas .*bootstrapUnloaded=True'){throw 'No authorized native sequence on the retired canvas.'}
$handoffs=@([regex]::Matches($hk,'HK_SEQUENCE_HANDOFF_READY scene=(\S+) index=(\d+) geometry=(\d+) drained=True image=True'))
if($handoffs.Count -lt $RequireHandoffs){throw "Insufficient host handoffs: $($handoffs.Count) required=$RequireHandoffs"}
foreach($handoff in $handoffs) {
    $scene=$handoff.Groups[1].Value; $geometry=$handoff.Groups[3].Value
    if($ns -notmatch "NS_SEQUENCE_HANDOFF_READY scene=$scene geometry=$geometry .*nativeReset=False pendingOutcomes=0"){throw "Missing drained native handoff for $scene/$geometry"}
}
$players=@([regex]::Matches($ns,'NS_SEQUENCE_HANDOFF_READY .*player=(-?\d+)') | ForEach-Object {$_.Groups[1].Value} | Sort-Object -Unique)
if($players.Count -gt 1){throw 'Native Yi identity changed between completed handoffs.'}
foreach($advance in [regex]::Matches($hk,'ENCOUNTER_STATE role=HollowKnight encounter=(\S+) geometryFrame=(\d+) phase=Advancing')) {
    $id=$advance.Groups[1].Value
    if($hk -notmatch "HK_(?:NATIVE_HIT|SUMMON_HIT)[^`r`n]*encounter=$id" -or $ns -notmatch "ENCOUNTER_STATE role=NineSols encounter=$id [^`r`n]*phase=Advancing"){throw "Missing actual combat/native lifecycle for $id"}
}
if($RequireInterlude) {
    $exit=[regex]::Match($hk,'HK_PANTHEON_EXIT name=\S+ position=.+ target=(\S+) colliders=')
    if(-not $exit.Success){throw 'No authored interlude exit recorded.'}
    $next=$exit.Groups[1].Value
    if($hk -notmatch "HK_SEQUENCE_ROOM [^`r`n]*scene=$next " -or $ns -notmatch "NS_HUB_HANDOFF scene=$next geometry=\d+ samePlayer=True" -or $hk -notmatch "HK_PANTHEON_NATIVE_ROOM scene=$next [^`r`n]*camera=Normal"){throw 'Interlude did not enter and reveal its authored next room on the canvas.'}
}
if($RequireReturn -and $hk -match "HK_PANTHEON_FIXTURE_BEGIN [^`r`n]*index=11 " -and ($hk -notmatch "HK_PANTHEON_RESULTS_ACK" -or $ns -notmatch "phase=SequenceResults")){throw "No actual final native results/continuation handshake."}
if($RequireReturn -and $hk -notmatch 'HK_HUB_COMBAT_RETURN_READY'){throw 'No final/defeat native return completed.'}
if($ns -notmatch 'NS_REPOSITORY_DISPOSED templates=\d+ leases=0' -or $ns -notmatch 'NS_CANVAS_DISPOSED .*bootstrapUnloaded=True'){throw 'Canvas teardown incomplete.'}
Write-Output "PASS: $Session completed authored handoffs=$($handoffs.Count), requested native terminal return=$([bool]$RequireReturn), matched generations and canvas teardown. Scope excludes work after host timer; no full-clear, attrition or normal timing claim."
