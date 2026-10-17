[CmdletBinding()]
param([switch]$CheckOnly, [ValidateSet('GruzMother','Xero','VengeflyKing','BroodingMawlek','PureVessel','MantisLords','SistersOfBattle','Grimm','NightmareKingGrimm','NailsageSly','WatcherKnights','HornetProtector','HornetSentinel','BrokenVessel','LostKin','CrystalGuardian','EnragedGuardian','DungDefender','WhiteDefender','HiveKnight','TraitorLord','MassiveMossCharger','SoulWarrior','Nosk','WingedNosk','OroMato','Sheo','Oblobbles','GodTamer','Galien','Gorb','ElderHu','Markoth','Marmu','NoEyes','SoulMaster','SoulTyrant','Collector','GreyPrinceZote','Flukemarm')][string]$Boss = 'GruzMother', [ValidateSet('Combat','Hub','HubCombat')][string]$Mode = 'Combat')
$ErrorActionPreference = 'Stop'
# Explorer's Run with PowerShell uses Windows PowerShell 5.1. Keep this
# entry point parseable there and hand off before invoking the PS7 tools.
if ($PSVersionTable.PSVersion.Major -lt 7) {
    $candidates = @(
        (Join-Path $env:ProgramFiles 'PowerShell\7\pwsh.exe'),
        (Join-Path $env:USERPROFILE '.cache\codex-runtimes\codex-primary-runtime\dependencies\native\powershell\pwsh.exe')
    )
    $command = Get-Command pwsh.exe -ErrorAction SilentlyContinue
    if ($command) { $candidates = @($command.Source) + $candidates }
    $runtime = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
    if (-not $runtime) {
        Write-Host 'PowerShell 7 was not found. Install PowerShell 7, then run this launcher again.' -ForegroundColor Red
        if (-not $CheckOnly) { $null = Read-Host 'Press Enter to close' }
        exit 1
    }
    $forward = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath, '-Boss', $Boss, '-Mode', $Mode)
    if ($CheckOnly) { $forward += '-CheckOnly' }
    & $runtime @forward
    $result = $LASTEXITCODE
    if (-not $CheckOnly) { $null = Read-Host 'Press Enter to close' }
    exit $result
}
$workspace = Split-Path $PSScriptRoot -Parent
$logDirectory = Join-Path $workspace '.local/launcher'
$null = New-Item -ItemType Directory -Path $logDirectory -Force
$logPath = Join-Path $logDirectory ((Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N') + '.log')
$null = Start-Transcript -LiteralPath $logPath
$temporaryNineSolsLoader = $false
try {
    Write-Host "Yi in Godhome launcher - PowerShell $($PSVersionTable.PSVersion)"
    Write-Host "Launcher log: $logPath"
    foreach ($script in @('Restore-NineSolsTestLoader.ps1', 'Uninstall-Baseline.ps1', 'Test-InputBridge.ps1')) {
        if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot $script))) { throw "Missing launcher component: $script" }
    }
    if (-not (Test-Path -LiteralPath (Join-Path $workspace '.local/deployments/NineSols.json'))) {
        & "$PSScriptRoot/Restore-NineSolsTestLoader.ps1" -CheckOnly:$CheckOnly
        if (-not $CheckOnly) { $temporaryNineSolsLoader = $true }
    }
    $testArguments = @{ Human = $true; Unlimited = $true }
    if ($Mode -eq 'HubCombat') {
        $testArguments.HubCombat = $true
        $testArguments.Boss = $Boss
        Write-Host 'Selected: Godhome hub and native statue selection (no time limit).'
        Write-Host 'Interact: E / B. Yi inventory: Tab / Select. Equipment changes last for this test session.'
    } elseif ($Mode -eq 'Hub') {
        $testArguments.HubProbe = $true
        Write-Host 'Selected: standalone Godhome traversal diagnostic (no time limit).'
    } else {
        $testArguments.HitProbe = $true
        $testArguments.EffectsProbe = $true
        $testArguments.Boss = $Boss
        Write-Host "Selected: $Boss combat prototype."
    }
    if ($CheckOnly) { Write-Host 'PASS: PowerShell 7 handoff and launcher components available. No games launched.'; return }
    Write-Host 'Starting Nine Sols, then Hollow Knight. Keep Hollow Knight focused.'
    Write-Host 'No time limit. Close both games when finished; keep this launcher open until then. To challenge again, use the statue.'
    & "$PSScriptRoot/Test-InputBridge.ps1" @testArguments
    Write-Host 'Controller test finished. Its telemetry is saved; player feedback is still required.'
} catch {
    Write-Host "Launcher failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace
    Write-Host "Details saved to: $logPath"
    exit 1
} finally {
    if ($temporaryNineSolsLoader) {
        try {
            & "$PSScriptRoot/Uninstall-Baseline.ps1" -Game NineSols
            Write-Host 'Nine Sols restored to vanilla launch mode.'
        } catch {
            Write-Host "Could not restore vanilla mode: $($_.Exception.Message). Close both games and run tools/Uninstall-Baseline.ps1 -Game NineSols." -ForegroundColor Red
        }
    }
    $null = Stop-Transcript
}
