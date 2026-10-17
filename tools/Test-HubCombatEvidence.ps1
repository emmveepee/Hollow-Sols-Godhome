param([Parameter(Mandatory)][Guid]$Session, [switch]$RequireReentry)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hk = Get-BoundedRuntimeLog -Run "$workspace/.local/runs/HollowKnight/$Session"
$ns = Get-BoundedRuntimeLog -Run "$workspace/.local/runs/NineSols/$Session"
if (($hk + $ns) -match 'NS_AUDIO_FAILED|LIFECYCLE_FAILED|HIT_FAILED|CONTACT_FAILED|NS_MEMORY_FAILED|NS_INPUT_FAILED|HK_PROXY_FAILED|HK_HUB_FAILED|HK_GODHOME_PROBE_FAILED|NS_VISUAL_FAILED|HK_VISUAL_FAILED|Invalid encounter state/authority|IPC_DISCONNECTED reason=InvalidDataException') { throw 'Hub combat adapter failed.' }
$begins = @([regex]::Matches($hk, 'ENCOUNTER_STATE role=HollowKnight encounter=([\w-]+) geometryFrame=(\d+) phase=Preparing'))
if ($begins.Count -lt $(if($RequireReentry){2}else{1})) { throw 'Missing native encounter generations.' }
if (@($begins | ForEach-Object {$_.Groups[1].Value} | Sort-Object -Unique).Count -ne $begins.Count) { throw 'Encounter identity reused.' }
$totalHits = 0
for ($i=0; $i -lt $begins.Count; $i++) {
    $begin=$begins[$i]; $id=$begin.Groups[1].Value; $geometry=$begin.Groups[2].Value
    $end=if($i+1 -lt $begins.Count){$begins[$i+1].Index}else{$hk.Length}
    $hostRun=$hk.Substring($begin.Index,$end-$begin.Index)
    $nativeStart=[regex]::Match($ns,"ENCOUNTER_STATE role=NineSols encounter=$id geometryFrame=$geometry phase=Restoring")
    if(-not $nativeStart.Success){throw 'Matching native restoration missing.'}
    $nextNative=[regex]::Match($ns.Substring($nativeStart.Index+$nativeStart.Length),'ENCOUNTER_STATE role=NineSols encounter=(?!'+$id+')[\w-]+ geometryFrame=\d+ phase=Restoring')
    $nativeEnd=if($nextNative.Success){$nativeStart.Index+$nativeStart.Length+$nextNative.Index}else{$ns.Length}
    $nativeRun=$ns.Substring($nativeStart.Index,$nativeEnd-$nativeStart.Index)
    foreach($phase in @('Active','Ready')) { if($nativeRun -notmatch "ENCOUNTER_STATE role=NineSols encounter=$id geometryFrame=$geometry phase=$phase"){throw "Native matching $phase missing for $id"} }
    foreach($marker in @('HK_ENCOUNTER_RELEASED','HK_HUB_COMBAT_ENDED','HK_ENCOUNTER_RETURN native=True','HK_HUB_COMBAT_RETURN_READY')){if(-not $hostRun.Contains($marker)){throw "Missing $marker for $id"}}
    $hits=@([regex]::Matches($hostRun,'HK_NATIVE_HIT sequence=(\d+) kind=(\w+) nativeDamage=([\d.E+-]+) translated=(\d+) before=(\d+) after=(\d+) applied=(True|False) (?:targetDead|dead)=(True|False)'))
    if(@($hits | Where-Object {$_.Groups[7].Value -eq 'True'}).Count -lt 1){throw "No real outgoing damage for $id"}
    foreach($h in $hits){
        $sequence=$h.Groups[1].Value; $kind=$h.Groups[2].Value; $damage=[regex]::Escape($h.Groups[3].Value); $health=$h.Groups[6].Value
        if($nativeRun -notmatch "NS_HIT_OUTCOME sequence=$sequence frame=\d+ entity=-?\d+ kind=$kind damage=$damage(?: encounter=$id activation=\d+ attack=\d+)?\r?\n" -or $nativeRun -notmatch "NS_HOST_HIT_RESULT sequence=$sequence result=(Applied|Refused) health=$health\r?\n"){throw "Missing generation-scoped acknowledgement for $id/$sequence"}
    }
    if($nativeRun -notmatch 'NS_CONTACT_RESOLVED id=\d+ kind=(damage|parry)'){throw 'No native incoming verdict.'}
    $returnGeometry=[regex]::Match($hostRun,'HK_HUB_COMBAT_RETURN_READY geometry=(\d+)').Groups[1].Value
    if ($hostRun -notmatch 'HK_HUB_COMBAT_RETURN_READY[^\r\n]*presented=True nativeWakeComplete=True') { throw 'Return controls released without presentation/wake evidence.' }
    $after=$hostRun.Substring($hostRun.IndexOf('HK_HUB_COMBAT_RETURN_READY'))
    if($after -notmatch 'HK_VISUAL_BINDING scene=GG_Workshop[^\r\n]*active=True enabled=True cameraVisible=True' -or $hostRun -notmatch "HK_HUB_STATE scene=GG_Workshop geometry=$returnGeometry ready=True canInteract=True knightVisible=False nativeMovement=False"){throw "Visible/interactable returned Yi missing for $id"}
    $totalHits+=$hits.Count
}
if($RequireReentry){
    if($hk -notmatch 'HK_NATIVE_STATUE_REENTRY' -or [regex]::Matches($hk,'HK_HUB_COMBAT_STATUE_SELECT tier=[0-2]').Count -lt 2){throw 'Second native statue selection missing.'}
    if($hk -notmatch 'HK_NATIVE_SPELL_CHECK canCast=False nativeMethod=True'){throw 'Native spell gate check missing.'}
    if($ns -notmatch 'NS_AMBIENCE_ISOLATED nativeTracksStopped=True' -or $ns -notmatch 'NS_AMBIENCE_SUPPRESSED event='){throw 'Native ambience isolation evidence missing.'}
}
& "$PSScriptRoot/Test-WarmSceneEvidence.ps1" -Session $Session
Write-Output "PASS: $($begins.Count) native statue combat generations, $totalHits generation-scoped acknowledged hits, incoming verdicts, native returns and visible/interactable Yi. Physical controls and sound require player acceptance."
