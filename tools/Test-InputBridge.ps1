[CmdletBinding()]
param([ValidateSet('Preparing','Active','Victory','Defeated','Returned')][string]$FailureBoundary, [ValidateRange(0,600)][int]$DiagnosticSeconds=0, [switch]$PerformanceProbe, [switch]$CanvasBaseline, [switch]$SwitchProbe, [ValidateRange(0,4)][int]$Pantheon=0, [ValidateRange(0,20)][int]$PantheonStart=0, [switch]$PureVesselProbe, [switch]$ReflectionFinishProbe, [switch]$MarkothShieldProbe, [switch]$MarkothProjectileProbe, [switch]$HomingProbe, [switch]$StaggerProbe, [switch]$ConfigurationProbe, [switch]$TraceProbe, [switch]$HudReferenceProbe, [switch]$PauseProbe, [switch]$Human, [switch]$Unlimited, [switch]$RestProbe, [switch]$LeverProbe, [switch]$TravelProbe, [switch]$CheatProbe, [switch]$InvulnerableProbe, [switch]$KickProbe, [switch]$TalismanTraversalProbe, [switch]$WallProbe, [switch]$VoidProbe, [switch]$VisualProbe, [switch]$ProxyProbe, [switch]$SequentialStartup, [switch]$ArenaProbe, [switch]$HubProbe, [switch]$HubCombat, [switch]$ContactBridge, [switch]$TargetProbe, [switch]$HitProbe, [switch]$LifecycleProbe, [switch]$RetryProbe, [switch]$EffectsProbe, [switch]$CombatProbe, [switch]$CrimsonProbe, [switch]$RecoveryProbe, [ValidateSet('GruzMother','Xero','VengeflyKing','BroodingMawlek','PureVessel','MantisLords','SistersOfBattle','Grimm','NightmareKingGrimm','NailsageSly','WatcherKnights','HornetProtector','HornetSentinel','BrokenVessel','LostKin','CrystalGuardian','EnragedGuardian','DungDefender','WhiteDefender','HiveKnight','TraitorLord','MassiveMossCharger','SoulWarrior','Nosk','WingedNosk','OroMato','Sheo','Oblobbles','GodTamer','Galien','Gorb','ElderHu','Markoth','Marmu','NoEyes','SoulMaster','SoulTyrant','Collector','GreyPrinceZote','Flukemarm','FalseKnight','FailedChampion','Uumuu','AbsoluteRadiance')][string]$Boss = 'GruzMother', [ValidateRange(0,2)][int]$Tier=0)
$ErrorActionPreference = 'Stop'
if ($Pantheon) { if ($Human -or $Unlimited -or $PerformanceProbe -or $SwitchProbe -or $RestProbe -or $TravelProbe -or $FailureBoundary) { throw 'Pantheon requires a separate bounded scripted diagnostic.' }; $HubCombat=$true }
if ($PantheonStart -and -not $Pantheon) { throw 'PantheonStart requires Pantheon.' }
if ($PauseProbe) { if ($Human -or $Unlimited -or $PerformanceProbe -or $FailureBoundary -or $PureVesselProbe -or $ReflectionFinishProbe -or $TravelProbe) { throw 'PauseProbe requires separate bounded scripted combat.' }; if (-not $PSBoundParameters.ContainsKey('Boss')) { $Boss='PureVessel' }; $HubCombat=$CheatProbe=$true }
if ($CanvasBaseline -and (-not $PerformanceProbe -or -not $HubCombat)) { throw 'CanvasBaseline requires fixed idle HubCombat benchmark.' }
if ($ReflectionFinishProbe) { if ($Human -or $Unlimited -or $PerformanceProbe -or $FailureBoundary -or $PureVesselProbe) { throw 'Reflected finish is a separate bounded scripted diagnostic.' }; $Boss='PureVessel'; $HubCombat=$CheatProbe=$true }
if ($PureVesselProbe) { if ($Human -or $Unlimited -or $PerformanceProbe -or $FailureBoundary) { throw 'PV probe is bounded scripted only.' }; $Boss='PureVessel'; $HubCombat=$CheatProbe=$true }
if ($PerformanceProbe) { if ($Human -or $Unlimited -or $CheatProbe -or $RestProbe -or $WallProbe -or $VoidProbe -or $LeverProbe -or $TravelProbe -or $RetryProbe -or $LifecycleProbe -or $CrimsonProbe -or $FailureBoundary -or $SwitchProbe -or $DiagnosticSeconds -gt 0) { throw 'PerformanceProbe is a bounded idle benchmark without grants or fixtures.' }; $HubProbe=$true }
if ($FailureBoundary) { if ($Human -or $Unlimited -or $PerformanceProbe) { throw 'Failure injection requires bounded scripted combat.' }; $HubCombat=$true; if ($FailureBoundary -in @('Victory','Returned')) { $CheatProbe=$true } }
if ($SwitchProbe) { if ($Human -or $Unlimited -or $PerformanceProbe -or $FailureBoundary) { throw 'SwitchProbe requires an isolated scripted combat diagnostic.' }; $HubCombat=$CheatProbe=$true }
$workspace = Split-Path $PSScriptRoot -Parent
if ($Unlimited -and -not $Human) { throw 'Unlimited sessions require -Human.' }
if ($VoidProbe) { if ($Human -or $Unlimited -or $CheatProbe) { throw "VoidProbe is bounded scripted without damage grants." }; $HubCombat=$true }
if ($WallProbe) { if ($Human) { throw 'WallProbe is scripted only.' }; $HubCombat = $true }
if ($StaggerProbe) { if ($Human -or $Unlimited -or $Boss -notin @('Grimm','NightmareKingGrimm','FalseKnight','FailedChampion') -or $Pantheon -or $HomingProbe -or $MarkothShieldProbe -or $MarkothProjectileProbe) { throw 'StaggerProbe requires bounded standalone Grimm/armor-family combat.' }; $HubCombat = $CheatProbe = $true }
if ($HomingProbe) { if ($Human -or $Unlimited -or $Boss -ne 'WatcherKnights' -or $Pantheon -or $MarkothShieldProbe -or $MarkothProjectileProbe) { throw 'HomingProbe requires bounded standalone Watcher Knights.' }; $HubCombat = $CheatProbe = $true }
if ($MarkothProjectileProbe) { if ($Human -or $Boss -ne 'Markoth' -or $Pantheon -or $MarkothShieldProbe) { throw 'MarkothProjectileProbe is a separate bounded Markoth diagnostic.' }; $HubCombat = $CheatProbe = $true }
if ($MarkothShieldProbe) { if ($Human -or $Boss -ne 'Markoth' -or $Pantheon) { throw 'MarkothShieldProbe is a bounded standalone Markoth diagnostic.' }; $HubCombat = $CheatProbe = $true }
if ($HudReferenceProbe) { $TraceProbe=$true };
if ($TraceProbe) { if ($Human -or $Unlimited) { throw 'TraceProbe is bounded scripted-only.' }; $HubCombat = $true }
if ($TalismanTraversalProbe) { if ($Human -or $Unlimited -or $Boss -ne 'SistersOfBattle' -or $Pantheon -or $KickProbe) { throw 'TalismanTraversalProbe requires bounded standalone Sisters combat.' }; $HubCombat = $CheatProbe = $true }
if ($InvulnerableProbe) { if ($Human -or $Unlimited -or $Tier -ne 2 -or $PerformanceProbe -or $FailureBoundary -or $KickProbe -or $CrimsonProbe -or $PureVesselProbe -or $ReflectionFinishProbe -or $MarkothShieldProbe -or $MarkothProjectileProbe -or $StaggerProbe -or $TalismanTraversalProbe -or $Pantheon) { throw 'InvulnerableProbe is bounded Radiant victory-only.' }; $HubCombat = $CheatProbe = $true }
if ($KickProbe) { if ($Human -or $Boss -notin @('VengeflyKing','MantisLords','SistersOfBattle','WatcherKnights','HornetProtector','HornetSentinel','NightmareKingGrimm','MassiveMossCharger','HiveKnight','BrokenVessel','LostKin','Nosk','WingedNosk','SoulWarrior','TraitorLord','GodTamer','DungDefender','WhiteDefender','NailsageSly','OroMato','GreyPrinceZote')) { throw 'KickProbe requires a bounded native kick profile.' }; $HubCombat = $CheatProbe = $true }
if ($TravelProbe) { if ($Human -or $Unlimited -or $CheatProbe) { throw 'TravelProbe is bounded scripted without grants.' }; $HubCombat=$true }
if ($LeverProbe) { if ($Human -or $Unlimited -or $CheatProbe) { throw 'LeverProbe is bounded scripted without grants.' }; $HubCombat=$true }
if ($RestProbe -and $Human) { throw 'RestProbe is scripted only.' }
if ($RestProbe) { $HubCombat = $true }
if ($CheatProbe -and ($Human -or -not $HubCombat)) { throw 'CheatProbe requires a scripted HubCombat diagnostic.' }
if ($HubCombat) { $HubProbe = $CombatProbe = $EffectsProbe = $true }
if ($HubProbe -and -not $HubCombat) {
    if ($ArenaProbe -or $ContactBridge -or $HitProbe -or $CombatProbe -or $RetryProbe -or $CrimsonProbe -or $RecoveryProbe) { throw 'Hub traversal is a separate diagnostic gate.' }
    $ProxyProbe = $EffectsProbe = $true
}
if ($RecoveryProbe) {
    if ($Human -or $Boss -ne 'Xero' -or $HitProbe -or $CombatProbe -or $RetryProbe -or $CrimsonProbe) { throw 'RecoveryProbe is a bounded scripted Xero contact/presentation check.' }
    $ContactBridge = $true; $EffectsProbe = $true
}
if ($CrimsonProbe -and $Boss -eq 'GruzMother') { throw 'Gruz bounce is now Tai-Chi; the historical crimson fixture is superseded. Use protected HubCombat for current gameplay.' }
if ($CrimsonProbe -and $Human) { throw 'CrimsonProbe is an automated input diagnostic. Use the normal human combat test for controller playtesting.' }
if ($CrimsonProbe) { $CombatProbe = $true; $EffectsProbe = $true }
if ($EffectsProbe) { $VisualProbe = $true }
if ($CombatProbe) { $HitProbe = $true }
if ($RetryProbe) { $LifecycleProbe = $true }
if ($LifecycleProbe) { $HitProbe = $true }
if ($HitProbe) { $TargetProbe = $true }
if ($TargetProbe) { $ContactBridge = $true }
if ($ContactBridge) { $ArenaProbe = $true }
if ($ArenaProbe) { $ProxyProbe = $true }
if ($ProxyProbe) { $VisualProbe = $true }
if (Get-Process hollow_knight,NineSols -ErrorAction SilentlyContinue) { throw 'Close both games first.' }
if ($DiagnosticSeconds -ne 0 -and ($Human -or -not $HubCombat -or ($DiagnosticSeconds -lt 200 -and (-not $Pantheon -or $DiagnosticSeconds -lt 60)))) { throw 'Diagnostics require scripted HubCombat: 200..600 seconds, or 60..600 for Pantheon boundaries.' }
$session = [Guid]::NewGuid().ToString()
$logRoot = Join-Path $workspace ".local/input-tests/$session"
$null = New-Item -ItemType Directory -Path $logRoot -Force
$helpers = @()
$launchOrder = if ($SequentialStartup) { @('HollowKnight','NineSols') } else { @('NineSols','HollowKnight') }
foreach ($game in $launchOrder) {
    $seconds = if ($game -eq 'HollowKnight') { if ($Human) { 120 } else { 95 } } else { if ($Human) { 90 } else { 65 } }
    if ($ArenaProbe) { $seconds = if ($game -eq 'HollowKnight') { 120 } else { 100 } }
    if ($HitProbe) { $seconds = if ($game -eq 'HollowKnight') { 200 } else { 180 } }
    if ($CrimsonProbe) { $seconds = if ($game -eq 'HollowKnight') { 140 } else { 120 } }
    if (-not $SequentialStartup) {
        # The background native game starts first and must outlive the host.
        $seconds = if ($game -eq 'NineSols') { $seconds + 20 } else { $seconds - 20 }
    }
    if ($HubProbe -and $Human) { $seconds = if ($game -eq 'HollowKnight') { 540 } else { 600 } }
    if ($HubCombat -and -not $Human) { $seconds = if ($game -eq 'HollowKnight') { 180 } else { 200 } }
    if ($VoidProbe) { $seconds = if ($game -eq "HollowKnight") { 110 } else { 130 } }
    if ($LeverProbe) { $seconds = if ($game -eq 'HollowKnight') { 70 } else { 90 } }
    if ($RestProbe) { $seconds = if ($game -eq 'HollowKnight') { 90 } else { 110 } }
    if ($HubProbe -and -not $HubCombat -and -not $Human) { $seconds = if ($game -eq 'HollowKnight') { 110 } else { 120 } }
    if ($DiagnosticSeconds -gt 0) { $seconds=if($game -eq 'NineSols'){$DiagnosticSeconds}else{$DiagnosticSeconds-20} }
    $arguments = @('-NoProfile','-File',('"' + (Join-Path $PSScriptRoot 'Start-Baseline.ps1') + '"'),'-Game',$game,'-Seconds',"$seconds",'-InputProbe','-SharedSession',$session,'-Boss',$Boss,'-Tier',$Tier)
    if (-not $Human) { $arguments += '-ScriptedInput' }
    if ($Unlimited) { $arguments += '-Unlimited' }
    if ($FailureBoundary) { $arguments += @('-FailureBoundary',$FailureBoundary) }
    if ($SwitchProbe) { $arguments += '-SwitchProbe' }
    if ($Pantheon) { $arguments += @('-Pantheon', $Pantheon, '-PantheonStart', $PantheonStart) }
    if ($PureVesselProbe) { $arguments += '-PureVesselProbe' }
    if ($PauseProbe) { $arguments += '-PauseProbe' }
    if ($ReflectionFinishProbe) { $arguments += '-ReflectionFinishProbe' }
    if ($PerformanceProbe) { $arguments += '-PerformanceProbe' }
    if ($CanvasBaseline) { $arguments += '-CanvasBaseline' }
    if ($CheatProbe) { $arguments += '-CheatProbe' }
    if ($MarkothProjectileProbe) { $arguments += '-MarkothProjectileProbe' }
    if ($HomingProbe) { $arguments += '-HomingProbe' }
    if ($StaggerProbe) { $arguments += '-StaggerProbe' }
    if ($MarkothShieldProbe) { $arguments += '-MarkothShieldProbe' }
    if ($ConfigurationProbe) { $arguments += '-ConfigurationProbe' }
    if ($TraceProbe) { $arguments += '-TraceProbe' }
    if ($HudReferenceProbe) { $arguments += '-HudReferenceProbe' }
    if ($InvulnerableProbe) { $arguments += '-InvulnerableProbe' }
    if ($KickProbe) { $arguments += '-KickProbe' }
    if ($TalismanTraversalProbe) { $arguments += '-TalismanTraversalProbe' }
    if ($VoidProbe) { $arguments += "-VoidProbe" }
    if ($WallProbe) { $arguments += '-WallProbe' }
    if ($TravelProbe) { $arguments += '-TravelProbe' }
    if ($LeverProbe) { $arguments += '-LeverProbe' }
    if ($RestProbe) { $arguments += '-RestProbe' }
    if ($VisualProbe) { $arguments += '-VisualProbe' }
    if ($EffectsProbe) { $arguments += '-EffectsProbe' }
    if ($CombatProbe) { $arguments += '-CombatProbe' }
    if ($CrimsonProbe) { $arguments += '-CrimsonProbe' }
    if ($ProxyProbe) { $arguments += '-ProxyProbe' }
    if ($ArenaProbe) { $arguments += '-ArenaProbe' }
    if ($HubProbe) { $arguments += '-HubProbe' }
if ($HubCombat) { $arguments += '-HubCombat' }
    if ($ContactBridge) { $arguments += '-ContactBridge' }
    if ($TargetProbe) { $arguments += '-TargetProbe' }
    if ($HitProbe) { $arguments += '-HitProbe' }
    if ($LifecycleProbe) { $arguments += '-LifecycleProbe' }
    if ($RetryProbe) { $arguments += '-RetryProbe' }
    if ($game -eq 'NineSols') {
        if ($SequentialStartup) { $arguments += @('-GeometrySnapshot', ('"' + (Join-Path $workspace ".local/runs/HollowKnight/$session/godhome-geometry.bin") + '"')) }
        else { $arguments += '-AwaitGeometry' }
    }
    $helpers += Start-Process -FilePath (Join-Path $PSHOME 'pwsh.exe') -ArgumentList $arguments -WindowStyle Hidden -PassThru -RedirectStandardOutput "$logRoot/$game.stdout.log" -RedirectStandardError "$logRoot/$game.stderr.log"
    if ($game -eq $launchOrder[0]) {
        # Steam can drop one of two simultaneous command requests. Acknowledge the
        # guarded process first; scene loading still overlaps with Nine Sols.
        $readyFile = if ($SequentialStartup) { 'plugin.log' } else { 'bootstrap.log' }
        $readyPattern = if ($SequentialStartup) { 'HK_GEOMETRY_EXPORTED' } else { "BOOTSTRAP_GUARD_VERIFIED session=$session" }
        $readyPath = Join-Path $workspace ".local/runs/$game/$session/$readyFile"
        $deadline = [DateTime]::UtcNow.AddSeconds(60)
        $ready = $false
        while ([DateTime]::UtcNow -lt $deadline) {
            if ($helpers[-1].HasExited) { throw "Protected $game startup helper exited; inspect $logRoot" }
            if (Test-Path -LiteralPath $readyPath) {
                $text = Get-Content -LiteralPath $readyPath -Raw
                if ($text -match 'HK_GODHOME_PROBE_FAILED') { throw "Godhome initialization failed; inspect $logRoot" }
                if ($text -match $readyPattern) { $ready = $true; break }
            }
            Start-Sleep -Milliseconds 200
        }
        if (-not $ready) { throw "Protected $game startup/export timed out; inspect $logRoot" }
    }
}
Write-Output "Protected input test session=$session. Concurrent startup=$(-not $SequentialStartup); native camera/target proxy=$ProxyProbe."
$deadline = [DateTime]::UtcNow.AddSeconds($(if ($HubProbe -and $Human) { 660 } elseif ($DiagnosticSeconds -gt 0) { $DiagnosticSeconds+60 } elseif ($HitProbe) { 250 } else { 160 }))
foreach ($helper in $helpers) {
    while (-not $helper.WaitForExit(1000)) {
        if (-not $Unlimited -and [DateTime]::UtcNow -gt $deadline) { throw "Launcher helper timed out; inspect $logRoot" }
    }
    if ($helper.ExitCode -ne 0) { throw "Input test failed; inspect $logRoot" }
}
if ($CheatProbe -and -not $InvulnerableProbe -and -not $FailureBoundary) { & "$PSScriptRoot/Test-BotDamageEvidence.ps1" -Session $session -RequireUnshieldedHealing }
if (-not $FailureBoundary) { & "$PSScriptRoot/Test-SessionFailureEvidence.ps1" -Session $session }
if ($TraceProbe) { & "$PSScriptRoot/Test-DetailedLoggingEvidence.ps1" -Session $session; return }
if ($StaggerProbe -and $Boss -in @('FalseKnight','FailedChampion')) { & "$PSScriptRoot/Test-ShockwaveStrikeEvidence.ps1" -Session $session; & "$PSScriptRoot/Test-ArmorPlatformEvidence.ps1" -Session $session; & "$PSScriptRoot/Test-RosterEvidence.ps1" -Session $session -Boss $Boss; return }
if ($CrimsonProbe -and $Boss -in @('FalseKnight','FailedChampion')) { & "$PSScriptRoot/Test-ShockwaveStrikeEvidence.ps1" -Session $session -CounterOnly; & "$PSScriptRoot/Test-RosterEvidence.ps1" -Session $session -Boss $Boss; return }
if ($Pantheon) { & "$PSScriptRoot/Test-PantheonEvidence.ps1" -Session $session -RequireHandoffs $(if($PantheonStart -in @(5,10,11)){0}else{1}) -RequireReturn:($PantheonStart -in @(5,10,11)) -RequireInterlude:($PantheonStart -in @(5,10)) -CompletedBeforeHostExit; return }
if ($PauseProbe) { & "$PSScriptRoot/Test-CombatPauseEvidence.ps1" -Session $session; return }
if ($TravelProbe) { if($WallProbe){ & "$PSScriptRoot/Test-GodhomeWallEvidence.ps1" -Session $session }else{ & "$PSScriptRoot/Test-TravelEvidence.ps1" -Session $session }; return }
if ($LeverProbe) { & "$PSScriptRoot/Test-LeverEvidence.ps1" -Session $session; return }
if ($VoidProbe) { & "$PSScriptRoot/Test-VoidEvidence.ps1" -Session $session; return }
if ($FailureBoundary) { & "$PSScriptRoot/Test-FailureEvidence.ps1" -Session $session -Boundary $FailureBoundary; return }
if ($SwitchProbe) { & "$PSScriptRoot/Test-SwitchEvidence.ps1" -Session $session; return }
if ($PerformanceProbe) { & "$PSScriptRoot/Measure-Session.ps1" -Session $session -FixedIdle; return }
if ($ReflectionFinishProbe) { & "$PSScriptRoot/Test-ReflectedFinishEvidence.ps1" -Session $session; return }
if ($PureVesselProbe) { & "$PSScriptRoot/Test-PureVesselTuningEvidence.ps1" -Session $session; return }
if ($MarkothProjectileProbe) { & "$PSScriptRoot/Test-ThunderVisualEvidence.ps1" -Session $session; & "$PSScriptRoot/Test-MarkothShieldEvidence.ps1" -Session $session -ProjectilesOnly; return }
if ($MarkothShieldProbe) { & "$PSScriptRoot/Test-MarkothShieldEvidence.ps1" -Session $session; return }
if ($TalismanTraversalProbe) { & "$PSScriptRoot/Test-TalismanTraversalEvidence.ps1" -Session $session; return }
if ($InvulnerableProbe) { & "$PSScriptRoot/Test-InvulnerableVictoryEvidence.ps1" -Session $session; return }
if ($CrimsonProbe -and $Boss -eq 'NailsageSly') { & "$PSScriptRoot/Test-SlyCounterEvidence.ps1" -Session $session; return }
if ($CrimsonProbe -and $Boss -in @('DungDefender','WhiteDefender')) { & "$PSScriptRoot/Test-AuthoredMoveEvidence.ps1" -Session $session -Boss $Boss; return }
if ($HubCombat -and $CheatProbe -and -not $Human -and $Boss -eq 'Gorb') { & "$PSScriptRoot/Test-GorbProjectileEvidence.ps1" -Session $session; & "$PSScriptRoot/Test-GorbVolleyEvidence.ps1" -Session $session; return }
if ($HubCombat -and -not $Human -and -not $KickProbe -and $Boss -in @('MantisLords','SistersOfBattle','Grimm','NightmareKingGrimm','NailsageSly','WatcherKnights','HornetProtector','HornetSentinel','BrokenVessel','LostKin','CrystalGuardian','EnragedGuardian','DungDefender','WhiteDefender','HiveKnight','TraitorLord','MassiveMossCharger','SoulWarrior','Nosk','WingedNosk','OroMato','Sheo','Oblobbles','GodTamer','Galien','Gorb','ElderHu','Markoth','Marmu','NoEyes','SoulMaster','SoulTyrant','Collector','GreyPrinceZote','Flukemarm','FalseKnight','FailedChampion','Uumuu','AbsoluteRadiance')) { & "$PSScriptRoot/Test-RosterEvidence.ps1" -Session $session -Boss $Boss; return }
if ($Tier -eq 2 -and $Boss -eq 'PureVessel' -and $CheatProbe -and -not $Human) { & "$PSScriptRoot/Test-RadiantEvidence.ps1" -Session $session; return }
if ($Boss -eq 'PureVessel' -and $CheatProbe -and -not $Human) { & "$PSScriptRoot/Test-PureVesselEvidence.ps1" -Session $session -Tier $Tier; & "$PSScriptRoot/Test-HudEvidence.ps1" -Session $session -HighResolution; & "$PSScriptRoot/Test-BossHudEvidence.ps1" -Session $session; return }
if ($RestProbe) { & "$PSScriptRoot/Test-RestEvidence.ps1" -Session $session; return }
if ($KickProbe) { if($Boss -eq "NightmareKingGrimm"){ & "$PSScriptRoot/Test-NkgMechanicsEvidence.ps1" -Session $session -RequireSlashCounter }elseif($Boss -eq "VengeflyKing"){ & "$PSScriptRoot/Test-KickEvidence.ps1" -Session $session }elseif($Boss -in @('HornetProtector','HornetSentinel')){ & "$PSScriptRoot/Test-HornetEvidence.ps1" -Session $session -Boss $Boss -RequireKick }elseif($Boss -in @('MantisLords','SistersOfBattle','WatcherKnights')){ & "$PSScriptRoot/Test-RosterKickEvidence.ps1" -Session $session -Boss $Boss }else{ & "$PSScriptRoot/Test-AuthoredMoveEvidence.ps1" -Session $session -Boss $Boss }; return }
if ($HubCombat) { if (-not $Human) { & "$PSScriptRoot/Test-HubCombatEvidence.ps1" -Session $session -RequireReentry; & "$PSScriptRoot/Test-NativeRoomEvidence.ps1" -Session $session; & "$PSScriptRoot/Test-HudEvidence.ps1" -Session $session -HighResolution; & "$PSScriptRoot/Test-BossHudEvidence.ps1" -Session $session -RequireInjury:$CheatProbe }; Write-Output "Hub combat diagnostic completed: $session. Physical feedback remains separate."; return }
if ($Human -and $HitProbe) { & "$PSScriptRoot/Test-HumanCombatEvidence.ps1" -Session $session; return }

