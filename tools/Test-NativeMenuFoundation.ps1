[CmdletBinding()]
param([ValidateSet('cancel-loading-late','cancel-loading','ui-inspect','native-ui','ui-flows','menus','vanilla-hk','vanilla-ns','vanilla-active-hk','vanilla-active-ns','crash-hk','crash-ns','quit-hk','quit-ns','delete','recovery','unrecoverable','godhome-yes','godhome-no','classic','steel','classic-death','steel-death','reload','capture-stress','controls','controls-controller','loadout','switch','combat-SoulWarrior','combat-SoulMaster','combat-SoulTyrant','combat-AbsoluteRadiance','combat-RadianceFinish','combat-RadianceRainPerf','combat-RadianceArrowPerf','combat-RadianceIdlePerf','combat-RadianceRainVisual','combat-Collector','combat-Nosk','combat-GruzMother','combat-Endurance-Sweep','combat-Endurance-GruzMother','combat-Endurance-Xero','combat-Endurance-VengeflyKing','combat-Endurance-VengeflyKingAscended','combat-Endurance-BroodingMawlek','combat-Endurance-PureVessel','combat-Endurance-MantisLords','combat-Endurance-SistersOfBattle','combat-Endurance-Grimm','combat-Endurance-NightmareKingGrimm','combat-Endurance-NailsageSly','combat-Endurance-WatcherKnights','combat-Endurance-FalseKnight','combat-Endurance-FailedChampion','combat-Endurance-HornetProtector','combat-Endurance-HornetSentinel','combat-Endurance-BrokenVessel','combat-Endurance-LostKin','combat-Endurance-CrystalGuardian','combat-Endurance-EnragedGuardian','combat-Endurance-DungDefender','combat-Endurance-WhiteDefender','combat-Endurance-HiveKnight','combat-Endurance-TraitorLord','combat-Endurance-MassiveMossCharger','combat-Endurance-SoulWarrior','combat-Endurance-Nosk','combat-Endurance-WingedNosk','combat-Endurance-OroMato','combat-Endurance-Sheo','combat-Endurance-Oblobbles','combat-Endurance-GodTamer','combat-Endurance-Galien','combat-Endurance-Gorb','combat-Endurance-ElderHu','combat-Endurance-Markoth','combat-Endurance-Marmu','combat-Endurance-NoEyes','combat-Endurance-SoulMaster','combat-Endurance-SoulTyrant','combat-Endurance-Collector','combat-Endurance-GreyPrinceZote','combat-Endurance-Flukemarm','combat-Endurance-Uumuu','combat-Endurance-AbsoluteRadiance')][string]$Scenario='menus',
      [ValidateSet('HollowKnight','NineSols')][string]$First='HollowKnight', [ValidateRange(30,10980)][int]$Seconds=100,
      [string]$ExistingFixture='', [string[]]$EndurancePlan=@())
