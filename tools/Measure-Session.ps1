[CmdletBinding()]
param([Parameter(Mandatory)][Guid]$Session, [switch]$FixedIdle, [int]$WarmupSeconds=10, [int]$SampleSeconds=30)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
if($WarmupSeconds -lt 5 -or $SampleSeconds -lt 20){throw 'Use at least five seconds warm-up and twenty seconds sampling.'}
function Rows($lines,$marker) {
    foreach($line in $lines){
        if($line -notmatch "^(\d{4}-\d\d-\d\dT[^ ]+Z) $marker "){continue}
        $row=@{utc=[DateTime]::Parse($Matches[1]).ToUniversalTime()}
        foreach($match in [regex]::Matches($line,'([A-Za-z0-9]+)=([^ ]+)')){$row[$match.Groups[1].Value]=$match.Groups[2].Value}
        [pscustomobject]$row
    }
}
function Number($value){[double]::Parse($value,[Globalization.CultureInfo]::InvariantCulture)}
$hostLines=Get-BoundedRuntimeLog -Run "$root/.local/runs/HollowKnight/$Session" -Lines
$anchor=@(Rows $hostLines 'PERFORMANCE_READY')
if($FixedIdle -and $anchor.Count -ne 1){throw 'Fixed idle workload requires exactly one ready marker.'}
$start=if($FixedIdle){$anchor[0].utc.AddSeconds($WarmupSeconds)}else{[DateTime]::Parse(($hostLines | Where-Object {$_ -match 'FRAME_PERF'} | Select-Object -First 1).Split(' ')[0]).ToUniversalTime().AddSeconds($WarmupSeconds)}
$end=$start.AddSeconds($SampleSeconds)
$report=@{session="$Session"; workload=$(if($FixedIdle){$anchor[0].workload}else{'observational-not-comparable'}); startUtc=$start; endUtc=$end; warmupSeconds=$WarmupSeconds; sampleSeconds=$SampleSeconds; games=@{}}
foreach($game in @('HollowKnight','NineSols')) {
    $run="$root/.local/runs/$game/$Session"; $lines=Get-BoundedRuntimeLog -Run $run -Lines
    $frames=@(Rows $lines 'FRAME_PERF' | Where-Object {$_.utc -ge $start -and $_.utc -le $end})
    if($frames.Count -lt [Math]::Floor($SampleSeconds/2)-2){throw "$game sample window incomplete"}
    $window=@($lines | Where-Object {$_ -match '^(\d{4}-\d\d-\d\dT[^ ]+Z) ' -and [DateTime]::Parse($Matches[1]).ToUniversalTime() -ge $start -and [DateTime]::Parse($Matches[1]).ToUniversalTime() -le $end})
    # Event names only: GG_Failed_Champion is a scene, not a failure event.
    if($window -match '^\S+ (?:[A-Z_]+_FAILED|SESSION_TERMINAL)(?: |$)'){throw "$game failed during measurement"}
    $scenes=@($frames.scene | Select-Object -Unique)
    if($FixedIdle -and ($scenes.Count -ne 1 -or ($game -eq 'HollowKnight' -and $scenes[0] -ne 'GG_Atrium'))){throw 'Benchmark scene changed.'}
    $resource=@(Get-Content -LiteralPath "$run/resource-samples.jsonl" | ForEach-Object {ConvertFrom-Json $_} | Where-Object {([DateTime]$_.utc).ToUniversalTime() -ge $start -and ([DateTime]$_.utc).ToUniversalTime() -le $end})
    if($resource.Count -lt $SampleSeconds-5){throw "$game process samples incomplete"}
    $frameCount=0; $frameMs=0.0
    foreach($f in $frames){$count=Number $f.frames; $frameCount+=$count; $frameMs+=(Number $f.meanMs)*$count}
    $transfers=@(Rows $window '(?:VISUAL|HUD|MENU|REST|SUMMON_UI|BOSS_HUD)_(?:SENT|RECEIVED)')
    $captures=@(Rows $window 'NS_VISUAL_COST')
    $restCaptures=@(Rows $window 'NS_REST_COST')
    $gameReport=@{scene=$scenes; frames=$frameCount; meanFrameMs=$frameMs/$frameCount;
        unityAllocationAvailable=(@($frames | Where-Object {$null -ne $_.unityAllocatedBytes -and (Number $_.unityAllocatedBytes) -ge 0}).Count -eq $frames.Count);
        windowSettings=@($frames | ForEach-Object {"$($_.width)x$($_.height) vsync=$($_.vsync) cap=$($_.frameCap)"} | Select-Object -Unique);
        maxWindowP95Ms=($frames | ForEach-Object {Number $_.p95Ms} | Measure-Object -Maximum).Maximum;
        maxWindowP99Ms=($frames | ForEach-Object {Number $_.p99Ms} | Measure-Object -Maximum).Maximum;
        maxFrameMs=($frames | ForEach-Object {Number $_.maxMs} | Measure-Object -Maximum).Maximum;
        managedHeapRangeMB=@{minimum=($frames | ForEach-Object {Number $_.managedMB} | Measure-Object -Minimum).Minimum; maximum=($frames | ForEach-Object {Number $_.managedMB} | Measure-Object -Maximum).Maximum};
        gc0=($frames | ForEach-Object {Number $_.gc0} | Measure-Object -Sum).Sum;
        gc1=($frames | ForEach-Object {Number $_.gc1} | Measure-Object -Sum).Sum;
        gc2=($frames | ForEach-Object {Number $_.gc2} | Measure-Object -Sum).Sum;
        mainAllocationAvailable=(@($frames | Where-Object {(Number $_.mainAllocatedBytes) -ge 0}).Count -eq $frames.Count);
        peakPrivateMB=($resource.privateBytes | Measure-Object -Maximum).Maximum/1MB;
        privateDeltaMB=($resource[-1].privateBytes-$resource[0].privateBytes)/1MB;
        handleDelta=$resource[-1].handles-$resource[0].handles; threadDelta=$resource[-1].threads-$resource[0].threads;
        cpuCoreEquivalent=($resource[-1].cpuMs-$resource[0].cpuMs)/($SampleSeconds*1000);
        pluginGrowthBytes=$resource[-1].pluginBytes-$resource[0].pluginBytes;
        loaderLogGrowthBytes=$resource[-1].loaderLogBytes-$resource[0].loaderLogBytes;
        peakReceiverOwners=($frames | ForEach-Object {Number $_.receiverOwners} | Measure-Object -Maximum).Maximum;
        settings=@(Rows $lines 'SESSION_BUILD'); transferSamples=$transfers.Count;
        maxSampledTransferMs=($transfers | ForEach-Object {Number $_.transferMs} | Measure-Object -Maximum).Maximum;
        captureWindows=$captures.Count}
    if($gameReport.unityAllocationAvailable){$gameReport.unityAllocatedMB=($frames | ForEach-Object {Number $_.unityAllocatedBytes} | Measure-Object -Sum).Sum/1MB}
    if($captures.Count){$gameReport.captureMeanMs=($captures | ForEach-Object {Number $_.meanMs} | Measure-Object -Average).Average; $gameReport.readbackMeanMs=($captures | ForEach-Object {Number $_.readbackMeanMs} | Measure-Object -Average).Average}
    if($restCaptures.Count){$gameReport.restCaptureMeanMs=($restCaptures | ForEach-Object {Number $_.meanMs} | Measure-Object -Average).Average; $gameReport.restReadbackMeanMs=($restCaptures | ForEach-Object {Number $_.readbackMeanMs} | Measure-Object -Average).Average; $gameReport.restCaptureMaxMs=($restCaptures | ForEach-Object {Number $_.maxMs} | Measure-Object -Maximum).Maximum}
    if($FixedIdle){
        $poses=@(Rows $window 'IPC_POSE_(?:SENT|RECEIVED)')
        if($poses.Count -lt 3){throw 'Insufficient pose samples for fixed position.'}
        foreach($axis in @('x','y')){ $range=$poses | ForEach-Object {Number $_.$axis} | Measure-Object -Minimum -Maximum; if($range.Maximum-$range.Minimum -gt 0.02){throw "Fixed $axis position drifted"} }
        $gameReport.position=@{x=$poses[0].x;y=$poses[0].y;geometry=$poses[0].geometryFrame}
    }
    $report.games[$game]=$gameReport
}
$destination="$root/.local/input-tests/$Session/performance.json"
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $destination
Write-Output "Measured both games: $destination. Window percentiles are not whole-session percentiles; this is not endurance or controller acceptance."
