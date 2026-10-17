$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
if(Get-Process hollow_knight,NineSols -ErrorAction SilentlyContinue){throw 'Close both games first.'}
& "$PSScriptRoot/Start-CombatPlaytest.ps1" -Mode HubCombat -CheckOnly
$out=Join-Path $root '.local/foundations/normal-startup.stdout.log'
$err=Join-Path $root '.local/foundations/normal-startup.stderr.log'
$started=[DateTime]::UtcNow
$helper=Start-Process -FilePath (Join-Path $PSHOME 'pwsh.exe') -ArgumentList @('-NoProfile','-File',('"'+$PSScriptRoot+'/Test-InputBridge.ps1"'),'-HubCombat','-Human','-Unlimited') -WindowStyle Hidden -PassThru -RedirectStandardOutput $out -RedirectStandardError $err
$session=$null; $owned=@(); $ready=$false
try {
    $deadline=[DateTime]::UtcNow.AddSeconds(100)
    while([DateTime]::UtcNow -lt $deadline) {
        if($helper.HasExited){throw 'Normal launcher exited before readiness.'}
        $output=Get-Content -LiteralPath $out -Raw
        if(-not $session -and $output -match 'Protected input test session=([\w-]+)'){$session=$Matches[1]}
        if($session) {
            $hostPath="$root/.local/runs/HollowKnight/$session/plugin.log"
            $nativePath="$root/.local/runs/NineSols/$session/plugin.log"
            if((Test-Path -LiteralPath $hostPath) -and (Test-Path -LiteralPath $nativePath)) {
                $hostText=Get-Content -LiteralPath $hostPath -Raw
                $nativeText=Get-Content -LiteralPath $nativePath -Raw
                if(($hostText+$nativeText) -match '(?m)^\S+ (?:SESSION_TERMINAL|[A-Z_]+_FAILED)(?: |$)'){throw 'Normal startup failed.'}
                if($hostText -match 'HK_HUB_STATE scene=GG_Atrium [^\r\n]*ready=True' -and $hostText -match 'HK_VISUAL_PRESENTED' -and
                    $nativeText -match 'NS_CANVAS_READY scene=YiGodhomeCanvas ' -and
                    $nativeText -match 'NS_MEMORY_SAMPLE .*scene=YiGodhomeCanvas'){$ready=$true;break}
            }
        }
        Start-Sleep -Milliseconds 250
    }
    if(-not $ready){throw 'Normal visible hub startup timed out.'}
    foreach($game in @('HollowKnight','NineSols')) {
        $run="$root/.local/runs/$game/$session"
        $configuration=@(Get-Content -LiteralPath "$run/plugin.log" | Where-Object {$_ -match 'SESSION_CONFIGURATION capabilities='})
        if($configuration.Count -ne 1 -or $configuration[0] -notmatch 'unlimited-human' -or $configuration[0] -match 'canvas-baseline|content-audit|canvas-service-audit|scripted-input|cheat-probe|failure-probe|switch-probe|performance-probe|lever-probe|travel-probe|pause-probe|void-probe|rest-probe|wall-probe|kick-probe|retry-probe|injury-refusal'){throw 'Normal mode acquired diagnostic capability.'}
        if((Get-Content -LiteralPath "$run/run.json" -Raw | ConvertFrom-Json).seconds -ne 0){throw 'Normal session is time limited.'}
        $sample=Get-Content -LiteralPath "$run/resource-samples.jsonl" -Tail 1 | ConvertFrom-Json
        $process=Get-Process -Id $sample.pid
        if($process.StartTime.ToUniversalTime() -lt $started -or $process.ProcessName -notin @('hollow_knight','NineSols')){throw 'Ambiguous process ownership.'}
        $owned+=$process
    }
    foreach($process in $owned){if(-not $process.CloseMainWindow()){throw 'Native game did not accept window close.'}}
    foreach($process in $owned){if(-not $process.WaitForExit(30000)){throw 'Native game did not finish normal window-close shutdown.'}}
    if(-not $helper.WaitForExit(30000) -or $helper.ExitCode -ne 0){throw 'Normal launcher did not collect completed shutdown.'}
    & "$PSScriptRoot/Test-CanvasEvidence.ps1" -Session $session -NoDonors
    Write-Output "PASS: unlimited normal Steam startup, visible Yi, no diagnostic capabilities, ordinary window-close shutdown. Session=$session. This is startup/teardown, not physical-controller or endurance acceptance."
} finally {
    foreach($process in $owned){if(-not $process.HasExited){$null=$process.CloseMainWindow()}}
}
