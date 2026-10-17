[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
if (Get-Process hollow_knight,NineSols -ErrorAction SilentlyContinue) { throw 'Close both games first.' }
$session = [Guid]::NewGuid().ToString()
$logRoot = Join-Path $workspace ".local/pose-tests/$session"
$null = New-Item -ItemType Directory -Path $logRoot -Force
$helpers = @()
foreach ($game in @('HollowKnight','NineSols')) {
    $seconds = if ($game -eq 'HollowKnight') { 95 } else { 50 }
    $arguments = @('-NoProfile','-File',('"' + (Join-Path $PSScriptRoot 'Start-Baseline.ps1') + '"'),'-Game',$game,'-Seconds',"$seconds",'-PoseProbe','-SharedSession',$session)
    if ($game -eq 'NineSols') { $arguments += @('-GeometrySnapshot', ('"' + (Join-Path $workspace ".local/runs/HollowKnight/$session/godhome-geometry.bin") + '"')) }
    $helpers += Start-Process -FilePath (Join-Path $PSHOME 'pwsh.exe') -ArgumentList $arguments -WindowStyle Hidden -PassThru -RedirectStandardOutput "$logRoot/$game.stdout.log" -RedirectStandardError "$logRoot/$game.stderr.log"
    if ($game -eq 'HollowKnight') {
        $readyPath = Join-Path $workspace ".local/runs/HollowKnight/$session/plugin.log"
        $deadline = [DateTime]::UtcNow.AddSeconds(60)
        $ready = $false
        while ([DateTime]::UtcNow -lt $deadline) {
            if (Test-Path -LiteralPath $readyPath) {
                $text = Get-Content -LiteralPath $readyPath -Raw
                if ($text -match 'HK_GODHOME_PROBE_FAILED') { throw "Godhome initialization failed; inspect $logRoot" }
                if ($text -match 'HK_GEOMETRY_EXPORTED') { $ready = $true; break }
            }
            Start-Sleep -Milliseconds 200
        }
        if (-not $ready) { throw "Protected geometry export timed out; inspect $logRoot" }
    }
}
Write-Output "Protected pose test session=$session. Frozen Godhome geometry and read-only native Yi pose."
$deadline = [DateTime]::UtcNow.AddSeconds(160)
foreach ($helper in $helpers) {
    while (-not $helper.WaitForExit(1000)) {
        if ([DateTime]::UtcNow -gt $deadline) { throw "Launcher helper timed out; inspect $logRoot" }
    }
    if ($helper.ExitCode -ne 0) { throw "Pose test failed; inspect $logRoot" }
}
$hk = Get-Content -LiteralPath "$workspace/.local/runs/HollowKnight/$session/plugin.log"
$ns = Get-Content -LiteralPath "$workspace/.local/runs/NineSols/$session/plugin.log"
$matched = 0
foreach ($line in $ns) {
    if ($line -match 'IPC_POSE_SENT (.*)$') {
        $expected = 'IPC_POSE_RECEIVED ' + $Matches[1]
        if ($hk | Where-Object { $_.EndsWith($expected, [StringComparison]::Ordinal) }) { $matched++ }
    }
}
if ($matched -lt 3) { throw "Insufficient exact native pose matches: $matched" }
if (-not ($ns -match 'NS_GEOMETRY_VERIFIED .*wallBlocked=True')) { throw 'Native terrain collision test did not pass.' }
$rangeLine = $hk | Where-Object { $_ -match 'HK_POSE_RANGE' } | Select-Object -Last 1
if ($rangeLine -notmatch 'minY=([-\d.E+]+) maxY=([-\d.E+]+)') { throw 'Native pose range missing.' }
$minY = [float]::Parse($Matches[1], [Globalization.CultureInfo]::InvariantCulture)
$maxY = [float]::Parse($Matches[2], [Globalization.CultureInfo]::InvariantCulture)
if ($maxY - $minY -lt 0.1 -or ($ns -match 'NS_POSE_NATIVE_JUMP').Count -ne 3) { throw 'Native movement range or scripted jump calls are missing.' }
$disconnected = [Array]::FindIndex([string[]]$hk, [Predicate[string]]{ param($line) $line.Contains('IPC_DISCONNECTED') })
if ($disconnected -lt 0) { throw 'Hollow Knight did not record peer exit.' }
if ($hk | Select-Object -Skip ($disconnected + 1) | Where-Object { $_.Contains('IPC_SAMPLE') -or $_.Contains('IPC_POSE_RECEIVED') }) { throw 'Stale peer pose after disconnect.' }
Write-Output "PASS: $matched exact native pose matches, received Y range $minY to $maxY, geometry landing/wall tests and peer-exit invalidation. Evidence: $logRoot"
