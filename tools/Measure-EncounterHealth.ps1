param([Parameter(Mandatory)][Guid]$Session)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
. "$PSScriptRoot/Read-BoundedRuntimeLog.ps1"
$hostLines=Get-BoundedRuntimeLog "$root/.local/runs/HollowKnight/$Session" -Lines
$returns=@($hostLines | Where-Object {$_ -match 'HK_HUB_COMBAT_RETURN_READY .*presented=True nativeWakeComplete=True'} | ForEach-Object {[DateTime]::Parse($_.Split(' ')[0]).ToUniversalTime()})
if($returns.Count -lt 2){throw 'Two completed visible returns are required.'}
$report=@{session="$Session"; workload='observational-combat-and-return'; games=@{}}
foreach($game in @('HollowKnight','NineSols')) {
    $run="$root/.local/runs/$game/$Session"
    $lines=Get-BoundedRuntimeLog $run -Lines
    $samples=@(Get-Content -LiteralPath "$run/resource-samples.jsonl" | ForEach-Object {ConvertFrom-Json $_})
    $windows=@()
    foreach($return in $returns) {
        $start=$return.AddSeconds(2); $end=$return.AddSeconds(7)
        $window=@($samples | Where-Object {([DateTime]$_.utc).ToUniversalTime() -ge $start -and ([DateTime]$_.utc).ToUniversalTime() -le $end})
        if($window.Count -lt 4){throw "$game has an incomplete post-return sample window."}
        $windows+=@{startUtc=$start;endUtc=$end;handles=($window.handles | Measure-Object -Average).Average;
            threads=($window.threads | Measure-Object -Average).Average;privateMB=($window.privateBytes | Measure-Object -Average).Average/1MB;
            workingMB=($window.workingBytes | Measure-Object -Average).Average/1MB}
    }
    $handleDelta=$windows[-1].handles-$windows[0].handles; $threadDelta=$windows[-1].threads-$windows[0].threads
    $owners=@([regex]::Matches(($lines -join "`n"),'FRAME_PERF[^\r\n]* receiverOwners=(\d+)') | ForEach-Object {[int]$_.Groups[1].Value})
    $peak=($owners | Measure-Object -Maximum).Maximum
    if($peak -gt 64 -or $handleDelta -gt 8 -or $threadDelta -gt 4){throw "$game exceeded the provisional receiver/handle/thread budget."}
    if($game -eq 'NineSols' -and @($lines | Where-Object {$_ -match 'NS_SUMMON_DISPOSE retained=0 active=0 pending=0'}).Count -lt $returns.Count){throw 'Native owners did not return to zero for every encounter.'}
    $report.games[$game]=@{postReturnWindows=$windows;handleDelta=$handleDelta;threadDelta=$threadDelta;peakSampledReceiverOwners=$peak;
        peakPrivateMB=($samples.privateBytes | Measure-Object -Maximum).Maximum/1MB;peakWorkingMB=($samples.workingBytes | Measure-Object -Maximum).Maximum/1MB;
        finalPluginBytes=$samples[-1].pluginBytes;finalLoaderLogBytes=$samples[-1].loaderLogBytes}
}
$path="$root/.local/input-tests/$Session/encounter-health.json"
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $path
Write-Output "PASS: $($returns.Count) completed returns; post-return handle/thread and retained-owner budgets. Report=$path. Private memory is reported without a flat-memory or endurance claim."
