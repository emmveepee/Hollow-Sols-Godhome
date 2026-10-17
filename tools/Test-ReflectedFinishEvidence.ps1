param([Parameter(Mandatory)][Guid]$Session, [Guid]$Encounter=[Guid]::Empty)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$hk=Get-Content -LiteralPath "$root/.local/runs/HollowKnight/$Session/plugin.log" -Raw
$ns=Get-Content -LiteralPath "$root/.local/runs/NineSols/$Session/plugin.log" -Raw
if(($hk+$ns) -match 'CONTACT_FAILED|HIT_FAILED|LIFECYCLE_FAILED' -or $hk -match 'SESSION_TERMINAL'){throw 'Reflected finish session failed.'}
# The bounded host exits first. Its peer must fail closed on channel loss;
# accept only that exact shutdown after the final native return, never combat loss.
$terminals=@([regex]::Matches($ns,'(?m)^([^\r\n ]+) SESSION_TERMINAL[^\r\n]*'))
if($terminals.Count -gt 0) {
    $exit=[regex]::Match($hk,'(?m)^([^\r\n ]+) SMOKE_EXIT_REQUEST game=HollowKnight frame=\d+\r?$')
    $returned=@([regex]::Matches($hk,'ENCOUNTER_STATE role=HollowKnight encounter=([\w-]+) geometryFrame=\d+ phase=Returned'))
    if($terminals.Count -ne 1 -or -not $exit.Success -or $returned.Count -lt 2){throw 'Unexpected terminal failure.'}
    $id=$returned[-1].Groups[1].Value
    if($terminals[0].Value -notmatch "reason=VISUAL_CHANNEL_STOPPED session=$Session encounter=$id phase=Ready " -or
        [DateTimeOffset]::Parse($terminals[0].Groups[1].Value) -lt [DateTimeOffset]::Parse($exit.Groups[1].Value) -or
        $exit.Index -lt $returned[-1].Index){throw 'Terminal failure preceded completed timed shutdown.'}
    Write-Output 'PASS: bounded host exit followed by expected peer channel-loss shutdown after final return.'
}
& "$PSScriptRoot/Test-HubCombatEvidence.ps1" -Session $Session -RequireReentry
$begins=@([regex]::Matches($hk,'ENCOUNTER_STATE role=HollowKnight encounter=([\w-]+) geometryFrame=\d+ phase=Preparing'))
$lethalCount=0
for($i=0;$i -lt $begins.Count;$i++) {
    $start=$begins[$i];$id=$start.Groups[1].Value
    if($Encounter -ne [Guid]::Empty -and $id -ne "$Encounter"){continue}
    $end=if($i+1 -lt $begins.Count){$begins[$i+1].Index}else{$hk.Length}
    $run=$hk.Substring($start.Index,$end-$start.Index)
    $lethal=[regex]::Match($run,'HK_REFLECTED_HIT id=(-?\d+) activation=(\d+) before=([1-9]\d*) after=0 collision=True frame=(\d+)')
    if(-not $lethal.Success){throw "No actual reflected finishing collision for $id"}
    if([int]$lethal.Groups[3].Value -gt 21){throw 'Finishing reflection exceeded native 21 damage.'}
    $frame=$lethal.Groups[4].Value;$activation=$lethal.Groups[2].Value
    if($run -notmatch "HK_REFLECTED_HEALTH entity=-?\d+ targetActivation=\d+ health=0 projectileActivation=$activation frame=$frame"){throw 'Lethal snapshot not settled in same native frame.'}
    $tail=$run.Substring($lethal.Index)
    if($tail -notmatch "ENCOUNTER_STATE role=HollowKnight encounter=$id geometryFrame=\d+ phase=Victory" -or $tail -notmatch "ENCOUNTER_STATE role=HollowKnight encounter=$id geometryFrame=\d+ phase=Returned"){throw 'Native victory/return did not follow reflected collision.'}
    $lethalCount++
}
if($lethalCount -eq 0){throw 'Requested reflected finishing encounter was not recorded.'}
Write-Output "PASS: scope=$Encounter; $lethalCount actual reflected finishing collisions, same-frame health snapshots, native victories/visible returns. Whole-session combat/return and reentry checks pass; other generations are not certified as reflected finishes by an explicit encounter scope. Controlled fixture; normal fight timing remains separate."
