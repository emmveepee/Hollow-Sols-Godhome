[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
if (Get-Process hollow_knight,NineSols -ErrorAction SilentlyContinue) { throw 'Close both games first.' }
$session = [Guid]::NewGuid().ToString()
$logRoot = Join-Path $workspace ".local/bridge-tests/$session"
$null = New-Item -ItemType Directory -Path $logRoot -Force
$helpers = @()
foreach ($game in @('HollowKnight','NineSols')) {
    $seconds = if ($game -eq 'HollowKnight') { 40 } else { 55 }
    # Hidden helper windows; each game remains an ordinary visible Steam application.
    $arguments = @('-NoProfile','-File',('"' + (Join-Path $PSScriptRoot 'Start-Baseline.ps1') + '"'),'-Game',$game,'-Seconds',"$seconds",'-Bridge','-SharedSession',$session)
    $helpers += Start-Process -FilePath (Join-Path $PSHOME 'pwsh.exe') -ArgumentList $arguments -WindowStyle Hidden -PassThru -RedirectStandardOutput "$logRoot/$game.stdout.log" -RedirectStandardError "$logRoot/$game.stderr.log"
    if ($game -eq 'HollowKnight') {
        # Steam can drop one of two simultaneous -applaunch requests. Wait for the first protected process.
        $readyPath = Join-Path $workspace ".local/runs/HollowKnight/$session/plugin.log"
        $deadline = [DateTime]::UtcNow.AddSeconds(20)
        while (-not (Test-Path -LiteralPath $readyPath) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 200 }
        if (-not (Test-Path -LiteralPath $readyPath)) { throw "Hollow Knight did not initialize; inspect $logRoot" }
    }
}
Write-Output "Protected dual-game test session=$session. No custom game arguments or save access."
$failures = @()
foreach ($helper in $helpers) {
    if (-not $helper.WaitForExit(120000)) { throw "Launcher helper timed out: $($helper.Id)" }
    if ($helper.ExitCode -ne 0) { $failures += $helper.Id }
}
if ($failures.Count -gt 0) { throw "Game test failed. Inspect $logRoot" }
$hk = Get-Content -LiteralPath "$workspace/.local/runs/HollowKnight/$session/plugin.log"
$ns = Get-Content -LiteralPath "$workspace/.local/runs/NineSols/$session/plugin.log"
function Verify-Direction($sentLines, $receivedLines, [string]$direction) {
    $matched = 0
    foreach ($line in $sentLines) {
        if ($line -match 'IPC_SENT sequence=(\d+) frame=(\d+) sentTick=(\d+)') {
            $expected = "IPC_RECEIVED sequence=$($Matches[1]) frame=$($Matches[2]) peerSentTick=$($Matches[3]) "
            if ($receivedLines | Where-Object { $_.Contains($expected) }) { $matched++ }
        }
    }
    if ($matched -lt 3) { throw "Insufficient matching values for $direction : $matched" }
    Write-Output "$direction : $matched exact sequence/frame/timestamp matches"
}
Verify-Direction $hk $ns 'HollowKnight -> NineSols'
Verify-Direction $ns $hk 'NineSols -> HollowKnight'
$disconnected = [Array]::FindIndex([string[]]$ns, [Predicate[string]]{ param($line) $line.Contains('IPC_DISCONNECTED') })
if ($disconnected -lt 0) { throw 'Nine Sols did not record peer exit.' }
if ($ns[($disconnected + 1)..($ns.Count - 1)] | Where-Object { $_.Contains('IPC_SAMPLE') }) { throw 'Stale peer sample logged after disconnect.' }
Write-Output "PASS: both directions, both title scenes, clean exits, peer-exit invalidation. Evidence: $logRoot"