if ($HubProbe -and -not $HubCombat) {
    if (-not $Human) {
        & "$PSScriptRoot/Test-HubEvidence.ps1" -Session $session -RequirePool -RequireStatue -RequireVoid
        & "$PSScriptRoot/Test-RecoveryEvidence.ps1" -Session $session -ExpectedFloor 13
        & "$PSScriptRoot/Test-HudEvidence.ps1" -Session $session -HighResolution
    }
    Write-Output "Hub diagnostic completed: $session. Physical interaction and animation feedback remain separate."
    return
}
if ($CrimsonProbe) { & "$PSScriptRoot/Test-CrimsonEvidence.ps1" -Session $session; return }
if ($RecoveryProbe) {
    & "$PSScriptRoot/Test-RecoveryEvidence.ps1" -Session $session
    & "$PSScriptRoot/Test-HudEvidence.ps1" -Session $session -HighResolution
    & "$PSScriptRoot/Test-ArenaEvidence.ps1" -Session $session -Boss $Boss -TimedPresentation
    & "$PSScriptRoot/Test-ContactEvidence.ps1" -Session $session -Boss $Boss
    return
}
if (-not $ContactBridge) { & "$PSScriptRoot/Test-InputEvidence.ps1" -Session $session -Human:$Human }
if (-not $Human -and -not $ContactBridge) { & "$PSScriptRoot/Test-ActionEvidence.ps1" -Session $session }
if ($VisualProbe) { & "$PSScriptRoot/Test-VisualEvidence.ps1" -Session $session; & "$PSScriptRoot/Test-HudEvidence.ps1" -Session $session }
if ($ProxyProbe -and -not $ArenaProbe) { & "$PSScriptRoot/Test-ProxyEvidence.ps1" -Session $session }
if ($ArenaProbe -and -not $RetryProbe) { & "$PSScriptRoot/Test-ArenaEvidence.ps1" -Session $session -Hits:$HitProbe -Boss $Boss }
if ($ContactBridge -and -not $LifecycleProbe) { & "$PSScriptRoot/Test-ContactEvidence.ps1" -Session $session -Boss $Boss }
if ($TargetProbe -and -not $HitProbe) { & "$PSScriptRoot/Test-TargetEvidence.ps1" -Session $session }
if ($HitProbe -and -not $LifecycleProbe) { & "$PSScriptRoot/Test-HitEvidence.ps1" -Session $session }
if ($LifecycleProbe) { & "$PSScriptRoot/Test-LifecycleEvidence.ps1" -Session $session }
if ($RetryProbe) { & "$PSScriptRoot/Test-RetryEvidence.ps1" -Session $session -Boss $Boss }
if ($HitProbe -and -not $Human) { & "$PSScriptRoot/Test-StartupEvidence.ps1" -Session $session }
if ($CombatProbe) {
    $nativeLog = Get-Content -LiteralPath "$workspace/.local/runs/NineSols/$session/plugin.log" -Raw
    if ($nativeLog.Contains('NS_TARGET_BODY_ISOLATED')) { throw 'Simultaneous combat incorrectly disabled incoming damage.' }
    Write-Output 'PASS: incoming native contact stayed enabled throughout the verified fight.'
}