$ErrorActionPreference='Stop'
if($Scenario -ne 'combat-Endurance-Sweep' -and ($Seconds -gt 600 -or $EndurancePlan.Count)){throw 'Extended plan is restricted to continuous endurance.'}
if($Scenario -eq 'combat-Endurance-Sweep' -and ($EndurancePlan.Count -lt 1 -or $EndurancePlan.Count -gt 45 -or @($EndurancePlan | Select-Object -Unique).Count -ne $EndurancePlan.Count -or $Seconds -lt $EndurancePlan.Count*180+120 -or $Seconds -gt $EndurancePlan.Count*240+180)){throw 'Invalid finite continuous endurance plan.'}
$workspace=Split-Path $PSScriptRoot -Parent
if(Get-Process hollow_knight,NineSols -ErrorAction SilentlyContinue){throw 'Close both games before this isolated native fixture.'}
# A manifest can be valid while still pointing at yesterday's plugin. Reject
# that before copying saves or launching either process.
foreach($game in @('HollowKnight','NineSols')) {
    $built=Get-Content -LiteralPath (Join-Path $workspace ".local/build-$game.json") -Raw | ConvertFrom-Json
    $manifest=Get-Content -LiteralPath (Join-Path $workspace ".local/deployments/$game.json") -Raw | ConvertFrom-Json
    $artifact=Join-Path $workspace "src/$game/bin/Release/netstandard2.1/YiGodhome.$game.dll"
    $installed=Join-Path $manifest.root "BepInEx/plugins/YiGodhome.$game.dll"
    if(-not $built.success -or (Get-FileHash -LiteralPath $artifact).Hash -ne $built.sha256 -or
        (Get-FileHash -LiteralPath $installed).Hash -ne $built.sha256){throw "Latest $game build is not deployed. Build and update before testing."}
}
$session=[Guid]::NewGuid().ToString()
$evidence=Join-Path $workspace ".local/pre-v01-native/$session"
$null=New-Item -ItemType Directory -Path $evidence
$applicationHome=Join-Path $evidence 'app/HollowSols'
$saveRoot=Join-Path $applicationHome 'paired/HollowSols'
$null=New-Item -ItemType Directory -Path (Join-Path $applicationHome 'logs/bootstrap-fixture')
[IO.File]::WriteAllText((Join-Path $applicationHome 'logs/bootstrap-fixture/bootstrap.log'), 'Preserved startup infrastructure; outside the paired namespace.')
if($Scenario.StartsWith('combat-') -or $Scenario -in @('cancel-loading-late','cancel-loading','reload','capture-stress','controls','controls-controller','loadout','switch','vanilla-active-hk','vanilla-active-ns','crash-hk','crash-ns','quit-hk','quit-ns','delete','recovery','unrecoverable')) {
    if($ExistingFixture -eq 'current') {
        # The user's existing mod-owned pair is copied, never loaded in place.
        $fixtureSource=Join-Path $env:LOCALAPPDATA 'HollowSols/paired/HollowSols'
        if([IO.File]::ReadAllText((Join-Path $fixtureSource 'namespace')) -ne "HollowSols paired saves schema 1`n"){throw 'Current mod namespace is not recognized.'}
    } else {
        $existingId=[Guid]::Parse($ExistingFixture).ToString()
        $sourceEvidence=Join-Path $workspace ".local/pre-v01-native/$existingId"
        $fixtureSource=(Get-Content -LiteralPath (Join-Path $sourceEvidence 'HollowKnight/native-menu.fixture'))[1]
        if(-not([IO.Path]::GetFullPath($fixtureSource).StartsWith([IO.Path]::GetFullPath($sourceEvidence)+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase))){throw 'Source save root escaped its disposable fixture.'}
    }
    if(-not(Test-Path -LiteralPath (Join-Path $fixtureSource 'namespace'))){throw 'Reload requires an existing disposable fixture namespace.'}
    # Keep the original fixture intact. Bootstrap admits only data below this
    # new run's root, so copy just its mod-owned namespace and four slot trees.
    $null=New-Item -ItemType Directory -Path $saveRoot
    Copy-Item -LiteralPath (Join-Path $fixtureSource 'namespace') -Destination (Join-Path $saveRoot 'namespace')
    for($slot=1;$slot -le 4;$slot++) {
        $sourceSlot=Join-Path $fixtureSource "slot-$slot"
        if(Test-Path -LiteralPath $sourceSlot) {
            foreach($item in @(Get-Item -LiteralPath $sourceSlot)+@(Get-ChildItem -LiteralPath $sourceSlot -Recurse)) {
                if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Linked fixture data is prohibited.'}
            }
            Copy-Item -LiteralPath $sourceSlot -Destination (Join-Path $saveRoot "slot-$slot") -Recurse
        }
    }
    if($Scenario -in @('recovery','unrecoverable')) {
        $generations=@(Get-ChildItem -LiteralPath (Join-Path $saveRoot 'slot-1') -Directory | Where-Object Name -match '^generation-[0-9]{20}-' | Sort-Object Name -Descending)
        if($generations.Count -lt 2){throw 'Recovery comparison requires multiple committed disposable checkpoints.'}
        foreach($generation in $(if($Scenario -eq 'recovery'){@($generations[0])}else{$generations})) {
            $payload=Join-Path $generation.FullName 'hk.payload'; $bytes=[IO.File]::ReadAllBytes($payload); $bytes[0]=$bytes[0] -bxor 1; [IO.File]::WriteAllBytes($payload,$bytes)
        }
    }
} elseif($ExistingFixture){throw 'Existing fixture data is accepted only for disposable pair operations.'}
function SaveFingerprints {
    $records=@()
    $hkRoot=Join-Path $env:USERPROFILE 'AppData/LocalLow/Team Cherry/Hollow Knight'
    $nsRoot=Join-Path $env:USERPROFILE 'AppData/LocalLow/RedCandleGames/NineSols'
    foreach($path in @($hkRoot,$nsRoot)) {
        if(-not(Test-Path -LiteralPath $path)){ $records += [ordered]@{path=$path;absent=$true};continue }
        foreach($file in Get-ChildItem -LiteralPath $path -Recurse -File) {
            if($file.FullName -match '[\\/]user[0-9]' -or $file.FullName -match '[\\/]saveslot[0-9]') {
                $records += [ordered]@{path=$file.FullName;sha256=(Get-FileHash -LiteralPath $file.FullName).Hash;length=$file.Length}
            }
        }
    }
    return ($records | Sort-Object { $_.path } | ConvertTo-Json -Depth 5 -Compress)
}
$before=SaveFingerprints
[IO.File]::WriteAllText((Join-Path $evidence 'vanilla-before.json'),$before)
$processes=@{}
try {
    $games=if($Scenario -eq 'ui-inspect'){@('NineSols')}elseif($Scenario -eq 'native-ui'){@('HollowKnight')}else{@($First, $(if($First -eq 'HollowKnight'){'NineSols'}else{'HollowKnight'}))}
    foreach($game in $games) {
        $manifest=Get-Content -LiteralPath (Join-Path $workspace ".local/deployments/$game.json") -Raw | ConvertFrom-Json
        if((Get-FileHash -LiteralPath $manifest.assemblyPath).Hash -ne $manifest.assemblyHash){throw "$game native assembly changed"}
        foreach($entry in $manifest.files){
            if($entry.relative -eq 'BepInEx\config\BepInEx.cfg'){continue}
            if((Get-FileHash -LiteralPath (Join-Path $manifest.root $entry.relative)).Hash -ne $entry.sha256){throw "Owned deployment changed: $game/$($entry.relative)"}
        }
        $folder=Join-Path $evidence $game
        $null=New-Item -ItemType Directory -Path $folder
        $delay=if($Scenario -eq 'menus' -and $game -ne $First){8}else{0}
        [IO.File]::WriteAllLines((Join-Path $folder 'native-menu.fixture'),@($session,$saveRoot,$Scenario,"$delay",$game))
        if($Scenario -eq 'combat-Endurance-Sweep'){[IO.File]::WriteAllLines((Join-Path $folder 'endurance-plan.request'),@($session)+$EndurancePlan)}
        $ticket=Join-Path $manifest.root 'BepInEx/core/YiGodhome.session'
        if(Test-Path -LiteralPath $ticket){
            $oldTicket=[IO.File]::ReadAllLines($ticket)
            $oldId=[Guid]::Empty; $oldExpiry=[DateTime]::MinValue
            if($oldTicket.Length -ne 5 -or -not ([Guid]::TryParse($oldTicket[0],[ref]$oldId)) -or
                -not ([DateTime]::TryParse($oldTicket[1],[ref]$oldExpiry)) -or $oldExpiry.ToUniversalTime() -gt [DateTime]::UtcNow -or
                $oldTicket[4] -ne 'menu-test'){throw "Unconsumed launch ticket: $game"}
            $oldFolder=Join-Path $workspace ".local/pre-v01-native/$oldId/$game"
            if(-not(Test-Path -LiteralPath (Join-Path $oldFolder 'native-menu.fixture'))){throw 'Expired ticket is not owned by a known disposable fixture.'}
            Move-Item -LiteralPath $ticket -Destination (Join-Path $oldFolder ('unconsumed-ticket-'+[Guid]::NewGuid().ToString('N')+'.txt'))
        }
        [IO.File]::WriteAllLines($ticket,@($session,[DateTime]::UtcNow.AddMinutes(2).ToString('O'),"$Seconds",(Join-Path $folder 'bootstrap.log'),'menu-test'))
        $launch=[Diagnostics.ProcessStartInfo]::new('C:/Steam/steam.exe')
        $launch.UseShellExecute=$false;$launch.CreateNoWindow=$true
        $launch.ArgumentList.Add('-applaunch');$launch.ArgumentList.Add($(if($game -eq 'HollowKnight'){'367520'}else{'1809540'}))
        $launch.ArgumentList.Add('-logFile');$launch.ArgumentList.Add((Join-Path $folder 'native-player.log'))
        $sender=[Diagnostics.Process]::Start($launch)
        # Sender exit is not client receipt: Steam can coalesce back-to-back
        # command notifications. Require its receipt for this exact fixture
        # before the second dispatch, never a game process/title/intro wait.
        # A newly started Steam client can retain this process after dispatch.
        # Its exact request/game receipt owns success, not sender process exit.
        $sender.Dispose()
        $appId=if($game -eq 'HollowKnight'){'367520'}else{'1809540'}
        $receiptDeadline=[DateTime]::UtcNow.AddSeconds(10);$received=$false
        do {
            $lines=Get-Content -LiteralPath 'C:/Steam/logs/console_log.txt' -Tail 128
            $received=@($lines|Where-Object {$_.Contains($session) -and
                ($_ -match 'ExecCommandLine:' -and $_ -match "-applaunch $appId\b" -or $_ -match "Game process added : AppID $appId\b")}).Count -gt 0
            if(-not$received){Start-Sleep -Milliseconds 100}
        }while(-not$received -and [DateTime]::UtcNow -lt $receiptDeadline)
        [IO.File]::AppendAllText((Join-Path $evidence 'native-launch-receipts.log'),"$game clientReceived=$received utc=$([DateTime]::UtcNow.ToString('O'))`n")
        if(-not$received){throw "Steam did not acknowledge the $game command; no duplicate launch issued."}
    }
    # Dispatch both Steam launches before waiting for either native title.
    $deadline=[DateTime]::UtcNow.AddSeconds(90)
    do {
        foreach($game in $games) {
            $name=if($game -eq 'HollowKnight'){'hollow_knight'}else{'NineSols'}
            $candidates=@(Get-Process $name -ErrorAction SilentlyContinue)
            if($candidates.Count -gt 1){throw "Ambiguous $game process"}
            if($candidates.Count -eq 1){$processes[$game]=$candidates[0]}
        }
        if($processes.Count -lt $games.Count){Start-Sleep -Milliseconds 250}
    }while($processes.Count -lt $games.Count -and [DateTime]::UtcNow -lt $deadline)
    foreach($game in $games){if(-not$processes.ContainsKey($game)){throw "$game did not start"}}
    $deadline=[DateTime]::UtcNow.AddSeconds($Seconds+45)
    $forcedCrash=$false
    do {
        $crashTrigger=Join-Path $evidence 'fixture-crash.trigger'
        if($Scenario -in @('crash-hk','crash-ns') -and -not$forcedCrash -and (Test-Path -LiteralPath $crashTrigger) -and [IO.File]::ReadAllText($crashTrigger) -eq $session) {
            $crashSide=if($Scenario -eq 'crash-hk'){'HollowKnight'}else{'NineSols'}
            $crashProcess=$processes[$crashSide]
            if($crashProcess.HasExited){throw 'The disposable process exited before its authorized abrupt-termination request.'}
            $crashProcess.Kill(); if(-not$crashProcess.WaitForExit(5000)){throw 'The exact disposable process did not terminate.'}
            $forcedCrash=$true
        }
        $alive=@($processes.Values | Where-Object {-not $_.HasExited})
        foreach($game in $games) {
            $failureReceipt=Join-Path $evidence "$game/menu-fixture.failed"
            $hostReceipt=Join-Path $evidence 'HollowKnight/menu-fixture.complete'
            $hostCompleted=(Test-Path -LiteralPath $hostReceipt) -and [IO.File]::ReadAllText($hostReceipt) -eq $session
            if((Test-Path -LiteralPath $failureReceipt) -and (Get-Content -LiteralPath $failureReceipt -TotalCount 1) -eq $session -and
                -not($game -eq 'NineSols' -and $hostCompleted -and ($Scenario.StartsWith('combat-') -or $Scenario -in @('loadout','controls','controls-controller','reload','capture-stress')))){throw "$game reported a native fixture failure; ending immediately. Inspect $evidence"}
        }
        if($alive.Count -eq 0){break}
        $receiptSide=if($Scenario -eq 'crash-hk'){'NineSols'}else{'HollowKnight'}
        if($Scenario -notin @('cancel-loading-late','cancel-loading','menus','vanilla-hk','vanilla-ns','vanilla-active-hk','vanilla-active-ns','quit-hk','quit-ns') -and (Test-Path -LiteralPath (Join-Path $evidence "$receiptSide/menu-fixture.complete"))){
            foreach($process in $alive){$null=$process.CloseMainWindow()}
        }
        Start-Sleep -Milliseconds 500
    }while([DateTime]::UtcNow -lt $deadline)
    foreach($game in $processes.Keys) {
        $process=$processes[$game]
        if(-not$process.HasExited){$process.Kill();$null=$process.WaitForExit(5000);throw "$game fixture exceeded its finite deadline"}
        if($Scenario -in @('crash-hk','crash-ns') -and -not$forcedCrash){throw 'The authorized crash fixture did not take its expected abrupt-termination path.'}
        $manifest=Get-Content -LiteralPath (Join-Path $workspace ".local/deployments/$game.json") -Raw | ConvertFrom-Json
        Copy-Item -LiteralPath (Join-Path $manifest.root 'BepInEx/LogOutput.log') -Destination (Join-Path $evidence "$game/bepinex.log")
    }
    $after=SaveFingerprints
    [IO.File]::WriteAllText((Join-Path $evidence 'vanilla-after.json'),$after)
    if($after -ne $before){throw 'Vanilla save fingerprints changed. Stop native fixtures and investigate; do not repair or overwrite user data.'}
    # BepInEx buffers its tail and can lose the final success line at native quit.
    # The bounded component writes a scoped completion receipt synchronously only
    # after its actual state/UI assertions. Never infer success from compilation.
    foreach($game in $(if($Scenario -in @('ui-inspect','crash-hk')){@('NineSols')}elseif($Scenario -in @('cancel-loading-late','cancel-loading','menus','vanilla-hk','vanilla-ns','vanilla-active-hk','vanilla-active-ns','quit-hk','quit-ns')){@('HollowKnight','NineSols')}else{@('HollowKnight')})) {
        $receipt=Join-Path $evidence "$game/menu-fixture.complete"
        if(-not(Test-Path -LiteralPath $receipt) -or [IO.File]::ReadAllText($receipt) -ne $session){throw "Native fixture incomplete. Inspect $evidence"}
    }
    Write-Output "PASS: bounded native $Scenario fixture; vanilla save fingerprints unchanged. Evidence: $evidence"
} finally {
    # A failed first-process check must not leave the second game untracked.
    # Include only processes carrying this fixture's exact log-path identity.
    foreach($game in $games){if(-not$processes.ContainsKey($game)){
        $name=if($game -eq 'HollowKnight'){'hollow_knight'}else{'NineSols'}
        foreach($candidate in @(Get-Process $name -ErrorAction SilentlyContinue)){
            $info=Get-CimInstance Win32_Process -Filter "ProcessId=$($candidate.Id)"
            if($info.CommandLine -and $info.CommandLine.Contains($session)){$processes[$game]=$candidate}
        }
    }}
    foreach($process in $processes.Values){if(-not$process.HasExited){$null=$process.CloseMainWindow();if(-not$process.WaitForExit(5000)){$process.Kill();$null=$process.WaitForExit(5000)}}}
    $after=SaveFingerprints
    [IO.File]::WriteAllText((Join-Path $evidence 'vanilla-after.json'),$after)
    if($after -ne $before){throw 'Vanilla save fingerprints changed. Stop fixtures; do not repair or overwrite user data.'}
}
