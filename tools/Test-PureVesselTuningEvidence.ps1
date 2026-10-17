param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hostText=Get-BoundedRuntimeLog -Run "$root/.local/runs/HollowKnight/$Session"
$nativeText=Get-BoundedRuntimeLog -Run "$root/.local/runs/NineSols/$Session"
$errors=[Collections.Generic.List[string]]::new()
$terminalFailures=@(($hostText+"`n"+$nativeText) -split "`n" | Where-Object {$_ -match 'SESSION_TERMINAL' -and $_ -notmatch 'reason=PEER_DISCONNECTED.*phase=Ready '})
if($terminalFailures.Count -gt 0 -or ($hostText+$nativeText) -match 'CONTACT_FAILED|HIT_FAILED'){$errors.Add('Session ended with a terminal failure.')}
$interruptions=[regex]::Matches($hostText,'(?m)^(\S+) HK_PV_DASH_INTERRUPTED action=(\d+) seconds=0.5 damaging=False velocity=0 frame=(\d+)')
$successfulRoutes=[Collections.Generic.HashSet[string]]::new()
foreach($hit in $interruptions){
    $action=$hit.Groups[2].Value
    $nativeHit=[regex]::Match($nativeText,"NS_TAICHI_NATIVE attack=$action weakness=True bounce=True frame=(\d+)")
    if(-not $nativeHit.Success -or $nativeText -notmatch "NS_VOLUME_ACTION_RETIRED action=$action sameNativeFrame=$($nativeHit.Groups[1].Value) "){$errors.Add("Missing same-callback native retirement for action $action")}
    $fixture=[regex]::Match($nativeText,"NS_PV_KICK_FIXTURE action=$action route=(kick|aerial-parry)")
    if($fixture.Success){$null=$successfulRoutes.Add($fixture.Groups[1].Value)}
    # Actions restart in each encounter. Never pair a later interruption with
    # the first encounter's recovery just because the numeric action matches.
    $recovery=@([regex]::Matches($hostText,"(?m)^(\S+) HK_PV_DASH_RECOVERED action=$action ") | Where-Object {[DateTime]$_.Groups[1].Value -gt [DateTime]$hit.Groups[1].Value}) | Select-Object -First 1
    if($null -eq $recovery){$errors.Add("Dash action $action did not recover.");continue}
    $elapsed=([DateTime]$recovery.Groups[1].Value-[DateTime]$hit.Groups[1].Value).TotalSeconds
    if($elapsed -lt 0.49 -or $elapsed -gt 0.75){$errors.Add("Incorrect dash stagger duration $elapsed")}
}
foreach($route in @('kick','aerial-parry')){if(-not $successfulRoutes.Contains($route)){$errors.Add("No successful native route $route")}}
$counters=[regex]::Matches($nativeText,'NS_VOLUME_CRIMSON_COUNTER action=(\d+) frame=(\d+)')
if($counters.Count -eq 0){$errors.Add('No native tendril counter.')}
foreach($counter in $counters){
    $action=$counter.Groups[1].Value
    if($nativeText -notmatch "NS_VOLUME_ACTION_RETIRED action=$action sameNativeFrame=$($counter.Groups[2].Value) " -or $hostText -notmatch "HK_PV_TENDRIL_INTERRUPTED action=$action state=Stun Start nativeStagger=True "){$errors.Add("Missing tendril retirement/native stagger for $action")}
}
$reflections=[regex]::Matches($hostText,'HK_PROJECTILE_REFLECTED id=(-?\d+) activation=(\d+) ')
$collisionHits=0
foreach($reflection in $reflections){
    $id=$reflection.Groups[1].Value;$activation=$reflection.Groups[2].Value
    if($nativeText -notmatch "NS_PROJECTILE_RESOLVED id=$id activation=$activation sameNativeFrame=\d+ verdict=parry reflected=True"){$errors.Add("Reflection lacks native parry: $id/$activation")}
    $hits=[regex]::Matches($hostText,"HK_REFLECTED_HIT id=$id activation=$activation before=(\d+) after=(\d+) collision=True ")
    if($hits.Count -gt 1){$errors.Add("Repeated reflected hit: $id/$activation")}
    foreach($hit in $hits){$loss=[int]$hit.Groups[1].Value-[int]$hit.Groups[2].Value;if($loss -gt 0 -and $loss -le 21){$collisionHits++}elseif($loss -ne 0){$errors.Add('Unexpected reflected damage.')}}
}
if($collisionHits -lt 1){$errors.Add('No returned projectile collision damage established.')}
$pauses=[regex]::Matches($hostText,'HK_PARRY_PAUSE sequence=(\d+) ')
if($pauses.Count -eq 0){$errors.Add('No native HK blocking pause.')}
try { & "$PSScriptRoot/Test-CounterFreezeEvidence.ps1" -Session $Session } catch { $errors.Add($_.Exception.Message) }
if($hostText -notmatch 'HK_PV_MANUAL_COUNTER_ONLY enabled=True' -or $hostText -notmatch 'HK_PV_MANUAL_COUNTER_ONLY enabled=False' -or [regex]::Matches($hostText,'HK_PV_MANUAL_COUNTER_CYCLE cycle=\d+ nativeAntic=True').Count -lt 2){$errors.Add('Counter-only mode must enable, repeat native anticipation, and disable.')}
$report=@{session=$Session;dashInterruptions=$interruptions.Count;successfulRoutes=@($successfulRoutes);tendrilCounters=$counters.Count;reflections=$reflections.Count;collisionHits=$collisionHits;blockPauses=$pauses.Count;errors=@($errors);passed=$errors.Count -eq 0;scope='Controlled fixtures; no normal timing, visual or triple-parry feel claim.'}
$report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath "$root/.local/pv-tuning/$Session-tuning-check.json"
if($errors.Count -gt 0){throw ($errors -join '; ')}
Write-Output "PASS: native PV kick retirement/recovery, reflected collision hit, tendril counter/stagger and block freeze/resume. Session=$Session. Human acceptance remains separate."
