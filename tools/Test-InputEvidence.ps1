[CmdletBinding()]
param([Parameter(Mandatory)][Guid]$Session, [switch]$Human)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$logRoot = Join-Path $workspace ".local/input-tests/$Session"
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
if (-not ($ns -match 'NS_INPUT_READY')) { throw 'Native input adapter not installed.' }
if ($Human)
{
    foreach ($state in @('Roll','Parry','Attack')) {
        if (-not ($ns -match "NS_INPUT_NATIVE .*state=$state .*focused=False")) { throw "No background native $state response; human test remains unverified." }
    }
    foreach ($action in @('Jump','Dodge','Parry','Attack')) {
        if (-not ($ns -match "NS_INPUT_EDGE .*action=$action pressed=True") -or -not ($ns -match "NS_INPUT_EDGE .*action=$action pressed=False released=True")) { throw "Missing physical $action press/release." }
    }
    if ($ns -match 'NS_INPUT_FAILED|NS_MEMORY_FAILED|Input gap or receive overflow') { throw 'Native physical-input diagnostic failed.' }
    Write-Output "PASS: $matched exact pose matches and physical action press/releases with native Roll, Parry and Attack in the background. User confirmation is recorded separately. Evidence: $logRoot"
    return
}
if (-not ($hk -match 'HK_INPUT_SCRIPT_COMPLETE')) { throw 'HK input script did not complete.' }
if (-not ($ns -match 'NS_INPUT_RECOVERY_VERIFIED')) { throw 'Native input disable/re-enable transition unverified.' }
foreach ($action in @('Jump','Dodge','Parry','Attack')) {
    if (-not ($ns -match "NS_INPUT_EDGE .*action=$action pressed=True")) { throw "No native pressed edge for $action" }
    if (-not ($ns -match "NS_INPUT_EDGE .*action=$action pressed=False released=True")) { throw "No native release edge for $action" }
}
if ($ns -match 'NS_POSE_NATIVE_JUMP') { throw 'Direct jump diagnostic leaked into routed input test.' }
$samples = @($ns | ForEach-Object {
    if ($_ -match 'NS_INPUT_NATIVE id=(\d+) frame=\d+ state=(\w+) x=([-\d.E+]+) y=([-\d.E+]+) vx=([-\d.E+]+) vy=([-\d.E+]+) grounded=(True|False)') {
        [pscustomobject]@{ id=[long]$Matches[1]; state=$Matches[2]; x=[float]::Parse($Matches[3], [Globalization.CultureInfo]::InvariantCulture); y=[float]::Parse($Matches[4], [Globalization.CultureInfo]::InvariantCulture); vx=[float]::Parse($Matches[5], [Globalization.CultureInfo]::InvariantCulture); vy=[float]::Parse($Matches[6], [Globalization.CultureInfo]::InvariantCulture); grounded=$Matches[7] -eq 'True' }
    }
})
foreach ($state in @('Roll','Parry','Attack')) { if (-not ($samples | Where-Object state -eq $state)) { throw "Native state never reached: $state" } }
if (-not ($samples | Where-Object { $_.vx -lt -100 }) -or -not ($samples | Where-Object { $_.vx -gt 100 })) { throw 'Native walking did not accelerate in both directions.' }
$jumpIds = @($ns | ForEach-Object { if ($_ -match 'NS_INPUT_EDGE id=(\d+) action=Jump pressed=True') { [long]$Matches[1] } })
if ($jumpIds.Count -ne 2) { throw 'Expected ground jump and air jump input edges.' }
foreach ($jumpId in $jumpIds) { if (-not ($samples | Where-Object { $_.id -eq $jumpId -and $_.vy -gt 200 -and -not $_.grounded })) { throw "No native upward response to jump $jumpId" } }
$rollX = $samples | Where-Object state -eq 'Roll' | Measure-Object -Property x -Minimum -Maximum
if ($rollX.Maximum - $rollX.Minimum -lt 20) { throw 'Native roll did not move Yi.' }
$disconnected = [Array]::FindIndex([string[]]$hk, [Predicate[string]]{ param($line) $line.Contains('IPC_DISCONNECTED') })
if ($disconnected -lt 0) { throw 'Peer exit not recorded.' }
if ($hk | Select-Object -Skip ($disconnected + 1) | Where-Object { $_.Contains('IPC_SAMPLE') -or $_.Contains('IPC_POSE_RECEIVED') }) { throw 'Stale peer data after exit.' }
Write-Output "PASS: $matched exact pose matches, native bidirectional walk, two upward jump responses, moving Roll, Parry and Attack states, action release edges and peer-exit invalidation. Evidence: $logRoot"
